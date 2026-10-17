mod commands;
mod dialogs;
mod fileargs;
mod links;
mod menu;
mod opening;
mod quarantine;
mod settings;
pub mod sidecar;

use std::path::{Path, PathBuf};

use opening::OpenCoordinator;
use settings::{InitialSettings, ResolvedMode, SETTINGS_FILE, WindowColors};
use sidecar::{SidecarConfig, SidecarService};
use tauri::webview::PageLoadEvent;
use tauri::window::Color;
use tauri::{Manager, RunEvent, Theme, WebviewUrl, WebviewWindowBuilder, Wry};

// 主窗口的属性，与原来 tauri.conf.json 中的定义一致
pub(crate) const MAIN_WINDOW_LABEL: &str = "main";
const MAIN_WINDOW_TITLE: &str = "EmailFileViewer";
const MAIN_WINDOW_SIZE: (f64, f64) = (1200.0, 800.0);
const MAIN_WINDOW_MIN_SIZE: (f64, f64) = (800.0, 560.0);

fn to_theme(mode: ResolvedMode) -> Theme {
    match mode {
        ResolvedMode::Light => Theme::Light,
        ResolvedMode::Dark => Theme::Dark,
    }
}

fn from_theme(theme: Theme) -> ResolvedMode {
    match theme {
        Theme::Dark => ResolvedMode::Dark,
        _ => ResolvedMode::Light,
    }
}

// 在创建窗口之前读取已保存的设置和系统语言，通过 initialization_script 在首次绘制前把它们交给页面，
// 并把窗口底色设成所选配色方案的底色。窗口先隐藏，设好底色后再显示。返回用到的初始设置（菜单文字也用其中的语言）。
fn create_main_window(app: &tauri::App) -> tauri::Result<InitialSettings> {
    let for_links = app.handle().clone();
    let for_navigation = app.handle().clone();
    // 应用首次加载完成之后，导航策略收紧（见 links.rs）
    let loaded = links::AppLoaded::default();
    let for_load = loaded.clone();
    let colors = WindowColors::embedded();
    let store_path = tauri_plugin_store::resolve_store_path(app.handle(), SETTINGS_FILE).ok();
    let stored = store_path
        .as_deref()
        .map(settings::read_stored)
        .unwrap_or_default();
    let system_locale = sys_locale::get_locale();
    let initial = InitialSettings::resolve(&stored, system_locale.as_deref(), colors);

    let window = WebviewWindowBuilder::new(app, MAIN_WINDOW_LABEL, WebviewUrl::default())
        .title(MAIN_WINDOW_TITLE)
        .inner_size(MAIN_WINDOW_SIZE.0, MAIN_WINDOW_SIZE.1)
        .min_inner_size(MAIN_WINDOW_MIN_SIZE.0, MAIN_WINDOW_MIN_SIZE.1)
        .theme(initial.mode.forced_theme().map(to_theme))
        .initialization_script(settings::initialization_script(&initial))
        // 邮件正文里的链接：只放行 http、https、mailto 并交给系统默认程序，应用内一律不创建新窗口（design.md D5）
        .on_new_window(move |url, _features| links::on_new_window(&for_links, &url))
        .on_navigation(move |url| links::on_navigation(&for_navigation, url, &loaded))
        .on_page_load(move |_, payload| {
            if payload.event() == PageLoadEvent::Finished {
                for_load.mark();
            }
        })
        .visible(false)
        .build()?;

    // 拖放：drop 时把文件路径作为一个打开批次处理；拖入文字或链接没有路径，得到空批次，什么也不发生。
    // 前端只用 enter / over / leave 事件显示遮罩。
    let for_drop = app.handle().clone();
    window
        .on_window_event(move |event| submit(&for_drop, fileargs::files_from_window_event(event)));

    // 跟随系统时，窗口自己的主题就是当前系统外观
    let system = window
        .theme()
        .map(from_theme)
        .unwrap_or(ResolvedMode::Light);
    let background = initial.window_background(colors, system);
    // 底色设置失败不应该让应用起不来：记录后照常显示窗口
    if let Err(error) =
        window.set_background_color(Some(Color(background.0, background.1, background.2, 255)))
    {
        eprintln!("[efv] failed to set the window background color: {error}");
    }

    // 调试构建下把读到的初始设置打到 stderr，方便不开 Web Inspector 也能确认注入的值
    #[cfg(debug_assertions)]
    eprintln!(
        "[efv] initial settings: {} (store: {}, system locale: {:?}, system theme: {:?}, window background: {})",
        serde_json::to_string(&initial).unwrap_or_default(),
        store_path
            .as_deref()
            .map(|p| p.display().to_string())
            .unwrap_or_else(|| "unavailable".into()),
        system_locale,
        system,
        background.to_hex(),
    );

    window.show()?;
    Ok(initial)
}

