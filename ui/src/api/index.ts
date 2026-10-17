import { isTauri } from '@tauri-apps/api/core'
import { createMockApi } from './mockApi.ts'
import { createTauriApi } from './tauriApi.ts'
import type { ViewerApi } from './types.ts'

export * from './types.ts'

// 在 Tauri 中用真实实现；浏览器里 `npm run dev` 用示例数据驱动的模拟实现（只在开发模式下才会被打进来）。
export async function createViewerApi(): Promise<ViewerApi> {
  if (isTauri()) return createTauriApi()
  if (import.meta.env.DEV) return (await import('../dev/devApi.ts')).createDevApi()
  // 生产构建在 Tauri 之外打开：没有可打开的东西，“打开…”相当于取消
  return createMockApi()
}
