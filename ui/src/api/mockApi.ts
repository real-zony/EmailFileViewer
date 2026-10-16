// 模拟实现：浏览器里 `npm run dev` 没有 Tauri 时，以及测试里，用它代替 Rust。
// 行为照 D11 写：事件在前端注册监听之后才会收到；打开文件的来源（对话框、拖放、启动参数）都由“Rust”这一侧发起。
import { isExecutableFileName } from './executable.ts'
import {
  ApiError,
  COMMANDS,
  EVENTS,
  type DocumentHandle,
  type DocumentOpenedEvent,
  type DocumentOpenFailedEvent,
  type DragEvent,
  type EmailDocument,
  type OpenBatchFinishedEvent,
  type OpenedNested,
  type SavedAttachment,
  type SaveAllResult,
  type Unlisten,
  type ViewerApi,
} from './types.ts'

/**
 * 模拟里的一个“文件”：成功时有 document（嵌套邮件放在 nested 里，键是附件 id），失败时有 error。
 * 这也是 dev/fixtures/*.json 的格式。
 */
export interface MockFile {
  fileName: string
  document?: EmailDocument
  error?: { code: string; message?: string }
  nested?: Record<string, MockFile>
}

/** 命令调用记录。`command` 和 `args` 与真实的 invoke 完全一致，测试可以直接断言 */
export interface MockCall {
  command: string
  args?: Record<string, unknown>
}

type CommandName = keyof Pick<
  ViewerApi,
  'appReady' | 'openWithDialog' | 'openNested' | 'closeDocument' | 'saveAttachment' | 'saveAllAttachments' | 'openAttachment'
>
type CommandImpl = { [K in CommandName]: ViewerApi[K] }

export interface MockApi extends ViewerApi {
  /** 按时间顺序记录的命令调用（不含事件监听） */
  readonly calls: readonly MockCall[]
  /** 命令调用和事件监听的合并时间线，用来检查“先注册监听，再 app_ready” */
  readonly timeline: readonly string[]
  /** 目前还没有被 close_document 关闭的文档句柄 */
  readonly openHandles: ReadonlySet<DocumentHandle>
  /** 前端是否已经调用过 app_ready */
  readonly ready: boolean
  /**
   * 排一批文件，等用户下次点“打开”（open_with_dialog）时作为选择结果返回。
   * 队列为空时 open_with_dialog 表示用户取消了对话框，不发任何事件。
   */
  queueDialogChoice(files: readonly MockFile[]): void
  /** 队列为空时由它决定对话框的选择结果；返回 undefined 表示用户取消。不设置时队列为空就是取消 */
  setDialogProvider(provider: () => readonly MockFile[] | undefined): void
  /**
   * 模拟 Rust 从别处收到文件（启动参数、单实例转发、拖放）：发出 document-opened / document-open-failed，
   * 最后发出 open-batch-finished。app_ready 之前先排队，和 Rust 一致。返回 batchId。
   */
  deliverBatch(files: readonly MockFile[]): Promise<number>
  /** 让某个命令的下一次调用以这个错误码失败（只生效一次） */
  failNext(command: CommandName, code: string, message?: string): void
  /** 替换某个命令的实现（比如让保存对话框返回 null 表示取消） */
  override<K extends CommandName>(command: K, impl: CommandImpl[K]): void
  /** 模拟窗口上的拖放事件（Rust 在 drop 时自己打开文件，这里的 drop 只是通知前端） */
  emitDrag(event: DragEvent): void
  /** 模拟原生菜单的“关闭标签页”（⌘W），Rust 向前端发 efv://close-active-tab */
  emitCloseActiveTab(): void
  /** 模拟原生菜单的“设置…”（⌘,），Rust 向前端发 efv://open-settings */
  emitOpenSettings(): void
  /** 事件监听数量，用来检查卸载后没有泄漏 */
  listenerCount(): number
}

