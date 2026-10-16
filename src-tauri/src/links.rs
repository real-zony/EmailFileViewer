//! 外部链接与导航策略（design.md D5）。
//!
//! 邮件正文里的链接都是 `target="_blank"`，点击时 WebView 会请求打开新窗口。这里拦截这些请求：
//! 只放行 `http`、`https`、`mailto`，交给系统默认程序；一律不在应用内创建窗口。
//! 主窗口自己的导航也只允许应用自己的页面，其他地址不会在应用内加载。

use tauri::AppHandle;
use tauri::Wry;
use tauri::webview::NewWindowResponse;
use tauri_plugin_opener::OpenerExt;
use url::Url;

/// 可以交给系统默认程序打开的 scheme。
const EXTERNAL_SCHEMES: [&str; 3] = ["http", "https", "mailto"];

/// 是否允许交给系统默认程序打开。
pub fn is_external_link(url: &Url) -> bool {
    EXTERNAL_SCHEMES.contains(&url.scheme())
}

/// 是不是应用自己的页面：打包后是 `tauri://localhost`（macOS、Linux）或 `http(s)://tauri.localhost`（Windows），
/// 开发时是 dev server；`about:blank` 和 `about:srcdoc`（邮件正文的 iframe）也属于应用。
pub fn is_app_url(url: &Url, dev_url: Option<&Url>) -> bool {
    match url.scheme() {
        "tauri" => url.host_str() == Some("localhost"),
        "http" | "https" => {
            url.host_str() == Some("tauri.localhost")
                || dev_url.is_some_and(|dev| dev.origin() == url.origin())
        }
        "about" => matches!(url.as_str(), "about:blank" | "about:srcdoc"),
        _ => false,
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

/// 新窗口请求：放行的链接交给系统，应用内一律不创建窗口。
pub fn on_new_window(app: &AppHandle<Wry>, url: &Url) -> NewWindowResponse<Wry> {
    if is_external_link(url) {
        open_external(app, url);
    } else {
        log::debug!("blocked a request for a new window ({} URL)", url.scheme());
    }
    NewWindowResponse::Deny
}

/// 主窗口的导航：应用自己的页面放行；其他地址不在应用内加载，放行的 scheme 交给系统。
pub fn on_navigation(app: &AppHandle<Wry>, url: &Url) -> bool {
    let dev_url = app.config().build.dev_url.as_ref();
    if is_app_url(url, dev_url) {
        return true;
    }
    if is_external_link(url) {
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
