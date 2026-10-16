//! 用假 sidecar（`examples/fake_sidecar.rs`，一个真实的子进程）测试监管逻辑。
//! 假 sidecar 由 `cargo test` 一起构建；只跑单个测试目标时先 `cargo build --example fake_sidecar`。

use std::path::{Path, PathBuf};
use std::sync::{Mutex, Once};
use std::time::{Duration, Instant};

use serde_json::{Value, json};
use tokio::time::sleep;

use super::*;

// ---------------------------------------------------------------- 辅助

fn fake_program() -> PathBuf {
    let exe = std::env::current_exe().unwrap();
    // target/debug/deps/<测试程序> -> target/debug/examples/fake_sidecar
    let program = exe
        .parent()
        .and_then(Path::parent)
        .unwrap()
        .join("examples")
        .join(format!("fake_sidecar{}", std::env::consts::EXE_SUFFIX));
    assert!(
        program.exists(),
        "{} is missing: run `cargo test` (it builds examples) or `cargo build --example fake_sidecar`",
        program.display()
    );
    program
}

fn timeouts() -> Timeouts {
    Timeouts {
        handshake: Duration::from_secs(5),
        ping: Duration::from_secs(5),
        open: Duration::from_secs(5),
        close: Duration::from_secs(5),
        save: Duration::from_secs(5),
        save_all: Duration::from_secs(5),
        shutdown_grace: Duration::from_secs(2),
    }
}

fn service_with(envs: &[(&str, &str)], timeouts: Timeouts) -> SidecarService {
    let mut command = SidecarCommand::stdio(fake_program());
    command.envs = envs
        .iter()
        .map(|(k, v)| ((*k).into(), (*v).into()))
        .collect();
    SidecarService::new(SidecarConfig { command, timeouts })
}

fn service() -> SidecarService {
    service_with(&[], timeouts())
}

#[cfg(unix)]
fn process_exists(pid: u32) -> bool {
    use std::process::{Command, Stdio};
    // 进程被回收之后才会失败；僵尸进程仍然存在
    Command::new("kill")
        .args(["-0", &pid.to_string()])
        .stderr(Stdio::null())
        .status()
        .is_ok_and(|status| status.success())
}

/// 捕获 `log` 输出，检查 sidecar 的 stderr 是否被转发。
static LOGS: Mutex<Vec<String>> = Mutex::new(Vec::new());
struct CaptureLogger;
impl log::Log for CaptureLogger {
    fn enabled(&self, _: &log::Metadata) -> bool {
        true
    }
    fn log(&self, record: &log::Record) {
        LOGS.lock().unwrap().push(format!(
            "{}|{}|{}",
            record.level(),
            record.target(),
            record.args()
        ));
    }
    fn flush(&self) {}
}
fn capture_logs() {
    static ONCE: Once = Once::new();
    ONCE.call_once(|| {
        log::set_logger(&CaptureLogger).unwrap();
        log::set_max_level(log::LevelFilter::Debug);
    });
}
async fn wait_for_log(needle: &str) -> bool {
    for _ in 0..50 {
        if LOGS
            .lock()
            .unwrap()
            .iter()
            .any(|line| line.contains(needle))
        {
            return true;
        }
        sleep(Duration::from_millis(40)).await;
    }
    false
}

/// 假 sidecar 里当前打开的文档：docId -> 来源。
async fn fake_docs(service: &SidecarService) -> Vec<String> {
    let value = service.raw("fake.docs", json!({})).await.unwrap();
    let mut sources: Vec<String> = value["docs"]
        .as_object()
        .unwrap()
        .values()
        .map(|v| v.as_str().unwrap().to_owned())
        .collect();
    sources.sort();
    sources
}

async fn crash(service: &SidecarService) {
    let error = service
        .open_file(Path::new("/mail/crash.eml"))
        .await
        .unwrap_err();
    assert!(error.is_crash(), "{error}");
}

fn mail(name: &str) -> PathBuf {
    PathBuf::from(format!("/mail/{name}"))
}

// ---------------------------------------------------------------- 往返

#[tokio::test]
async fn nothing_starts_until_the_first_request() {
    let service = service();
    assert!(service.current_process().await.is_none());
    service.open_file(&mail("a.eml")).await.unwrap();
    assert_eq!(service.current_process().await.unwrap().0, 1);
}

