// 界面测试的公共部分：用模拟实现启动整个 App，打开 fixture，取常用的元素
import { act, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { expect } from 'vitest'
import { StrictMode } from 'react'
import App from '../App.tsx'
import { createMockApi, type MockApi, type MockFile } from '../api/mockApi.ts'
import type { EmailAddress, ViewerApi } from '../api/types.ts'
import { fixture } from '../dev/fixtures.ts'
import { createMemoryStore } from '../settings/store.ts'
import type { Language } from '../settings/language.ts'
import type { AppSettings } from '../settings/types.ts'
import type { Mode } from '../theme/palettes.ts'
import type { WindowControl } from '../settings/windowControl.ts'
import type { Platform } from '../viewer/platform.ts'
import { installSystemAppearance } from './fakes.ts'

export interface StartOptions {
  language?: Language
  mode?: Mode
  platform?: Platform
  strict?: boolean
  /** 在渲染之前对模拟实现做准备，例如先排好启动时收到的文件 */
  prepare?: (api: MockApi) => void | Promise<void>
  /** 不用模拟实现，换成别的 ViewerApi（例如经过 mockIPC 的真实实现） */
  api?: ViewerApi
  /** 原生窗口控制（窗口标题、底色）；不传时和浏览器里一样没有 */
  windowControl?: WindowControl
}

export async function startApp(options: StartOptions = {}) {
  const system = installSystemAppearance(false)
  const user = userEvent.setup()
  const mock = createMockApi()
  await options.prepare?.(mock)
  const settings: AppSettings = { language: options.language ?? 'en', mode: options.mode ?? 'light', palette: 'verdigris' }
  const app = (
    <App
      initialSettings={settings}
      store={createMemoryStore()}
      api={options.api ?? mock}
      platform={options.platform ?? 'other'}
      windowControl={options.windowControl ?? null}
    />
  )
  const view = render(options.strict ? <StrictMode>{app}</StrictMode> : app)
  // app_ready 在全部事件监听注册完成之后才会被调用
  if (!options.api) await waitFor(() => expect(mock.ready).toBe(true))
  return { api: mock, user, view, system }
}

/** 模拟 Rust 一次收到这些文件（对话框、拖放、启动参数、单实例转发都一样） */
export async function deliver(api: MockApi, ...files: (MockFile | string)[]) {
  await act(async () => {
    await api.deliverBatch(files.map((f) => (typeof f === 'string' ? fixture(f) : f)))
  })
}

/** 用户在文件对话框里选了这些文件 */
export async function chooseInDialog(api: MockApi, ...files: (MockFile | string)[]) {
  api.queueDialogChoice(files.map((f) => (typeof f === 'string' ? fixture(f) : f)))
  await act(async () => {
    await api.openWithDialog()
  })
}

export const bodyFrame = () => screen.getByTitle('Message content') as HTMLIFrameElement
export const bodyDoc = () => bodyFrame().getAttribute('srcdoc') ?? ''
export const tabs = () => screen.queryAllByRole('tab')
export const toastRegion = () => screen.queryByRole('region', { name: /^(Notifications|通知)$/ })
export const headerOf = () => within(document.querySelector<HTMLElement>('.msghead')!)

export function address(name: string | null, address: string | null): EmailAddress {
  return { name, address }
}

/** 复制一个 fixture 并改掉 document 的部分字段 */
export function withDocument(name: string, patch: Partial<NonNullable<MockFile['document']>>, fileName?: string): MockFile {
  const base = fixture(name)
  return { ...base, fileName: fileName ?? base.fileName, document: { ...base.document!, ...patch } }
}

export function callsOf(api: MockApi, command: string) {
  return api.calls.filter((c) => c.command === command)
}
