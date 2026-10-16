import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { describe, expect, it, vi } from 'vitest'
import { Button } from '../Button.tsx'
import { Dialog, ExecutableBlockedDialog } from '../Dialog.tsx'
import { DropOverlay, EmptyState } from '../EmptyState.tsx'
import { OpenErrorState } from '../OpenErrorState.tsx'
import { SettingsDialog } from '../SettingsDialog.tsx'
import { Toast, ToastRegion } from '../Toast.tsx'

describe('ExecutableBlockedDialog', () => {
  const props = {
    title: '“setup.exe” can’t be opened here',
    body: 'Programs and scripts can change things on your computer.',
    note: 'Run it only if you trust the sender.',
    cancelLabel: 'Cancel',
    saveLabel: 'Save…',
  }

  it('是以标题命名的 alertdialog，初始焦点在“取消”上', () => {
    render(<ExecutableBlockedDialog {...props} onSave={() => {}} onCancel={() => {}} />)
    const dialog = screen.getByRole('alertdialog', { name: props.title })
    expect(dialog).toHaveAttribute('aria-modal', 'true')
    expect(dialog).toHaveAccessibleDescription(/Programs and scripts/)
    expect(screen.getByRole('button', { name: 'Cancel' })).toHaveFocus()
  })

  it('保存和取消回调；Esc 等同取消', async () => {
    const user = userEvent.setup()
    const onSave = vi.fn()
    const onCancel = vi.fn()
    render(<ExecutableBlockedDialog {...props} onSave={onSave} onCancel={onCancel} />)
    await user.click(screen.getByRole('button', { name: 'Save…' }))
    expect(onSave).toHaveBeenCalledOnce()
    await user.keyboard('{Escape}')
    expect(onCancel).toHaveBeenCalledOnce()
  })
})

describe('Dialog', () => {
  it('Tab 在对话框内循环', async () => {
    const user = userEvent.setup()
    render(
      <Dialog title="T" onClose={() => {}} actions={<><Button>One</Button><Button>Two</Button></>}>
        <p>body</p>
      </Dialog>,
    )
    expect(screen.getByRole('button', { name: 'One' })).toHaveFocus()
    await user.tab()
    expect(screen.getByRole('button', { name: 'Two' })).toHaveFocus()
    await user.tab()
    expect(screen.getByRole('button', { name: 'One' })).toHaveFocus()
    await user.tab({ shift: true })
    expect(screen.getByRole('button', { name: 'Two' })).toHaveFocus()
  })

  it('点击遮罩关闭，关闭后焦点回到打开前的元素', async () => {
    const user = userEvent.setup()
    function Host() {
      const [open, setOpen] = useState(false)
      return (
        <>
          <Button onClick={() => setOpen(true)}>Open dialog</Button>
          {open && <Dialog title="T" onClose={() => setOpen(false)} actions={<Button>Inside</Button>} />}
        </>
      )
    }
    const { container } = render(<Host />)
    const opener = screen.getByRole('button', { name: 'Open dialog' })
    await user.click(opener)
    expect(screen.getByRole('button', { name: 'Inside' })).toHaveFocus()
    await user.click(container.querySelector('.scrim')!)
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(opener).toHaveFocus()
  })
})

