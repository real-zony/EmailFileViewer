// 真实实现通过 Tauri 官方的 mockIPC 测试：断言 invoke 的命令名和参数（D11），以及事件的监听和拖放事件的转换
import { emit } from '@tauri-apps/api/event'
import { clearMocks, mockIPC, mockWindows } from '@tauri-apps/api/mocks'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createTauriApi, toApiError } from '../tauriApi.ts'
import { ApiError, type EmailDocument } from '../types.ts'

const emptyDocument: EmailDocument = {
  from: [], to: [], cc: [], bcc: [], replyTo: [], subject: null, sentAtUtc: null, dateHeaderRaw: null,
  body: { sourceFormat: 'None', html: null }, attachments: [], isSigned: false, isEncrypted: false, hasRemoteContent: false,
}

type Handler = (cmd: string, payload?: unknown) => unknown
let invoked: { cmd: string; args: unknown }[]

function install(handler: Handler = () => undefined) {
  invoked = []
  mockWindows('main')
  mockIPC(
    (cmd, args) => {
      invoked.push({ cmd, args })
      return handler(cmd, args)
    },
    { shouldMockEvents: true },
  )
}

beforeEach(() => install())
afterEach(() => clearMocks())

// 事件监听通过 plugin:event|listen 注册，不算命令调用
const commands = () => invoked.filter((c) => !c.cmd.startsWith('plugin:event|'))

describe('命令（invoke）：命令名和参数名与 D11 一致', () => {
  it('app_ready 和 open_with_dialog 不带参数', async () => {
    const api = createTauriApi()
    await api.appReady()
    await api.openWithDialog()
    expect(commands()).toEqual([
      { cmd: 'app_ready', args: {} },
      { cmd: 'open_with_dialog', args: {} },
    ])
  })

  it('open_nested、close_document、save_attachment、open_attachment 的参数是 handle 和 attachmentId', async () => {
    install((cmd) => (cmd === 'open_nested' ? { handle: 8, document: emptyDocument } : cmd === 'save_attachment' ? { path: '/x/a.pdf' } : undefined))
    const api = createTauriApi()
    await expect(api.openNested(7, '2')).resolves.toEqual({ handle: 8, document: emptyDocument })
    await api.closeDocument(8)
    await expect(api.saveAttachment(7, '2')).resolves.toEqual({ path: '/x/a.pdf' })
    await api.openAttachment(7, '3')
    expect(commands()).toEqual([
      { cmd: 'open_nested', args: { handle: 7, attachmentId: '2' } },
      { cmd: 'close_document', args: { handle: 8 } },
      { cmd: 'save_attachment', args: { handle: 7, attachmentId: '2' } },
      { cmd: 'open_attachment', args: { handle: 7, attachmentId: '3' } },
    ])
  })

  it('save_all_attachments 只传 handle，结果原样返回；用户取消时返回 null', async () => {
    const result = { saved: [{ attachmentId: '2', path: '/x/a.pdf' }], failed: [{ attachmentId: '3', code: 'write_failed' }] }
    install(() => result)
    await expect(createTauriApi().saveAllAttachments(5)).resolves.toEqual(result)
    expect(commands()).toEqual([{ cmd: 'save_all_attachments', args: { handle: 5 } }])
    install(() => null)
    await expect(createTauriApi().saveAllAttachments(5)).resolves.toBeNull()
    await expect(createTauriApi().saveAttachment(5, '1')).resolves.toBeNull()
  })

  it('任何命令都不带文件路径', async () => {
    const api = createTauriApi()
    await api.appReady()
    await api.openWithDialog()
    await api.openNested(1, 'a').catch(() => {})
    await api.closeDocument(1)
    await api.saveAttachment(1, 'a')
    await api.saveAllAttachments(1)
    await api.openAttachment(1, 'a')
    for (const { args } of commands()) {
      expect(Object.keys((args as object | undefined) ?? {}).every((k) => k === 'handle' || k === 'attachmentId')).toBe(true)
    }
  })
})

describe('出错时抛出 { code, message }', () => {
  it('统一成带 code 的 ApiError', async () => {
    install(() => {
      throw { code: 'executable_refused', message: 'refused' }
    })
    const error = await createTauriApi().openAttachment(1, '2').catch((e: unknown) => e)
    expect(error).toBeInstanceOf(ApiError)
    expect(error).toMatchObject({ code: 'executable_refused', message: 'refused' })
  })

  it('toApiError：字符串、Error、没有 code 的对象都变成 unknown', () => {
    expect(toApiError('boom')).toMatchObject({ code: 'unknown', message: 'boom' })
    expect(toApiError(new Error('bad'))).toMatchObject({ code: 'unknown', message: 'bad' })
    expect(toApiError({ message: 'x' })).toMatchObject({ code: 'unknown' })
    expect(toApiError({ code: 'timeout' })).toMatchObject({ code: 'timeout', message: 'timeout' })
    const existing = new ApiError('corrupted')
    expect(toApiError(existing)).toBe(existing)
  })
})

describe('事件（listen）', () => {
  it('三个事件名与 D11 一致，处理函数收到的是 payload', async () => {
    const api = createTauriApi()
    const opened = vi.fn()
    const failed = vi.fn()
    const finished = vi.fn()
    await api.onDocumentOpened(opened)
    await api.onDocumentOpenFailed(failed)
    await api.onOpenBatchFinished(finished)

    const openedEvent = { batchId: 1, handle: 3, fileName: 'a.eml', document: emptyDocument }
    const failedEvent = { batchId: 1, fileName: 'b.png', code: 'unsupported_format', message: 'x' }
    const finishedEvent = { batchId: 1, opened: 1, failed: 1 }
    await emit('efv://document-opened', openedEvent)
    await emit('efv://document-open-failed', failedEvent)
    await emit('efv://open-batch-finished', finishedEvent)

    expect(opened).toHaveBeenCalledExactlyOnceWith(openedEvent)
    expect(failed).toHaveBeenCalledExactlyOnceWith(failedEvent)
    expect(finished).toHaveBeenCalledExactlyOnceWith(finishedEvent)
  })

  it('取消监听后不再收到事件', async () => {
    const api = createTauriApi()
    const handler = vi.fn()
    const unlisten = await api.onOpenBatchFinished(handler)
    unlisten()
    await emit('efv://open-batch-finished', { batchId: 1, opened: 1, failed: 0 })
    expect(handler).not.toHaveBeenCalled()
  })
})

describe('拖放事件（getCurrentWebview().onDragDropEvent）', () => {
  const position = { type: 'Physical', x: 10, y: 20 }

  it('enter 只带文件数量；over、leave、drop 只带类型，路径不往外传', async () => {
    const handler = vi.fn()
    await createTauriApi().onDragDrop(handler)
    await emit('tauri://drag-enter', { paths: ['/a/1.eml', '/a/2.msg', '/a/3.eml'], position })
    await emit('tauri://drag-over', { position })
    await emit('tauri://drag-leave', {})
    await emit('tauri://drag-drop', { paths: ['/a/1.eml'], position })
    expect(handler.mock.calls.map(([e]) => e)).toEqual([{ type: 'enter', count: 3 }, { type: 'over' }, { type: 'leave' }, { type: 'drop' }])
    expect(JSON.stringify(handler.mock.calls)).not.toContain('/a/')
  })

  it('拖入文字或链接时 paths 为空，数量为 0', async () => {
    const handler = vi.fn()
    await createTauriApi().onDragDrop(handler)
    await emit('tauri://drag-enter', { paths: [], position })
    expect(handler).toHaveBeenCalledExactlyOnceWith({ type: 'enter', count: 0 })
  })
})
