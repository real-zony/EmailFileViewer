//! 一个 sidecar 进程（一代）的连接：启动进程，按行收发 JSON，用待处理表按 id 匹配响应。
//!
//! 进程退出（或崩溃）时，这一代的所有进行中的请求以 [`SidecarError::Crashed`] 返回，连接此后不可用。
//! 重启由上层（`service.rs`）负责：丢弃旧连接，创建新的一代。

use std::collections::HashMap;
use std::ffi::OsString;
use std::path::PathBuf;
use std::process::Stdio;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex};
use std::time::Duration;

use serde_json::Value;
use tokio::io::{AsyncBufRead, AsyncBufReadExt, AsyncReadExt, AsyncWriteExt, BufReader};
use tokio::process::{ChildStderr, ChildStdin, ChildStdout, Command};
use tokio::sync::{mpsc, oneshot, watch};
use tokio::task::JoinHandle;
use tokio::time::timeout;

use super::protocol::{Result, SidecarError, code};

/// 日志里一行最多保留多少字符，避免异常输出刷屏。
const LOG_LINE_LIMIT: usize = 300;
/// stdout 单行（一条协议消息）的长度上限，字节数。sidecar 的正文和内联图片本身有上限，远小于它；
/// 超过就说明协议已经不可信（或者 sidecar 失控了），不能让 Rust 端的内存跟着涨。
pub(crate) const MAX_LINE_BYTES: usize = 256 * 1024 * 1024;
/// stderr 一行日志最多读取的字节数，超出的部分丢弃。
const STDERR_LINE_BYTES: usize = 64 * 1024;
/// 进程退出后，等待 stdout 读完最后几行的时间。
pub(crate) const STDOUT_DRAIN: Duration = Duration::from_secs(1);
/// stdout 先关闭、进程却还没退出时，等待进程退出的时间，超时则强制结束。
const STDOUT_CLOSED_GRACE: Duration = Duration::from_secs(2);

/// 怎样启动 sidecar。
#[derive(Clone, Debug)]
pub struct SidecarCommand {
    pub program: PathBuf,
    pub args: Vec<OsString>,
    pub envs: Vec<(OsString, OsString)>,
    /// stdout 单行的长度上限（字节）。超过就当作协议错误并结束进程。
    pub max_line_bytes: usize,
}

impl SidecarCommand {
    /// 以 `--stdio` 参数运行指定的程序。
    pub fn stdio(program: impl Into<PathBuf>) -> Self {
        Self {
            program: program.into(),
            args: vec!["--stdio".into()],
            envs: Vec::new(),
            max_line_bytes: MAX_LINE_BYTES,
        }
    }

    /// 随应用打包的 sidecar：和主程序在同一个目录。
    /// - 打包后：macOS 的 `Contents/MacOS/efv-sidecar`，Windows 和 Linux 在主程序旁边；
    /// - `cargo tauri dev`：tauri-build 会把 `externalBin` 复制到 `target/debug/`，也在主程序旁边。
    pub fn bundled() -> std::io::Result<Self> {
        let exe = std::env::current_exe()?;
        let dir = exe
            .parent()
            .ok_or_else(|| std::io::Error::other("the executable has no parent directory"))?;
        Ok(Self::stdio(dir.join(format!(
            "efv-sidecar{}",
            std::env::consts::EXE_SUFFIX
        ))))
    }
}

type Reply = std::result::Result<Value, SidecarError>;

/// 要求监管任务结束进程：先等 `grace` 让它自己退出，不退就强制结束。`reason` 是进行中的请求会看到的原因。
struct Stop {
    grace: Duration,
    reason: String,
}

/// 待处理表。关闭之后不再接受新的请求。
struct Pending {
    closed: Option<SidecarError>,
    waiting: HashMap<u64, oneshot::Sender<Reply>>,
}

struct Shared {
    generation: u64,
    pending: Mutex<Pending>,
    alive: AtomicBool,
}

