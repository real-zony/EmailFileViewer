import { describe, expect, it } from 'vitest'
import type { MockFile } from '../../api/mockApi.ts'
import { FIXTURES, fixture } from '../fixtures.ts'

const documents = Object.entries(FIXTURES).filter(([, f]) => f.document)

// 每个必须覆盖的类别至少有一个 fixture（清单在 scripts/generate-fixtures.ts）
const REQUIRED = [
  'marketing-remote', 'html-malicious', 'link-mismatch', 'cid-inline', 'charset-gbk', 'plain-text', 'rtf-only', 'signed', 'signed-opaque',
  'encrypted', 'attachments-multi', 'attachments-executables', 'attachments-unsafe-names', 'nested-two-levels', 'winmail', 'segmented-body',
]

describe('固定的示例数据（dev/fixtures）', () => {
  it('包含要求的全部类别和三种打不开的文件', () => {
    for (const name of REQUIRED) expect(FIXTURES, name).toHaveProperty(name)
    expect(fixture('error-unsupported').error?.code).toBe('unsupported_format')
    expect(fixture('error-corrupted').error?.code).toBe('corrupted')
    expect(fixture('error-unreadable').error?.code).toBe('unreadable')
  })

  it.each(documents)('%s 的 document 与协议文档里的 EmailDocument 结构一致', (_name, file) => {
    const doc = file.document!
    for (const key of ['from', 'to', 'cc', 'bcc', 'replyTo', 'attachments'] as const) expect(Array.isArray(doc[key]), key).toBe(true)
    for (const key of ['isSigned', 'isEncrypted', 'hasRemoteContent'] as const) expect(typeof doc[key], key).toBe('boolean')
    expect(['None', 'Html', 'Rtf', 'PlainText']).toContain(doc.body.sourceFormat)
    expect(doc.body.html === null || typeof doc.body.html === 'string').toBe(true)
    expect(doc.sentAtUtc === null || /^\d{4}-\d\d-\d\dT[\d:]+Z$/.test(doc.sentAtUtc)).toBe(true)
    for (const a of doc.attachments) {
      expect(typeof a.id).toBe('string')
      expect(typeof a.size).toBe('number')
      expect(typeof a.isNestedEmail).toBe('boolean')
    }
  })

  it('嵌套邮件的 fixture 覆盖每个嵌套附件，两层嵌套都有', () => {
    const depth = (file: MockFile): number => {
      const nestedIds = file.document!.attachments.filter((a) => a.isNestedEmail).map((a) => a.id)
      for (const id of nestedIds) expect(file.nested?.[id], `${file.fileName} 的附件 ${id}`).toBeDefined()
      return 1 + Math.max(0, ...Object.values(file.nested ?? {}).map((child) => (child.document ? depth(child) : 0)))
    }
    expect(depth(fixture('nested-two-levels'))).toBe(3)
    expect(depth(fixture('nested-msg-two-levels'))).toBe(3)
  })

  it('样本的关键特征与 samples/README.md 一致', () => {
    expect(fixture('marketing-remote').document!.hasRemoteContent).toBe(true)
    expect(fixture('signed').document!.isSigned).toBe(true)
    expect(fixture('signed-opaque').document!.isSigned).toBe(true)
    expect(fixture('encrypted').document).toMatchObject({ isEncrypted: true, body: { sourceFormat: 'None', html: null } })
    expect(fixture('rtf-only').document!.body.sourceFormat).toBe('Rtf')
    expect(fixture('plain-text').document!.body.sourceFormat).toBe('PlainText')
    expect(fixture('charset-gbk').document!.subject).toBe('测试邮件：GBK 编码的中文主题')
    expect(fixture('cid-inline').document!.attachments.map((a) => a.fileName)).toEqual(['quarterly-report.docx'])
    expect(fixture('winmail').document!.attachments.map((a) => a.fileName)).toEqual(['meeting-minutes.docx'])
    expect(fixture('attachments-unsafe-names').document!.attachments).toHaveLength(7)
    expect(fixture('headers-timezone').document!.sentAtUtc).toBe('2026-10-05T16:00:00Z')
    expect(fixture('headers-timezone').document!.dateHeaderRaw).toBe('Mon, 5 Oct 2026 09:00:00 -0700')
  })

  it('恶意 HTML 样本经 sidecar 处理后不含脚本和事件属性', () => {
    const html = fixture('html-malicious').document!.body.html!
    expect(html).not.toMatch(/<script/i)
    expect(html).not.toMatch(/\son\w+\s*=/i)
    expect(html).not.toMatch(/href\s*=\s*["']?\s*javascript:/i)
  })
})
