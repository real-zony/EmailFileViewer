import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'
import { PALETTES } from '../theme/palettes.ts'
import { parseThemeCss, resolveRootStyle } from './cssTokens.ts'

const themesCss = readFileSync(resolve(import.meta.dirname, '../theme/themes.css'), 'utf8')

// 级联计算器本身的测试：外观测试依赖它，它必须能发现 CSS 的错误
describe('resolveRootStyle', () => {
  it('固定模式与系统外观无关；跟随系统时由 prefers-color-scheme 决定', () => {
    for (const p of PALETTES) {
      const at = (mode: string, systemDark: boolean) => resolveRootStyle(themesCss, { 'data-mode': mode, 'data-palette': p.id }, systemDark)['--efv-desk']
      expect(at('light', true), p.id).toBe(p.light.desk)
      expect(at('light', false), p.id).toBe(p.light.desk)
      expect(at('dark', true), p.id).toBe(p.dark.desk)
      expect(at('dark', false), p.id).toBe(p.dark.desk)
      expect(at('system', false), p.id).toBe(p.light.desk)
      expect(at('system', true), p.id).toBe(p.dark.desk)
    }
  })

  it('没有任何属性时使用 :root 的兜底值（默认方案的浅色）', () => {
    expect(resolveRootStyle(themesCss, {}, true)['--efv-desk']).toBe('#e6eae8')
  })

  it('去掉 themes.css 里“跟随系统”的 @media 分支后，系统切到深色界面不再变化——测试能发现这种错误', () => {
    const broken = themesCss.replace(/@media \(prefers-color-scheme: dark\) \{[\s\S]*?\n\}\n/g, '')
    expect(broken).not.toBe(themesCss)
    const style = (dark: boolean) => resolveRootStyle(broken, { 'data-mode': 'system', 'data-palette': 'plum' }, dark)['--efv-desk']
    expect(style(true)).toBe(style(false))
  })

  it('同优先级时后写的规则生效；更具体的选择器优先', () => {
    const css = ':root { --a: 1; } [data-mode="x"] { --a: 2; } [data-mode="x"][data-palette="y"] { --a: 3; } @media (prefers-color-scheme: dark) { [data-mode="x"] { --a: 4; } }'
    expect(resolveRootStyle(css, {}, false)['--a']).toBe('1')
    expect(resolveRootStyle(css, { 'data-mode': 'x' }, false)['--a']).toBe('2')
    expect(resolveRootStyle(css, { 'data-mode': 'x' }, true)['--a']).toBe('4')
    expect(resolveRootStyle(css, { 'data-mode': 'x', 'data-palette': 'y' }, false)['--a']).toBe('3')
    // 媒体查询里的规则写在后面，但选择器不如前面的具体，所以不生效
    expect(resolveRootStyle(css, { 'data-mode': 'x', 'data-palette': 'y' }, true)['--a']).toBe('3')
    expect(parseThemeCss(css)).toHaveLength(4)
  })
})
