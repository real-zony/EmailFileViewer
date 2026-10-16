import { useLayoutEffect, useRef, useState, type ReactNode } from 'react'
import { AddressText, type Address } from './Address.tsx'
import { cx } from './cx.ts'

export interface MessageHeaderProps {
  subject: string
  showFullSubjectLabel: string
  showLessSubjectLabel: string
  fromLabel: string
  from: Address
  dateLabel: string
  /** 已按界面语言和本地时区格式化好的日期 */
  dateText: string
  /** <time datetime> 的 ISO 值 */
  dateTime: string
  /** 悬停提示：原始 Date 头 */
  dateTitle?: string
  /** 其余地址行：AddressRow（回复至、收件人、抄送、密送） */
  children?: ReactNode
  /** 徽标：已签名 / 已加密 */
  badges?: ReactNode
}

// 邮件头：主题（超过两行时折叠）、发件人、日期、地址行、徽标。
export function MessageHeader(props: MessageHeaderProps) {
  const { subject, from, children, badges } = props
  const subjectRef = useRef<HTMLHeadingElement>(null)
  const [overflowing, setOverflowing] = useState(false)
  const [expanded, setExpanded] = useState(false)

  // 用实际排版判断主题是否超过两行；窗口大小变化时重新判断
  useLayoutEffect(() => {
    const el = subjectRef.current
    if (!el || expanded) return
    const measure = () => setOverflowing(el.scrollHeight > el.clientHeight + 1)
    measure()
    if (typeof ResizeObserver === 'undefined') return
    const ro = new ResizeObserver(measure)
    ro.observe(el)
    return () => ro.disconnect()
  }, [subject, expanded])

  return (
    <header className="msghead">
      <div className="subject-wrap">
        <h1 ref={subjectRef} className={cx('subject', !expanded && 'subject--clamped')} title={subject}>
          {subject}
        </h1>
        {(overflowing || expanded) && (
          <button type="button" className="btn btn--link addr-more" aria-expanded={expanded} onClick={() => setExpanded((v) => !v)}>
            {expanded ? props.showLessSubjectLabel : props.showFullSubjectLabel}
          </button>
        )}
      </div>
      <div className="hgrid">
        <span className="hlabel">{props.fromLabel}</span>
        <span className="hvalue">
          <AddressText address={from} emphasis />
        </span>
        <span className="hlabel hlabel--date">{props.dateLabel}</span>
        <time className="hdate" dateTime={props.dateTime} title={props.dateTitle}>
          {props.dateText}
        </time>
        {children}
      </div>
      {badges}
    </header>
  )
}
