//! 测试辅助：用假 sidecar（`examples/fake_sidecar.rs`，一个真实的子进程）创建 [`SidecarService`]。
//! 假 sidecar 由 `cargo test` 一起构建；只跑某个测试目标时先 `cargo build --example fake_sidecar`。

use std::path::{Path, PathBuf};
use std::time::Duration;

use super::{SidecarCommand, SidecarConfig, SidecarService, Timeouts};

pub(crate) fn fake_program() -> PathBuf {
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

pub(crate) fn timeouts() -> Timeouts {
    Timeouts {
        handshake: Duration::from_secs(5),
        ping: Duration::from_secs(5),
        open: Duration::from_secs(5),
        close: Duration::from_secs(5),
        save: Duration::from_secs(5),
        save_all: Duration::from_secs(5),
        shutdown_grace: Duration::from_secs(2),
        // 测试里经常连续制造失败，默认不退避；要测退避的用例自己设
        start_backoff: Duration::ZERO,
        start_backoff_max: Duration::ZERO,
    }
}

/// 启动假 sidecar 的命令，`envs` 是传给它的环境变量。
pub(crate) fn fake_command(envs: &[(&str, &str)]) -> SidecarCommand {
    let mut command = SidecarCommand::stdio(fake_program());
    command.envs = envs
        .iter()
        .map(|(k, v)| ((*k).into(), (*v).into()))
        .collect();
    command
}

pub(crate) fn service_with(envs: &[(&str, &str)], timeouts: Timeouts) -> SidecarService {
    SidecarService::new(SidecarConfig {
        command: fake_command(envs),
        timeouts,
    })
}

pub(crate) fn service() -> SidecarService {
    service_with(&[], timeouts())
}
