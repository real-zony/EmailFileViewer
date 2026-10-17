// EmailDocument -> 展示组件需要的数据
import { isExecutableFileName } from '../api/executable.ts'
import type { EmailAddress, EmailAttachment } from '../api/types.ts'
import type { Address, AttachmentView } from '../components/index.ts'

/** 名称和邮箱地址都保留；只有名称没有地址时（例如 undisclosed-recipients）把名称当作地址显示 */
export function toAddress(source: EmailAddress): Address | null {
  const name = source.name?.trim() || undefined
  const address = source.address?.trim() || undefined
  if (address) return { name, address }
  if (name) return { address: name }
  return null
}

export function toAddresses(list: readonly EmailAddress[]): Address[] {
  return list.flatMap((a) => toAddress(a) ?? [])
}

export function attachmentName(attachment: EmailAttachment, unnamed: string): string {
  return attachment.fileName?.trim() || unnamed
}

// 嵌套邮件在应用内打开；可执行文件和脚本（D6）只能保存；其余按普通文件
export function toAttachmentViews(attachments: readonly EmailAttachment[], unnamed: string): AttachmentView[] {
  return attachments.map((a) => {
    const fileName = attachmentName(a, unnamed)
    const kind = a.isNestedEmail ? 'message' : isExecutableFileName(fileName) ? 'executable' : 'file'
    return { id: a.id, fileName, size: a.size, kind }
  })
}

/** 标签页和面包屑上的名字：去掉 .eml / .msg 扩展名，完整文件名放在悬停提示里 */
export function displayName(fileName: string): string {
  return fileName.replace(/\.(eml|msg)$/i, '') || fileName
}
