import { PALETTES, type Mode, type PaletteId, type ResolvedMode } from '../theme/palettes.ts'
import type { AppSettings } from './types.ts'

// 把设置写到 <html> 上：明暗模式、配色方案和界面语言。themes.css 和 app.css 只认这三个属性。
export function applyDocumentSettings(root: HTMLElement, settings: AppSettings): void {
  root.setAttribute('data-mode', settings.mode)
  root.setAttribute('data-palette', settings.palette)
  root.setAttribute('lang', settings.language)
}

// 跟随系统时按系统外观解析成具体的浅色或深色
export function resolveMode(mode: Mode, systemDark: boolean): ResolvedMode {
  if (mode === 'system') return systemDark ? 'dark' : 'light'
  return mode
}

// 窗口底色：该配色方案当前模式下的 desk，与 <body> 的背景一致
export function windowBackground(palette: PaletteId, resolved: ResolvedMode): string {
  const found = PALETTES.find((p) => p.id === palette)
  if (!found) throw new Error(`unknown palette: ${palette}`)
  return found[resolved].desk
}

// 窗口原生标题栏的主题：固定浅色或深色时强制，跟随系统时交还给系统
export function nativeTheme(mode: Mode): 'light' | 'dark' | null {
  return mode === 'system' ? null : mode
}
