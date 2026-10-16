import { describe, expect, it, vi } from 'vitest'
import { fixture } from '../../dev/fixtures.ts'
import { isExecutableFileName } from '../executable.ts'
import { createMockApi } from '../mockApi.ts'
import { ApiError } from '../types.ts'

async function listening() {
  const api = createMockApi()
  const opened = vi.fn()
  const failed = vi.fn()
  const finished = vi.fn()
  await api.onDocumentOpened(opened)
  await api.onDocumentOpenFailed(failed)
  await api.onOpenBatchFinished(finished)
  return { api, opened, failed, finished }
}

describe('模拟实现按 D11 的约定工作', () => {
  it('open_with_dialog：用户取消（队列为空）时不发任何事件', async () => {
    const { api, opened, failed, finished } = await listening()
    await api.appReady()
    await api.openWithDialog()
    expect(opened).not.toHaveBeenCalled()
    expect(failed).not.toHaveBeenCalled()
    expect(finished).not.toHaveBeenCalled()
  })

  it('open_with_dialog：选择的文件按顺序逐个发出事件，最后发出批次汇总；成功和失败互不影响', async () => {
    const { api, opened, failed, finished } = await listening()
    await api.appReady()
    api.queueDialogChoice([fixture('headers-normal'), fixture('error-unsupported'), fixture('plain-text')])
    await api.openWithDialog()
    expect(opened.mock.calls.map(([e]) => [e.batchId, e.handle, e.fileName])).toEqual([
      [1, 1, 'headers-normal.eml'],
      [1, 2, 'body-plain-only.eml'],
    ])
    expect(failed).toHaveBeenCalledExactlyOnceWith(expect.objectContaining({ batchId: 1, fileName: 'not-an-email.png', code: 'unsupported_format' }))
    expect(finished).toHaveBeenCalledExactlyOnceWith({ batchId: 1, opened: 2, failed: 1 })
    expect([...api.openHandles]).toEqual([1, 2])
  })

  it('app_ready 之前收到的文件先排队，app_ready 之后才发出；没有监听时事件丢失（和 Tauri 一样）', async () => {
    const api = createMockApi()
    await api.deliverBatch([fixture('headers-normal')])
    const opened = vi.fn()
    await api.onDocumentOpened(opened)
    expect(opened).not.toHaveBeenCalled()
    await api.appReady()
    expect(opened).toHaveBeenCalledTimes(1)

    // 就绪之后，没有任何监听时发出的事件没人收到
    const lonely = createMockApi()
    await lonely.appReady()
    await lonely.deliverBatch([fixture('headers-normal')])
    expect([...lonely.openHandles]).toEqual([1])
  })

  it('时间线记录“先注册监听、再 app_ready”的顺序', async () => {
    const { api } = await listening()
    await api.appReady()
    expect(api.timeline).toEqual([
      'listen efv://document-opened',
      'listen efv://document-open-failed',
      'listen efv://open-batch-finished',
      'app_ready',
    ])
  })

  it('open_nested：返回新句柄和嵌套邮件，层数不限；记录调用参数', async () => {
    const { api, opened } = await listening()
    await api.appReady()
    await api.deliverBatch([fixture('nested-two-levels')])
    const outer = opened.mock.calls[0][0]
    const middle = await api.openNested(outer.handle, '2')
    expect(middle.handle).not.toBe(outer.handle)
    expect(middle.document.subject).toContain('Level 2')
    const inner = await api.openNested(middle.handle, middle.document.attachments.find((a) => a.isNestedEmail)!.id)
    expect(inner.document.subject).toContain('Level 3')
    expect(api.calls.filter((c) => c.command === 'open_nested')[0]).toEqual({ command: 'open_nested', args: { handle: outer.handle, attachmentId: '2' } })
  })

  it('open_nested 的错误码：文档不存在、附件不存在、不是嵌套邮件', async () => {
    const { api, opened } = await listening()
    await api.appReady()
    await api.deliverBatch([fixture('attachments-multi')])
    const handle = opened.mock.calls[0][0].handle
    await expect(api.openNested(99, '1')).rejects.toMatchObject({ code: 'document_not_found' })
    await expect(api.openNested(handle, 'nope')).rejects.toMatchObject({ code: 'attachment_not_found' })
    await expect(api.openNested(handle, '2')).rejects.toMatchObject({ code: 'not_nested_email' })
  })

  it('close_document：句柄不再有效', async () => {
    const { api, opened } = await listening()
    await api.appReady()
    await api.deliverBatch([fixture('headers-normal')])
    const handle = opened.mock.calls[0][0].handle
    await api.closeDocument(handle)
    expect(api.openHandles.has(handle)).toBe(false)
    await expect(api.openNested(handle, '1')).rejects.toBeInstanceOf(ApiError)
  })

  it('保存和全部保存：默认成功；可以替换成取消（null）或部分失败', async () => {
    const { api, opened } = await listening()
    await api.appReady()
    await api.deliverBatch([fixture('attachments-multi')])
    const handle = opened.mock.calls[0][0].handle
    await expect(api.saveAttachment(handle, '2')).resolves.toEqual({ path: '/mock/Downloads/report.pdf' })
    expect((await api.saveAllAttachments(handle))?.saved).toHaveLength(3)
    api.override('saveAttachment', async () => null)
    await expect(api.saveAttachment(handle, '2')).resolves.toBeNull()
    api.override('saveAllAttachments', async () => ({ saved: [], failed: [{ attachmentId: '2', code: 'write_failed' }] }))
    expect((await api.saveAllAttachments(handle))?.failed).toHaveLength(1)
  })

  it('open_attachment：可执行类型抛出 executable_refused，普通文件成功', async () => {
    const { api, opened } = await listening()
    await api.appReady()
    await api.deliverBatch([fixture('attachments-executables'), fixture('attachments-multi')])
    const exe = opened.mock.calls[0][0].handle
    const normal = opened.mock.calls[1][0].handle
    await expect(api.openAttachment(exe, '2')).rejects.toMatchObject({ code: 'executable_refused' })
    await expect(api.openAttachment(normal, '2')).resolves.toBeUndefined()
  })

  it('failNext 只生效一次，并且命令调用照常记录', async () => {
    const { api, opened } = await listening()
    await api.appReady()
    await api.deliverBatch([fixture('attachments-multi')])
    const handle = opened.mock.calls[0][0].handle
    api.failNext('saveAttachment', 'write_failed', 'disk full')
    await expect(api.saveAttachment(handle, '2')).rejects.toMatchObject({ code: 'write_failed', message: 'disk full' })
    await expect(api.saveAttachment(handle, '2')).resolves.toHaveProperty('path')
    expect(api.calls.filter((c) => c.command === 'save_attachment')).toHaveLength(2)
  })

  it('原生菜单的两个事件：监听之后才会收到，各自独立，取消监听后不再送达', async () => {
    const api = createMockApi()
    const closeTab = vi.fn()
    const openSettings = vi.fn()
    api.emitCloseActiveTab() // 还没有人监听：和 Tauri 一样丢失
    const unCloseTab = await api.onCloseActiveTab(closeTab)
    const unOpenSettings = await api.onOpenSettings(openSettings)
    expect(api.listenerCount()).toBe(2)
    expect(api.timeline).toEqual(['listen efv://close-active-tab', 'listen efv://open-settings'])
    api.emitCloseActiveTab()
    expect(closeTab).toHaveBeenCalledTimes(1)
    expect(openSettings).not.toHaveBeenCalled()
    api.emitOpenSettings()
    expect(openSettings).toHaveBeenCalledTimes(1)
    unCloseTab()
    unOpenSettings()
    api.emitCloseActiveTab()
    api.emitOpenSettings()
    expect(closeTab).toHaveBeenCalledTimes(1)
    expect(openSettings).toHaveBeenCalledTimes(1)
    expect(api.listenerCount()).toBe(0)
    expect(api.calls).toEqual([]) // 菜单事件不是命令
  })

  it('取消监听后事件不再送达，listenerCount 归零', async () => {
    const api = createMockApi()
    const handler = vi.fn()
    const un = await api.onDocumentOpened(handler)
    expect(api.listenerCount()).toBe(1)
    un()
    expect(api.listenerCount()).toBe(0)
  })
})

