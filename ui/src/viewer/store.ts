// 打开的文件、标签页、嵌套邮件的面包屑、通知和对话框的状态。不依赖 React 和 i18n：
// 状态里只放数据（错误码、文件名），文字在渲染时按当前界面语言取，所以切换语言后通知也跟着变。
import type { DocumentHandle, DocumentOpenedEvent, DocumentOpenFailedEvent, EmailDocument, OpenBatchFinishedEvent } from '../api/types.ts'

/** 面包屑里的一级：一封已经打开的邮件（Rust 分配的句柄） */
export interface Level {
  handle: DocumentHandle
  fileName: string
  document: EmailDocument
  /** 用户已对这一封选择加载远程内容。只在内存里，随这一级一起关闭 */
  remoteAllowed: boolean
}

export interface MailTab {
  kind: 'mail'
  id: string
  /** 从最外层文件到当前正在看的嵌套邮件，至少一级 */
  trail: Level[]
}

/** 打开失败的文件也占一个标签页 */
export interface ErrorTab {
  kind: 'error'
  id: string
  fileName: string
  code: string
}

export type Tab = MailTab | ErrorTab

export interface OpenFailure {
  tabId: string
  fileName: string
  code: string
}

export type OperationKind = 'dialogFailed' | 'openNestedFailed' | 'openFailed' | 'saveFailed' | 'saveAllFailed'

export type ToastData =
  /** 一次打开多个文件，部分失败 */
  | { kind: 'partialOpen'; opened: number; total: number; failures: OpenFailure[] }
  /** 保存、打开附件等操作失败 */
  | { kind: 'operationFailed'; operation: OperationKind; name?: string; code: string }
  /** 全部保存完成的汇总 */
  | { kind: 'savedAll'; saved: number; total: number; failedNames: string[]; code?: string }

export type Toast = ToastData & { id: number }

/** 被拒绝打开的可执行附件，显示 ExecutableBlockedDialog */
export interface BlockedAttachment {
  handle: DocumentHandle
  attachmentId: string
  fileName: string
}

interface BatchProgress {
  /** 这个批次已经激活过什么：还没有 / 一个失败的标签页 / 一封成功打开的邮件 */
  activated: 'none' | 'error' | 'mail'
  failures: OpenFailure[]
}

export interface ViewerState {
  tabs: Tab[]
  activeId: string | null
  batches: Record<number, BatchProgress>
  toasts: Toast[]
  blocked: BlockedAttachment | null
  /** 文件正悬停在窗口上方时的文件数量 */
  dragging: { count: number } | null
  nextErrorId: number
  nextToastId: number
}

export const initialViewerState: ViewerState = {
  tabs: [],
  activeId: null,
  batches: {},
  toasts: [],
  blocked: null,
  dragging: null,
  nextErrorId: 1,
  nextToastId: 1,
}

export type ViewerAction =
  | { type: 'documentOpened'; event: DocumentOpenedEvent }
  | { type: 'documentOpenFailed'; event: DocumentOpenFailedEvent }
  | { type: 'batchFinished'; event: OpenBatchFinishedEvent }
  | { type: 'selectTab'; tabId: string }
  | { type: 'closeTab'; tabId: string }
  /** 进入嵌套邮件。只在 parentHandle 仍是这个标签页当前显示的那一级时才生效 */
  | { type: 'enterNested'; tabId: string; parentHandle: DocumentHandle; level: Level }
  /** 回到面包屑的第 index 级（从 0 开始），更深的几级被丢弃 */
  | { type: 'navigateTrail'; tabId: string; index: number }
  | { type: 'allowRemote'; tabId: string; handle: DocumentHandle }
  | { type: 'pushToast'; toast: ToastData }
  | { type: 'dismissToast'; id: number }
  | { type: 'block'; blocked: BlockedAttachment }
  | { type: 'unblock' }
  | { type: 'drag'; dragging: { count: number } | null }

export function mailTabId(handle: DocumentHandle): string {
  return `m${handle}`
}

/** 标签页（以及它的面包屑链）里全部的文档句柄，关闭标签页时要逐个 close_document */
export function tabHandles(tab: Tab): DocumentHandle[] {
  return tab.kind === 'mail' ? tab.trail.map((level) => level.handle) : []
}

export function activeTab(state: ViewerState): Tab | null {
  return state.tabs.find((tab) => tab.id === state.activeId) ?? null
}

function progressOf(state: ViewerState, batchId: number): BatchProgress {
  return state.batches[batchId] ?? { activated: 'none', failures: [] }
}

