//! 从启动参数、系统的“打开文件”事件和拖放事件里取出要打开的文件路径。

use std::ffi::OsStr;
use std::path::{Path, PathBuf};

use tauri::{DragDropEvent, WindowEvent};
use url::Url;

/// 参数是不是 `file://` 开头的 URL（scheme 不区分大小写）。
fn is_file_url(arg: &OsStr) -> bool {
    let bytes = arg.as_encoded_bytes();
    bytes.len() >= 7 && bytes[..7].eq_ignore_ascii_case(b"file://")
}

/// 解析命令行参数（第一个是程序自己）。参数用 `OsStr`，路径里有不合法的 UTF-8 也不会出问题。
///
/// 跳过空参数、`-` 开头的参数（例如 macOS 的 `-psn_…`）和目录；相对路径按 `cwd` 补全——
/// 单实例转发来的参数是第二个实例的参数，要用它的工作目录；也接受 `file://` URL（`FILE://` 也行）。
/// **不存在的路径照样保留**：交给打开批次，由它得到“无法读取”的失败事件，用户才知道出了什么事，而不是什么也没发生。
pub fn paths_from_args<S: AsRef<OsStr>>(args: &[S], cwd: &Path) -> Vec<PathBuf> {
    args.iter()
        .skip(1)
        .map(AsRef::as_ref)
        .filter(|arg| !arg.is_empty() && !arg.as_encoded_bytes().starts_with(b"-"))
        .filter_map(|arg| {
            if is_file_url(arg) {
                // 地址必须是合法的 UTF-8（百分号编码之后），解析不出路径的忽略
                arg.to_str()
                    .and_then(|text| Url::parse(text).ok())
                    .and_then(|url| url.to_file_path().ok())
            } else {
                Some(PathBuf::from(arg))
            }
        })
        .map(|path| {
            if path.is_relative() {
                cwd.join(path)
            } else {
                path
            }
        })
        .filter(|path| !path.is_dir())
        .collect()
}

/// macOS 的 `RunEvent::Opened`：只认 `file://`，其他 scheme 忽略。和命令行参数一样，目录忽略，不存在的文件保留。
#[cfg_attr(not(target_os = "macos"), allow(dead_code))]
pub fn paths_from_urls(urls: &[Url]) -> Vec<PathBuf> {
    urls.iter()
        .filter(|url| url.scheme() == "file")
        .filter_map(|url| url.to_file_path().ok())
        .filter(|path| !path.is_dir())
        .collect()
}

/// 拖放事件里的路径。拖入文字或链接时没有路径，得到空列表；文件夹不是邮件，忽略。
/// 其他文件（不管扩展名）都交给 sidecar 判断格式，不合适的会得到“不支持的格式”错误。
pub fn files_from_drop(paths: &[PathBuf]) -> Vec<PathBuf> {
    paths
        .iter()
        .filter(|path| !path.is_dir())
        .cloned()
        .collect()
}

/// 窗口事件里要打开的文件：只有 drop 才算（enter、over、leave 只是前端用来显示遮罩的）；
/// 不是文件的拖放（文本、链接）没有路径，得到空列表。
pub fn files_from_window_event(event: &WindowEvent) -> Vec<PathBuf> {
    match event {
        WindowEvent::DragDrop(DragDropEvent::Drop { paths, .. }) => files_from_drop(paths),
        _ => Vec::new(),
    }
}

#[cfg(test)]
mod tests {
    use std::fs;

    use super::*;

    struct TempDir(PathBuf);
    impl TempDir {
        fn new(name: &str) -> Self {
            let dir = std::env::temp_dir().join(format!("efv-args-{}-{name}", std::process::id()));
            let _ = fs::remove_dir_all(&dir);
            fs::create_dir_all(&dir).unwrap();
            Self(dir)
        }
        fn file(&self, name: &str) -> PathBuf {
            let path = self.0.join(name);
            fs::write(&path, "x").unwrap();
            path
        }
    }
    impl Drop for TempDir {
        fn drop(&mut self) {
            let _ = fs::remove_dir_all(&self.0);
        }
    }

