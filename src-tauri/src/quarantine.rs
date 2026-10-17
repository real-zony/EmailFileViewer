//! 附件的“来自网络”标记。
//!
//! 提取（用默认程序打开）和保存出来的附件来自不可信的邮件，要像浏览器下载的文件一样带上标记，
//! 系统才会对它们多一道检查：
//! - Windows：给文件写入 NTFS 备用数据流 `Zone.Identifier`（`ZoneId=3`，即“Internet”区域），
//!   Office 会据此用受保护视图打开文档，资源管理器会对可执行文件弹出安全警告；
//! - macOS：写入扩展属性 `com.apple.quarantine`，`.dmg`、`.terminal` 等文件首次打开时要经过 Gatekeeper；
//! - 其他系统没有对应的机制，什么也不做。
//!
//! 路径由 Rust 在拿到 sidecar 的结果后统一处理（见 `sidecar::service`）。标记失败（例如 FAT 分区、网络共享不支持）
//! 只记一条警告，不影响保存或打开。

use std::io;
use std::path::{Path, PathBuf};

/// Windows 的 `Zone.Identifier` 流内容。ZoneId=3 表示 Internet。
#[cfg(windows)]
const ZONE_TRANSFER: &str = "[ZoneTransfer]\r\nZoneId=3\r\n";

/// macOS 隔离属性的名字。
#[cfg(target_os = "macos")]
const QUARANTINE_ATTRIBUTE: &str = "com.apple.quarantine";

/// 给一个文件打上“来自网络”的标记。只处理普通文件（符号链接会跟随到目标），目录和不存在的路径返回错误。
pub fn mark_downloaded(path: &Path) -> io::Result<()> {
    if !std::fs::metadata(path)?.is_file() {
        return Err(io::Error::other("not a regular file"));
    }
    platform::mark(path)
}

/// 给一批文件打标记。逐个处理，某个失败只记警告，不影响其余的；在阻塞线程里执行，不占用异步运行时。
pub async fn mark_all(paths: Vec<PathBuf>) {
    if paths.is_empty() {
        return;
    }
    let marked = tokio::task::spawn_blocking(move || {
        for path in &paths {
            // 日志里只记文件名，不记完整路径
            let name = path.file_name().unwrap_or_default().to_string_lossy();
            match mark_downloaded(path) {
                Ok(()) => log::debug!("marked {name} as downloaded from the internet"),
                Err(error) => {
                    log::warn!("could not mark {name} as downloaded from the internet: {error}")
                }
            }
        }
    })
    .await;
    if let Err(error) = marked {
        log::warn!("marking files as downloaded failed: {error}");
    }
}

#[cfg(windows)]
mod platform {
    use std::io;
    use std::path::{Path, PathBuf};

    use super::ZONE_TRANSFER;

    /// `path:Zone.Identifier`：同一个文件的备用数据流。
    pub(super) fn stream_path(path: &Path) -> PathBuf {
        let mut stream = path.as_os_str().to_owned();
        stream.push(":Zone.Identifier");
        PathBuf::from(stream)
    }

    pub(super) fn mark(path: &Path) -> io::Result<()> {
        std::fs::write(stream_path(path), ZONE_TRANSFER)
    }

    #[cfg(test)]
    pub(super) fn read_mark(path: &Path) -> Option<String> {
        std::fs::read_to_string(stream_path(path)).ok()
    }
}

#[cfg(target_os = "macos")]
mod platform {
    use std::ffi::CString;
    use std::io;
    use std::os::unix::ffi::OsStrExt;
    use std::path::Path;
    use std::time::{SystemTime, UNIX_EPOCH};

    use super::QUARANTINE_ATTRIBUTE;

    /// 属性值的格式是 `标志;时间戳(十六进制秒);来源程序;UUID`。标志 `0081` 与浏览器下载的文件相同
    /// （下载来源）；UUID 是系统用来关联“下载事件”数据库的，没有记录时可以省略。
    pub(super) fn quarantine_value(now: SystemTime) -> String {
        let seconds = now
            .duration_since(UNIX_EPOCH)
            .map(|elapsed| elapsed.as_secs())
            .unwrap_or(0);
        format!("0081;{seconds:x};EmailFileViewer;")
    }

    pub(super) fn mark(path: &Path) -> io::Result<()> {
        let path = CString::new(path.as_os_str().as_bytes())
            .map_err(|_| io::Error::other("the path contains a NUL byte"))?;
        let name = CString::new(QUARANTINE_ATTRIBUTE).expect("no NUL byte");
        let value = quarantine_value(SystemTime::now());
        // SAFETY: 三个指针都指向在调用期间一直有效的缓冲区，value 的长度与 size 一致。
        let status = unsafe {
            libc::setxattr(
                path.as_ptr(),
                name.as_ptr(),
                value.as_ptr().cast(),
                value.len(),
                0,
                0,
            )
        };
        if status == 0 {
            Ok(())
        } else {
            Err(io::Error::last_os_error())
        }
    }

    #[cfg(test)]
    pub(super) fn read_mark(path: &Path) -> Option<String> {
        let path = CString::new(path.as_os_str().as_bytes()).ok()?;
        let name = CString::new(QUARANTINE_ATTRIBUTE).ok()?;
        let mut buffer = [0u8; 512];
        // SAFETY: buffer 在调用期间有效，长度与 size 一致。
        let length = unsafe {
            libc::getxattr(
                path.as_ptr(),
                name.as_ptr(),
                buffer.as_mut_ptr().cast(),
                buffer.len(),
                0,
                0,
            )
        };
        usize::try_from(length)
            .ok()
            .map(|length| String::from_utf8_lossy(&buffer[..length]).into_owned())
    }
}

