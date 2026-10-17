//! 外部链接与导航策略（design.md D5）。
//!
//! 邮件正文里的链接都是 `target="_blank"`，点击时 WebView 会请求打开新窗口。这里拦截这些请求：
//! 只放行 `http`、`https`、`mailto`，交给系统默认程序；一律不在应用内创建窗口，应用自己的地址也不交给系统。
//!
//! 主窗口的导航分两个阶段：应用首次加载完成之前，放行应用自己的页面（加载它需要）；加载完成之后，
//! 只放行邮件正文 iframe 自己的地址（`about:srcdoc`、`about:blank`），开发模式下再加上 dev server 的整页刷新。
//! 邮件里的空链接、相对链接、SVG 的 `xlink:href` 会按父页面（应用首页）解析，WebView 没法区分主框架和子框架，
//! 放行应用首页的话邮件正文的 iframe 会被换成应用自己的页面，正文就消失了。所以加载完成之后，
//! 主框架想重新加载应用页面（生产环境里不会发生）也一并拒绝。

use std::sync::Arc;
use std::sync::atomic::{AtomicBool, Ordering};

use tauri::AppHandle;
use tauri::Wry;
use tauri::webview::NewWindowResponse;
use tauri_plugin_opener::OpenerExt;
use url::Url;

/// 可以交给系统默认程序打开的 scheme。
const EXTERNAL_SCHEMES: [&str; 3] = ["http", "https", "mailto"];

/// 应用是否已经完成首次加载。克隆出来的副本共享同一个状态。
#[derive(Clone, Default)]
pub struct AppLoaded(Arc<AtomicBool>);

impl AppLoaded {
    /// 首次加载完成（页面加载事件 `Finished`）。
    pub fn mark(&self) {
        self.0.store(true, Ordering::SeqCst);
    }

    pub fn is_loaded(&self) -> bool {
        self.0.load(Ordering::SeqCst)
    }
}

/// 是否允许交给系统默认程序打开。
pub fn is_external_link(url: &Url) -> bool {
    EXTERNAL_SCHEMES.contains(&url.scheme())
}

/// 是不是应用自己的地址：`tauri://`、`ipc:`、`asset:`，Windows 上 `http(s)://tauri.localhost` 之类的主机名，
/// 开发时还有 dev server。这些地址不管能不能加载，都不应该交给系统浏览器。
pub fn is_own_address(url: &Url, dev_url: Option<&Url>) -> bool {
    match url.scheme() {
        "tauri" | "ipc" | "asset" => true,
        "http" | "https" => {
            matches!(
                url.host_str(),
                Some("tauri.localhost" | "ipc.localhost" | "asset.localhost")
            ) || dev_url.is_some_and(|dev| dev.origin() == url.origin())
        }
        _ => false,
    }
}

/// 要不要交给系统默认程序：外部链接，并且不是应用自己的地址。
/// （Windows 上邮件里的相对链接会被解析成 `http://tauri.localhost/foo.html`，它是 http，但不是外部链接。）
pub fn should_open_externally(url: &Url, dev_url: Option<&Url>) -> bool {
    is_external_link(url) && !is_own_address(url, dev_url)
}

/// 邮件正文 iframe 自己的地址：`about:blank` 和 `about:srcdoc`。
///
/// `about:srcdoc#锚点` 也算：邮件正文 iframe 里指向 `about:srcdoc#section` 的页内锚点链接是文档内的滚动，不会加载任何东西。
/// WebView 会为它调用导航回调（macOS 的 WKWebView 实测会，并且把 `#` 写成 `%23`：`about:srcdoc%23section`，一并认得），
/// 不应该因为回调拒绝而影响滚动。带查询串的（`about:srcdoc?x`）不是页内锚点，不算。
pub fn is_message_frame_url(url: &Url) -> bool {
    let path = url.path();
    url.scheme() == "about"
        && url.query().is_none()
        && (matches!(path, "blank" | "srcdoc") || path.starts_with("srcdoc%23"))
}

/// 是不是应用自己的页面：打包后是 `tauri://localhost`（macOS、Linux）或 `http(s)://tauri.localhost`（Windows），
/// 开发时是 dev server；邮件正文 iframe 的地址（见 [`is_message_frame_url`]）也属于应用。
/// 这里只回答“是不是应用的页面”；要不要放行导航见 [`is_navigation_allowed`]。
pub fn is_app_url(url: &Url, dev_url: Option<&Url>) -> bool {
    match url.scheme() {
        "tauri" => url.host_str() == Some("localhost"),
        "http" | "https" => {
            url.host_str() == Some("tauri.localhost")
                || dev_url.is_some_and(|dev| dev.origin() == url.origin())
        }
        "about" => is_message_frame_url(url),
        _ => false,
    }
}

