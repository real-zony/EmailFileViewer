import type { ReactNode } from 'react'

// 整个窗口。对话框遮罩相对它定位。
export function AppShell({ children }: { children: ReactNode }) {
  return <div className="win">{children}</div>
}

export interface MainAreaProps {
  children: ReactNode
  id?: string
  /** 有标签栏时传入当前标签的 id（tab-<id>），此时作为 tabpanel */
  labelledBy?: string
}

// 工具栏下方的内容区。通知区域和拖放遮罩相对它定位。
export function MainArea({ children, id, labelledBy }: MainAreaProps) {
  return (
    <div className="win__main" id={id} role={labelledBy ? 'tabpanel' : undefined} aria-labelledby={labelledBy}>
      {children}
    </div>
  )
}

export interface ViewerLayoutProps {
  /** 邮件头 + 附件：不随正文滚走，过高时自身滚动 */
  top: ReactNode
  /** BodySheet 或 PlaceholderSheet */
  body: ReactNode
}

// 邮件查看布局（D7）：正文只在 iframe 内部滚动
export function ViewerLayout({ top, body }: ViewerLayoutProps) {
  return (
    <div className="viewer">
      <div className="viewer__top">{top}</div>
      {body}
    </div>
  )
}
