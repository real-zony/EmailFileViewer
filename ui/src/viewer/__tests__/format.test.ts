import { describe, expect, it } from 'vitest'
import { operationReason, openFailureCategory } from '../errors.ts'
import { formatMailDate } from '../formatDate.ts'
import { attachmentName, displayName, toAddress, toAddresses, toAttachmentViews } from '../mailModel.ts'

describe('formatMailDate：按界面语言格式化，转换到时区', () => {
  // email-viewing / 日期按本地时区显示：Mon, 5 Oct 2026 09:00:00 -0700 就是 2026-10-05T16:00:00Z
  const sent = '2026-10-05T16:00:00Z'

  it('UTC+8：2026-10-06 00:00（英文和中文各按自己的格式）', () => {
    expect(formatMailDate(sent, 'en', 'Asia/Shanghai')).toBe('Tue, Oct 6, 2026, 12:00 AM')
    expect(formatMailDate(sent, 'zh-CN', 'Asia/Shanghai')).toBe('2026年10月6日周二 00:00')
  })

  it('其他时区得到各自的本地时间', () => {
    expect(formatMailDate(sent, 'en', 'America/Los_Angeles')).toBe('Mon, Oct 5, 2026, 9:00 AM')
    expect(formatMailDate(sent, 'en', 'UTC')).toBe('Mon, Oct 5, 2026, 4:00 PM')
    expect(formatMailDate(sent, 'zh-CN', 'UTC')).toBe('2026年10月5日周一 16:00')
  })

  it('中文用 24 小时制，午夜是 00:xx 而不是 24:xx', () => {
    expect(formatMailDate('2026-10-05T00:05:00Z', 'zh-CN', 'UTC')).toBe('2026年10月5日周一 00:05')
  })

  it('不传时区就用系统时区（本地）', () => {
    const original = process.env.TZ
    try {
      process.env.TZ = 'Asia/Shanghai'
      expect(formatMailDate(sent, 'en')).toBe('Tue, Oct 6, 2026, 12:00 AM')
      process.env.TZ = 'America/Los_Angeles'
      expect(formatMailDate(sent, 'en')).toBe('Mon, Oct 5, 2026, 9:00 AM')
    } finally {
      if (original === undefined) delete process.env.TZ
      else process.env.TZ = original
    }
  })

  it('无法解析的日期返回 null', () => {
    expect(formatMailDate('not a date', 'en')).toBeNull()
  })
})

describe('openFailureCategory：错误码 -> 说明类别', () => {
  it.each([
    ['unsupported_format', 'unsupported'],
    ['corrupted', 'corrupt'],
    ['sidecar_crashed', 'corrupt'],
    ['timeout', 'corrupt'],
    ['unreadable', 'unreadable'],
    ['internal_error', 'generic'],
    ['dialog_failed', 'generic'],
    ['something_new', 'generic'],
    ['unknown', 'generic'],
  ])('%s -> %s', (code, category) => {
    expect(openFailureCategory(code)).toBe(category)
  })
})

describe('operationReason', () => {
  it('已知的错误码各有说明，崩溃和超时按“已损坏”，其余是通用说明', () => {
    expect(operationReason('write_failed')).toBe('write_failed')
    expect(operationReason('document_not_found')).toBe('document_not_found')
    expect(operationReason('sidecar_crashed')).toBe('corrupted')
    expect(operationReason('timeout')).toBe('corrupted')
    expect(operationReason('internal_error')).toBe('generic')
    expect(operationReason('')).toBe('generic')
  })
})

describe('mailModel', () => {
  it('地址同时保留名称和邮箱；只有名称时把名称当作地址；两项都没有的丢弃', () => {
    expect(toAddress({ name: 'Alice Zhang', address: 'alice@example.com' })).toEqual({ name: 'Alice Zhang', address: 'alice@example.com' })
    expect(toAddress({ name: null, address: 'a@example.com' })).toEqual({ name: undefined, address: 'a@example.com' })
    expect(toAddress({ name: 'undisclosed-recipients', address: null })).toEqual({ address: 'undisclosed-recipients' })
    expect(toAddress({ name: ' ', address: ' ' })).toBeNull()
    expect(toAddresses([{ name: null, address: null }, { name: 'B', address: 'b@example.com' }])).toEqual([{ name: 'B', address: 'b@example.com' }])
  })

  it('附件：嵌套邮件、可执行文件、普通文件；没有文件名时用占位名', () => {
    const views = toAttachmentViews(
      [
        { id: '1', fileName: 'report.pdf', size: 10, mimeType: 'application/pdf', isNestedEmail: false },
        { id: '2', fileName: 'forwarded.eml', size: 20, mimeType: 'message/rfc822', isNestedEmail: true },
        { id: '3', fileName: 'setup.EXE', size: 30, mimeType: null, isNestedEmail: false },
        { id: '4', fileName: null, size: 40, mimeType: null, isNestedEmail: false },
      ],
      'Unnamed',
    )
    expect(views).toEqual([
      { id: '1', fileName: 'report.pdf', size: 10, kind: 'file' },
      { id: '2', fileName: 'forwarded.eml', size: 20, kind: 'message' },
      { id: '3', fileName: 'setup.EXE', size: 30, kind: 'executable' },
      { id: '4', fileName: 'Unnamed', size: 40, kind: 'file' },
    ])
    expect(attachmentName({ id: '5', fileName: '  ', size: 0, mimeType: null, isNestedEmail: false }, 'Unnamed')).toBe('Unnamed')
  })

  it('标签页名称去掉 .eml / .msg 扩展名', () => {
    expect(displayName('AW26 trade preview.msg')).toBe('AW26 trade preview')
    expect(displayName('a.b.EML')).toBe('a.b')
    expect(displayName('photo.png')).toBe('photo.png')
    expect(displayName('.eml')).toBe('.eml')
  })
})