describe('SettingsDialog', () => {
  function setup() {
    const cb = { onLanguageChange: vi.fn(), onModeChange: vi.fn(), onPaletteChange: vi.fn(), onClose: vi.fn() }
    render(
      <SettingsDialog
        labels={{
          title: 'Settings', close: 'Close', done: 'Done', language: 'Language', languageHint: 'Applies right away.',
          appearance: 'Appearance', mode: 'Mode', palette: 'Colour scheme', selected: 'Selected', appearanceHint: 'App only.',
        }}
        languages={[{ value: 'en', label: 'English', lang: 'en' }, { value: 'zh-CN', label: '简体中文', lang: 'zh-CN' }]}
        language="en"
        modes={[{ value: 'light', label: 'Light' }, { value: 'dark', label: 'Dark' }, { value: 'system', label: 'Match system' }]}
        mode="system"
        palettes={[{ id: 'verdigris', name: 'Verdigris' }, { id: 'plum', name: 'Plum' }]}
        palette="verdigris"
        about={[{ label: 'Version', value: '0.1.0' }]}
        {...cb}
      />,
    )
    return cb
  }

  it('对话框以标题命名，焦点在关闭按钮；三组单选都有名称', () => {
    setup()
    expect(screen.getByRole('dialog', { name: 'Settings' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Close' })).toHaveFocus()
    expect(screen.getByRole('radiogroup', { name: 'Language' })).toBeInTheDocument()
    expect(screen.getByRole('radiogroup', { name: 'Mode' })).toBeInTheDocument()
    expect(screen.getByRole('radiogroup', { name: 'Colour scheme' })).toBeInTheDocument()
    expect(screen.getByText('0.1.0')).toBeInTheDocument()
  })

  it('各项修改立即回调', async () => {
    const cb = setup()
    await userEvent.click(screen.getByRole('radio', { name: '简体中文' }))
    expect(cb.onLanguageChange).toHaveBeenCalledWith('zh-CN')
    await userEvent.click(screen.getByRole('radio', { name: 'Dark' }))
    expect(cb.onModeChange).toHaveBeenCalledWith('dark')
    await userEvent.click(screen.getByRole('radio', { name: 'Plum' }))
    expect(cb.onPaletteChange).toHaveBeenCalledWith('plum')
    await userEvent.click(screen.getByRole('button', { name: 'Done' }))
    expect(cb.onClose).toHaveBeenCalledOnce()
  })
})

describe('Toast', () => {
  it('通知区域有名称，通知是 status，可以带操作', async () => {
    const onShow = vi.fn()
    render(
      <ToastRegion label="Notifications">
        <Toast title="Opened 2 of 3 files" actions={<Button onClick={onShow}>Show</Button>}>
          Invoice.msg couldn’t be opened.
        </Toast>
      </ToastRegion>,
    )
    expect(screen.getByRole('region', { name: 'Notifications' })).toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent('Opened 2 of 3 files')
    expect(screen.getByRole('status')).toHaveTextContent('Invoice.msg couldn’t be opened.')
    await userEvent.click(screen.getByRole('button', { name: 'Show' }))
    expect(onShow).toHaveBeenCalledOnce()
  })
})

describe('EmptyState / DropOverlay', () => {
  const base = {
    title: 'Open an email file',
    body: 'Drop .eml or .msg files anywhere in this window.',
    openLabel: 'Open file…',
    privacyNote: 'Messages are read on this computer. Nothing is uploaded unless you choose to.',
  }

  it('标题、说明、隐私说明和打开按钮', async () => {
    const onOpen = vi.fn()
    render(<EmptyState {...base} onOpen={onOpen} shortcutHint={<>or press <kbd>Ctrl+O</kbd></>} />)
    expect(screen.getByRole('heading', { name: base.title })).toBeInTheDocument()
    expect(screen.getByText(base.privacyNote)).toBeInTheDocument()
    expect(screen.getByText('Ctrl+O')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Open file…' }))
    expect(onOpen).toHaveBeenCalledOnce()
  })

  it('拖入状态换成拖放文字，不显示按钮', () => {
    const { container } = render(<EmptyState {...base} onOpen={() => {}} dragging={{ title: 'Release to open 3 files', body: 'Each file opens in its own tab.' }} />)
    expect(screen.getByRole('heading', { name: 'Release to open 3 files' })).toBeInTheDocument()
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
    expect(container.querySelector('.dropzone')).toHaveClass('dropzone--active')
  })

  it('DropOverlay 是 status 提示', () => {
    render(<DropOverlay title="Release to open 2 files" body="Each file opens in its own tab." />)
    expect(screen.getByRole('status')).toHaveTextContent('Release to open 2 files')
  })
})

describe('OpenErrorState', () => {
  it('alert 角色，显示原因类别、标题、文件名和操作', () => {
    render(
      <OpenErrorState
        reasonLabel="Damaged file"
        title="This file is damaged"
        fileName="Invoice_88213.msg"
        body="Ask for a new copy."
        actions={<Button>Close tab</Button>}
      />,
    )
    const alert = screen.getByRole('alert')
    expect(alert).toHaveTextContent('Damaged file')
    expect(screen.getByRole('heading', { name: 'This file is damaged' })).toBeInTheDocument()
    expect(screen.getByTitle('Invoice_88213.msg')).toHaveTextContent('Invoice_88213.msg')
    expect(screen.getByRole('button', { name: 'Close tab' })).toBeInTheDocument()
  })
})
