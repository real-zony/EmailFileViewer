import { describe, expect, it } from 'vitest'
import { readInitialSettings, resolveInitialSettings } from '../initial.ts'

describe('启动时的初始设置', () => {
  it('Rust 注入了设置：原样使用，不再看系统语言', () => {
    const injected = { language: 'zh-CN', mode: 'dark', palette: 'plum' }
    expect(resolveInitialSettings({ injected, systemLocale: 'de-DE' })).toEqual(injected)
  })

  it('浏览器里直接调试（没有注入对象）：语言按 navigator 判断，模式 system，配色 verdigris', () => {
    expect(resolveInitialSettings({ injected: undefined, systemLocale: 'zh-CN' })).toEqual({ language: 'zh-CN', mode: 'system', palette: 'verdigris' })
    expect(resolveInitialSettings({ injected: undefined, systemLocale: 'de-DE' })).toEqual({ language: 'en', mode: 'system', palette: 'verdigris' })
    expect(resolveInitialSettings({ injected: undefined, systemLocale: undefined })).toEqual({ language: 'en', mode: 'system', palette: 'verdigris' })
  })

  it('每个字段单独校验：无效或缺失的字段回退到默认值，其余保留', () => {
    expect(resolveInitialSettings({ injected: { language: 'fr', mode: 'dark', palette: 'neon' }, systemLocale: 'zh-CN' })).toEqual({
      language: 'zh-CN',
      mode: 'dark',
      palette: 'verdigris',
    })
    expect(resolveInitialSettings({ injected: { language: 'en', mode: 'sepia' } })).toEqual({ language: 'en', mode: 'system', palette: 'verdigris' })
  })

  it('注入的不是对象（null、字符串、数组）时按没有注入处理', () => {
    for (const injected of [null, 'dark', 42, ['dark']]) {
      expect(resolveInitialSettings({ injected, systemLocale: 'en-US' })).toEqual({ language: 'en', mode: 'system', palette: 'verdigris' })
    }
  })

  it('readInitialSettings 读取 window 上的注入对象和 navigator.languages 的第一项', () => {
    const win = { __EFV_INITIAL_SETTINGS__: { mode: 'light' } }
    const settings = readInitialSettings(win, { language: 'en-US', languages: ['zh-CN', 'en-US'] })
    expect(settings).toEqual({ language: 'zh-CN', mode: 'light', palette: 'verdigris' })
    expect(readInitialSettings({}, { language: 'de-DE', languages: [] })).toMatchObject({ language: 'en' })
  })
})
