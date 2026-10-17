//! 首屏前的初始设置：创建主窗口之前，读取已保存的设置（store 插件的 JSON 文件）和系统语言，
//! 生成要注入页面的 `initialization_script`，并决定窗口底色，避免启动时语言和主题闪变。
//!
//! 这里全是不依赖 Tauri 运行时的纯函数，窗口创建在 `lib.rs`。

use std::collections::BTreeMap;
use std::fs;
use std::path::Path;
use std::sync::OnceLock;

use serde::{Deserialize, Serialize};
use serde_json::Value;

/// store 文件名。相对路径落在应用数据目录下，前端用 `LazyStore('settings.json')` 读写同一个文件。
/// Windows 绿色版的应用数据目录是 exe 旁边的 `data`（见 `portable.rs`），这里和前端都不用改。
pub const SETTINGS_FILE: &str = "settings.json";

/// 初始化脚本模板，占位符在 [`initialization_script`] 中替换。前端有测试在 jsdom 里执行同一个文件。
const SCRIPT_TEMPLATE: &str = include_str!("initial_settings.js");
const SCRIPT_PLACEHOLDER: &str = "__EFV_SETTINGS_JSON__";

/// 各配色方案在浅色和深色下的窗口底色，由 `ui/scripts/build-themes.ts` 根据 `palettes.ts` 生成。
const WINDOW_COLORS_JSON: &str = include_str!("window_colors.json");

// ---------------------------------------------------------------- 设置项

/// 界面语言。取值与前端 `ui/src/settings/language.ts` 一致。
#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize)]
pub enum Language {
    #[serde(rename = "en")]
    En,
    #[serde(rename = "zh-CN")]
    ZhCn,
}

impl Language {
    pub fn parse(value: &str) -> Option<Self> {
        match value {
            "en" => Some(Self::En),
            "zh-CN" => Some(Self::ZhCn),
            _ => None,
        }
    }
}

/// 外观模式：浅色、深色、跟随系统。
#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize)]
#[serde(rename_all = "lowercase")]
pub enum Mode {
    Light,
    Dark,
    System,
}

impl Mode {
    pub fn parse(value: &str) -> Option<Self> {
        match value {
            "light" => Some(Self::Light),
            "dark" => Some(Self::Dark),
            "system" => Some(Self::System),
            _ => None,
        }
    }

    /// 固定模式下强制原生窗口使用对应主题（标题栏随之变化）；跟随系统时返回 `None`。
    pub fn forced_theme(self) -> Option<ResolvedMode> {
        match self {
            Self::Light => Some(ResolvedMode::Light),
            Self::Dark => Some(ResolvedMode::Dark),
            Self::System => None,
        }
    }
}

/// 解析后的具体明暗。
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum ResolvedMode {
    Light,
    Dark,
}

/// store 文件里读到的原始值。字段缺失或类型不对都当作没有保存过。
#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub struct StoredSettings {
    pub language: Option<String>,
    pub mode: Option<String>,
    pub palette: Option<String>,
}

/// 解析 store 文件内容。store 插件写出的是一个扁平的 JSON 对象，例如
/// `{"language": "zh-CN", "mode": "dark", "palette": "plum"}`。内容损坏时按没有保存过处理。
pub fn parse_stored(text: &str) -> StoredSettings {
    let Ok(Value::Object(map)) = serde_json::from_str::<Value>(text) else {
        return StoredSettings::default();
    };
    let get = |key: &str| map.get(key).and_then(Value::as_str).map(str::to_owned);
    StoredSettings {
        language: get("language"),
        mode: get("mode"),
        palette: get("palette"),
    }
}

/// 读取 store 文件。文件不存在（首次启动）或读不了时返回空设置。
pub fn read_stored(path: &Path) -> StoredSettings {
    fs::read_to_string(path)
        .map(|text| parse_stored(&text))
        .unwrap_or_default()
}

// ---------------------------------------------------------------- 系统语言

/// 繁体中文没有对应资源，这些地区的中文按英文处理。
const TRADITIONAL_REGIONS: [&str; 3] = ["tw", "hk", "mo"];

