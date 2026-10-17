//! 系统对话框：打开（多选）、保存、选择文件夹。路径只来自这些对话框，不接受前端传入的路径。

use std::path::PathBuf;

use tauri::{AppHandle, WebviewWindow, Wry};
use tauri_plugin_dialog::{DialogExt, FileDialogBuilder, FilePath};
use tokio::sync::oneshot;

use crate::settings::{InitialSettings, Language, SETTINGS_FILE, WindowColors, read_stored};
use crate::sidecar::{SidecarError, code};

fn dialog_failed(message: impl Into<String>) -> SidecarError {
    SidecarError::remote(code::DIALOG_FAILED, message)
}

/// 打开对话框的筛选器：邮件文件、所有文件。名称跟随界面语言。
pub fn open_filters(language: Language) -> [(&'static str, &'static [&'static str]); 2] {
    match language {
        Language::En => [
            ("Email files (*.eml, *.msg)", &["eml", "msg"]),
            ("All files", &["*"]),
        ],
        Language::ZhCn => [
            ("邮件文件 (*.eml, *.msg)", &["eml", "msg"]),
            ("所有文件", &["*"]),
        ],
    }
}

/// 当前的界面语言：设置文件（前端每次修改都会立即保存）里的值，没有保存过就按系统语言。
fn current_language(app: &AppHandle<Wry>) -> Language {
    let stored = tauri_plugin_store::resolve_store_path(app, SETTINGS_FILE)
        .ok()
        .map(|path| read_stored(&path))
        .unwrap_or_default();
    InitialSettings::resolve(
        &stored,
        sys_locale::get_locale().as_deref(),
        WindowColors::embedded(),
    )
    .language
}

/// 清理后名字为空时用的名字，与 Core 的 `AttachmentFileNames.FallbackName` 相同。
const FALLBACK_NAME: &str = "attachment";
/// 文件名按 UTF-8 编码后的最大字节数（主流文件系统的单个名字上限）。
const MAX_NAME_BYTES: usize = 255;
/// 扩展名超过这个字节数就不当扩展名保留了（截断过长的名字时用）。
const MAX_EXTENSION_BYTES: usize = 20;

/// Windows 的保留设备名（不区分大小写）。带扩展名也一样保留（`CON.txt`、`com1.log`），上标数字的写法同样被保留。
const RESERVED_NAMES: &[&str] = &[
    "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$", "COM0", "COM1", "COM2", "COM3", "COM4",
    "COM5", "COM6", "COM7", "COM8", "COM9", "COM¹", "COM²", "COM³", "LPT0", "LPT1", "LPT2", "LPT3",
    "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT¹", "LPT²", "LPT³",
];

/// 要直接去掉的字符：控制字符，以及能让文字显示顺序反转的双向控制符（U+202A 到 U+202E、U+2066 到 U+2069），
/// 它们能让 `invoice<U+202E>gpj.exe` 在界面上看起来像 `invoiceexe.jpg`。
fn is_removed(c: char) -> bool {
    c.is_control() || matches!(c, '\u{202A}'..='\u{202E}' | '\u{2066}'..='\u{2069}')
}

fn is_reserved_name(name: &str) -> bool {
    // 保留名看第一个点之前的部分，Windows 还会忽略它结尾的空格
    let stem = name.split('.').next().unwrap_or("").trim_end_matches(' ');
    RESERVED_NAMES
        .iter()
        .any(|reserved| reserved.eq_ignore_ascii_case(stem))
}

/// 把名字按字符截断到不超过 `max_bytes` 个 UTF-8 字节。
fn truncate_to_bytes(text: &str, max_bytes: usize) -> &str {
    let mut end = 0;
    for (index, c) in text.char_indices() {
        if index + c.len_utf8() > max_bytes {
            break;
        }
        end = index + c.len_utf8();
    }
    &text[..end]
}

/// 名字太长时截断，尽量保留扩展名。
fn limit_length(name: String) -> String {
    if name.len() <= MAX_NAME_BYTES {
        return name;
    }
    let (stem, extension) = match name.rfind('.') {
        Some(dot) if dot > 0 && name.len() - dot <= MAX_EXTENSION_BYTES => name.split_at(dot),
        _ => (name.as_str(), ""),
    };
    let stem = truncate_to_bytes(stem, MAX_NAME_BYTES - extension.len());
    format!("{}{extension}", stem.trim_end_matches(['.', ' ']))
}

/// 保存对话框的默认文件名。规则与 Core 的 `AttachmentFileNames.Sanitize` 完全一致：
/// 去掉路径成分（附件名来自邮件，可能带 `../` 或 `C:\`，`/` 和 `\` 都算分隔符，只留最后一段）；
/// 去掉控制字符和双向控制符；把 `<>:"/\|?*` 换成 `_`；去掉首尾空白和结尾的点；Windows 保留名前面加 `_`；
/// 按 UTF-8 不超过 255 字节截断（保留扩展名）；结果为空时用 `attachment`。
///
/// 这只是对话框里的默认值，用户可以改；真正写文件时 sidecar 不会再改动用户选的路径。
pub fn default_save_name(raw: Option<&str>) -> String {
    let raw = raw.unwrap_or("");
    let last = raw.rsplit(['/', '\\']).next().unwrap_or("");
    let cleaned: String = last
        .chars()
        .filter(|&c| !is_removed(c))
        .map(|c| if "<>:\"/\\|?*".contains(c) { '_' } else { c })
        .collect();
    let cleaned = cleaned.trim().trim_end_matches(['.', ' ']);
    if cleaned.is_empty() {
        return FALLBACK_NAME.to_owned();
    }
    let name = if is_reserved_name(cleaned) {
        format!("_{cleaned}")
    } else {
        cleaned.to_owned()
    };
    let name = limit_length(name);
    if name.is_empty() {
        FALLBACK_NAME.to_owned()
    } else {
        name
    }
}

fn to_path(path: FilePath) -> Result<PathBuf, SidecarError> {
    path.into_path().map_err(|e| {
        dialog_failed(format!(
            "the dialog returned something that is not a file path: {e}"
        ))
    })
}

async fn wait<T>(rx: oneshot::Receiver<T>) -> Result<T, SidecarError> {
    rx.await
        .map_err(|_| dialog_failed("the dialog closed without returning a result"))
}

fn builder(app: &AppHandle<Wry>, parent: &WebviewWindow<Wry>) -> FileDialogBuilder<Wry> {
    app.dialog().file().set_parent(parent)
}

/// 弹出“打开文件”对话框（多选）。取消返回 `None`。
pub async fn pick_files(
    app: &AppHandle<Wry>,
    parent: &WebviewWindow<Wry>,
) -> Result<Option<Vec<PathBuf>>, SidecarError> {
    let mut dialog = builder(app, parent);
    // macOS 的系统面板没有筛选器下拉框，rfd 会把所有筛选器的扩展名合并成“只允许这些类型”，
    // 而“*”并不是它认得的扩展名，加上反而可能让面板把所有文件都置灰。所以 macOS 不加筛选器，所有文件都可以选。
    if !cfg!(target_os = "macos") {
        for (name, extensions) in open_filters(current_language(app)) {
            dialog = dialog.add_filter(name, extensions);
        }
    }
    let (tx, rx) = oneshot::channel();
    dialog.pick_files(move |picked| {
        let _ = tx.send(picked);
    });
    match wait(rx).await? {
        None => Ok(None),
        Some(paths) => paths
            .into_iter()
            .map(to_path)
            .collect::<Result<Vec<_>, _>>()
            .map(Some),
    }
}

/// 弹出“保存文件”对话框，默认文件名是 `default_name`。取消返回 `None`。
pub async fn save_file(
    app: &AppHandle<Wry>,
    parent: &WebviewWindow<Wry>,
    default_name: &str,
) -> Result<Option<PathBuf>, SidecarError> {
    let (tx, rx) = oneshot::channel();
    builder(app, parent)
        .set_file_name(default_name)
        .save_file(move |picked| {
            let _ = tx.send(picked);
        });
    wait(rx).await?.map(to_path).transpose()
}

/// 弹出“选择文件夹”对话框。取消返回 `None`。
pub async fn pick_folder(
    app: &AppHandle<Wry>,
    parent: &WebviewWindow<Wry>,
) -> Result<Option<PathBuf>, SidecarError> {
    let (tx, rx) = oneshot::channel();
    builder(app, parent).pick_folder(move |picked| {
        let _ = tx.send(picked);
    });
    wait(rx).await?.map(to_path).transpose()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn open_filters_offer_email_files_and_all_files_in_the_ui_language() {
        let en = open_filters(Language::En);
        assert_eq!(en[0], ("Email files (*.eml, *.msg)", &["eml", "msg"][..]));
        assert_eq!(en[1].0, "All files");
        let zh = open_filters(Language::ZhCn);
        assert_eq!(zh[0], ("邮件文件 (*.eml, *.msg)", &["eml", "msg"][..]));
        assert_eq!(zh[1].0, "所有文件");
        assert_eq!(zh[1].1, ["*"]);
    }

    #[test]
    fn default_names_keep_ordinary_names_unchanged() {
        assert_eq!(default_save_name(Some("report.pdf")), "report.pdf");
        assert_eq!(
            default_save_name(Some("2026年第四季度对账单（华东区）.xlsx")),
            "2026年第四季度对账单（华东区）.xlsx"
        );
        assert_eq!(
            default_save_name(Some("name with spaces (1).docx")),
            "name with spaces (1).docx"
        );
        assert_eq!(default_save_name(Some(".hidden")), ".hidden");
    }

    #[test]
    fn default_names_drop_path_components_from_any_platform() {
        assert_eq!(default_save_name(Some("../../etc/passwd")), "passwd");
        assert_eq!(default_save_name(Some("../../.bashrc")), ".bashrc");
        assert_eq!(
            default_save_name(Some("..\\..\\Windows\\win.ini")),
            "win.ini"
        );
        assert_eq!(
            default_save_name(Some("C:\\Users\\alice\\evil.exe")),
            "evil.exe"
        );
        assert_eq!(default_save_name(Some("/abs/path/file.txt")), "file.txt");
        assert_eq!(default_save_name(Some("mixed/..\\sep.txt")), "sep.txt");
        assert_eq!(
            default_save_name(Some("dir/sub\\mixed/name.pdf")),
            "name.pdf"
        );
    }

    #[test]
    fn default_names_replace_characters_that_are_illegal_somewhere() {
        assert_eq!(
            default_save_name(Some("a<b>c:d\"e|f?g*h.txt")),
            "a_b_c_d_e_f_g_h.txt"
        );
        assert_eq!(default_save_name(Some("a:b*c?.txt")), "a_b_c_.txt");
        assert_eq!(default_save_name(Some("say \"hi\".txt")), "say _hi_.txt");
        assert_eq!(
            default_save_name(Some("stream.txt:evil.exe")),
            "stream.txt_evil.exe"
        );
    }

    #[test]
    fn default_names_remove_control_characters_like_core_does() {
        // 与 Core 一致：控制字符是直接去掉，不是换成下划线
        assert_eq!(default_save_name(Some("a\0b.txt")), "ab.txt");
        assert_eq!(default_save_name(Some("a\tb\r\nc.txt")), "abc.txt");
        assert_eq!(default_save_name(Some("a\u{7F}b.txt")), "ab.txt");
        assert_eq!(default_save_name(Some("a\u{85}b.txt")), "ab.txt");
        assert_eq!(default_save_name(Some("\0\u{1}")), "attachment");
    }

    #[test]
    fn default_names_remove_bidirectional_controls() {
        // U+202E 会让 "invoice\u{202E}gpj.exe" 在界面上显示成 "invoiceexe.jpg"
        assert_eq!(
            default_save_name(Some("invoice\u{202E}gpj.exe")),
            "invoicegpj.exe"
        );
        assert_eq!(
            default_save_name(Some("\u{2066}hidden\u{2069}.txt")),
            "hidden.txt"
        );
        for c in ('\u{202A}'..='\u{202E}').chain('\u{2066}'..='\u{2069}') {
            assert_eq!(
                default_save_name(Some(&format!("a{c}b.txt"))),
                "ab.txt",
                "U+{:04X}",
                c as u32
            );
        }
    }

    #[test]
    fn default_names_never_end_with_a_dot_or_space_and_are_never_empty() {
        assert_eq!(default_save_name(Some("name. . ")), "name");
        for (input, expected) in [
            ("name.", "name"),
            ("name. . .", "name"),
            ("name ", "name"),
            ("  name.txt  ", "name.txt"),
            ("name.txt.", "name.txt"),
        ] {
            assert_eq!(default_save_name(Some(input)), expected, "{input:?}");
        }
        for empty in [
            None,
            Some(""),
            Some(".."),
            Some("."),
            Some("..."),
            Some("   "),
            Some("../"),
            Some("dir/"),
            Some("a/b/"),
        ] {
            assert_eq!(default_save_name(empty), "attachment", "{empty:?}");
        }
    }

    #[test]
    fn default_names_prefix_windows_reserved_names() {
        for (input, expected) in [
            ("CON", "_CON"),
            ("con", "_con"),
            ("CON.txt", "_CON.txt"),
            ("PRN.log", "_PRN.log"),
            ("aux.tar.gz", "_aux.tar.gz"),
            ("NUL", "_NUL"),
            ("nul.", "_nul"),
            ("COM1", "_COM1"),
            ("com1.log", "_com1.log"),
            ("COM9.txt", "_COM9.txt"),
            ("LPT1.txt", "_LPT1.txt"),
            ("lpt9", "_lpt9"),
            ("CON .txt", "_CON .txt"),
            ("COM\u{B9}.txt", "_COM\u{B9}.txt"),
            ("LPT\u{B3}", "_LPT\u{B3}"),
            ("CONIN$", "_CONIN$"),
            ("CONOUT$.x", "_CONOUT$.x"),
        ] {
            assert_eq!(default_save_name(Some(input)), expected, "{input:?}");
        }
        // 只是名字里含有保留名的不改动
        for same in [
            "CONSOLE.txt",
            "COM10.txt",
            "COM.txt",
            "LPT.txt",
            "nullable.txt",
            "my.CON.txt",
            "a.con",
        ] {
            assert_eq!(default_save_name(Some(same)), same);
        }
    }

    #[test]
    fn default_names_are_limited_to_255_utf8_bytes_and_keep_the_extension() {
        let name = default_save_name(Some(&format!("{}.pdf", "a".repeat(400))));
        assert_eq!(name.len(), 255);
        assert!(name.ends_with(".pdf") && name.starts_with("aaaa"));

        // 中文按字节截断，不拆开字符
        let name = default_save_name(Some(&format!("{}.docx", "报告".repeat(200))));
        assert!(
            name.len() <= 255 && name.ends_with(".docx"),
            "{}",
            name.len()
        );
        assert!(!name.contains('\u{FFFD}'));
        let name = default_save_name(Some(&format!("{}.txt", "𠮷".repeat(200))));
        assert!(name.len() <= 255 && name.ends_with(".txt"));

        // 超长的扩展名不当扩展名保留，整体截断
        let name = default_save_name(Some(&format!("a.{}", "b".repeat(400))));
        assert_eq!(name.len(), 255);
        assert!(name.starts_with("a.bbb"));

        // 刚好 255 字节的不改动
        let exact = format!("{}.txt", "a".repeat(251));
        assert_eq!(default_save_name(Some(&exact)), exact);
    }
}