    fn args(items: &[&str]) -> Vec<String> {
        items.iter().map(|s| (*s).to_owned()).collect()
    }

    fn file_name(path: &Path) -> &str {
        path.file_name().unwrap().to_str().unwrap()
    }

    #[test]
    fn existing_files_are_kept_in_order_and_the_program_name_is_skipped() {
        let dir = TempDir::new("order");
        let (a, b) = (dir.file("a.eml"), dir.file("b.msg"));
        let given = args(&[
            "/Applications/EmailFileViewer",
            a.to_str().unwrap(),
            b.to_str().unwrap(),
        ]);
        assert_eq!(paths_from_args(&given, Path::new("/")), [a, b]);
    }

    #[test]
    fn options_empty_arguments_and_directories_are_skipped() {
        let dir = TempDir::new("skip");
        let a = dir.file("a.eml");
        let given = args(&[
            "app",
            "-psn_0_12345",
            "--flag",
            "",
            dir.0.to_str().unwrap(),
            a.to_str().unwrap(),
        ]);
        assert_eq!(paths_from_args(&given, Path::new("/")), [a]);
        assert!(paths_from_args(&args(&["app"]), Path::new("/")).is_empty());
        assert!(paths_from_args::<String>(&[], Path::new("/")).is_empty());
    }

    #[test]
    fn a_path_that_does_not_exist_is_kept_so_the_user_is_told_it_cannot_be_read() {
        let dir = TempDir::new("missing");
        let (a, missing) = (dir.file("a.eml"), dir.0.join("missing.eml"));
        let given = args(&["app", missing.to_str().unwrap(), a.to_str().unwrap()]);
        assert_eq!(
            paths_from_args(&given, Path::new("/")),
            [missing.clone(), a]
        );
        // 相对路径也一样，补全之后交给打开批次
        assert_eq!(
            paths_from_args(&args(&["app", "gone.msg"]), &dir.0),
            [dir.0.join("gone.msg")]
        );
        // file:// 形式的也是
        let url = Url::from_file_path(&missing).unwrap();
        assert_eq!(
            paths_from_args(&args(&["app", url.as_str()]), Path::new("/")),
            [missing]
        );
    }

    #[test]
    fn the_file_scheme_is_matched_without_regard_to_case() {
        let dir = TempDir::new("scheme-case");
        let a = dir.file("a b.eml");
        let url = Url::from_file_path(&a).unwrap();
        for scheme in ["file://", "FILE://", "File://", "fIlE://"] {
            let text = url.as_str().replacen("file://", scheme, 1);
            assert_eq!(
                paths_from_args(&args(&["app", &text]), Path::new("/")),
                std::slice::from_ref(&a),
                "{text}"
            );
        }
        // 解析不出路径的 file URL（例如带主机名）忽略；它不会被当作相对路径
        assert!(paths_from_args(&args(&["app", "FILE://example.com/x.eml"]), &dir.0).is_empty());
    }

    #[cfg(unix)]
    #[test]
    fn arguments_that_are_not_valid_utf8_are_kept_without_panicking() {
        use std::ffi::OsString;
        use std::os::unix::ffi::OsStringExt;

        // 文件名不是合法的 UTF-8（Latin-1 写法的 é）。macOS 的文件系统建不出这样的文件，
        // 但解析参数不需要文件存在：不存在的路径同样保留
        let path = PathBuf::from(OsString::from_vec(b"/mail/caf\xe9.eml".to_vec()));
        let given = vec![
            OsString::from("app"),
            path.clone().into_os_string(),
            // 不合法的 UTF-8 开头的 file URL 不是路径，直接忽略
            OsString::from_vec(b"file:///tmp/\xff.eml".to_vec()),
            // 以 - 开头的仍然当作选项
            OsString::from_vec(b"-\xff".to_vec()),
        ];
        assert_eq!(paths_from_args(&given, Path::new("/")), [path]);
    }

