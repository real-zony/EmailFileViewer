// 任务 7.4（邮件头）、7.5（正文 iframe）、7.6（签名与加密）：email-viewing 和 app-appearance 规格里面向界面的场景
import { screen, within } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import { fixture } from '../dev/fixtures.ts'
import { BODY_CSP_BLOCKED, BODY_CSP_REMOTE, BODY_SANDBOX } from '../viewer/bodyDocument.ts'
import { address, bodyDoc, bodyFrame, deliver, headerOf, startApp, tabs, withDocument } from '../test/viewerHarness.tsx'

const originalTz = process.env.TZ
afterEach(() => {
  if (originalTz === undefined) delete process.env.TZ
  else process.env.TZ = originalTz
})

// 邮件头里一行的值（按标签找）
const rowValue = (label: string) => {
  const labelEl = headerOf().getByText(label, { selector: '.hlabel' })
  return labelEl.nextElementSibling as HTMLElement
}

describe('7.4 邮件头', () => {
  it('场景“普通邮件”：显示发件人、收件人、抄送、日期和主题，每个地址都有名称和邮箱地址', async () => {
    const { api } = await startApp()
    await deliver(api, 'headers-normal')
    expect(screen.getByRole('heading', { level: 1, name: 'Quarterly planning meeting' })).toBeInTheDocument()
    expect(rowValue('From')).toHaveTextContent('Alice Zhang')
    expect(rowValue('From')).toHaveTextContent('alice.zhang@example.com')
    expect(rowValue('To')).toHaveTextContent('Bob Li')
    expect(rowValue('To')).toHaveTextContent('bob.li@example.org')
    expect(rowValue('To')).toHaveTextContent('Carol Wang')
    expect(rowValue('To')).toHaveTextContent('carol.wang@example.net')
    expect(rowValue('Cc')).toHaveTextContent('Dave Chen')
    expect(rowValue('Cc')).toHaveTextContent('dave.chen@example.com')
    expect(headerOf().getByRole('time')).toBeInTheDocument()
  })

  it('每个地址的名称和邮箱分开显示（邮箱在等宽字体的元素里）', async () => {
    const { api } = await startApp()
    await deliver(api, 'headers-normal')
    const first = rowValue('To').querySelector('.addr') as HTMLElement
    expect(first.querySelector('.addr__name')).toHaveTextContent('Bob Li')
    expect(first.querySelector('.addr__email')).toHaveTextContent('bob.li@example.org')
  })

  it('场景“缺少某个邮件头”：没有抄送时不显示抄送栏，也不显示空白占位；有密送和回复至时照常显示', async () => {
    const { api } = await startApp()
    await deliver(api, 'headers-timezone')
    expect(headerOf().queryByText('Cc')).not.toBeInTheDocument()
    expect(rowValue('Bcc')).toHaveTextContent('Erin Xu')
    expect(rowValue('Bcc')).toHaveTextContent('erin.xu@example.org')
    expect(rowValue('Reply to')).toHaveTextContent('Alice Zhang (Support)')
    expect(rowValue('Reply to')).toHaveTextContent('support@example.com')
    // 每个标签后面都跟着内容，没有空的值单元格
    for (const label of headerOf().getAllByText(/^(From|To|Bcc|Reply to)$/, { selector: '.hlabel' })) {
      expect((label.nextElementSibling as HTMLElement).textContent?.trim()).not.toBe('')
    }
  })

  it('缺失的字段整行隐藏：没有发件人、没有日期、没有主题时，对应的行或标题不出现', async () => {
    const { api } = await startApp()
    await deliver(api, withDocument('headers-normal', { from: [], subject: null, sentAtUtc: null, dateHeaderRaw: null, cc: [], to: [] }))
    expect(screen.queryByRole('heading', { level: 1 })).not.toBeInTheDocument()
    expect(headerOf().queryByText('From')).not.toBeInTheDocument()
    expect(headerOf().queryByText('To')).not.toBeInTheDocument()
    expect(headerOf().queryByText('Date')).not.toBeInTheDocument()
    expect(headerOf().queryByRole('time')).not.toBeInTheDocument()
  })

  it('只有名称没有邮箱的地址（例如 undisclosed-recipients）也能显示；两项都没有的丢弃', async () => {
    const { api } = await startApp()
    await deliver(api, withDocument('headers-normal', { to: [address('undisclosed-recipients', null), address(null, null), address(null, 'x@example.com')] }))
    expect(rowValue('To').querySelectorAll('.addr')).toHaveLength(2)
    expect(rowValue('To')).toHaveTextContent('undisclosed-recipients')
    expect(rowValue('To')).toHaveTextContent('x@example.com')
  })

  it('多个发件人都显示', async () => {
    const { api } = await startApp()
    await deliver(api, withDocument('headers-normal', { from: [address('A One', 'a1@example.com'), address('B Two', 'b2@example.com')] }))
    expect(rowValue('From').querySelectorAll('.addr')).toHaveLength(2)
    expect(rowValue('From')).toHaveTextContent('B Two')
  })

  it('收件人很多时折叠：先显示 3 个，其余折叠成“+N more”，点开后可以收起', async () => {
    const { api, user } = await startApp()
    const many = Array.from({ length: 8 }, (_, i) => address(`Person ${i + 1}`, `p${i + 1}@example.com`))
    await deliver(api, withDocument('headers-normal', { to: many }))
    expect(rowValue('To').querySelectorAll('.addr')).toHaveLength(3)
    expect(rowValue('To')).not.toHaveTextContent('Person 4')
    await user.click(within(rowValue('To')).getByRole('button', { name: '+5 more' }))
    expect(rowValue('To').querySelectorAll('.addr')).toHaveLength(8)
    await user.click(within(rowValue('To')).getByRole('button', { name: 'Show less' }))
    expect(rowValue('To').querySelectorAll('.addr')).toHaveLength(3)
  })

  it('中文界面：标签是中文，折叠按钮是中文', async () => {
    const { api } = await startApp({ language: 'zh-CN' })
    const many = Array.from({ length: 5 }, (_, i) => address(`人${i + 1}`, `p${i + 1}@example.com`))
    await deliver(api, withDocument('headers-timezone', { to: many }))
    expect(rowValue('发件人')).toHaveTextContent('Alice Zhang')
    expect(rowValue('收件人')).toHaveTextContent('人1')
    expect(rowValue('密送')).toHaveTextContent('Erin Xu')
    expect(rowValue('回复至')).toHaveTextContent('support@example.com')
    expect(within(rowValue('收件人')).getByRole('button', { name: '另外 2 人' })).toBeInTheDocument()
  })

  it('字符集：GBK 和 RFC 2047 邮件头的主题、名称按 sidecar 解码后的文字显示', async () => {
    const { api } = await startApp()
    await deliver(api, 'charset-gbk')
    expect(screen.getByRole('heading', { level: 1, name: '测试邮件：GBK 编码的中文主题' })).toBeInTheDocument()
    expect(rowValue('From')).toHaveTextContent('张三')
    expect(bodyDoc()).toContain('您好，')
    expect(bodyDoc()).toContain('常用字：你好世界，邮件查看器，附件，发件人，收件人。')
  })
})

