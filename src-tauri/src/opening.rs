//! 打开批次（design.md D11）：一组文件（来自对话框、拖放、启动参数或单实例转发）按顺序依次交给 sidecar 打开，
//! 每个文件完成后立即发事件，不必等整批结束；批次结束再发一条汇总。
//!
//! 前端调用 `app_ready` 之前，收到的批次先排队，等前端注册好事件监听后再处理。

use std::future::Future;
use std::path::{Path, PathBuf};
use std::sync::{Arc, Mutex};

use serde::Serialize;
use serde_json::Value;
use tauri::{AppHandle, Emitter};
use tokio::sync::mpsc;

use crate::sidecar::{DocHandle, OpenedDocument, SidecarError, SidecarService};

/// 事件只发给主窗口。
const MAIN_WINDOW: &str = "main";

pub mod event {
    pub const DOCUMENT_OPENED: &str = "efv://document-opened";
    pub const DOCUMENT_OPEN_FAILED: &str = "efv://document-open-failed";
    pub const OPEN_BATCH_FINISHED: &str = "efv://open-batch-finished";
}

/// 一个打开批次。`id` 由 Rust 生成，同一批次的事件带着同一个 `batchId`。
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Batch {
    pub id: u64,
    pub paths: Vec<PathBuf>,
}

