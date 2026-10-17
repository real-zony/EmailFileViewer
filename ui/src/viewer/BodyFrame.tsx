import { useMemo } from 'react'
import type { BodySourceFormat } from '../api/types.ts'
import { BODY_SANDBOX, buildBodyDocument } from './bodyDocument.ts'

export interface BodyFrameProps {
  /** sidecar 已清理过的 HTML，原样放进文档 */
  html: string | null
  format: BodySourceFormat
  /** 用户是否已对这一封邮件选择加载远程内容 */
  remoteAllowed: boolean
  title: string
}

// 邮件正文：沙箱 iframe + srcdoc（D5）。必须放在 BodySheet 的插槽里，应用不在它上面叠加任何东西。
// CSP 改变时换 key 重新创建 iframe，不改 sandbox 属性。
export function BodyFrame({ html, format, remoteAllowed, title }: BodyFrameProps) {
  const srcDoc = useMemo(() => buildBodyDocument(html, format, remoteAllowed), [html, format, remoteAllowed])
  return (
    <iframe
      key={remoteAllowed ? 'remote' : 'blocked'}
      className="sheet__frame"
      title={title}
      sandbox={BODY_SANDBOX}
      referrerPolicy="no-referrer"
      srcDoc={srcDoc}
    />
  )
}