describe('7.4 场景“日期按本地时区显示：跨时区邮件”', () => {
  // Date: Mon, 5 Oct 2026 09:00:00 -0700 = 2026-10-05T16:00:00Z；用户系统时区为 UTC+8 -> 2026-10-06 00:00
  const dateCell = () => headerOf().getByRole('time')

  it('UTC+8、英文界面：Tue, Oct 6, 2026, 12:00 AM；悬停显示原始 Date 头', async () => {
    process.env.TZ = 'Asia/Shanghai'
    const { api } = await startApp({ language: 'en' })
    await deliver(api, 'headers-timezone')
    expect(dateCell()).toHaveTextContent('Tue, Oct 6, 2026, 12:00 AM')
    expect(dateCell()).toHaveAttribute('dateTime', '2026-10-05T16:00:00Z')
    expect(dateCell()).toHaveAttribute('title', 'Mon, 5 Oct 2026 09:00:00 -0700')
  })

  it('UTC+8、中文界面：2026年10月6日周二 00:00（按界面语言的日期格式）', async () => {
    process.env.TZ = 'Asia/Shanghai'
    const { api } = await startApp({ language: 'zh-CN' })
    await deliver(api, 'headers-timezone')
    expect(dateCell()).toHaveTextContent('2026年10月6日周二 00:00')
    expect(dateCell()).toHaveAttribute('title', 'Mon, 5 Oct 2026 09:00:00 -0700')
  })

  it('时区换成别的，显示的本地时间跟着变（说明用的是系统时区）', async () => {
    process.env.TZ = 'America/Los_Angeles'
    const { api } = await startApp({ language: 'en' })
    await deliver(api, 'headers-timezone')
    expect(dateCell()).toHaveTextContent('Mon, Oct 5, 2026, 9:00 AM')
    expect(dateCell()).toHaveAttribute('title', 'Mon, 5 Oct 2026 09:00:00 -0700')
  })

  it('没有 Date 头的邮件不显示日期行；日期无法解析时显示原始文字', async () => {
    const { api } = await startApp()
    await deliver(api, withDocument('headers-normal', { sentAtUtc: null, dateHeaderRaw: null }, 'nodate.eml'))
    expect(headerOf().queryByRole('time')).not.toBeInTheDocument()
    await deliver(api, withDocument('headers-normal', { sentAtUtc: null, dateHeaderRaw: 'sometime last week' }, 'rawdate.eml'))
    expect(headerOf().getByRole('time')).toHaveTextContent('sometime last week')
  })
})

