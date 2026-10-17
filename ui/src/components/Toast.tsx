import type { ReactNode } from 'react'
import { Icon, type IconName } from './Icon.tsx'

// 窗口右上角的通知区域
export function ToastRegion({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="toasts" role="region" aria-label={label}>
      {children}
    </div>
  )
}

/**
 * 通知的语义，沿用全局语义色：
 * - success：操作完成（已保存、已打开），强调色 + 对勾
 * - info：中性信息，灰色 + i
 * - warning：需要注意，例如部分失败，琥珀色 + 三角
 * - error：没能完成，红色 + 叉
 */
export type ToastVariant = 'success' | 'info' | 'warning' | 'error'

const VARIANT_ICON: Record<ToastVariant, IconName> = {
  success: 'checkCircle',
  info: 'info',
  warning: 'alert',
  error: 'xCircle',
}

export interface ToastProps {
  title: ReactNode
  children?: ReactNode
  /** 操作按钮，例如“查看”“关闭” */
  actions?: ReactNode
  /** 语义变体，默认 error（与之前只有一种红色通知时的含义一致） */
  variant?: ToastVariant
}

// 通知：不自动消失，由用户关闭。颜色和图标由 variant 决定，文字本身也要说清楚结果。
export function Toast({ title, children, actions, variant = 'error' }: ToastProps) {
  return (
    <div className={`toast toast--${variant}`} role="status" data-variant={variant}>
      <Icon name={VARIANT_ICON[variant]} className="toast__icon" />
      <div className="toast__body">
        <span className="toast__title">{title}</span>
        {children && <span className="toast__text">{children}</span>}
        {actions && <div className="toast__actions">{actions}</div>}
      </div>
    </div>
  )
}
