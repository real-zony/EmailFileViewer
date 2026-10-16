// WCAG 2.x 对比度检查（纯函数）。scripts/check-contrast.ts 和测试共用。
import { PALETTES, type ColorToken, type Palette, type ResolvedMode } from './palettes.ts'

export type ContrastLevel = 'text' | 'ui'

// 文字 ≥ 4.5:1；图标、焦点环、大号文字 ≥ 3:1
export const MIN_RATIO: Record<ContrastLevel, number> = { text: 4.5, ui: 3 }

export interface ContrastPair {
  fg: ColorToken
  bg: ColorToken
  level: ContrastLevel
  use: string
}

// 每一组都对应界面里的一个真实用法
export const CONTRAST_PAIRS: readonly ContrastPair[] = [
  { fg: 'ink', bg: 'desk', level: 'text', use: 'body text on window ground' },
  { fg: 'ink', bg: 'surface', level: 'text', use: 'text on panels' },
  { fg: 'ink', bg: 'raised', level: 'text', use: 'text on cards and dialogs' },
  { fg: 'ink', bg: 'sunken', level: 'text', use: 'text near the tab strip' },
  { fg: 'ink', bg: 'accent-soft', level: 'text', use: 'selected option label' },
  { fg: 'ink-2', bg: 'desk', level: 'text', use: 'secondary text, header labels' },
  { fg: 'ink-2', bg: 'surface', level: 'text', use: 'body label, field hints' },
  { fg: 'ink-2', bg: 'raised', level: 'text', use: 'sizes, captions on cards' },
  { fg: 'ink-2', bg: 'sunken', level: 'text', use: 'inactive tab labels' },
  { fg: 'accent', bg: 'desk', level: 'text', use: 'link buttons' },
  { fg: 'accent', bg: 'surface', level: 'text', use: 'links on panels' },
  { fg: 'accent', bg: 'raised', level: 'text', use: 'links in dialogs' },
  { fg: 'accent-ink', bg: 'accent', level: 'text', use: 'primary button label' },
  { fg: 'accent-ink', bg: 'accent-hover', level: 'text', use: 'primary button label, hover' },
  { fg: 'accent-soft-ink', bg: 'accent-soft', level: 'text', use: 'drop state title, message badge' },
  { fg: 'warn-ink', bg: 'warn-bg', level: 'text', use: 'remote content notice' },
  { fg: 'warn-ink', bg: 'raised', level: 'text', use: 'warn button, Save only flag' },
  { fg: 'danger-ink', bg: 'danger-bg', level: 'text', use: 'error text on tint' },
  { fg: 'danger-ink', bg: 'desk', level: 'text', use: 'error reason label' },
  { fg: 'ink-3', bg: 'desk', level: 'ui', use: 'icons on window ground' },
  { fg: 'ink-3', bg: 'surface', level: 'ui', use: 'icons on panels' },
  { fg: 'ink-3', bg: 'raised', level: 'ui', use: 'icons on cards' },
  { fg: 'ink-3', bg: 'sunken', level: 'ui', use: 'tab icons' },
  { fg: 'focus', bg: 'desk', level: 'ui', use: 'focus ring' },
  { fg: 'focus', bg: 'surface', level: 'ui', use: 'focus ring on panels' },
  { fg: 'focus', bg: 'raised', level: 'ui', use: 'focus ring on cards' },
  { fg: 'focus', bg: 'sunken', level: 'ui', use: 'focus ring in tab strip' },
  { fg: 'accent', bg: 'accent-soft', level: 'ui', use: 'selected option border' },
  { fg: 'warn-icon', bg: 'warn-bg', level: 'ui', use: 'notice icon' },
  { fg: 'danger-icon', bg: 'danger-bg', level: 'ui', use: 'error icon' },
  { fg: 'danger-icon', bg: 'raised', level: 'ui', use: 'toast icon and edge' },
]

export interface ContrastResult extends ContrastPair {
  palette: string
  mode: ResolvedMode
  fgColor: string
  bgColor: string
  ratio: number
  ok: boolean
}

function channel(hex: string, i: number): number {
  const v = parseInt(hex.slice(1 + i * 2, 3 + i * 2), 16) / 255
  return v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4
}

export function relativeLuminance(hex: string): number {
  if (!/^#[0-9a-f]{6}$/i.test(hex)) throw new Error(`expected #rrggbb, got ${hex}`)
  return 0.2126 * channel(hex, 0) + 0.7152 * channel(hex, 1) + 0.0722 * channel(hex, 2)
}

export function contrastRatio(a: string, b: string): number {
  const la = relativeLuminance(a)
  const lb = relativeLuminance(b)
  return (Math.max(la, lb) + 0.05) / (Math.min(la, lb) + 0.05)
}

export function checkContrast(palettes: readonly Palette[] = PALETTES): ContrastResult[] {
  const results: ContrastResult[] = []
  for (const p of palettes) {
    for (const mode of ['light', 'dark'] as const) {
      for (const pair of CONTRAST_PAIRS) {
        const fgColor = p[mode][pair.fg]
        const bgColor = p[mode][pair.bg]
        const ratio = contrastRatio(fgColor, bgColor)
        results.push({ ...pair, palette: p.id, mode, fgColor, bgColor, ratio, ok: ratio >= MIN_RATIO[pair.level] })
      }
    }
  }
  return results
}