impl Shared {
    fn register(&self, id: u64) -> Result<oneshot::Receiver<Reply>> {
        let mut pending = self.pending.lock().unwrap();
        if let Some(reason) = &pending.closed {
            return Err(reason.clone());
        }
        let (tx, rx) = oneshot::channel();
        pending.waiting.insert(id, tx);
        Ok(rx)
    }

    fn take(&self, id: u64) -> Option<oneshot::Sender<Reply>> {
        self.pending.lock().unwrap().waiting.remove(&id)
    }

    /// 连接结束：让所有进行中的请求以同一个错误返回。只有第一次调用生效。
    fn close(&self, reason: SidecarError) {
        let waiting = {
            let mut pending = self.pending.lock().unwrap();
            if pending.closed.is_some() {
                return;
            }
            pending.closed = Some(reason.clone());
            std::mem::take(&mut pending.waiting)
        };
        self.alive.store(false, Ordering::SeqCst);
        for (_, tx) in waiting {
            let _ = tx.send(Err(reason.clone()));
        }
    }
}

/// 请求被取消（调用方放弃等待）或超时时，把它从待处理表移除。
struct PendingGuard<'a> {
    shared: &'a Shared,
    id: u64,
}

impl Drop for PendingGuard<'_> {
    fn drop(&mut self) {
        self.shared.take(self.id);
    }
}

pub(crate) struct Connection {
    shared: Arc<Shared>,
    #[cfg(test)]
    pid: Option<u32>,
    /// 写 stdin 的任务的输入。取走（关闭）后，写任务结束并关闭 stdin，sidecar 读到 EOF 退出。
    writer: Mutex<Option<mpsc::UnboundedSender<String>>>,
    /// 通知监管任务：开始退出流程。
    stop: Mutex<Option<oneshot::Sender<Stop>>>,
    /// 进程被回收后变为 true。
    exited: watch::Receiver<bool>,
}

impl Connection {
    /// 启动进程。不做握手，由调用方发送 `system.ping`。
    pub(crate) fn spawn(command: &SidecarCommand, generation: u64) -> Result<Arc<Self>> {
        let mut cmd = Command::new(&command.program);
        cmd.args(&command.args)
            .envs(command.envs.iter().map(|(k, v)| (k, v)))
            .stdin(Stdio::piped())
            .stdout(Stdio::piped())
            .stderr(Stdio::piped())
            .kill_on_drop(true);
        // Windows 上从图形界面程序启动控制台程序，不加这个标志会弹出控制台窗口
        #[cfg(windows)]
        cmd.creation_flags(0x0800_0000); // CREATE_NO_WINDOW

        let mut child = cmd.spawn().map_err(|e| SidecarError::Unavailable {
            message: format!("failed to start {}: {e}", command.program.display()),
        })?;
        let pid = child.id();
        let stdin = child.stdin.take().expect("stdin is piped");
        let stdout = child.stdout.take().expect("stdout is piped");
        let stderr = child.stderr.take().expect("stderr is piped");

        let shared = Arc::new(Shared {
            generation,
            pending: Mutex::new(Pending {
                closed: None,
                waiting: HashMap::new(),
            }),
            alive: AtomicBool::new(true),
        });
        let (line_tx, line_rx) = mpsc::unbounded_channel();
        let (stop_tx, stop_rx) = oneshot::channel();
        let (exited_tx, exited_rx) = watch::channel(false);

        tokio::spawn(write_stdin(stdin, line_rx, shared.clone()));
        tokio::spawn(forward_stderr(stderr, generation, pid));
        let reader = tokio::spawn(read_stdout(stdout, shared.clone(), command.max_line_bytes));
        tokio::spawn(supervise(child, reader, stop_rx, shared.clone(), exited_tx));
        log::debug!("sidecar #{generation} started (pid {pid:?})");

        Ok(Arc::new(Self {
            shared,
            #[cfg(test)]
            pid,
            writer: Mutex::new(Some(line_tx)),
            stop: Mutex::new(Some(stop_tx)),
            exited: exited_rx,
        }))
    }

