// 任务 7.10：macOS 原生菜单发来的两个事件（关闭标签页、设置），以及窗口标题跟随当前邮件
import { act, cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react'
import { emit } from '@tauri-apps/api/event'
import { clearMocks, mockIPC, mockWindows } from '@tauri-apps/api/mocks'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { createTauriApi } from '../api/tauriApi.ts'
import { fixture } from '../dev/fixtures.ts'
import { createFakeWindowControl } from '../test/fakes.ts'
import { bodyFrame, callsOf, deliver, startApp, tabs, withDocument } from '../test/viewerHarness.tsx'

afterEach(() => {
  // 先卸载界面（会取消事件监听，需要 Tauri 的内部对象还在），再清掉 IPC 模拟
  cleanup()
  clearMocks()
})

type Api = Parameters<typeof callsOf>[0]
// close_document 是异步命令：等微任务跑完，句柄才会从模拟的 Rust 一侧消失
const closeFromMenu = (api: Api) => act(async () => api.emitCloseActiveTab())
const settingsFromMenu = (api: Api) => act(async () => api.emitOpenSettings())
const attachmentCard = (fileName: string) => screen.getByTitle(fileName) as HTMLElement
const subjectOf = (name: string) => fixture(name).document!.subject!
const heading = (name: string) => screen.getByRole('heading', { level: 1, name })
const emptyState = () => screen.getByRole('heading', { name: 'Open an email file' })
// 只有一个标签页时不显示标签栏，所以用文档句柄的数量表示还开着几封邮件
const closedHandles = (api: Api) =>
  callsOf(api, 'close_document').map((c) => (c.args as { handle: number }).handle).sort()

describe('7.10 菜单“关闭标签页”（efv://close-active-tab）', () => {
  it('关闭当前标签页并调用 close_document，活动标签页换成相邻的一个', async () => {
    const { api } = await startApp()
    await deliver(api, 'headers-normal', 'plain-text')
    const [first, second] = [...api.openHandles]
    expect(tabs()).toHaveLength(2)
    expect(tabs()[0]).toHaveAttribute('aria-selected', 'true')

    await closeFromMenu(api)
    expect(closedHandles(api)).toEqual([first])
    expect(tabs()).toHaveLength(0) // 只剩一个标签页，标签栏收起
    expect(heading(subjectOf('plain-text'))).toBeInTheDocument()
    expect([...api.openHandles]).toEqual([second])
  })

  it('关闭的是“当前”标签页，不一定是第一个', async () => {
    const { api, user } = await startApp()
    await deliver(api, 'headers-normal', 'plain-text')
    const [first, second] = [...api.openHandles]
    await user.click(screen.getByRole('tab', { name: /body-plain-only/ }))
    await closeFromMenu(api)
    expect(closedHandles(api)).toEqual([second])
    expect([...api.openHandles]).toEqual([first])
    expect(heading(subjectOf('headers-normal'))).toBeInTheDocument()
  })

  it('进入了嵌套邮件时，整个标签页关闭，面包屑链上的每一层都调用 close_document', async () => {
    const { api, user } = await startApp()
    await deliver(api, 'nested-two-levels')
    await user.click(screen.getByRole('button', { name: 'Open' }))
    await screen.findByRole('heading', { level: 1, name: /Level 2/ })
    const handles = [...api.openHandles]
    expect(handles).toHaveLength(2)
    await closeFromMenu(api)
    expect(closedHandles(api)).toEqual(handles.sort())
    expect(emptyState()).toBeInTheDocument()
  })

  it('打开失败的文件也占一个标签页，同样可以关闭；调用 close_document 的只有真正打开过的邮件', async () => {
    const { api } = await startApp()
    await deliver(api, 'error-corrupted')
    expect(screen.getByRole('heading', { name: 'This file is damaged' })).toBeInTheDocument()
    await closeFromMenu(api)
    expect(emptyState()).toBeInTheDocument()
    expect(callsOf(api, 'close_document')).toHaveLength(0)
  })

  it('没有标签页（空状态）时什么也不做', async () => {
    const { api } = await startApp()
    await closeFromMenu(api)
    expect(emptyState()).toBeInTheDocument()
    expect(callsOf(api, 'close_document')).toHaveLength(0)
  })

  it('焦点在正文 iframe 里时同样有效（事件来自 Rust，与网页里的焦点无关）', async () => {
    const { api } = await startApp()
    await deliver(api, 'headers-normal')
    const frame = bodyFrame()
    frame.focus()
    // iframe 内的键盘事件到不了应用页面：⌘W 在网页里不会触发关闭
    fireEvent.keyDown(frame, { key: 'w', metaKey: true })
    expect(api.openHandles.size).toBe(1)
    await closeFromMenu(api)
    expect(emptyState()).toBeInTheDocument()
    expect(api.openHandles.size).toBe(0)
  })

  it('设置对话框打开时不响应，和键盘快捷键一致', async () => {
    const { api, user } = await startApp()
    await deliver(api, 'headers-normal')
    await user.click(screen.getByRole('button', { name: 'Settings' }))
    await closeFromMenu(api)
    expect(api.openHandles.size).toBe(1)
    expect(callsOf(api, 'close_document')).toHaveLength(0)
    // 关掉对话框之后恢复响应
    await user.click(screen.getByRole('button', { name: 'Done' }))
    await closeFromMenu(api)
    expect(api.openHandles.size).toBe(0)
    expect(emptyState()).toBeInTheDocument()
  })

  it('“无法打开可执行附件”的提示框出现时不响应', async () => {
    const { api } = await startApp()
    await deliver(api, 'attachments-executables')
    fireEvent.doubleClick(attachmentCard('installer.exe'))
    await screen.findByRole('alertdialog')
    await closeFromMenu(api)
    expect(api.openHandles.size).toBe(1)
    expect(callsOf(api, 'close_document')).toHaveLength(0)
  })
})

describe('7.10 菜单“设置…”（efv://open-settings）', () => {
  it('打开设置对话框；对话框已经打开时再选一次，仍然只有一个', async () => {
    const { api, user } = await startApp()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    await settingsFromMenu(api)
    const dialog = await screen.findByRole('dialog', { name: 'Settings' })
    await settingsFromMenu(api)
    expect(screen.getAllByRole('dialog')).toHaveLength(1)
    await user.click(within(dialog).getByRole('button', { name: 'Done' }))
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('有邮件打开时也可以', async () => {
    const { api } = await startApp()
    await deliver(api, 'headers-normal')
    await settingsFromMenu(api)
    expect(await screen.findByRole('dialog', { name: 'Settings' })).toBeInTheDocument()
  })

  it('“无法打开可执行附件”的提示框出现时不叠加设置对话框', async () => {
    const { api } = await startApp()
    await deliver(api, 'attachments-executables')
    fireEvent.doubleClick(attachmentCard('installer.exe'))
    await screen.findByRole('alertdialog')
    await settingsFromMenu(api)
    expect(screen.queryByRole('dialog', { name: 'Settings' })).not.toBeInTheDocument()
  })

  it('中文界面', async () => {
    const { api } = await startApp({ language: 'zh-CN' })
    await settingsFromMenu(api)
    expect(await screen.findByRole('dialog', { name: '设置' })).toBeInTheDocument()
  })
})

describe('7.10 菜单事件的监听', () => {
  it('卸载后取消监听，没有泄漏；StrictMode 重复挂载也只留一份', async () => {
    for (const strict of [false, true]) {
      const { api, view } = await startApp({ strict })
      await deliver(api, 'headers-normal', 'plain-text')
      await closeFromMenu(api)
      expect(api.openHandles.size, `strict=${strict}`).toBe(1) // 只关了一个，没有重复处理
      view.unmount()
      expect(api.listenerCount()).toBe(0)
    }
  })

  it('经真实实现和 Tauri 事件系统：事件名 efv://close-active-tab 和 efv://open-settings', async () => {
    mockWindows('main')
    mockIPC(() => undefined, { shouldMockEvents: true })
    await startApp({ api: createTauriApi() })
    const file = fixture('headers-normal')
    await act(async () => {
      await emit('efv://document-opened', { batchId: 1, handle: 7, fileName: file.fileName, document: file.document })
    })
    expect(await screen.findByRole('heading', { level: 1, name: subjectOf('headers-normal') })).toBeInTheDocument()

    await act(async () => {
      await emit('efv://open-settings')
    })
    expect(await screen.findByRole('dialog', { name: 'Settings' })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Done' }))

    await act(async () => {
      await emit('efv://close-active-tab')
    })
    expect(await screen.findByRole('heading', { name: 'Open an email file' })).toBeInTheDocument()
  })
})

describe('7.10 窗口标题', () => {
  const start = async (options: Parameters<typeof startApp>[0] = {}) => {
    const win = createFakeWindowControl()
    const app = await startApp({ ...options, windowControl: win.control })
    const title = () => win.titles[win.titles.length - 1]
    return { ...app, win, title }
  }

  it('空状态是“EmailFileViewer”', async () => {
    const { title } = await start()
    await waitFor(() => expect(title()).toBe('EmailFileViewer'))
  })

  it('打开邮件后是“主题 — EmailFileViewer”', async () => {
    const { api, title } = await start()
    await deliver(api, 'headers-normal')
    await waitFor(() => expect(title()).toBe('Quarterly planning meeting — EmailFileViewer'))
  })

  it('没有主题时用文件名', async () => {
    const { api, title } = await start()
    await deliver(api, withDocument('headers-normal', { subject: null }, 'no-subject.eml'))
    await waitFor(() => expect(title()).toBe('no-subject.eml — EmailFileViewer'))
  })

  it('切换标签页时标题跟着换', async () => {
    const { api, user, title } = await start()
    await deliver(api, 'headers-normal', 'plain-text')
    await waitFor(() => expect(title()).toBe('Quarterly planning meeting — EmailFileViewer'))
    await user.click(screen.getByRole('tab', { name: /body-plain-only/ }))
    await waitFor(() => expect(title()).toBe('Plain text only: paragraphs and blank lines — EmailFileViewer'))
    await user.click(screen.getByRole('tab', { name: /headers-normal/ }))
    await waitFor(() => expect(title()).toBe('Quarterly planning meeting — EmailFileViewer'))
  })

  it('关闭标签页后换成剩下的那封；关闭最后一个回到空状态', async () => {
    const { api, title } = await start()
    await deliver(api, 'headers-normal', 'plain-text')
    await closeFromMenu(api)
    await waitFor(() => expect(title()).toBe('Plain text only: paragraphs and blank lines — EmailFileViewer'))
    await closeFromMenu(api)
    await waitFor(() => expect(title()).toBe('EmailFileViewer'))
  })

  it('用 ⌘W / Ctrl+W 或标签页上的关闭按钮关闭最后一个标签页，同样回到空状态', async () => {
    const { api, user, title } = await start()
    await deliver(api, 'headers-normal')
    await waitFor(() => expect(title()).toContain('Quarterly'))
    await user.keyboard('{Control>}w{/Control}')
    await waitFor(() => expect(title()).toBe('EmailFileViewer'))
  })

  it('进入嵌套邮件：标题换成这一层的主题；点面包屑或“返回”回到上一层，标题也换回去', async () => {
    const { api, user, title } = await start()
    await deliver(api, 'nested-two-levels')
    await waitFor(() => expect(title()).toBe('Level 1 (outer) message with two nested levels — EmailFileViewer'))
    await user.click(screen.getByRole('button', { name: 'Open' }))
    await waitFor(() => expect(title()).toBe('Level 2 (middle) message — EmailFileViewer'))
    await user.click(screen.getByRole('button', { name: 'Open' }))
    await waitFor(() => expect(title()).toBe('Level 3 (innermost) message — EmailFileViewer'))
    await user.click(screen.getByRole('button', { name: 'Back' }))
    await waitFor(() => expect(title()).toBe('Level 2 (middle) message — EmailFileViewer'))
    await user.click(within(screen.getByRole('navigation', { name: 'Message levels' })).getByRole('button', { name: 'nested-rfc822-two-levels' }))
    await waitFor(() => expect(title()).toBe('Level 1 (outer) message with two nested levels — EmailFileViewer'))
  })

  it('嵌套邮件没有主题：用附件的文件名', async () => {
    const { api, user, title } = await start()
    const inner = withDocument('attachments-multi', { subject: null }, 'inner.eml')
    await deliver(api, { ...fixture('nested-two-levels'), nested: { '2': inner } })
    await user.click(screen.getByRole('button', { name: 'Open' }))
    await waitFor(() => expect(title()).toBe('Level 2 (middle) message.eml — EmailFileViewer'))
  })

  it('打开失败的文件占一个标签页，标题用它的文件名', async () => {
    const { api, title } = await start()
    await deliver(api, 'error-corrupted')
    await waitFor(() => expect(title()).toBe('truncated.msg — EmailFileViewer'))
  })

  it('标题与界面语言无关；标题没变时不重复调用 setTitle', async () => {
    const { api, user, win, title } = await start({ language: 'zh-CN' })
    await deliver(api, 'headers-normal')
    await waitFor(() => expect(title()).toBe('Quarterly planning meeting — EmailFileViewer'))
    const count = win.titles.length
    await user.click(screen.getByRole('button', { name: '设置' }))
    await user.click(screen.getByRole('button', { name: '完成' }))
    expect(win.titles).toHaveLength(count)
  })

  it('setTitle 失败时只记录错误，界面照常工作', async () => {
    const error = vi.spyOn(console, 'error').mockImplementation(() => {})
    const win = createFakeWindowControl()
    win.control.setTitle = () => Promise.reject(new Error('denied'))
    const { api } = await startApp({ windowControl: win.control })
    await deliver(api, 'headers-normal')
    expect(await screen.findByRole('heading', { level: 1, name: 'Quarterly planning meeting' })).toBeInTheDocument()
    await waitFor(() => expect(error).toHaveBeenCalledWith('Failed to update the window title', expect.any(Error)))
    error.mockRestore()
  })

  it('不在 Tauri 中（没有窗口控制）时不报错', async () => {
    const { api } = await startApp()
    await deliver(api, 'headers-normal')
    expect(await screen.findByRole('heading', { level: 1, name: 'Quarterly planning meeting' })).toBeInTheDocument()
  })
})