    #[test]
    fn relative_paths_use_the_working_directory_of_the_process_that_was_launched() {
        let dir = TempDir::new("relative");
        let a = dir.file("a.eml");
        // 第二个实例在 dir 里执行 `app a.eml`，已运行的实例的工作目录不同
        assert_eq!(paths_from_args(&args(&["app", "a.eml"]), &dir.0), [a]);
        // 工作目录不对时补出的是另一个路径（不存在，由打开批次报告）
        assert_eq!(
            paths_from_args(&args(&["app", "a.eml"]), Path::new("/definitely/elsewhere")),
            [PathBuf::from("/definitely/elsewhere/a.eml")]
        );
    }

    #[test]
    fn file_urls_are_accepted() {
        let dir = TempDir::new("url");
        let a = dir.file("with space.eml");
        let url = Url::from_file_path(&a).unwrap();
        assert_eq!(
            paths_from_args(&args(&["app", url.as_str()]), Path::new("/")),
            std::slice::from_ref(&a)
        );
        assert_eq!(
            paths_from_urls(&[
                url,
                Url::parse("https://example.com/x.eml").unwrap(),
                Url::parse("mailto:a@b.c").unwrap()
            ]),
            [a]
        );
    }

    #[test]
    fn opened_urls_that_do_not_exist_are_kept_and_directories_are_dropped() {
        let dir = TempDir::new("opened");
        let missing = Url::from_file_path("/definitely/not/here.eml").unwrap();
        assert_eq!(
            paths_from_urls(&[missing]),
            [PathBuf::from("/definitely/not/here.eml")]
        );
        assert!(paths_from_urls(&[Url::from_directory_path(&dir.0).unwrap()]).is_empty());
        assert_eq!(file_name(&dir.file("x.eml")), "x.eml");
    }

    #[test]
    fn dropped_files_all_count_whatever_their_extension() {
        let dir = TempDir::new("drop");
        let files = [dir.file("a.eml"), dir.file("photo.png"), dir.file("b.msg")];
        assert_eq!(files_from_drop(&files), files);
    }

    #[test]
    fn dropped_folders_are_ignored_and_an_empty_drop_is_nothing() {
        let dir = TempDir::new("folder");
        let a = dir.file("a.eml");
        assert_eq!(files_from_drop(&[dir.0.clone(), a.clone()]), [a]);
        // 拖入文字或链接：没有路径
        assert!(files_from_drop(&[]).is_empty());
    }

    fn position() -> tauri::PhysicalPosition<f64> {
        tauri::PhysicalPosition::new(10.0, 20.0)
    }

    #[test]
    fn a_drop_of_several_files_is_one_batch_with_all_of_them() {
        let dir = TempDir::new("window-drop");
        let files = vec![dir.file("a.eml"), dir.file("b.msg"), dir.file("c.txt")];
        let event = WindowEvent::DragDrop(DragDropEvent::Drop {
            paths: files.clone(),
            position: position(),
        });
        assert_eq!(files_from_window_event(&event), files);
    }

    #[test]
    fn a_drop_without_paths_does_nothing() {
        // 拖入文字或链接：WebView 给出空的路径列表
        let event = WindowEvent::DragDrop(DragDropEvent::Drop {
            paths: vec![],
            position: position(),
        });
        assert!(files_from_window_event(&event).is_empty());
    }

    #[test]
    fn enter_over_and_leave_never_open_anything() {
        let dir = TempDir::new("window-hover");
        let a = dir.file("a.eml");
        for event in [
            WindowEvent::DragDrop(DragDropEvent::Enter {
                paths: vec![a],
                position: position(),
            }),
            WindowEvent::DragDrop(DragDropEvent::Over {
                position: position(),
            }),
            WindowEvent::DragDrop(DragDropEvent::Leave),
            WindowEvent::Focused(true),
        ] {
            assert!(files_from_window_event(&event).is_empty(), "{event:?}");
        }
    }
}
