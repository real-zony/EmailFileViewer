//! Windows 绿色版（design.md D13）：模式判断与数据目录。
//!
//! exe 所在目录里有标记文件 `EmailFileViewer.portable` 就进入绿色模式，设置、WebView2 的数据和日志都放在 exe 旁边的
//! `data` 文件夹里：复制整个文件夹设置就跟着走，删除整个文件夹就清除全部持久数据。`data` 不可写时（例如放在
//! `Program Files` 或只读介质上）退回安装版使用的标准目录，照常运行。
//!
//! 数据目录靠 Tauri 的 `app > appDirectoriesOverride`（运行时设在 context 上）整体改到 `data`：
//! `app_data_dir`、`app_local_data_dir`、`app_log_dir` 都跟着变，所以 store 插件（前端的 `LazyStore('settings.json')`
//! 和首屏前 Rust 侧的 `resolve_store_path` 走的都是 `app_data_dir`）、`tauri-plugin-log` 的日志目录不用改一行代码；
//! WebView2 的数据目录另外在创建窗口时显式指定为 `data\webview`（`window.dataDirectory` 不受这个覆盖影响）。
//!
//! 这里全是不依赖 Tauri 运行时的纯函数（文件系统探测作为参数传入），方便在任何平台上测试。只有 Windows 会真的进入
//! 绿色模式（见 [`detect_current`]）。

use std::io;
use std::path::{Path, PathBuf};

use tauri::utils::config::AppDirectoriesOverride;

/// 标记文件名。放在 exe 旁边（绿色版的 zip 里带着它，安装版没有）。
pub const MARKER_FILE: &str = "EmailFileViewer.portable";
/// exe 旁边存放全部数据的文件夹。
pub const DATA_DIR: &str = "data";
/// `data` 里 WebView2 的用户数据目录。
pub const WEBVIEW_DIR: &str = "webview";

/// 数据放在哪里。
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum Mode {
    /// 安装版（目录里没有标记文件）：Tauri 的标准目录。
    Installed,
    /// 绿色模式：全部数据在 `data_dir`（exe 旁边的 `data`）。
    Portable { data_dir: PathBuf },
    /// 有标记文件，但 `data` 不可写：退回标准目录。`reason` 是不可写的原因。
    PortableFallback { data_dir: PathBuf, reason: String },
}

impl Mode {
    /// 绿色模式下的 `data` 目录。安装版和退回标准目录的情况下为 `None`。
    pub fn data_dir(&self) -> Option<&Path> {
        match self {
            Self::Portable { data_dir } => Some(data_dir),
            Self::Installed | Self::PortableFallback { .. } => None,
        }
    }

    /// 要传给 Tauri 的目录覆盖：把 config、data、local data 都指到 `data`，日志到 `data\logs`，缓存到 `data\caches`。
    pub fn app_directories_override(&self) -> Option<AppDirectoriesOverride> {
        self.data_dir()
            .map(|dir| AppDirectoriesOverride::Root(dir.to_path_buf()))
    }

    /// WebView2 的数据目录（`data\webview`），只有绿色模式需要指定；其余情况用 WebView2 / Tauri 的默认位置。
    pub fn webview_data_dir(&self) -> Option<PathBuf> {
        self.data_dir().map(|dir| dir.join(WEBVIEW_DIR))
    }

    /// 退回标准目录时要记的警告。
    pub fn fallback_warning(&self) -> Option<String> {
        match self {
            Self::PortableFallback { reason, .. } => Some(format!(
                "the portable data folder next to the program is not writable ({reason}); \
                 using the standard application data folders instead"
            )),
            Self::Installed | Self::Portable { .. } => None,
        }
    }
}

/// 判断模式。`marker_exists` 判断标记文件是否存在，`prepare_data_dir` 确认 `data` 目录可用（建好并可写），
/// 两者都由调用方传入，测试里用假的。没有标记文件时 `prepare_data_dir` 不会被调用，磁盘上什么也不会创建。
pub fn detect(
    exe_dir: &Path,
    marker_exists: impl FnOnce(&Path) -> bool,
    prepare_data_dir: impl FnOnce(&Path) -> io::Result<()>,
) -> Mode {
    if !marker_exists(&exe_dir.join(MARKER_FILE)) {
        return Mode::Installed;
    }
    let data_dir = exe_dir.join(DATA_DIR);
    match prepare_data_dir(&data_dir) {
        Ok(()) => Mode::Portable { data_dir },
        Err(error) => Mode::PortableFallback {
            data_dir,
            reason: error.to_string(),
        },
    }
}