describe('7.5 正文 iframe：沙箱、CSP、纸面', () => {
  it('iframe 的 sandbox 严格等于 "allow-popups allow-popups-to-escape-sandbox"，用 srcdoc 加载', async () => {
    const { api } = await startApp()
    await deliver(api, 'plain-text')
    const frame = bodyFrame()
    expect(frame.getAttribute('sandbox')).toBe('allow-popups allow-popups-to-escape-sandbox')
    expect(frame.getAttribute('sandbox')).toBe(BODY_SANDBOX)
    for (const forbidden of ['allow-scripts', 'allow-same-origin', 'allow-forms']) expect(frame.getAttribute('sandbox')).not.toContain(forbidden)
    expect(frame.hasAttribute('src')).toBe(false)
    expect(frame.getAttribute('referrerpolicy')).toBe('no-referrer')
    expect(frame).toHaveClass('sheet__frame')
    // 放在 BodySheet 接缝以下的插槽里，应用在 iframe 上没有叠加任何东西
    expect(frame.parentElement).toHaveClass('sheet__body')
    expect(frame.parentElement!.children).toHaveLength(1)
  })

  it('默认 CSP：meta 在 srcdoc 的最前面，字符串与 D5 一致', async () => {
    const { api } = await startApp()
    await deliver(api, 'marketing-remote')
    expect(bodyDoc().startsWith('<!doctype html><html><head><meta http-equiv="Content-Security-Policy" content="')).toBe(true)
    expect(bodyDoc()).toContain(`content="${BODY_CSP_BLOCKED}"`)
    expect(BODY_CSP_BLOCKED).toBe("default-src 'none'; img-src data:; style-src 'unsafe-inline'; font-src data:; base-uri 'none'; form-action 'none'")
    expect(bodyDoc()).not.toContain('https: http:')
  })

  it('srcdoc 原样包含 sidecar 已清理过的 HTML，前端不再改写它', async () => {
    const { api } = await startApp()
    for (const name of ['html-malicious', 'link-mismatch', 'cid-inline', 'segmented-body', 'rtf-only', 'plain-text', 'marketing-remote']) {
      await deliver(api, name)
      expect(bodyDoc(), name).toContain(fixture(name).document!.body.html!)
    }
  })

  it('邮件内容里的 CSP meta、base、事件属性残留，都改不了最前面的那条 CSP', async () => {
    const { api } = await startApp()
    const hostile = '<meta http-equiv="Content-Security-Policy" content="default-src *"><base href="https://evil.example/"><img src="https://evil.example/x.png" onerror="alert(1)">'
    await deliver(api, withDocument('headers-normal', { body: { sourceFormat: 'Html', html: hostile } }, 'hostile.eml'))
    const doc = new DOMParser().parseFromString(bodyDoc(), 'text/html')
    expect(doc.head.firstElementChild!.getAttribute('content')).toBe(BODY_CSP_BLOCKED)
    expect(bodyDoc()).toContain(hostile)
    expect(bodyDoc().indexOf(BODY_CSP_BLOCKED)).toBeLessThan(bodyDoc().indexOf(hostile))
  })

  it('没有远程内容的邮件不显示提示条', async () => {
    const { api } = await startApp()
    await deliver(api, 'plain-text')
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Load remote content' })).not.toBeInTheDocument()
  })

  it('场景“深色模式下查看纯文本邮件”：界面深色，正文以深色文字显示在浅色底上，文档和浅色模式下完全相同', async () => {
    const light = await startApp({ mode: 'light' })
    await deliver(light.api, 'plain-text')
    const lightDoc = bodyDoc()
    light.view.unmount()

    const dark = await startApp({ mode: 'dark' })
    await deliver(dark.api, 'plain-text')
    expect(document.documentElement).toHaveAttribute('data-mode', 'dark')
    expect(bodyDoc()).toBe(lightDoc)
    expect(bodyDoc()).toContain('color-scheme:light')
    expect(bodyDoc()).toContain('color:#1f2421')
    // 纸面颜色不在主题令牌里：iframe 的样式类固定为白底浅色，应用没有给它别的样式
    expect(bodyFrame().getAttribute('style')).toBeNull()
    expect(bodyFrame().parentElement).toHaveClass('sheet__body')
  })

  it('场景“深色模式下查看 HTML 邮件”：HTML 正文的文档也不随应用主题变化', async () => {
    const light = await startApp({ mode: 'light' })
    await deliver(light.api, 'marketing-remote')
    const lightDoc = bodyDoc()
    light.view.unmount()
    const dark = await startApp({ mode: 'dark' })
    await deliver(dark.api, 'marketing-remote')
    expect(bodyDoc()).toBe(lightDoc)
  })

  it('格式标签：HTML、纯文本、RTF、无正文', async () => {
    const { api } = await startApp()
    await deliver(api, 'marketing-remote')
    expect(screen.getByText('HTML')).toBeInTheDocument()
    await deliver(api, 'plain-text')
    expect(screen.getByText('Plain text')).toBeInTheDocument()
    await deliver(api, 'rtf-only')
    expect(screen.getByText('RTF')).toBeInTheDocument()
    await deliver(api, withDocument('headers-normal', { body: { sourceFormat: 'None', html: null } }, 'empty.eml'))
    expect(screen.getByText('No body')).toBeInTheDocument()
    expect(bodyDoc()).toContain('<body></body>')
  })

  it('正文区域的说明文字：邮件内容隔离显示，脚本已禁用', async () => {
    const { api } = await startApp()
    await deliver(api, 'plain-text')
    expect(screen.getByText('Message content · shown in isolation, scripts off')).toBeInTheDocument()
  })
})