/// dev server 的整页刷新（例如 Vite 的热更新在改动无法热替换时会整页刷新）：开发模式下 dev server 的页面，不带 fragment。
fn is_dev_server_reload(url: &Url, dev_url: Option<&Url>) -> bool {
    dev_url.is_some_and(|dev| {
        matches!(url.scheme(), "http" | "https")
            && dev.origin() == url.origin()
            && url.fragment().is_none()
    })
}

/// 主窗口里的导航（包括邮件正文 iframe 里的）要不要放行。
///
/// - 应用首次加载完成之前（`app_loaded` 为 false）：应用自己的页面，带 fragment 的除外。
/// - 加载完成之后：只放行邮件正文 iframe 自己的地址（`about:srcdoc` 及其锚点、`about:blank`），
///   开发模式下再加上 dev server 的整页刷新；其余全部拒绝，包括应用首页 `tauri://localhost/`。
///
/// 邮件正文是 `srcdoc` iframe，文档地址是 `about:srcdoc`，而相对地址按父页面解析：里面的 `<a href="#x">`、
/// `<a href="">`、`<a href="foo.html">` 和 SVG 的 `xlink:href` 导航到的是 `tauri://localhost#x`、`tauri://localhost/`、
/// `tauri://localhost/foo.html`（Windows 上是 `http://tauri.localhost/…`）。macOS 上实测放行它们，iframe 会被换成
/// 应用自己的页面，邮件正文整个消失。应用加载完成之后从不导航到自己的页面，所以一律拒绝：
/// 这样这类链接至少不会毁掉正文。（要让页内锚点真的滚动，链接得指向 `about:srcdoc#x`，由 Core 的清理器改写。）
pub fn is_navigation_allowed(url: &Url, dev_url: Option<&Url>, app_loaded: bool) -> bool {
    if app_loaded {
        is_message_frame_url(url) || is_dev_server_reload(url, dev_url)
    } else {
        is_app_url(url, dev_url) && (url.scheme() == "about" || url.fragment().is_none())
    }
}

/// dev server 的地址，只在开发模式下有值：配置里的 `devUrl` 打包后依然存在，但那时 `localhost:1420` 就是一个普通地址。
fn dev_server_url(app: &AppHandle<Wry>) -> Option<Url> {
    if tauri::is_dev() {
        app.config().build.dev_url.clone()
    } else {
        None
    }
}

/// 用系统默认程序打开。失败只记日志：用户点了一个链接，没有更好的补救办法。
pub fn open_external(app: &AppHandle<Wry>, url: &Url) {
    // 日志里只记 scheme，不记地址：链接的查询参数里可能有个人信息
    match app.opener().open_url(url.as_str(), None::<&str>) {
        Ok(()) => log::debug!(
            "opened a {} link with the system default program",
            url.scheme()
        ),
        Err(error) => log::warn!("failed to open a {} link: {error}", url.scheme()),
    }
}

/// 新窗口请求：外部链接交给系统，应用内一律不创建窗口；应用自己的地址（相对链接解析出来的）既不创建窗口也不交给系统。
pub fn on_new_window(app: &AppHandle<Wry>, url: &Url) -> NewWindowResponse<Wry> {
    let dev_url = dev_server_url(app);
    if should_open_externally(url, dev_url.as_ref()) {
        open_external(app, url);
    } else {
        log::debug!("blocked a request for a new window ({} URL)", url.scheme());
    }
    NewWindowResponse::Deny
}

/// 主窗口的导航：放行的见 [`is_navigation_allowed`]；其他地址不在应用内加载，外部链接交给系统。
pub fn on_navigation(app: &AppHandle<Wry>, url: &Url, loaded: &AppLoaded) -> bool {
    let dev_url = dev_server_url(app);
    if is_navigation_allowed(url, dev_url.as_ref(), loaded.is_loaded()) {
        return true;
    }
    if should_open_externally(url, dev_url.as_ref()) {
        open_external(app, url);
    } else {
        log::debug!("blocked a navigation ({} URL)", url.scheme());
    }
    false
}

#[cfg(test)]
mod tests {
    use super::*;

    fn url(text: &str) -> Url {
        Url::parse(text).unwrap()
    }

