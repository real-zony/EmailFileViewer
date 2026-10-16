//! macOS 的原生菜单（design.md D11 的补充，任务 7.10）。
//!
//! 为什么需要：焦点进入正文 iframe 后，键盘事件不会冒泡到应用页面，网页里注册的 ⌘O / ⌘W 会失效；
//! 没有“编辑”菜单，WebView 里也无法用 ⌘C 复制邮件内容。Windows 和 Linux 不设置菜单栏。
//!
//! 菜单项的处理方式：
//! - “打开”由 Rust 直接执行 `open_with_dialog` 的逻辑，不经过前端；
//! - “关闭标签页”和“设置”向主窗口发事件 `efv://close-active-tab`、`efv://open-settings`，由前端处理；
//! - 其余都是系统预定义的菜单项（关于、隐藏、退出、撤销、复制、最小化……），由系统处理。
//!
//! 菜单文字跟随界面语言，只在启动时按已保存的设置（没有就按系统语言）确定一次：
//! 用户在设置里切换语言后，菜单文字要到下次启动才会更新。
//!
//! 菜单项 id 到动作的映射（[`effect_for`]）和各语言的文字（[`Labels`]）不依赖 Tauri 运行时，可以直接单元测试；
//! 菜单本身的构建和事件分发需要真实的应用，由 macOS 上的手工验证覆盖。

// 构建菜单的代码只在 macOS 上调用，其他平台只编译、不使用
#![cfg_attr(not(target_os = "macos"), allow(dead_code))]

use tauri::menu::{AboutMetadataBuilder, Menu, MenuItemBuilder, SubmenuBuilder, WINDOW_SUBMENU_ID};
use tauri::{AppHandle, Emitter, Manager, Wry};

use crate::commands;
use crate::opening::OpenCoordinator;
use crate::settings::Language;

/// 自定义菜单项的 id。预定义的菜单项（复制、退出……）有各自的 id，不在这里。
pub mod id {
    pub const OPEN: &str = "efv.open";
    pub const CLOSE_TAB: &str = "efv.close-tab";
    pub const SETTINGS: &str = "efv.settings";
}

/// 菜单发给前端的事件，与 `ui/src/api/types.ts` 的 `EVENTS` 一致。
pub mod event {
    pub const CLOSE_ACTIVE_TAB: &str = "efv://close-active-tab";
    pub const OPEN_SETTINGS: &str = "efv://open-settings";
}

