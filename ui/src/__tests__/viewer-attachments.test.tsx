// 任务 7.7：email-attachments 规格里面向界面的场景（列出、保存、全部保存、打开、可执行附件、嵌套邮件、winmail.dat）
import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { ApiError } from '../api/types.ts'
import { fixture } from '../dev/fixtures.ts'
import { callsOf, deliver, startApp, toastRegion, withDocument } from '../test/viewerHarness.tsx'

const attachmentCard = (fileName: string) => screen.getByTitle(fileName) as HTMLElement
const region = () => screen.getByRole('region', { name: /^(Notifications|通知)$/ })

describe('7.7 列出附件', () => {
  it('场景“带两个附件的邮件”：显示每个附件的文件名和大小，以及总数和总大小', async () => {
    const { api } = await startApp()
    await deliver(api, 'attachments-multi')
    expect(attachmentCard('report.pdf')).toHaveTextContent('597 B')
    expect(attachmentCard('budget.xlsx')).toHaveTextContent('2 KB')
    expect(attachmentCard('photo.jpg')).toHaveTextContent('3 KB')
    expect(screen.getByRole('heading', { name: 'Attachments' })).toBeInTheDocument()
    expect(screen.getByText('3 files · 6 KB')).toBeInTheDocument()
  })

  it('只有一个附件时用单数摘要，并且不显示“全部保存”', async () => {
    const { api } = await startApp()
    await deliver(api, 'cid-inline')
    expect(screen.getByText('1 file · 1 KB')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Save all…' })).not.toBeInTheDocument()
  })

  it('场景“内嵌图片不重复列出”：正文用 cid: 引用的图片不在附件列表里，只有 docx', async () => {
    const { api } = await startApp()
    await deliver(api, 'cid-inline')
    expect([...document.querySelectorAll('.att')].map((el) => el.getAttribute('title'))).toEqual(['quarterly-report.docx'])
  })

  it('没有附件的邮件不显示附件区', async () => {
    const { api } = await startApp()
    await deliver(api, 'headers-normal')
    expect(screen.queryByRole('heading', { name: 'Attachments' })).not.toBeInTheDocument()
  })

  it('场景“展开 winmail.dat”：列出的是 docx，可以保存和打开', async () => {
    const { api, user } = await startApp()
    await deliver(api, 'winmail')
    expect(screen.queryByTitle('winmail.dat')).not.toBeInTheDocument()
    expect(attachmentCard('meeting-minutes.docx')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Open' }))
    expect(callsOf(api, 'open_attachment')).toEqual([{ command: 'open_attachment', args: { handle: 1, attachmentId: '2.t1' } }])
    await user.click(screen.getByRole('button', { name: 'Save meeting-minutes.docx…' }))
    expect(callsOf(api, 'save_attachment')).toEqual([{ command: 'save_attachment', args: { handle: 1, attachmentId: '2.t1' } }])
  })

  it('不安全的文件名原样显示（清理发生在保存时）；超过 6 个时先显示 6 个，可以展开', async () => {
    const { api, user } = await startApp()
    await deliver(api, 'attachments-unsafe-names')
    expect(document.querySelectorAll('.att')).toHaveLength(6)
    expect(screen.getByTitle('../../.bashrc')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Show all 7' }))
    expect(document.querySelectorAll('.att')).toHaveLength(7)
    expect(screen.getByTitle('x<y>z|w.txt')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Show fewer' }))
    expect(document.querySelectorAll('.att')).toHaveLength(6)
  })

  it('没有文件名的附件显示占位名', async () => {
    const { api } = await startApp()
    await deliver(api, withDocument('attachments-multi', { attachments: [{ id: 'x', fileName: null, size: 5, mimeType: null, isNestedEmail: false }] }))
    expect(screen.getByTitle('Unnamed attachment')).toBeInTheDocument()
  })

  it('中文界面：附件区文案', async () => {
    const { api } = await startApp({ language: 'zh-CN' })
    await deliver(api, 'attachments-multi')
    expect(screen.getByRole('heading', { name: '附件' })).toBeInTheDocument()
    expect(screen.getByText('3 个文件 · 6 KB')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '全部保存…' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '保存 report.pdf…' })).toBeInTheDocument()
  })
})

describe('7.7 保存单个附件', () => {
  it('场景“保存 PDF 附件”：调用 save_attachment（只传 handle 和 attachmentId），用户确认后不显示任何提示', async () => {
    const { api, user } = await startApp()
    await deliver(api, 'attachments-multi')
    await user.click(screen.getByRole('button', { name: 'Save report.pdf…' }))
    expect(callsOf(api, 'save_attachment')).toEqual([{ command: 'save_attachment', args: { handle: 1, attachmentId: '2' } }])
    expect(toastRegion()).not.toBeInTheDocument()
  })

  it('用户取消保存对话框（返回 null）：什么都不显示', async () => {
    const { api, user } = await startApp()
    await deliver(api, 'attachments-multi')
    api.override('saveAttachment', async () => null)
    await user.click(screen.getByRole('button', { name: 'Save budget.xlsx…' }))
    expect(callsOf(api, 'save_attachment')).toHaveLength(1)
    expect(toastRegion()).not.toBeInTheDocument()
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument()
  })

  it('保存失败：toast 提示文件名和原因，可以关闭', async () => {
    const { api, user } = await startApp()
    await deliver(api, 'attachments-multi')
    api.failNext('saveAttachment', 'write_failed')
    await user.click(screen.getByRole('button', { name: 'Save report.pdf…' }))
    const toasts = await screen.findByRole('region', { name: 'Notifications' })
    expect(within(toasts).getByText("Couldn't save report.pdf")).toBeInTheDocument()
    expect(within(toasts).getByText(/Check that the folder exists/)).toBeInTheDocument()
    await user.click(within(toasts).getByRole('button', { name: 'Dismiss' }))
    expect(toastRegion()).not.toBeInTheDocument()
  })

  it('保存失败的原因按错误码区分：文档已关闭、未知错误码', async () => {
    const { api, user } = await startApp()
    await deliver(api, 'attachments-multi')
    api.failNext('saveAttachment', 'document_not_found')
    await user.click(screen.getByRole('button', { name: 'Save report.pdf…' }))
    expect(await screen.findByText(/no longer open/)).toBeInTheDocument()
    api.failNext('saveAttachment', 'weird_code')
    await user.click(screen.getByRole('button', { name: 'Save report.pdf…' }))
    expect(await screen.findByText('Something went wrong. Try again.')).toBeInTheDocument()
    expect(within(region()).getAllByRole('status')).toHaveLength(2)
  })

  it('保存超时：提示“操作超时，文件可能仍在保存”，不说文件已损坏', async () => {
    const { api, user } = await startApp()
    await deliver(api, 'attachments-multi')
    api.failNext('saveAttachment', 'timeout')
    await user.click(screen.getByRole('button', { name: 'Save report.pdf…' }))
    const toasts = await screen.findByRole('region', { name: 'Notifications' })
    expect(within(toasts).getByText("Couldn't save report.pdf")).toBeInTheDocument()
    expect(within(toasts).getByText('The operation timed out. The file may still be saving, so check the destination.')).toBeInTheDocument()
    expect(toasts.textContent).not.toMatch(/damaged/)
  })

  it('中文界面：保存超时的提示', async () => {
    const { api, user } = await startApp({ language: 'zh-CN' })
    await deliver(api, 'attachments-multi')
    api.failNext('saveAttachment', 'timeout')
    await user.click(screen.getByRole('button', { name: '保存 report.pdf…' }))
    expect(await screen.findByText('操作超时，文件可能仍在保存，请检查目标位置。')).toBeInTheDocument()
  })

  it('中文界面：保存失败的提示', async () => {
    const { api, user } = await startApp({ language: 'zh-CN' })
    await deliver(api, 'attachments-multi')
    api.failNext('saveAttachment', 'write_failed')
    await user.click(screen.getByRole('button', { name: '保存 report.pdf…' }))
    expect(await screen.findByText('无法保存 report.pdf')).toBeInTheDocument()
  })
})

describe('7.7 保存全部附件', () => {
  it('调用 save_all_attachments（只传 handle）；全部成功后用 toast 汇总', async () => {
    const { api, user } = await startApp()
    await deliver(api, 'attachments-multi')
    await user.click(screen.getByRole('button', { name: 'Save all…' }))
    expect(callsOf(api, 'save_all_attachments')).toEqual([{ command: 'save_all_attachments', args: { handle: 1 } }])
    expect(within(await screen.findByRole('region', { name: 'Notifications' })).getByText('Saved 3 attachments')).toBeInTheDocument()
  })

  it('部分失败：汇总里写明保存了几个、总共几个，并列出失败的文件和原因', async () => {
    const { api, user } = await startApp()
    await deliver(api, 'attachments-multi')
    api.override('saveAllAttachments', async () => ({
      saved: [{ attachmentId: '2', path: '/x/report.pdf' }, { attachmentId: '4', path: '/x/photo.jpg' }],
      failed: [{ attachmentId: '3', code: 'write_failed' }],
    }))
    await user.click(screen.getByRole('button', { name: 'Save all…' }))
    const toasts = await screen.findByRole('region', { name: 'Notifications' })
    expect(within(toasts).getByText('Saved 2 of 3 attachments')).toBeInTheDocument()
    expect(toasts.textContent).toContain('budget.xlsx')
    expect(toasts.textContent).toContain('Check that the folder exists')
  })

  it('全部失败：汇总写 0 个', async () => {
    const { api, user } = await startApp()
    await deliver(api, 'attachments-multi')
    api.override('saveAllAttachments', async () => ({ saved: [], failed: [{ attachmentId: '2', code: 'write_failed' }, { attachmentId: '3', code: 'write_failed' }] }))
    await user.click(screen.getByRole('button', { name: 'Save all…' }))
    expect(await screen.findByText('Saved 0 of 2 attachments')).toBeInTheDocument()
  })

  it('只保存了一个时用单数', async () => {
    const { api, user } = await startApp()
    await deliver(api, 'attachments-multi')
    api.override('saveAllAttachments', async () => ({ saved: [{ attachmentId: '2', path: '/x/a' }], failed: [] }))
    await user.click(screen.getByRole('button', { name: 'Save all…' }))
    expect(await screen.findByText('Saved 1 attachment')).toBeInTheDocument()
  })

  it('用户取消选择文件夹的对话框（返回 null）：什么都不显示', async () => {
    const { api, user } = await startApp()
    await deliver(api, 'attachments-multi')
    api.override('saveAllAttachments', async () => null)
    await user.click(screen.getByRole('button', { name: 'Save all…' }))
    expect(callsOf(api, 'save_all_attachments')).toHaveLength(1)
    expect(toastRegion()).not.toBeInTheDocument()
  })

  it('整个请求失败（例如文件夹不存在）：toast 提示', async () => {
    const { api, user } = await startApp()
    await deliver(api, 'attachments-multi')
    api.failNext('saveAllAttachments', 'write_failed')
    await user.click(screen.getByRole('button', { name: 'Save all…' }))
    const toasts = await screen.findByRole('region', { name: 'Notifications' })
    expect(within(toasts).getByText("Couldn't save the attachments")).toBeInTheDocument()
  })

  it('整个请求超时：提示“操作超时，文件可能仍在保存”', async () => {
    const { api, user } = await startApp()
    await deliver(api, 'attachments-multi')
    api.failNext('saveAllAttachments', 'timeout')
    await user.click(screen.getByRole('button', { name: 'Save all…' }))
    const toasts = await screen.findByRole('region', { name: 'Notifications' })
    expect(within(toasts).getByText("Couldn't save the attachments")).toBeInTheDocument()
    expect(within(toasts).getByText('The operation timed out. The file may still be saving, so check the destination.')).toBeInTheDocument()
  })

  it('部分失败且原因是超时：汇总里同样提示“操作超时”', async () => {
    const { api, user } = await startApp()
    await deliver(api, 'attachments-multi')
    api.override('saveAllAttachments', async () => ({
      saved: [{ attachmentId: '2', path: '/x/report.pdf' }],
      failed: [{ attachmentId: '3', code: 'timeout' }],
    }))
    await user.click(screen.getByRole('button', { name: 'Save all…' }))
    const toasts = await screen.findByRole('region', { name: 'Notifications' })
    expect(within(toasts).getByText('Saved 1 of 2 attachments')).toBeInTheDocument()
    expect(toasts.textContent).toContain('The operation timed out.')
    expect(toasts.textContent).not.toMatch(/damaged/)
  })

  it('中文界面：全部保存超时的提示', async () => {
    const { api, user } = await startApp({ language: 'zh-CN' })
    await deliver(api, 'attachments-multi')
    api.failNext('saveAllAttachments', 'timeout')
    await user.click(screen.getByRole('button', { name: '全部保存…' }))
    expect(await screen.findByText('操作超时，文件可能仍在保存，请检查目标位置。')).toBeInTheDocument()
  })

  it('中文界面：汇总', async () => {
    const { api, user } = await startApp({ language: 'zh-CN' })
    await deliver(api, 'attachments-multi')
    await user.click(screen.getByRole('button', { name: '全部保存…' }))
    expect(await screen.findByText('已保存 3 个附件')).toBeInTheDocument()
    api.override('saveAllAttachments', async () => ({ saved: [{ attachmentId: '2', path: '/x' }], failed: [{ attachmentId: '3', code: 'write_failed' }] }))
    await user.click(screen.getByRole('button', { name: '全部保存…' }))
    expect(await screen.findByText('已保存 2 个附件中的 1 个')).toBeInTheDocument()
  })
})

describe('7.7 用系统默认程序打开附件', () => {
  it('场景“打开图片附件”：点“打开”调用 open_attachment（只传 handle 和 attachmentId），成功后不显示任何提示', async () => {
    const { api, user } = await startApp()
    await deliver(api, 'attachments-multi')
    await user.click(within(attachmentCard('photo.jpg')).getByRole('button', { name: 'Open' }))
    expect(callsOf(api, 'open_attachment')).toEqual([{ command: 'open_attachment', args: { handle: 1, attachmentId: '4' } }])
    expect(toastRegion()).not.toBeInTheDocument()
  })

  it('双击普通附件卡片也是打开', async () => {
    const { api } = await startApp()
    await deliver(api, 'attachments-multi')
    fireEvent.doubleClick(attachmentCard('report.pdf'))
    expect(callsOf(api, 'open_attachment')).toEqual([{ command: 'open_attachment', args: { handle: 1, attachmentId: '2' } }])
  })

  it('打开失败（例如写入临时目录失败）：toast 提示', async () => {
    const { api, user } = await startApp()
    await deliver(api, 'attachments-multi')
    api.failNext('openAttachment', 'write_failed')
    await user.click(within(attachmentCard('report.pdf')).getByRole('button', { name: 'Open' }))
    expect(await screen.findByText("Couldn't open report.pdf")).toBeInTheDocument()
  })

  it('打开附件超时（open_attachment 先把附件提取出来）：提示“打开附件超时，请重试”，不提目标位置，也不说文件已损坏', async () => {
    const { api, user } = await startApp()
    await deliver(api, 'attachments-multi')
    api.failNext('openAttachment', 'timeout')
    await user.click(within(attachmentCard('report.pdf')).getByRole('button', { name: 'Open' }))
    expect(await screen.findByText("Couldn't open report.pdf")).toBeInTheDocument()
    expect(screen.getByText('Opening the attachment timed out. Try again.')).toBeInTheDocument()
    expect(screen.queryByText(/destination/)).not.toBeInTheDocument()
    expect(screen.queryByText(/damaged/)).not.toBeInTheDocument()
  })

  it('中文界面：打开附件超时的提示', async () => {
    const { api, user } = await startApp({ language: 'zh-CN' })
    await deliver(api, 'attachments-multi')
    api.failNext('openAttachment', 'timeout')
    await user.click(within(attachmentCard('report.pdf')).getByRole('button', { name: '打开' }))
    expect(await screen.findByText('打开附件超时，请重试。')).toBeInTheDocument()
    expect(screen.queryByText(/目标位置/)).not.toBeInTheDocument()
  })
})

describe('7.7 场景“打开可执行附件”', () => {
  it('可执行附件没有“打开”按钮，显示“仅可保存”；每个都能保存', async () => {
    const { api } = await startApp()
    await deliver(api, 'attachments-executables')
    expect(screen.getAllByText('Save only')).toHaveLength(6)
    expect(screen.queryByRole('button', { name: 'Open' })).not.toBeInTheDocument()
    for (const name of ['installer.exe', 'run-me.bat', 'setup.ps1', 'install.sh', 'launcher.command', 'Tool.app']) {
      expect(within(attachmentCard(name)).getByRole('button', { name: `Save ${name}…` })).toBeInTheDocument()
    }
  })

  it('双击可执行附件：弹出“无法在此打开”提示，不调用 open_attachment；初始焦点在“取消”上，没有“仍然打开”', async () => {
    const { api } = await startApp()
    await deliver(api, 'attachments-executables')
    fireEvent.doubleClick(attachmentCard('installer.exe'))
    const dialog = await screen.findByRole('alertdialog')
    expect(within(dialog).getByText('“installer.exe” can\'t be opened here')).toBeInTheDocument()
    expect(dialog).toHaveTextContent("Programs and scripts can change things on your computer")
    expect(within(dialog).getByRole('button', { name: 'Cancel' })).toHaveFocus()
    expect(within(dialog).queryByRole('button', { name: /open/i })).not.toBeInTheDocument()
    expect(callsOf(api, 'open_attachment')).toHaveLength(0)
  })

  it('在卡片上按回车同样弹出提示', async () => {
    const { api, user } = await startApp()
    await deliver(api, 'attachments-executables')
    attachmentCard('run-me.bat').focus()
    await user.keyboard('{Enter}')
    expect(await screen.findByRole('alertdialog')).toHaveTextContent('run-me.bat')
    expect(callsOf(api, 'open_attachment')).toHaveLength(0)
  })

  it('提示里的“保存…”调用 save_attachment 并关掉提示；“取消”只关掉提示', async () => {
    const { api, user } = await startApp()
    await deliver(api, 'attachments-executables')
    fireEvent.doubleClick(attachmentCard('installer.exe'))
    await user.click(within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Cancel' }))
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument()
    expect(callsOf(api, 'save_attachment')).toHaveLength(0)

    fireEvent.doubleClick(attachmentCard('installer.exe'))
    await user.click(within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Save…' }))
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument()
    expect(callsOf(api, 'save_attachment')).toEqual([{ command: 'save_attachment', args: { handle: 1, attachmentId: '2' } }])
  })

  it('提示里保存时用户取消保存对话框：什么都不显示', async () => {
    const { api, user } = await startApp()
    await deliver(api, 'attachments-executables')
    api.override('saveAttachment', async () => null)
    fireEvent.doubleClick(attachmentCard('setup.ps1'))
    await user.click(within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Save…' }))
    expect(toastRegion()).not.toBeInTheDocument()
  })

  it('Esc 关闭提示', async () => {
    const { api, user } = await startApp()
    await deliver(api, 'attachments-executables')
    fireEvent.doubleClick(attachmentCard('install.sh'))
    await screen.findByRole('alertdialog')
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument()
  })

  it('Rust 返回 executable_refused（界面没把它认作可执行文件，比如清单之外的扩展名）时同样弹出提示', async () => {
    const { api, user } = await startApp()
    await deliver(api, withDocument('attachments-multi', { attachments: [{ id: 'x1', fileName: 'strange.zzz', size: 10, mimeType: null, isNestedEmail: false }] }))
    api.override('openAttachment', async () => {
      throw new ApiError('executable_refused')
    })
    await user.click(screen.getByRole('button', { name: 'Open' }))
    expect(callsOf(api, 'open_attachment')).toEqual([{ command: 'open_attachment', args: { handle: 1, attachmentId: 'x1' } }])
    expect(await screen.findByRole('alertdialog')).toHaveTextContent('strange.zzz')
    await user.click(within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Save…' }))
    expect(callsOf(api, 'save_attachment')).toEqual([{ command: 'save_attachment', args: { handle: 1, attachmentId: 'x1' } }])
  })

  it('扩展名判断不区分大小写，只看最后一个扩展名（invoice.pdf.exe 是可执行文件，a.exe.txt 不是）', async () => {
    const { api } = await startApp()
    const attachments = ['INSTALL.EXE', 'invoice.pdf.exe', 'a.exe.txt', 'tool.jar', 'disk.iso'].map((fileName, i) => ({
      id: String(i), fileName, size: 1, mimeType: null, isNestedEmail: false,
    }))
    await deliver(api, withDocument('attachments-multi', { attachments }))
    expect(within(attachmentCard('INSTALL.EXE')).getByText('Save only')).toBeInTheDocument()
    expect(within(attachmentCard('invoice.pdf.exe')).getByText('Save only')).toBeInTheDocument()
    expect(within(attachmentCard('tool.jar')).getByText('Save only')).toBeInTheDocument()
    expect(within(attachmentCard('disk.iso')).getByText('Save only')).toBeInTheDocument()
    expect(within(attachmentCard('a.exe.txt')).queryByText('Save only')).not.toBeInTheDocument()
  })

  it('中文界面：提示文案', async () => {
    const { api } = await startApp({ language: 'zh-CN' })
    await deliver(api, 'attachments-executables')
    expect(screen.getAllByText('仅可保存')).toHaveLength(6)
    fireEvent.doubleClick(attachmentCard('installer.exe'))
    const dialog = await screen.findByRole('alertdialog')
    expect(within(dialog).getByText('无法在此打开“installer.exe”')).toBeInTheDocument()
    expect(within(dialog).getByRole('button', { name: '取消' })).toHaveFocus()
  })
})

describe('7.7 打开嵌套邮件', () => {
  it('嵌套邮件的附件显示“邮件”类型，“打开”调用 open_nested（不是 open_attachment）', async () => {
    const { api, user } = await startApp()
    await deliver(api, 'nested-two-levels')
    expect(attachmentCard('Level 2 (middle) message.eml')).toHaveTextContent('Email')
    await user.click(screen.getByRole('button', { name: 'Open' }))
    expect(callsOf(api, 'open_nested')).toEqual([{ command: 'open_nested', args: { handle: 1, attachmentId: '2' } }])
    expect(callsOf(api, 'open_attachment')).toHaveLength(0)
  })

  it('双击嵌套邮件卡片也是在应用内打开', async () => {
    const { api } = await startApp()
    await deliver(api, 'nested-two-levels')
    fireEvent.doubleClick(attachmentCard('Level 2 (middle) message.eml'))
    await waitFor(() => expect(callsOf(api, 'open_nested')).toHaveLength(1))
    expect(await screen.findByRole('heading', { level: 1, name: /Level 2/ })).toBeInTheDocument()
  })

  it('嵌套邮件里的附件也能保存和打开，句柄是这一层的', async () => {
    const { api, user } = await startApp()
    const inner = { fileName: 'inner.eml', document: fixture('attachments-multi').document! }
    await deliver(api, {
      ...fixture('nested-two-levels'),
      nested: { '2': inner },
    })
    await user.click(screen.getByRole('button', { name: 'Open' }))
    await screen.findByText('3 files · 6 KB')
    await user.click(screen.getByRole('button', { name: 'Save report.pdf…' }))
    await user.click(within(attachmentCard('budget.xlsx')).getByRole('button', { name: 'Open' }))
    const innerHandle = [...api.openHandles][1]
    expect(callsOf(api, 'save_attachment')[0].args).toEqual({ handle: innerHandle, attachmentId: '2' })
    expect(callsOf(api, 'open_attachment')[0].args).toEqual({ handle: innerHandle, attachmentId: '3' })
  })
})
