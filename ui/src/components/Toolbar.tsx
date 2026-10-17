import type { ReactNode } from 'react'
import { Button, IconButton } from './Button.tsx'

export interface ToolbarProps {
  /** 左侧：打开按钮、面包屑 */
  start?: ReactNode
  /** 右侧：设置按钮 */
  end?: ReactNode
}

// 窗口顶部的工具栏，只负责布局
export function Toolbar({ start, end }: ToolbarProps) {
  return (
    <div className="toolbar">
      {start}
      <span className="toolbar__spacer" />
      {end}
    </div>
  )
}

export interface CrumbItem {
  id: string
  label: string
  /** 悬停提示：完整文件名 */
  title?: string
}

export interface BreadcrumbsProps {
  /** nav 的可访问名称 */
  label: string
  /** 从最外层到当前邮件；少于 2 级时不渲染 */
  items: readonly CrumbItem[]
  onNavigate: (index: number) => void
  backLabel: string
}

// 嵌套邮件的面包屑：返回按钮回到上一级，每一级都可以点击
export function Breadcrumbs({ label, items, onNavigate, backLabel }: BreadcrumbsProps) {
  if (items.length < 2) return null
  const last = items.length - 1
  return (
    <>
      <span className="toolbar__divider" aria-hidden="true" />
      <IconButton icon="back" label={backLabel} onClick={() => onNavigate(last - 1)} />
      <nav className="crumbs" aria-label={label}>
        <ol>
          {items.map((item, i) =>
            i === last ? (
              <li key={item.id}>
                <span aria-current="page" title={item.title ?? item.label}>{item.label}</span>
              </li>
            ) : (
              <li key={item.id}>
                <Button title={item.title ?? item.label} onClick={() => onNavigate(i)}>
                  <span>{item.label}</span>
                </Button>
              </li>
            ),
          )}
        </ol>
      </nav>
    </>
  )
}
