import { describe, expect, it } from 'vitest'
import { fixture } from '../../dev/fixtures.ts'
import type { DocumentOpenedEvent } from '../../api/types.ts'
import { activeTab, createViewerStore, initialViewerState, viewerReducer, type Level, type ViewerAction, type ViewerState } from '../store.ts'

const doc = fixture('headers-normal').document!
const opened = (batchId: number, handle: number, fileName = `f${handle}.eml`): ViewerAction => ({
  type: 'documentOpened',
  event: { batchId, handle, fileName, document: doc } satisfies DocumentOpenedEvent,
})
const failed = (batchId: number, fileName: string, code = 'corrupted'): ViewerAction => ({
  type: 'documentOpenFailed',
  event: { batchId, fileName, code, message: code },
})
const run = (...actions: ViewerAction[]): ViewerState => actions.reduce(viewerReducer, initialViewerState)
const ids = (s: ViewerState) => s.tabs.map((t) => t.id)
const level = (handle: number): Level => ({ handle, fileName: `n${handle}.eml`, document: doc, remoteAllowed: false })

describe('新打开的文件激活对应的标签页', () => {
  it('单个文件：打开后成为当前标签页', () => {
    const s = run(opened(1, 10))
    expect(ids(s)).toEqual(['m10'])
    expect(s.activeId).toBe('m10')
  })

  it('已有标签页时，再打开一个文件激活新的那个', () => {
    const s = run(opened(1, 10), opened(2, 11))
    expect(s.activeId).toBe('m11')
  })

  it('一次打开多个文件：第一封成功打开的邮件获得焦点，其余只加标签页', () => {
    const s = run(opened(1, 10), opened(1, 11), opened(1, 12))
    expect(ids(s)).toEqual(['m10', 'm11', 'm12'])
    expect(s.activeId).toBe('m10')
  })

  it('批次里第一个文件失败、后面成功：成功的邮件获得焦点（失败的靠汇总通知找到）', () => {
    const s = run(failed(1, 'bad.png'), opened(1, 11))
    expect(ids(s)).toEqual(['e1', 'm11'])
    expect(s.activeId).toBe('m11')
  })

  it('批次里全部失败：第一个失败的标签页获得焦点', () => {
    const s = run(failed(1, 'a.png'), failed(1, 'b.png'))
    expect(s.activeId).toBe('e1')
  })

  it('同一个句柄的事件重复时不会出现两个标签页', () => {
    expect(ids(run(opened(1, 10), opened(1, 10)))).toEqual(['m10'])
  })
})

describe('汇总通知', () => {
  it('既有成功又有失败时才另加汇总，列出失败的文件', () => {
    const s = run(opened(1, 10), failed(1, 'bad.png', 'unsupported_format'), opened(1, 11), { type: 'batchFinished', event: { batchId: 1, opened: 2, failed: 1 } })
    expect(s.toasts).toHaveLength(1)
    expect(s.toasts[0]).toMatchObject({ kind: 'partialOpen', opened: 2, total: 3, failures: [{ tabId: 'e1', fileName: 'bad.png', code: 'unsupported_format' }] })
    expect(s.batches).toEqual({})
  })

  it('全部成功或全部失败时不加汇总', () => {
    expect(run(opened(1, 10), { type: 'batchFinished', event: { batchId: 1, opened: 1, failed: 0 } }).toasts).toEqual([])
    expect(run(failed(1, 'a.png'), { type: 'batchFinished', event: { batchId: 1, opened: 0, failed: 1 } }).toasts).toEqual([])
  })
})

