//! sidecar 监管：按需启动、请求超时、崩溃后重启，以及崩溃后按需恢复已打开的文档。
//!
//! 所有方法都要在 tokio 运行时里调用（Tauri 命令就是）。

use std::collections::HashMap;
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::sync::{Arc, Mutex as StdMutex};

use serde_json::{Value, json};
use tokio::sync::Mutex;

use super::connection::{Connection, SidecarCommand};
use super::documents::{DocEntry, Source};
use super::protocol::{
    DocHandle, OpenResult, OpenedDocument, PROTOCOL_VERSION, PathResult, PingResult, Result,
    SaveAllResult, SidecarError, Timeouts, code, kills_sidecar_on_timeout, method, parse_result,
};

/// 怎样启动 sidecar，以及各类请求的超时。
#[derive(Clone, Debug)]
pub struct SidecarConfig {
    pub command: SidecarCommand,
    pub timeouts: Timeouts,
}

impl SidecarConfig {
    pub fn new(command: SidecarCommand) -> Self {
        Self {
            command,
            timeouts: Timeouts::default(),
        }
    }

    /// 随应用打包的 sidecar，默认超时。
    pub fn bundled() -> std::io::Result<Self> {
        Ok(Self::new(SidecarCommand::bundled()?))
    }
}

struct Inner {
    config: SidecarConfig,
    /// 当前这一代 sidecar。加锁期间可能在启动进程并握手，所以并发的第一批请求会等同一次启动。
    current: Mutex<Option<Arc<Connection>>>,
    next_request_id: AtomicU64,
    next_generation: AtomicU64,
    next_handle: AtomicU64,
    documents: StdMutex<HashMap<DocHandle, Arc<DocEntry>>>,
    shutting_down: AtomicBool,
}

/// sidecar 监管服务。可以随便 `clone`，所有副本共享同一个 sidecar 和同一张文档表。
///
/// 创建时不启动任何东西，第一次请求才启动并握手。
#[derive(Clone)]
pub struct SidecarService {
    inner: Arc<Inner>,
}

impl SidecarService {
    pub fn new(config: SidecarConfig) -> Self {
        Self {
            inner: Arc::new(Inner {
                config,
                current: Mutex::new(None),
                next_request_id: AtomicU64::new(1),
                next_generation: AtomicU64::new(1),
                next_handle: AtomicU64::new(1),
                documents: StdMutex::new(HashMap::new()),
                shutting_down: AtomicBool::new(false),
            }),
        }
    }

    // ------------------------------------------------------------ 公开 API

    /// 打开顶层邮件文件。路径必须来自 Rust 弹出的对话框、拖放事件或启动参数，不能来自前端。
    ///
    /// sidecar 在处理中崩溃时直接返回 [`SidecarError::Crashed`]，**不会自动重试**：
    /// 可能就是这个文件让 sidecar 崩溃的，重试只会再崩一次。
    pub async fn open_file(&self, path: &Path) -> Result<OpenedDocument> {
        // 相对路径（例如启动参数）按应用的工作目录补全，sidecar 的工作目录不一定相同；恢复文档时用的也是这个路径
        let path = std::path::absolute(path).map_err(|e| {
            SidecarError::remote(
                code::INVALID_PARAMS,
                format!("cannot resolve {}: {e}", path.display()),
            )
        })?;
        let path_text = path_param(&path)?;
        let conn = self.connection().await?;
        let opened = self
            .open(&conn, method::EMAIL_OPEN, json!({ "path": path_text }))
            .await?;
        let handle = self.new_handle();
        let entry = DocEntry::new(handle, Source::File(path), conn.generation(), opened.doc_id);
        self.inner.documents.lock().unwrap().insert(handle, entry);
        Ok(OpenedDocument {
            handle,
            document: opened.document,
        })
    }