    #[test]
    fn only_http_https_and_mailto_are_handed_to_the_system() {
        for ok in [
            "http://example.com/a?b=1",
            "https://example.com",
            "mailto:alice@example.com?subject=Hi",
            "HTTPS://EXAMPLE.COM",
        ] {
            assert!(is_external_link(&url(ok)), "{ok}");
        }
        for blocked in [
            "file:///etc/passwd",
            "javascript:alert(1)",
            "data:text/html,<script>alert(1)</script>",
            "tel:+1555",
            "ftp://example.com/x",
            "ssh://host",
            "x-apple.systempreferences:com.apple.preference",
            "tauri://localhost/index.html",
            "ipc://localhost/cmd",
            "blob:https://example.com/uuid",
            "about:blank",
            "vbscript:x",
            "smb://server/share",
            "tmp:///x",
        ] {
            assert!(!is_external_link(&url(blocked)), "{blocked}");
        }
    }

    #[test]
    fn the_apps_own_pages_are_recognized() {
        let dev = url("http://localhost:1420/");
        for app in [
            "tauri://localhost/",
            "tauri://localhost/index.html",
            "http://tauri.localhost/",
            "https://tauri.localhost/index.html",
            "about:blank",
            "about:srcdoc",
            "http://localhost:1420/?showcase",
        ] {
            assert!(is_app_url(&url(app), Some(&dev)), "{app}");
        }
    }

    #[test]
    fn in_page_anchors_of_the_message_frame_are_allowed_before_and_after_the_first_load() {
        // 邮件正文 iframe 里的 <a href="about:srcdoc#section"> 导航到 about:srcdoc#section（macOS 上会经过导航回调）
        for anchor in [
            "about:srcdoc#section",
            "about:srcdoc#",
            "about:srcdoc#top",
            "about:srcdoc#%E7%AC%AC%E4%B8%80%E7%AB%A0",
            "about:srcdoc#a/b?c",
            "about:blank#x",
            // macOS 的 WKWebView 报告的写法
            "about:srcdoc%23section",
            "about:srcdoc%23",
        ] {
            assert!(is_app_url(&url(anchor), None), "{anchor}");
            assert!(is_message_frame_url(&url(anchor)), "{anchor}");
            for loaded in [false, true] {
                assert!(
                    is_navigation_allowed(&url(anchor), None, loaded),
                    "{anchor} (loaded: {loaded})"
                );
            }
        }
    }

    #[test]
    fn before_the_first_load_the_app_itself_may_load_but_not_with_a_fragment() {
        let dev = url("http://localhost:1420/");
        for allowed in [
            "tauri://localhost/",
            "tauri://localhost",
            "tauri://localhost/index.html",
            "http://tauri.localhost/",
            "https://tauri.localhost/index.html",
            "http://localhost:1420/?showcase",
            "about:srcdoc",
            "about:blank",
        ] {
            assert!(
                is_navigation_allowed(&url(allowed), Some(&dev), false),
                "{allowed}"
            );
        }
        for blocked in [
            "tauri://localhost#x",
            "tauri://localhost/#",
            "tauri://localhost/index.html#section",
            "http://tauri.localhost/#x",
            "https://tauri.localhost/#x",
            "http://localhost:1420/#x",
            "https://example.com/",
            "tauri://evil/",
        ] {
            assert!(
                !is_navigation_allowed(&url(blocked), Some(&dev), false),
                "{blocked}"
            );
        }
    }

    #[test]
    fn after_the_first_load_only_the_message_frame_is_navigable() {
        // 邮件里的空链接、相对链接、SVG 的 xlink:href 按父页面（应用首页）解析，放行会把 iframe 换成应用首页（macOS 实测）
        let dev = url("http://localhost:1420/");
        for blocked in [
            // <a href="">、<a href="foo.html">、<a href="#x">、xlink:href 解析出来的地址
            "tauri://localhost/",
            "tauri://localhost",
            "tauri://localhost/index.html",
            "tauri://localhost/foo.html",
            "tauri://localhost/?x=1",
            "tauri://localhost#x",
            "http://tauri.localhost/",
            "https://tauri.localhost/foo.html",
            "http://tauri.localhost/#x",
            "ipc://localhost/app_ready",
            "http://ipc.localhost/app_ready",
            "asset://localhost/x",
            "http://asset.localhost/x",
            // 其余的从来就不是应用的页面
            "https://example.com/",
            "mailto:a@b.c",
            "file:///index.html",
            "data:text/html,hi",
            "javascript:1",
            "about:config",
            "about:srcdoc?x=1",
        ] {
            for dev_url in [None, Some(&dev)] {
                assert!(
                    !is_navigation_allowed(&url(blocked), dev_url, true),
                    "{blocked} (dev: {})",
                    dev_url.is_some()
                );
            }
        }
        for allowed in ["about:srcdoc", "about:blank", "about:srcdoc#x"] {
            assert!(
                is_navigation_allowed(&url(allowed), None, true),
                "{allowed}"
            );
        }
    }

