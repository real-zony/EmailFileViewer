//! 前端可以调用的 Tauri 命令（design.md D11）。命令名、参数名、返回值和错误都以 D11 为准。
//!
//! 前端只认 Rust 分配的文档句柄，从不向 Rust 传入文件路径：打开文件的路径来自对话框、拖放、启动参数或单实例转发，
//! 保存附件的目标路径来自 Rust 弹出的对话框。错误统一序列化成 `{ code, message }`。

use serde::Serialize;
use tauri::{AppHandle, State, WebviewWindow, Wry};
use tauri_plugin_opener::OpenerExt;

use crate::dialogs;
use crate::opening::OpenCoordinator;
use crate::sidecar::{
    DocHandle, OpenedDocument, SaveAllResult, SidecarError, SidecarService, code,
};

type CommandResult<T> = Result<T, SidecarError>;

/// `save_attachment` 的返回值。
#[derive(Debug, Serialize, PartialEq, Eq)]
pub struct SavedPath {
    pub path: String,
}

#[derive(Debug, Serialize, PartialEq, Eq)]
#[serde(rename_all = "camelCase")]
pub struct SavedItem {
    pub attachment_id: String,
    pub path: String,
}

#[derive(Debug, Serialize, PartialEq, Eq)]
#[serde(rename_all = "camelCase")]
pub struct FailedItem {
    pub attachment_id: String,
    pub code: String,
}

/// `save_all_attachments` 的返回值：`{ saved: [{attachmentId, path}], failed: [{attachmentId, code}] }`。
#[derive(Debug, Serialize, PartialEq, Eq)]
pub struct SaveAllReport {
    pub saved: Vec<SavedItem>,
    pub failed: Vec<FailedItem>,
}

impl From<SaveAllResult> for SaveAllReport {
    fn from(result: SaveAllResult) -> Self {
        Self {
            saved: result
                .saved
                .into_iter()
                .map(|item| SavedItem {
                    attachment_id: item.attachment_id,
                    path: item.path.to_string_lossy().into_owned(),
                })
                .collect(),
            // 前端只需要错误码；sidecar 给开发者看的说明里可能带路径，不转发
            failed: result
                .failed
                .into_iter()
                .map(|item| FailedItem {
                    attachment_id: item.attachment_id,
                    code: item.code,
                })
                .collect(),
        }
    }
}

/// 前端注册好事件监听后调用。在此之前，启动参数和单实例转来的文件先排队。
#[tauri::command]
pub fn app_ready(coordinator: State<'_, OpenCoordinator>) {
    log::debug!("command app_ready");
    coordinator.app_ready();
}

/// 弹出多选对话框，结果通过事件返回。用户取消时不发任何事件。
#[tauri::command]
pub async fn open_with_dialog(
    app: AppHandle<Wry>,
    window: WebviewWindow<Wry>,
    coordinator: State<'_, OpenCoordinator>,
) -> CommandResult<()> {
    log::debug!("command open_with_dialog");
    open_via_dialog(&app, &window, &coordinator).await
}

/// `open_with_dialog` 的实际逻辑，原生菜单的“打开…”（⌘O）也走这里。
pub(crate) async fn open_via_dialog(
    app: &AppHandle<Wry>,
    window: &WebviewWindow<Wry>,
    coordinator: &OpenCoordinator,
) -> CommandResult<()> {
    if let Some(paths) = dialogs::pick_files(app, window).await? {
        coordinator.submit(paths);
    }
    Ok(())
}

/// 在当前标签页内逐层进入嵌套邮件。
#[tauri::command]
pub async fn open_nested(
    service: State<'_, SidecarService>,
    handle: DocHandle,
    attachment_id: String,
) -> CommandResult<OpenedDocument> {
    log::debug!("command open_nested");
    service.open_nested(handle, &attachment_id).await
}

#[tauri::command]
pub async fn close_document(
    service: State<'_, SidecarService>,
    handle: DocHandle,
) -> CommandResult<()> {
    log::debug!("command close_document");
    service.close_document(handle).await
}

/// 弹出保存对话框（默认文件名是清理后的附件名）并保存附件。用户取消时返回 `null`。
#[tauri::command]
pub async fn save_attachment(
    app: AppHandle<Wry>,
    window: WebviewWindow<Wry>,
    service: State<'_, SidecarService>,
    handle: DocHandle,
    attachment_id: String,
) -> CommandResult<Option<SavedPath>> {
    log::debug!("command save_attachment");
    // 先确认句柄和附件存在，再弹对话框
    let name = service.attachment_file_name(handle, &attachment_id)?;
    let default_name = dialogs::default_save_name(name.as_deref());
    let Some(target) = dialogs::save_file(&app, &window, &default_name).await? else {
        return Ok(None);
    };
    let path = service
        .save_attachment(handle, &attachment_id, &target)
        .await?;
    Ok(Some(SavedPath {
        path: path.to_string_lossy().into_owned(),
    }))
}

