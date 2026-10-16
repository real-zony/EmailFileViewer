import { DEFAULT_MODE, DEFAULT_PALETTE, isPaletteId } from '../theme/palettes.ts'
import type { Mode } from '../theme/palettes.ts'
import { isLanguage, resolveSystemLanguage, type Language } from './language.ts'
import type { AppSettings } from './types.ts'

declare global {
  interface Window {
    /** 由 Rust 在创建窗口时通过 initialization_script 注入：已保存的设置，或首次启动的默认值 */
    __EFV_INITIAL_SETTINGS__?: unknown
  }
}

export function isMode(value: unknown): value is Mode {
  return value === 'light' || value === 'dark' || value === 'system'
}

export interface InitialEnvironment {
  /** window.__EFV_INITIAL_SETTINGS__；浏览器里直接调试时没有 */
  injected?: unknown
  /** 系统首选语言（navigator.languages[0]），只在没有注入语言时使用 */
  systemLocale?: string | null
}

// 合并注入的设置和默认值：每个字段单独校验，无效或缺失的字段回退到默认值
export function resolveInitialSettings({ injected, systemLocale }: InitialEnvironment): AppSettings {
  const raw: Record<string, unknown> =
    typeof injected === 'object' && injected !== null ? (injected as Record<string, unknown>) : {}
  const language: Language = isLanguage(raw.language) ? raw.language : resolveSystemLanguage(systemLocale)
  const mode: Mode = isMode(raw.mode) ? raw.mode : DEFAULT_MODE
  const palette = typeof raw.palette === 'string' && isPaletteId(raw.palette) ? raw.palette : DEFAULT_PALETTE
  return { language, mode, palette }
}

// 启动时同步读取，必须在首次渲染之前调用
export function readInitialSettings(
  win: Pick<Window, '__EFV_INITIAL_SETTINGS__'> = window,
  nav: Pick<Navigator, 'language' | 'languages'> = navigator,
): AppSettings {
  return resolveInitialSettings({
    injected: win.__EFV_INITIAL_SETTINGS__,
    systemLocale: nav.languages?.[0] ?? nav.language,
  })
}
