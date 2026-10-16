import { useRef, type KeyboardEvent } from 'react'
import { IconButton } from './Button.tsx'
import { Icon } from './Icon.tsx'
import { cx } from './cx.ts'

export interface TabItem {
  id: string
  /** 标签上的文字：文件名（不含扩展名） */
  label: string
  /** 悬停提示：完整文件名或路径 */
  title?: string
  status?: 'ok' | 'error'
}

export interface TabBarProps {
  /** tablist 的可访问名称，例如 "Open files" */
  label: string
  tabs: readonly TabItem[]
  activeId: string
  onSelect: (id: string) => void
  onClose: (id: string) => void
  /** 关闭按钮的可访问名称，例如 (label) => `Close ${label}` */
  closeLabel: (tab: TabItem) => string
  addLabel: string
  onAdd: () => void
  /** 标签页面板的 id，用于 aria-controls */
  panelId?: string
}

// 标签栏：少于 2 个标签页时不渲染（D7）。
// 键盘：← → 切换并激活，Home / End 到首尾；关闭按钮不在 Tab 顺序中（用 Ctrl/⌘+W 关闭）。
export function TabBar({ label, tabs, activeId, onSelect, onClose, closeLabel, addLabel, onAdd, panelId }: TabBarProps) {
  const refs = useRef(new Map<string, HTMLButtonElement>())
  if (tabs.length < 2) return null

  const onKeyDown = (e: KeyboardEvent, index: number) => {
    const n = tabs.length
    let j: number
    if (e.key === 'ArrowRight') j = (index + 1) % n
    else if (e.key === 'ArrowLeft') j = (index - 1 + n) % n
    else if (e.key === 'Home') j = 0
    else if (e.key === 'End') j = n - 1
    else return
    e.preventDefault()
    const next = tabs[j]
    onSelect(next.id)
    refs.current.get(next.id)?.focus()
  }

  return (
    <div className="tabbar">
      <div className="tabbar__list" role="tablist" aria-label={label}>
        {tabs.map((tab, i) => {
          const selected = tab.id === activeId
          return (
            <div key={tab.id} className={cx('tab', selected && 'tab--active', tab.status === 'error' && 'tab--error')} title={tab.title ?? tab.label}>
              <Icon name={tab.status === 'error' ? 'alert' : 'envelope'} className="tab__icon" />
              <button
                ref={(el) => {
                  if (el) refs.current.set(tab.id, el)
                  else refs.current.delete(tab.id)
                }}
                type="button"
                role="tab"
                id={`tab-${tab.id}`}
                className="tab__label"
                aria-selected={selected}
                aria-controls={selected ? panelId : undefined}
                tabIndex={selected ? 0 : -1}
                onClick={() => onSelect(tab.id)}
                onKeyDown={(e) => onKeyDown(e, i)}
              >
                {tab.label}
              </button>
              <IconButton icon="x" label={closeLabel(tab)} className="tab__close" tabIndex={-1} onClick={() => onClose(tab.id)} />
            </div>
          )
        })}
      </div>
      <IconButton icon="plus" label={addLabel} className="tabbar__add" onClick={onAdd} />
    </div>
  )
}