    /// 打开文档里的嵌套邮件附件，得到一个新的文档句柄。父文档需要时会先被恢复。
    pub async fn open_nested(
        &self,
        parent: DocHandle,
        attachment_id: &str,
    ) -> Result<OpenedDocument> {
        let parent_entry = self.entry(parent)?;
        let conn = self.connection().await?;
        let parent_doc_id = self.resolve(&conn, &parent_entry).await?;
        let opened = self
            .open(
                &conn,
                method::EMAIL_OPEN_NESTED,
                json!({ "docId": parent_doc_id, "attachmentId": attachment_id }),
            )
            .await?;

        let handle = self.new_handle();
        parent_entry.open_children.fetch_add(1, Ordering::SeqCst);
        let source = Source::Nested {
            parent: parent_entry,
            attachment_id: attachment_id.to_owned(),
        };
        let entry = DocEntry::new(handle, source, conn.generation(), opened.doc_id);
        self.inner.documents.lock().unwrap().insert(handle, entry);
        Ok(OpenedDocument {
            handle,
            document: opened.document,
        })
    }

    /// 关闭文档并释放 sidecar 里的内存。句柄随即失效。
    ///
    /// 文档还有没关闭的嵌套子文档时，sidecar 里的文档先保留（子文档重新打开时要用），等最后一个子文档关闭后再关闭。
    /// sidecar 已经重启过（文档已随旧进程消失）时不会为了关闭而启动它。
    pub async fn close_document(&self, handle: DocHandle) -> Result<()> {
        let entry = self
            .inner
            .documents
            .lock()
            .unwrap()
            .remove(&handle)
            .ok_or(SidecarError::UnknownHandle)?;
        entry.closed_by_user.store(true, Ordering::SeqCst);
        self.finalize(entry).await
    }

    /// 把附件保存到用户在保存对话框里选的路径。
    pub async fn save_attachment(
        &self,
        handle: DocHandle,
        attachment_id: &str,
        target_path: &Path,
    ) -> Result<PathBuf> {
        let target = path_param(target_path)?;
        let value = self
            .document_call(handle, method::ATTACHMENT_SAVE, |doc_id| {
                json!({ "docId": doc_id, "attachmentId": attachment_id, "targetPath": target })
            })
            .await?;
        Ok(parse_result::<PathResult>(method::ATTACHMENT_SAVE, value)?.path)
    }

    /// 把全部附件保存到用户选的文件夹。单个附件失败不影响其他附件，见结果里的 `failed`。
    pub async fn save_all_attachments(
        &self,
        handle: DocHandle,
        target_dir: &Path,
    ) -> Result<SaveAllResult> {
        let target = path_param(target_dir)?;
        let value = self
            .document_call(
                handle,
                method::ATTACHMENT_SAVE_ALL,
                |doc_id| json!({ "docId": doc_id, "targetDir": target }),
            )
            .await?;
        parse_result(method::ATTACHMENT_SAVE_ALL, value)
    }

    /// 把附件提取到本次会话的临时目录，返回文件路径，由调用方交给系统默认程序打开。
    /// 可执行文件和脚本会被 sidecar 拒绝（`executable_refused`）。
    pub async fn extract_for_open(
        &self,
        handle: DocHandle,
        attachment_id: &str,
    ) -> Result<PathBuf> {
        let value = self
            .document_call(
                handle,
                method::ATTACHMENT_EXTRACT_FOR_OPEN,
                |doc_id| json!({ "docId": doc_id, "attachmentId": attachment_id }),
            )
            .await?;
        Ok(parse_result::<PathResult>(method::ATTACHMENT_EXTRACT_FOR_OPEN, value)?.path)
    }

    /// 应用退出时调用：关闭 sidecar 的 stdin，等它自己退出，超过 `Timeouts::shutdown_grace` 仍不退出就强制结束。
    /// 返回时进程已被回收。此后所有请求都返回 [`SidecarError::Unavailable`]。没有启动过 sidecar 时什么也不做。
    pub async fn shutdown(&self) {
        self.inner.shutting_down.store(true, Ordering::SeqCst);
        let conn = self.inner.current.lock().await.take();
        if let Some(conn) = conn {
            conn.shutdown(self.inner.config.timeouts.shutdown_grace)
                .await;
        }
        self.inner.documents.lock().unwrap().clear();
    }

    /// 同步版本，给没有运行在异步环境里的退出回调用（例如 `RunEvent::Exit`）。
    pub fn shutdown_blocking(&self) {
        tauri::async_runtime::block_on(self.shutdown());
    }

    // ------------------------------------------------------------ 内部

    fn new_handle(&self) -> DocHandle {
        DocHandle(self.inner.next_handle.fetch_add(1, Ordering::SeqCst))
    }

