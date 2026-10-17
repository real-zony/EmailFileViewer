import type { AppSettings, SettingKey } from './types.ts'

// 设置的持久化接口。正式运行时由 Tauri 的 store 插件实现（见 platform/tauri.ts）。
export interface SettingsStore {
  set(key: SettingKey, value: string): Promise<void>
  save(): Promise<void>
}

// store 文件名。相对路径落在应用数据目录下，Rust 侧在创建窗口前也读取同一个文件。
export const SETTINGS_FILE = 'settings.json'

// 内存实现：浏览器里直接调试（没有 Tauri）时使用，也用于测试
export function createMemoryStore(initial: Partial<Record<SettingKey, string>> = {}): SettingsStore & { data: Map<SettingKey, string> } {
  const data = new Map<SettingKey, string>(Object.entries(initial) as [SettingKey, string][])
  return {
    data,
    set: (key, value) => {
      data.set(key, value)
      return Promise.resolve()
    },
    save: () => Promise.resolve(),
  }
}

// 保存一项设置。写入后立即落盘，不依赖插件的防抖自动保存，避免用户马上退出时丢失。
// 保存失败不影响界面（设置已经生效），只记录错误。
export async function persistSetting<K extends SettingKey>(store: SettingsStore, key: K, value: AppSettings[K]): Promise<void> {
  try {
    await store.set(key, value)
    await store.save()
  } catch (error) {
    console.error(`Failed to save setting "${key}"`, error)
  }
}
