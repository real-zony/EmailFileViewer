import { useEffect, useId, useRef, type KeyboardEvent, type ReactNode, type RefObject } from 'react'
import { Button } from './Button.tsx'
import { Icon, type IconName } from './Icon.tsx'
import { cx } from './cx.ts'

const FOCUSABLE = 'button:not([disabled]), [href], input:not([disabled]), select, textarea, [tabindex]:not([tabindex="-1"])'

export interface DialogProps {
  /** alertdialog 用于需要用户确认风险的情况（例如可执行附件） */
  role?: 'dialog' | 'alertdialog'
  title: ReactNode
  /** 标题上方的图标（警示样式） */
  icon?: IconName
  /** 标题行右侧的内容，例如关闭按钮 */
  headerEnd?: ReactNode
  children?: ReactNode
  actions?: ReactNode
  onClose: () => void
  /** 打开时获得焦点的元素；不传时聚焦对话框内第一个可聚焦元素 */
  initialFocusRef?: RefObject<HTMLElement | null>
  className?: string
}

// 模态对话框：遮罩盖住整个窗口；Esc 和点击遮罩关闭；Tab 在对话框内循环；关闭后焦点回到打开前的位置
export function Dialog({ role = 'dialog', title, icon, headerEnd, children, actions, onClose, initialFocusRef, className }: DialogProps) {
  const titleId = useId()
  const bodyId = useId()
  const panel = useRef<HTMLDivElement>(null)

  useEffect(() => {
    const previous = document.activeElement as HTMLElement | null
    const first = initialFocusRef?.current ?? panel.current?.querySelector<HTMLElement>(FOCUSABLE)
    first?.focus()
    return () => previous?.focus?.()
    // ref 对象本身是稳定的，实际上只在打开时执行一次
  }, [initialFocusRef])

  const onKeyDown = (e: KeyboardEvent) => {
    if (e.key === 'Escape') {
      e.stopPropagation()
      onClose()
      return
    }
    if (e.key !== 'Tab' || !panel.current) return
    const items = Array.from(panel.current.querySelectorAll<HTMLElement>(FOCUSABLE))
    if (items.length === 0) return
    const firstEl = items[0]
    const lastEl = items[items.length - 1]
    if (e.shiftKey && document.activeElement === firstEl) {
      e.preventDefault()
      lastEl.focus()
    } else if (!e.shiftKey && document.activeElement === lastEl) {
      e.preventDefault()
      firstEl.focus()
    }
  }

  return (
    <div
      className="scrim"
      onMouseDown={(e) => {
        if (e.target !== e.currentTarget) return
        // 阻止默认的失焦，让关闭后焦点能回到打开前的元素
        e.preventDefault()
        onClose()
      }}
    >
      <div
        ref={panel}
        className={cx('dialog', className)}
        role={role}
        aria-modal="true"
        aria-labelledby={titleId}
        aria-describedby={children ? bodyId : undefined}
        onKeyDown={onKeyDown}
      >
        {icon && (
          <div className="dialog__icon">
            <Icon name={icon} />
          </div>
        )}
        {headerEnd ? (
          <div className="settings__head">
            <h2 className="dialog__title" id={titleId}>{title}</h2>
            {headerEnd}
          </div>
        ) : (
          <h2 className="dialog__title" id={titleId}>{title}</h2>
        )}
        {children && <div id={bodyId} className="dialog__content">{children}</div>}
        {actions && <div className="dialog__actions">{actions}</div>}
      </div>
    </div>
  )
}

export interface ExecutableBlockedDialogProps {
  /** 已经代入文件名的标题，例如 “setup.exe” can't be opened here */
  title: string
  body: string
  note: string
  cancelLabel: string
  saveLabel: string
  onSave: () => void
  onCancel: () => void
}

// 可执行附件被拒绝打开：唯一的出路是保存；初始焦点在“取消”上，避免回车直接保存
export function ExecutableBlockedDialog({ title, body, note, cancelLabel, saveLabel, onSave, onCancel }: ExecutableBlockedDialogProps) {
  const cancelRef = useRef<HTMLButtonElement>(null)
  return (
    <Dialog
      role="alertdialog"
      icon="lock"
      title={title}
      onClose={onCancel}
      initialFocusRef={cancelRef}
      actions={
        <>
          <Button ref={cancelRef} variant="secondary" onClick={onCancel}>{cancelLabel}</Button>
          <Button variant="primary" icon="save" onClick={onSave}>{saveLabel}</Button>
        </>
      }
    >
      <p className="dialog__body">{body}</p>
      <p className="dialog__body dialog__body--note">{note}</p>
    </Dialog>
  )
}
