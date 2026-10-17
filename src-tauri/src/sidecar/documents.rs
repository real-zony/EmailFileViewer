//! 文档句柄：Rust 发给前端的稳定标识，以及重新打开文档所需的来源。
//!
//! sidecar 的 `docId` 只在它那一代进程内有效。每个句柄记录它属于哪一代 sidecar、对应的 `docId`，
//! 以及来源：顶层文件的路径，或“父文档 + 附件 Id”。sidecar 重启后，旧代次的句柄在下次使用时按来源重新打开。

use std::collections::HashMap;
use std::path::PathBuf;
use std::sync::Arc;
use std::sync::atomic::{AtomicBool, AtomicUsize};

use serde_json::Value;
use tokio::sync::Mutex;

use super::protocol::DocHandle;

/// 文档从哪里来，重新打开时照着做。
pub(crate) enum Source {
    /// 用户选的（或拖入的、系统传来的）邮件文件。
    File(PathBuf),
    /// 另一个文档里的嵌套邮件附件。持有父文档，所以父句柄被关闭后，只要子文档还在，父文档就还能被重新打开。
    Nested {
        parent: Arc<DocEntry>,
        attachment_id: String,
    },
}

/// 文档当前在哪一代 sidecar 里，以及它在那里的 `docId`。
pub(crate) struct Residence {
    pub generation: u64,
    pub doc_id: String,
}

pub(crate) struct DocEntry {
    pub handle: DocHandle,
    pub source: Source,
    /// 附件 Id -> 文件名（文档里没有文件名的为 `None`）。保存对话框的默认文件名要用，Rust 不向 sidecar 再查一次。
    pub attachment_names: HashMap<String, Option<String>>,
    /// 加锁期间不会有第二个请求同时重新打开同一个文档。
    pub residence: Mutex<Residence>,
    /// 还没有关闭的子文档（嵌套邮件）个数。
    pub open_children: AtomicUsize,
    /// 前端已经关闭了这个句柄。还有子文档时，sidecar 里的文档先保留，等最后一个子文档关闭再真正关闭。
    pub closed_by_user: AtomicBool,
    /// 已经向 sidecar 发出过关闭（或不需要发），不再重复处理。
    pub finalized: AtomicBool,
}

impl DocEntry {
    pub(crate) fn new(
        handle: DocHandle,
        source: Source,
        generation: u64,
        doc_id: String,
        document: &Value,
    ) -> Arc<Self> {
        Arc::new(Self {
            handle,
            source,
            attachment_names: attachment_names(document),
            residence: Mutex::new(Residence { generation, doc_id }),
            open_children: AtomicUsize::new(0),
            closed_by_user: AtomicBool::new(false),
            finalized: AtomicBool::new(false),
        })
    }
}

/// 从 sidecar 返回的 `document.attachments` 里取出 Id 和文件名。
fn attachment_names(document: &Value) -> HashMap<String, Option<String>> {
    let Some(list) = document.get("attachments").and_then(Value::as_array) else {
        return HashMap::new();
    };
    list.iter()
        .filter_map(|item| {
            let id = item.get("id")?.as_str()?.to_owned();
            Some((
                id,
                item.get("fileName")
                    .and_then(Value::as_str)
                    .map(str::to_owned),
            ))
        })
        .collect()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn attachment_names_are_read_from_the_document() {
        let document = serde_json::json!({ "attachments": [
            { "id": "2", "fileName": "report.pdf" },
            { "id": "3", "fileName": null },
            { "fileName": "no-id.txt" },
        ]});
        let names = attachment_names(&document);
        assert_eq!(names.len(), 2);
        assert_eq!(names["2"].as_deref(), Some("report.pdf"));
        assert_eq!(names["3"], None);
        assert!(attachment_names(&serde_json::json!({})).is_empty());
    }
}
