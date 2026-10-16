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

/// 保存对话框的默认文件名：去掉路径成分（附件名来自邮件，可能带 `../` 或 `C:\`）和各平台的非法字符。
/// 这只是对话框里的默认值，用户可以改；真正写文件时 sidecar 不会再改动用户选的路径。
pub fn default_save_name(raw: Option<&str>) -> String {
    let last = raw.unwrap_or("").rsplit(['/', '\\']).next().unwrap_or("");
    let cleaned: String = last
        .chars()
        .map(|c| {
            if c.is_control() || "<>:\"|?*".contains(c) {
                '_'
            } else {
                c
            }
        })
        .collect();
    let cleaned = cleaned.trim_end_matches([' ', '.']);
    if cleaned.is_empty() {
        "attachment".to_owned()
    } else {
        cleaned.to_owned()
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
    }

    #[test]
    fn default_names_replace_characters_that_are_illegal_somewhere() {
        assert_eq!(
            default_save_name(Some("a<b>c:d\"e|f?g*h.txt")),
            "a_b_c_d_e_f_g_h.txt"
        );
        assert_eq!(
            default_save_name(Some("tab\there\nnewline.txt")),
            "tab_here_newline.txt"
        );
        assert_eq!(default_save_name(Some("nul\0byte.txt")), "nul_byte.txt");
    }

    #[test]
    fn default_names_never_end_with_a_dot_or_space_and_are_never_empty() {
        assert_eq!(default_save_name(Some("name. . ")), "name");
        for empty in [
            None,
            Some(""),
            Some(".."),
            Some("."),
            Some("   "),
            Some("../"),
            Some("dir/"),
        ] {
            assert_eq!(default_save_name(empty), "attachment", "{empty:?}");
        }
    }
}
