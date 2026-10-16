import { act, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { I18nextProvider, useTranslation } from 'react-i18next'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import App from '../App.tsx'
import { AddressRow, BodySheet, MessageHeader } from '../components/index.ts'
import { createI18n } from '../i18n/index.ts'
import { resolveInitialSettings } from '../settings/initial.ts'
import { createMemoryStore, type SettingsStore } from '../settings/store.ts'
import type { AppSettings } from '../settings/types.ts'
import { createFakeWindowControl, installSystemAppearance } from '../test/fakes.ts'
import { resolveRootStyle } from '../test/cssTokens.ts'
import { PALETTES } from '../theme/palettes.ts'

const themesCss = readFileSync(resolve(import.meta.dirname, '../theme/themes.css'), 'utf8')
const html = document.documentElement
const paletteOf = (id: string) => PALETTES.find((p) => p.id === id)!

// 当前 <html> 属性 + 系统外观下，浏览器最终会用的令牌值
function tokens(systemDark: boolean) {
  return resolveRootStyle(
    themesCss,
    { 'data-mode': html.getAttribute('data-mode') ?? undefined, 'data-palette': html.getAttribute('data-palette') ?? undefined },
    systemDark,
  )
}

// 模拟 Rust 在每次启动时做的事：读取 store 里保存的设置，没保存的字段用默认值（语言用系统语言）
function startupSettings(store: ReturnType<typeof createMemoryStore>, systemLocale: string): AppSettings {
  return resolveInitialSettings({ injected: Object.fromEntries(store.data), systemLocale })
}

let system: ReturnType<typeof installSystemAppearance>

beforeEach(() => {
  system = installSystemAppearance(false)
  html.removeAttribute('data-mode')
  html.removeAttribute('data-palette')
  html.removeAttribute('lang')
})
afterEach(() => system.restore())

async function openSettings(user: ReturnType<typeof userEvent.setup>, name: string) {
  await user.click(screen.getByRole('button', { name }))
  return screen.getByRole('dialog')
}

describe('app-localization', () => {
  describe('默认跟随系统语言', () => {
    it('场景“系统语言为简体中文”：zh-CN 首次启动，界面为简体中文', () => {
      const store = createMemoryStore()
      render(<App initialSettings={startupSettings(store, 'zh-CN')} store={store} />)
      expect(screen.getByRole('heading', { name: '打开邮件文件' })).toBeInTheDocument()
      expect(screen.getByRole('button', { name: '打开文件…' })).toBeInTheDocument()
      expect(screen.getByRole('button', { name: '设置' })).toBeInTheDocument()
      expect(html).toHaveAttribute('lang', 'zh-CN')
    })

    it('场景“系统语言为德语”：de-DE 首次启动，界面为英文', () => {
      const store = createMemoryStore()
      render(<App initialSettings={startupSettings(store, 'de-DE')} store={store} />)
      expect(screen.getByRole('heading', { name: 'Open an email file' })).toBeInTheDocument()
      expect(screen.getByRole('button', { name: 'Settings' })).toBeInTheDocument()
      expect(html).toHaveAttribute('lang', 'en')
    })

    it('首次启动不会写入 store：只有用户改了设置才保存', () => {
      const store = createMemoryStore()
      render(<App initialSettings={startupSettings(store, 'zh-CN')} store={store} />)
      expect(store.data.size).toBe(0)
    })
  })

  describe('手动切换语言', () => {
    it('场景“切换为英文并重启”：切换后立即变为英文，重启后仍为英文', async () => {
      const user = userEvent.setup()
      const store = createMemoryStore()
      const first = render(<App initialSettings={startupSettings(store, 'zh-CN')} store={store} />)
      expect(screen.getByRole('heading', { name: '打开邮件文件' })).toBeInTheDocument()

      const dialog = await openSettings(user, '设置')
      expect(within(dialog).getByRole('heading', { name: '设置' })).toBeInTheDocument()
      await user.click(within(dialog).getByRole('radio', { name: 'English' }))

      // 立即生效：没有重新挂载，设置对话框和主界面的文字都变了
      expect(within(screen.getByRole('dialog')).getByRole('heading', { name: 'Settings' })).toBeInTheDocument()
      expect(screen.getByRole('heading', { name: 'Open an email file' })).toBeInTheDocument()
      expect(html).toHaveAttribute('lang', 'en')
      expect(store.data.get('language')).toBe('en')

      // 重启：丢弃整个应用，用保存的设置重新初始化；系统语言仍然是 zh-CN
      first.unmount()
      render(<App initialSettings={startupSettings(store, 'zh-CN')} store={store} />)
      expect(screen.getByRole('heading', { name: 'Open an email file' })).toBeInTheDocument()
      expect(html).toHaveAttribute('lang', 'en')
    })

    it('语言选项始终用各自的语言书写，当前选项为选中状态', async () => {
      const user = userEvent.setup()
      const store = createMemoryStore()
      render(<App initialSettings={startupSettings(store, 'zh-CN')} store={store} />)
      const dialog = await openSettings(user, '设置')
      expect(within(dialog).getByRole('radio', { name: 'English' })).toHaveAttribute('aria-checked', 'false')
      expect(within(dialog).getByRole('radio', { name: '简体中文' })).toHaveAttribute('aria-checked', 'true')
    })

    it('保存失败时界面照常切换，只记录错误', async () => {
      const error = vi.spyOn(console, 'error').mockImplementation(() => {})
      const user = userEvent.setup()
      const broken: SettingsStore = { set: () => Promise.reject(new Error('disk full')), save: () => Promise.resolve() }
      render(<App initialSettings={resolveInitialSettings({ systemLocale: 'zh-CN' })} store={broken} />)
      await user.click(within(await openSettings(user, '设置')).getByRole('radio', { name: 'English' }))
      expect(screen.getByRole('heading', { name: 'Open an email file' })).toBeInTheDocument()
      expect(error).toHaveBeenCalled()
      error.mockRestore()
    })
  })

  describe('界面语言不改变邮件内容', () => {
    // 7.4/7.5 的邮件查看视图还没有实现，这里用现有的邮件头和正文组件搭一个最小的查看界面，
    // 邮件内容（主题、地址、正文）作为数据传入，应用文字全部来自 t()。
    const email = {
      subject: 'Quarterly numbers: final draft attached',
      from: { name: 'Mira Okafor', address: 'mira@okafor-outfitters.example' },
      to: [{ address: 'orders@okafor-outfitters.example' }],
      body: '<p>Hi team, the numbers are final.</p>',
    }

    function Viewer() {
      const { t } = useTranslation()
      return (
        <>
          <MessageHeader
            subject={email.subject}
            showFullSubjectLabel={t('header.showSubject')}
            showLessSubjectLabel={t('header.hideSubject')}
            fromLabel={t('header.from')}
            from={email.from}
            dateLabel={t('header.date')}
            dateText="Mon, Oct 5, 2026, 9:00 AM"
            dateTime="2026-10-05T16:00:00Z"
          >
            <AddressRow label={t('header.to')} addresses={email.to} moreLabel={(n) => t('header.more', { n })} lessLabel={t('header.showLess')} />
          </MessageHeader>
          <BodySheet label={t('body.frame')} railText={t('body.label')} formatLabel={t('body.format.html')}>
            <iframe className="sheet__frame" title={t('body.frame')} srcDoc={email.body} />
          </BodySheet>
        </>
      )
    }

    it('场景“中文界面查看英文邮件”：按钮和标签为中文，邮件主题和正文保持原样；切换语言后依然如此', async () => {
      const i18n = createI18n('zh-CN')
      const { container } = render(
        <I18nextProvider i18n={i18n}>
          <Viewer />
        </I18nextProvider>,
      )
      expect(screen.getByText('发件人')).toBeInTheDocument()
      expect(screen.getByText('收件人')).toBeInTheDocument()
      expect(screen.getByRole('region', { name: '邮件内容' })).toBeInTheDocument()
      const iframe = container.querySelector('iframe')!
      expect(iframe).toHaveAttribute('title', '邮件内容')
      expect(screen.getByRole('heading', { name: email.subject })).toBeInTheDocument()
      expect(screen.getByText(email.from.name)).toBeInTheDocument()
      expect(iframe).toHaveAttribute('srcdoc', email.body)

      await act(() => i18n.changeLanguage('en'))
      expect(screen.getByText('From')).toBeInTheDocument()
      expect(container.querySelector('iframe')).toHaveAttribute('title', 'Message content')
      // 同一个 iframe，正文没有被改写；主题和发件人也没变
      expect(container.querySelector('iframe')).toBe(iframe)
      expect(iframe).toHaveAttribute('srcdoc', email.body)
      expect(screen.getByRole('heading', { name: email.subject })).toBeInTheDocument()
      expect(container.querySelector('.addr__name')).toHaveTextContent(email.from.name)
      expect(container.querySelector('.addr__email')).toHaveTextContent(email.from.address)
    })
  })
})

describe('app-appearance', () => {
  const boot = (opts: { initial?: Partial<AppSettings>; store?: ReturnType<typeof createMemoryStore>; locale?: string; withWindow?: boolean } = {}) => {
    const store = opts.store ?? createMemoryStore()
    const win = createFakeWindowControl()
    const initial = { ...startupSettings(store, opts.locale ?? 'en-US'), ...opts.initial }
    const view = render(<App initialSettings={initial} store={store} windowControl={opts.withWindow === false ? null : win.control} />)
    return { store, win, view }
  }

  describe('明暗模式', () => {
    it('场景“首次启动跟随系统”：系统处于深色，首次启动界面为深色', () => {
      system.setDark(true)
      boot()
      expect(html).toHaveAttribute('data-mode', 'system')
      expect(html).toHaveAttribute('data-palette', 'verdigris')
      expect(tokens(system.dark)['--efv-desk']).toBe(paletteOf('verdigris').dark.desk)
      expect(tokens(system.dark)['--efv-ink']).toBe(paletteOf('verdigris').dark.ink)
      expect(tokens(system.dark)['color-scheme']).toBe('dark')
    })

    it('首次启动在浅色系统上：界面为浅色', () => {
      boot()
      expect(tokens(system.dark)['--efv-desk']).toBe(paletteOf('verdigris').light.desk)
    })

    it('场景“运行中系统切换外观”：跟随系统模式下，系统从浅色变深色，界面和窗口底色立即跟着变，无需重启', () => {
      const { win } = boot()
      expect(tokens(system.dark)['--efv-desk']).toBe('#e6eae8')
      expect(win.backgrounds.at(-1)).toBe('#e6eae8')

      act(() => system.setDark(true))
      // <html> 上的属性不变（仍是 system），变的是 CSS 的 prefers-color-scheme 分支
      expect(html).toHaveAttribute('data-mode', 'system')
      expect(tokens(system.dark)['--efv-desk']).toBe('#121715')
      expect(win.backgrounds.at(-1)).toBe('#121715')

      act(() => system.setDark(false))
      expect(tokens(system.dark)['--efv-desk']).toBe('#e6eae8')
      expect(win.backgrounds.at(-1)).toBe('#e6eae8')
      // 标题栏主题交还给系统
      expect(win.themes.every((t) => t === null)).toBe(true)
    })

    it('场景“手动固定为浅色”：选择浅色后系统切换为深色，界面保持浅色', async () => {
      const user = userEvent.setup()
      system.setDark(true)
      const { win, store } = boot()
      expect(tokens(system.dark)['--efv-desk']).toBe('#121715')

      await user.click(within(await openSettings(user, 'Settings')).getByRole('radio', { name: 'Light' }))
      expect(html).toHaveAttribute('data-mode', 'light')
      expect(tokens(system.dark)['--efv-desk']).toBe('#e6eae8')
      expect(tokens(system.dark)['color-scheme']).toBe('light')
      expect(win.backgrounds.at(-1)).toBe('#e6eae8')
      expect(win.themes.at(-1)).toBe('light')
      expect(store.data.get('mode')).toBe('light')

      // 系统再切换：不再监听，窗口底色不变
      const calls = win.backgrounds.length
      act(() => system.setDark(false))
      act(() => system.setDark(true))
      expect(system.listenerCount()).toBe(0)
      expect(win.backgrounds).toHaveLength(calls)
      expect(tokens(system.dark)['--efv-desk']).toBe('#e6eae8')
    })

    it('固定为深色后系统是浅色：界面仍为深色；改回“跟随系统”重新跟随', async () => {
      const user = userEvent.setup()
      const { win } = boot()
      await user.click(within(await openSettings(user, 'Settings')).getByRole('radio', { name: 'Dark' }))
      expect(tokens(system.dark)['--efv-desk']).toBe('#121715')
      expect(win.themes.at(-1)).toBe('dark')

      await user.click(screen.getByRole('radio', { name: 'Match system' }))
      expect(html).toHaveAttribute('data-mode', 'system')
      expect(tokens(system.dark)['--efv-desk']).toBe('#e6eae8')
      expect(win.themes.at(-1)).toBeNull()
      act(() => system.setDark(true))
      expect(win.backgrounds.at(-1)).toBe('#121715')
    })

    it('默认模式是跟随系统，三种模式都可选', async () => {
      const user = userEvent.setup()
      boot()
      const dialog = await openSettings(user, 'Settings')
      expect(within(dialog).getByRole('radio', { name: 'Match system' })).toHaveAttribute('aria-checked', 'true')
      expect(within(dialog).getAllByRole('radio', { name: /^(Light|Dark|Match system)$/ })).toHaveLength(3)
    })
  })

  describe('预设配色方案', () => {
    it('首次启动使用默认方案 verdigris', () => {
      boot()
      expect(html).toHaveAttribute('data-palette', 'verdigris')
    })

    it('场景“切换配色方案”：立即改用该方案，当前明暗模式保持不变', async () => {
      const user = userEvent.setup()
      const { win } = boot({ initial: { mode: 'dark' } })
      expect(tokens(system.dark)['--efv-accent']).toBe(paletteOf('verdigris').dark.accent)

      await user.click(within(await openSettings(user, 'Settings')).getByRole('radio', { name: /Plum/ }))
      expect(html).toHaveAttribute('data-palette', 'plum')
      expect(html).toHaveAttribute('data-mode', 'dark')
      expect(tokens(system.dark)['--efv-accent']).toBe(paletteOf('plum').dark.accent)
      expect(win.backgrounds.at(-1)).toBe(paletteOf('plum').dark.desk)
    })

    it('场景“配色方案与明暗模式组合”：选了方案再切到深色，显示该方案的深色变体；每套方案的两种模式都能组合', async () => {
      const user = userEvent.setup()
      const { win } = boot()
      const dialog = await openSettings(user, 'Settings')
      for (const palette of PALETTES) {
        const name = palette.name.en
        await user.click(within(dialog).getByRole('radio', { name: new RegExp(name) }))
        await user.click(within(dialog).getByRole('radio', { name: 'Dark' }))
        expect(html).toHaveAttribute('data-palette', palette.id)
        expect(tokens(false)['--efv-desk'], `${palette.id}/dark`).toBe(palette.dark.desk)
        expect(tokens(false)['--efv-surface'], `${palette.id}/dark`).toBe(palette.dark.surface)
        expect(win.backgrounds.at(-1)).toBe(palette.dark.desk)
        await user.click(within(dialog).getByRole('radio', { name: 'Light' }))
        expect(tokens(true)['--efv-desk'], `${palette.id}/light`).toBe(palette.light.desk)
        expect(win.backgrounds.at(-1)).toBe(palette.light.desk)
      }
    })

    it('配色的名称和说明按界面语言取', async () => {
      const user = userEvent.setup()
      boot({ locale: 'zh-CN' })
      const dialog = await openSettings(user, '设置')
      for (const p of PALETTES) {
        const radio = within(dialog).getByRole('radio', { name: new RegExp(p.name.zh) })
        expect(radio).toHaveAttribute('title', p.blurb.zh)
      }
      await user.click(within(dialog).getByRole('radio', { name: 'English' }))
      for (const p of PALETTES) {
        expect(within(dialog).getByRole('radio', { name: new RegExp(p.name.en) })).toHaveAttribute('title', p.blurb.en)
      }
    })
  })

  describe('外观设置持久保存', () => {
    it('场景“重启后保持设置”：选深色和非默认方案，重启后以深色和该方案启动', async () => {
      const user = userEvent.setup()
      const { store, view, win } = boot()
      const dialog = await openSettings(user, 'Settings')
      await user.click(within(dialog).getByRole('radio', { name: 'Dark' }))
      await user.click(within(dialog).getByRole('radio', { name: /Indigo/ }))
      expect(Object.fromEntries(store.data)).toEqual({ mode: 'dark', palette: 'indigo' })

      // 重启：整个应用丢弃，<html> 属性也清掉，仅凭保存的数据重新初始化
      view.unmount()
      html.removeAttribute('data-mode')
      html.removeAttribute('data-palette')
      const second = createFakeWindowControl()
      render(<App initialSettings={startupSettings(store, 'en-US')} store={store} windowControl={second.control} />)
      expect(html).toHaveAttribute('data-mode', 'dark')
      expect(html).toHaveAttribute('data-palette', 'indigo')
      expect(tokens(false)['--efv-desk']).toBe(paletteOf('indigo').dark.desk)
      expect(second.backgrounds.at(-1)).toBe(paletteOf('indigo').dark.desk)
      expect(second.themes.at(-1)).toBe('dark')
      expect(win.backgrounds.at(-1)).toBe(paletteOf('indigo').dark.desk)
    })

    it('语言、模式、配色都保存，每次修改立即写入并落盘', async () => {
      const user = userEvent.setup()
      const saved: string[] = []
      const store = createMemoryStore()
      const spy: SettingsStore = {
        set: (key, value) => (saved.push(`set ${key}=${value}`), store.set(key, value)),
        save: () => (saved.push('save'), store.save()),
      }
      render(<App initialSettings={resolveInitialSettings({ systemLocale: 'en-US' })} store={spy} />)
      const dialog = await openSettings(user, 'Settings')
      await user.click(within(dialog).getByRole('radio', { name: '简体中文' }))
      await user.click(within(dialog).getByRole('radio', { name: '深色' }))
      await user.click(within(dialog).getByRole('radio', { name: /石墨/ }))
      expect(saved).toEqual(['set language=zh-CN', 'save', 'set mode=dark', 'save', 'set palette=graphite', 'save'])
    })

    it('点击已经选中的项不会重复保存', async () => {
      const user = userEvent.setup()
      const saved: string[] = []
      const spy: SettingsStore = { set: (key, value) => (saved.push(`${key}=${value}`), Promise.resolve()), save: () => Promise.resolve() }
      render(<App initialSettings={resolveInitialSettings({ systemLocale: 'en-US' })} store={spy} />)
      const dialog = await openSettings(user, 'Settings')
      await user.click(within(dialog).getByRole('radio', { name: 'Match system' }))
      await user.click(within(dialog).getByRole('radio', { name: 'English' }))
      await user.click(within(dialog).getByRole('radio', { name: /Verdigris/ }))
      expect(saved).toEqual([])
    })
  })

  describe('外观不改变邮件内容：邮件正文纸面始终是浅色底', () => {
    const appCss = readFileSync(resolve(import.meta.dirname, '../styles/app.css'), 'utf8')
    // 取 app.css 中以该选择器单独开头的规则的声明块
    const rule = (selector: string) => {
      const m = new RegExp(`^${selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}\\s*\\{([^}]*)\\}`, 'm').exec(appCss)
      expect(m, `app.css 中没有 ${selector} 规则`).not.toBeNull()
      return m![1]
    }

    it('任何方案、任何模式下，主题令牌都不包含纸面颜色', () => {
      for (const p of PALETTES) {
        for (const mode of ['light', 'dark', 'system'] as const) {
          for (const systemDark of [false, true]) {
            const style = resolveRootStyle(themesCss, { 'data-mode': mode, 'data-palette': p.id }, systemDark)
            expect(Object.keys(style).filter((k) => k.includes('paper'))).toEqual([])
          }
        }
      }
    })

    it('纸面颜色是写死的白色，正文容器和 iframe 都用它做底色，iframe 声明 color-scheme: light', () => {
      expect(rule('.sheet__body, .sheet__frame')).toMatch(/--efv-paper:\s*#ffffff;/)
      expect(rule('.sheet__body')).toMatch(/background:\s*var\(--efv-paper\)/)
      expect(rule('.sheet__frame')).toMatch(/background:\s*var\(--efv-paper\)/)
      expect(rule('.sheet__frame')).toMatch(/color-scheme:\s*light/)
      // 不使用任何主题文字色：iframe 里的文档不继承界面的颜色
      expect(rule('.sheet__frame')).not.toMatch(/var\(--efv-ink/)
    })

    // 7.5 实现正文视图后，这两个场景还要用真实的 HTML 和纯文本邮件再验证一次
    it('场景“深色模式下查看 HTML 邮件”：界面为深色，iframe 仍挂在固定浅色底的正文容器里', () => {
      system.setDark(true)
      boot({ initial: { mode: 'dark', palette: 'plum' } })
      render(
        <BodySheet label="Message content" railText="Message content" formatLabel="HTML">
          <iframe className="sheet__frame" title="Message content" srcDoc="<p>Sale</p>" />
        </BodySheet>,
      )
      expect(html).toHaveAttribute('data-mode', 'dark')
      expect(tokens(true)['--efv-desk']).toBe(paletteOf('plum').dark.desk)
      const frame = document.querySelector('.sheet__body > iframe.sheet__frame')
      expect(frame).not.toBeNull()
    })

    it('场景“深色模式下查看纯文本邮件”：正文同样放在固定浅色底的 iframe 里，界面文字色不会传进去', () => {
      system.setDark(true)
      boot({ initial: { mode: 'dark' } })
      render(
        <BodySheet label="Message content" railText="Message content" formatLabel="Plain text">
          <iframe className="sheet__frame" title="Message content" srcDoc="<pre>Hello</pre>" />
        </BodySheet>,
      )
      // 界面文字在深色下是浅色，而 .sheet__frame 不引用它
      expect(tokens(true)['--efv-ink']).toBe(paletteOf('verdigris').dark.ink)
      expect(document.querySelector('.sheet__body > iframe.sheet__frame')).not.toBeNull()
    })
  })
})