/// 系统首选语言 -> 界面语言。规则与前端 `resolveSystemLanguage` 完全相同（共用 `language-cases.json` 做测试）：
/// 只有简体中文使用中文界面，其他一律英文。
/// - 有文字子标签时以它为准：`Hans` 为简体，其他（`Hant` 等）为繁体；
/// - 没有文字子标签时，TW / HK / MO 为繁体，其余（含只写 `zh`）为简体；
/// - 兼容 `zh_CN`、`zh_CN.UTF-8`、`zh_CN@pinyin` 这类 POSIX 写法，大小写不敏感。
pub fn resolve_language(locale: Option<&str>) -> Language {
    let Some(locale) = locale else {
        return Language::En;
    };
    let head = locale.split(['.', '@']).next().unwrap_or("");
    let tag = head.replace('_', "-").to_ascii_lowercase();
    let mut parts = tag.split('-');
    if parts.next() != Some("zh") {
        return Language::En;
    }

    let mut script: Option<&str> = None;
    let mut region: Option<&str> = None;
    for sub in parts {
        if sub.len() == 1 {
            break; // 扩展或私有用途子标签，后面的都不看
        }
        let alpha = sub.bytes().all(|b| b.is_ascii_lowercase());
        let digits = sub.bytes().all(|b| b.is_ascii_digit());
        if script.is_none() && region.is_none() && sub.len() == 4 && alpha {
            script = Some(sub);
        } else if region.is_none() && ((sub.len() == 2 && alpha) || (sub.len() == 3 && digits)) {
            region = Some(sub);
        }
    }

    match (script, region) {
        (Some("hans"), _) => Language::ZhCn,
        (Some(_), _) => Language::En,
        (None, Some(region)) if TRADITIONAL_REGIONS.contains(&region) => Language::En,
        (None, _) => Language::ZhCn,
    }
}

// ---------------------------------------------------------------- 窗口底色

/// 一个 RGB 颜色。
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct Rgb(pub u8, pub u8, pub u8);

impl Rgb {
    /// 解析 `#rrggbb`。
    pub fn parse(hex: &str) -> Option<Self> {
        let digits = hex.strip_prefix('#')?;
        if digits.len() != 6 || !digits.is_ascii() {
            return None;
        }
        let channel = |i: usize| u8::from_str_radix(&digits[i..i + 2], 16).ok();
        Some(Self(channel(0)?, channel(2)?, channel(4)?))
    }

    /// 只用于调试日志和测试。
    #[cfg(any(test, debug_assertions))]
    pub fn to_hex(self) -> String {
        format!("#{:02x}{:02x}{:02x}", self.0, self.1, self.2)
    }
}

#[derive(Debug, Deserialize)]
struct ModeColors {
    light: String,
    dark: String,
}

#[derive(Debug, Deserialize)]
struct WindowColorsFile {
    default: String,
    palettes: BTreeMap<String, ModeColors>,
}

/// 窗口底色表：配色方案 id -> 浅色和深色下的底色（该方案的 `desk` 令牌）。
#[derive(Debug)]
pub struct WindowColors {
    default: String,
    palettes: BTreeMap<String, (Rgb, Rgb)>,
}

impl WindowColors {
    /// 解析 `window_colors.json`。默认方案不在表里、或颜色不是 `#rrggbb` 都视为无效。
    pub fn parse(json: &str) -> Result<Self, String> {
        let file: WindowColorsFile = serde_json::from_str(json).map_err(|e| e.to_string())?;
        let mut palettes = BTreeMap::new();
        for (id, colors) in file.palettes {
            let light = Rgb::parse(&colors.light)
                .ok_or_else(|| format!("{id}: invalid light color {}", colors.light))?;
            let dark = Rgb::parse(&colors.dark)
                .ok_or_else(|| format!("{id}: invalid dark color {}", colors.dark))?;
            palettes.insert(id, (light, dark));
        }
        if !palettes.contains_key(&file.default) {
            return Err(format!("default palette {} is not defined", file.default));
        }
        Ok(Self {
            default: file.default,
            palettes,
        })
    }

