import type { ReactNode } from 'react'
import { Icon } from './Icon.tsx'

// 窗口右上角的通知区域
export function ToastRegion({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="toasts" role="region" aria-label={label}>
      {children}
    </div>
  )
}

export interface ToastProps {
  title: ReactNode
  children?: ReactNode
  /** 操作按钮，例如“查看”“关闭” */
  actions?: ReactNode
}

// 通知（目前只有一种：打开失败的汇总）。不自动消失，由用户关闭。
export function Toast({ title, children, actions }: ToastProps) {
  return (
    <div className="toast" role="status">
      <Icon name="alert" className="toast__icon" />
      <div className="toast__body">
        <span className="toast__title">{title}</span>
        {children && <span className="toast__text">{children}</span>}
        {actions && <div className="toast__actions">{actions}</div>}
      </div>
    </div>
  )
}
