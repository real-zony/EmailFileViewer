import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { describe, expect, it, vi } from 'vitest'
import { AppShell, MainArea, ViewerLayout } from '../Layout.tsx'
import { TabBar, type TabItem } from '../TabBar.tsx'
import { Breadcrumbs, Toolbar } from '../Toolbar.tsx'

const TABS: TabItem[] = [
  { id: 'a', label: 'AW26 trade preview', title: 'AW26 trade preview.msg' },
  { id: 'b', label: '第四季度对账通知' },
  { id: 'c', label: 'Invoice_88213', status: 'error' },
]

function ControlledTabs({ onSelect = () => {}, onClose = () => {}, onAdd = () => {} }: { onSelect?: (id: string) => void; onClose?: (id: string) => void; onAdd?: () => void }) {
  const [active, setActive] = useState('a')
  return (
    <TabBar
      label="Open files"
      tabs={TABS}
      activeId={active}
      onSelect={(id) => {
        setActive(id)
        onSelect(id)
      }}
      onClose={onClose}
      closeLabel={(t) => `Close ${t.label}`}
      addLabel="Open file"
      onAdd={onAdd}
      panelId="panel"
    />
  )
}

describe('TabBar', () => {
  it('只有 1 个标签页时不渲染', () => {
    const { container } = render(
      <TabBar label="Open files" tabs={[TABS[0]]} activeId="a" onSelect={() => {}} onClose={() => {}} closeLabel={() => 'x'} addLabel="Open file" onAdd={() => {}} />,
    )
    expect(container).toBeEmptyDOMElement()
  })

  it('渲染带名称的 tablist，当前标签 aria-selected 并关联面板', () => {
    render(<ControlledTabs />)
    expect(screen.getByRole('tablist', { name: 'Open files' })).toBeInTheDocument()
    const tabs = screen.getAllByRole('tab')
    expect(tabs).toHaveLength(3)
    expect(tabs[0]).toHaveAttribute('aria-selected', 'true')
    expect(tabs[0]).toHaveAttribute('aria-controls', 'panel')
    expect(tabs[0]).toHaveAttribute('tabindex', '0')
    expect(tabs[1]).toHaveAttribute('aria-selected', 'false')
    expect(tabs[1]).toHaveAttribute('tabindex', '-1')
  })

  it('失败的文件显示为错误标签', () => {
    render(<ControlledTabs />)
    expect(screen.getByRole('tab', { name: 'Invoice_88213' }).closest('.tab')).toHaveClass('tab--error')
  })

  it('← → 切换并激活，Home / End 到首尾', async () => {
    const user = userEvent.setup()
    const onSelect = vi.fn()
    render(<ControlledTabs onSelect={onSelect} />)
    await user.tab()
    expect(screen.getByRole('tab', { name: 'AW26 trade preview' })).toHaveFocus()

    await user.keyboard('{ArrowRight}')
    expect(onSelect).toHaveBeenLastCalledWith('b')
    expect(screen.getByRole('tab', { name: '第四季度对账通知' })).toHaveFocus()
    expect(screen.getByRole('tab', { name: '第四季度对账通知' })).toHaveAttribute('aria-selected', 'true')

    await user.keyboard('{End}')
    expect(onSelect).toHaveBeenLastCalledWith('c')
    await user.keyboard('{ArrowRight}')
    expect(onSelect).toHaveBeenLastCalledWith('a')
    await user.keyboard('{ArrowLeft}')
    expect(onSelect).toHaveBeenLastCalledWith('c')
    await user.keyboard('{Home}')
    expect(screen.getByRole('tab', { name: 'AW26 trade preview' })).toHaveFocus()
  })

  it('关闭按钮有名称、不在 Tab 顺序中，点击回调对应 id', async () => {
    const onClose = vi.fn()
    render(<ControlledTabs onClose={onClose} />)
    const close = screen.getByRole('button', { name: 'Close 第四季度对账通知' })
    expect(close).toHaveAttribute('tabindex', '-1')
    await userEvent.click(close)
    expect(onClose).toHaveBeenCalledWith('b')
  })

  it('新增按钮', async () => {
    const onAdd = vi.fn()
    render(<ControlledTabs onAdd={onAdd} />)
    await userEvent.click(screen.getByRole('button', { name: 'Open file' }))
    expect(onAdd).toHaveBeenCalledOnce()
  })
})

describe('Toolbar / Breadcrumbs', () => {
  it('Toolbar 渲染左右两侧内容', () => {
    render(<Toolbar start={<button type="button">Open…</button>} end={<button type="button">Settings</button>} />)
    expect(screen.getByRole('button', { name: 'Open…' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Settings' })).toBeInTheDocument()
  })

  it('少于 2 级时不渲染面包屑', () => {
    const { container } = render(<Breadcrumbs label="Breadcrumb" backLabel="Back" items={[{ id: 'a', label: 'A' }]} onNavigate={() => {}} />)
    expect(container).toBeEmptyDOMElement()
  })

  it('最后一级是当前页，其余可点击；返回按钮回到上一级', async () => {
    const onNavigate = vi.fn()
    render(
      <Breadcrumbs
        label="Breadcrumb"
        backLabel="Back"
        items={[{ id: 'a', label: 'AW26 trade preview' }, { id: 'b', label: 'RE: Pallet delivery dates' }, { id: 'c', label: 'Carrier confirmation' }]}
        onNavigate={onNavigate}
      />,
    )
    expect(screen.getByRole('navigation', { name: 'Breadcrumb' })).toBeInTheDocument()
    expect(screen.getByText('Carrier confirmation')).toHaveAttribute('aria-current', 'page')
    await userEvent.click(screen.getByRole('button', { name: 'AW26 trade preview' }))
    expect(onNavigate).toHaveBeenLastCalledWith(0)
    await userEvent.click(screen.getByRole('button', { name: 'Back' }))
    expect(onNavigate).toHaveBeenLastCalledWith(1)
  })
})

describe('Layout', () => {
  it('MainArea 只有传入 labelledBy 时才是 tabpanel', () => {
    const { rerender } = render(<MainArea>content</MainArea>)
    expect(screen.queryByRole('tabpanel')).not.toBeInTheDocument()
    rerender(<MainArea id="p" labelledBy="tab-a">content</MainArea>)
    expect(screen.getByRole('tabpanel')).toHaveAttribute('aria-labelledby', 'tab-a')
  })

  it('ViewerLayout 把邮件头放在固定的上方区域，正文在其后', () => {
    const { container } = render(
      <AppShell>
        <ViewerLayout top={<header>head</header>} body={<section>body</section>} />
      </AppShell>,
    )
    const viewer = container.querySelector('.win > .viewer')!
    expect(viewer.children[0]).toHaveClass('viewer__top')
    expect(viewer.children[0]).toHaveTextContent('head')
    expect(viewer.children[1]).toHaveTextContent('body')
  })
})