#[tokio::test]
async fn every_method_round_trips() {
    let service = service();
    let opened = service.open_file(&mail("a.eml")).await.unwrap();
    assert_eq!(opened.document["source"], "/mail/a.eml");
    let pid = service.current_process().await.unwrap().1.unwrap();

    let nested = service.open_nested(opened.handle, "att1").await.unwrap();
    assert_ne!(nested.handle, opened.handle);
    assert_eq!(nested.document["source"], "/mail/a.eml>att1");

    let saved = service
        .save_attachment(opened.handle, "att1", Path::new("/out/x.pdf"))
        .await
        .unwrap();
    assert_eq!(
        saved,
        PathBuf::from(format!("/out/x.pdf|/mail/a.eml|pid{pid}"))
    );

    let all = service
        .save_all_attachments(nested.handle, Path::new("/out"))
        .await
        .unwrap();
    assert_eq!(
        all.saved,
        vec![SavedAttachment {
            attachment_id: "a1".into(),
            path: "/out//mail/a.eml>att1".into()
        }]
    );
    assert_eq!(
        all.failed,
        vec![FailedAttachment {
            attachment_id: "a2".into(),
            code: "write_failed".into(),
            message: "disk full".into()
        }]
    );

    let extracted = service
        .extract_for_open(opened.handle, "report.pdf")
        .await
        .unwrap();
    assert_eq!(
        extracted,
        PathBuf::from(format!("/tmp/fake/{pid}//mail/a.eml/report.pdf"))
    );

    assert_eq!(fake_docs(&service).await.len(), 2);
    service.close_document(nested.handle).await.unwrap();
    service.close_document(opened.handle).await.unwrap();
    assert!(fake_docs(&service).await.is_empty());
    assert_eq!(service.open_handles(), 0);
}

#[tokio::test]
async fn sidecar_errors_pass_through_with_their_codes() {
    let service = service();
    let code_of = |error: SidecarError| error.code().to_owned();
    assert_eq!(
        code_of(service.open_file(&mail("missing.eml")).await.unwrap_err()),
        "unreadable"
    );
    assert_eq!(
        code_of(service.open_file(&mail("corrupt.eml")).await.unwrap_err()),
        "corrupted"
    );
    let a = service.open_file(&mail("a.eml")).await.unwrap().handle;
    assert_eq!(
        code_of(service.open_nested(a, "notnested").await.unwrap_err()),
        "not_nested_email"
    );
    assert_eq!(
        code_of(service.extract_for_open(a, "setup.exe").await.unwrap_err()),
        "executable_refused"
    );
    let error = service.raw("no.such.method", json!({})).await.unwrap_err();
    assert_eq!(
        error,
        SidecarError::remote("method_not_found", "unknown method no.such.method")
    );
    // 这些错误都不影响 sidecar 继续工作
    assert_eq!(service.current_process().await.unwrap().0, 1);
    // 打开失败不会留下句柄
    assert_eq!(service.open_handles(), 1);
}

#[tokio::test]
async fn unknown_and_closed_handles_are_rejected() {
    let service = service();
    let a = service.open_file(&mail("a.eml")).await.unwrap().handle;
    assert_eq!(
        service
            .save_attachment(DocHandle(999), "x", Path::new("/o"))
            .await
            .unwrap_err(),
        SidecarError::UnknownHandle
    );
    service.close_document(a).await.unwrap();
    assert_eq!(
        service
            .save_attachment(a, "x", Path::new("/o"))
            .await
            .unwrap_err(),
        SidecarError::UnknownHandle
    );
    assert_eq!(
        service.close_document(a).await.unwrap_err(),
        SidecarError::UnknownHandle
    );
    assert_eq!(
        service.open_nested(a, "x").await.unwrap_err(),
        SidecarError::UnknownHandle
    );
}

#[cfg(unix)]
#[tokio::test]
async fn paths_that_are_not_utf8_are_rejected_before_reaching_the_sidecar() {
    use std::ffi::OsStr;
    use std::os::unix::ffi::OsStrExt;
    let service = service();
    let error = service
        .open_file(Path::new(OsStr::from_bytes(b"/mail/\xff.eml")))
        .await
        .unwrap_err();
    assert_eq!(error.code(), "invalid_params");
    assert!(
        service.current_process().await.is_none(),
        "no need to start the sidecar for that"
    );
}

// ---------------------------------------------------------------- 并发与乱序

#[tokio::test]
async fn concurrent_requests_are_matched_by_id_when_replies_arrive_out_of_order() {
    let service = service();
    let a = service.open_file(&mail("a.eml")).await.unwrap().handle;
    let order = Mutex::new(Vec::new());
    let finished = |name: &'static str| order.lock().unwrap().push(name);

    let (one, two, three) = tokio::join!(
        async {
            let r = service
                .save_attachment(a, "x", Path::new("/t/one-slow-500"))
                .await;
            finished("one");
            r
        },
        async {
            let r = service
                .save_attachment(a, "x", Path::new("/t/two-slow-200"))
                .await;
            finished("two");
            r
        },
        async {
            sleep(Duration::from_millis(50)).await;
            let r = service.save_attachment(a, "x", Path::new("/t/three")).await;
            finished("three");
            r
        },
    );

    // 响应的顺序和请求的顺序相反，但每个请求拿到的是自己的响应
    assert_eq!(*order.lock().unwrap(), ["three", "two", "one"]);
    assert!(one.unwrap().starts_with("/t/one-slow-500|"));
    assert!(two.unwrap().starts_with("/t/two-slow-200|"));
    assert!(three.unwrap().starts_with("/t/three|"));
    assert_eq!(service.current_connection().await.unwrap().pending_len(), 0);
}

