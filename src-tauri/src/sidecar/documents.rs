//! 文档句柄：Rust 发给前端的稳定标识，以及重新打开文档所需的来源。
//!
//! sidecar 的 `docId` 只在它那一代进程内有效。每个句柄记录它属于哪一代 sidecar、对应的 `docId`，
//! 以及来源：顶层文件的路径，或“父文档 + 附件 Id”。sidecar 重启后，旧代次的句柄在下次使用时按来源重新打开。

use std::path::PathBuf;
use std::sync::Arc;
use std::sync::atomic::{AtomicBool, AtomicUsize};

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
    ) -> Arc<Self> {
        Arc::new(Self {
            handle,
            source,
            residence: Mutex::new(Residence { generation, doc_id }),
            open_children: AtomicUsize::new(0),
            closed_by_user: AtomicBool::new(false),
            finalized: AtomicBool::new(false),
        })
    }
}