/// 选中一个菜单项之后要做的事。
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Effect {
    /// 由 Rust 弹出打开文件的对话框（与 `open_with_dialog` 命令是同一段逻辑）
    OpenFiles,
    /// 向主窗口发一个不带内容的事件，由前端处理
    EmitToMain(&'static str),
}

/// 菜单项 id -> 要做的事。不认识的 id（系统预定义的项）返回 `None`，由系统自己处理。
pub fn effect_for(menu_id: &str) -> Option<Effect> {
    match menu_id {
        id::OPEN => Some(Effect::OpenFiles),
        id::CLOSE_TAB => Some(Effect::EmitToMain(event::CLOSE_ACTIVE_TAB)),
        id::SETTINGS => Some(Effect::EmitToMain(event::OPEN_SETTINGS)),
        _ => None,
    }
}

/// 菜单事件的入口：在 `Builder::on_menu_event` 中调用。
pub fn handle(app: &AppHandle<Wry>, menu_id: &str) {
    match effect_for(menu_id) {
        Some(effect) => perform(app, effect),
        None => log::debug!("menu item {menu_id} is handled by the system"),
    }
}

fn perform(app: &AppHandle<Wry>, effect: Effect) {
    log::debug!("menu effect {effect:?}");
    match effect {
        Effect::OpenFiles => {
            let app = app.clone();
            tauri::async_runtime::spawn(async move { open_files(&app).await });
        }
        Effect::EmitToMain(name) => {
            if let Err(error) = app.emit_to(crate::MAIN_WINDOW_LABEL, name, ()) {
                log::warn!("failed to emit {name}: {error}");
            }
        }
    }
}

/// “打开…”：与 `open_with_dialog` 命令共用 [`commands::open_via_dialog`]。
/// 命令出错时前端会弹出通知；菜单没有这条通路，只能记日志。
async fn open_files(app: &AppHandle<Wry>) {
    let Some(window) = app.get_webview_window(crate::MAIN_WINDOW_LABEL) else {
        log::warn!("the main window does not exist, cannot open the file dialog");
        return;
    };
    let coordinator = app.state::<OpenCoordinator>();
    if let Err(error) = commands::open_via_dialog(app, &window, &coordinator).await {
        log::warn!("the open dialog from the menu failed: {error:?}");
    }
}

// ---------------------------------------------------------------- 菜单文字

/// 菜单上的全部文字。macOS 的菜单使用标题式大小写；中文参照系统自带应用的叫法（如“拷贝”）。
#[derive(Debug, PartialEq, Eq)]
pub struct Labels {
    pub app_name: &'static str,
    pub about: &'static str,
    pub settings: &'static str,
    pub hide: &'static str,
    pub quit: &'static str,
    pub file: &'static str,
    pub open: &'static str,
    pub close_tab: &'static str,
    pub edit: &'static str,
    pub undo: &'static str,
    pub redo: &'static str,
    pub cut: &'static str,
    pub copy: &'static str,
    pub paste: &'static str,
    pub select_all: &'static str,
    pub window: &'static str,
    pub minimize: &'static str,
    pub zoom: &'static str,
}

const EN: Labels = Labels {
    app_name: "EmailFileViewer",
    about: "About EmailFileViewer",
    settings: "Settings…",
    hide: "Hide EmailFileViewer",
    quit: "Quit EmailFileViewer",
    file: "File",
    open: "Open…",
    close_tab: "Close Tab",
    edit: "Edit",
    undo: "Undo",
    redo: "Redo",
    cut: "Cut",
    copy: "Copy",
    paste: "Paste",
    select_all: "Select All",
    window: "Window",
    minimize: "Minimize",
    zoom: "Zoom",
};

const ZH_CN: Labels = Labels {
    app_name: "EmailFileViewer",
    about: "关于 EmailFileViewer",
    settings: "设置…",
    hide: "隐藏 EmailFileViewer",
    quit: "退出 EmailFileViewer",
    file: "文件",
    open: "打开…",
    close_tab: "关闭标签页",
    edit: "编辑",
    undo: "撤销",
    redo: "重做",
    cut: "剪切",
    copy: "拷贝",
    paste: "粘贴",
    select_all: "全选",
    window: "窗口",
    minimize: "最小化",
    zoom: "缩放",
};

impl Labels {
    pub fn for_language(language: Language) -> &'static Labels {
        match language {
            Language::En => &EN,
            Language::ZhCn => &ZH_CN,
        }
    }
}

// ---------------------------------------------------------------- 构建

// 加速键。“CmdOrCtrl”在 macOS 上是 ⌘
const ACCELERATOR_OPEN: &str = "CmdOrCtrl+O";
const ACCELERATOR_CLOSE_TAB: &str = "CmdOrCtrl+W";
const ACCELERATOR_SETTINGS: &str = "CmdOrCtrl+,";