    pub(crate) fn generation(&self) -> u64 {
        self.shared.generation
    }

    #[cfg(test)]
    pub(crate) fn pid(&self) -> Option<u32> {
        self.pid
    }

    pub(crate) fn is_alive(&self) -> bool {
        self.shared.alive.load(Ordering::SeqCst)
    }

    /// 发送一个请求并等待响应。超时或调用方取消时，请求从待处理表移除，迟到的响应会被忽略。
    pub(crate) async fn request(
        &self,
        id: u64,
        method: &str,
        params: &Value,
        limit: Duration,
    ) -> Result<Value> {
        let rx = self.shared.register(id)?;
        let _guard = PendingGuard {
            shared: &self.shared,
            id,
        };

        let mut line =
            serde_json::json!({ "id": id, "method": method, "params": params }).to_string();
        line.push('\n');
        let sent = self
            .writer
            .lock()
            .unwrap()
            .as_ref()
            .map(|tx| tx.send(line).is_ok());
        if sent != Some(true) {
            return Err(SidecarError::Crashed {
                message: "the sidecar is not accepting requests".into(),
            });
        }

        match timeout(limit, rx).await {
            Ok(Ok(reply)) => reply,
            Ok(Err(_)) => Err(SidecarError::Crashed {
                message: "the sidecar connection was dropped".into(),
            }),
            Err(_) => Err(SidecarError::Timeout {
                method: method.to_owned(),
                after: limit,
            }),
        }
    }

    /// 退出：关闭 stdin，等进程自己退出；超过 `grace` 仍不退出就强制结束。返回时进程已被回收。
    pub(crate) async fn shutdown(&self, grace: Duration) {
        self.stop_with(grace, "the sidecar was shut down");
        let mut exited = self.exited.clone();
        let _ = exited.wait_for(|done| *done).await;
    }

    /// 立即结束进程，不等它回收（请求超时后用）。进行中的请求会以 `reason` 为原因失败，之后连接不可用。
    pub(crate) fn abort(&self, reason: &str) {
        self.stop_with(Duration::ZERO, reason);
    }

    /// 立即结束进程（握手失败时用）。返回时进程已被回收。
    pub(crate) async fn kill(&self) {
        self.shutdown(Duration::ZERO).await;
    }

    fn stop_with(&self, grace: Duration, reason: &str) {
        // 立刻标记为不可用，不等进程被回收：这之后的请求要启动新的一代，而不是拿到这个正在退出、
        // 已经不收请求的连接（否则紧接着超时的那个请求会被误报成崩溃）
        self.shared.alive.store(false, Ordering::SeqCst);
        self.writer.lock().unwrap().take();
        if let Some(stop) = self.stop.lock().unwrap().take() {
            let _ = stop.send(Stop {
                grace,
                reason: reason.to_owned(),
            });
        }
    }

    #[cfg(test)]
    pub(crate) fn pending_len(&self) -> usize {
        self.shared.pending.lock().unwrap().waiting.len()
    }
}

fn truncate(text: &str) -> String {
    match text.char_indices().nth(LOG_LINE_LIMIT) {
        Some((end, _)) => format!("{}…", &text[..end]),
        None => text.to_owned(),
    }
}

async fn write_stdin(
    mut stdin: ChildStdin,
    mut lines: mpsc::UnboundedReceiver<String>,
    shared: Arc<Shared>,
) {
    while let Some(line) = lines.recv().await {
        let written = async {
            stdin.write_all(line.as_bytes()).await?;
            stdin.flush().await
        };
        if let Err(e) = written.await {
            // 进程已经退出，监管任务会让所有请求返回；这里只是更早地结束
            log::debug!(
                "sidecar #{}: failed to write to stdin: {e}",
                shared.generation
            );
            break;
        }
    }
    // 离开作用域时 stdin 被关闭，sidecar 读到 EOF
}