// ---------------------------------------------------------------- 事件

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct DocumentOpened {
    pub batch_id: u64,
    pub handle: DocHandle,
    pub file_name: String,
    pub document: Value,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct DocumentOpenFailed {
    pub batch_id: u64,
    pub file_name: String,
    pub code: String,
    pub message: String,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct BatchFinished {
    pub batch_id: u64,
    pub opened: u32,
    pub failed: u32,
}

#[derive(Debug)]
pub enum OpenEvent {
    Opened(DocumentOpened),
    Failed(DocumentOpenFailed),
    Finished(BatchFinished),
}

impl OpenEvent {
    pub fn name(&self) -> &'static str {
        match self {
            Self::Opened(_) => event::DOCUMENT_OPENED,
            Self::Failed(_) => event::DOCUMENT_OPEN_FAILED,
            Self::Finished(_) => event::OPEN_BATCH_FINISHED,
        }
    }

    pub fn payload(&self) -> Value {
        let value = match self {
            Self::Opened(e) => serde_json::to_value(e),
            Self::Failed(e) => serde_json::to_value(e),
            Self::Finished(e) => serde_json::to_value(e),
        };
        value.expect("event payloads are serializable")
    }
}

/// 事件里只放文件名，不暴露完整路径。
pub fn file_name_of(path: &Path) -> String {
    path.file_name()
        .map(|name| name.to_string_lossy().into_owned())
        .unwrap_or_default()
}

/// sidecar 的错误说明里可能带着完整路径（例如“无法读取 /Users/…”），发给前端之前换成文件名。
pub fn redact_path(message: &str, path: &Path) -> String {
    let full = path.to_string_lossy();
    if full.is_empty() {
        return message.to_owned();
    }
    message.replace(full.as_ref(), &file_name_of(path))
}

/// 依次打开批次里的文件。每个文件完成后立即调用 `emit`；单个文件失败不影响后面的文件；最后发汇总。
pub async fn run_batch<F, Fut>(batch: Batch, open: F, emit: impl Fn(OpenEvent))
where
    F: Fn(PathBuf) -> Fut,
    Fut: Future<Output = Result<OpenedDocument, SidecarError>>,
{
    let (mut opened, mut failed) = (0u32, 0u32);
    for path in batch.paths {
        let file_name = file_name_of(&path);
        match open(path.clone()).await {
            Ok(document) => {
                opened += 1;
                emit(OpenEvent::Opened(DocumentOpened {
                    batch_id: batch.id,
                    handle: document.handle,
                    file_name,
                    document: document.document,
                }));
            }
            Err(error) => {
                failed += 1;
                log::debug!("opening {file_name} failed: {error}");
                emit(OpenEvent::Failed(DocumentOpenFailed {
                    batch_id: batch.id,
                    file_name,
                    code: error.wire_code().to_owned(),
                    message: redact_path(&error.message(), &path),
                }));
            }
        }
    }
    emit(OpenEvent::Finished(BatchFinished {
        batch_id: batch.id,
        opened,
        failed,
    }));
}

// ---------------------------------------------------------------- app_ready 之前的缓冲

struct GateState {
    ready: bool,
    waiting: Vec<Batch>,
    next_id: u64,
}

/// 前端调用 `app_ready` 之前，批次先排队；之后直接交给 `sink`。批次按提交的先后顺序交给 `sink`。
pub struct Gate<S: Fn(Batch)> {
    state: Mutex<GateState>,
    sink: S,
}

impl<S: Fn(Batch)> Gate<S> {
    pub fn new(sink: S) -> Self {
        Self {
            state: Mutex::new(GateState {
                ready: false,
                waiting: Vec::new(),
                next_id: 1,
            }),
            sink,
        }
    }

    /// 提交一组文件。路径列表为空时什么也不做，返回 `None`；否则返回新批次的 id。
    pub fn submit(&self, paths: Vec<PathBuf>) -> Option<u64> {
        if paths.is_empty() {
            return None;
        }
        let mut state = self.state.lock().unwrap();
        let batch = Batch {
            id: state.next_id,
            paths,
        };
        state.next_id += 1;
        let id = batch.id;
        if state.ready {
            // 在锁内交给 sink，保证和 open() 排队的批次之间不会乱序
            (self.sink)(batch);
        } else {
            state.waiting.push(batch);
        }
        Some(id)
    }

    /// 前端已经准备好：处理排队的批次，此后不再排队。可以重复调用（例如开发时页面热更新后）。
    pub fn open(&self) {
        let mut state = self.state.lock().unwrap();
        state.ready = true;
        for batch in std::mem::take(&mut state.waiting) {
            (self.sink)(batch);
        }
    }

    pub fn is_ready(&self) -> bool {
        self.state.lock().unwrap().ready
    }
}

// ---------------------------------------------------------------- 协调器

type BoxedSink = Box<dyn Fn(Batch) + Send + Sync>;

/// 所有打开文件的来源（对话框、拖放、启动参数、单实例转发）都通过它提交批次。
/// 批次在一个后台任务里顺序处理，所以标签页的顺序和提交的顺序一致。
#[derive(Clone)]
pub struct OpenCoordinator {
    gate: Arc<Gate<BoxedSink>>,
}

impl OpenCoordinator {
    /// `emit` 把事件交给前端。要在 tokio 运行时里创建（Tauri 的 `setup` 满足）。
    pub fn new(service: SidecarService, emit: impl Fn(OpenEvent) + Send + Sync + 'static) -> Self {
        let (tx, mut rx) = mpsc::unbounded_channel::<Batch>();
        tauri::async_runtime::spawn(async move {
            while let Some(batch) = rx.recv().await {
                run_batch(
                    batch,
                    |path| {
                        let service = service.clone();
                        async move { service.open_file(&path).await }
                    },
                    &emit,
                )
                .await;
            }
        });
        let sink: BoxedSink = Box::new(move |batch| {
            let _ = tx.send(batch);
        });
        Self {
            gate: Arc::new(Gate::new(sink)),
        }
    }

    /// 用 Tauri 的事件系统把事件发给主窗口。
    pub fn for_app(app: AppHandle, service: SidecarService) -> Self {
        Self::new(service, move |event| {
            log::debug!(
                "emitting {} (batch {})",
                event.name(),
                event.payload()["batchId"]
            );
            if let Err(error) = app.emit_to(MAIN_WINDOW, event.name(), event.payload()) {
                log::warn!("failed to emit {}: {error}", event.name());
            }
        })
    }

    /// 提交一组文件作为一个批次；前端还没有调用 `app_ready` 时先排队。路径列表为空时什么也不做。
    pub fn submit(&self, paths: Vec<PathBuf>) {
        if let Some(id) = self.gate.submit(paths) {
            log::debug!(
                "open batch {id} submitted (frontend ready: {})",
                self.gate.is_ready()
            );
        }
    }

    pub fn app_ready(&self) {
        self.gate.open();
    }
}

#[cfg(test)]
mod tests {
    use serde_json::json;
    use std::cell::RefCell;

    use super::*;

    fn paths(names: &[&str]) -> Vec<PathBuf> {
        names
            .iter()
            .map(|n| PathBuf::from(format!("/mail/{n}")))
            .collect()
    }

    fn collect(events: &RefCell<Vec<(String, Value)>>) -> impl Fn(OpenEvent) + '_ {
        |event| {
            events
                .borrow_mut()
                .push((event.name().to_owned(), event.payload()))
        }
    }

    /// 假的“打开”：文件名里带 bad 的失败，带 crash 的以 sidecar_crashed 失败，其余成功，handle 按顺序编号。
    fn fake_open(path: PathBuf) -> impl Future<Output = Result<OpenedDocument, SidecarError>> {
        let name = file_name_of(&path);
        async move {
            if name.contains("crash") {
                Err(SidecarError::Crashed {
                    message: format!("the sidecar exited while opening {}", path.display()),
                })
            } else if name.contains("bad") {
                Err(SidecarError::remote(
                    "corrupted",
                    format!("{} is damaged", path.display()),
                ))
            } else {
                Ok(OpenedDocument {
                    handle: DocHandle(name.len() as u64),
                    document: json!({ "subject": name }),
                })
            }
        }
    }

    #[tokio::test]
    async fn every_file_gets_an_event_in_order_followed_by_a_summary() {
        let events = RefCell::new(Vec::new());
        run_batch(
            Batch {
                id: 7,
                paths: paths(&["a.eml", "bad.msg", "c.eml"]),
            },
            fake_open,
            collect(&events),
        )
        .await;
        let events = events.into_inner();
        let names: Vec<_> = events.iter().map(|(n, _)| n.as_str()).collect();
        assert_eq!(
            names,
            [
                "efv://document-opened",
                "efv://document-open-failed",
                "efv://document-opened",
                "efv://open-batch-finished"
            ]
        );
        assert_eq!(
            events[0].1,
            json!({ "batchId": 7, "handle": 5, "fileName": "a.eml", "document": { "subject": "a.eml" } })
        );
        assert_eq!(events[1].1["batchId"], 7);
        assert_eq!(events[1].1["fileName"], "bad.msg");
        assert_eq!(events[1].1["code"], "corrupted");
        assert_eq!(events[2].1["fileName"], "c.eml");
        assert_eq!(
            events[3].1,
            json!({ "batchId": 7, "opened": 2, "failed": 1 })
        );
    }

    #[tokio::test]
    async fn events_use_the_exact_field_names_of_d11() {
        let events = RefCell::new(Vec::new());
        run_batch(
            Batch {
                id: 1,
                paths: paths(&["a.eml", "bad.eml"]),
            },
            fake_open,
            collect(&events),
        )
        .await;
        let events = events.into_inner();
        let keys = |v: &Value| {
            let mut keys: Vec<_> = v.as_object().unwrap().keys().cloned().collect();
            keys.sort();
            keys
        };
        assert_eq!(
            keys(&events[0].1),
            ["batchId", "document", "fileName", "handle"]
        );
        assert_eq!(
            keys(&events[1].1),
            ["batchId", "code", "fileName", "message"]
        );
        assert_eq!(keys(&events[2].1), ["batchId", "failed", "opened"]);
    }

    #[tokio::test]
    async fn each_event_is_emitted_before_the_next_file_is_opened() {
        // 不是等整批结束才发：打开第二个文件时，第一个文件的事件已经发出
        let events = RefCell::new(Vec::<String>::new());
        let seen_at_second_open = RefCell::new(None);
        let open = |path: PathBuf| {
            if file_name_of(&path) == "b.eml" {
                *seen_at_second_open.borrow_mut() = Some(events.borrow().len());
            }
            fake_open(path)
        };
        run_batch(
            Batch {
                id: 1,
                paths: paths(&["a.eml", "b.eml"]),
            },
            open,
            |e| events.borrow_mut().push(e.name().to_owned()),
        )
        .await;
        assert_eq!(*seen_at_second_open.borrow(), Some(1));
    }

    #[tokio::test]
    async fn only_the_file_name_is_exposed_never_the_full_path() {
        let events = RefCell::new(Vec::new());
        let batch = Batch {
            id: 1,
            paths: vec![
                PathBuf::from("/Users/alice/Secret Folder/crash.eml"),
                PathBuf::from("/Users/alice/Secret Folder/bad.eml"),
            ],
        };
        run_batch(batch, fake_open, collect(&events)).await;
        let text = serde_json::to_string(
            &events
                .into_inner()
                .iter()
                .map(|(_, v)| v.clone())
                .collect::<Vec<_>>(),
        )
        .unwrap();
        assert!(!text.contains("/Users/alice"), "{text}");
        assert!(!text.contains("Secret Folder"), "{text}");
        assert!(text.contains("crash.eml") && text.contains("bad.eml"));
    }

    #[tokio::test]
    async fn failures_use_the_error_codes_of_d11() {
        let events = RefCell::new(Vec::new());
        run_batch(
            Batch {
                id: 1,
                paths: paths(&["crash.eml"]),
            },
            fake_open,
            collect(&events),
        )
        .await;
        assert_eq!(events.borrow()[0].1["code"], "sidecar_crashed");
        assert_eq!(
            events.borrow()[1].1,
            json!({ "batchId": 1, "opened": 0, "failed": 1 })
        );
    }

    #[tokio::test]
    async fn an_empty_batch_still_reports_a_summary() {
        let events = RefCell::new(Vec::new());
        run_batch(
            Batch {
                id: 3,
                paths: vec![],
            },
            fake_open,
            collect(&events),
        )
        .await;
        assert_eq!(
            events.into_inner(),
            [(
                "efv://open-batch-finished".to_owned(),
                json!({ "batchId": 3, "opened": 0, "failed": 0 })
            )]
        );
    }

    #[test]
    fn redaction_replaces_the_full_path_with_the_file_name() {
        let path = Path::new("/Users/alice/Mail/a b.eml");
        assert_eq!(
            redact_path("cannot read /Users/alice/Mail/a b.eml: denied", path),
            "cannot read a b.eml: denied"
        );
        assert_eq!(redact_path("nothing to hide", path), "nothing to hide");
        assert_eq!(file_name_of(Path::new("/")), "");
    }

    // ---- app_ready 之前的缓冲

    type Collected = Arc<Mutex<Vec<Batch>>>;

    fn gate() -> (Gate<impl Fn(Batch)>, Collected) {
        let out = Arc::new(Mutex::new(Vec::new()));
        let sink = out.clone();
        (
            Gate::new(move |batch| sink.lock().unwrap().push(batch)),
            out,
        )
    }

    #[test]
    fn batches_wait_until_the_frontend_is_ready_and_then_run_in_order() {
        let (gate, out) = gate();
        assert_eq!(gate.submit(paths(&["startup.eml"])), Some(1));
        assert_eq!(
            gate.submit(paths(&["forwarded-1.eml", "forwarded-2.eml"])),
            Some(2)
        );
        assert!(
            out.lock().unwrap().is_empty(),
            "nothing is processed before app_ready"
        );
        assert!(!gate.is_ready());

        gate.open();
        let ids: Vec<_> = out.lock().unwrap().iter().map(|b| b.id).collect();
        assert_eq!(ids, [1, 2]);
        assert_eq!(out.lock().unwrap()[1].paths.len(), 2);
    }

    #[test]
    fn after_app_ready_batches_go_straight_through() {
        let (gate, out) = gate();
        gate.open();
        assert_eq!(gate.submit(paths(&["a.eml"])), Some(1));
        assert_eq!(out.lock().unwrap().len(), 1);
        assert_eq!(gate.submit(paths(&["b.eml"])), Some(2));
        assert_eq!(
            out.lock().unwrap().iter().map(|b| b.id).collect::<Vec<_>>(),
            [1, 2]
        );
    }

    #[test]
    fn app_ready_can_be_called_again_without_replaying_anything() {
        let (gate, out) = gate();
        gate.submit(paths(&["a.eml"]));
        gate.open();
        gate.open();
        assert_eq!(out.lock().unwrap().len(), 1);
    }

    #[test]
    fn empty_path_lists_do_not_create_batches() {
        let (gate, out) = gate();
        assert_eq!(gate.submit(vec![]), None);
        gate.open();
        assert_eq!(gate.submit(vec![]), None);
        assert!(out.lock().unwrap().is_empty());
        // 没有消耗批次 id
        assert_eq!(gate.submit(paths(&["a.eml"])), Some(1));
    }
}

