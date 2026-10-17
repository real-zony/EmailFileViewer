import { invoke } from '@tauri-apps/api/core'
import { listen, type EventCallback } from '@tauri-apps/api/event'
import { getCurrentWebview } from '@tauri-apps/api/webview'
import {
  ApiError,
  COMMANDS,
  EVENTS,
  type DocumentOpenedEvent,
  type DocumentOpenFailedEvent,
  type OpenBatchFinishedEvent,
  type OpenedNested,
  type SavedAttachment,
  type SaveAllResult,
  type ViewerApi,
} from './types.ts'

// invoke 出错时抛出的是 Rust 序列化的 `{ code, message }`（也可能是字符串或别的东西），统一成 ApiError
export function toApiError(error: unknown): ApiError {
  if (error instanceof ApiError) return error
  if (typeof error === 'object' && error !== null && 'code' in error && typeof error.code === 'string') {
    const message = 'message' in error && typeof error.message === 'string' ? error.message : error.code
    return new ApiError(error.code, message)
  }
  return new ApiError('unknown', typeof error === 'string' ? error : error instanceof Error ? error.message : 'Unknown error')
}

async function call<T>(command: string, args?: Record<string, unknown>): Promise<T> {
  try {
    return await invoke<T>(command, args)
  } catch (error) {
    throw toApiError(error)
  }
}

function on<T>(event: string, handler: (payload: T) => void) {
  const callback: EventCallback<T> = (e) => handler(e.payload)
  return listen<T>(event, callback)
}

// 真实实现：调用 Rust 命令、监听 Rust 事件。参数名与 D11 一致（Tauri 把 Rust 的 snake_case 参数转成 camelCase）。
export function createTauriApi(): ViewerApi {
  return {
    appReady: () => call<void>(COMMANDS.appReady),
    openWithDialog: () => call<void>(COMMANDS.openWithDialog),
    openNested: (handle, attachmentId) => call<OpenedNested>(COMMANDS.openNested, { handle, attachmentId }),
    closeDocument: (handle) => call<void>(COMMANDS.closeDocument, { handle }),
    saveAttachment: (handle, attachmentId) => call<SavedAttachment | null>(COMMANDS.saveAttachment, { handle, attachmentId }),
    saveAllAttachments: (handle) => call<SaveAllResult | null>(COMMANDS.saveAllAttachments, { handle }),
    openAttachment: (handle, attachmentId) => call<void>(COMMANDS.openAttachment, { handle, attachmentId }),

    onDocumentOpened: (handler) => on<DocumentOpenedEvent>(EVENTS.documentOpened, handler),
    onDocumentOpenFailed: (handler) => on<DocumentOpenFailedEvent>(EVENTS.documentOpenFailed, handler),
    onOpenBatchFinished: (handler) => on<OpenBatchFinishedEvent>(EVENTS.openBatchFinished, handler),
    // 菜单事件没有内容
    onCloseActiveTab: (handler) => on<null>(EVENTS.closeActiveTab, () => handler()),
    onOpenSettings: (handler) => on<null>(EVENTS.openSettings, () => handler()),
    // 只取文件数量，路径不往外传；真正的打开由 Rust 在窗口的拖放事件里处理
    onDragDrop: (handler) =>
      getCurrentWebview().onDragDropEvent((event) => {
        const payload = event.payload
        if (payload.type === 'enter') handler({ type: 'enter', count: payload.paths.length })
        else handler({ type: payload.type })
      }),
  }
}
