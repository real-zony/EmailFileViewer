import { useState } from 'react'
import { cx } from './cx.ts'

export interface Address {
  name?: string
  address: string
}

export interface AddressTextProps {
  address: Address
  /** 发件人用更重的字重 */
  emphasis?: boolean
}

// 名称 + 等宽字体的邮箱地址。没有名称或名称与地址相同时只显示地址。
export function AddressText({ address, emphasis }: AddressTextProps) {
  const showName = Boolean(address.name) && address.name !== address.address
  return (
    <span className={cx('addr', emphasis && 'addr--from')}>
      {showName && <span className="addr__name">{address.name}</span>}
      <span className="addr__email">{address.address}</span>
    </span>
  )
}

export interface AddressRowProps {
  /** 行标签，例如 "To"；放在 MessageHeader 的网格里 */
  label: string
  addresses: readonly Address[]
  /** 折叠时先显示几个 */
  collapseAfter?: number
  /** 折叠按钮文字，例如 (n) => `+${n} more` */
  moreLabel: (hidden: number) => string
  lessLabel: string
  defaultExpanded?: boolean
}

// 邮件头中的一行地址。没有地址时整行不渲染（规格：缺少某个邮件头）。
// 渲染为两个网格单元（标签 + 值），必须放在 MessageHeader 内。
export function AddressRow({ label, addresses, collapseAfter = 3, moreLabel, lessLabel, defaultExpanded = false }: AddressRowProps) {
  const [expanded, setExpanded] = useState(defaultExpanded)
  if (addresses.length === 0) return null
  const collapsible = addresses.length > collapseAfter
  const shown = expanded || !collapsible ? addresses : addresses.slice(0, collapseAfter)
  return (
    <>
      <span className="hlabel">{label}</span>
      <span className="hvalue">
        {shown.map((a, i) => (
          <AddressText key={`${a.address}-${i}`} address={a} />
        ))}
        {collapsible && (
          <button type="button" className="btn btn--link addr-more" aria-expanded={expanded} onClick={() => setExpanded((v) => !v)}>
            {expanded ? lessLabel : moreLabel(addresses.length - collapseAfter)}
          </button>
        )}
      </span>
    </>
  )
}