#[cfg(test)]
mod coordinator_tests {
    //! 协调器的端到端测试：真实的批次队列 + 假 sidecar 子进程，事件收集在列表里。
    use std::sync::{Arc, Mutex};
    use std::time::{Duration, Instant};

    use serde_json::Value;
    use tokio::time::sleep;

    use super::*;
    use crate::sidecar::testing::service;

    type Events = Arc<Mutex<Vec<(String, Value)>>>;

    fn coordinator() -> (OpenCoordinator, Events, SidecarService) {
        let events: Events = Arc::default();
        let sink = events.clone();
        let service = service();
        let coordinator = OpenCoordinator::new(service.clone(), move |event| {
            sink.lock()
                .unwrap()
                .push((event.name().to_owned(), event.payload()))
        });
        (coordinator, events, service)
    }

    async fn wait_for(events: &Events, count: usize) -> Vec<(String, Value)> {
        let deadline = Instant::now() + Duration::from_secs(10);
        while events.lock().unwrap().len() < count {
            assert!(
                Instant::now() < deadline,
                "timed out waiting for {count} events, got {:?}",
                events.lock().unwrap()
            );
            sleep(Duration::from_millis(20)).await;
        }
        events.lock().unwrap().clone()
    }

    fn files(names: &[&str]) -> Vec<PathBuf> {
        names
            .iter()
            .map(|n| PathBuf::from(format!("/mail/{n}")))
            .collect()
    }

