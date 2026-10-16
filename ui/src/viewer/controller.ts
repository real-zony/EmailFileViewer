// 把 D11 的命令和事件接到状态上。不依赖 React，状态都在 store 里，组件只负责渲染和调用这里的方法。
import { ApiError, type DocumentHandle, type Unlisten, type ViewerApi } from '../api/types.ts'
import {
  createViewerStore,
  tabHandles,
  type Level,
  type MailTab,
  type OperationKind,
  type ViewerState,
  type ViewerStore,
} from './store.ts'

export interface ViewerController {
  readonly store: ViewerStore
  /**
   * 先注册好全部事件监听，再调用 app_ready：在此之前 Rust 把启动参数和单实例转来的文件排队，
   * 监听还没注册就调用会丢事件。返回停止函数（取消监听）。
   */
  start(): () => void
  /** 弹出打开文件的对话框（工具栏按钮、空状态按钮、⌘O / Ctrl+O 都走这里） */
  openDialog(): Promise<void>
  selectTab(tabId: string): void
  /** 关闭标签页，并对它的面包屑链上的所有句柄调用 close_document */
  closeTab(tabId: string): void
  /** 在当前标签页内进入嵌套邮件 */
  openNested(tabId: string, parentHandle: DocumentHandle, attachmentId: string): Promise<void>
  /** 回到面包屑的第 index 级，更深几级的文档随之关闭 */
  navigate(tabId: string, index: number): void
  /** 只对这个标签页里的这一封邮件放宽 CSP */
  allowRemote(tabId: string, handle: DocumentHandle): void
  openAttachment(handle: DocumentHandle, attachmentId: string): Promise<void>
  saveAttachment(handle: DocumentHandle, attachmentId: string): Promise<void>
  saveAllAttachments(handle: DocumentHandle): Promise<void>
  /** 用户试图打开可执行附件：不调用 Rust，直接弹出提示 */
  showBlocked(handle: DocumentHandle, attachmentId: string): void
  /** 提示对话框里的“保存”：先关掉提示，再走保存流程 */
  saveBlocked(): Promise<void>
  dismissBlocked(): void
  dismissToast(id: number): void
}

function errorCode(error: unknown): string {
  return error instanceof ApiError ? error.code : 'unknown'
}

function findLevel(state: ViewerState, handle: DocumentHandle): Level | undefined {
  for (const tab of state.tabs) {
    if (tab.kind !== 'mail') continue
    const level = tab.trail.find((l) => l.handle === handle)
    if (level) return level
  }
  return undefined
}

/** 附件的文件名；没有文件名时返回空字符串，由界面显示“未命名的附件” */
function attachmentFileName(state: ViewerState, handle: DocumentHandle, attachmentId: string): string {
  return findLevel(state, handle)?.document.attachments.find((a) => a.id === attachmentId)?.fileName ?? ''
}

