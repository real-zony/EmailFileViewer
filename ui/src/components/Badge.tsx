import { useEffect, useId, useRef, useState, type ReactNode } from 'react'
import { IconButton } from './Button.tsx'
import { Icon, type IconName } from './Icon.tsx'

export interface BadgeProps {
  icon: IconName
  children: ReactNode
  /** 弱化的补充文字，例如 "signature not verified" */
  muted?: ReactNode
  /** 可选的说明气泡：info 按钮的名称和气泡内容 */
  details?: { buttonLabel: string; content: ReactNode }
}

// 邮件头里的状态徽标（已签名、已加密）。始终是中性色，不用绿色暗示“通过”。
export function Badge({ icon, children, muted, details }: BadgeProps) {
  const [open, setOpen] = useState(false)
  const popId = useId()
  const wrap = useRef<HTMLSpanElement>(null)

  useEffect(() => {
    if (!open) return
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setOpen(false)
    }
    const onDown = (e: MouseEvent) => {
      if (wrap.current && !wrap.current.contains(e.target as Node)) setOpen(false)
    }
    document.addEventListener('keydown', onKey)
    document.addEventListener('mousedown', onDown)
    return () => {
      document.removeEventListener('keydown', onKey)
      document.removeEventListener('mousedown', onDown)
    }
  }, [open])

  return (
    <span className="badge-wrap" ref={wrap}>
      <span className="badge">
        <Icon name={icon} />
        <span>
          {children}
          {muted && <span className="badge__muted"> · {muted}</span>}
        </span>
        {details && (
          <IconButton
            icon="info"
            label={details.buttonLabel}
            aria-expanded={open}
            aria-controls={popId}
            onClick={() => setOpen((v) => !v)}
          />
        )}
      </span>
      {details && open && (
        <span className="popover" id={popId} role="note">
          {details.content}
        </span>
      )}
    </span>
  )
}

export function BadgeList({ children }: { children: ReactNode }) {
  return <div className="badges">{children}</div>
}