    #[tokio::test]
    async fn files_queued_before_app_ready_are_opened_once_the_frontend_is_ready() {
        let (coordinator, events, service) = coordinator();
        coordinator.submit(files(&["startup.eml"]));
        coordinator.submit(files(&["forwarded-1.eml", "forwarded-2.eml"]));
        sleep(Duration::from_millis(300)).await;
        assert!(
            events.lock().unwrap().is_empty(),
            "nothing is emitted before app_ready"
        );
        assert!(
            service.current_process().await.is_none(),
            "the sidecar is not even started yet"
        );

        coordinator.app_ready();
        let all = wait_for(&events, 5).await;
        let names: Vec<_> = all.iter().map(|(n, _)| n.as_str()).collect();
        assert_eq!(
            names,
            [
                "efv://document-opened",
                "efv://open-batch-finished",
                "efv://document-opened",
                "efv://document-opened",
                "efv://open-batch-finished"
            ]
        );
        assert_eq!(all[0].1["fileName"], "startup.eml");
        assert_eq!(all[0].1["batchId"], 1);
        assert_eq!(
            all[1].1,
            serde_json::json!({ "batchId": 1, "opened": 1, "failed": 0 })
        );
        assert_eq!(
            [
                all[2].1["fileName"].as_str().unwrap(),
                all[3].1["fileName"].as_str().unwrap()
            ],
            ["forwarded-1.eml", "forwarded-2.eml"]
        );
        assert_eq!(all[2].1["batchId"], 2);
        // 句柄是 Rust 分配的整数，彼此不同
        let handles: Vec<_> = [&all[0], &all[2], &all[3]]
            .iter()
            .map(|(_, v)| v["handle"].as_u64().unwrap())
            .collect();
        assert_eq!(handles, [1, 2, 3]);
    }

