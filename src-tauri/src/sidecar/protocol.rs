//! sidecar 的 stdio 协议：方法名、错误码、超时、错误类型和结果类型。
//!
//! 每行一个 JSON：请求 `{"id", "method", "params"}`，成功 `{"id", "result"}`，失败 `{"id", "error": {"code", "message"}}`。
//! 无法解析的请求行，sidecar 用 `id: null` 回复。

use std::fmt;
use std::path::PathBuf;
use std::time::Duration;

use serde::{Deserialize, Serialize, Serializer};
use serde_json::Value;

/// 本模块支持的协议版本，`system.ping` 返回的 `protocolVersion` 必须与它相同。
pub const PROTOCOL_VERSION: u64 = 1;

pub mod method {
    pub const PING: &str = "system.ping";
    pub const EMAIL_OPEN: &str = "email.open";
    pub const EMAIL_OPEN_NESTED: &str = "email.openNested";
    pub const DOCUMENT_CLOSE: &str = "document.close";
    pub const ATTACHMENT_SAVE: &str = "attachment.save";
    pub const ATTACHMENT_SAVE_ALL: &str = "attachment.saveAll";
    pub const ATTACHMENT_EXTRACT_FOR_OPEN: &str = "attachment.extractForOpen";
}

/// sidecar 返回的错误码（`error.code`）。错误码是开放的：遇到不认识的码照常透传，所以这里只列出已知的。
pub mod code {
    pub const UNSUPPORTED_FORMAT: &str = "unsupported_format";
    pub const CORRUPTED: &str = "corrupted";
    pub const UNREADABLE: &str = "unreadable";
    pub const DOCUMENT_NOT_FOUND: &str = "document_not_found";
    pub const ATTACHMENT_NOT_FOUND: &str = "attachment_not_found";
    pub const NOT_NESTED_EMAIL: &str = "not_nested_email";
    pub const EXECUTABLE_REFUSED: &str = "executable_refused";
    pub const WRITE_FAILED: &str = "write_failed";
    pub const INVALID_PARAMS: &str = "invalid_params";
    pub const INVALID_REQUEST: &str = "invalid_request";
    pub const METHOD_NOT_FOUND: &str = "method_not_found";
    pub const INTERNAL_ERROR: &str = "internal_error";

    // 以下是 Rust 端自己产生的错误码，不来自 sidecar
    /// 请求超时。
    pub const TIMEOUT: &str = "timeout";
    /// sidecar 进程在请求进行中退出或崩溃。界面应按“文件已损坏或无法解析”显示，但它和 `corrupted` 不同：
    /// 文件本身不一定有问题，用户重试时会重启 sidecar。
    pub const SIDECAR_CRASHED: &str = "sidecar_crashed";
    /// sidecar 无法启动，或启动后的握手没有通过。
    pub const SIDECAR_UNAVAILABLE: &str = "sidecar_unavailable";
    /// 文档句柄不存在：已经关闭，或者从未发出。
    pub const UNKNOWN_HANDLE: &str = "unknown_handle";
    /// 系统对话框（打开、保存、选择文件夹）没有正常返回。
    pub const DIALOG_FAILED: &str = "dialog_failed";
    /// sidecar 的响应不符合协议。
    pub const PROTOCOL_ERROR: &str = "protocol_error";
}

/// 各类请求的超时时间。默认值采用 `core/docs/sidecar-protocol.md` 第 8 节的建议。
#[derive(Clone, Debug)]
pub struct Timeouts {
    /// 启动后的就绪握手（`system.ping`）。冷启动时可能被系统的安全扫描拖慢，所以比 `ping` 宽松。
    pub handshake: Duration,
    /// 已经启动的 sidecar 上的 `system.ping`。
    pub ping: Duration,
    /// `email.open`、`email.openNested`。
    pub open: Duration,
    /// `document.close`。
    pub close: Duration,
    /// `attachment.save`、`attachment.extractForOpen`。
    pub save: Duration,
    /// `attachment.saveAll`。
    pub save_all: Duration,
    /// 退出时关闭 stdin 之后等待进程自己退出的时间（它要清理临时目录），超时则强制结束。
    /// 退出的总时长最多是它加上读完最后几行输出的 1 秒，所以默认值要让两者之和在 5 秒以内。
    pub shutdown_grace: Duration,
    /// 启动失败（进程起不来或握手没通过）之后，第一次退避的时间：这段时间内的请求直接失败，不再启动进程。
    /// 此后每连续失败一次翻倍，最多 `start_backoff_max`；启动成功后清零。为零则不退避。
    pub start_backoff: Duration,
    /// 启动退避的上限。
    pub start_backoff_max: Duration,
}

