import { useLayoutEffect, useRef, useState, type ReactNode } from 'react'
import { AddressText, type Address } from './Address.tsx'
import { cx } from './cx.ts'

export interface MessageHeaderProps {
  /** 缺失（没有或为空）时不显示主题 */
  subject?: string
  showFullSubjectLabel: string
  showLessSubjectLabel: string
  fromLabel: string
  /** 发件人（可能不止一个）；缺失或为空时整行不显示 */
  from?: Address | readonly Address[]
  dateLabel: string
  /** 已按界面语言和本地时区格式化好的日期；缺失时整行不显示 */
  dateText?: string
  /** <time datetime> 的 ISO 值 */
  dateTime?: string
  /** 悬停提示：原始 Date 头 */
  dateTitle?: string
  /** 其余地址行：AddressRow（回复至、收件人、抄送、密送） */
  children?: ReactNode
  /** 徽标：已签名 / 已加密 */
  badges?: ReactNode
}

// 邮件头：主题（超过两行时折叠）、发件人、日期、地址行、徽标。
export function MessageHeader(props: MessageHeaderProps) {
  const { subject, children, badges } = props
  const senders = props.from === undefined ? [] : 'address' in props.from ? [props.from] : props.from
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
      {subject && (
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
      )}
      <div className="hgrid">
        {senders.length > 0 && (
          <>
            <span className="hlabel">{props.fromLabel}</span>
            <span className="hvalue">
              {senders.map((sender, i) => (
                <AddressText key={`${sender.address}-${i}`} address={sender} emphasis />
              ))}
            </span>
          </>
        )}
        {props.dateText && (
          <>
            <span className="hlabel hlabel--date">{props.dateLabel}</span>
            <time className="hdate" dateTime={props.dateTime} title={props.dateTitle}>
              {props.dateText}
            </time>
          </>
        )}
        {children}
      </div>
      {badges}
    </header>
  )
}