    fn entry(&self, handle: DocHandle) -> Result<Arc<DocEntry>> {
        self.inner
            .documents
            .lock()
            .unwrap()
            .get(&handle)
            .cloned()
            .ok_or(SidecarError::UnknownHandle)
    }

    /// 当前这一代 sidecar；没有或已经退出就启动新的一代，并完成握手。
    async fn connection(&self) -> Result<Arc<Connection>> {
        let mut current = self.inner.current.lock().await;
        if self.inner.shutting_down.load(Ordering::SeqCst) {
            return Err(SidecarError::Unavailable {
                message: "the application is shutting down".into(),
            });
        }
        if let Some(conn) = current.as_ref().filter(|c| c.is_alive()) {
            return Ok(conn.clone());
        }

        let generation = self.inner.next_generation.fetch_add(1, Ordering::SeqCst);
        let conn = Connection::spawn(&self.inner.config.command, generation)?;
        match self.handshake(&conn).await {
            Ok(()) => {
                *current = Some(conn.clone());
                Ok(conn)
            }
            Err(error) => {
                conn.kill().await;
                *current = None;
                Err(error)
            }
        }
    }

    /// 就绪握手：`system.ping`，协议版本必须一致。
    async fn handshake(&self, conn: &Connection) -> Result<()> {
        let unavailable = |why: String| SidecarError::Unavailable {
            message: format!("sidecar handshake failed: {why}"),
        };
        let id = self.inner.next_request_id.fetch_add(1, Ordering::SeqCst);
        let value = conn
            .request(
                id,
                method::PING,
                &json!({}),
                self.inner.config.timeouts.handshake,
            )
            .await
            .map_err(|e| unavailable(e.to_string()))?;
        let ping: PingResult =
            parse_result(method::PING, value).map_err(|e| unavailable(e.to_string()))?;
        if ping.protocol_version != PROTOCOL_VERSION {
            return Err(unavailable(format!(
                "protocol version {} is not supported (expected {PROTOCOL_VERSION})",
                ping.protocol_version
            )));
        }
        Ok(())
    }

    /// 发送一个请求，超时时间按方法区分。
    pub(crate) async fn call(
        &self,
        conn: &Connection,
        method: &str,
        params: Value,
    ) -> Result<Value> {
        let id = self.inner.next_request_id.fetch_add(1, Ordering::SeqCst);
        let limit = self.inner.config.timeouts.for_method(method);
        let result = conn.request(id, method, &params, limit).await;
        if matches!(result, Err(SidecarError::Timeout { .. })) && kills_sidecar_on_timeout(method) {
            // 卡住的解析无法取消：结束这个 sidecar，下次请求重新启动，已打开的文档按需恢复
            log::warn!("{method} timed out after {limit:?}; restarting the sidecar");
            conn.abort(&format!(
                "the sidecar was restarted because {method} timed out"
            ));
        }
        result
    }

    async fn open(&self, conn: &Connection, method: &str, params: Value) -> Result<OpenResult> {
        let value = self.call(conn, method, params).await?;
        parse_result(method, value)
    }

    /// 对一个已打开的文档发请求：先确保 sidecar 在运行，并且文档已经在当前这一代里（旧代次的会先重新打开）。
    async fn document_call(
        &self,
        handle: DocHandle,
        method: &str,
        params: impl FnOnce(&str) -> Value,
    ) -> Result<Value> {
        let entry = self.entry(handle)?;
        let conn = self.connection().await?;
        let doc_id = self.resolve(&conn, &entry).await?;
        self.call(&conn, method, params(&doc_id)).await
    }

