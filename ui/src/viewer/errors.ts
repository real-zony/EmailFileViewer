// 错误码 -> 界面上的说法

/** 打开失败的文件在错误标签页里显示哪一类说明 */
export type OpenFailureCategory = 'unsupported' | 'corrupt' | 'unreadable' | 'generic'

export function openFailureCategory(code: string): OpenFailureCategory {
  switch (code) {
    case 'unsupported_format':
      return 'unsupported'
    // 解析进程崩溃或超时，前端按“文件已损坏或无法解析”显示（D11）
    case 'corrupted':
    case 'sidecar_crashed':
    case 'timeout':
      return 'corrupt'
    case 'unreadable':
      return 'unreadable'
    default:
      return 'generic'
  }
}

const KNOWN_REASONS = [
  'write_failed',
  'document_not_found',
  'attachment_not_found',
  'not_nested_email',
  'unsupported_format',
  'corrupted',
  'unreadable',
  'dialog_failed',
] as const

export type OperationReason = (typeof KNOWN_REASONS)[number] | 'generic'

/** 保存、打开附件等操作失败时，toast 里的说明 */
export function operationReason(code: string): OperationReason {
  switch (code) {
    case 'sidecar_crashed':
    case 'timeout':
      return 'corrupted'
    default:
      return (KNOWN_REASONS as readonly string[]).includes(code) ? (code as OperationReason) : 'generic'
  }
}
