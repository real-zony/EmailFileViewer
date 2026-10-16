import type { ReactNode } from 'react'
import { Button } from './Button.tsx'
import { Icon, type IconName } from './Icon.tsx'
import { cx } from './cx.ts'

export interface NoticeProps {
  /** warn：需要用户决定；quiet：一行安静的确认文字 */
  tone: 'warn' | 'quiet'
  icon: IconName
  title: ReactNode
  description?: ReactNode
  /** 作用范围说明，例如 "This message only" */
  scope?: string
  action?: { label: string; onClick: () => void }
}

// 提示条。只能放在 BodySheet 的 notice 插槽里（接缝以上）。
export function Notice({ tone, icon, title, description, scope, action }: NoticeProps) {
  return (
    <div className={cx('notice', `notice--${tone}`)} role="status">
      <Icon name={icon} className="notice__icon" />
      <span className="notice__text">
        {tone === 'warn' ? <strong>{title}</strong> : title}
        {description && <> <span className="notice__sub">{description}</span></>}
      </span>
      {(scope || action) && (
        <span className="notice__actions">
          {scope && <span className="notice__scope">{scope}</span>}
          {action && <Button variant="warn" onClick={action.onClick}>{action.label}</Button>}
        </span>
      )}
    </div>
  )
}

export interface RemoteContentNoticeProps {
  state: 'blocked' | 'loaded'
  /** blocked 时的标题和说明 */
  blockedTitle: string
  blockedDescription: string
  scopeLabel: string
  loadLabel: string
  /** loaded 时的一行确认文字 */
  loadedText: string
  onLoad: () => void
}

// 远程内容提示：blocked 时是琥珀色提示条加“加载”按钮；loaded 时收起为一行确认。v1 没有“重新阻止”。
export function RemoteContentNotice(props: RemoteContentNoticeProps) {
  if (props.state === 'loaded') return <Notice tone="quiet" icon="check" title={props.loadedText} />
  return (
    <Notice
      tone="warn"
      icon="imageOff"
      title={props.blockedTitle}
      description={props.blockedDescription}
      scope={props.scopeLabel}
      action={{ label: props.loadLabel, onClick: props.onLoad }}
    />
  )
}
