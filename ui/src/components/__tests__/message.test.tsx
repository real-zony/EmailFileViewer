import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { AddressRow, AddressText, type Address } from '../Address.tsx'
import { Badge, BadgeList } from '../Badge.tsx'
import { BodySheet, PlaceholderSheet } from '../BodySheet.tsx'
import { MessageHeader } from '../MessageHeader.tsx'
import { Notice, RemoteContentNotice } from '../Notice.tsx'

const more = (n: number) => `+${n} more`

describe('AddressText', () => {
  it('同时显示名称和邮箱', () => {
    render(<AddressText address={{ name: 'Mira Okafor', address: 'mira@okafor.example' }} />)
    expect(screen.getByText('Mira Okafor')).toHaveClass('addr__name')
    expect(screen.getByText('mira@okafor.example')).toHaveClass('addr__email')
  })

  it('没有名称或名称等于地址时只显示地址', () => {
    const { container, rerender } = render(<AddressText address={{ address: 'a@b.example' }} />)
    expect(container.querySelector('.addr__name')).toBeNull()
    rerender(<AddressText address={{ name: 'a@b.example', address: 'a@b.example' }} />)
    expect(container.querySelector('.addr__name')).toBeNull()
  })

  it('发件人加重', () => {
    const { container } = render(<AddressText address={{ address: 'a@b.example' }} emphasis />)
    expect(container.firstChild).toHaveClass('addr--from')
  })
})

describe('AddressRow', () => {
  const people: Address[] = Array.from({ length: 5 }, (_, i) => ({ name: `Person ${i + 1}`, address: `p${i + 1}@x.example` }))

  it('没有地址时整行不渲染', () => {
    const { container } = render(<AddressRow label="Cc" addresses={[]} moreLabel={more} lessLabel="Show less" />)
    expect(container).toBeEmptyDOMElement()
  })

  it('不超过阈值时全部显示，没有折叠按钮', () => {
    render(<AddressRow label="To" addresses={people.slice(0, 3)} moreLabel={more} lessLabel="Show less" />)
    expect(screen.getByText('To')).toHaveClass('hlabel')
    expect(screen.getAllByText(/Person/)).toHaveLength(3)
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
  })

  it('超过阈值时折叠，按钮可展开和收起', async () => {
    render(<AddressRow label="To" addresses={people} collapseAfter={3} moreLabel={more} lessLabel="Show less" />)
    expect(screen.getAllByText(/Person/)).toHaveLength(3)
    const toggle = screen.getByRole('button', { name: '+2 more' })
    expect(toggle).toHaveAttribute('aria-expanded', 'false')
    await userEvent.click(toggle)
    expect(screen.getAllByText(/Person/)).toHaveLength(5)
    expect(screen.getByRole('button', { name: 'Show less' })).toHaveAttribute('aria-expanded', 'true')
  })
})

describe('MessageHeader', () => {
  const base = {
    subject: 'Autumn/Winter ’26 trade preview',
    showFullSubjectLabel: 'Show full subject',
    showLessSubjectLabel: 'Show less',
    fromLabel: 'From',
    from: { name: 'Halden & Moss Trade', address: 'trade@haldenmoss.example' },
    dateLabel: 'Date',
    dateText: 'Tue, Oct 6, 2026, 12:00 AM',
    dateTime: '2026-10-05T09:00:00-07:00',
    dateTitle: 'Mon, 5 Oct 2026 09:00:00 -0700',
  }

  afterEach(() => vi.restoreAllMocks())

  it('主题是一级标题，日期带 datetime 和原始 Date 头', () => {
    render(<MessageHeader {...base}><AddressRow label="To" addresses={[{ address: 'a@b.example' }]} moreLabel={more} lessLabel="Show less" /></MessageHeader>)
    expect(screen.getByRole('heading', { level: 1, name: base.subject })).toBeInTheDocument()
    const time = screen.getByText(base.dateText)
    expect(time.tagName).toBe('TIME')
    expect(time).toHaveAttribute('datetime', base.dateTime)
    expect(time).toHaveAttribute('title', base.dateTitle)
    expect(screen.getByText('Halden & Moss Trade')).toBeInTheDocument()
    expect(screen.getByText('To')).toBeInTheDocument()
  })

  it('主题没有溢出时不显示展开按钮', () => {
    render(<MessageHeader {...base} />)
    expect(screen.queryByRole('button', { name: 'Show full subject' })).not.toBeInTheDocument()
  })

  it('主题超过两行时显示展开按钮，展开后取消折叠', async () => {
    vi.spyOn(HTMLElement.prototype, 'scrollHeight', 'get').mockReturnValue(90)
    vi.spyOn(HTMLElement.prototype, 'clientHeight', 'get').mockReturnValue(56)
    render(<MessageHeader {...base} />)
    const heading = screen.getByRole('heading', { level: 1 })
    expect(heading).toHaveClass('subject--clamped')
    await userEvent.click(screen.getByRole('button', { name: 'Show full subject' }))
    expect(heading).not.toHaveClass('subject--clamped')
    expect(screen.getByRole('button', { name: 'Show less' })).toHaveAttribute('aria-expanded', 'true')
  })

  it('徽标插槽', () => {
    render(<MessageHeader {...base} badges={<BadgeList><Badge icon="lock">Encrypted</Badge></BadgeList>} />)
    expect(screen.getByText('Encrypted')).toBeInTheDocument()
  })
})

