import type { Mode, PaletteId } from '../theme/palettes.ts'
import { nativeTheme, resolveMode, windowBackground } from './appearance.ts'

// 原生窗口的控制：底色、标题栏主题和窗口标题。正式运行时由 Tauri 实现（见 platform/tauri.ts）。
export interface WindowControl {
  setBackgroundColor(color: string): Promise<void>
  /** null 表示跟随系统 */
  setTheme(theme: 'light' | 'dark' | null): Promise<void>
  setTitle(title: string): Promise<void>
}

// 把当前模式和配色同步到原生窗口。两项互不依赖，一项失败不影响另一项。
export async function syncWindow(control: WindowControl, mode: Mode, palette: PaletteId, systemDark: boolean): Promise<void> {
  const color = windowBackground(palette, resolveMode(mode, systemDark))
  const results = await Promise.allSettled([control.setTheme(nativeTheme(mode)), control.setBackgroundColor(color)])
  for (const r of results) {
    if (r.status === 'rejected') console.error('Failed to update the native window', r.reason)
  }
}

const DARK_QUERY = '(prefers-color-scheme: dark)'

// 订阅系统外观变化。没有 matchMedia（例如测试环境）时只返回当前值为浅色。
export function watchSystemDark(onChange: (dark: boolean) => void): { current: boolean; stop: () => void } {
  if (typeof window.matchMedia !== 'function') return { current: false, stop: () => {} }
  const query = window.matchMedia(DARK_QUERY)
  const listener = (event: MediaQueryListEvent) => onChange(event.matches)
  query.addEventListener('change', listener)
  return { current: query.matches, stop: () => query.removeEventListener('change', listener) }
}
