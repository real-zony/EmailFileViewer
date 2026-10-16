import { useId, type ReactNode } from 'react'
import { Button } from './Button.tsx'
import { Icon } from './Icon.tsx'
import { cx } from './cx.ts'

// 信封插图，内框是与“接缝”相同的斜纹
function EnvelopeArt() {
  const patternId = useId()
  return (
    <svg className="dropzone__art" viewBox="0 0 132 96" aria-hidden="true" focusable="false">
      <defs>
        <pattern id={patternId} width="10" height="10" patternUnits="userSpaceOnUse" patternTransform="rotate(45)">
          <rect width="5" height="10" style={{ fill: 'var(--efv-accent)' }} />
        </pattern>
      </defs>
      <rect x="3" y="10" width="126" height="80" rx="7" style={{ fill: 'var(--efv-raised)' }} stroke="currentColor" strokeWidth="1.5" />
      <rect x="9" y="16" width="114" height="68" rx="3" fill="none" stroke={`url(#${patternId})`} strokeWidth="4" opacity=".55" />
      <path d="M14 22 66 56l52-34" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinejoin="round" />
      <rect x="94" y="56" width="20" height="22" rx="1.5" fill="none" stroke="currentColor" strokeWidth="1.2" strokeDasharray="2 2" />
    </svg>
  )
}

export interface DropMessage {
  title: string
  body: string
}

export interface EmptyStateProps {
  title: string
  body: string
  openLabel: string
  onOpen: () => void
  /** 按钮下方的快捷键提示，例如 <>or press <Kbd>⌘O</Kbd></> */
  shortcutHint?: ReactNode
  privacyNote: string
  /** 文件正悬停在窗口上方时传入，显示拖放状态 */
  dragging?: DropMessage
}

// 空状态：整个内容区都是拖放目标
export function EmptyState({ title, body, openLabel, onOpen, shortcutHint, privacyNote, dragging }: EmptyStateProps) {
  return (
    <div className="empty">
      <div className={cx('dropzone', dragging && 'dropzone--active')}>
        <div className="state">
          <EnvelopeArt />
          {dragging ? (
            <>
              <h1 className="state__title">{dragging.title}</h1>
              <p className="state__body">{dragging.body}</p>
            </>
          ) : (
            <>
              <h1 className="state__title">{title}</h1>
              <p className="state__body">{body}</p>
              <div className="state__actions">
                <Button variant="primary" size="lg" icon="folder" onClick={onOpen}>{openLabel}</Button>
              </div>
              {shortcutHint && <span className="dropzone__hint">{shortcutHint}</span>}
            </>
          )}
        </div>
        <p className="dropzone__foot">
          <Icon name="lock" />
          <span>{privacyNote}</span>
        </p>
      </div>
    </div>
  )
}

// 已有邮件打开时，文件悬停在窗口上方的遮罩。放在 MainArea 内。
export function DropOverlay({ title, body }: DropMessage) {
  return (
    <div className="drop-overlay" role="status">
      <div className="dropzone dropzone--active">
        <div className="state">
          <EnvelopeArt />
          <p className="state__title">{title}</p>
          <p className="state__body">{body}</p>
        </div>
      </div>
    </div>
  )
}