    #[test]
    fn in_development_the_dev_server_may_reload_the_whole_page() {
        // Vite 在无法热替换时整页刷新：导航到 dev server 的页面。只在开发模式（有 dev_url）下放行
        let dev = url("http://localhost:1420/");
        for reload in [
            "http://localhost:1420/",
            "http://localhost:1420/?showcase",
            "http://localhost:1420/index.html",
        ] {
            assert!(
                is_navigation_allowed(&url(reload), Some(&dev), true),
                "{reload}"
            );
            assert!(
                !is_navigation_allowed(&url(reload), None, true),
                "{reload} in a packaged app"
            );
        }
        for other in [
            "http://localhost:9999/",
            "https://localhost:1420/",
            "http://localhost:1420/#x",
            "http://127.0.0.1:1420/",
        ] {
            assert!(
                !is_navigation_allowed(&url(other), Some(&dev), true),
                "{other}"
            );
        }
    }

    #[test]
    fn the_apps_own_addresses_are_never_handed_to_the_system_browser() {
        let dev = url("http://localhost:1420/");
        // Windows 上邮件里的相对链接会被解析成 http://tauri.localhost/foo.html
        for own in [
            "http://tauri.localhost/foo.html",
            "https://tauri.localhost/",
            "http://ipc.localhost/cmd",
            "http://asset.localhost/x",
            "tauri://localhost/foo.html",
            "ipc://localhost/cmd",
            "asset://localhost/x",
        ] {
            assert!(is_own_address(&url(own), Some(&dev)), "{own}");
            assert!(!should_open_externally(&url(own), Some(&dev)), "{own}");
            assert!(!should_open_externally(&url(own), None), "{own}");
        }
        assert!(is_own_address(&url("http://localhost:1420/x"), Some(&dev)));
        assert!(!should_open_externally(
            &url("http://localhost:1420/x"),
            Some(&dev)
        ));
        // 打包后 localhost:1420 就是一个普通的外部地址
        assert!(should_open_externally(
            &url("http://localhost:1420/x"),
            None
        ));
        for external in [
            "https://example.com/",
            "http://tauri.localhost.evil.com/",
            "mailto:a@b.c",
        ] {
            assert!(
                should_open_externally(&url(external), Some(&dev)),
                "{external}"
            );
        }
        // 不是外部链接的 scheme 当然也不交给系统
        assert!(!should_open_externally(&url("file:///etc/passwd"), None));
    }

    #[test]
    fn the_first_load_flag_is_shared_between_clones() {
        let loaded = AppLoaded::default();
        let copy = loaded.clone();
        assert!(!loaded.is_loaded());
        copy.mark();
        assert!(loaded.is_loaded());
    }

    #[test]
    fn other_about_urls_stay_blocked_even_with_an_anchor() {
        for blocked in [
            "about:config",
            "about:config#srcdoc",
            "about:srcdoc?x=1",
            "about:srcdoc?x=1#y",
            "about:srcdoc/extra",
            "about:srcdoc2",
            "about:blank?x",
            "about:",
            "about:Srcdoc2#x",
            "about:srcdoc%2Fx",
            "about:srcdoc%23x?y",
            "about:blank%23x",
        ] {
            assert!(!is_app_url(&url(blocked), None), "{blocked}");
        }
    }

    #[test]
    fn other_origins_are_not_the_app() {
        let dev = url("http://localhost:1420/");
        for other in [
            "https://example.com/",
            "http://localhost:9999/",
            "https://localhost:1420/",
            "http://tauri.localhost.evil.com/",
            "tauri://evil/",
            "file:///index.html",
            "about:config",
            "data:text/html,hi",
            "javascript:1",
        ] {
            assert!(!is_app_url(&url(other), Some(&dev)), "{other}");
        }
        // 打包后没有 dev server，localhost 上别的端口也不是应用
        assert!(!is_app_url(&url("http://localhost:1420/"), None));
    }

    #[test]
    fn external_http_links_are_never_treated_as_the_app() {
        // 放行外部链接和判断是不是应用页面互不重叠：同一个地址不会既是“应用”又是“外部”（除了开发服务器本身，它不会出现在邮件里）
        for u in ["https://example.com/", "mailto:a@b.c"] {
            assert!(is_external_link(&url(u)) && !is_app_url(&url(u), None));
        }
    }
}