#[cfg(not(any(windows, target_os = "macos")))]
mod platform {
    use std::io;
    use std::path::Path;

    pub(super) fn mark(_path: &Path) -> io::Result<()> {
        Ok(())
    }

    #[cfg(test)]
    pub(super) fn read_mark(_path: &Path) -> Option<String> {
        None
    }
}

/// 测试用：读出文件上的标记（Windows 是 `Zone.Identifier` 流的内容，macOS 是隔离属性的值，其他系统恒为 `None`）。
#[cfg(test)]
pub(crate) fn read_mark(path: &Path) -> Option<String> {
    platform::read_mark(path)
}

/// 当前平台有没有“来自网络”的标记机制。
#[cfg(test)]
pub(crate) const SUPPORTED: bool = cfg!(any(windows, target_os = "macos"));

#[cfg(test)]
mod tests {
    use std::fs;

    use super::*;

    struct TempDir(PathBuf);
    impl TempDir {
        fn new(name: &str) -> Self {
            let dir = std::env::temp_dir().join(format!("efv-mark-{}-{name}", std::process::id()));
            let _ = fs::remove_dir_all(&dir);
            fs::create_dir_all(&dir).unwrap();
            Self(dir)
        }
        fn file(&self, name: &str) -> PathBuf {
            let path = self.0.join(name);
            fs::write(&path, "content").unwrap();
            path
        }
    }
    impl Drop for TempDir {
        fn drop(&mut self) {
            let _ = fs::remove_dir_all(&self.0);
        }
    }

    #[test]
    fn a_marked_file_keeps_its_content_and_carries_the_platform_mark() {
        let dir = TempDir::new("basic");
        let file = dir.file("invoice.pdf");
        assert_eq!(read_mark(&file), None, "a fresh file has no mark");

        mark_downloaded(&file).unwrap();

        assert_eq!(fs::read_to_string(&file).unwrap(), "content");
        let mark = read_mark(&file);
        if SUPPORTED {
            assert!(mark.is_some(), "the mark was not written");
        } else {
            assert_eq!(mark, None, "this platform has no such mark");
        }
    }

    #[cfg(target_os = "macos")]
    #[test]
    fn the_macos_mark_is_a_download_quarantine_with_a_hex_timestamp() {
        let dir = TempDir::new("macos");
        let file = dir.file("run.terminal");
        mark_downloaded(&file).unwrap();
        let value = read_mark(&file).unwrap();

        let parts: Vec<&str> = value.split(';').collect();
        assert_eq!(parts.len(), 4, "{value}");
        assert_eq!(parts[0], "0081", "{value}");
        let seconds = u64::from_str_radix(parts[1], 16).expect("hex timestamp");
        let now = std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .unwrap()
            .as_secs();
        assert!(now.abs_diff(seconds) < 60, "{value}");
        assert_eq!(parts[2], "EmailFileViewer");

        // 系统工具也认得：xattr -l 列得出这个属性
        let listing = std::process::Command::new("xattr")
            .arg("-l")
            .arg(&file)
            .output()
            .unwrap();
        let listing = String::from_utf8_lossy(&listing.stdout);
        assert!(listing.contains("com.apple.quarantine: 0081;"), "{listing}");
    }

    #[cfg(target_os = "macos")]
    #[test]
    fn marking_again_replaces_the_value() {
        let dir = TempDir::new("macos-twice");
        let file = dir.file("a.dmg");
        mark_downloaded(&file).unwrap();
        mark_downloaded(&file).unwrap();
        assert!(read_mark(&file).unwrap().starts_with("0081;"));
    }

    #[cfg(windows)]
    #[test]
    fn the_windows_mark_is_a_zone_identifier_stream_with_zone_id_3() {
        let dir = TempDir::new("windows");
        let file = dir.file("macro.docm");
        mark_downloaded(&file).unwrap();
        assert_eq!(
            read_mark(&file).as_deref(),
            Some("[ZoneTransfer]\r\nZoneId=3\r\n")
        );
        // 主数据流不受影响
        assert_eq!(fs::read_to_string(&file).unwrap(), "content");
        assert_eq!(
            platform::stream_path(Path::new(r"C:\x\a.pdf")),
            PathBuf::from(r"C:\x\a.pdf:Zone.Identifier")
        );
    }

    #[test]
    fn missing_files_and_directories_are_errors_not_panics() {
        let dir = TempDir::new("errors");
        assert!(mark_downloaded(&dir.0.join("nope.txt")).is_err());
        assert!(mark_downloaded(&dir.0).is_err());
    }

    #[cfg(unix)]
    #[test]
    fn a_symbolic_link_marks_the_file_it_points_to() {
        let dir = TempDir::new("link");
        let target = dir.file("real.pdf");
        let link = dir.0.join("link.pdf");
        std::os::unix::fs::symlink(&target, &link).unwrap();
        mark_downloaded(&link).unwrap();
        assert_eq!(read_mark(&target).is_some(), SUPPORTED);
    }

    #[tokio::test]
    async fn marking_a_batch_goes_on_after_a_failure() {
        let dir = TempDir::new("batch");
        let (a, b) = (dir.file("a.pdf"), dir.file("b.pdf"));
        mark_all(vec![a.clone(), dir.0.join("missing.pdf"), b.clone()]).await;
        assert_eq!(read_mark(&a).is_some(), SUPPORTED);
        assert_eq!(read_mark(&b).is_some(), SUPPORTED);
        mark_all(Vec::new()).await;
    }
}