impl Default for Timeouts {
    fn default() -> Self {
        Self {
            handshake: Duration::from_secs(15),
            ping: Duration::from_secs(5),
            open: Duration::from_secs(30),
            close: Duration::from_secs(5),
            save: Duration::from_secs(60),
            save_all: Duration::from_secs(120),
            shutdown_grace: Duration::from_secs(3),
            start_backoff: Duration::from_millis(500),
            start_backoff_max: Duration::from_secs(10),
        }
    }
}

impl Timeouts {
    pub fn for_method(&self, method: &str) -> Duration {
        match method {
            method::PING => self.ping,
            method::EMAIL_OPEN | method::EMAIL_OPEN_NESTED => self.open,
            method::DOCUMENT_CLOSE => self.close,
            method::ATTACHMENT_SAVE_ALL => self.save_all,
            _ => self.save,
        }
    }
}

/// 请求超时后是否结束 sidecar 进程。
///
/// sidecar 无法取消正在处理的请求，卡住的解析会一直占着一个处理名额（同时处理的请求数有上限），
/// 积累几个之后 sidecar 就彻底不可用了，所以解析类请求超时后结束进程，下次请求重新启动。
/// 保存类请求（`attachment.save`、`attachment.saveAll`）不结束：它们正在往用户选的文件夹里写文件，
/// 中途结束会在那里留下残缺的临时文件；`document.close` 也不结束：它只是排在同一文档的某个慢请求后面。
pub(crate) fn kills_sidecar_on_timeout(method: &str) -> bool {
    matches!(
        method,
        method::PING
            | method::EMAIL_OPEN
            | method::EMAIL_OPEN_NESTED
            | method::ATTACHMENT_EXTRACT_FOR_OPEN
    )
}

/// 调用 sidecar 失败的原因。序列化后是 `{"code", "message"}`，可以直接作为 Tauri 命令的错误返回给前端。
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum SidecarError {
    /// sidecar 处理请求后返回了错误。
    Remote { code: String, message: String },
    /// 请求超时。请求已经从待处理表移除，迟到的响应会被忽略。
    Timeout { method: String, after: Duration },
    /// sidecar 进程在请求进行中退出或崩溃。
    Crashed { message: String },
    /// 无法启动 sidecar，或握手失败。
    Unavailable { message: String },
    /// 文档句柄不存在。
    UnknownHandle,
    /// sidecar 的响应不符合协议。
    Protocol { message: String },
}

impl SidecarError {
    pub(crate) fn remote(code: &str, message: impl Into<String>) -> Self {
        Self::Remote {
            code: code.to_owned(),
            message: message.into(),
        }
    }

    /// 稳定的错误码，界面按它判断显示什么。
    pub fn code(&self) -> &str {
        match self {
            Self::Remote { code, .. } => code,
            Self::Timeout { .. } => code::TIMEOUT,
            Self::Crashed { .. } => code::SIDECAR_CRASHED,
            Self::Unavailable { .. } => code::SIDECAR_UNAVAILABLE,
            Self::UnknownHandle => code::UNKNOWN_HANDLE,
            Self::Protocol { .. } => code::PROTOCOL_ERROR,
        }
    }

    /// 交给前端的错误码（design.md D11）：只用 sidecar 的错误码，加上 `sidecar_crashed`、`timeout`、`dialog_failed`。
    /// 句柄不存在对前端来说就是文档不存在；sidecar 无法启动按“解析进程崩溃”处理（`message` 里有具体原因）；
    /// 响应不符合协议是 sidecar 的 bug，按 `internal_error`。
    pub fn wire_code(&self) -> &str {
        match self {
            Self::Unavailable { .. } => code::SIDECAR_CRASHED,
            Self::UnknownHandle => code::DOCUMENT_NOT_FOUND,
            Self::Protocol { .. } => code::INTERNAL_ERROR,
            other => other.code(),
        }
    }

