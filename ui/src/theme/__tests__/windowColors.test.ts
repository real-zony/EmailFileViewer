// @vitest-environment node
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'
import { DEFAULT_PALETTE, PALETTES } from '../palettes.ts'
import { buildWindowColorsJson } from '../windowColors.ts'

// Rust 在创建主窗口前读取这个文件来设置窗口底色（src-tauri/src/settings.rs）
const file = resolve(import.meta.dirname, '../../../../src-tauri/src/window_colors.json')

describe('window_colors.json（Rust 读取的窗口底色）', () => {
  it('与配色数据一致（不一致时运行 npm run themes）', () => {
    expect(readFileSync(file, 'utf8')).toBe(buildWindowColorsJson())
  })

  it('每套方案、两种模式的底色就是该方案的 desk，也就是 <body> 的背景', () => {
    const data = JSON.parse(readFileSync(file, 'utf8')) as { default: string; palettes: Record<string, { light: string; dark: string }> }
    expect(data.default).toBe(DEFAULT_PALETTE)
    expect(Object.keys(data.palettes)).toEqual(PALETTES.map((p) => p.id))
    for (const p of PALETTES) expect(data.palettes[p.id]).toEqual({ light: p.light.desk, dark: p.dark.desk })
    const appCss = readFileSync(resolve(import.meta.dirname, '../../styles/app.css'), 'utf8')
    expect(appCss).toMatch(/body\s*\{\s*background:\s*var\(--efv-desk\)/)
  })

  it('配色数据变了而文件没更新时，检查能发现', () => {
    const tampered = PALETTES.map((p) => (p.id === 'plum' ? { ...p, dark: { ...p.dark, desk: '#000001' } } : p))
    expect(buildWindowColorsJson(tampered)).not.toBe(readFileSync(file, 'utf8'))
  })
})