#[tokio::test]
async fn request_ids_increase_and_never_repeat() {
    let service = service();
    let a = service.open_file(&mail("a.eml")).await.unwrap().handle;
    let before = service.requests_sent();
    for i in 0..5 {
        service
            .save_attachment(a, "x", Path::new(&format!("/t/{i}")))
            .await
            .unwrap();
    }
    assert_eq!(service.requests_sent(), before + 5);
}

#[tokio::test]
async fn lines_that_are_not_valid_replies_are_ignored() {
    capture_logs();
    let service = service();
    // 回复之前，sidecar 先输出了非 JSON 行、非对象、未知 id 和 id 为 null 的行
    let opened = service.open_file(&mail("garbage.eml")).await.unwrap();
    assert_eq!(opened.document["source"], "/mail/garbage.eml");
    assert!(
        service
            .save_attachment(opened.handle, "x", Path::new("/t/y"))
            .await
            .is_ok()
    );
    assert!(wait_for_log("ignoring a non-JSON line").await);
    assert_eq!(
        service.current_process().await.unwrap().0,
        1,
        "nothing was restarted"
    );
}

// ---------------------------------------------------------------- 超时

#[tokio::test]
async fn a_request_that_never_gets_a_reply_times_out_and_leaves_the_pending_table() {
    let service = service_with(
        &[],
        Timeouts {
            open: Duration::from_millis(300),
            ..timeouts()
        },
    );
    let started = Instant::now();
    let error = service.open_file(&mail("hang.eml")).await.unwrap_err();
    assert_eq!(error.code(), "timeout");
    assert!(
        matches!(&error, SidecarError::Timeout { method, .. } if method == "email.open"),
        "{error}"
    );
    assert!(
        started.elapsed() >= Duration::from_millis(300)
            && started.elapsed() < Duration::from_secs(3)
    );
    assert_eq!(service.current_connection().await.unwrap().pending_len(), 0);
    assert_eq!(service.open_handles(), 0);
}

#[cfg(unix)]
#[tokio::test]
async fn a_parse_that_times_out_restarts_the_sidecar_and_open_documents_survive() {
    let service = service_with(
        &[],
        Timeouts {
            open: Duration::from_millis(300),
            ..timeouts()
        },
    );
    let a = service.open_file(&mail("a.eml")).await.unwrap().handle;
    let (_, old_pid) = service.current_process().await.unwrap();

    // 卡住的解析无法取消，所以超时后 sidecar 被结束；同一时刻进行中的其他请求以崩溃返回
    let hanging = mail("hang.eml");
    let (hung, in_flight) = tokio::join!(
        service.open_file(&hanging),
        service.save_attachment(a, "x", Path::new("/t/slow-3000")),
    );
    assert_eq!(hung.unwrap_err().code(), "timeout");
    let in_flight = in_flight.unwrap_err();
    assert!(in_flight.is_crash(), "{in_flight}");
    assert!(
        in_flight.message().contains("email.open timed out"),
        "{in_flight}"
    );

    // 下次请求重启 sidecar；已经打开的文档按需恢复
    let saved = service
        .save_attachment(a, "x", Path::new("/t/after"))
        .await
        .unwrap();
    let (generation, new_pid) = service.current_process().await.unwrap();
    assert_eq!(generation, 2);
    assert_ne!(old_pid, new_pid);
    assert!(
        saved
            .to_str()
            .unwrap()
            .starts_with("/t/after|/mail/a.eml|pid")
    );
    assert!(
        !process_exists(old_pid.unwrap()),
        "the hung process was killed and reaped"
    );
}

#[tokio::test]
async fn each_method_uses_its_own_timeout() {
    let service = service_with(
        &[],
        Timeouts {
            save: Duration::from_millis(200),
            ..timeouts()
        },
    );
    let a = service.open_file(&mail("a.eml")).await.unwrap().handle; // 打开用 5 秒的超时，不受影响
    // 同样慢的请求：保存超时，打开不超时
    let slow_save = service
        .save_attachment(a, "x", Path::new("/t/slow-600"))
        .await
        .unwrap_err();
    assert_eq!(slow_save.code(), "timeout");
    assert!(service.open_file(&mail("slow-600.eml")).await.is_ok());
    // 保存超时不结束 sidecar：它正在往用户的文件夹里写文件
    assert_eq!(service.current_process().await.unwrap().0, 1);
}

