import { isTauri } from '@tauri-apps/api/core'
import { getCurrentWebviewWindow } from '@tauri-apps/api/webviewWindow'
import { LazyStore } from '@tauri-apps/plugin-store'
import { createMemoryStore, SETTINGS_FILE, type SettingsStore } from '../settings/store.ts'
import type { WindowControl } from '../settings/windowControl.ts'

// 运行在 Tauri 中时使用 store 插件（文件在应用数据目录下）；
// 在浏览器里直接调试时退回内存实现，设置只在当前页面有效。
export function createSettingsStore(): SettingsStore {
  if (!isTauri()) return createMemoryStore()
  // 关闭防抖自动保存，由 persistSetting 在每次写入后显式保存
  const store = new LazyStore(SETTINGS_FILE, { autoSave: false })
  return {
    set: (key, value) => store.set(key, value),
    save: () => store.save(),
  }
}

// 不在 Tauri 中时返回 null，调用方跳过原生窗口的同步
export function createWindowControl(): WindowControl | null {
  if (!isTauri()) return null
  const win = getCurrentWebviewWindow()
  return {
    setBackgroundColor: (color) => win.setBackgroundColor(color),
    setTheme: (theme) => win.setTheme(theme),
    setTitle: (title) => win.setTitle(title),
  }
}