    /// 编译进程序的那份表。内容在编译期固定，测试保证它有效。
    pub fn embedded() -> &'static Self {
        static COLORS: OnceLock<WindowColors> = OnceLock::new();
        COLORS
            .get_or_init(|| Self::parse(WINDOW_COLORS_JSON).expect("window_colors.json is invalid"))
    }

    pub fn default_palette(&self) -> &str {
        &self.default
    }

    pub fn contains(&self, palette: &str) -> bool {
        self.palettes.contains_key(palette)
    }

    #[cfg(test)]
    pub fn palette_ids(&self) -> impl Iterator<Item = &str> {
        self.palettes.keys().map(String::as_str)
    }

    pub fn background(&self, palette: &str, mode: ResolvedMode) -> Option<Rgb> {
        let (light, dark) = self.palettes.get(palette)?;
        Some(match mode {
            ResolvedMode::Light => *light,
            ResolvedMode::Dark => *dark,
        })
    }
}

// ---------------------------------------------------------------- 初始设置

/// 注入页面的初始设置，字段名与前端的 `AppSettings` 一致。
#[derive(Clone, Debug, PartialEq, Eq, Serialize)]
pub struct InitialSettings {
    pub language: Language,
    pub mode: Mode,
    pub palette: String,
}

impl InitialSettings {
    /// 已保存的设置优先；没有保存（或值无效）的字段用默认值：
    /// 语言按系统语言判断，模式为跟随系统，配色为默认方案。
    pub fn resolve(
        stored: &StoredSettings,
        system_locale: Option<&str>,
        colors: &WindowColors,
    ) -> Self {
        let language = stored
            .language
            .as_deref()
            .and_then(Language::parse)
            .unwrap_or_else(|| resolve_language(system_locale));
        let mode = stored
            .mode
            .as_deref()
            .and_then(Mode::parse)
            .unwrap_or(Mode::System);
        let palette = stored
            .palette
            .as_deref()
            .filter(|id| colors.contains(id))
            .unwrap_or_else(|| colors.default_palette())
            .to_owned();
        Self {
            language,
            mode,
            palette,
        }
    }

    /// 窗口底色。跟随系统时由调用方传入窗口当前的系统外观。
    pub fn window_background(&self, colors: &WindowColors, system: ResolvedMode) -> Rgb {
        let mode = self.mode.forced_theme().unwrap_or(system);
        colors
            .background(&self.palette, mode)
            .expect("palette was validated in resolve()")
    }
}