/// 构建菜单：应用菜单、文件、编辑、窗口。
/// 窗口菜单用 `WINDOW_SUBMENU_ID`，Tauri 会把它登记为系统的“窗口”菜单。
pub fn build(app: &AppHandle<Wry>, language: Language) -> tauri::Result<Menu<Wry>> {
    let labels = Labels::for_language(language);

    let about = AboutMetadataBuilder::new()
        .name(Some(labels.app_name))
        .version(Some(app.package_info().version.to_string()))
        .copyright(app.config().bundle.copyright.clone())
        .build();

    let settings = MenuItemBuilder::with_id(id::SETTINGS, labels.settings)
        .accelerator(ACCELERATOR_SETTINGS)
        .build(app)?;
    let app_menu = SubmenuBuilder::new(app, labels.app_name)
        .about_with_text(labels.about, Some(about))
        .separator()
        .item(&settings)
        .separator()
        .hide_with_text(labels.hide)
        .separator()
        .quit_with_text(labels.quit)
        .build()?;

    let open = MenuItemBuilder::with_id(id::OPEN, labels.open)
        .accelerator(ACCELERATOR_OPEN)
        .build(app)?;
    let close_tab = MenuItemBuilder::with_id(id::CLOSE_TAB, labels.close_tab)
        .accelerator(ACCELERATOR_CLOSE_TAB)
        .build(app)?;
    let file_menu = SubmenuBuilder::new(app, labels.file)
        .item(&open)
        .item(&close_tab)
        .build()?;

    // 没有这一组，WebView（包括正文 iframe）里的 ⌘C / ⌘V / ⌘A 都无法使用
    let edit_menu = SubmenuBuilder::new(app, labels.edit)
        .undo_with_text(labels.undo)
        .redo_with_text(labels.redo)
        .separator()
        .cut_with_text(labels.cut)
        .copy_with_text(labels.copy)
        .paste_with_text(labels.paste)
        .select_all_with_text(labels.select_all)
        .build()?;

    // 预定义的 maximize 在 macOS 上就是“缩放”
    let window_menu = SubmenuBuilder::with_id(app, WINDOW_SUBMENU_ID, labels.window)
        .minimize_with_text(labels.minimize)
        .maximize_with_text(labels.zoom)
        .build()?;

    Menu::with_items(app, &[&app_menu, &file_menu, &edit_menu, &window_menu])
}

/// 设置为应用菜单。失败时只记日志，沿用 Tauri 的默认菜单，不影响应用启动。
pub fn install(app: &AppHandle<Wry>, language: Language) {
    let installed = build(app, language).and_then(|menu| {
        // 调试构建：把菜单结构打到 stderr。没有辅助功能权限、无法用 System Events 读取菜单栏时，靠它确认菜单已创建
        #[cfg(debug_assertions)]
        match describe(&menu) {
            Ok(text) => eprintln!("[efv] native menu ({language:?}):\n{text}"),
            Err(error) => eprintln!("[efv] could not describe the native menu: {error}"),
        }
        app.set_menu(menu)
    });
    match installed {
        Ok(_) => log::info!("native menu installed ({language:?})"),
        Err(error) => log::error!("failed to install the native menu: {error}"),
    }
}