describe('Badge', () => {
  it('显示文字和弱化说明', () => {
    render(<Badge icon="seal" muted="signature not verified">Signed</Badge>)
    expect(screen.getByText('Signed')).toBeInTheDocument()
    expect(screen.getByText(/signature not verified/)).toHaveClass('badge__muted')
  })

  it('说明按钮切换气泡，Esc 关闭', async () => {
    const user = userEvent.setup()
    render(<Badge icon="seal" details={{ buttonLabel: 'About this signature', content: 'Not checked.' }}>Signed</Badge>)
    const btn = screen.getByRole('button', { name: 'About this signature' })
    expect(btn).toHaveAttribute('aria-expanded', 'false')
    await user.click(btn)
    expect(btn).toHaveAttribute('aria-expanded', 'true')
    const note = screen.getByRole('note')
    expect(note).toHaveTextContent('Not checked.')
    expect(btn).toHaveAttribute('aria-controls', note.id)
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('note')).not.toBeInTheDocument()
  })

  it('点击外部关闭气泡', async () => {
    const user = userEvent.setup()
    render(<><Badge icon="seal" details={{ buttonLabel: 'About', content: 'x' }}>Signed</Badge><p>outside</p></>)
    await user.click(screen.getByRole('button', { name: 'About' }))
    await user.click(screen.getByText('outside'))
    expect(screen.queryByRole('note')).not.toBeInTheDocument()
  })
})

describe('Notice / RemoteContentNotice', () => {
  const props = {
    blockedTitle: 'Remote images and styles are blocked.',
    blockedDescription: 'Loading them lets the sender see that you opened this message.',
    scopeLabel: 'This message only',
    loadLabel: 'Load remote content',
    loadedText: 'Remote content is loaded for this message.',
  }

  it('阻止状态：status 角色、说明作用范围，按钮触发加载', async () => {
    const onLoad = vi.fn()
    render(<RemoteContentNotice state="blocked" {...props} onLoad={onLoad} />)
    const status = screen.getByRole('status')
    expect(status).toHaveClass('notice--warn')
    expect(status).toHaveTextContent('This message only')
    await userEvent.click(screen.getByRole('button', { name: 'Load remote content' }))
    expect(onLoad).toHaveBeenCalledOnce()
  })

  it('已加载状态：一行确认文字，没有按钮（v1 不提供重新阻止）', () => {
    render(<RemoteContentNotice state="loaded" {...props} onLoad={() => {}} />)
    expect(screen.getByRole('status')).toHaveClass('notice--quiet')
    expect(screen.getByRole('status')).toHaveTextContent(props.loadedText)
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
  })

  it('Notice 可以不带操作', () => {
    render(<Notice tone="quiet" icon="check" title="Done" />)
    expect(screen.getByRole('status')).toHaveTextContent('Done')
  })
})

describe('BodySheet / PlaceholderSheet', () => {
  it('提示条在接缝以上，iframe 插槽在接缝以下', () => {
    const { container } = render(
      <BodySheet label="Message content" railText="Message content · shown in isolation" formatLabel="HTML" notice={<div data-testid="notice" />}>
        <iframe title="Body" />
      </BodySheet>,
    )
    expect(screen.getByRole('region', { name: 'Message content' })).toBeInTheDocument()
    expect(screen.getByText('HTML')).toHaveClass('sheet__format')
    const sheet = container.querySelector('.sheet')!
    const [chrome, seam, body] = Array.from(sheet.children)
    expect(chrome).toHaveClass('sheet__chrome')
    expect(chrome).toContainElement(screen.getByTestId('notice'))
    expect(seam).toHaveClass('sheet__seam')
    expect(seam).toHaveAttribute('aria-hidden', 'true')
    expect(body).toHaveClass('sheet__body')
    expect(body).toContainElement(screen.getByTitle('Body'))
  })

  it('占位纸面不画接缝', () => {
    const { container } = render(<PlaceholderSheet icon="lock" title="This message is encrypted" body="Can't be shown." />)
    expect(screen.getByRole('region', { name: 'This message is encrypted' })).toHaveClass('sheet--placeholder')
    expect(screen.getByRole('heading', { name: 'This message is encrypted' })).toBeInTheDocument()
    expect(container.querySelector('.sheet__seam')).toBeNull()
  })
})