/// 确认 `data` 目录可用：建好目录（连同 `webview` 子目录），再写入并删除一个探测文件。
pub fn prepare_data_dir(data_dir: &Path) -> io::Result<()> {
    std::fs::create_dir_all(data_dir.join(WEBVIEW_DIR))?;
    let probe = data_dir.join(format!(".write-test-{}", std::process::id()));
    std::fs::write(&probe, b"")?;
    std::fs::remove_file(&probe)
}

/// 当前进程的模式。只有 Windows 才有绿色版，其他平台一律是安装版（macOS 的 `.app` 目录里放不放标记文件都一样）。
pub fn detect_current() -> Mode {
    if !cfg!(windows) {
        return Mode::Installed;
    }
    let exe_dir = std::env::current_exe()
        .ok()
        .and_then(|exe| exe.parent().map(Path::to_path_buf));
    match exe_dir {
        Some(dir) => detect(&dir, |marker| marker.is_file(), prepare_data_dir),
        None => Mode::Installed,
    }
}

#[cfg(test)]
mod tests {
    use std::cell::Cell;
    use std::fs;

    use super::*;

    /// 每个测试用自己的目录，结束后清理。
    struct TempDir(PathBuf);
    impl TempDir {
        fn new(name: &str) -> Self {
            let dir =
                std::env::temp_dir().join(format!("efv-portable-{}-{name}", std::process::id()));
            let _ = fs::remove_dir_all(&dir);
            fs::create_dir_all(&dir).unwrap();
            Self(dir)
        }
        fn marker(&self) -> &Self {
            fs::write(self.0.join(MARKER_FILE), "").unwrap();
            self
        }
    }
    impl Drop for TempDir {
        fn drop(&mut self) {
            let _ = fs::remove_dir_all(&self.0);
        }
    }

    fn exe_dir() -> PathBuf {
        PathBuf::from("/apps/EmailFileViewer")
    }

    #[test]
    fn without_the_marker_file_it_is_the_installed_build_and_nothing_is_touched() {
        let probed = Cell::new(false);
        let mode = detect(
            &exe_dir(),
            |_| false,
            |_| {
                probed.set(true);
                Ok(())
            },
        );
        assert_eq!(mode, Mode::Installed);
        assert!(!probed.get(), "the data folder must not even be prepared");
        assert_eq!(mode.data_dir(), None);
        assert_eq!(mode.app_directories_override(), None);
        assert_eq!(mode.webview_data_dir(), None);
        assert_eq!(mode.fallback_warning(), None);
    }

    #[test]
    fn the_marker_is_looked_for_next_to_the_executable() {
        let seen = Cell::new(None);
        detect(
            &exe_dir(),
            |marker| {
                seen.set(Some(marker.to_path_buf()));
                true
            },
            |_| Ok(()),
        );
        assert_eq!(
            seen.take(),
            Some(exe_dir().join("EmailFileViewer.portable"))
        );
    }

    #[test]
    fn with_the_marker_and_a_writable_data_folder_everything_goes_next_to_the_executable() {
        let prepared = Cell::new(None);
        let mode = detect(
            &exe_dir(),
            |_| true,
            |dir| {
                prepared.set(Some(dir.to_path_buf()));
                Ok(())
            },
        );
        let data = exe_dir().join("data");
        assert_eq!(prepared.take(), Some(data.clone()));
        assert_eq!(
            mode,
            Mode::Portable {
                data_dir: data.clone()
            }
        );
        assert_eq!(mode.data_dir(), Some(data.as_path()));
        assert_eq!(
            mode.app_directories_override(),
            Some(AppDirectoriesOverride::Root(data.clone()))
        );
        assert_eq!(mode.webview_data_dir(), Some(data.join("webview")));
        assert_eq!(mode.fallback_warning(), None);
    }

    #[test]
    fn an_unwritable_data_folder_falls_back_to_the_standard_folders() {
        let mode = detect(
            &exe_dir(),
            |_| true,
            |_| {
                Err(io::Error::new(
                    io::ErrorKind::PermissionDenied,
                    "access denied",
                ))
            },
        );
        assert!(matches!(mode, Mode::PortableFallback { .. }), "{mode:?}");
        // 退回标准目录：不覆盖任何目录，也不指定 WebView2 的数据目录
        assert_eq!(mode.data_dir(), None);
        assert_eq!(mode.app_directories_override(), None);
        assert_eq!(mode.webview_data_dir(), None);
        let warning = mode.fallback_warning().unwrap();
        assert!(warning.contains("access denied"), "{warning}");
        assert!(warning.contains("not writable"), "{warning}");
    }

