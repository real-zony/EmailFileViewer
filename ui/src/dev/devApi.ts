// 浏览器里 `npm run dev`（没有 Tauri）使用的模拟实现：用固定的示例数据驱动整个界面。
// - 点“打开…”或按 Ctrl/⌘+O：依次打开下一个示例（循环）。
// - `?open=a,b,c`：启动时一次打开这几个示例，相当于用多个文件参数启动（名字见 fixtures 目录，打不开的用 error-*）。
// - `window.__efvDev`：在控制台或自动化脚本里驱动界面（见 DevHooks）。
import { createMockApi, type MockApi, type MockFile } from '../api/mockApi.ts'
import type { DragEvent, EmailDocument } from '../api/types.ts'
import { FIXTURES, fixture } from './fixtures.ts'

export interface DevHooks {
  /** 全部示例的名字 */
  fixtures: string[]
  api: MockApi
  /** 像用多个文件参数启动或拖放那样，一次打开这几个示例 */
  open(...names: string[]): Promise<number>
  /** 让下一次“打开…”对话框选中这几个示例 */
  queueDialog(...names: string[]): void
  /** 模拟窗口上的拖放事件 */
  drag(event: DragEvent): void
  /** 打开一封任意内容的邮件（用来做安全探测，比如正文里带脚本的 HTML） */
  deliver(fileName: string, document: EmailDocument): Promise<number>
}

declare global {
  interface Window {
    __efvDev?: DevHooks
  }
}

export function createDevApi(): MockApi {
  const api = createMockApi()
  const documents = Object.keys(FIXTURES).filter((name) => FIXTURES[name].document)
  let cursor = 0
  api.setDialogProvider(() => {
    const file = FIXTURES[documents[cursor % documents.length]]
    cursor++
    return [file]
  })

  const pick = (names: string[]): MockFile[] => names.map(fixture)
  window.__efvDev = {
    fixtures: Object.keys(FIXTURES),
    api,
    open: (...names) => api.deliverBatch(pick(names)),
    queueDialog: (...names) => api.queueDialogChoice(pick(names)),
    drag: (event) => api.emitDrag(event),
    deliver: (fileName, document) => api.deliverBatch([{ fileName, document }]),
  }

  const initial = new URLSearchParams(window.location.search).get('open')
  if (initial) void api.deliverBatch(pick(initial.split(',').map((name) => name.trim()).filter(Boolean)))
  return api
}