#[tokio::test]
async fn a_reply_that_arrives_after_the_timeout_is_ignored() {
    let service = service_with(
        &[],
        Timeouts {
            save: Duration::from_millis(200),
            ..timeouts()
        },
    );
    let a = service.open_file(&mail("a.eml")).await.unwrap().handle;
    let error = service
        .save_attachment(a, "x", Path::new("/t/late-slow-600"))
        .await
        .unwrap_err();
    assert_eq!(error.code(), "timeout");
    sleep(Duration::from_millis(700)).await; // 迟到的响应到了
    assert_eq!(service.current_connection().await.unwrap().pending_len(), 0);
    let ok = service
        .save_attachment(a, "x", Path::new("/t/next"))
        .await
        .unwrap();
    assert!(
        ok.starts_with("/t/next|"),
        "the late reply must not be mistaken for this one: {}",
        ok.display()
    );
}

#[tokio::test]
async fn dropping_a_request_future_removes_it_from_the_pending_table() {
    let service = service();
    let a = service.open_file(&mail("a.eml")).await.unwrap().handle;
    let abandoned = tokio::time::timeout(
        Duration::from_millis(100),
        service.save_attachment(a, "x", Path::new("/t/slow-800")),
    )
    .await;
    assert!(abandoned.is_err());
    assert_eq!(service.current_connection().await.unwrap().pending_len(), 0);
}

// ---------------------------------------------------------------- 崩溃与重启

#[tokio::test]
async fn a_crash_fails_every_request_in_flight_at_once() {
    let service = service();
    let a = service.open_file(&mail("a.eml")).await.unwrap().handle;
    let b = service.open_file(&mail("b.eml")).await.unwrap().handle;

    let started = Instant::now();
    let (save, save_all, extract, crashing_open) = tokio::join!(
        service.save_attachment(a, "x", Path::new("/t/slow-4000")),
        service.save_all_attachments(b, Path::new("/t/slow-4000")),
        service.extract_for_open(a, "slow-4000.pdf"),
        async {
            sleep(Duration::from_millis(150)).await;
            service.open_file(&mail("crash.eml")).await
        },
    );

    for error in [
        save.unwrap_err(),
        save_all.unwrap_err(),
        extract.unwrap_err(),
        crashing_open.unwrap_err(),
    ] {
        assert!(error.is_crash(), "{error}");
        assert_eq!(error.code(), "sidecar_crashed");
    }
    // 是进程退出让它们返回的，而不是等超时
    assert!(
        started.elapsed() < Duration::from_secs(3),
        "{:?}",
        started.elapsed()
    );
    assert_eq!(service.current_connection().await.unwrap().pending_len(), 0);
}

#[cfg(unix)]
#[tokio::test]
async fn the_next_request_restarts_the_sidecar_and_handshakes_again() {
    let service = service();
    service.open_file(&mail("a.eml")).await.unwrap();
    let (gen1, pid1) = service.current_process().await.unwrap();
    crash(&service).await;

    // 崩溃之后、下一次请求之前，不会自己重启
    assert!(!service.current_connection().await.unwrap().is_alive());
    assert_eq!(service.current_process().await.unwrap().0, gen1);

    let opened = service.open_file(&mail("b.eml")).await.unwrap();
    let (gen2, pid2) = service.current_process().await.unwrap();
    assert_eq!(gen2, gen1 + 1);
    assert_ne!(pid1, pid2);
    assert_eq!(opened.document["pid"], json!(pid2.unwrap()));
    assert!(
        !process_exists(pid1.unwrap()),
        "the crashed process was reaped"
    );
}

#[tokio::test]
async fn the_file_that_crashed_the_sidecar_is_not_retried() {
    let service = service();
    let error = service.open_file(&mail("crash.eml")).await.unwrap_err();
    assert!(error.is_crash());
    // 如果自动重试了，这里已经是第二代，并且又崩了一次
    let (generation, _) = service.current_process().await.unwrap();
    assert_eq!(generation, 1);
    assert_eq!(service.open_handles(), 0);

    // 用户再次打开同一个文件，是一次新的请求
    let again = service.open_file(&mail("crash.eml")).await.unwrap_err();
    assert!(again.is_crash());
    assert_eq!(service.current_process().await.unwrap().0, 2);
}

// ---------------------------------------------------------------- 崩溃后恢复已打开的文档