    #[tokio::test]
    async fn a_dropped_batch_opens_every_file_and_a_failure_does_not_stop_the_rest() {
        let (coordinator, events, _service) = coordinator();
        coordinator.app_ready();
        // 拖入三个文件：第二个无法读取，第三个让 sidecar 崩溃，第四个仍然能打开（sidecar 自动重启）
        coordinator.submit(files(&["one.eml", "missing.msg", "crash.eml", "four.eml"]));
        let all = wait_for(&events, 5).await;
        assert_eq!(all[0].1["fileName"], "one.eml");
        assert_eq!(
            (all[1].0.as_str(), all[1].1["code"].as_str()),
            ("efv://document-open-failed", Some("unreadable"))
        );
        assert_eq!(
            (all[2].0.as_str(), all[2].1["code"].as_str()),
            ("efv://document-open-failed", Some("sidecar_crashed"))
        );
        assert_eq!(
            (all[3].0.as_str(), all[3].1["fileName"].as_str()),
            ("efv://document-opened", Some("four.eml"))
        );
        assert_eq!(
            all[4].1,
            serde_json::json!({ "batchId": 1, "opened": 2, "failed": 2 })
        );
        // 假 sidecar 会把路径回显在 document 里；真实的 document 不含路径。其余字段里不能出现完整路径
        let text = serde_json::to_string(
            &all.iter()
                .map(|(_, v)| (&v["fileName"], &v["code"], &v["message"]))
                .collect::<Vec<_>>(),
        )
        .unwrap();
        assert!(!text.contains("/mail/"), "no full path leaves Rust: {text}");
    }

