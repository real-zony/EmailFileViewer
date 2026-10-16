import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

// Rust 注入的 initialization_script 模板（src-tauri/src/initial_settings.js）。
// 占位符替换方式与 settings.rs 中的 initialization_script() 一致。
const template = readFileSync(resolve(import.meta.dirname, '../../../../src-tauri/src/initial_settings.js'), 'utf8')
const settings = { language: 'zh-CN', mode: 'dark', palette: 'plum' }
const script = template.replace('__EFV_SETTINGS_JSON__', JSON.stringify(settings))

interface FakeWindow {
  top: unknown
  __EFV_INITIAL_SETTINGS__?: unknown
}

function run(win: FakeWindow, doc: Document) {
  new Function('window', 'document', 'MutationObserver', script)(win, doc, MutationObserver)
}

function topWindow(): FakeWindow {
  const win: FakeWindow = { top: null }
  win.top = win
  return win
}

describe('首屏前的初始化脚本', () => {
  it('占位符只出现一次，替换后是合法的脚本', () => {
    expect(template.split('__EFV_SETTINGS_JSON__')).toHaveLength(2)
    expect(script).not.toContain('__EFV_SETTINGS_JSON__')
    expect(() => new Function(script)).not.toThrow()
  })

  it('注入 window.__EFV_INITIAL_SETTINGS__，并直接在 <html> 上设置 data-mode、data-palette 和 lang', () => {
    const doc = document.implementation.createHTMLDocument('')
    doc.documentElement.setAttribute('data-mode', 'system') // index.html 里的静态默认值会被覆盖
    doc.documentElement.setAttribute('data-palette', 'verdigris')
    doc.documentElement.setAttribute('lang', 'en')
    const win = topWindow()
    run(win, doc)
    expect(win.__EFV_INITIAL_SETTINGS__).toEqual(settings)
    expect(doc.documentElement.getAttribute('data-mode')).toBe('dark')
    expect(doc.documentElement.getAttribute('data-palette')).toBe('plum')
    expect(doc.documentElement.getAttribute('lang')).toBe('zh-CN')
  })

  it('脚本运行时 <html> 还不存在：元素一出现就设置（先于首次绘制）', async () => {
    const doc = document.implementation.createDocument(null, null)
    expect(doc.documentElement).toBeNull()
    const win = topWindow()
    run(win, doc)
    expect(win.__EFV_INITIAL_SETTINGS__).toEqual(settings)

    const html = doc.createElement('html')
    html.setAttribute('data-mode', 'system')
    doc.appendChild(html)
    await new Promise((resolve) => setTimeout(resolve, 0))
    expect(html.getAttribute('data-mode')).toBe('dark')
    expect(html.getAttribute('data-palette')).toBe('plum')
    expect(html.getAttribute('lang')).toBe('zh-CN')
  })

  it('子框架里什么都不做：不注入对象，也不改动框架内文档（邮件内容所在的 iframe）', () => {
    const doc = document.implementation.createHTMLDocument('')
    const win: FakeWindow = { top: {} }
    run(win, doc)
    expect(win.__EFV_INITIAL_SETTINGS__).toBeUndefined()
    expect(doc.documentElement.hasAttribute('data-mode')).toBe(false)
    expect(doc.documentElement.hasAttribute('lang')).toBe(false)
  })
})
