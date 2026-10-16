import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { describe, expect, it, vi } from 'vitest'
import { Button, IconButton, Kbd } from '../Button.tsx'
import { ChoiceGroup } from '../ChoiceGroup.tsx'
import { PalettePicker } from '../PalettePicker.tsx'
import { SegmentedControl, type SegmentedOption } from '../SegmentedControl.tsx'

describe('Button / IconButton', () => {
  it('按 variant 和 size 输出类名，默认 type=button', async () => {
    const onClick = vi.fn()
    render(<Button variant="primary" size="lg" icon="folder" onClick={onClick}>Open file…</Button>)
    const btn = screen.getByRole('button', { name: 'Open file…' })
    expect(btn).toHaveClass('btn', 'btn--primary', 'btn--lg')
    expect(btn).toHaveAttribute('type', 'button')
    await userEvent.click(btn)
    expect(onClick).toHaveBeenCalledOnce()
  })

  it('ghost 是默认样式，不额外加类名', () => {
    render(<Button>Close tab</Button>)
    expect(screen.getByRole('button')).toHaveClass('btn')
    expect(screen.getByRole('button').className).toBe('btn')
  })

  it('IconButton 有可访问名称，默认把名称作为 title', () => {
    render(<IconButton icon="settings" label="Settings" />)
    const btn = screen.getByRole('button', { name: 'Settings' })
    expect(btn).toHaveAttribute('title', 'Settings')
    expect(btn).toHaveClass('btn--icon')
    expect(btn.querySelector('svg')).toHaveAttribute('aria-hidden', 'true')
  })

  it('IconButton 可以单独指定 title', () => {
    render(<IconButton icon="save" label="Save report.pdf…" title="Save…" />)
    expect(screen.getByRole('button', { name: 'Save report.pdf…' })).toHaveAttribute('title', 'Save…')
  })

  it('Kbd 渲染为 kbd 元素', () => {
    render(<Kbd>Ctrl+O</Kbd>)
    expect(screen.getByText('Ctrl+O').tagName).toBe('KBD')
  })
})

type ModeValue = 'light' | 'dark' | 'system'
const MODES: SegmentedOption<ModeValue>[] = [
  { value: 'light', label: 'Light', icon: 'sun' },
  { value: 'dark', label: 'Dark', icon: 'moon' },
  { value: 'system', label: 'Match system', icon: 'monitor' },
]

function ControlledSegmented({ onChange }: { onChange?: (v: ModeValue) => void }) {
  const [value, setValue] = useState<ModeValue>('system')
  return (
    <SegmentedControl
      label="Mode"
      options={MODES}
      value={value}
      onChange={(v) => {
        setValue(v)
        onChange?.(v)
      }}
    />
  )
}

describe('SegmentedControl', () => {
  it('渲染为带名称的 radiogroup，选中项 aria-checked', () => {
    render(<ControlledSegmented />)
    expect(screen.getByRole('radiogroup', { name: 'Mode' })).toBeInTheDocument()
    expect(screen.getByRole('radio', { name: 'Match system' })).toHaveAttribute('aria-checked', 'true')
    expect(screen.getByRole('radio', { name: 'Light' })).toHaveAttribute('aria-checked', 'false')
  })

  it('只有选中项在 Tab 顺序中（roving tabindex）', () => {
    render(<ControlledSegmented />)
    expect(screen.getByRole('radio', { name: 'Match system' })).toHaveAttribute('tabindex', '0')
    expect(screen.getByRole('radio', { name: 'Dark' })).toHaveAttribute('tabindex', '-1')
  })

  it('方向键循环切换并移动焦点，Home / End 到首尾', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    render(<ControlledSegmented onChange={onChange} />)
    await user.tab()
    expect(screen.getByRole('radio', { name: 'Match system' })).toHaveFocus()

    await user.keyboard('{ArrowRight}')
    expect(onChange).toHaveBeenLastCalledWith('light')
    expect(screen.getByRole('radio', { name: 'Light' })).toHaveFocus()
    expect(screen.getByRole('radio', { name: 'Light' })).toHaveAttribute('aria-checked', 'true')

    await user.keyboard('{ArrowLeft}')
    expect(onChange).toHaveBeenLastCalledWith('system')

    await user.keyboard('{Home}')
    expect(screen.getByRole('radio', { name: 'Light' })).toHaveFocus()
    await user.keyboard('{End}')
    expect(screen.getByRole('radio', { name: 'Match system' })).toHaveFocus()
    await user.keyboard('{ArrowDown}')
    expect(onChange).toHaveBeenLastCalledWith('light')
  })

  it('点击选中', async () => {
    const onChange = vi.fn()
    render(<ControlledSegmented onChange={onChange} />)
    await userEvent.click(screen.getByRole('radio', { name: 'Dark' }))
    expect(onChange).toHaveBeenCalledWith('dark')
  })
})

