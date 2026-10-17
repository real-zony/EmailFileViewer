import { describe, expect, it } from 'vitest'
import type { EmailDocument } from '../../api/types.ts'
import { fixture } from '../../dev/fixtures.ts'
import type { ErrorTab, Level, MailTab } from '../store.ts'
import { APP_TITLE, windowTitle } from '../title.ts'

const base: EmailDocument = fixture('headers-normal').document!

function level(handle: number, fileName: string, subject: string | null): Level {
  return { handle, fileName, document: { ...base, subject }, remoteAllowed: false }
}

function mailTab(...trail: Level[]): MailTab {
  return { kind: 'mail', id: `m${trail[0].handle}`, trail }
}

describe('windowTitle（D11 补充：窗口标题跟随当前邮件）', () => {
  it('空状态只有应用名', () => {
    expect(APP_TITLE).toBe('EmailFileViewer')
    expect(windowTitle(null)).toBe('EmailFileViewer')
  })

  it('有邮件时是“主题 — EmailFileViewer”', () => {
    expect(windowTitle(mailTab(level(1, 'a.eml', 'Quarterly planning')))).toBe('Quarterly planning — EmailFileViewer')
  })

  it('没有主题（null、空串、只有空白）时用文件名，文件名保留扩展名', () => {
    for (const subject of [null, '', '   \n\t ']) {
      expect(windowTitle(mailTab(level(1, 'invoice.msg', subject)))).toBe('invoice.msg — EmailFileViewer')
    }
  })

  it('主题里的换行和连续空白折成一个空格，标题只有一行', () => {
    expect(windowTitle(mailTab(level(1, 'a.eml', '  Re:\r\n  hello \t world  ')))).toBe('Re: hello world — EmailFileViewer')
  })

  it('嵌套邮件：标题跟随当前这一层，回到上一层就换回去', () => {
    const outer = level(1, 'outer.eml', 'Outer subject')
    const inner = level(2, 'inner.eml', 'Inner subject')
    expect(windowTitle(mailTab(outer, inner))).toBe('Inner subject — EmailFileViewer')
    expect(windowTitle(mailTab(outer))).toBe('Outer subject — EmailFileViewer')
  })

  it('嵌套邮件没有主题：用这一层的文件名（附件名）；附件也没有名字才用最外层文件的名字', () => {
    const outer = level(1, 'outer.eml', 'Outer subject')
    expect(windowTitle(mailTab(outer, level(2, 'forwarded.eml', null)))).toBe('forwarded.eml — EmailFileViewer')
    expect(windowTitle(mailTab(outer, level(2, '', null)))).toBe('outer.eml — EmailFileViewer')
  })

  it('打开失败的标签页用它的文件名', () => {
    const failed: ErrorTab = { kind: 'error', id: 'e1', fileName: 'truncated.msg', code: 'corrupted' }
    expect(windowTitle(failed)).toBe('truncated.msg — EmailFileViewer')
  })

  it('什么名字都没有时退回应用名，不留下孤零零的分隔符', () => {
    expect(windowTitle(mailTab(level(1, '', null)))).toBe('EmailFileViewer')
    expect(windowTitle({ kind: 'error', id: 'e1', fileName: '  ', code: 'corrupted' })).toBe('EmailFileViewer')
  })
})