#[cfg(unix)]
#[tokio::test]
async fn an_old_top_level_handle_is_reopened_transparently_and_only_once() {
    let service = service();
    let a = service.open_file(&mail("a.eml")).await.unwrap().handle;
    let (_, old_pid) = service.current_process().await.unwrap();
    crash(&service).await;

    // 三个并发请求同时用旧句柄：只重新打开一次
    let (saved, saved_again, extracted) = tokio::join!(
        service.save_attachment(a, "x", Path::new("/t/1")),
        service.save_attachment(a, "x", Path::new("/t/2")),
        service.extract_for_open(a, "report.pdf"),
    );
    let (_, new_pid) = service.current_process().await.unwrap();
    assert_ne!(old_pid, new_pid);
    let new_pid = new_pid.unwrap();
    assert_eq!(
        saved.unwrap(),
        PathBuf::from(format!("/t/1|/mail/a.eml|pid{new_pid}"))
    );
    assert_eq!(
        saved_again.unwrap(),
        PathBuf::from(format!("/t/2|/mail/a.eml|pid{new_pid}"))
    );
    assert!(
        extracted
            .unwrap()
            .to_str()
            .unwrap()
            .ends_with("/mail/a.eml/report.pdf")
    );
    assert_eq!(fake_docs(&service).await, ["/mail/a.eml"]);

    // 恢复之后句柄就在新的一代里，再用不会重新打开
    service
        .save_attachment(a, "x", Path::new("/t/3"))
        .await
        .unwrap();
    assert_eq!(fake_docs(&service).await, ["/mail/a.eml"]);
}

#[cfg(unix)]
#[tokio::test]
async fn nested_handles_are_recovered_through_their_parents() {
    let service = service();
    let a = service.open_file(&mail("a.eml")).await.unwrap().handle;
    let n = service.open_nested(a, "att1").await.unwrap().handle;
    let nn = service.open_nested(n, "att2").await.unwrap().handle;
    crash(&service).await;

    // 只用最深的一层，整条链都被重新打开
    let saved = service
        .save_attachment(nn, "x", Path::new("/t/out"))
        .await
        .unwrap();
    let pid = service.current_process().await.unwrap().1.unwrap();
    assert_eq!(
        saved,
        PathBuf::from(format!("/t/out|/mail/a.eml>att1>att2|pid{pid}"))
    );
    assert_eq!(
        fake_docs(&service).await,
        ["/mail/a.eml", "/mail/a.eml>att1", "/mail/a.eml>att1>att2"]
    );

    // 父级随后也在新的一代里，不会再被重新打开
    service
        .save_attachment(a, "x", Path::new("/t/parent"))
        .await
        .unwrap();
    service
        .save_attachment(n, "x", Path::new("/t/middle"))
        .await
        .unwrap();
    assert_eq!(fake_docs(&service).await.len(), 3);
}

#[cfg(unix)]
#[tokio::test]
async fn a_nested_document_can_be_recovered_even_after_its_parent_handle_was_closed() {
    let service = service();
    let a = service.open_file(&mail("a.eml")).await.unwrap().handle;
    let n = service.open_nested(a, "att1").await.unwrap().handle;

    // 父句柄先关闭：前端不能再用它，但 sidecar 里的父文档保留到子文档关闭
    service.close_document(a).await.unwrap();
    assert_eq!(service.open_handles(), 1);
    assert_eq!(
        fake_docs(&service).await,
        ["/mail/a.eml", "/mail/a.eml>att1"]
    );
    assert_eq!(
        service
            .save_attachment(a, "x", Path::new("/t/z"))
            .await
            .unwrap_err(),
        SidecarError::UnknownHandle
    );

    crash(&service).await;
    let saved = service
        .save_attachment(n, "x", Path::new("/t/out"))
        .await
        .unwrap();
    assert!(
        saved
            .to_str()
            .unwrap()
            .starts_with("/t/out|/mail/a.eml>att1|pid")
    );
    assert_eq!(
        fake_docs(&service).await,
        ["/mail/a.eml", "/mail/a.eml>att1"]
    );

    // 子文档关闭后，保留的父文档也随之关闭
    service.close_document(n).await.unwrap();
    assert!(fake_docs(&service).await.is_empty());
}

#[tokio::test]
async fn closing_a_child_first_leaves_the_parent_open_until_it_is_closed_too() {
    let service = service();
    let a = service.open_file(&mail("a.eml")).await.unwrap().handle;
    let n = service.open_nested(a, "att1").await.unwrap().handle;
    service.close_document(n).await.unwrap();
    assert_eq!(fake_docs(&service).await, ["/mail/a.eml"]);
    assert!(
        service
            .save_attachment(a, "x", Path::new("/t/z"))
            .await
            .is_ok()
    );
    service.close_document(a).await.unwrap();
    assert!(fake_docs(&service).await.is_empty());
}

#[tokio::test]
async fn closing_a_document_after_a_crash_does_not_start_the_sidecar() {
    let service = service();
    let a = service.open_file(&mail("a.eml")).await.unwrap().handle;
    crash(&service).await;
    service.close_document(a).await.unwrap();
    assert_eq!(service.open_handles(), 0);
    assert_eq!(
        service.current_process().await.unwrap().0,
        1,
        "no new sidecar just to close a document that is already gone"
    );
}

