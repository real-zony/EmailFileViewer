//! Windows：启动前检查 WebView2 运行时（design.md D13）。
//!
//! 没有 WebView2 时 WebView 根本创建不出来，应用会无提示地闪退。所以创建窗口之前先查询运行时版本，
//! 查不到就弹一个原生消息框，说明需要先安装 WebView2 并给出微软的下载地址，然后退出。
//! 安装版同样受益：它能兜住“安装之后 WebView2 被卸载”的情况。
//!
//! 文案和选语言的逻辑是纯函数，在任何平台上都可以测试；只有真正查询和弹框的部分只在 Windows 上编译。

#![cfg_attr(not(windows), allow(dead_code))]

use crate::settings::{Language, resolve_language};

/// 微软官方的 WebView2 运行时下载页。
pub const DOWNLOAD_URL: &str = "https://developer.microsoft.com/microsoft-edge/webview2/";

/// 消息框的标题和正文。
#[derive(Debug, PartialEq, Eq)]
pub struct Message {
    pub title: &'static str,
    pub body: String,
}

/// 缺少 WebView2 时的提示文字。语言规则与首次启动时的界面语言一致：简体中文用中文，其余一律英文。
pub fn missing_runtime_message(language: Language) -> Message {
    let body = match language {
        Language::En => format!(
            "EmailFileViewer needs the Microsoft Edge WebView2 Runtime, which is not installed on this computer.\n\n\
             Download and install it from:\n{DOWNLOAD_URL}\n\n\
             Then start EmailFileViewer again.\n\n\
             (Press Ctrl+C to copy this message.)"
        ),
        Language::ZhCn => format!(
            "EmailFileViewer 需要 Microsoft Edge WebView2 运行时，但这台电脑上没有安装。\n\n\
             请先从下面的地址下载并安装：\n{DOWNLOAD_URL}\n\n\
             安装完成后再启动 EmailFileViewer。\n\n\
             （按 Ctrl+C 可以复制这段文字。）"
        ),
    };
    Message {
        title: "EmailFileViewer",
        body,
    }
}

/// 按系统语言选择提示文字。
pub fn missing_runtime_message_for_locale(locale: Option<&str>) -> Message {
    missing_runtime_message(resolve_language(locale))
}

/// 转成 Windows API 要的以 NUL 结尾的 UTF-16。
pub fn to_wide(text: &str) -> Vec<u16> {
    text.encode_utf16().chain(std::iter::once(0)).collect()
}

/// 查询 WebView2 运行时；没有就提示并退出进程。要在创建任何窗口之前调用。
#[cfg(windows)]
pub fn ensure_installed() {
    if tauri::webview_version().is_ok() {
        return;
    }
    let message = missing_runtime_message_for_locale(sys_locale::get_locale().as_deref());
    native::show_error(message.title, &message.body);
    std::process::exit(1);
}

#[cfg(windows)]
mod native {
    use std::ffi::c_void;

    use super::to_wide;

    const MB_OK: u32 = 0x0000;
    const MB_ICONERROR: u32 = 0x0010;
    const MB_SETFOREGROUND: u32 = 0x0001_0000;

    #[link(name = "user32")]
    unsafe extern "system" {
        fn MessageBoxW(
            window: *mut c_void,
            text: *const u16,
            caption: *const u16,
            kind: u32,
        ) -> i32;
    }

    /// 原生的错误消息框，等用户点“确定”。
    pub fn show_error(title: &str, text: &str) {
        let (title, text) = (to_wide(title), to_wide(text));
        // SAFETY: 两个字符串都是以 NUL 结尾的 UTF-16，在调用期间一直有效；窗口句柄为空表示没有父窗口。
        unsafe {
            MessageBoxW(
                std::ptr::null_mut(),
                text.as_ptr(),
                title.as_ptr(),
                MB_OK | MB_ICONERROR | MB_SETFOREGROUND,
            );
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn both_languages_name_webview2_and_give_the_download_address() {
        for language in [Language::En, Language::ZhCn] {
            let message = missing_runtime_message(language);
            assert_eq!(message.title, "EmailFileViewer");
            assert!(message.body.contains("WebView2"), "{language:?}");
            assert!(
                message
                    .body
                    .contains("https://developer.microsoft.com/microsoft-edge/webview2/"),
                "{language:?}"
            );
        }
    }

    #[test]
    fn the_language_follows_the_system_language_like_the_first_launch() {
        let text = |locale| missing_runtime_message_for_locale(locale).body;
        // 简体中文用中文
        for chinese in ["zh-CN", "zh-Hans-CN", "zh_CN.UTF-8", "zh"] {
            assert!(text(Some(chinese)).contains("需要"), "{chinese}");
        }
        // 其他一律英文，包括繁体中文、没有语言信息
        for english in ["en-US", "de-DE", "zh-TW", "zh-Hant", "ja-JP"] {
            assert!(
                text(Some(english)).contains("needs the Microsoft"),
                "{english}"
            );
        }
        assert!(text(None).contains("needs the Microsoft"));
    }

    #[test]
    fn the_message_is_encoded_for_the_windows_api() {
        assert_eq!(to_wide(""), [0]);
        assert_eq!(to_wide("ab"), [0x61, 0x62, 0]);
        // 非 BMP 的字符是一对代理项
        assert_eq!(to_wide("\u{1F600}"), [0xD83D, 0xDE00, 0]);
        let wide = to_wide(&missing_runtime_message(Language::ZhCn).body);
        assert_eq!(wide.last(), Some(&0));
        assert_eq!(wide.iter().filter(|&&unit| unit == 0).count(), 1);
    }
}
