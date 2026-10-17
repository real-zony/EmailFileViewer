// 前端与 Rust 的接口（design.md D11）。命令名、参数名和事件名都与 D11 一致，改动前先改 D11。
// 前端只认 Rust 分配的文档句柄（整数），从不向 Rust 传入文件路径。

/** 解析进程的 `EmailDocument` 里的地址；两项至少有一项不是 null */
export interface EmailAddress {
  name: string | null
  address: string | null
}

/** 正文来自哪种格式。`None` 表示没有正文（例如 S/MIME 加密邮件） */
export type BodySourceFormat = 'None' | 'Html' | 'Rtf' | 'PlainText'

export interface EmailBody {
  sourceFormat: BodySourceFormat
  /** 已经由 sidecar 清理过的 HTML（`cid:` 图片已换成 `data:` URI）；没有正文时为 null */
  html: string | null
}

export interface EmailAttachment {
  /** 只在所属文档内有意义的不透明字符串 */
  id: string
  fileName: string | null
  /** 字节数 */
  size: number
  mimeType: string | null
  isNestedEmail: boolean
}

/** 与 core/docs/sidecar-protocol.md 第 4 节完全一致（camelCase） */
export interface EmailDocument {
  from: EmailAddress[]
  to: EmailAddress[]
  cc: EmailAddress[]
  bcc: EmailAddress[]
  replyTo: EmailAddress[]
  subject: string | null
  /** 统一换算成 UTC、带 Z 的 ISO 8601；没有可解析的日期时为 null */
  sentAtUtc: string | null
  /** 原始 Date 邮件头；MSG 通常没有 */
  dateHeaderRaw: string | null
  body: EmailBody
  attachments: EmailAttachment[]
  /** 带 S/MIME 签名；签名是否有效不验证 */
  isSigned: boolean
  isEncrypted: boolean
  /** 正文引用了远程资源 */
  hasRemoteContent: boolean
}

/** Rust 分配的文档句柄 */
export type DocumentHandle = number

// ---- 错误 ----

/** sidecar 的错误码，外加 D11 增加的三个 */
export type ErrorCode =
  | 'unsupported_format'
  | 'corrupted'
  | 'unreadable'
  | 'document_not_found'
  | 'attachment_not_found'
  | 'not_nested_email'
  | 'executable_refused'
  | 'write_failed'
  | 'invalid_params'
  | 'internal_error'
  | 'sidecar_crashed'
  | 'timeout'
  | 'dialog_failed'

/** 命令出错时 invoke 抛出 `{ code, message }`；这里统一成 Error，`code` 可能是列表之外的值，要按未知处理 */
export class ApiError extends Error {
  readonly code: string
  constructor(code: string, message = code) {
    super(message)
    this.name = 'ApiError'
    this.code = code
  }
}

// ---- 命令的返回值 ----

export interface OpenedNested {
  handle: DocumentHandle
  document: EmailDocument
}

export interface SavedAttachment {
  path: string
}

export interface SaveAllResult {
  saved: { attachmentId: string; path: string }[]
  failed: { attachmentId: string; code: string }[]
}

// ---- 事件的内容 ----

export interface DocumentOpenedEvent {
  batchId: number
  handle: DocumentHandle
  fileName: string
  document: EmailDocument
}

export interface DocumentOpenFailedEvent {
  batchId: number
  fileName: string
  code: string
  message: string
}

export interface OpenBatchFinishedEvent {
  batchId: number
  opened: number
  failed: number
}

/**
 * 窗口的拖放事件。真正的打开由 Rust 在窗口的拖放事件里处理，前端只用它显示遮罩：
 * 这里故意不带文件路径，前端的其余部分碰不到路径。
 * `enter` 时 count 为拖入的文件数（拖入文字或链接时为 0，应忽略）。
 */
export type DragEvent =
  | { type: 'enter'; count: number }
  | { type: 'over' }
  | { type: 'leave' }
  | { type: 'drop' }

export type Unlisten = () => void

/**
 * D11 的全部命令和事件。真实实现在 tauriApi.ts（invoke / listen），
 * 浏览器调试和测试用 mockApi.ts / dev/devApi.ts。
 */
export interface ViewerApi {
  // ---- 命令（invoke） ----
  /** 前端注册好事件监听后调用；在此之前 Rust 把启动参数和单实例转来的文件先排队 */
  appReady(): Promise<void>
  /** 弹出多选对话框，结果通过事件返回；用户取消时不发任何事件 */
  openWithDialog(): Promise<void>
  /** 在当前标签页内逐层进入嵌套邮件 */
  openNested(handle: DocumentHandle, attachmentId: string): Promise<OpenedNested>
  closeDocument(handle: DocumentHandle): Promise<void>
  /** Rust 弹出保存对话框；取消时返回 null */
  saveAttachment(handle: DocumentHandle, attachmentId: string): Promise<SavedAttachment | null>
  /** Rust 弹出选择文件夹的对话框；取消时返回 null */
  saveAllAttachments(handle: DocumentHandle): Promise<SaveAllResult | null>
  /** 可执行类型抛出 `executable_refused` */
  openAttachment(handle: DocumentHandle, attachmentId: string): Promise<void>

  // ---- 事件（listen）。返回取消监听的函数 ----
  onDocumentOpened(handler: (event: DocumentOpenedEvent) => void): Promise<Unlisten>
  onDocumentOpenFailed(handler: (event: DocumentOpenFailedEvent) => void): Promise<Unlisten>
  onOpenBatchFinished(handler: (event: OpenBatchFinishedEvent) => void): Promise<Unlisten>
  onDragDrop(handler: (event: DragEvent) => void): Promise<Unlisten>

  // ---- 原生菜单（macOS）发来的事件，不带内容（D11 补充）。“打开”由 Rust 直接处理，不经过前端 ----
  /** 菜单“关闭标签页”（⌘W）：关闭当前标签页 */
  onCloseActiveTab(handler: () => void): Promise<Unlisten>
  /** 菜单“设置…”（⌘,）：打开设置对话框 */
  onOpenSettings(handler: () => void): Promise<Unlisten>
}

/** 命令名和事件名，真实实现和测试共用 */
export const COMMANDS = {
  appReady: 'app_ready',
  openWithDialog: 'open_with_dialog',
  openNested: 'open_nested',
  closeDocument: 'close_document',
  saveAttachment: 'save_attachment',
  saveAllAttachments: 'save_all_attachments',
  openAttachment: 'open_attachment',
} as const

export const EVENTS = {
  documentOpened: 'efv://document-opened',
  documentOpenFailed: 'efv://document-open-failed',
  openBatchFinished: 'efv://open-batch-finished',
  closeActiveTab: 'efv://close-active-tab',
  openSettings: 'efv://open-settings',
} as const
