// 任务 7.3（空状态、打开、拖放）、7.8（错误）、7.9（标签页）：email-opening 规格里面向界面的场景
import { act, cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react'
import { clearMocks, mockIPC, mockWindows } from '@tauri-apps/api/mocks'
import { emit } from '@tauri-apps/api/event'
import { afterEach, describe, expect, it } from 'vitest'
import { createTauriApi } from '../api/tauriApi.ts'
import { fixture } from '../dev/fixtures.ts'
import { callsOf, chooseInDialog, deliver, startApp, tabs, toastRegion } from '../test/viewerHarness.tsx'

afterEach(() => {
  // 先卸载界面（会取消事件监听，需要 Tauri 的内部对象还在），再清掉 IPC 模拟
  cleanup()
  clearMocks()
  document.documentElement.removeAttribute('data-mode')
})

const subjectOf = (name: string) => fixture(name).document!.subject!

describe('7.3 空状态与打开文件', () => {
  it('启动时是空状态：标题、说明、打开按钮、快捷键提示和隐私说明；工具栏没有“打开…”', async () => {
    await startApp()
    expect(screen.getByRole('heading', { name: 'Open an email file' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Open file…' })).toBeInTheDocument()
    expect(screen.getByText(/Nothing is uploaded unless you choose to/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Open…' })).not.toBeInTheDocument()
    expect(screen.queryByRole('tablist')).not.toBeInTheDocument()
  })

  it('快捷键提示按平台显示：macOS 是 ⌘O，其余是 Ctrl+O', async () => {
    const mac = await startApp({ platform: 'mac' })
    expect(screen.getByText('⌘O')).toBeInTheDocument()
    mac.view.unmount()
    await startApp({ platform: 'other' })
    expect(screen.getByText('Ctrl+O')).toBeInTheDocument()
    expect(screen.queryByText('⌘O')).not.toBeInTheDocument()
  })

  it('点“打开文件…”调用 open_with_dialog（不带任何参数，不传路径）', async () => {
    const { api, user } = await startApp()
    await user.click(screen.getByRole('button', { name: 'Open file…' }))
    expect(callsOf(api, 'open_with_dialog')).toEqual([{ command: 'open_with_dialog' }])
  })

  it('Ctrl+O（非 macOS）调用 open_with_dialog；⌘O 在非 macOS 上不响应', async () => {
    const { api, user } = await startApp({ platform: 'other' })
    await user.keyboard('{Control>}o{/Control}')
    expect(callsOf(api, 'open_with_dialog')).toHaveLength(1)
    await user.keyboard('{Meta>}o{/Meta}')
    await user.keyboard('o')
    expect(callsOf(api, 'open_with_dialog')).toHaveLength(1)
  })

  it('⌘O（macOS）调用 open_with_dialog；Ctrl+O 在 macOS 上不响应', async () => {
    const { api, user } = await startApp({ platform: 'mac' })
    await user.keyboard('{Meta>}o{/Meta}')
    expect(callsOf(api, 'open_with_dialog')).toHaveLength(1)
    await user.keyboard('{Control>}o{/Control}')
    expect(callsOf(api, 'open_with_dialog')).toHaveLength(1)
  })

  it('已有邮件打开时，工具栏的“打开…”按钮和标签栏的“+”也调用 open_with_dialog', async () => {
    const { api, user } = await startApp()
    await deliver(api, 'headers-normal')
    await user.click(screen.getByRole('button', { name: 'Open…' }))
    expect(callsOf(api, 'open_with_dialog')).toHaveLength(1)
    await deliver(api, 'plain-text') // 两个标签页，标签栏出现
    await user.click(screen.getByRole('button', { name: 'Open file' }))
    expect(callsOf(api, 'open_with_dialog')).toHaveLength(2)
  })

  it('有对话框（设置）打开时，快捷键不响应', async () => {
    const { api, user } = await startApp()
    await user.click(screen.getByRole('button', { name: 'Settings' }))
    await user.keyboard('{Control>}o{/Control}')
    expect(callsOf(api, 'open_with_dialog')).toHaveLength(0)
  })

  it('场景“选择单个文件”：选择一个 .msg 文件后打开并显示内容，不显示标签栏', async () => {
    const { api } = await startApp()
    await chooseInDialog(api, 'rtf-only')
    expect(screen.getByRole('heading', { level: 1, name: subjectOf('rtf-only') })).toBeInTheDocument()
    expect(screen.queryByRole('tablist')).not.toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Open an email file' })).not.toBeInTheDocument()
  })

  it('场景“取消对话框”：保持当前状态不变，不显示错误', async () => {
    const { api, user } = await startApp()
    await user.click(screen.getByRole('button', { name: 'Open file…' })) // 队列为空 = 用户取消
    expect(callsOf(api, 'open_with_dialog')).toHaveLength(1)
    expect(screen.getByRole('heading', { name: 'Open an email file' })).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(toastRegion()).not.toBeInTheDocument()

    await deliver(api, 'headers-normal')
    await user.click(screen.getByRole('button', { name: 'Open…' }))
    expect(screen.getByRole('heading', { level: 1, name: subjectOf('headers-normal') })).toBeInTheDocument()
    expect(toastRegion()).not.toBeInTheDocument()
  })

  it('open_with_dialog 失败（dialog_failed）时用 toast 提示，界面保持可用', async () => {
    const { api, user } = await startApp()
    api.failNext('openWithDialog', 'dialog_failed')
    await user.click(screen.getByRole('button', { name: 'Open file…' }))
    const region = await screen.findByRole('region', { name: 'Notifications' })
    expect(within(region).getByText("Couldn't open the file dialog")).toBeInTheDocument()
    expect(within(region).getByText('The system dialog could not be shown.')).toBeInTheDocument()
    await user.click(within(region).getByRole('button', { name: 'Dismiss' }))
    expect(toastRegion()).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Open file…' })).toBeInTheDocument()
  })
})

describe('7.3 启动：先注册事件监听，再调用 app_ready', () => {
  it('四个监听都注册完成之后才调用 app_ready，并且只调用一次（含 StrictMode 的重复挂载）', async () => {
    for (const strict of [false, true]) {
      const { api, view } = await startApp({ strict })
      expect(api.timeline).toContain('app_ready')
      const ready = api.timeline.indexOf('app_ready')
      const listens = api.timeline.map((entry, i) => [entry, i] as const).filter(([entry]) => entry.startsWith('listen '))
      expect(listens.map(([entry]) => entry)).toEqual(
        expect.arrayContaining(['listen efv://document-opened', 'listen efv://document-open-failed', 'listen efv://open-batch-finished']),
      )
      expect(listens.every(([, i]) => i < ready), `strict=${strict}`).toBe(true)
      expect(callsOf(api, 'app_ready')).toHaveLength(1)
      view.unmount()
      // 卸载后取消全部监听，没有泄漏
      expect(api.listenerCount()).toBe(0)
    }
  })

  it('app_ready 之前 Rust 排队的文件（启动参数、单实例转发），在 app_ready 之后显示出来', async () => {
    await startApp({
      prepare: async (api) => {
        await api.deliverBatch([fixture('headers-normal'), fixture('plain-text')])
      },
    })
    expect(await screen.findAllByRole('tab')).toHaveLength(2)
    expect(screen.getByRole('heading', { level: 1, name: subjectOf('headers-normal') })).toBeInTheDocument()
  })
})

describe('7.3 经真实实现和 Tauri IPC 模拟：命令名、参数和事件接线正确', () => {
  async function startWithIpc() {
    const invoked: { cmd: string; args: unknown }[] = []
    mockWindows('main')
    mockIPC(
      (cmd, args) => {
        if (cmd === 'save_attachment') return { path: '/Users/me/Downloads/report.pdf' }
        void args
      },
      { shouldMockEvents: true },
    )
    // 事件监听命令（plugin:event|listen）被 mockIPC 自己消化，不会到上面的回调里；
    // 这里包一层，记录所有经过 invoke 的调用，才能检查“先注册监听，再 app_ready”
    const internals = (window as unknown as { __TAURI_INTERNALS__: { invoke: (cmd: string, args?: unknown, options?: unknown) => Promise<unknown> } }).__TAURI_INTERNALS__
    const inner = internals.invoke
    internals.invoke = (cmd, args, options) => {
      invoked.push({ cmd, args })
      return inner(cmd, args, options)
    }
    const app = await startApp({ api: createTauriApi() })
    await waitFor(() => expect(invoked.some((c) => c.cmd === 'app_ready')).toBe(true))
    return { ...app, invoked }
  }

  it('listen 全部先于 app_ready；点“打开文件…”调用 open_with_dialog，参数为空', async () => {
    const { user, invoked } = await startWithIpc()
    const names = invoked.map((c) => c.cmd)
    const ready = names.indexOf('app_ready')
    expect(names.slice(0, ready).filter((n) => n === 'plugin:event|listen')).toHaveLength(9) // 3 个业务事件 + 4 个拖放事件 + 2 个菜单事件
    expect(names.filter((n) => n === 'app_ready')).toHaveLength(1)
    expect(names.slice(0, ready).every((n) => n === 'plugin:event|listen')).toBe(true)
    await user.click(screen.getByRole('button', { name: 'Open file…' }))
    expect(invoked.filter((c) => c.cmd === 'open_with_dialog')).toEqual([{ cmd: 'open_with_dialog', args: {} }])
  })

  it('Rust 的 document-opened 事件显示成标签页；保存附件的参数是 handle 和 attachmentId', async () => {
    const { user, invoked } = await startWithIpc()
    const file = fixture('attachments-multi')
    await act(async () => {
      await emit('efv://document-opened', { batchId: 1, handle: 42, fileName: file.fileName, document: file.document })
    })
    expect(await screen.findByRole('heading', { level: 1, name: file.document!.subject! })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Save report.pdf…' }))
    expect(invoked.filter((c) => c.cmd === 'save_attachment')).toEqual([{ cmd: 'save_attachment', args: { handle: 42, attachmentId: '2' } }])
    await user.keyboard('{Control>}w{/Control}')
    expect(invoked.filter((c) => c.cmd === 'close_document')).toEqual([{ cmd: 'close_document', args: { handle: 42 } }])
  })

  it('拖放事件经 onDragDropEvent 显示遮罩，drop 时只隐藏遮罩', async () => {
    await startWithIpc()
    const position = { type: 'Physical', x: 1, y: 1 }
    await act(async () => {
      await emit('tauri://drag-enter', { paths: ['/a/1.eml', '/a/2.msg'], position })
    })
    expect(screen.getByRole('heading', { name: 'Release to open 2 files' })).toBeInTheDocument()
    await act(async () => {
      await emit('tauri://drag-drop', { paths: ['/a/1.eml', '/a/2.msg'], position })
    })
    expect(screen.queryByRole('heading', { name: /Release to open/ })).not.toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Open an email file' })).toBeInTheDocument()
  })
})

describe('7.3 拖放', () => {
  it('场景“拖入一个 EML 文件”：enter 显示遮罩和文件数量；drop 时只隐藏遮罩，打开由 Rust 完成', async () => {
    const { api } = await startApp()
    act(() => api.emitDrag({ type: 'enter', count: 1 }))
    expect(screen.getByRole('heading', { name: 'Release to open 1 file' })).toBeInTheDocument()
    expect(screen.getByText('Each file opens in its own tab. Text and links are ignored.')).toBeInTheDocument()
    act(() => api.emitDrag({ type: 'drop' }))
    expect(screen.queryByRole('heading', { name: /Release to open/ })).not.toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Open an email file' })).toBeInTheDocument()
    // 前端不发任何命令：真正的打开由 Rust 在窗口的拖放事件里处理
    expect(api.calls.map((c) => c.command)).toEqual(['app_ready'])
    // Rust 随后发来 document-opened
    await deliver(api, 'plain-text')
    expect(screen.getByRole('heading', { level: 1, name: subjectOf('plain-text') })).toBeInTheDocument()
  })

  it('over 不改变遮罩；多个文件时显示数量（复数）；leave 隐藏遮罩', async () => {
    const { api } = await startApp()
    act(() => api.emitDrag({ type: 'enter', count: 3 }))
    expect(screen.getByRole('heading', { name: 'Release to open 3 files' })).toBeInTheDocument()
    act(() => api.emitDrag({ type: 'over' }))
    expect(screen.getByRole('heading', { name: 'Release to open 3 files' })).toBeInTheDocument()
    act(() => api.emitDrag({ type: 'leave' }))
    expect(screen.queryByRole('heading', { name: /Release to open/ })).not.toBeInTheDocument()
  })

  it('场景“拖入非文件内容”：拖入文字或链接（没有文件）时忽略，不显示遮罩也不显示错误', async () => {
    const { api } = await startApp()
    act(() => api.emitDrag({ type: 'enter', count: 0 }))
    expect(screen.queryByRole('heading', { name: /Release to open/ })).not.toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Open an email file' })).toBeInTheDocument()
    act(() => api.emitDrag({ type: 'drop' }))
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(toastRegion()).not.toBeInTheDocument()
  })

  it('已有邮件打开时，遮罩盖在内容区上（DropOverlay），邮件仍在下面', async () => {
    const { api } = await startApp()
    await deliver(api, 'headers-normal')
    act(() => api.emitDrag({ type: 'enter', count: 2 }))
    const overlay = document.querySelector('.drop-overlay') as HTMLElement
    expect(within(overlay).getByText('Release to open 2 files')).toBeInTheDocument()
    act(() => api.emitDrag({ type: 'leave' }))
    expect(document.querySelector('.drop-overlay')).toBeNull()
    expect(screen.getByRole('heading', { level: 1, name: subjectOf('headers-normal') })).toBeInTheDocument()
  })

  it('中文界面：遮罩文案是中文，数量代入', async () => {
    const { api } = await startApp({ language: 'zh-CN' })
    act(() => api.emitDrag({ type: 'enter', count: 3 }))
    expect(screen.getByRole('heading', { name: '松开以打开 3 个文件' })).toBeInTheDocument()
    act(() => api.emitDrag({ type: 'enter', count: 1 }))
    expect(screen.getByRole('heading', { name: '松开以打开 1 个文件' })).toBeInTheDocument()
  })
})

describe('7.8 无法识别或损坏的文件', () => {
  const errorFile = (code: string, fileName = 'photo.png') => ({ fileName, error: { code, message: code } })

  it('场景“打开一张图片”：错误标签页里显示文件名和“不支持的格式”，应用仍可继续打开其他文件', async () => {
    const { api } = await startApp()
    await chooseInDialog(api, 'error-unsupported')
    const alert = screen.getByRole('alert')
    expect(within(alert).getByText('Unsupported format')).toBeInTheDocument()
    expect(within(alert).getByRole('heading', { name: "This file isn't a supported email" })).toBeInTheDocument()
    expect(within(alert).getByTitle('not-an-email.png')).toBeInTheDocument()
    // 只有一个文件，不显示标签栏，也没有汇总通知
    expect(screen.queryByRole('tablist')).not.toBeInTheDocument()
    expect(toastRegion()).not.toBeInTheDocument()

    await chooseInDialog(api, 'headers-normal')
    expect(screen.getByRole('heading', { level: 1, name: subjectOf('headers-normal') })).toBeInTheDocument()
  })

  it.each([
    ['unsupported_format', 'Unsupported format', "This file isn't a supported email"],
    ['corrupted', 'Damaged file', 'This file is damaged'],
    ['sidecar_crashed', 'Damaged file', 'This file is damaged'],
    ['timeout', 'Damaged file', 'This file is damaged'],
    ['unreadable', "Can't read file", "This file can't be read"],
    ['internal_error', 'Something went wrong', "This file couldn't be opened"],
    ['dialog_failed', 'Something went wrong', "This file couldn't be opened"],
    ['totally_new_code', 'Something went wrong', "This file couldn't be opened"],
  ])('错误码 %s -> “%s”', async (code, reason, title) => {
    const { api } = await startApp()
    await deliver(api, errorFile(code))
    const alert = screen.getByRole('alert')
    expect(within(alert).getByText(reason)).toBeInTheDocument()
    expect(within(alert).getByRole('heading', { name: title })).toBeInTheDocument()
    expect(within(alert).getByTitle('photo.png')).toBeInTheDocument()
  })

  it('场景“解析过程中崩溃”：显示已损坏或无法解析的提示，界面不退出，随后打开其他文件仍然成功', async () => {
    const { api } = await startApp()
    await deliver(api, errorFile('sidecar_crashed', 'crash.msg'))
    expect(screen.getByText('Damaged file')).toBeInTheDocument()
    await chooseInDialog(api, 'headers-normal', 'plain-text')
    expect(await screen.findAllByRole('tab')).toHaveLength(3)
    expect(screen.getByRole('heading', { level: 1, name: subjectOf('headers-normal') })).toBeInTheDocument()
  })

  it('错误标签页的操作：“打开其他文件”调用 open_with_dialog；“关闭标签页”关闭它（不调用 close_document）', async () => {
    const { api, user } = await startApp()
    await deliver(api, errorFile('unreadable'))
    await user.click(screen.getByRole('button', { name: 'Open another file' }))
    expect(callsOf(api, 'open_with_dialog')).toHaveLength(1)
    await user.click(screen.getByRole('button', { name: 'Close tab' }))
    expect(screen.getByRole('heading', { name: 'Open an email file' })).toBeInTheDocument()
    expect(callsOf(api, 'close_document')).toHaveLength(0)
  })

  it('中文界面：错误说明和文件名', async () => {
    const { api } = await startApp({ language: 'zh-CN' })
    await deliver(api, errorFile('corrupted', '坏文件.msg'))
    const alert = screen.getByRole('alert')
    expect(within(alert).getByText('文件已损坏')).toBeInTheDocument()
    expect(within(alert).getByRole('heading', { name: '这个文件已损坏' })).toBeInTheDocument()
    expect(within(alert).getByTitle('坏文件.msg')).toBeInTheDocument()
  })
})

describe('7.8 / 7.9 场景“多个文件中有一个损坏”', () => {
  async function openThree() {
    const app = await startApp()
    await deliver(app.api, 'headers-normal', 'error-corrupted', 'plain-text')
    return app
  }

  it('另外两个文件正常打开；损坏的那个显示为错误标签页（红色图标），并另加一条汇总通知', async () => {
    await openThree()
    expect(tabs().map((t) => t.textContent)).toEqual(['headers-normal', 'truncated', 'body-plain-only'])
    const errorTab = screen.getByRole('tab', { name: 'truncated' })
    expect(errorTab.closest('.tab')).toHaveClass('tab--error')
    expect(screen.getByRole('tab', { name: 'headers-normal' }).closest('.tab')).not.toHaveClass('tab--error')
    const region = screen.getByRole('region', { name: 'Notifications' })
    expect(within(region).getByText('Opened 2 of 3 files')).toBeInTheDocument()
    expect(region.textContent).toContain('truncated.msg')
    expect(region.textContent).toContain('Damaged file')
  })

  it('三封邮件都能独立查看：切换标签页显示各自的内容', async () => {
    const { user } = await openThree()
    // 一个批次里第一封成功打开的邮件获得焦点
    expect(screen.getByRole('tab', { name: 'headers-normal' })).toHaveAttribute('aria-selected', 'true')
    expect(screen.getByRole('heading', { level: 1, name: subjectOf('headers-normal') })).toBeInTheDocument()
    await user.click(screen.getByRole('tab', { name: 'body-plain-only' }))
    expect(screen.getByRole('heading', { level: 1, name: subjectOf('plain-text') })).toBeInTheDocument()
    await user.click(screen.getByRole('tab', { name: 'truncated' }))
    expect(screen.getByRole('alert')).toHaveTextContent('Damaged file')
  })

  it('汇总通知的“查看”切到失败的标签页并收起通知；“关闭”只收起通知', async () => {
    const { user } = await openThree()
    await user.click(within(screen.getByRole('region', { name: 'Notifications' })).getByRole('button', { name: 'Show' }))
    expect(screen.getByRole('tab', { name: 'truncated' })).toHaveAttribute('aria-selected', 'true')
    expect(toastRegion()).not.toBeInTheDocument()
  })

  it('汇总通知可以关闭', async () => {
    const { user } = await openThree()
    await user.click(within(screen.getByRole('region', { name: 'Notifications' })).getByRole('button', { name: 'Dismiss' }))
    expect(toastRegion()).not.toBeInTheDocument()
    expect(tabs()).toHaveLength(3)
  })

  it('三个都能关闭：每关一个，邮件对应的句柄调用 close_document，错误标签页不调用', async () => {
    const { api, user } = await openThree()
    const handles = [...api.openHandles]
    expect(handles).toHaveLength(2)
    await user.click(screen.getByRole('button', { name: 'Close headers-normal' }))
    await user.click(screen.getByRole('button', { name: 'Close truncated' }))
    expect(screen.queryByRole('tablist')).not.toBeInTheDocument() // 只剩一个：标签栏隐藏
    await user.keyboard('{Control>}w{/Control}')
    expect(screen.getByRole('heading', { name: 'Open an email file' })).toBeInTheDocument()
    expect(callsOf(api, 'close_document').map((c) => c.args)).toEqual(handles.map((handle) => ({ handle })))
    expect(api.openHandles.size).toBe(0)
  })

  it('全部成功或全部失败时没有汇总通知', async () => {
    const { api } = await startApp()
    await deliver(api, 'headers-normal', 'plain-text')
    expect(toastRegion()).not.toBeInTheDocument()
    await deliver(api, 'error-corrupted', 'error-unsupported')
    expect(toastRegion()).not.toBeInTheDocument()
    expect(tabs()).toHaveLength(4)
  })

  it('汇总通知列出失败的文件和各自的原因；超过 5 个时折叠成“另外 N 个”', async () => {
    const { api } = await startApp()
    const bad = (n: number) => ({ fileName: `bad${n}.png`, error: { code: n === 1 ? 'unsupported_format' : 'corrupted' } })
    await deliver(api, 'headers-normal', bad(1), bad(2), bad(3), bad(4), bad(5), bad(6), bad(7))
    const region = screen.getByRole('region', { name: 'Notifications' })
    expect(within(region).getByText('Opened 1 of 8 files')).toBeInTheDocument()
    expect(region.textContent).toContain('bad1.png')
    expect(region.textContent).toContain('Unsupported format')
    expect(region.textContent).toContain('bad5.png')
    expect(region.textContent).not.toContain('bad6.png')
    expect(region.textContent).toContain('+2 more')
  })

  it('中文界面：汇总通知', async () => {
    const { api } = await startApp({ language: 'zh-CN' })
    await deliver(api, 'headers-normal', 'error-unsupported', 'plain-text')
    const region = screen.getByRole('region', { name: '通知' })
    expect(within(region).getByText('已打开 3 个文件中的 2 个')).toBeInTheDocument()
    expect(region.textContent).toContain('not-an-email.png')
    expect(region.textContent).toContain('不支持的格式')
    expect(within(region).getByRole('button', { name: '查看' })).toBeInTheDocument()
    expect(within(region).getByRole('button', { name: '关闭' })).toBeInTheDocument()
  })
})

describe('7.9 单窗口多标签页', () => {
  it('只有一个文件时隐藏标签栏；第二个文件打开后出现，并激活新打开的那个', async () => {
    const { api } = await startApp()
    await deliver(api, 'headers-normal')
    expect(screen.queryByRole('tablist')).not.toBeInTheDocument()
    await chooseInDialog(api, 'plain-text')
    expect(screen.getByRole('tablist', { name: 'Open files' })).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: 'body-plain-only' })).toHaveAttribute('aria-selected', 'true')
    expect(screen.getByRole('heading', { level: 1, name: subjectOf('plain-text') })).toBeInTheDocument()
    expect(screen.getByRole('tabpanel')).toHaveAccessibleName('body-plain-only')
  })

  it('标签页上是不带扩展名的文件名，完整文件名在悬停提示里', async () => {
    const { api } = await startApp()
    await deliver(api, 'headers-normal')
    await deliver(api, 'rtf-only')
    expect(screen.getByRole('tab', { name: 'body-rtf-only' }).closest('.tab')).toHaveAttribute('title', 'body-rtf-only.msg')
  })

  it('点关闭按钮关闭标签页：对它的句柄调用 close_document，当前标签页是被关的就激活相邻的', async () => {
    const { api, user } = await startApp()
    await deliver(api, 'headers-normal')
    await deliver(api, 'plain-text')
    await deliver(api, 'rtf-only')
    const handles = [...api.openHandles]
    await user.click(screen.getByRole('tab', { name: 'body-plain-only' }))
    await user.click(screen.getByRole('button', { name: 'Close body-plain-only' }))
    expect(callsOf(api, 'close_document')).toEqual([{ command: 'close_document', args: { handle: handles[1] } }])
    expect(tabs().map((t) => t.textContent)).toEqual(['headers-normal', 'body-rtf-only'])
    expect(screen.getByRole('tab', { name: 'body-rtf-only' })).toHaveAttribute('aria-selected', 'true')
  })

  it('Ctrl+W 关闭当前标签页（非 macOS），⌘W 在非 macOS 上不响应', async () => {
    const { api, user } = await startApp({ platform: 'other' })
    await deliver(api, 'headers-normal')
    await deliver(api, 'plain-text')
    await user.keyboard('{Meta>}w{/Meta}')
    expect(tabs()).toHaveLength(2)
    await user.keyboard('{Control>}w{/Control}')
    // 只剩一个文件：标签栏隐藏，显示留下的那封邮件
    expect(screen.queryByRole('tablist')).not.toBeInTheDocument()
    expect(screen.getByRole('heading', { level: 1, name: subjectOf('headers-normal') })).toBeInTheDocument()
    expect(callsOf(api, 'close_document')).toHaveLength(1)
  })

  it('⌘W 关闭当前标签页（macOS）；关闭最后一个后回到空状态，不退出应用；没有标签页时 ⌘W 什么也不做', async () => {
    const { api, user } = await startApp({ platform: 'mac' })
    await user.keyboard('{Meta>}w{/Meta}')
    expect(callsOf(api, 'close_document')).toHaveLength(0)
    await deliver(api, 'headers-normal')
    await user.keyboard('{Meta>}w{/Meta}')
    expect(screen.getByRole('heading', { name: 'Open an email file' })).toBeInTheDocument()
    expect(callsOf(api, 'close_document')).toHaveLength(1)
  })

  it('有对话框打开时 Ctrl+W 不关闭标签页', async () => {
    const { api, user } = await startApp()
    await deliver(api, 'headers-normal')
    await user.click(screen.getByRole('button', { name: 'Settings' }))
    await user.keyboard('{Control>}w{/Control}')
    expect(callsOf(api, 'close_document')).toHaveLength(0)
  })

  it('键盘：左右方向键在标签页之间切换', async () => {
    const { api, user } = await startApp()
    await deliver(api, 'headers-normal', 'plain-text')
    screen.getByRole('tab', { name: 'headers-normal' }).focus()
    await user.keyboard('{ArrowRight}')
    expect(screen.getByRole('tab', { name: 'body-plain-only' })).toHaveAttribute('aria-selected', 'true')
  })

  it('切换标签页时邮件头和附件列表的折叠状态重置（key 不同）', async () => {
    const { api, user } = await startApp()
    const many = Array.from({ length: 6 }, (_, i) => ({ name: `Person ${i}`, address: `p${i}@example.com` }))
    const manyAttachments = Array.from({ length: 8 }, (_, i) => ({ id: String(i), fileName: `f${i}.txt`, size: 10, mimeType: 'text/plain', isNestedEmail: false }))
    await deliver(api, {
      ...fixture('headers-normal'),
      fileName: 'first.eml',
      document: { ...fixture('headers-normal').document!, to: many, attachments: manyAttachments },
    })
    await deliver(api, 'plain-text')

    await user.click(screen.getByRole('tab', { name: 'first' }))
    await user.click(screen.getByRole('button', { name: '+3 more' }))
    await user.click(screen.getByRole('button', { name: 'Show all 8' }))
    expect(screen.getByRole('button', { name: 'Show less' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Show fewer' })).toBeInTheDocument()

    await user.click(screen.getByRole('tab', { name: 'body-plain-only' }))
    await user.click(screen.getByRole('tab', { name: 'first' }))
    expect(screen.getByRole('button', { name: '+3 more' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Show all 8' })).toBeInTheDocument()
  })

  it('同一个文件打开两次是两个独立的标签页（各自的句柄）', async () => {
    const { api } = await startApp()
    await deliver(api, 'headers-normal')
    await deliver(api, 'headers-normal')
    expect(tabs()).toHaveLength(2)
    expect(api.openHandles.size).toBe(2)
  })
})

describe('7.9 嵌套邮件在当前标签页内逐层进入，用面包屑返回', () => {
  const openLevel = async (user: Awaited<ReturnType<typeof startApp>>['user']) => user.click(screen.getByRole('button', { name: 'Open' }))
  const crumbs = () => within(screen.getByRole('navigation', { name: 'Message levels' }))

  it('场景“MSG 中附带另一封 MSG”：点“打开”调用 open_nested，在当前标签页里显示嵌套邮件，没有新标签页', async () => {
    const { api, user } = await startApp()
    await deliver(api, 'nested-msg-two-levels')
    const outer = [...api.openHandles][0]
    expect(screen.queryByRole('navigation', { name: 'Message levels' })).not.toBeInTheDocument()
    await openLevel(user)
    expect(callsOf(api, 'open_nested')).toEqual([{ command: 'open_nested', args: { handle: outer, attachmentId: expect.any(String) } }])
    expect(screen.getByRole('heading', { level: 1, name: /Level 2/ })).toBeInTheDocument()
    expect(screen.queryByRole('tablist')).not.toBeInTheDocument()
    expect(crumbs().getAllByRole('listitem')).toHaveLength(2)
    expect(crumbs().getByText(/Level 2/)).toHaveAttribute('aria-current', 'page')
  })

  it('场景“多层嵌套”：继续在应用内打开下一层；每一层的面包屑都能点回去', async () => {
    const { api, user } = await startApp()
    await deliver(api, 'nested-two-levels')
    await openLevel(user)
    expect(screen.getByRole('heading', { level: 1, name: /Level 2/ })).toBeInTheDocument()
    await openLevel(user)
    expect(screen.getByRole('heading', { level: 1, name: /Level 3/ })).toBeInTheDocument()
    expect(crumbs().getAllByRole('listitem').map((li) => li.textContent)).toEqual([
      'nested-rfc822-two-levels',
      'Level 2 (middle) message',
      'Level 3 (innermost) message',
    ])
    expect(callsOf(api, 'open_nested')).toHaveLength(2)

    // 点第一级：回到最外层，更深的两级文档被关闭
    const [outer, middle, inner] = [...api.openHandles]
    await user.click(crumbs().getByRole('button', { name: 'nested-rfc822-two-levels' }))
    expect(screen.getByRole('heading', { level: 1, name: /Level 1/ })).toBeInTheDocument()
    expect(screen.queryByRole('navigation', { name: 'Message levels' })).not.toBeInTheDocument()
    expect(callsOf(api, 'close_document').map((c) => (c.args as { handle: number }).handle).sort()).toEqual([middle, inner].sort())
    expect([...api.openHandles]).toEqual([outer])
  })

  it('返回按钮回到上一级，并关闭刚才那一级', async () => {
    const { api, user } = await startApp()
    await deliver(api, 'nested-two-levels')
    await openLevel(user)
    await openLevel(user)
    const [, middle, inner] = [...api.openHandles]
    await user.click(screen.getByRole('button', { name: 'Back' }))
    expect(screen.getByRole('heading', { level: 1, name: /Level 2/ })).toBeInTheDocument()
    expect(callsOf(api, 'close_document')).toEqual([{ command: 'close_document', args: { handle: inner } }])
    expect(api.openHandles.has(middle)).toBe(true)
  })

  it('关闭标签页时，对面包屑链上的所有句柄调用 close_document', async () => {
    const { api, user } = await startApp()
    await deliver(api, 'nested-two-levels')
    await openLevel(user)
    await openLevel(user)
    const handles = [...api.openHandles]
    expect(handles).toHaveLength(3)
    await user.keyboard('{Control>}w{/Control}')
    expect(callsOf(api, 'close_document').map((c) => (c.args as { handle: number }).handle).sort()).toEqual(handles.sort())
    expect(api.openHandles.size).toBe(0)
  })

  it('进入嵌套邮件后，邮件头、附件和正文都换成这一层的；每一层的远程内容各自阻止', async () => {
    const { api, user } = await startApp()
    const nested = { fileName: 'inner.eml', document: fixture('marketing-remote').document! }
    const outer = {
      ...fixture('attachments-multi'),
      fileName: 'outer.eml',
      document: {
        ...fixture('attachments-multi').document!,
        hasRemoteContent: true,
        attachments: [{ id: 'n1', fileName: 'inner.eml', size: 100, mimeType: 'message/rfc822', isNestedEmail: true }],
      },
      nested: { n1: nested },
    }
    await deliver(api, outer)
    await user.click(screen.getByRole('button', { name: 'Load remote content' }))
    expect(screen.getByText(/Remote content is loaded for this message/)).toBeInTheDocument()
    await openLevel(user)
    // 里面这一层是另一封邮件，仍然阻止
    expect(screen.getByRole('button', { name: 'Load remote content' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { level: 1, name: nested.document.subject! })).toBeInTheDocument()
  })

  it('场景“附件声明为邮件但内容不是邮件”：toast 说明原因，仍停留在当前这一层', async () => {
    const { api, user } = await startApp()
    await deliver(api, {
      ...fixture('nested-two-levels'),
      nested: { '2': { fileName: 'Level 2 (middle) message.eml', error: { code: 'unsupported_format' } } },
    })
    await openLevel(user)
    const region = await screen.findByRole('region', { name: 'Notifications' })
    expect(within(region).getByText("Couldn't open Level 2 (middle) message.eml as an email")).toBeInTheDocument()
    expect(within(region).getByText("This attachment isn't a valid email.")).toBeInTheDocument()
    expect(screen.getByRole('heading', { level: 1, name: /Level 1/ })).toBeInTheDocument()
    expect(screen.queryByRole('navigation', { name: 'Message levels' })).not.toBeInTheDocument()
  })

  it('打开嵌套邮件超时：属于解析，仍按“附件已损坏，或无法读取”显示', async () => {
    const { api, user } = await startApp()
    await deliver(api, {
      ...fixture('nested-two-levels'),
      nested: { '2': { fileName: 'Level 2 (middle) message.eml', error: { code: 'timeout' } } },
    })
    await openLevel(user)
    const region = await screen.findByRole('region', { name: 'Notifications' })
    expect(within(region).getByText("Couldn't open Level 2 (middle) message.eml as an email")).toBeInTheDocument()
    expect(within(region).getByText("The attachment is damaged or can't be read.")).toBeInTheDocument()
    expect(region.textContent).not.toContain('timed out')
  })

  it('open_nested 返回之前标签页被关闭：新文档被关闭，不留下句柄', async () => {
    const { api, user } = await startApp()
    await deliver(api, 'nested-two-levels')
    let release!: () => void
    const gate = new Promise<void>((resolve) => (release = resolve))
    api.override('openNested', async () => {
      await gate
      return { handle: 999, document: fixture('nested-two-levels').nested!['2'].document! }
    })
    // 点“打开”之后、返回之前关闭标签页
    await openLevel(user)
    await user.keyboard('{Control>}w{/Control}')
    await act(async () => {
      release()
      await gate
    })
    await waitFor(() => expect(callsOf(api, 'close_document').map((c) => (c.args as { handle: number }).handle)).toContain(999))
    expect(screen.getByRole('heading', { name: 'Open an email file' })).toBeInTheDocument()
  })

  it('open_nested 返回之前已经进入了别的层级（连点两次“打开”）：多出来的那个文档被关闭', async () => {
    const { api, user } = await startApp()
    await deliver(api, 'nested-two-levels')
    const file = fixture('nested-two-levels')
    const pending: (() => void)[] = []
    let next = 100
    api.override('openNested', async () => {
      await new Promise<void>((resolve) => pending.push(resolve))
      return { handle: next++, document: file.nested!['2'].document! }
    })
    await openLevel(user)
    await openLevel(user)
    expect(pending).toHaveLength(2)
    await act(async () => {
      pending.forEach((release) => release())
      await Promise.resolve()
    })
    await waitFor(() => expect(callsOf(api, 'close_document')).toHaveLength(1))
    expect(callsOf(api, 'close_document')[0].args).toEqual({ handle: 101 })
    expect(crumbs().getAllByRole('listitem')).toHaveLength(2)
  })
})

describe('切换界面语言', () => {
  it('已经显示的通知、错误标签页和遮罩立即换成新语言，邮件内容不变', async () => {
    const { api, user } = await startApp({ language: 'en' })
    await deliver(api, 'headers-normal', 'error-corrupted', 'plain-text')
    expect(screen.getByText('Opened 2 of 3 files')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Settings' }))
    await user.click(screen.getByRole('radio', { name: /简体中文/ }))
    await user.click(screen.getByRole('button', { name: '完成' }))
    expect(screen.getByText('已打开 3 个文件中的 2 个')).toBeInTheDocument()
    expect(screen.getByRole('region', { name: '通知' }).textContent).toContain('文件已损坏')
    expect(screen.getByRole('heading', { level: 1, name: subjectOf('headers-normal') })).toBeInTheDocument()
    await user.click(screen.getByRole('tab', { name: 'truncated' }))
    expect(screen.getByRole('heading', { name: '这个文件已损坏' })).toBeInTheDocument()
  })
})

describe('窗口外的拖放事件不会影响 StrictMode 下的状态', () => {
  it('卸载后事件不再处理', async () => {
    const { api, view } = await startApp({ strict: true })
    view.unmount()
    expect(() => api.emitDrag({ type: 'enter', count: 3 })).not.toThrow()
    fireEvent.keyDown(window, { key: 'o', ctrlKey: true })
    expect(callsOf(api, 'open_with_dialog')).toHaveLength(0)
  })
})
