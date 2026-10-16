import { fireEvent, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { AttachmentList, type AttachmentLabels, type AttachmentView } from '../Attachments.tsx'
import { FileName } from '../FileName.tsx'
import { formatFileSize, splitFileName } from '../format.ts'

const labels: AttachmentLabels = {
  heading: 'Attachments',
  summary: '3 files · 5.2 MB',
  saveAll: 'Save all…',
  open: 'Open',
  openMessageHint: 'Opens here',
  save: 'Save…',
  saveNamed: (n) => `Save ${n}…`,
  saveOnly: 'Save only',
  messageKind: 'Email',
  showAll: (n) => `Show all ${n}`,
  showFewer: 'Show fewer',
}

const ATTS: AttachmentView[] = [
  { id: 'pdf', fileName: 'report.pdf', size: 5033165, kind: 'file' },
  { id: 'msg', fileName: 'RE: Pallet delivery dates.msg', size: 47104, kind: 'message' },
  { id: 'exe', fileName: 'setup.exe', size: 14889779, kind: 'executable' },
]

function setup(attachments = ATTS) {
  const cb = { onOpen: vi.fn(), onSave: vi.fn(), onSaveAll: vi.fn(), onOpenMessage: vi.fn(), onBlockedOpen: vi.fn() }
  render(<AttachmentList attachments={attachments} labels={labels} {...cb} />)
  return cb
}

describe('AttachmentList', () => {
  it('没有附件时不渲染', () => {
    const { container } = render(
      <AttachmentList attachments={[]} labels={labels} onOpen={() => {}} onSave={() => {}} onSaveAll={() => {}} onOpenMessage={() => {}} onBlockedOpen={() => {}} />,
    )
    expect(container).toBeEmptyDOMElement()
  })

  it('标题命名区域，列出文件名和大小', () => {
    setup()
    const region = screen.getByRole('region', { name: 'Attachments' })
    expect(within(region).getAllByRole('listitem')).toHaveLength(3)
    expect(screen.getByText('3 files · 5.2 MB')).toBeInTheDocument()
    expect(screen.getByText('4.8 MB')).toBeInTheDocument()
    expect(screen.getByText(/Email · 46 KB/)).toBeInTheDocument()
  })

  it('打开、保存、全部保存都回调对应 id', async () => {
    const cb = setup()
    await userEvent.click(within(screen.getByTitle('report.pdf')).getByRole('button', { name: 'Open' }))
    expect(cb.onOpen).toHaveBeenCalledWith('pdf')
    await userEvent.click(screen.getByRole('button', { name: 'Save report.pdf…' }))
    expect(cb.onSave).toHaveBeenCalledWith('pdf')
    await userEvent.click(screen.getByRole('button', { name: 'Save all…' }))
    expect(cb.onSaveAll).toHaveBeenCalledOnce()
  })

  it('嵌套邮件在应用内打开', async () => {
    const cb = setup()
    const item = screen.getByTitle('RE: Pallet delivery dates.msg')
    await userEvent.click(within(item).getByRole('button', { name: 'Open' }))
    expect(cb.onOpenMessage).toHaveBeenCalledWith('msg')
    expect(cb.onOpen).not.toHaveBeenCalled()
  })

  it('可执行附件没有“打开”按钮，只能保存；回车或双击触发被拒绝的回调', async () => {
    const cb = setup()
    const item = screen.getByRole('listitem', { name: 'setup.exe, Save only' })
    expect(within(item).queryByRole('button', { name: 'Open' })).not.toBeInTheDocument()
    expect(within(item).getByRole('button', { name: 'Save setup.exe…' })).toBeInTheDocument()
    expect(item).toHaveTextContent('Save only')
    item.focus()
    await userEvent.keyboard('{Enter}')
    expect(cb.onBlockedOpen).toHaveBeenCalledWith('exe')
    fireEvent.doubleClick(item)
    expect(cb.onBlockedOpen).toHaveBeenCalledTimes(2)
    expect(cb.onOpen).not.toHaveBeenCalled()
  })

  it('在保存按钮上按回车只保存，不触发卡片的默认动作', async () => {
    const cb = setup()
    screen.getByRole('button', { name: 'Save setup.exe…' }).focus()
    await userEvent.keyboard('{Enter}')
    expect(cb.onSave).toHaveBeenCalledWith('exe')
    expect(cb.onBlockedOpen).not.toHaveBeenCalled()
  })

  it('只有 1 个附件时不显示“全部保存”', () => {
    setup([ATTS[0]])
    expect(screen.queryByRole('button', { name: 'Save all…' })).not.toBeInTheDocument()
  })

  it('超过 6 个时先显示 6 个，可展开全部', async () => {
    const many = Array.from({ length: 9 }, (_, i): AttachmentView => ({ id: `f${i}`, fileName: `file-${i}.txt`, size: 10, kind: 'file' }))
    setup(many)
    expect(screen.getAllByRole('listitem')).toHaveLength(6)
    const toggle = screen.getByRole('button', { name: 'Show all 9' })
    expect(toggle).toHaveAttribute('aria-expanded', 'false')
    await userEvent.click(toggle)
    expect(screen.getAllByRole('listitem')).toHaveLength(9)
    expect(screen.getByRole('button', { name: 'Show fewer' })).toBeInTheDocument()
  })
})

describe('FileName / format', () => {
  it('扩展名单独成段，主体可截断', () => {
    const { container } = render(<FileName name="Floor plan rev C (ground floor).pdf" />)
    expect(container.querySelector('.fname__stem')).toHaveTextContent('Floor plan rev C (ground floor)')
    expect(container.querySelector('.fname__ext')).toHaveTextContent('.pdf')
  })

  it('splitFileName 处理没有扩展名和过长扩展名', () => {
    expect(splitFileName('README')).toEqual(['README', ''])
    expect(splitFileName('.bashrc')).toEqual(['.bashrc', ''])
    expect(splitFileName('archive.verylongext')).toEqual(['archive.verylongext', ''])
    expect(splitFileName('a.tar.gz')).toEqual(['a.tar', '.gz'])
  })

  it('formatFileSize 按 1024 进制', () => {
    expect(formatFileSize(512)).toBe('512 B')
    expect(formatFileSize(90112)).toBe('88 KB')
    expect(formatFileSize(5033165)).toBe('4.8 MB')
    expect(formatFileSize(14889779)).toBe('14 MB')
  })
})