/// 读到的一行的情况。
#[derive(Debug, PartialEq, Eq)]
enum LineRead {
    /// 流已经结束，没有更多内容。
    Eof,
    /// 读到完整的一行（含换行；流结束前最后一行可以没有换行）。
    Complete,
    /// 这一行超过上限，只读了开头 `limit + 1` 字节，这一行的其余部分还在流里。
    TooLong,
}

/// 读一行到 `buf`（先清空），内容不超过 `limit` 字节（不含结尾的换行）。
async fn read_line_capped<R: AsyncBufRead + Unpin>(
    reader: &mut R,
    buf: &mut Vec<u8>,
    limit: usize,
) -> std::io::Result<LineRead> {
    buf.clear();
    // 最多读 limit + 1 字节：多出来的那一个字节用来区分“正好 limit 字节加换行”和“更长”
    let read = (&mut *reader)
        .take(limit as u64 + 1)
        .read_until(b'\n', buf)
        .await?;
    Ok(if read == 0 {
        LineRead::Eof
    } else if buf.last() == Some(&b'\n') || read <= limit {
        LineRead::Complete
    } else {
        LineRead::TooLong
    })
}

/// 丢弃当前这一行剩下的部分（直到换行或流结束）。
async fn skip_rest_of_line<R: AsyncBufRead + Unpin>(reader: &mut R) -> std::io::Result<()> {
    loop {
        let chunk = reader.fill_buf().await?;
        if chunk.is_empty() {
            return Ok(());
        }
        match chunk.iter().position(|&byte| byte == b'\n') {
            Some(newline) => {
                reader.consume(newline + 1);
                return Ok(());
            }
            None => {
                let length = chunk.len();
                reader.consume(length);
            }
        }
    }
}

/// 转发 sidecar 的 stderr 到调试日志。不合法的 UTF-8 按替换字符处理（不能因此停止读取，否则管道写满后 sidecar 会被卡住），
/// 过长的行只记开头。
async fn forward_stderr(stderr: ChildStderr, generation: u64, pid: Option<u32>) {
    let mut reader = BufReader::new(stderr);
    let mut buf = Vec::new();
    loop {
        let read = read_line_capped(&mut reader, &mut buf, STDERR_LINE_BYTES).await;
        let Ok(kind) = read else { break };
        if kind == LineRead::Eof {
            break;
        }
        let line = String::from_utf8_lossy(&buf);
        log::debug!(
            target: "efv::sidecar",
            "#{generation} (pid {pid:?}): {}",
            truncate(line.trim_end_matches(['\r', '\n']))
        );
        if kind == LineRead::TooLong && skip_rest_of_line(&mut reader).await.is_err() {
            break;
        }
    }
}

/// 读取 stdout 结束的原因。
#[derive(Debug, PartialEq, Eq)]
enum StdoutEnd {
    /// 流正常结束。
    Closed,
    /// 读取出错。
    Failed,
    /// 有一行超过上限；已经让进行中的请求以协议错误返回，进程应当立即结束。
    LineTooLong,
}

async fn read_stdout(stdout: ChildStdout, shared: Arc<Shared>, max_line: usize) -> StdoutEnd {
    let mut reader = BufReader::new(stdout);
    let mut buf = Vec::new();
    loop {
        match read_line_capped(&mut reader, &mut buf, max_line).await {
            Ok(LineRead::Eof) => return StdoutEnd::Closed,
            Ok(LineRead::Complete) => {
                // 不合法的 UTF-8 不应该让读取中断，按替换字符处理，后面解析 JSON 时会被当作非 JSON 行忽略
                let line = String::from_utf8_lossy(&buf);
                handle_line(&shared, line.trim_end_matches(['\r', '\n']));
                // 别让一条很大的消息撑大的缓冲区一直占着内存
                if buf.capacity() > 1024 * 1024 {
                    buf = Vec::new();
                }
            }
            Ok(LineRead::TooLong) => {
                log::warn!(
                    "sidecar #{}: a line on stdout is longer than {max_line} bytes; stopping the sidecar",
                    shared.generation
                );
                shared.close(SidecarError::Protocol {
                    message: format!("the sidecar wrote a line longer than {max_line} bytes"),
                });
                return StdoutEnd::LineTooLong;
            }
            Err(e) => {
                log::warn!("sidecar #{}: failed to read stdout: {e}", shared.generation);
                return StdoutEnd::Failed;
            }
        }
    }
}