#[cfg(unix)]
#[tokio::test]
async fn a_malformed_file_that_crashes_the_sidecar_does_not_take_other_tabs_down() {
    let poison = std::env::temp_dir().join(format!(
        "efv-fake-poison-{}-{}",
        std::process::id(),
        line!()
    ));
    let _ = std::fs::remove_file(&poison);
    let service = service_with(
        &[("FAKE_POISON_FILE", poison.to_str().unwrap())],
        timeouts(),
    );

    // 两个标签页，都是第一次能正常打开的文件
    let good = service.open_file(&mail("good.eml")).await.unwrap().handle;
    let bad = service.open_file(&mail("bad.eml")).await.unwrap().handle;
    // 之后 bad.eml 一旦被重新解析就会让 sidecar 崩溃；先用另一个文件让 sidecar 崩一次，触发恢复
    std::fs::write(&poison, "bad.eml\n").unwrap();
    crash(&service).await;

    // 其他标签页：保存附件照常可用
    let saved = service
        .save_attachment(good, "x", Path::new("/t/good"))
        .await
        .unwrap();
    assert!(
        saved
            .to_str()
            .unwrap()
            .starts_with("/t/good|/mail/good.eml|pid")
    );

    // 有问题的那个标签页：重新打开时 sidecar 又崩了，请求失败，且不会自己循环重试
    let error = service
        .save_attachment(bad, "x", Path::new("/t/bad"))
        .await
        .unwrap_err();
    assert!(error.is_crash(), "{error}");
    let (generation_after_failure, _) = service.current_process().await.unwrap();
    sleep(Duration::from_millis(300)).await;
    assert_eq!(
        service.current_process().await.unwrap().0,
        generation_after_failure
    );
    assert!(!service.current_connection().await.unwrap().is_alive());

    // 崩溃没有波及好的文档：下一次请求重启 sidecar，好文档再次被恢复
    let again = service
        .save_attachment(good, "x", Path::new("/t/good2"))
        .await
        .unwrap();
    assert!(
        again
            .to_str()
            .unwrap()
            .starts_with("/t/good2|/mail/good.eml|pid")
    );
    let _ = std::fs::remove_file(&poison);
}

// ---------------------------------------------------------------- 启动失败

#[tokio::test]
async fn a_sidecar_that_never_answers_the_ping_is_reported_as_unavailable() {
    let service = service_with(
        &[("FAKE_NO_PING", "1")],
        Timeouts {
            handshake: Duration::from_millis(300),
            ..timeouts()
        },
    );
    let started = Instant::now();
    let error = service.open_file(&mail("a.eml")).await.unwrap_err();
    assert_eq!(error.code(), "sidecar_unavailable");
    assert!(error.message().contains("handshake"), "{error}");
    assert!(started.elapsed() < Duration::from_secs(4));
    assert!(service.current_process().await.is_none());
}

#[tokio::test]
async fn a_sidecar_that_exits_during_startup_is_reported_as_unavailable() {
    let service = service_with(&[("FAKE_CRASH_ON_START", "1")], timeouts());
    let error = service.open_file(&mail("a.eml")).await.unwrap_err();
    assert_eq!(error.code(), "sidecar_unavailable");
    assert!(service.current_process().await.is_none());
}

#[tokio::test]
async fn an_unsupported_protocol_version_is_refused() {
    let service = service_with(&[("FAKE_PROTOCOL_VERSION", "2")], timeouts());
    let error = service.open_file(&mail("a.eml")).await.unwrap_err();
    assert_eq!(error.code(), "sidecar_unavailable");
    assert!(error.message().contains("protocol version 2"), "{error}");
}

#[tokio::test]
async fn a_missing_sidecar_program_is_reported_as_unavailable() {
    let service = SidecarService::new(SidecarConfig::new(SidecarCommand::stdio(
        "/definitely/not/here/efv-sidecar",
    )));
    let error = service.open_file(&mail("a.eml")).await.unwrap_err();
    assert_eq!(error.code(), "sidecar_unavailable");
    assert!(error.message().contains("failed to start"), "{error}");
}

// ---------------------------------------------------------------- 日志与退出

#[tokio::test]
async fn sidecar_stderr_is_forwarded_to_the_debug_log() {
    capture_logs();
    let service = service();
    service.open_file(&mail("logged.eml")).await.unwrap();
    assert!(wait_for_log("DEBUG|efv::sidecar|").await);
    assert!(wait_for_log("fake-sidecar: handling email.open").await);
}