export function viewerReducer(state: ViewerState, action: ViewerAction): ViewerState {
  switch (action.type) {
    case 'documentOpened': {
      const { event } = action
      const id = mailTabId(event.handle)
      if (state.tabs.some((tab) => tab.id === id)) return state
      const tab: MailTab = {
        kind: 'mail',
        id,
        trail: [{ handle: event.handle, fileName: event.fileName, document: event.document, remoteAllowed: false }],
      }
      const progress = progressOf(state, event.batchId)
      // 一个批次里第一封成功打开的邮件获得焦点；没有成功的才轮到第一个失败的标签页。单个文件就是它自己。
      const activate = progress.activated !== 'mail'
      return {
        ...state,
        tabs: [...state.tabs, tab],
        activeId: activate ? id : state.activeId,
        batches: { ...state.batches, [event.batchId]: { ...progress, activated: 'mail' } },
      }
    }

    case 'documentOpenFailed': {
      const { event } = action
      const id = `e${state.nextErrorId}`
      const tab: ErrorTab = { kind: 'error', id, fileName: event.fileName, code: event.code }
      const progress = progressOf(state, event.batchId)
      const activate = progress.activated === 'none'
      return {
        ...state,
        tabs: [...state.tabs, tab],
        activeId: activate ? id : state.activeId,
        nextErrorId: state.nextErrorId + 1,
        batches: {
          ...state.batches,
          [event.batchId]: {
            activated: progress.activated === 'none' ? 'error' : progress.activated,
            failures: [...progress.failures, { tabId: id, fileName: event.fileName, code: event.code }],
          },
        },
      }
    }

    case 'batchFinished': {
      const { event } = action
      const { [event.batchId]: progress, ...batches } = state.batches
      const next: ViewerState = { ...state, batches }
      // 全部成功不需要通知；全部失败时每个失败文件自己的标签页就是通知；只有既有成功又有失败才另加汇总
      if (event.opened > 0 && event.failed > 0) {
        return addToast(next, {
          kind: 'partialOpen',
          opened: event.opened,
          total: event.opened + event.failed,
          failures: progress?.failures ?? [],
        })
      }
      return next
    }

    case 'selectTab':
      return state.tabs.some((tab) => tab.id === action.tabId) ? { ...state, activeId: action.tabId } : state

    case 'closeTab': {
      const index = state.tabs.findIndex((tab) => tab.id === action.tabId)
      if (index === -1) return state
      const tabs = state.tabs.filter((tab) => tab.id !== action.tabId)
      let activeId = state.activeId
      if (activeId === action.tabId) activeId = (tabs[index] ?? tabs[index - 1] ?? null)?.id ?? null
      // 汇总通知里指向已关闭标签页的失败项没有意义了
      const toasts = state.toasts.map((toast) =>
        toast.kind === 'partialOpen' ? { ...toast, failures: toast.failures.filter((f) => f.tabId !== action.tabId) } : toast,
      )
      return { ...state, tabs, activeId, toasts }
    }

    case 'enterNested':
      return updateMailTab(state, action.tabId, (tab) => {
        const top = tab.trail[tab.trail.length - 1]
        if (top.handle !== action.parentHandle) return tab
        return { ...tab, trail: [...tab.trail, action.level] }
      })

    case 'navigateTrail':
      return updateMailTab(state, action.tabId, (tab) => {
        if (action.index < 0 || action.index >= tab.trail.length - 1) return tab
        return { ...tab, trail: tab.trail.slice(0, action.index + 1) }
      })

    case 'allowRemote':
      return updateMailTab(state, action.tabId, (tab) => ({
        ...tab,
        trail: tab.trail.map((level) => (level.handle === action.handle ? { ...level, remoteAllowed: true } : level)),
      }))

    case 'pushToast':
      return addToast(state, action.toast)

    case 'dismissToast':
      return { ...state, toasts: state.toasts.filter((toast) => toast.id !== action.id) }

    case 'block':
      return { ...state, blocked: action.blocked }

    case 'unblock':
      return { ...state, blocked: null }

    case 'drag':
      return { ...state, dragging: action.dragging }
  }
}

function addToast(state: ViewerState, toast: ToastData): ViewerState {
  return { ...state, toasts: [...state.toasts, { ...toast, id: state.nextToastId }], nextToastId: state.nextToastId + 1 }
}

function updateMailTab(state: ViewerState, tabId: string, update: (tab: MailTab) => MailTab): ViewerState {
  let changed = false
  const tabs = state.tabs.map((tab) => {
    if (tab.id !== tabId || tab.kind !== 'mail') return tab
    const next = update(tab)
    if (next !== tab) changed = true
    return next
  })
  return changed ? { ...state, tabs } : state
}

/** 同步的外部 store：控制器在异步回调里需要读到最新状态，所以不放在 React 的 reducer 里 */
export interface ViewerStore {
  getState(): ViewerState
  dispatch(action: ViewerAction): void
  subscribe(listener: () => void): () => void
}

export function createViewerStore(): ViewerStore {
  let state = initialViewerState
  const listeners = new Set<() => void>()
  return {
    getState: () => state,
    dispatch(action) {
      const next = viewerReducer(state, action)
      if (next === state) return
      state = next
      for (const listener of [...listeners]) listener()
    },
    subscribe(listener) {
      listeners.add(listener)
      return () => listeners.delete(listener)
    },
  }
}