    /// 给开发者看的说明，不要直接显示给用户。
    pub fn message(&self) -> String {
        match self {
            Self::Remote { message, .. }
            | Self::Crashed { message }
            | Self::Unavailable { message }
            | Self::Protocol { message } => message.clone(),
            Self::Timeout { method, after } => format!("{method} did not finish within {after:?}"),
            Self::UnknownHandle => "the document handle is not open".to_owned(),
        }
    }

    /// 是否因为 sidecar 进程在请求进行中退出或崩溃而没有得到结果（sidecar 明确回答的错误不算）。
    pub fn is_crash(&self) -> bool {
        matches!(self, Self::Crashed { .. })
    }
}

impl fmt::Display for SidecarError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(f, "{}: {}", self.code(), self.message())
    }
}

impl std::error::Error for SidecarError {}

impl Serialize for SidecarError {
    fn serialize<S: Serializer>(&self, serializer: S) -> std::result::Result<S::Ok, S::Error> {
        #[derive(Serialize)]
        struct Wire<'a> {
            code: &'a str,
            message: String,
        }
        Wire {
            code: self.wire_code(),
            message: self.message(),
        }
        .serialize(serializer)
    }
}

pub type Result<T> = std::result::Result<T, SidecarError>;

// ---------------------------------------------------------------- 结果类型

/// Rust 端发给前端的文档句柄。它不是 sidecar 的 `docId`：sidecar 重启后 `docId` 会失效，句柄不会。
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, PartialOrd, Ord, Serialize, Deserialize)]
#[serde(transparent)]
pub struct DocHandle(pub u64);

/// 打开邮件的结果。
#[derive(Clone, Debug, Serialize)]
pub struct OpenedDocument {
    pub handle: DocHandle,
    /// sidecar 返回的 `document`，原样交给前端。
    pub document: Value,
}

/// `attachment.saveAll` 里保存成功的一项。
#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SavedAttachment {
    pub attachment_id: String,
    pub path: PathBuf,
}

/// `attachment.saveAll` 里保存失败的一项。单个失败不影响其他附件。
#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct FailedAttachment {
    pub attachment_id: String,
    pub code: String,
    pub message: String,
}

/// `attachment.saveAll` 的结果。
#[derive(Clone, Debug, Default, PartialEq, Eq, Serialize, Deserialize)]
pub struct SaveAllResult {
    #[serde(default)]
    pub saved: Vec<SavedAttachment>,
    #[serde(default)]
    pub failed: Vec<FailedAttachment>,
}

// ---------------------------------------------------------------- 线路格式

/// `system.ping` 的结果。
#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub(crate) struct PingResult {
    pub protocol_version: u64,
}

/// `email.open` 和 `email.openNested` 的结果。
#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub(crate) struct OpenResult {
    pub doc_id: String,
    pub document: Value,
}

/// `attachment.save` 和 `attachment.extractForOpen` 的结果。
#[derive(Debug, Deserialize)]
pub(crate) struct PathResult {
    pub path: PathBuf,
}