#[cfg(unix)]
#[tokio::test]
async fn shutdown_closes_stdin_and_the_process_is_reaped() {
    capture_logs();
    let service = service();
    service.open_file(&mail("a.eml")).await.unwrap();
    let pid = service.current_process().await.unwrap().1.unwrap();
    assert!(process_exists(pid));

    let started = Instant::now();
    service.shutdown().await;
    assert!(
        !process_exists(pid),
        "the process must be gone after shutdown returns"
    );
    // 是它自己读到 EOF 退出的，不是被强制结束
    assert!(
        started.elapsed() < Duration::from_secs(2),
        "{:?}",
        started.elapsed()
    );
    assert!(wait_for_log("fake-sidecar: stdin closed").await);

    let error = service.open_file(&mail("b.eml")).await.unwrap_err();
    assert_eq!(error.code(), "sidecar_unavailable");
}

#[cfg(unix)]
#[tokio::test]
async fn shutdown_kills_a_sidecar_that_ignores_the_closed_stdin() {
    let service = service_with(
        &[("FAKE_IGNORE_EOF", "1")],
        Timeouts {
            shutdown_grace: Duration::from_millis(400),
            ..timeouts()
        },
    );
    service.open_file(&mail("a.eml")).await.unwrap();
    let pid = service.current_process().await.unwrap().1.unwrap();

    let started = Instant::now();
    service.shutdown().await;
    let elapsed = started.elapsed();
    assert!(
        elapsed >= Duration::from_millis(400) && elapsed < Duration::from_secs(3),
        "{elapsed:?}"
    );
    assert!(
        !process_exists(pid),
        "the stuck process was killed and reaped"
    );
}

#[cfg(unix)]
#[tokio::test]
async fn shutdown_lets_requests_in_flight_finish_before_the_process_exits() {
    let service = service();
    let a = service.open_file(&mail("a.eml")).await.unwrap().handle;
    let pid = service.current_process().await.unwrap().1.unwrap();
    let (saved, ()) = tokio::join!(
        service.save_attachment(a, "x", Path::new("/t/last-slow-400")),
        async {
            sleep(Duration::from_millis(100)).await;
            service.shutdown().await;
        }
    );
    // sidecar 先处理完已经收到的请求再退出
    assert!(
        saved
            .unwrap()
            .to_str()
            .unwrap()
            .starts_with("/t/last-slow-400|")
    );
    assert!(!process_exists(pid));
}

#[tokio::test]
async fn shutdown_without_a_running_sidecar_does_nothing() {
    let service = service();
    service.shutdown().await;
    assert!(service.current_process().await.is_none());
}

#[cfg(unix)]
#[tokio::test]
async fn shutting_down_after_a_crash_is_fine() {
    let service = service();
    crash(&service).await;
    service.shutdown().await;
}

// ---------------------------------------------------------------- 配置

#[test]
fn the_bundled_sidecar_is_next_to_the_main_executable() {
    let command = SidecarCommand::bundled().unwrap();
    let exe = std::env::current_exe().unwrap();
    assert_eq!(command.program.parent(), exe.parent());
    assert_eq!(
        command.program.file_name().unwrap().to_str().unwrap(),
        format!("efv-sidecar{}", std::env::consts::EXE_SUFFIX)
    );
    assert_eq!(command.args, ["--stdio"]);
}

// ---------------------------------------------------------------- 真实的 sidecar

fn sample(name: &str) -> PathBuf {
    Path::new(env!("CARGO_MANIFEST_DIR"))
        .join("../samples")
        .join(name)
        .canonicalize()
        .unwrap()
}

fn attachments(document: &Value) -> Vec<Value> {
    document["attachments"].as_array().unwrap().clone()
}