type Handlers = {
  opened: Set<(e: DocumentOpenedEvent) => void>
  failed: Set<(e: DocumentOpenFailedEvent) => void>
  finished: Set<(e: OpenBatchFinishedEvent) => void>
  drag: Set<(e: DragEvent) => void>
  closeActiveTab: Set<() => void>
  openSettings: Set<() => void>
}

export function createMockApi(): MockApi {
  const calls: MockCall[] = []
  const timeline: string[] = []
  const handlers: Handlers = {
    opened: new Set(),
    failed: new Set(),
    finished: new Set(),
    drag: new Set(),
    closeActiveTab: new Set(),
    openSettings: new Set(),
  }
  const registry = new Map<DocumentHandle, MockFile>()
  const dialogChoices: (readonly MockFile[])[] = []
  let dialogProvider: (() => readonly MockFile[] | undefined) | undefined
  const pendingBatches: { batchId: number; files: readonly MockFile[] }[] = []
  const failures = new Map<CommandName, ApiError>()
  const overrides: Partial<CommandImpl> = {}
  let nextHandle = 1
  let nextBatch = 1
  let ready = false

  const record = (command: string, args?: Record<string, unknown>) => {
    calls.push(args ? { command, args } : { command })
    timeline.push(command)
  }

  // 每个命令先记录，再看有没有排好的失败，没有就走（可能被替换的）实现
  async function run<T>(name: CommandName, command: string, args: Record<string, unknown> | undefined, impl: () => Promise<T>): Promise<T> {
    record(command, args)
    await Promise.resolve() // 真实的 invoke 总是异步的
    const failure = failures.get(name)
    if (failure) {
      failures.delete(name)
      throw failure
    }
    return impl()
  }

  function attachmentName(handle: DocumentHandle, attachmentId: string): string {
    const attachment = registry.get(handle)?.document?.attachments.find((a) => a.id === attachmentId)
    return attachment?.fileName ?? 'attachment'
  }

  function allocate(file: MockFile): DocumentHandle {
    const handle = nextHandle++
    registry.set(handle, file)
    return handle
  }

  async function deliver(files: readonly MockFile[], batchId = nextBatch++): Promise<number> {
    let opened = 0
    let failed = 0
    for (const file of files) {
      if (file.document && !file.error) {
        opened++
        const handle = allocate(file)
        for (const h of [...handlers.opened]) h({ batchId, handle, fileName: file.fileName, document: file.document })
      } else {
        failed++
        const code = file.error?.code ?? 'corrupted'
        for (const h of [...handlers.failed]) h({ batchId, fileName: file.fileName, code, message: file.error?.message ?? code })
      }
    }
    for (const h of [...handlers.finished]) h({ batchId, opened, failed })
    return batchId
  }

  function listen<T>(set: Set<(e: T) => void>, event: string, handler: (e: T) => void): Promise<Unlisten> {
    // 真实的 listen 是异步的：返回的 Promise 完成之后监听才算注册好
    return Promise.resolve().then(() => {
      set.add(handler)
      timeline.push(`listen ${event}`)
      return () => {
        set.delete(handler)
      }
    })
  }

  const api: MockApi = {
    calls,
    timeline,
    get openHandles() {
      return new Set(registry.keys())
    },
    get ready() {
      return ready
    },

    appReady: () =>
      run('appReady', COMMANDS.appReady, undefined, async () => {
        const impl = overrides.appReady
        if (impl) return impl()
        ready = true
        // Rust 把这之前收到的文件先排队，现在一起发出
        for (const batch of pendingBatches.splice(0)) await deliver(batch.files, batch.batchId)
      }),

    openWithDialog: () =>
      run('openWithDialog', COMMANDS.openWithDialog, undefined, async () => {
        const impl = overrides.openWithDialog
        if (impl) return impl()
        const choice = dialogChoices.shift() ?? dialogProvider?.()
        if (!choice) return // 用户取消：不发任何事件
        await deliver(choice)
      }),

    openNested: (handle, attachmentId) =>
      run('openNested', COMMANDS.openNested, { handle, attachmentId }, async (): Promise<OpenedNested> => {
        const impl = overrides.openNested
        if (impl) return impl(handle, attachmentId)
        const parent = registry.get(handle)
        if (!parent) throw new ApiError('document_not_found')
        const attachment = parent.document?.attachments.find((a) => a.id === attachmentId)
        if (!attachment) throw new ApiError('attachment_not_found')
        if (!attachment.isNestedEmail) throw new ApiError('not_nested_email')
        const child = parent.nested?.[attachmentId]
        if (!child) throw new ApiError('unsupported_format')
        if (child.error || !child.document) throw new ApiError(child.error?.code ?? 'corrupted', child.error?.message)
        return { handle: allocate(child), document: child.document }
      }),

    closeDocument: (handle) =>
      run('closeDocument', COMMANDS.closeDocument, { handle }, async () => {
        const impl = overrides.closeDocument
        if (impl) return impl(handle)
        registry.delete(handle)
      }),

    saveAttachment: (handle, attachmentId) =>
      run('saveAttachment', COMMANDS.saveAttachment, { handle, attachmentId }, async (): Promise<SavedAttachment | null> => {
        const impl = overrides.saveAttachment
        if (impl) return impl(handle, attachmentId)
        return { path: `/mock/Downloads/${attachmentName(handle, attachmentId)}` }
      }),

    saveAllAttachments: (handle) =>
      run('saveAllAttachments', COMMANDS.saveAllAttachments, { handle }, async (): Promise<SaveAllResult | null> => {
        const impl = overrides.saveAllAttachments
        if (impl) return impl(handle)
        const attachments = registry.get(handle)?.document?.attachments ?? []
        return { saved: attachments.map((a) => ({ attachmentId: a.id, path: `/mock/Downloads/${a.fileName ?? 'attachment'}` })), failed: [] }
      }),

    openAttachment: (handle, attachmentId) =>
      run('openAttachment', COMMANDS.openAttachment, { handle, attachmentId }, async () => {
        const impl = overrides.openAttachment
        if (impl) return impl(handle, attachmentId)
        // 和 Rust 一样：可执行类型直接拒绝
        if (isExecutableFileName(attachmentName(handle, attachmentId))) throw new ApiError('executable_refused')
      }),

    onDocumentOpened: (handler) => listen(handlers.opened, EVENTS.documentOpened, handler),
    onDocumentOpenFailed: (handler) => listen(handlers.failed, EVENTS.documentOpenFailed, handler),
    onOpenBatchFinished: (handler) => listen(handlers.finished, EVENTS.openBatchFinished, handler),
    onDragDrop: (handler) => listen(handlers.drag, 'drag-drop', handler),
    onCloseActiveTab: (handler) => listen(handlers.closeActiveTab, EVENTS.closeActiveTab, handler),
    onOpenSettings: (handler) => listen(handlers.openSettings, EVENTS.openSettings, handler),

    queueDialogChoice: (files) => {
      dialogChoices.push(files)
    },
    setDialogProvider: (provider) => {
      dialogProvider = provider
    },
    deliverBatch: async (files) => {
      const batchId = nextBatch++
      if (!ready) {
        pendingBatches.push({ batchId, files })
        return batchId
      }
      return deliver(files, batchId)
    },
    failNext: (command, code, message) => {
      failures.set(command, new ApiError(code, message))
    },
    override: (command, impl) => {
      ;(overrides as Record<string, unknown>)[command] = impl
    },
    emitDrag: (event) => {
      for (const h of [...handlers.drag]) h(event)
    },
    emitCloseActiveTab: () => {
      for (const h of [...handlers.closeActiveTab]) h()
    },
    emitOpenSettings: () => {
      for (const h of [...handlers.openSettings]) h()
    },
    listenerCount: () =>
      handlers.opened.size + handlers.failed.size + handlers.finished.size + handlers.drag.size + handlers.closeActiveTab.size + handlers.openSettings.size,
  }
  return api
}
