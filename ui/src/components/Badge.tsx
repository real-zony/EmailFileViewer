import { useEffect, useId, useRef, useState, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { IconButton } from './Button.tsx'
import { Icon, type IconName } from './Icon.tsx'
import { useFloatingPosition } from './floating.ts'

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
  const pop = useRef<HTMLSpanElement>(null)
  const pos = useFloatingPosition(open, wrap, pop)

  useEffect(() => {
    if (!open) return
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setOpen(false)
    }
    const onDown = (e: MouseEvent) => {
      const target = e.target as Node
      if (wrap.current?.contains(target) || pop.current?.contains(target)) return
      setOpen(false)
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
      {/* 气泡渲染到 body 并用 fixed 定位，不会被 .viewer__top 的 overflow 裁切；贴近窗口边缘时翻转或内收 */}
      {details &&
        open &&
        createPortal(
          <span
            ref={pop}
            className="popover"
            id={popId}
            role="note"
            data-placement={pos?.placement}
            style={pos ? { top: pos.top, left: pos.left } : { top: 0, left: 0, visibility: 'hidden' }}
          >
            {details.content}
          </span>,
          document.body,
        )}
    </span>
  )
}

export function BadgeList({ children }: { children: ReactNode }) {
  return <div className="badges">{children}</div>
}
