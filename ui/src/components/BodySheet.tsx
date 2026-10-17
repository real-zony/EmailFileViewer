import type { ReactNode } from 'react'
import { Icon, type IconName } from './Icon.tsx'

export interface BodySheetProps {
  /** 区域的可访问名称，例如 "Message content" */
  label: string
  /** 纸面顶部标签行的文字，例如 "Message content · shown in isolation, scripts off" */
  railText: string
  /** 正文格式，例如 "HTML" / "Plain text" */
  formatLabel: string
  /** 应用层的提示条（RemoteContentNotice），显示在接缝以上 */
  notice?: ReactNode
  /** 接缝以下的插槽：只放沙箱 iframe（7.5），应用不在这里绘制任何东西 */
  children: ReactNode
}

// 邮件纸面：接缝以上是应用（可信），以下是邮件内容（不可信）。纸面颜色固定，不随主题变化。
export function BodySheet({ label, railText, formatLabel, notice, children }: BodySheetProps) {
  return (
    <section className="sheet" aria-label={label}>
      <div className="sheet__chrome">
        <div className="sheet__label">
          <Icon name="shield" />
          <span className="sheet__label-text">{railText}</span>
          <span className="sheet__format">{formatLabel}</span>
        </div>
        {notice}
      </div>
      <div className="sheet__seam" aria-hidden="true" />
      <div className="sheet__body">{children}</div>
    </section>
  )
}

export interface PlaceholderSheetProps {
  icon: IconName
  title: string
  body: string
}

// 没有发件人内容可显示时（加密邮件）占据正文位置：虚线框，不画接缝
export function PlaceholderSheet({ icon, title, body }: PlaceholderSheetProps) {
  return (
    <section className="sheet sheet--placeholder" aria-label={title}>
      <div className="state">
        <div className="state__icon state__icon--neutral">
          <Icon name={icon} />
        </div>
        <h2 className="state__title">{title}</h2>
        <p className="state__body">{body}</p>
      </div>
    </section>
  )
}