describe('ChoiceGroup', () => {
  it('语言选项用各自的语言标注，点击切换', async () => {
    const onChange = vi.fn()
    render(
      <ChoiceGroup
        label="Language"
        options={[{ value: 'en', label: 'English', lang: 'en' }, { value: 'zh-CN', label: '简体中文', lang: 'zh-CN' }]}
        value="en"
        onChange={onChange}
      />,
    )
    expect(screen.getByRole('radiogroup', { name: 'Language' })).toBeInTheDocument()
    expect(screen.getByText('简体中文')).toHaveAttribute('lang', 'zh-CN')
    expect(screen.getByRole('radio', { name: 'English' })).toHaveAttribute('aria-checked', 'true')
    await userEvent.click(screen.getByRole('radio', { name: '简体中文' }))
    expect(onChange).toHaveBeenCalledWith('zh-CN')
  })

  it('方向键切换', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    render(<ChoiceGroup label="Language" options={[{ value: 'en', label: 'English' }, { value: 'zh', label: '简体中文' }]} value="en" onChange={onChange} />)
    await user.tab()
    await user.keyboard('{ArrowRight}')
    expect(onChange).toHaveBeenCalledWith('zh')
  })
})

describe('PalettePicker', () => {
  const palettes = [
    { id: 'verdigris', name: 'Verdigris', description: 'The default.' },
    { id: 'graphite', name: 'Graphite' },
    { id: 'plum', name: 'Plum' },
  ] as const

  it('每个方案一个 radio，选中项带屏幕阅读器文字', () => {
    render(<PalettePicker label="Colour scheme" palettes={palettes} value="graphite" mode="dark" selectedLabel="Selected" onChange={() => {}} />)
    const group = screen.getByRole('radiogroup', { name: 'Colour scheme' })
    expect(group).toBeInTheDocument()
    const selected = screen.getByRole('radio', { name: /Graphite/ })
    expect(selected).toHaveAttribute('aria-checked', 'true')
    expect(selected).toHaveTextContent('Selected')
    expect(screen.getByRole('radio', { name: 'Verdigris' })).toHaveAttribute('title', 'The default.')
  })

  it('预览用该方案自己的 data-palette 和当前模式渲染', () => {
    const { container } = render(<PalettePicker label="Colour scheme" palettes={palettes} value="plum" mode="system" selectedLabel="Selected" onChange={() => {}} />)
    const previews = container.querySelectorAll('.pal__preview')
    expect(previews).toHaveLength(3)
    expect(previews[2]).toHaveAttribute('data-palette', 'plum')
    expect(previews[2]).toHaveAttribute('data-mode', 'system')
    expect(previews[2]).toHaveAttribute('aria-hidden', 'true')
  })

  it('方向键切换方案', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    render(<PalettePicker label="Colour scheme" palettes={palettes} value="verdigris" mode="light" selectedLabel="Selected" onChange={onChange} />)
    await user.tab()
    await user.keyboard('{ArrowLeft}')
    expect(onChange).toHaveBeenCalledWith('plum')
  })
})