/// 把一组文件作为一个打开批次提交。登记之前（极早期的系统事件）提交的先放在 `EARLY` 里。
fn submit(app: &tauri::AppHandle<Wry>, paths: Vec<PathBuf>) {
    match app.try_state::<OpenCoordinator>() {
        Some(coordinator) => coordinator.submit(paths),
        None => EARLY.lock().unwrap().extend(paths),
    }
}

static EARLY: std::sync::Mutex<Vec<PathBuf>> = std::sync::Mutex::new(Vec::new());

/// 把主窗口带到前台：取消最小化、显示并聚焦。
fn show_main_window(app: &tauri::AppHandle<Wry>) {
    if let Some(window) = app.get_webview_window(MAIN_WINDOW_LABEL) {
        let _ = window.unminimize();
        let _ = window.show();
        let _ = window.set_focus();
    }
}

fn log_plugin() -> tauri::plugin::TauriPlugin<Wry> {
    use tauri_plugin_log::{Target, TargetKind};
    let builder = tauri_plugin_log::Builder::new();
    // 调试构建：输出到 stderr，应用自己的模块（包括 sidecar 的 stderr 转发）记到 debug；
    // 发布构建：只记 warn 及以上，写到 stderr 和日志目录
    #[cfg(debug_assertions)]
    let builder = builder
        .targets([Target::new(TargetKind::Stderr)])
        .level(log::LevelFilter::Info)
        .level_for("emailfileviewer_lib", log::LevelFilter::Debug)
        .level_for("efv", log::LevelFilter::Debug);
    #[cfg(not(debug_assertions))]
    let builder = builder
        .targets([
            Target::new(TargetKind::Stderr),
            Target::new(TargetKind::LogDir { file_name: None }),
        ])
        .level(log::LevelFilter::Warn);
    builder.build()
}

pub fn run() {
    tauri::Builder::default()
        // 单实例插件必须第一个注册。第二个实例退出前把它的参数和工作目录转给这里：
        // 解析出文件路径作为一个打开批次，并把主窗口带到前台。
        .plugin(tauri_plugin_single_instance::init(|app, args, cwd| {
            let paths = fileargs::paths_from_args(&args, Path::new(&cwd));
            log::debug!("a second instance was started with {} file(s)", paths.len());
            submit(app, paths);
            show_main_window(app);
        }))
        .plugin(log_plugin())
        .plugin(tauri_plugin_store::Builder::new().build())
        .plugin(tauri_plugin_dialog::init())
        .plugin(tauri_plugin_opener::init())
        // 原生菜单的事件（只有 macOS 会设置菜单）
        .on_menu_event(|app, event| menu::handle(app, event.id().as_ref()))
        .invoke_handler(tauri::generate_handler![
            commands::app_ready,
            commands::open_with_dialog,
            commands::open_nested,
            commands::close_document,
            commands::save_attachment,
            commands::save_all_attachments,
            commands::open_attachment,
        ])
        .setup(|app| {
            // sidecar 在第一次请求时才启动，这里只登记
            let service = SidecarService::new(SidecarConfig::bundled()?);
            app.manage(service.clone());
            let coordinator = OpenCoordinator::for_app(app.handle().clone(), service);
            app.manage(coordinator.clone());
            let initial = create_main_window(app)?;
            // 只有 macOS 设置菜单栏；Windows 和 Linux 不设置，快捷键沿用网页内的处理（D11 补充）
            #[cfg(target_os = "macos")]
            menu::install(app.handle(), initial.language);
            #[cfg(not(target_os = "macos"))]
            let _ = initial;

            // 启动参数里的文件（Windows、Linux 上双击文件，或命令行指定）；macOS 的双击走 RunEvent::Opened。
            // 前端调用 app_ready 之前先排队。
            // 用 args_os：文件名不是合法的 UTF-8 时 std::env::args() 会 panic
            let args: Vec<std::ffi::OsString> = std::env::args_os().collect();
            let cwd = std::env::current_dir().unwrap_or_default();
            let mut paths = fileargs::paths_from_args(&args, &cwd);
            paths.append(&mut EARLY.lock().unwrap());
            coordinator.submit(paths);
            Ok(())
        })
        .build(tauri::generate_context!())
        .expect("error while building tauri application")
        .run(|app, event| match event {
            // 退出时关闭 sidecar 的 stdin，等它自己清理并退出，等不到再强制结束；没启动过则什么也不做
            RunEvent::Exit => app.state::<SidecarService>().shutdown_blocking(),
            // macOS：双击文件、`open -a` 或拖到 Dock 图标上。冷启动时也走这里，前端没准备好之前先排队。
            #[cfg(target_os = "macos")]
            RunEvent::Opened { urls } => {
                submit(app, fileargs::paths_from_urls(&urls));
                show_main_window(app);
            }
            _ => {}
        });
}
