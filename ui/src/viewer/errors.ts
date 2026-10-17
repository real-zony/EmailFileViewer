// 错误码 -> 界面上的说法
import type { OperationKind } from './store.ts'

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

export type OperationReason = (typeof KNOWN_REASONS)[number] | 'timeout' | 'openTimeout' | 'generic'

/**
 * 用户选了位置、会往那里写文件的操作：保存、全部保存。超时只说明没等到结果，文件可能还在写，
 * 不能说成“文件已损坏”，提示用户去检查目标位置。
 */
const SAVING_OPERATIONS: ReadonlySet<OperationKind> = new Set<OperationKind>(['saveFailed', 'saveAllFailed'])

/** 保存、打开附件等操作失败时，toast 里的说明 */
export function operationReason(code: string, operation: OperationKind): OperationReason {
  switch (code) {
    case 'sidecar_crashed':
      return 'corrupted'
    case 'timeout':
      // 打开附件（openFailed，背后是 attachment.extractForOpen）用户没有选过保存位置，提示重试；
      // 打开嵌套邮件是解析，超时仍按“无法解析”显示
      if (SAVING_OPERATIONS.has(operation)) return 'timeout'
      return operation === 'openFailed' ? 'openTimeout' : 'corrupted'
    default:
      return (KNOWN_REASONS as readonly string[]).includes(code) ? (code as OperationReason) : 'generic'
  }
}