/// 生成 `initialization_script`：注入 `window.__EFV_INITIAL_SETTINGS__`，并直接在 `<html>` 上设置
/// `data-mode`、`data-palette` 和 `lang`。`InitialSettings` 的每个字段都是已校验的枚举值，不含需要转义的内容。
pub fn initialization_script(settings: &InitialSettings) -> String {
    let json = serde_json::to_string(settings).expect("settings are serializable");
    SCRIPT_TEMPLATE.replace(SCRIPT_PLACEHOLDER, &json)
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::path::PathBuf;

    fn colors() -> &'static WindowColors {
        WindowColors::embedded()
    }

    fn stored(language: Option<&str>, mode: Option<&str>, palette: Option<&str>) -> StoredSettings {
        StoredSettings {
            language: language.map(str::to_owned),
            mode: mode.map(str::to_owned),
            palette: palette.map(str::to_owned),
        }
    }

    /// 每个测试用自己的目录，结束后清理。
    struct TempDir(PathBuf);
    impl TempDir {
        fn new(name: &str) -> Self {
            let dir =
                std::env::temp_dir().join(format!("efv-settings-{}-{name}", std::process::id()));
            let _ = fs::remove_dir_all(&dir);
            fs::create_dir_all(&dir).unwrap();
            Self(dir)
        }
    }
    impl Drop for TempDir {
        fn drop(&mut self) {
            let _ = fs::remove_dir_all(&self.0);
        }
    }

    // ---- 8.1 系统语言

    #[derive(Deserialize)]
    struct LanguageCase {
        locale: Option<String>,
        expected: String,
    }

    #[test]
    fn shared_language_cases_match_the_frontend() {
        // 与前端 resolveSystemLanguage 共用同一份用例，保证两边的判断一致
        let cases: Vec<LanguageCase> =
            serde_json::from_str(include_str!("../../ui/src/settings/language-cases.json"))
                .unwrap();
        assert!(cases.len() >= 25);
        for case in cases {
            let expected = Language::parse(&case.expected).unwrap();
            assert_eq!(
                resolve_language(case.locale.as_deref()),
                expected,
                "{:?}",
                case.locale
            );
        }
    }

    #[test]
    fn spec_scenario_system_language_zh_cn_uses_simplified_chinese() {
        assert_eq!(resolve_language(Some("zh-CN")), Language::ZhCn);
    }

    #[test]
    fn spec_scenario_system_language_de_de_uses_english() {
        assert_eq!(resolve_language(Some("de-DE")), Language::En);
    }

    #[test]
    fn simplified_variants_use_chinese_and_traditional_variants_use_english() {
        for tag in [
            "zh-Hans",
            "zh-CN",
            "zh-Hans-CN",
            "zh-SG",
            "zh",
            "zh_CN.UTF-8",
            "ZH-cn",
        ] {
            assert_eq!(resolve_language(Some(tag)), Language::ZhCn, "{tag}");
        }
        for tag in [
            "zh-TW",
            "zh-HK",
            "zh-MO",
            "zh-Hant",
            "zh-Hant-TW",
            "zh-Hant-CN",
        ] {
            assert_eq!(resolve_language(Some(tag)), Language::En, "{tag}");
        }
        assert_eq!(resolve_language(Some("zh-Hans-HK")), Language::ZhCn);
        assert_eq!(resolve_language(None), Language::En);
        assert_eq!(resolve_language(Some("")), Language::En);
    }

    // ---- 读取 store 文件

    #[test]
    fn parses_the_file_written_by_the_store_plugin() {
        // 插件默认写出缩进的扁平 JSON 对象
        let text =
            "{\n  \"language\": \"zh-CN\",\n  \"mode\": \"dark\",\n  \"palette\": \"plum\"\n}";
        assert_eq!(
            parse_stored(text),
            stored(Some("zh-CN"), Some("dark"), Some("plum"))
        );
    }

    #[test]
    fn ignores_unknown_keys_and_wrong_types() {
        let text = r#"{"language": 5, "mode": null, "palette": ["plum"], "other": "x"}"#;
        assert_eq!(parse_stored(text), StoredSettings::default());
        assert_eq!(
            parse_stored(r#"{"mode": "dark", "extra": 1}"#),
            stored(None, Some("dark"), None)
        );
    }

    #[test]
    fn corrupt_or_unexpected_content_counts_as_nothing_saved() {
        for text in ["", "not json", "[1, 2]", "\"dark\"", "null", "{\"mode\": "] {
            assert_eq!(parse_stored(text), StoredSettings::default(), "{text:?}");
        }
    }

    #[test]
    fn read_stored_reads_a_file_and_tolerates_a_missing_one() {
        let dir = TempDir::new("read");
        let path = dir.0.join(SETTINGS_FILE);
        assert_eq!(read_stored(&path), StoredSettings::default());
        fs::write(
            &path,
            r#"{"language":"en","mode":"light","palette":"graphite"}"#,
        )
        .unwrap();
        assert_eq!(
            read_stored(&path),
            stored(Some("en"), Some("light"), Some("graphite"))
        );
        fs::write(&path, "garbage").unwrap();
        assert_eq!(read_stored(&path), StoredSettings::default());
    }

    // ---- 初始设置

    #[test]
    fn first_launch_uses_the_system_language_and_defaults() {
        let nothing = StoredSettings::default();
        let zh = InitialSettings::resolve(&nothing, Some("zh-CN"), colors());
        assert_eq!(
            (zh.language, zh.mode, zh.palette.as_str()),
            (Language::ZhCn, Mode::System, "verdigris")
        );
        let de = InitialSettings::resolve(&nothing, Some("de-DE"), colors());
        assert_eq!(
            (de.language, de.mode, de.palette.as_str()),
            (Language::En, Mode::System, "verdigris")
        );
        assert_eq!(
            InitialSettings::resolve(&nothing, None, colors()).language,
            Language::En
        );
    }

    #[test]
    fn saved_settings_win_over_the_system_language() {
        let s = InitialSettings::resolve(
            &stored(Some("en"), Some("dark"), Some("plum")),
            Some("zh-CN"),
            colors(),
        );
        assert_eq!(
            (s.language, s.mode, s.palette.as_str()),
            (Language::En, Mode::Dark, "plum")
        );
        let s = InitialSettings::resolve(
            &stored(Some("zh-CN"), Some("light"), Some("indigo")),
            Some("de-DE"),
            colors(),
        );
        assert_eq!(
            (s.language, s.mode, s.palette.as_str()),
            (Language::ZhCn, Mode::Light, "indigo")
        );
    }

    #[test]
    fn invalid_saved_values_fall_back_field_by_field() {
        // 语言写成 "zh"（不是支持的取值）：回退到系统语言；模式有效则保留；未知配色回退到默认方案
        let s = InitialSettings::resolve(
            &stored(Some("zh"), Some("dark"), Some("neon")),
            Some("zh-CN"),
            colors(),
        );
        assert_eq!(
            (s.language, s.mode, s.palette.as_str()),
            (Language::ZhCn, Mode::Dark, "verdigris")
        );
        let s = InitialSettings::resolve(
            &stored(Some("fr"), Some("sepia"), Some("")),
            Some("de-DE"),
            colors(),
        );
        assert_eq!(
            (s.language, s.mode, s.palette.as_str()),
            (Language::En, Mode::System, "verdigris")
        );
    }

    // ---- 窗口底色

    #[test]
    fn embedded_window_colors_are_valid() {
        let colors = colors();
        assert!(colors.contains(colors.default_palette()));
        assert_eq!(colors.default_palette(), "verdigris");
        let count = colors.palette_ids().count();
        assert!(
            (4..=6).contains(&count),
            "规格要求 4 到 6 套方案，实际 {count}"
        );
        for id in colors.palette_ids() {
            assert!(colors.background(id, ResolvedMode::Light).is_some(), "{id}");
            assert!(colors.background(id, ResolvedMode::Dark).is_some(), "{id}");
        }
        assert_eq!(colors.background("neon", ResolvedMode::Light), None);
    }

    #[test]
    fn window_colors_match_known_palette_values() {
        // 与 ui/src/theme/palettes.ts 的 desk 一致；完整一致性由前端的 themes:check 和测试保证
        let c = colors();
        assert_eq!(
            c.background("verdigris", ResolvedMode::Light)
                .unwrap()
                .to_hex(),
            "#e6eae8"
        );
        assert_eq!(
            c.background("verdigris", ResolvedMode::Dark)
                .unwrap()
                .to_hex(),
            "#121715"
        );
        assert_eq!(
            c.background("plum", ResolvedMode::Dark).unwrap().to_hex(),
            "#18141a"
        );
        assert_eq!(
            c.background("contrast", ResolvedMode::Light).unwrap(),
            Rgb(255, 255, 255)
        );
        assert_eq!(
            c.background("contrast", ResolvedMode::Dark).unwrap(),
            Rgb(0, 0, 0)
        );
    }

    #[test]
    fn window_colors_reject_bad_input() {
        assert!(WindowColors::parse("{}").is_err());
        assert!(
            WindowColors::parse(
                r##"{"default":"a","palettes":{"b":{"light":"#ffffff","dark":"#000000"}}}"##
            )
            .is_err()
        );
        assert!(
            WindowColors::parse(
                r##"{"default":"a","palettes":{"a":{"light":"white","dark":"#000000"}}}"##
            )
            .is_err()
        );
        assert!(
            WindowColors::parse(
                r##"{"default":"a","palettes":{"a":{"light":"#ffffff","dark":"#00000"}}}"##
            )
            .is_err()
        );
        assert!(
            WindowColors::parse(
                r##"{"default":"a","palettes":{"a":{"light":"#ffffff","dark":"#000000"}}}"##
            )
            .is_ok()
        );
    }

    #[test]
    fn rgb_parses_and_formats_hex() {
        assert_eq!(Rgb::parse("#0a1B2c"), Some(Rgb(10, 27, 44)));
        assert_eq!(Rgb(10, 27, 44).to_hex(), "#0a1b2c");
        for bad in ["", "#", "#fff", "0a1b2c", "#0a1b2g", "#0a1b2c0", "#é0a1b"] {
            assert_eq!(Rgb::parse(bad), None, "{bad:?}");
        }
    }

    #[test]
    fn window_background_follows_the_mode() {
        let c = colors();
        let make = |mode| InitialSettings {
            language: Language::En,
            mode,
            palette: "plum".to_owned(),
        };
        // 固定模式不看系统外观
        assert_eq!(
            make(Mode::Light)
                .window_background(c, ResolvedMode::Dark)
                .to_hex(),
            "#ebe7e4"
        );
        assert_eq!(
            make(Mode::Dark)
                .window_background(c, ResolvedMode::Light)
                .to_hex(),
            "#18141a"
        );
        // 跟随系统时按窗口当前的系统外观
        assert_eq!(
            make(Mode::System)
                .window_background(c, ResolvedMode::Light)
                .to_hex(),
            "#ebe7e4"
        );
        assert_eq!(
            make(Mode::System)
                .window_background(c, ResolvedMode::Dark)
                .to_hex(),
            "#18141a"
        );
    }

    #[test]
    fn fixed_modes_force_the_native_theme_and_system_does_not() {
        assert_eq!(Mode::Light.forced_theme(), Some(ResolvedMode::Light));
        assert_eq!(Mode::Dark.forced_theme(), Some(ResolvedMode::Dark));
        assert_eq!(Mode::System.forced_theme(), None);
    }

    // ---- 初始化脚本

    /// 从脚本里取出注入的 JSON（`var settings = {...};` 这一行）。
    fn injected_json(script: &str) -> Value {
        let start =
            script.find("var settings = ").expect("settings assignment") + "var settings = ".len();
        let end = start + script[start..].find(";\n").expect("end of assignment");
        serde_json::from_str(&script[start..end]).expect("injected value is valid JSON")
    }

    #[test]
    fn script_injects_the_settings_object() {
        let s = InitialSettings {
            language: Language::ZhCn,
            mode: Mode::Dark,
            palette: "plum".to_owned(),
        };
        let script = initialization_script(&s);
        assert!(!script.contains(SCRIPT_PLACEHOLDER));
        assert_eq!(
            injected_json(&script),
            serde_json::json!({"language": "zh-CN", "mode": "dark", "palette": "plum"})
        );
        assert!(script.contains("window.__EFV_INITIAL_SETTINGS__ = settings"));
    }

    #[test]
    fn script_sets_the_html_attributes() {
        let script = initialization_script(&InitialSettings::resolve(
            &StoredSettings::default(),
            None,
            colors(),
        ));
        for attribute in ["'data-mode'", "'data-palette'", "'lang'"] {
            assert!(
                script.contains(&format!("setAttribute({attribute}")),
                "{attribute}"
            );
        }
        // <html> 可能还没创建，需要等它出现
        assert!(script.contains("MutationObserver"));
    }

    #[test]
    fn script_for_every_combination_embeds_valid_values() {
        for language in [Language::En, Language::ZhCn] {
            for mode in [Mode::Light, Mode::Dark, Mode::System] {
                for id in colors().palette_ids() {
                    let s = InitialSettings {
                        language,
                        mode,
                        palette: id.to_owned(),
                    };
                    let value = injected_json(&initialization_script(&s));
                    assert_eq!(value, serde_json::to_value(&s).unwrap());
                    assert!(Language::parse(value["language"].as_str().unwrap()).is_some());
                    assert!(Mode::parse(value["mode"].as_str().unwrap()).is_some());
                    assert!(colors().contains(value["palette"].as_str().unwrap()));
                }
            }
        }
    }

    #[test]
    fn manually_edited_store_file_is_reflected_in_the_script() {
        // 手动把 store 文件改成 mode=dark、palette=plum 后重启，注入的就是新值
        let dir = TempDir::new("edited");
        let path = dir.0.join(SETTINGS_FILE);
        fs::write(
            &path,
            "{\n  \"mode\": \"dark\",\n  \"palette\": \"plum\"\n}",
        )
        .unwrap();
        let settings = InitialSettings::resolve(&read_stored(&path), Some("zh-Hans-CN"), colors());
        let value = injected_json(&initialization_script(&settings));
        assert_eq!(
            value,
            serde_json::json!({"language": "zh-CN", "mode": "dark", "palette": "plum"})
        );
    }
}
