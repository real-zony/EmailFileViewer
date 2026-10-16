import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { Badge } from '../Badge.tsx'
import { ANCHOR_GAP, EDGE_GAP, computeFloatingPosition } from '../floating.ts'
import { Toast, ToastRegion, type ToastVariant } from '../Toast.tsx'

describe('Toast 语义变体', () => {
  it('默认是 error，保持原来的红色通知含义', () => {
    render(<Toast title="Couldn’t save report.pdf" />)
    const toast = screen.getByRole('status')
    expect(toast).toHaveClass('toast', 'toast--error')
    expect(toast).toHaveAttribute('data-variant', 'error')
  })

  it.each<ToastVariant>(['success', 'info', 'warning', 'error'])('%s 变体输出对应的类名，角色仍是 status', (variant) => {
    render(
      <ToastRegion label="Notifications">
        <Toast variant={variant} title="Title">Body</Toast>
      </ToastRegion>,
    )
    const toast = screen.getByRole('status')
    expect(toast).toHaveClass(`toast--${variant}`)
    expect(toast.querySelector('.toast__icon')).toHaveAttribute('aria-hidden', 'true')
  })

  it('不同变体使用不同形状的图标，不只靠颜色区分', () => {
    const shapes = (['success', 'info', 'warning', 'error'] as const).map((variant) => {
      const { container, unmount } = render(<Toast variant={variant} title="t" />)
      const html = container.querySelector('.toast__icon')!.innerHTML
      unmount()
      return html
    })
    expect(new Set(shapes).size).toBe(4)
  })
})

describe('computeFloatingPosition', () => {
  const viewport = { width: 1000, height: 700 }
  const size = { width: 300, height: 120 }

  it('默认放在锚点下方、左对齐', () => {
    const p = computeFloatingPosition({ top: 100, bottom: 124, left: 60, right: 200 }, size, viewport)
    expect(p).toEqual({ placement: 'bottom', top: 124 + ANCHOR_GAP, left: 60 })
  })

  it('下方放不下时翻到上方', () => {
    const p = computeFloatingPosition({ top: 620, bottom: 644, left: 60, right: 200 }, size, viewport)
    expect(p.placement).toBe('top')
    expect(p.top).toBe(620 - ANCHOR_GAP - size.height)
  })

  it('右侧超出窗口时向内收，左侧不小于边距', () => {
    expect(computeFloatingPosition({ top: 100, bottom: 124, left: 900, right: 990 }, size, viewport).left).toBe(1000 - EDGE_GAP - 300)
    expect(computeFloatingPosition({ top: 100, bottom: 124, left: 2, right: 40 }, size, { width: 250, height: 700 }).left).toBe(EDGE_GAP)
  })

  it('上下都放不下时贴住窗口，不超出视口', () => {
    const p = computeFloatingPosition({ top: 60, bottom: 84, left: 10, right: 50 }, { width: 300, height: 600 }, { width: 1000, height: 620 })
    expect(p.top).toBeGreaterThanOrEqual(EDGE_GAP)
    expect(p.top + 600).toBeLessThanOrEqual(620 - EDGE_GAP)
  })
})

describe('Badge 说明气泡的定位', () => {
  afterEach(() => vi.restoreAllMocks())

  it('气泡渲染在 body 下，不在可能裁切它的容器里', async () => {
    render(
      <div data-testid="clip" style={{ overflow: 'hidden', height: 30 }}>
        <Badge icon="seal" details={{ buttonLabel: 'About this signature', content: 'Not checked.' }}>Signed</Badge>
      </div>,
    )
    await userEvent.click(screen.getByRole('button', { name: 'About this signature' }))
    const note = screen.getByRole('note')
    expect(screen.getByTestId('clip')).not.toContainElement(note)
    expect(note.parentElement).toBe(document.body)
    expect(note).toHaveClass('popover')
  })

  it('贴近窗口底部时翻到徽标上方', async () => {
    vi.spyOn(window, 'innerHeight', 'get').mockReturnValue(600)
    vi.spyOn(window, 'innerWidth', 'get').mockReturnValue(900)
    vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockReturnValue({ top: 560, bottom: 584, left: 40, right: 200, width: 160, height: 24, x: 40, y: 560, toJSON: () => ({}) })
    vi.spyOn(HTMLElement.prototype, 'offsetHeight', 'get').mockReturnValue(100)
    vi.spyOn(HTMLElement.prototype, 'offsetWidth', 'get').mockReturnValue(300)
    render(<Badge icon="seal" details={{ buttonLabel: 'About', content: 'x' }}>Signed</Badge>)
    await userEvent.click(screen.getByRole('button', { name: 'About' }))
    const note = screen.getByRole('note')
    expect(note).toHaveAttribute('data-placement', 'top')
    expect(note.style.top).toBe(`${560 - ANCHOR_GAP - 100}px`)
    expect(note.style.visibility).toBe('')
  })

  it('点击气泡内部不会关闭', async () => {
    const user = userEvent.setup()
    render(<Badge icon="seal" details={{ buttonLabel: 'About', content: 'Inside text' }}>Signed</Badge>)
    await user.click(screen.getByRole('button', { name: 'About' }))
    await user.click(screen.getByText('Inside text'))
    expect(screen.getByRole('note')).toBeInTheDocument()
  })
})