describe('关闭标签页', () => {
  it('关闭当前标签页后激活右边的，没有右边的就激活左边的，最后一个关掉后没有当前标签页', () => {
    const base = run(opened(1, 10), opened(2, 11), opened(3, 12))
    expect(viewerReducer({ ...base, activeId: 'm11' }, { type: 'closeTab', tabId: 'm11' }).activeId).toBe('m12')
    expect(viewerReducer(base, { type: 'closeTab', tabId: 'm12' }).activeId).toBe('m11')
    expect(viewerReducer(base, { type: 'closeTab', tabId: 'm10' }).activeId).toBe('m12') // 当前是 m12，不受影响
    const one = run(opened(1, 10))
    const closed = viewerReducer(one, { type: 'closeTab', tabId: 'm10' })
    expect(closed.tabs).toEqual([])
    expect(closed.activeId).toBeNull()
    expect(activeTab(closed)).toBeNull()
  })

  it('关闭的不是当前标签页时，当前标签页不变', () => {
    const base = run(opened(1, 10), opened(2, 11))
    expect(viewerReducer(base, { type: 'closeTab', tabId: 'm10' }).activeId).toBe('m11')
  })
})

describe('嵌套邮件的面包屑', () => {
  const base = run(opened(1, 10))

  it('进入嵌套邮件：追加一级；只有父级仍是当前显示的那一级时才生效', () => {
    const entered = viewerReducer(base, { type: 'enterNested', tabId: 'm10', parentHandle: 10, level: level(20) })
    expect(entered.tabs[0]).toMatchObject({ trail: [{ handle: 10 }, { handle: 20 }] })
    // 慢了一步的第二个请求：父级已经不是 10 了
    const late = viewerReducer(entered, { type: 'enterNested', tabId: 'm10', parentHandle: 10, level: level(21) })
    expect(late).toBe(entered)
    // 标签页已经关闭
    expect(viewerReducer(viewerReducer(base, { type: 'closeTab', tabId: 'm10' }), { type: 'enterNested', tabId: 'm10', parentHandle: 10, level: level(22) }).tabs).toEqual([])
  })

  it('回到第 index 级：更深的几级被丢弃；回到当前级或越界时什么都不做', () => {
    const deep = [20, 30].reduce<ViewerState>((s, h, i) => viewerReducer(s, { type: 'enterNested', tabId: 'm10', parentHandle: i === 0 ? 10 : 20, level: level(h) }), base)
    expect(deep.tabs[0]).toMatchObject({ trail: [{ handle: 10 }, { handle: 20 }, { handle: 30 }] })
    const mid = viewerReducer(deep, { type: 'navigateTrail', tabId: 'm10', index: 1 })
    expect(mid.tabs[0]).toMatchObject({ trail: [{ handle: 10 }, { handle: 20 }] })
    expect(viewerReducer(mid, { type: 'navigateTrail', tabId: 'm10', index: 1 })).toBe(mid)
    expect(viewerReducer(mid, { type: 'navigateTrail', tabId: 'm10', index: 5 })).toBe(mid)
    expect(viewerReducer(mid, { type: 'navigateTrail', tabId: 'm10', index: 0 }).tabs[0]).toMatchObject({ trail: [{ handle: 10 }] })
  })
})

describe('远程内容只对一个标签页的一封邮件生效', () => {
  it('allowRemote 只改指定的那一级，其他标签页和之后新开的文档仍然阻止', () => {
    let s = run(opened(1, 10), opened(2, 11))
    s = viewerReducer(s, { type: 'allowRemote', tabId: 'm10', handle: 10 })
    const flags = (state: ViewerState) => state.tabs.map((t) => (t.kind === 'mail' ? t.trail.map((l) => l.remoteAllowed) : []))
    expect(flags(s)).toEqual([[true], [false]])
    s = viewerReducer(s, opened(3, 12))
    expect(flags(s)).toEqual([[true], [false], [false]])
  })
})

describe('createViewerStore', () => {
  it('dispatch 之后立刻能读到最新状态；状态没变时不通知订阅者', () => {
    const store = createViewerStore()
    let notified = 0
    const off = store.subscribe(() => notified++)
    store.dispatch(opened(1, 10))
    expect(store.getState().activeId).toBe('m10')
    expect(notified).toBe(1)
    store.dispatch({ type: 'selectTab', tabId: 'nope' })
    expect(notified).toBe(1)
    off()
    store.dispatch(opened(2, 11))
    expect(notified).toBe(1)
  })
})
