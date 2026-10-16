import { describe, expect, it, vi } from 'vitest'
import { applyDocumentSettings } from '../appearance.ts'
import { createMemoryStore, persistSetting, SETTINGS_FILE, type SettingsStore } from '../store.ts'
import { SETTING_KEYS } from '../types.ts'

describe('persistSetting', () => {
  it('先写入再保存，不依赖插件的防抖自动保存', async () => {
    const calls: string[] = []
    const store: SettingsStore = {
      set: (key, value) => (calls.push(`set ${key}=${value}`), Promise.resolve()),
      save: () => (calls.push('save'), Promise.resolve()),
    }
    await persistSetting(store, 'mode', 'dark')
    expect(calls).toEqual(['set mode=dark', 'save'])
  })

  it('写入失败时不抛出（设置已在界面生效），不再保存，只记录错误', async () => {
    const error = vi.spyOn(console, 'error').mockImplementation(() => {})
    const save = vi.fn(() => Promise.resolve())
    await expect(persistSetting({ set: () => Promise.reject(new Error('denied')), save }, 'palette', 'plum')).resolves.toBeUndefined()
    expect(save).not.toHaveBeenCalled()
    expect(error).toHaveBeenCalledTimes(1)
    error.mockRestore()
  })

  it('内存实现：可以用初始内容创建，写入后可读回', async () => {
    const store = createMemoryStore({ language: 'en' })
    await persistSetting(store, 'palette', 'indigo')
    expect(Object.fromEntries(store.data)).toEqual({ language: 'en', palette: 'indigo' })
  })
})

describe('约定', () => {
  it('store 文件名和键名与 Rust 侧一致（src-tauri/src/settings.rs）', () => {
    expect(SETTINGS_FILE).toBe('settings.json')
    expect([...SETTING_KEYS]).toEqual(['language', 'mode', 'palette'])
  })
})

describe('applyDocumentSettings', () => {
  it('在根元素上设置 data-mode、data-palette 和 lang', () => {
    const root = document.createElement('html')
    applyDocumentSettings(root, { language: 'zh-CN', mode: 'dark', palette: 'graphite' })
    expect(root.getAttribute('data-mode')).toBe('dark')
    expect(root.getAttribute('data-palette')).toBe('graphite')
    expect(root.getAttribute('lang')).toBe('zh-CN')
    applyDocumentSettings(root, { language: 'en', mode: 'system', palette: 'verdigris' })
    expect(root.getAttribute('data-mode')).toBe('system')
    expect(root.getAttribute('lang')).toBe('en')
  })
})