/// 把 sidecar 的 `result` 解析成具体类型，格式不对按协议错误处理。
pub(crate) fn parse_result<T: serde::de::DeserializeOwned>(
    method: &str,
    value: Value,
) -> Result<T> {
    serde_json::from_value(value).map_err(|e| SidecarError::Protocol {
        message: format!("unexpected result of {method}: {e}"),
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn timeouts_are_chosen_by_method() {
        let t = Timeouts::default();
        assert_eq!(t.handshake, Duration::from_secs(15));
        assert_eq!(t.shutdown_grace, Duration::from_secs(3));
        assert_eq!(t.start_backoff, Duration::from_millis(500));
        assert_eq!(t.start_backoff_max, Duration::from_secs(10));
        assert_eq!(t.for_method(method::PING), Duration::from_secs(5));
        assert_eq!(t.for_method(method::EMAIL_OPEN), Duration::from_secs(30));
        assert_eq!(
            t.for_method(method::EMAIL_OPEN_NESTED),
            Duration::from_secs(30)
        );
        assert_eq!(t.for_method(method::DOCUMENT_CLOSE), Duration::from_secs(5));
        assert_eq!(
            t.for_method(method::ATTACHMENT_SAVE),
            Duration::from_secs(60)
        );
        assert_eq!(
            t.for_method(method::ATTACHMENT_EXTRACT_FOR_OPEN),
            Duration::from_secs(60)
        );
        assert_eq!(
            t.for_method(method::ATTACHMENT_SAVE_ALL),
            Duration::from_secs(120)
        );
    }

    #[test]
    fn only_parsing_requests_kill_the_sidecar_when_they_time_out() {
        for m in [
            method::PING,
            method::EMAIL_OPEN,
            method::EMAIL_OPEN_NESTED,
            method::ATTACHMENT_EXTRACT_FOR_OPEN,
        ] {
            assert!(kills_sidecar_on_timeout(m), "{m}");
        }
        for m in [
            method::ATTACHMENT_SAVE,
            method::ATTACHMENT_SAVE_ALL,
            method::DOCUMENT_CLOSE,
        ] {
            assert!(!kills_sidecar_on_timeout(m), "{m}");
        }
    }

    #[test]
    fn errors_serialize_as_code_and_message() {
        let remote = SidecarError::remote(code::CORRUPTED, "bad header");
        assert_eq!(
            serde_json::to_value(&remote).unwrap(),
            serde_json::json!({"code": "corrupted", "message": "bad header"})
        );
        let crashed = SidecarError::Crashed {
            message: "exited".into(),
        };
        assert_eq!(crashed.code(), "sidecar_crashed");
        assert!(crashed.is_crash());
        assert!(!remote.is_crash());
        assert_eq!(
            SidecarError::Timeout {
                method: "email.open".into(),
                after: Duration::from_secs(30)
            }
            .code(),
            "timeout"
        );
        assert_eq!(SidecarError::UnknownHandle.code(), "unknown_handle");
        assert_eq!(
            SidecarError::Unavailable {
                message: String::new()
            }
            .code(),
            "sidecar_unavailable"
        );
        assert_eq!(
            SidecarError::Protocol {
                message: String::new()
            }
            .code(),
            "protocol_error"
        );
    }

    #[test]
    fn the_wire_format_only_uses_the_codes_the_frontend_knows() {
        let wire = |e: SidecarError| {
            serde_json::to_value(&e).unwrap()["code"]
                .as_str()
                .unwrap()
                .to_owned()
        };
        assert_eq!(wire(SidecarError::UnknownHandle), "document_not_found");
        assert_eq!(
            wire(SidecarError::Unavailable {
                message: "no binary".into()
            }),
            "sidecar_crashed"
        );
        assert_eq!(
            wire(SidecarError::Protocol {
                message: "bad".into()
            }),
            "internal_error"
        );
        assert_eq!(
            wire(SidecarError::Crashed {
                message: "x".into()
            }),
            "sidecar_crashed"
        );
        assert_eq!(
            wire(SidecarError::Timeout {
                method: "email.open".into(),
                after: Duration::from_secs(1)
            }),
            "timeout"
        );
        assert_eq!(
            wire(SidecarError::remote(code::DIALOG_FAILED, "x")),
            "dialog_failed"
        );
        assert_eq!(
            wire(SidecarError::remote(code::WRITE_FAILED, "x")),
            "write_failed"
        );
        // 内部判断仍然能区分它们
        assert_eq!(SidecarError::UnknownHandle.code(), "unknown_handle");
    }

    #[test]
    fn unknown_remote_codes_pass_through() {
        assert_eq!(
            SidecarError::remote("something_new", "x").code(),
            "something_new"
        );
    }

    #[test]
    fn save_all_result_parses_the_wire_format() {
        let value = serde_json::json!({
            "saved": [{"attachmentId": "a1", "path": "/tmp/a.pdf"}],
            "failed": [{"attachmentId": "a2", "code": "write_failed", "message": "disk full"}],
        });
        let parsed: SaveAllResult = parse_result(method::ATTACHMENT_SAVE_ALL, value).unwrap();
        assert_eq!(
            parsed.saved,
            vec![SavedAttachment {
                attachment_id: "a1".into(),
                path: "/tmp/a.pdf".into()
            }]
        );
        assert_eq!(parsed.failed[0].code, "write_failed");
        let err =
            parse_result::<PathResult>(method::ATTACHMENT_SAVE, serde_json::json!({"nope": 1}))
                .unwrap_err();
        assert_eq!(err.code(), "protocol_error");
    }

    #[test]
    fn handles_serialize_as_plain_numbers() {
        assert_eq!(serde_json::to_string(&DocHandle(7)).unwrap(), "7");
        assert_eq!(
            serde_json::from_str::<DocHandle>("7").unwrap(),
            DocHandle(7)
        );
    }
}
