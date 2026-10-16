mod settings;

use settings::{InitialSettings, ResolvedMode, SETTINGS_FILE, WindowColors};
use tauri::window::Color;
use tauri::{Theme, WebviewUrl, WebviewWindowBuilder};

// 主窗口的属性，与原来 tauri.conf.json 中的定义一致
const MAIN_WINDOW_LABEL: &str = "main";
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
// 并把窗口底色设成所选配色方案的底色。窗口先隐藏，设好底色后再显示。
fn create_main_window(app: &tauri::App) -> tauri::Result<()> {
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
        .visible(false)
        .build()?;

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

    window.show()
}

pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_store::Builder::new().build())
        .setup(|app| {
            create_main_window(app)?;
            Ok(())
        })
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
