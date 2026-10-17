//! .NET sidecar 的进程监管（tasks 5.3，见 design.md D2）。
//!
//! sidecar 以 `efv-sidecar --stdio` 运行，stdin/stdout 上每行一个 JSON，stderr 只有诊断日志。
//! 本模块负责：
//! - 按需启动：第一次请求时才启动，并用 `system.ping` 握手；
//! - 请求匹配与超时：自增 id、待处理表，每个请求都有超时（按方法区分，默认值见 [`Timeouts`]）。
//!   超时的请求返回 `timeout`；解析类请求（打开、提取）超时后还会结束 sidecar（卡住的解析无法取消，
//!   会一直占着处理名额），保存类请求不会（它们正在往用户的文件夹里写文件）；
//! - 崩溃处理：进程退出时，所有进行中的请求以 `sidecar_crashed` 返回；下次请求自动重启并重新握手；
//! - 文档句柄：前端拿到的是 Rust 的 [`DocHandle`]，不是 sidecar 的 `docId`；sidecar 重启后，
//!   旧句柄在下次使用时按来源（文件路径，或“父文档 + 附件 Id”）透明地重新打开；
//! - 退出：关闭 stdin，等待片刻，进程仍不退出就强制结束，并回收。
//!
//! 对外只有 [`SidecarService`]（以及它用到的配置、结果和错误类型）。Tauri 命令在 5.4 中基于它实现。

mod connection;
mod documents;
mod protocol;
mod service;

#[cfg(test)]
pub(crate) mod testing;
#[cfg(test)]
mod tests;

pub use connection::SidecarCommand;
pub use protocol::{
    DocHandle, FailedAttachment, OpenedDocument, PROTOCOL_VERSION, SaveAllResult, SavedAttachment,
    SidecarError, Timeouts, code, method,
};
pub use service::{SidecarConfig, SidecarService};
