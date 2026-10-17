import { useId, useState } from 'react'
import { Button, IconButton } from './Button.tsx'
import { FileName } from './FileName.tsx'
import { Icon } from './Icon.tsx'
import { cx } from './cx.ts'
import { formatFileSize, splitFileName } from './format.ts'

/** file：普通文件；message：嵌套邮件（在应用内打开）；executable：可执行文件（只能保存） */
export type AttachmentKind = 'file' | 'message' | 'executable'

export interface AttachmentView {
  id: string
  fileName: string
  size: number
  kind: AttachmentKind
}

export interface AttachmentLabels {
  heading: string
  /** 标题后的摘要，例如 "4 files · 5.2 MB"（调用方按语言拼好） */
  summary: string
  saveAll: string
  open: string
  /** 嵌套邮件“打开”按钮的悬停提示 */
  openMessageHint: string
  /** 保存按钮的悬停提示，例如 "Save…" */
  save: string
  /** 保存按钮的可访问名称，例如 (name) => `Save ${name}…` */
  saveNamed: (fileName: string) => string
  saveOnly: string
  /** 嵌套邮件大小前的类型文字，例如 "Email" */
  messageKind: string
  showAll: (count: number) => string
  showFewer: string
}

export interface AttachmentCallbacks {
  onOpen: (id: string) => void
  onSave: (id: string) => void
  onOpenMessage: (id: string) => void
  /** 用户试图打开可执行附件（双击或在卡片上按回车）：调用方弹出 ExecutableBlockedDialog */
  onBlockedOpen: (id: string) => void
}

export interface AttachmentItemProps extends AttachmentCallbacks {
  attachment: AttachmentView
  labels: AttachmentLabels
}

export function AttachmentItem({ attachment: a, labels, onOpen, onSave, onOpenMessage, onBlockedOpen }: AttachmentItemProps) {
  const ext = splitFileName(a.fileName)[1].replace('.', '')
  const exec = a.kind === 'executable'
  // 双击是卡片的默认动作
  const activate = () => {
    if (a.kind === 'message') onOpenMessage(a.id)
    else if (exec) onBlockedOpen(a.id)
    else onOpen(a.id)
  }
  return (
    <li
      className={cx('att', a.kind === 'message' && 'att--message', exec && 'att--exec')}
      title={a.fileName}
      onDoubleClick={activate}
      // 可执行附件没有“打开”按钮，卡片本身可聚焦，回车触发默认动作（被拒绝）
      tabIndex={exec ? 0 : undefined}
      aria-label={exec ? `${a.fileName}, ${labels.saveOnly}` : undefined}
      onKeyDown={exec ? (e) => { if (e.key === 'Enter' && e.target === e.currentTarget) { e.preventDefault(); onBlockedOpen(a.id) } } : undefined}
    >
      <span className="att__type" aria-hidden="true">
        {a.kind === 'message' ? <Icon name="envelope" /> : ext.slice(0, 4) || 'file'}
      </span>
      <span className="att__body">
        <FileName name={a.fileName} />
        <span className="att__size">
          {a.kind === 'message' && `${labels.messageKind} · `}
          {formatFileSize(a.size)}
          {exec && (
            <span className="att__flag">
              <Icon name="lock" />
              {labels.saveOnly}
            </span>
          )}
        </span>
      </span>
      <span className="att__actions">
        {a.kind === 'message' && (
          <Button variant="secondary" title={labels.openMessageHint} onClick={() => onOpenMessage(a.id)}>{labels.open}</Button>
        )}
        {a.kind === 'file' && <Button variant="secondary" onClick={() => onOpen(a.id)}>{labels.open}</Button>}
        <IconButton icon="save" label={labels.saveNamed(a.fileName)} title={labels.save} onClick={() => onSave(a.id)} />
      </span>
    </li>
  )
}

export interface AttachmentListProps extends AttachmentCallbacks {
  attachments: readonly AttachmentView[]
  labels: AttachmentLabels
  onSaveAll: () => void
  /** 折叠时先显示几个，默认 6 */
  collapseAfter?: number
}

// 附件列表：没有附件时不渲染；只有 1 个附件时不显示“全部保存”
export function AttachmentList({ attachments, labels, onSaveAll, collapseAfter = 6, ...callbacks }: AttachmentListProps) {
  const [expanded, setExpanded] = useState(false)
  const headingId = useId()
  if (attachments.length === 0) return null
  const collapsible = attachments.length > collapseAfter
  const shown = expanded || !collapsible ? attachments : attachments.slice(0, collapseAfter)
  return (
    <section className="atts" aria-labelledby={headingId}>
      <div className="atts__head">
        <h2 className="atts__title" id={headingId}>
          <Icon name="clip" />
          {labels.heading}
        </h2>
        <span className="atts__meta">{labels.summary}</span>
        <span className="toolbar__spacer" />
        {attachments.length > 1 && (
          <Button variant="secondary" size="sm" icon="save" onClick={onSaveAll}>{labels.saveAll}</Button>
        )}
      </div>
      <ul className="atts__grid">
        {shown.map((a) => (
          <AttachmentItem key={a.id} attachment={a} labels={labels} {...callbacks} />
        ))}
      </ul>
      {collapsible && (
        <div>
          <button type="button" className="btn btn--link" aria-expanded={expanded} onClick={() => setExpanded((v) => !v)}>
            {expanded ? labels.showFewer : labels.showAll(attachments.length)}
          </button>
        </div>
      )}
    </section>
  )
}