/// 处理 stdout 上的一行。stdout 应该只有协议消息，其他内容记日志后忽略。
fn handle_line(shared: &Shared, line: &str) {
    if line.trim().is_empty() {
        return;
    }
    let Ok(Value::Object(mut message)) = serde_json::from_str::<Value>(line) else {
        log::warn!(
            "sidecar #{}: ignoring a non-JSON line on stdout: {}",
            shared.generation,
            truncate(line)
        );
        return;
    };

    let id = match message.get("id") {
        Some(Value::Number(n)) => n.as_u64(),
        _ => None,
    };
    let Some(id) = id else {
        // id 为 null：sidecar 说它无法解析我们发的某一行。我们只发合法的请求，所以这不应该发生
        log::warn!(
            "sidecar #{}: reply without a usable id: {}",
            shared.generation,
            truncate(line)
        );
        return;
    };
    let Some(tx) = shared.take(id) else {
        log::debug!(
            "sidecar #{}: reply to request {id} ignored (it already timed out or was cancelled)",
            shared.generation
        );
        return;
    };

    let reply = if let Some(error) = message.remove("error").filter(|e| !e.is_null()) {
        let field = |name: &str, fallback: &str| {
            error
                .get(name)
                .and_then(Value::as_str)
                .unwrap_or(fallback)
                .to_owned()
        };
        Err(SidecarError::Remote {
            code: field("code", code::INTERNAL_ERROR),
            message: field("message", ""),
        })
    } else if let Some(result) = message.remove("result") {
        Ok(result)
    } else {
        Err(SidecarError::Protocol {
            message: format!("reply {id} has neither result nor error"),
        })
    };
    let _ = tx.send(reply);
}

/// 监管进程：等它退出（自己退出、被要求退出或强制结束），回收后让所有进行中的请求返回。
async fn supervise(
    mut child: tokio::process::Child,
    mut reader: JoinHandle<StdoutEnd>,
    stop: oneshot::Receiver<Stop>,
    shared: Arc<Shared>,
    exited: watch::Sender<bool>,
) {
    let generation = shared.generation;
    let mut reader_done = false;
    let mut stop_reason: Option<String> = None;

    let status = tokio::select! {
        status = child.wait() => status,
        ended = &mut reader => {
            reader_done = true;
            // 有一行超过上限：协议已经不可信，立即结束；
            // stdout 关闭了而进程还在：不再可用，给它一点时间退出，否则结束它
            let grace = if matches!(ended, Ok(StdoutEnd::LineTooLong)) {
                Duration::ZERO
            } else {
                STDOUT_CLOSED_GRACE
            };
            wait_or_kill(&mut child, grace, generation).await
        }
        stop = stop => {
            // 发送方被丢弃（连接没有经过 shutdown 就被丢掉）等同于不给宽限时间
            let stop = stop.unwrap_or(Stop { grace: Duration::ZERO, reason: "the sidecar connection was dropped".into() });
            stop_reason = Some(stop.reason);
            wait_or_kill(&mut child, stop.grace, generation).await
        }
    };

    // 进程已经退出：stdout 上最后几行可能还没读完，读完之后再让请求返回，避免丢掉已经写出的响应
    if !reader_done {
        let _ = timeout(STDOUT_DRAIN, &mut reader).await;
    }

    let message = match (&status, stop_reason) {
        (_, Some(reason)) => reason,
        (Ok(status), None) => format!("the sidecar exited unexpectedly ({status})"),
        (Err(e), None) => format!("lost track of the sidecar process: {e}"),
    };
    log::debug!("sidecar #{generation}: {message}");
    shared.close(SidecarError::Crashed { message });
    let _ = exited.send(true);
}