/// 对真实 sidecar 的冒烟测试：ping，打开样本，保存、全部保存、提取、打开嵌套邮件，
/// 再把 sidecar 进程强行杀掉，确认旧句柄透明恢复，最后关闭并回收。
///
/// 需要先 `node scripts/build-sidecar.mjs`，再 `cargo test -- --ignored real_sidecar`。
/// sidecar 路径默认是 tauri-build 复制到 `target/<profile>/efv-sidecar` 的那份，也可以用 `EFV_SIDECAR_PATH` 指定。
#[cfg(unix)]
#[tokio::test]
#[ignore = "needs the real sidecar: node scripts/build-sidecar.mjs, then cargo test -- --ignored real_sidecar"]
async fn real_sidecar_speaks_the_protocol() {
    let program = std::env::var_os("EFV_SIDECAR_PATH")
        .map(PathBuf::from)
        .unwrap_or_else(|| {
            let exe = std::env::current_exe().unwrap();
            exe.parent()
                .and_then(Path::parent)
                .unwrap()
                .join(format!("efv-sidecar{}", std::env::consts::EXE_SUFFIX))
        });
    assert!(program.exists(), "{} does not exist", program.display());
    let service = SidecarService::new(SidecarConfig::new(SidecarCommand::stdio(&program)));
    let out = std::env::temp_dir().join(format!("efv-smoke-{}", std::process::id()));
    std::fs::create_dir_all(&out).unwrap();

    // 就绪握手
    let ping = service.raw("system.ping", json!({})).await.unwrap();
    assert_eq!(ping["protocolVersion"], json!(PROTOCOL_VERSION));
    let (_, pid) = service.current_process().await.unwrap();
    let pid = pid.unwrap();

    // 打开、关闭
    let headers = service
        .open_file(&sample("headers-normal.eml"))
        .await
        .unwrap();
    assert!(
        headers.document["subject"].is_string(),
        "{}",
        headers.document
    );
    println!(
        "opened headers-normal.eml, subject: {}",
        headers.document["subject"]
    );
    service.close_document(headers.handle).await.unwrap();
    assert_eq!(
        service
            .save_attachment(headers.handle, "x", &out)
            .await
            .unwrap_err(),
        SidecarError::UnknownHandle
    );

    // 出错时的错误码
    let missing = service.open_file(&out.join("nope.eml")).await.unwrap_err();
    assert_eq!(missing.code(), "unreadable", "{missing}");
    let not_mail = service
        .open_file(&sample("../README.md"))
        .await
        .unwrap_err();
    assert_eq!(not_mail.code(), "unsupported_format", "{not_mail}");

    // 保存、全部保存、提取
    let mail = service
        .open_file(&sample("attachments-pdf-xlsx-jpg.eml"))
        .await
        .unwrap();
    let list = attachments(&mail.document);
    assert!(list.len() >= 2, "{}", mail.document);
    let first = &list[0];
    let target = out.join("first.bin");
    let saved = service
        .save_attachment(mail.handle, first["id"].as_str().unwrap(), &target)
        .await
        .unwrap();
    assert_eq!(
        std::fs::metadata(&saved).unwrap().len(),
        first["size"].as_u64().unwrap()
    );
    let all_dir = out.join("all");
    std::fs::create_dir_all(&all_dir).unwrap();
    let all = service
        .save_all_attachments(mail.handle, &all_dir)
        .await
        .unwrap();
    assert_eq!(
        (all.saved.len(), all.failed.len()),
        (list.len(), 0),
        "{all:?}"
    );
    let extracted = service
        .extract_for_open(mail.handle, first["id"].as_str().unwrap())
        .await
        .unwrap();
    assert!(extracted.exists(), "{}", extracted.display());

    // 可执行附件只能保存
    let exe_mail = service
        .open_file(&sample("attachments-executables.eml"))
        .await
        .unwrap();
    let exe = attachments(&exe_mail.document)
        .into_iter()
        .find(|a| a["fileName"].as_str().is_some_and(|n| n.ends_with(".exe")))
        .unwrap();
    let refused = service
        .extract_for_open(exe_mail.handle, exe["id"].as_str().unwrap())
        .await
        .unwrap_err();
    assert_eq!(refused.code(), "executable_refused", "{refused}");

    // 嵌套邮件
    let outer = service
        .open_file(&sample("attachment-eml-file.eml"))
        .await
        .unwrap();
    let nested_attachment = attachments(&outer.document)
        .into_iter()
        .find(|a| a["isNestedEmail"] == json!(true))
        .expect("a nested email attachment");
    let nested = service
        .open_nested(outer.handle, nested_attachment["id"].as_str().unwrap())
        .await
        .unwrap();
    assert!(nested.document.is_object());

    // 强行杀掉 sidecar：旧句柄（顶层和嵌套）在下次使用时透明恢复
    assert!(
        std::process::Command::new("kill")
            .args(["-9", &pid.to_string()])
            .status()
            .unwrap()
            .success()
    );
    sleep(Duration::from_millis(300)).await;
    let again = service
        .save_attachment(
            mail.handle,
            first["id"].as_str().unwrap(),
            &out.join("again.bin"),
        )
        .await
        .unwrap();
    assert_eq!(
        std::fs::metadata(&again).unwrap().len(),
        first["size"].as_u64().unwrap()
    );
    let (generation, new_pid) = service.current_process().await.unwrap();
    assert_eq!(generation, 2);
    assert_ne!(new_pid, Some(pid));
    let nested_all = service
        .save_all_attachments(nested.handle, &all_dir)
        .await
        .unwrap();
    println!(
        "after the restart: nested handle recovered, save_all -> {} saved / {} failed",
        nested_all.saved.len(),
        nested_all.failed.len()
    );

    // 退出：stdin 关闭，sidecar 清理后自己退出，进程被回收
    let started = Instant::now();
    service.shutdown().await;
    println!("shutdown took {:?}", started.elapsed());
    assert!(!process_exists(new_pid.unwrap()));
    let _ = std::fs::remove_dir_all(&out);
}