describe('7.5 场景“远程内容默认不加载”', () => {
  const loadButton = () => screen.queryByRole('button', { name: 'Load remote content' })

  it('场景“营销邮件中的远程图片”：默认阻止，显示“远程内容已阻止”的提示和“加载”操作', async () => {
    const { api } = await startApp()
    await deliver(api, 'marketing-remote')
    const notice = screen.getByRole('status')
    expect(within(notice).getByText('Remote images and styles are blocked.')).toBeInTheDocument()
    expect(within(notice).getByText('This message only')).toBeInTheDocument()
    expect(loadButton()).toBeInTheDocument()
    expect(bodyDoc()).toContain(`content="${BODY_CSP_BLOCKED}"`)
    // 提示条在接缝以上（BodySheet 的 chrome 里），不在 iframe 所在的纸面里
    expect(notice.closest('.sheet__chrome')).not.toBeNull()
    expect(notice.closest('.sheet__body')).toBeNull()
  })

  it('场景“用户选择加载”：当前邮件放宽 CSP 并重新渲染；提示条收起为一行确认，没有“重新阻止”', async () => {
    const { api, user } = await startApp()
    await deliver(api, 'marketing-remote')
    const before = bodyFrame()
    await user.click(loadButton()!)
    expect(bodyDoc()).toContain(`content="${BODY_CSP_REMOTE}"`)
    expect(BODY_CSP_REMOTE).toBe(
      "default-src 'none'; img-src data: https: http:; style-src 'unsafe-inline' https: http:; font-src data: https: http:; base-uri 'none'; form-action 'none'",
    )
    expect(bodyFrame()).not.toBe(before) // 重新渲染（新的 iframe）
    expect(bodyFrame().getAttribute('sandbox')).toBe(BODY_SANDBOX) // sandbox 不变
    expect(screen.getByText('Remote content is loaded for this message. Other messages still block it.')).toBeInTheDocument()
    expect(loadButton()).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /block/i })).not.toBeInTheDocument()
    expect(within(screen.getByRole('status')).queryAllByRole('button')).toHaveLength(0)
    // 只放宽了 CSP，正文还是 sidecar 的 HTML
    expect(bodyDoc()).toContain(fixture('marketing-remote').document!.body.html!)
  })

  it('场景“用户选择加载 / 随后打开的其他邮件仍然默认阻止”：其他标签页、之后新开的文档都仍然阻止；切回来保持已加载', async () => {
    const { api, user } = await startApp()
    await deliver(api, 'marketing-remote')
    await deliver(api, 'marketing-remote') // 第二个标签页（同一个文件再打开一次也是新文档）
    expect(tabs()).toHaveLength(2)
    // 第一个标签页：加载
    await user.click(tabs()[0])
    await user.click(loadButton()!)
    expect(bodyDoc()).toContain(BODY_CSP_REMOTE)
    // 第二个标签页：仍然阻止
    await user.click(tabs()[1])
    expect(bodyDoc()).toContain(`content="${BODY_CSP_BLOCKED}"`)
    expect(bodyDoc()).not.toContain(BODY_CSP_REMOTE)
    expect(loadButton()).toBeInTheDocument()
    // 切回第一个标签页：保持已加载
    await user.click(tabs()[0])
    expect(bodyDoc()).toContain(BODY_CSP_REMOTE)
    expect(screen.getByText(/Remote content is loaded for this message/)).toBeInTheDocument()
    // 之后新开的文档仍然阻止
    await deliver(api, 'marketing-remote')
    expect(tabs()).toHaveLength(3)
    expect(bodyDoc()).toContain(`content="${BODY_CSP_BLOCKED}"`)
    expect(loadButton()).toBeInTheDocument()
  })

  it('关掉标签页再打开就恢复阻止', async () => {
    const { api, user } = await startApp()
    await deliver(api, 'marketing-remote')
    await user.click(loadButton()!)
    expect(bodyDoc()).toContain(BODY_CSP_REMOTE)
    await user.keyboard('{Control>}w{/Control}')
    await deliver(api, 'marketing-remote')
    expect(bodyDoc()).toContain(`content="${BODY_CSP_BLOCKED}"`)
    expect(loadButton()).toBeInTheDocument()
  })

  it('中文界面：提示条文案', async () => {
    const { api, user } = await startApp({ language: 'zh-CN' })
    await deliver(api, 'marketing-remote')
    expect(screen.getByText('已阻止远程图片和样式。')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: '加载远程内容' }))
    expect(screen.getByText('已为此邮件加载远程内容。其他邮件仍默认阻止。')).toBeInTheDocument()
  })
})

