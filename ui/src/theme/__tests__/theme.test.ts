// @vitest-environment node
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'
import { checkContrast, contrastRatio, CONTRAST_PAIRS, MIN_RATIO } from '../contrast.ts'
import { COLOR_TOKENS, DEFAULT_MODE, DEFAULT_PALETTE, PALETTES, PALETTE_SOURCES, isPaletteId, resolvePalette, type PaletteSource } from '../palettes.ts'
import { buildThemesCss } from '../themeCss.ts'

describe('palettes', () => {
  it('4–6 套方案，id 唯一，默认值正确', () => {
    expect(PALETTES.length).toBeGreaterThanOrEqual(4)
    expect(PALETTES.length).toBeLessThanOrEqual(6)
    expect(new Set(PALETTES.map((p) => p.id)).size).toBe(PALETTES.length)
    expect(DEFAULT_PALETTE).toBe('verdigris')
    expect(DEFAULT_MODE).toBe('system')
    expect(isPaletteId('plum')).toBe(true)
    expect(isPaletteId('neon')).toBe(false)
  })

  it('每套方案的两种模式都包含全部颜色令牌，且都是 #rrggbb', () => {
    for (const p of PALETTES) {
      for (const mode of ['light', 'dark'] as const) {
        expect(Object.keys(p[mode])).toEqual([...COLOR_TOKENS])
        for (const v of Object.values(p[mode])) expect(v).toMatch(/^#[0-9a-f]{6}$/i)
      }
    }
  })

  it('邮件纸面颜色不是主题令牌', () => {
    expect(COLOR_TOKENS as readonly string[]).not.toContain('paper')
    expect(COLOR_TOKENS as readonly string[]).not.toContain('sheet')
  })

  it('缺少令牌时报错', () => {
    const broken = structuredClone(PALETTE_SOURCES[0]) as unknown as PaletteSource
    delete (broken.light as Partial<Record<string, string>>).ink
    expect(() => resolvePalette(broken)).toThrow(/missing token ink/)
  })
})

describe('contrast（8.5）', () => {
  it('已知值：黑白为 21:1，同色为 1:1', () => {
    expect(contrastRatio('#000000', '#ffffff')).toBeCloseTo(21, 5)
    expect(contrastRatio('#777777', '#777777')).toBe(1)
  })

  it('全部方案、两种模式都达标', () => {
    const results = checkContrast()
    expect(results).toHaveLength(PALETTES.length * 2 * CONTRAST_PAIRS.length)
    const failures = results.filter((r) => !r.ok).map((r) => `${r.palette}/${r.mode} ${r.fg} on ${r.bg} ${r.ratio.toFixed(2)}`)
    expect(failures).toEqual([])
  })

  it('故意把正文文字改成低对比度时检查失败', () => {
    const tampered = PALETTES.map((p) => (p.id === 'indigo' ? { ...p, dark: { ...p.dark, 'ink-2': '#3a3f55' } } : p))
    const failures = checkContrast(tampered).filter((r) => !r.ok)
    expect(failures.length).toBeGreaterThan(0)
    expect(failures.every((r) => r.palette === 'indigo' && r.mode === 'dark' && r.fg === 'ink-2')).toBe(true)
    expect(failures[0].ratio).toBeLessThan(MIN_RATIO.text)
  })

  it('图标颜色按 3:1 检查：低于 3:1 时失败', () => {
    const tampered = PALETTES.map((p) => (p.id === 'verdigris' ? { ...p, light: { ...p.light, 'ink-3': '#b4bdb9' } } : p))
    const failures = checkContrast(tampered).filter((r) => !r.ok)
    expect(failures.some((r) => r.fg === 'ink-3' && r.level === 'ui')).toBe(true)
  })
})

describe('themes.css', () => {
  it('提交的 themes.css 与数据源生成的结果一致（不一致时运行 npm run themes）', () => {
    const file = fileURLToPath(new URL('../themes.css', import.meta.url))
    expect(readFileSync(file, 'utf8')).toBe(buildThemesCss())
  })

  it('每套方案都有浅色、深色和跟随系统三段', () => {
    const css = buildThemesCss()
    for (const p of PALETTES) {
      expect(css).toContain(`[data-palette="${p.id}"] {`)
      expect(css).toContain(`[data-palette="${p.id}"][data-mode="dark"] {`)
      expect(css).toContain(`[data-palette="${p.id}"][data-mode="system"] {`)
    }
    expect(css).toContain('@media (prefers-color-scheme: dark)')
    expect(css).not.toContain('--efv-paper')
  })
})