    /// 返回文档在当前这一代 sidecar 里的 `docId`。文档属于旧的一代时，按来源重新打开：
    /// 顶层文件重新 `email.open`；嵌套邮件先恢复父文档，再 `email.openNested`。
    ///
    /// 只尝试一次：重新打开失败（包括 sidecar 又崩了）就把错误交给这次请求，不循环重试。
    async fn resolve(&self, conn: &Arc<Connection>, entry: &Arc<DocEntry>) -> Result<String> {
        let mut residence = entry.residence.lock().await;
        if residence.generation == conn.generation() {
            return Ok(residence.doc_id.clone());
        }

        log::debug!(
            "reopening document handle {} in sidecar #{}",
            entry.handle.0,
            conn.generation()
        );
        let opened = match &entry.source {
            Source::File(path) => {
                self.open(
                    conn,
                    method::EMAIL_OPEN,
                    json!({ "path": path_param(path)? }),
                )
                .await?
            }
            Source::Nested {
                parent,
                attachment_id,
            } => {
                let parent_doc_id = Box::pin(self.resolve(conn, parent)).await?;
                self.open(
                    conn,
                    method::EMAIL_OPEN_NESTED,
                    json!({ "docId": parent_doc_id, "attachmentId": attachment_id }),
                )
                .await?
            }
        };
        residence.generation = conn.generation();
        residence.doc_id = opened.doc_id.clone();
        Ok(opened.doc_id)
    }

    /// 真正关闭一个已被前端关闭的文档：它没有未关闭的子文档，并且还在当前这一代 sidecar 里时，向 sidecar 发 `document.close`。
    /// 关闭后，如果它是嵌套邮件，父文档可能因此变得可以关闭，沿着父链继续。
    async fn finalize(&self, entry: Arc<DocEntry>) -> Result<()> {
        let mut result = Ok(());
        let mut next = Some(entry);
        let mut first = true;
        while let Some(entry) = next.take() {
            if !entry.closed_by_user.load(Ordering::SeqCst)
                || entry.open_children.load(Ordering::SeqCst) > 0
                || entry.finalized.swap(true, Ordering::SeqCst)
            {
                break;
            }

            let outcome = self.close_in_sidecar(&entry).await;
            if first {
                result = outcome;
                first = false;
            } else if let Err(error) = outcome {
                log::debug!("closing parent document {} failed: {error}", entry.handle.0);
            }

            if let Source::Nested { parent, .. } = &entry.source {
                parent.open_children.fetch_sub(1, Ordering::SeqCst);
                next = Some(parent.clone());
            }
        }
        result
    }

    async fn close_in_sidecar(&self, entry: &DocEntry) -> Result<()> {
        // 只用已经在运行的 sidecar：为了关闭文档而去启动它没有意义，旧进程里的文档早就没了
        let conn = self
            .inner
            .current
            .lock()
            .await
            .clone()
            .filter(|c| c.is_alive());
        let Some(conn) = conn else { return Ok(()) };
        let doc_id = {
            let residence = entry.residence.lock().await;
            if residence.generation != conn.generation() {
                return Ok(());
            }
            residence.doc_id.clone()
        };
        match self
            .call(&conn, method::DOCUMENT_CLOSE, json!({ "docId": doc_id }))
            .await
        {
            Ok(_) => Ok(()),
            // 文档已经不在了就是想要的结果
            Err(SidecarError::Remote { code, .. }) if code == code::DOCUMENT_NOT_FOUND => Ok(()),
            Err(error) => Err(error),
        }
    }
}

/// 路径通过 JSON 传给 sidecar，必须是合法的 UTF-8。
fn path_param(path: &Path) -> Result<&str> {
    path.to_str().ok_or_else(|| {
        SidecarError::remote(
            code::INVALID_PARAMS,
            format!("the path is not valid UTF-8: {}", path.display()),
        )
    })
}

#[cfg(test)]
impl SidecarService {
    /// 当前（可能已经退出的）这一代 sidecar 的代次和进程 id。
    pub(crate) async fn current_process(&self) -> Option<(u64, Option<u32>)> {
        self.inner
            .current
            .lock()
            .await
            .as_ref()
            .map(|c| (c.generation(), c.pid()))
    }

    pub(crate) async fn current_connection(&self) -> Option<Arc<Connection>> {
        self.inner.current.lock().await.clone()
    }

    /// 还保留着多少个文档句柄。
    pub(crate) fn open_handles(&self) -> usize {
        self.inner.documents.lock().unwrap().len()
    }

    pub(crate) fn requests_sent(&self) -> u64 {
        self.inner.next_request_id.load(Ordering::SeqCst) - 1
    }

    /// 绕过句柄，直接对当前 sidecar 发原始请求（会按需启动）。
    pub(crate) async fn raw(&self, method: &str, params: Value) -> Result<Value> {
        let conn = self.connection().await?;
        self.call(&conn, method, params).await
    }
}