export function createViewerController(api: ViewerApi): ViewerController {
  const store = createViewerStore()
  const { dispatch } = store

  // 关闭文档是收尾工作，失败了也不打扰用户
  function closeHandles(handles: readonly DocumentHandle[]) {
    for (const handle of handles) {
      api.closeDocument(handle).catch((error: unknown) => console.warn('close_document 失败', handle, error))
    }
  }

  function failed(operation: OperationKind, error: unknown, name?: string) {
    dispatch({ type: 'pushToast', toast: { kind: 'operationFailed', operation, name, code: errorCode(error) } })
  }

  const controller: ViewerController = {
    store,

    start() {
      let stopped = false
      const unlisteners: Unlisten[] = []
      // 注册是异步的：如果在等待期间已经停止，注册完成后立刻取消
      const track = async (registration: Promise<Unlisten>) => {
        const unlisten = await registration
        if (stopped) unlisten()
        else unlisteners.push(unlisten)
      }
      void (async () => {
        try {
          await Promise.all([
            track(api.onDocumentOpened((event) => dispatch({ type: 'documentOpened', event }))),
            track(api.onDocumentOpenFailed((event) => dispatch({ type: 'documentOpenFailed', event }))),
            track(api.onOpenBatchFinished((event) => dispatch({ type: 'batchFinished', event }))),
            track(
              api.onDragDrop((event) => {
                // 拖入文字或链接时没有文件，忽略；drop 时 Rust 自己打开文件，这里只收起遮罩
                if (event.type === 'enter') dispatch({ type: 'drag', dragging: event.count > 0 ? { count: event.count } : null })
                else if (event.type !== 'over') dispatch({ type: 'drag', dragging: null })
              }),
            ),
          ])
          if (stopped) return
          await api.appReady()
        } catch (error) {
          console.error('启动时注册事件监听或调用 app_ready 失败', error)
        }
      })()
      return () => {
        stopped = true
        for (const unlisten of unlisteners.splice(0)) unlisten()
      }
    },

    async openDialog() {
      try {
        await api.openWithDialog()
      } catch (error) {
        failed('dialogFailed', error)
      }
    },

    selectTab: (tabId) => dispatch({ type: 'selectTab', tabId }),

    closeTab(tabId) {
      const tab = store.getState().tabs.find((t) => t.id === tabId)
      if (!tab) return
      dispatch({ type: 'closeTab', tabId })
      closeHandles(tabHandles(tab))
    },

    async openNested(tabId, parentHandle, attachmentId) {
      const fileName = attachmentFileName(store.getState(), parentHandle, attachmentId)
      try {
        const opened = await api.openNested(parentHandle, attachmentId)
        const tab = store.getState().tabs.find((t): t is MailTab => t.id === tabId && t.kind === 'mail')
        // 等待期间标签页被关了，或者已经进入了别的层级：这个文档没有人要了
        if (!tab || tab.trail[tab.trail.length - 1].handle !== parentHandle) {
          closeHandles([opened.handle])
          return
        }
        dispatch({
          type: 'enterNested',
          tabId,
          parentHandle,
          level: { handle: opened.handle, fileName, document: opened.document, remoteAllowed: false },
        })
      } catch (error) {
        failed('openNestedFailed', error, fileName)
      }
    },

    navigate(tabId, index) {
      const tab = store.getState().tabs.find((t): t is MailTab => t.id === tabId && t.kind === 'mail')
      if (!tab || index < 0 || index >= tab.trail.length - 1) return
      const dropped = tab.trail.slice(index + 1).map((level) => level.handle)
      dispatch({ type: 'navigateTrail', tabId, index })
      closeHandles(dropped)
    },

    allowRemote: (tabId, handle) => dispatch({ type: 'allowRemote', tabId, handle }),

    async openAttachment(handle, attachmentId) {
      const fileName = attachmentFileName(store.getState(), handle, attachmentId)
      try {
        await api.openAttachment(handle, attachmentId)
      } catch (error) {
        // 界面按 D6 的清单已经把可执行附件标成“仅可保存”；Rust 的判断是最终的，拒绝时同样弹出提示
        if (errorCode(error) === 'executable_refused') dispatch({ type: 'block', blocked: { handle, attachmentId, fileName } })
        else failed('openFailed', error, fileName)
      }
    },

    async saveAttachment(handle, attachmentId) {
      const fileName = attachmentFileName(store.getState(), handle, attachmentId)
      try {
        await api.saveAttachment(handle, attachmentId) // 返回 null 表示用户取消，什么都不显示
      } catch (error) {
        failed('saveFailed', error, fileName)
      }
    },

    async saveAllAttachments(handle) {
      try {
        const result = await api.saveAllAttachments(handle)
        if (!result) return
        const state = store.getState()
        dispatch({
          type: 'pushToast',
          toast: {
            kind: 'savedAll',
            saved: result.saved.length,
            total: result.saved.length + result.failed.length,
            failedNames: result.failed.map((f) => attachmentFileName(state, handle, f.attachmentId)),
            code: result.failed[0]?.code,
          },
        })
      } catch (error) {
        failed('saveAllFailed', error)
      }
    },

    showBlocked(handle, attachmentId) {
      dispatch({ type: 'block', blocked: { handle, attachmentId, fileName: attachmentFileName(store.getState(), handle, attachmentId) } })
    },

    async saveBlocked() {
      const blocked = store.getState().blocked
      if (!blocked) return
      dispatch({ type: 'unblock' })
      await controller.saveAttachment(blocked.handle, blocked.attachmentId)
    },

    dismissBlocked: () => dispatch({ type: 'unblock' }),
    dismissToast: (id) => dispatch({ type: 'dismissToast', id }),
  }
  return controller
}
