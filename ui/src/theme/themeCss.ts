// 由配色数据生成 themes.css 的纯函数，构建脚本和测试共用
import { DEFAULT_PALETTE, PALETTES, type Palette } from './palettes.ts'

// 与配色无关、只随明暗变化的值
const MODE_VALUES = {
  light: {
    scrim: 'rgba(14, 22, 19, 0.38)',
    'shadow-card': '0 1px 1px rgba(0, 0, 0, 0.05)',
    'shadow-sheet': '0 0 0 1px rgba(0, 0, 0, 0.09), 0 1px 2px rgba(0, 0, 0, 0.06), 0 10px 24px -14px rgba(0, 0, 0, 0.28)',
    'shadow-pop': '0 0 0 1px rgba(0, 0, 0, 0.1), 0 12px 32px -8px rgba(0, 0, 0, 0.32)',
    'seam-opacity': '0.6',
  },
  dark: {
    scrim: 'rgba(0, 0, 0, 0.55)',
    'shadow-card': '0 0 0 1px rgba(0, 0, 0, 0.2)',
    'shadow-sheet': '0 0 0 1px rgba(0, 0, 0, 0.5), 0 12px 28px -12px rgba(0, 0, 0, 0.7)',
    'shadow-pop': '0 0 0 1px rgba(255, 255, 255, 0.07), 0 16px 40px -8px rgba(0, 0, 0, 0.7)',
    'seam-opacity': '0.75',
  },
} as const

function block(selector: string, tokens: Record<string, string>, indent = ''): string {
  const lines = Object.entries(tokens).map(([k, v]) => `${indent}  --efv-${k}: ${v};`)
  return `${indent}${selector} {\n${lines.join('\n')}\n${indent}}\n`
}

export function buildThemesCss(palettes: readonly Palette[] = PALETTES): string {
  const def = palettes.find((p) => p.id === DEFAULT_PALETTE) ?? palettes[0]
  const out: string[] = []
  out.push('/* 由 scripts/build-themes.ts 根据 src/theme/palettes.ts 生成，请勿手改。 */\n')
  out.push(`/* 根元素属性：data-mode = light | dark | system，data-palette = ${palettes.map((p) => p.id).join(' | ')}。\n   选择器不限定 :root，任何元素都可以带自己的 data-palette / data-mode（配色预览用到）。\n   邮件纸面颜色不是主题令牌。 */\n`)
  // 属性尚未设置时的兜底：默认方案的浅色
  out.push(block(':root', { ...def.light, ...MODE_VALUES.light }))
  out.push('[data-mode="light"] { color-scheme: light; }\n[data-mode="dark"] { color-scheme: dark; }\n')
  out.push(block('[data-mode="light"], [data-mode="system"]', MODE_VALUES.light))
  out.push(block('[data-mode="dark"]', MODE_VALUES.dark))
  out.push(`@media (prefers-color-scheme: dark) {\n  [data-mode="system"] { color-scheme: dark; }\n${block('[data-mode="system"]', MODE_VALUES.dark, '  ')}}\n`)
  for (const p of palettes) {
    out.push(`/* ${p.name.en} / ${p.name.zh} */\n`)
    out.push(block(`[data-palette="${p.id}"]`, p.light))
    out.push(block(`[data-palette="${p.id}"][data-mode="dark"]`, p.dark))
    out.push(`@media (prefers-color-scheme: dark) {\n${block(`[data-palette="${p.id}"][data-mode="system"]`, p.dark, '  ')}}\n`)
  }
  return out.join('\n')
}