describe('7.6 签名与加密', () => {
  it('场景“签名邮件”：正常显示正文，并标明“已签名，签名未验证”', async () => {
    const { api } = await startApp()
    await deliver(api, 'signed')
    const badge = headerOf().getByText('Signed').closest('.badge') as HTMLElement
    expect(badge).toHaveTextContent('Signed')
    expect(badge).toHaveTextContent('signature not verified')
    expect(bodyFrame()).toBeInTheDocument()
    expect(bodyDoc()).toContain(fixture('signed').document!.body.html!)
    expect(screen.queryByText('This message is encrypted')).not.toBeInTheDocument()
  })

  it('签名徽标的说明：明确说明不验证签名，无法确认发件人和内容是否被改动', async () => {
    const { api, user } = await startApp()
    await deliver(api, 'signed')
    await user.click(screen.getByRole('button', { name: 'About this signature' }))
    expect(screen.getByRole('note')).toHaveTextContent("doesn't check signatures")
    expect(screen.getByRole('note')).toHaveTextContent("can't confirm who sent the message")
  })

  it('不透明签名：正文和附件照常显示，同样标明未验证', async () => {
    const { api } = await startApp()
    await deliver(api, 'signed-opaque')
    expect(headerOf().getByText('Signed').closest('.badge')).toHaveTextContent('signature not verified')
    expect(bodyDoc()).toContain('OPAQUE-SIGNED-HTML')
    expect(screen.getByTitle('signed-report.pdf')).toBeInTheDocument()
  })

  it('中文界面：已签名，签名未验证', async () => {
    const { api } = await startApp({ language: 'zh-CN' })
    await deliver(api, 'signed')
    const badge = headerOf().getByText('已签名').closest('.badge') as HTMLElement
    expect(badge).toHaveTextContent('签名未验证')
  })

  it('未签名的邮件没有徽标', async () => {
    const { api } = await startApp()
    await deliver(api, 'headers-normal')
    expect(document.querySelector('.badges')).toBeNull()
  })

  it('场景“加密邮件”：显示邮件头，并提示正文已加密、无法查看；没有 iframe', async () => {
    const { api } = await startApp()
    await deliver(api, 'encrypted')
    const doc = fixture('encrypted').document!
    expect(screen.getByRole('heading', { level: 1, name: doc.subject! })).toBeInTheDocument()
    expect(rowValue('From')).toHaveTextContent(doc.from[0].address!)
    expect(rowValue('To')).toHaveTextContent(doc.to[0].address!)
    const badge = headerOf().getByText('Encrypted').closest('.badge') as HTMLElement
    expect(badge).toBeInTheDocument()
    const placeholder = screen.getByRole('region', { name: 'This message is encrypted' })
    expect(placeholder).toHaveTextContent("EmailFileViewer can't decrypt S/MIME messages")
    expect(screen.queryByTitle('Message content')).not.toBeInTheDocument()
    expect(document.querySelector('.sheet__seam')).toBeNull() // 虚线框，不画接缝
    expect(screen.queryByRole('button', { name: 'About this signature' })).not.toBeInTheDocument()
  })

  it('加密且签名：两个徽标都显示', async () => {
    const { api } = await startApp()
    await deliver(api, withDocument('encrypted', { isSigned: true }, 'enc-signed.eml'))
    expect(headerOf().getByText('Encrypted')).toBeInTheDocument()
    expect(headerOf().getByText('Signed')).toBeInTheDocument()
  })

  it('中文界面：加密提示', async () => {
    const { api } = await startApp({ language: 'zh-CN' })
    await deliver(api, 'encrypted')
    expect(screen.getByRole('region', { name: '这封邮件已加密' })).toHaveTextContent('无法解密 S/MIME 邮件')
    expect(headerOf().getByText('已加密')).toBeInTheDocument()
  })
})