describe('isExecutableFileName（D6 的清单）', () => {
  it.each([
    'setup.exe', 'run-me.BAT', 'x.cmd', 'a.com', 'a.msi', 'setup.ps1', 'a.vbs', 'a.js', 'a.jse', 'a.wsf', 'a.scr', 'a.lnk', 'a.hta', 'a.cpl',
    'Tool.app', 'launcher.command', 'a.pkg', 'install.sh', 'a.desktop', 'x.AppImage', 'a.run',
    'a.jar', 'a.vbe', 'a.ws', 'a.wsh', 'a.psm1', 'a.psd1', 'a.ps1xml', 'a.reg', 'a.msc', 'a.scf', 'a.url', 'a.pif',
    'a.msix', 'a.msixbundle', 'a.appx', 'a.appxbundle', 'a.appinstaller', 'a.application', 'a.xll', 'a.iso', 'a.img', 'a.vhd', 'a.vhdx',
    'invoice.pdf.exe', '..\\..\\Windows\\evil.exe', '../../x.sh',
  ])('%s 是可执行文件', (name) => {
    expect(isExecutableFileName(name)).toBe(true)
  })

  it.each(['report.pdf', 'photo.jpg', 'budget.xlsx', 'notes.txt', 'archive.zip', 'noextension', '.bashrc', 'a.exe.txt', 'a.', 'forwarded.eml'])('%s 不是', (name) => {
    expect(isExecutableFileName(name)).toBe(false)
  })
})