/// 菜单的结构，每个子菜单一段：项的文字，自定义项后面带 id。
#[cfg(debug_assertions)]
fn describe(menu: &Menu<Wry>) -> tauri::Result<String> {
    use std::fmt::Write;
    use tauri::menu::MenuItemKind;

    let mut out = String::new();
    for kind in menu.items()? {
        let MenuItemKind::Submenu(submenu) = kind else {
            continue;
        };
        let _ = writeln!(out, "  [{}]", submenu.text()?);
        for item in submenu.items()? {
            match item {
                MenuItemKind::MenuItem(item) => {
                    let _ = writeln!(out, "      {} ({})", item.text()?, item.id().as_ref());
                }
                MenuItemKind::Predefined(item) => match item.text()? {
                    text if text.is_empty() => out.push_str("      ----\n"),
                    text => {
                        let _ = writeln!(out, "      {text}");
                    }
                },
                _ => {}
            }
        }
    }
    Ok(out)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn open_runs_the_dialog_in_rust_without_involving_the_frontend() {
        assert_eq!(effect_for(id::OPEN), Some(Effect::OpenFiles));
    }

    #[test]
    fn close_tab_and_settings_are_forwarded_to_the_frontend_as_events() {
        assert_eq!(
            effect_for(id::CLOSE_TAB),
            Some(Effect::EmitToMain("efv://close-active-tab"))
        );
        assert_eq!(
            effect_for(id::SETTINGS),
            Some(Effect::EmitToMain("efv://open-settings"))
        );
    }

    #[test]
    fn items_we_do_not_own_are_left_to_the_system() {
        for foreign in ["", "quit", "copy", "efv.unknown", "EFV.OPEN", "efv.open "] {
            assert_eq!(effect_for(foreign), None, "{foreign:?}");
        }
    }

    #[test]
    fn every_custom_item_has_a_distinct_id_with_exactly_one_effect() {
        let ids = [id::OPEN, id::CLOSE_TAB, id::SETTINGS];
        for (i, a) in ids.iter().enumerate() {
            assert!(effect_for(a).is_some(), "{a} has no effect");
            for b in &ids[i + 1..] {
                assert_ne!(a, b);
            }
        }
        // 事件只发两种，且名字与 D11 补充一致
        let events: Vec<_> = ids
            .iter()
            .filter_map(|i| match effect_for(i) {
                Some(Effect::EmitToMain(name)) => Some(name),
                _ => None,
            })
            .collect();
        assert_eq!(events, ["efv://close-active-tab", "efv://open-settings"]);
    }

    #[test]
    fn the_events_match_the_names_the_frontend_listens_for() {
        // ui/src/api/types.ts 的 EVENTS 里写着同样的两个字符串
        let types = include_str!("../../ui/src/api/types.ts");
        for name in [event::CLOSE_ACTIVE_TAB, event::OPEN_SETTINGS] {
            assert!(
                types.contains(&format!("'{name}'")),
                "{name} is not in ui/src/api/types.ts"
            );
        }
    }

    fn all_labels(l: &Labels) -> [&'static str; 18] {
        [
            l.app_name,
            l.about,
            l.settings,
            l.hide,
            l.quit,
            l.file,
            l.open,
            l.close_tab,
            l.edit,
            l.undo,
            l.redo,
            l.cut,
            l.copy,
            l.paste,
            l.select_all,
            l.window,
            l.minimize,
            l.zoom,
        ]
    }

    #[test]
    fn every_label_is_filled_in_both_languages() {
        for language in [Language::En, Language::ZhCn] {
            for label in all_labels(Labels::for_language(language)) {
                assert!(!label.trim().is_empty(), "{language:?} has an empty label");
            }
        }
    }

    #[test]
    fn labels_follow_the_interface_language() {
        let en = Labels::for_language(Language::En);
        assert_eq!(en.open, "Open…");
        assert_eq!(en.close_tab, "Close Tab");
        assert_eq!(en.settings, "Settings…");

        let zh = Labels::for_language(Language::ZhCn);
        assert_eq!(zh.open, "打开…");
        assert_eq!(zh.close_tab, "关闭标签页");
        assert_eq!(zh.settings, "设置…");

        // 除应用名外，中文菜单里不应该残留英文单词（应用名出现在“关于”“隐藏”“退出”里）
        for label in all_labels(zh) {
            let without_name = label.replace("EmailFileViewer", "");
            assert!(
                !without_name.chars().any(|c| c.is_ascii_alphabetic()),
                "{label:?} still has English text"
            );
        }
    }

    #[test]
    fn the_app_name_is_the_same_in_every_language() {
        assert_eq!(EN.app_name, ZH_CN.app_name);
        assert_eq!(EN.app_name, "EmailFileViewer");
    }

    #[test]
    fn accelerators_follow_macos_conventions() {
        assert_eq!(ACCELERATOR_OPEN, "CmdOrCtrl+O");
        assert_eq!(ACCELERATOR_CLOSE_TAB, "CmdOrCtrl+W");
        assert_eq!(ACCELERATOR_SETTINGS, "CmdOrCtrl+,");
    }
}