/// 弹出选择文件夹的对话框并保存全部附件。用户取消时返回 `null`。
#[tauri::command]
pub async fn save_all_attachments(
    app: AppHandle<Wry>,
    window: WebviewWindow<Wry>,
    service: State<'_, SidecarService>,
    handle: DocHandle,
) -> CommandResult<Option<SaveAllReport>> {
    log::debug!("command save_all_attachments");
    service.ensure_open(handle)?;
    let Some(folder) = dialogs::pick_folder(&app, &window).await? else {
        return Ok(None);
    };
    Ok(Some(
        service.save_all_attachments(handle, &folder).await?.into(),
    ))
}

/// 把附件提取到临时目录，用系统默认程序打开。可执行类型抛出 `executable_refused`。
#[tauri::command]
pub async fn open_attachment(
    app: AppHandle<Wry>,
    service: State<'_, SidecarService>,
    handle: DocHandle,
    attachment_id: String,
) -> CommandResult<()> {
    log::debug!("command open_attachment");
    let path = service.extract_for_open(handle, &attachment_id).await?;
    app.opener()
        .open_path(path.to_string_lossy(), None::<&str>)
        .map_err(|e| {
            SidecarError::remote(
                code::INTERNAL_ERROR,
                format!("the system could not open the attachment: {e}"),
            )
        })
}

#[cfg(test)]
mod tests {
    use serde_json::json;

    use super::*;
    use crate::sidecar::{FailedAttachment, SavedAttachment};

    #[test]
    fn save_all_report_has_exactly_the_fields_of_d11() {
        let report: SaveAllReport = SaveAllResult {
            saved: vec![SavedAttachment {
                attachment_id: "2".into(),
                path: "/Users/alice/Downloads/report (1).pdf".into(),
            }],
            failed: vec![FailedAttachment {
                attachment_id: "3".into(),
                code: "write_failed".into(),
                message: "cannot write /Users/alice/Downloads/x".into(),
            }],
        }
        .into();
        assert_eq!(
            serde_json::to_value(&report).unwrap(),
            json!({
                "saved": [{ "attachmentId": "2", "path": "/Users/alice/Downloads/report (1).pdf" }],
                "failed": [{ "attachmentId": "3", "code": "write_failed" }],
            })
        );
    }

    #[test]
    fn saved_path_serializes_as_a_path_object() {
        assert_eq!(
            serde_json::to_value(SavedPath {
                path: "/t/x".into()
            })
            .unwrap(),
            json!({ "path": "/t/x" })
        );
    }

    #[test]
    fn opened_documents_serialize_as_handle_and_document() {
        let opened = OpenedDocument {
            handle: DocHandle(4),
            document: json!({ "subject": "Hi" }),
        };
        assert_eq!(
            serde_json::to_value(&opened).unwrap(),
            json!({ "handle": 4, "document": { "subject": "Hi" } })
        );
    }

    #[test]
    fn errors_reach_the_frontend_as_code_and_message() {
        let error = SidecarError::remote(code::DIALOG_FAILED, "no dialog");
        assert_eq!(
            serde_json::to_value(&error).unwrap(),
            json!({ "code": "dialog_failed", "message": "no dialog" })
        );
    }

    /// design.md D11 的命令表。
    const D11_COMMANDS: [&str; 7] = [
        "app_ready",
        "open_with_dialog",
        "open_nested",
        "close_document",
        "save_attachment",
        "save_all_attachments",
        "open_attachment",
    ];

    #[test]
    fn every_d11_command_is_registered_in_all_three_places() {
        // 命令处理函数、build.rs 的应用清单、capabilities 里的权限必须一一对应：
        // 少了清单，命令不受权限控制；少了权限，前端调用会被拒绝
        let lib = include_str!("lib.rs");
        let build = include_str!("../build.rs");
        let capability: serde_json::Value =
            serde_json::from_str(include_str!("../capabilities/default.json")).unwrap();
        let permissions: Vec<&str> = capability["permissions"]
            .as_array()
            .unwrap()
            .iter()
            .map(|p| p.as_str().unwrap())
            .collect();
        for command in D11_COMMANDS {
            assert!(
                lib.contains(&format!("commands::{command},")),
                "{command} is not in generate_handler!"
            );
            assert!(
                build.contains(&format!("\"{command}\"")),
                "{command} is not in build.rs"
            );
            let permission = format!("allow-{}", command.replace('_', "-"));
            assert!(
                permissions.contains(&permission.as_str()),
                "{permission} is not granted in capabilities/default.json"
            );
        }
        let granted_commands = permissions.iter().filter(|p| !p.contains(':')).count();
        assert_eq!(
            granted_commands,
            D11_COMMANDS.len(),
            "capabilities grants a command that D11 does not define"
        );
    }
}
