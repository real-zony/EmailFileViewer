import type { Mode, PaletteId } from '../theme/palettes.ts'
import type { Language } from './language.ts'

export interface AppSettings {
  language: Language
  mode: Mode
  palette: PaletteId
}

// store 文件中的键名，与 Rust 侧 src-tauri/src/settings.rs 一致
export const SETTING_KEYS = ['language', 'mode', 'palette'] as const satisfies readonly (keyof AppSettings)[]
export type SettingKey = (typeof SETTING_KEYS)[number]