describe('内嵌图片、分段正文、恶意 HTML（样本）', () => {
  it('场景“带签名图片的邮件”：cid 图片已是 data: 图片，正文里没有 cid: 引用；附件列表只有 docx', async () => {
    const { api } = await startApp()
    await deliver(api, 'cid-inline')
    expect(bodyDoc()).toContain('src="data:image/png;base64,')
    expect(bodyDoc()).not.toContain('cid:')
    expect(screen.getByTitle('quarterly-report.docx')).toBeInTheDocument()
    expect(document.querySelectorAll('.att')).toHaveLength(1)
  })

  it('分段正文：三段文字按顺序都在同一个文档里', async () => {
    const { api } = await startApp()
    await deliver(api, 'segmented-body')
    const text = bodyDoc()
    const order = ['SEGMENT-ONE', 'SEGMENT-TWO', 'SEGMENT-THREE'].map((marker) => text.indexOf(marker))
    expect(order.every((i) => i > 0)).toBe(true)
    expect([...order].sort((a, b) => a - b)).toEqual(order)
  })

  it('场景“正文包含脚本”：sidecar 清理后的恶意样本仍然完整显示（标记文字都在），iframe 的沙箱严格不变', async () => {
    const { api } = await startApp()
    await deliver(api, 'html-malicious')
    expect(bodyDoc()).toContain('MARKER-START')
    expect(bodyDoc()).toContain('NO-SCRIPT-RAN')
    expect(bodyDoc()).toContain('MARKER-END')
    expect(bodyFrame().getAttribute('sandbox')).toBe('allow-popups allow-popups-to-escape-sandbox')
    // 恶意样本引用了远程样式表：提示条出现，默认阻止
    expect(screen.getByRole('button', { name: 'Load remote content' })).toBeInTheDocument()
  })

  it('场景“链接文字与实际地址不符”：链接的 title 就是实际地址，原样交给 iframe（悬停由原生提示显示）', async () => {
    const { api } = await startApp()
    await deliver(api, 'link-mismatch')
    const doc = new DOMParser().parseFromString(bodyDoc(), 'text/html')
    const link = [...doc.querySelectorAll('a')].find((a) => a.textContent === 'paypal.com')!
    expect(link.getAttribute('href')).toBe('https://example.com/not-paypal')
    expect(link.getAttribute('title')).toBe('https://example.com/not-paypal')
    expect(link.getAttribute('target')).toBe('_blank')
  })
})