/// 等进程退出，超过 `grace` 就强制结束，并回收。
async fn wait_or_kill(
    child: &mut tokio::process::Child,
    grace: Duration,
    generation: u64,
) -> std::io::Result<std::process::ExitStatus> {
    match timeout(grace, child.wait()).await {
        Ok(status) => status,
        Err(_) => {
            log::warn!("sidecar #{generation} did not exit within {grace:?}; killing it");
            let _ = child.start_kill();
            child.wait().await
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn shared() -> Shared {
        Shared {
            generation: 1,
            pending: Mutex::new(Pending {
                closed: None,
                waiting: HashMap::new(),
            }),
            alive: AtomicBool::new(true),
        }
    }

    fn reply_to(shared: &Shared, id: u64, line: &str) -> Reply {
        let mut rx = shared.register(id).unwrap();
        handle_line(shared, line);
        rx.try_recv().expect("a reply was delivered")
    }

    #[test]
    fn result_and_error_lines_complete_the_matching_request() {
        let s = shared();
        assert_eq!(
            reply_to(&s, 7, r#"{"id": 7, "result": {"ok": true}}"#).unwrap(),
            serde_json::json!({"ok": true})
        );
        assert_eq!(
            reply_to(
                &s,
                8,
                r#"{"id": 8, "error": {"code": "corrupted", "message": "bad"}}"#
            )
            .unwrap_err(),
            SidecarError::remote("corrupted", "bad")
        );
    }

    #[test]
    fn a_null_result_is_still_a_result() {
        assert_eq!(
            reply_to(&shared(), 1, r#"{"id": 1, "result": null}"#).unwrap(),
            Value::Null
        );
    }

    #[test]
    fn malformed_replies_are_reported_to_the_request() {
        let s = shared();
        assert_eq!(
            reply_to(&s, 1, r#"{"id": 1}"#).unwrap_err().code(),
            "protocol_error"
        );
        // 错误对象缺少 code：按 internal_error 处理
        assert_eq!(
            reply_to(&s, 2, r#"{"id": 2, "error": {}}"#)
                .unwrap_err()
                .code(),
            "internal_error"
        );
    }

    #[test]
    fn unusable_lines_are_ignored_and_leave_requests_pending() {
        let s = shared();
        let mut rx = s.register(5).unwrap();
        for line in [
            "",
            "   ",
            "not json",
            "[1, 2]",
            "42",
            r#"{"id": null, "error": {"code": "invalid_request", "message": "x"}}"#,
            r#"{"id": -1, "result": 1}"#,
            r#"{"id": 1.5, "result": 1}"#,
            r#"{"id": 99, "result": 1}"#,
            r#"{"id": "5", "result": 1}"#,
        ] {
            handle_line(&s, line);
        }
        assert!(rx.try_recv().is_err());
        assert!(s.pending.lock().unwrap().waiting.contains_key(&5));
    }

    #[test]
    fn closing_fails_every_pending_request_once_and_refuses_new_ones() {
        let s = shared();
        let mut a = s.register(1).unwrap();
        let mut b = s.register(2).unwrap();
        s.close(SidecarError::Crashed {
            message: "gone".into(),
        });
        s.close(SidecarError::Crashed {
            message: "second close is ignored".into(),
        });
        for rx in [&mut a, &mut b] {
            assert_eq!(
                rx.try_recv().unwrap().unwrap_err(),
                SidecarError::Crashed {
                    message: "gone".into()
                }
            );
        }
        assert_eq!(
            s.register(3).unwrap_err(),
            SidecarError::Crashed {
                message: "gone".into()
            }
        );
        assert!(!s.alive.load(Ordering::SeqCst));
    }

    // ---- 单行长度上限

    async fn read_all(input: &[u8], limit: usize) -> Vec<(LineRead, Vec<u8>)> {
        let mut reader = BufReader::new(input);
        let mut buf = Vec::new();
        let mut out = Vec::new();
        loop {
            let kind = read_line_capped(&mut reader, &mut buf, limit)
                .await
                .unwrap();
            if kind == LineRead::Eof {
                return out;
            }
            out.push((kind, buf.clone()));
            if out.len() > 10 {
                panic!("too many lines");
            }
        }
    }

    #[tokio::test]
    async fn lines_up_to_the_limit_are_read_whole() {
        let lines = read_all(b"abc\nde\r\nlast", 5).await;
        assert_eq!(
            lines,
            [
                (LineRead::Complete, b"abc\n".to_vec()),
                (LineRead::Complete, b"de\r\n".to_vec()),
                // 流结束前最后一行没有换行，也算一行
                (LineRead::Complete, b"last".to_vec()),
            ]
        );
    }

    #[tokio::test]
    async fn a_line_of_exactly_the_limit_is_accepted_and_one_more_byte_is_too_long() {
        // 内容正好 5 字节加换行：合法
        let exact = read_all(b"12345\n", 5).await;
        assert_eq!(exact, [(LineRead::Complete, b"12345\n".to_vec())]);
        // 没有换行结尾、正好 5 字节：也合法
        let exact_eof = read_all(b"12345", 5).await;
        assert_eq!(exact_eof, [(LineRead::Complete, b"12345".to_vec())]);
        // 内容 6 字节：超限，只读了开头 limit + 1 个字节
        let over = read_all(b"123456\nnext\n", 5).await;
        assert_eq!(over[0], (LineRead::TooLong, b"123456".to_vec()));
    }

    #[tokio::test]
    async fn the_rest_of_a_too_long_line_can_be_skipped() {
        let mut reader = BufReader::new(&b"0123456789ABCDEF\nnext\n"[..]);
        let mut buf = Vec::new();
        assert_eq!(
            read_line_capped(&mut reader, &mut buf, 4).await.unwrap(),
            LineRead::TooLong
        );
        skip_rest_of_line(&mut reader).await.unwrap();
        assert_eq!(
            read_line_capped(&mut reader, &mut buf, 4).await.unwrap(),
            LineRead::Complete
        );
        assert_eq!(buf, b"next\n");
        // 没有换行的流：一直丢到结束
        let mut reader = BufReader::new(&b"no newline at all"[..]);
        skip_rest_of_line(&mut reader).await.unwrap();
        assert_eq!(
            read_line_capped(&mut reader, &mut buf, 4).await.unwrap(),
            LineRead::Eof
        );
    }

    #[tokio::test]
    async fn invalid_utf8_does_not_stop_reading() {
        let lines = read_all(b"\xff\xfe broken\nok\n", 64).await;
        assert_eq!(lines.len(), 2);
        assert_eq!(
            String::from_utf8_lossy(&lines[0].1),
            "\u{fffd}\u{fffd} broken\n"
        );
    }

    #[test]
    fn shutdown_budget_stays_under_five_seconds() {
        // 退出的总时长 = 宽限期 + 读完最后几行输出的时间（启动握手会在退出时被打断，不再计入）
        let total = super::super::protocol::Timeouts::default().shutdown_grace + STDOUT_DRAIN;
        assert!(total < Duration::from_secs(5), "{total:?}");
    }

    #[test]
    fn long_log_lines_are_truncated_on_character_boundaries() {
        let text = "错".repeat(LOG_LINE_LIMIT + 50);
        let cut = truncate(&text);
        assert_eq!(cut.chars().count(), LOG_LINE_LIMIT + 1);
        assert_eq!(truncate("short"), "short");
    }
}