    #[tokio::test]
    async fn a_launch_argument_that_does_not_exist_is_reported_as_unreadable() {
        // 命令行里的路径不存在：不能静默忽略，要像其他打不开的文件一样得到失败事件，文件名是它的名字
        let (coordinator, events, _service) = coordinator();
        coordinator.app_ready();
        let args = ["app", "missing-mail.eml", "ok.eml"];
        coordinator.submit(crate::fileargs::paths_from_args(&args, Path::new("/work")));
        let all = wait_for(&events, 3).await;
        assert_eq!(
            (
                all[0].0.as_str(),
                all[0].1["fileName"].as_str(),
                all[0].1["code"].as_str()
            ),
            (
                "efv://document-open-failed",
                Some("missing-mail.eml"),
                Some("unreadable")
            )
        );
        assert_eq!(all[1].1["fileName"], "ok.eml");
        assert_eq!(
            all[2].1,
            serde_json::json!({ "batchId": 1, "opened": 1, "failed": 1 })
        );
        // 失败事件里只有文件名，没有补全后的完整路径
        assert!(!all[0].1.to_string().contains("/work"), "{}", all[0].1);
    }

    #[tokio::test]
    async fn an_empty_drop_or_empty_dialog_result_produces_no_events() {
        let (coordinator, events, _service) = coordinator();
        coordinator.app_ready();
        coordinator.submit(vec![]);
        sleep(Duration::from_millis(300)).await;
        assert!(events.lock().unwrap().is_empty());
    }

    #[tokio::test]
    async fn batches_are_processed_in_submission_order() {
        let (coordinator, events, _service) = coordinator();
        coordinator.app_ready();
        coordinator.submit(files(&["a-slow-300.eml"]));
        coordinator.submit(files(&["b.eml"]));
        let all = wait_for(&events, 4).await;
        let order: Vec<_> = all
            .iter()
            .filter(|(n, _)| n == "efv://document-opened")
            .map(|(_, v)| v["fileName"].as_str().unwrap().to_owned())
            .collect();
        assert_eq!(order, ["a-slow-300.eml", "b.eml"]);
    }
}