    #[test]
    fn on_disk_the_marker_creates_data_and_webview_and_leaves_no_probe_file() {
        let dir = TempDir::new("writable");
        dir.marker();
        let mode = detect(&dir.0, |marker| marker.is_file(), prepare_data_dir);
        assert_eq!(
            mode,
            Mode::Portable {
                data_dir: dir.0.join("data")
            }
        );
        assert!(dir.0.join("data").join("webview").is_dir());
        let leftovers: Vec<_> = fs::read_dir(dir.0.join("data"))
            .unwrap()
            .map(|entry| entry.unwrap().file_name())
            .collect();
        assert_eq!(leftovers, ["webview"], "the probe file was not removed");
    }

    #[test]
    fn on_disk_without_the_marker_no_data_folder_is_created() {
        let dir = TempDir::new("installed");
        let mode = detect(&dir.0, |marker| marker.is_file(), prepare_data_dir);
        assert_eq!(mode, Mode::Installed);
        assert!(!dir.0.join("data").exists());
    }

    #[test]
    fn a_file_where_the_data_folder_should_be_makes_it_fall_back() {
        let dir = TempDir::new("blocked");
        dir.marker();
        fs::write(dir.0.join("data"), "not a folder").unwrap();
        let mode = detect(&dir.0, |marker| marker.is_file(), prepare_data_dir);
        assert!(matches!(mode, Mode::PortableFallback { .. }), "{mode:?}");
        assert_eq!(mode.data_dir(), None);
    }

    #[cfg(unix)]
    #[test]
    fn a_read_only_program_folder_falls_back() {
        use std::os::unix::fs::PermissionsExt;

        let dir = TempDir::new("read-only");
        dir.marker();
        fs::set_permissions(&dir.0, fs::Permissions::from_mode(0o555)).unwrap();
        let blocked = fs::create_dir(dir.0.join("probe")).is_err();
        let mode = detect(&dir.0, |marker| marker.is_file(), prepare_data_dir);
        fs::set_permissions(&dir.0, fs::Permissions::from_mode(0o755)).unwrap();
        if blocked {
            // 以 root 运行时目录权限拦不住，这个断言就没有意义
            assert!(matches!(mode, Mode::PortableFallback { .. }), "{mode:?}");
        }
    }

    #[test]
    fn tauri_puts_the_settings_store_and_the_logs_inside_the_data_folder() {
        use tauri::Manager;

        let build = |mode: &Mode| {
            let mut context = tauri::test::mock_context(tauri::test::noop_assets());
            context.config_mut().app.app_directories_override = mode.app_directories_override();
            tauri::test::mock_builder().build(context).unwrap()
        };
        let data = std::env::temp_dir()
            .join("efv-portable-resolve")
            .join("data");
        let portable = build(&Mode::Portable {
            data_dir: data.clone(),
        });
        let path = portable.path();
        // 前端的 LazyStore('settings.json') 和首屏前 Rust 侧读取设置都走 resolve_store_path -> app_data_dir
        let settings = tauri_plugin_store::resolve_store_path(
            portable.handle(),
            crate::settings::SETTINGS_FILE,
        );
        assert_eq!(settings.unwrap(), data.join("settings.json"));
        assert_eq!(path.app_data_dir().unwrap(), data);
        assert_eq!(path.app_config_dir().unwrap(), data);
        assert_eq!(path.app_local_data_dir().unwrap(), data);
        // tauri-plugin-log 的 LogDir 目标写到 app_log_dir
        assert_eq!(path.app_log_dir().unwrap(), data.join("logs"));

        // 安装版不覆盖：全部在标准目录里，不会落进 data
        let installed = build(&Mode::Installed);
        let settings = tauri_plugin_store::resolve_store_path(
            installed.handle(),
            crate::settings::SETTINGS_FILE,
        );
        assert!(!settings.unwrap().starts_with(&data));
        assert!(!installed.path().app_log_dir().unwrap().starts_with(&data));
        // 退回标准目录的同样不覆盖
        let fallback = build(&Mode::PortableFallback {
            data_dir: data.clone(),
            reason: "access denied".into(),
        });
        assert_eq!(
            fallback.path().app_data_dir().unwrap(),
            installed.path().app_data_dir().unwrap()
        );
    }

    #[test]
    fn only_windows_has_a_portable_build() {
        if !cfg!(windows) {
            assert_eq!(detect_current(), Mode::Installed);
        }
    }

    #[test]
    fn the_files_shipped_in_the_zip_are_in_the_repository() {
        // release.yml 把这个目录里的文件放进 zip；标记文件名必须和代码里的一致
        let shipped = Path::new(env!("CARGO_MANIFEST_DIR")).join("windows/portable");
        assert!(shipped.join(MARKER_FILE).is_file());
        let readme = fs::read_to_string(shipped.join("README.txt")).unwrap();
        for needed in ["WebView2", "data", MARKER_FILE] {
            assert!(
                readme.contains(needed),
                "README.txt does not mention {needed}"
            );
        }
    }
}
