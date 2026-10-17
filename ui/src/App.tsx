import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState, useSyncExternalStore } from 'react'
import { I18nextProvider, useTranslation } from 'react-i18next'
import { createMockApi } from './api/mockApi.ts'
import type { Unlisten, ViewerApi } from './api/types.ts'
import {
  AppShell,
  Breadcrumbs,
  Button,
  DropOverlay,
  EmptyState,
  ExecutableBlockedDialog,
  IconButton,
  Kbd,
  MainArea,
  OpenErrorState,
  SettingsDialog,
  TabBar,
  Toolbar,
  type TabItem,
} from './components/index.ts'
import { createI18n } from './i18n/index.ts'
import { applyDocumentSettings } from './settings/appearance.ts'
import { LANGUAGE_OPTIONS } from './settings/language.ts'
import { persistSetting, type SettingsStore } from './settings/store.ts'
import type { AppSettings, SettingKey } from './settings/types.ts'
import { syncWindow, watchSystemDark, type WindowControl } from './settings/windowControl.ts'
import { PALETTES } from './theme/palettes.ts'
import { createViewerController } from './viewer/controller.ts'
import { openFailureCategory, type OpenFailureCategory } from './viewer/errors.ts'
import { displayName } from './viewer/mailModel.ts'
import { MailView } from './viewer/MailView.tsx'
import { detectPlatform, type Platform } from './viewer/platform.ts'
import { activeTab } from './viewer/store.ts'
import { windowTitle } from './viewer/title.ts'
import { Toasts } from './viewer/Toasts.tsx'

export interface AppProps {
  /** 启动时读到的设置（Rust 注入，或浏览器调试时的默认值） */
  initialSettings: AppSettings
  store: SettingsStore
  /** 原生窗口控制；不在 Tauri 中时为 null */
  windowControl?: WindowControl | null
  /** 与 Rust 的接口（D11）。不传时用没有任何文件的模拟实现，测试里传自己的模拟 */
  api?: ViewerApi
  /** 快捷键和提示按平台显示；不传时检测当前平台 */
  platform?: Platform
}

// 每个 App 持有自己的 i18n 实例，初始语言就是启动时的设置，首次渲染文字即正确
export default function App({ initialSettings, store, windowControl = null, api, platform }: AppProps) {
  const [i18n] = useState(() => createI18n(initialSettings.language))
  const [viewerApi] = useState(() => api ?? createMockApi())
  const [os] = useState(() => platform ?? detectPlatform())
  return (
    <I18nextProvider i18n={i18n}>
      <AppContent initialSettings={initialSettings} store={store} windowControl={windowControl} api={viewerApi} platform={os} />
    </I18nextProvider>
  )
}

const ERROR_KEYS = {
  unsupported: { reason: 'error.unsupported.reason', title: 'error.unsupported.title', body: 'error.unsupported.body' },
  corrupt: { reason: 'error.corrupt.reason', title: 'error.corrupt.title', body: 'error.corrupt.body' },
  unreadable: { reason: 'error.unreadable.reason', title: 'error.unreadable.title', body: 'error.unreadable.body' },
  generic: { reason: 'error.generic.reason', title: 'error.generic.title', body: 'error.generic.body' },
} as const satisfies Record<OpenFailureCategory, Record<'reason' | 'title' | 'body', string>>

function AppContent({ initialSettings, store, windowControl, api, platform }: Required<AppProps>) {
  const { t, i18n } = useTranslation()
  const [settings, setSettings] = useState(initialSettings)
  const [settingsOpen, setSettingsOpen] = useState(false)
  const [controller] = useState(() => createViewerController(api))
  const viewer = useSyncExternalStore(controller.store.subscribe, controller.store.getState)
  const current = activeTab(viewer)
  const modalOpen = settingsOpen || viewer.blocked !== null
  const shortcut = platform === 'mac' ? '⌘' : 'Ctrl+'

  // 先注册好事件监听，再调用 app_ready（D11）
  useEffect(() => controller.start(), [controller])

  // 打开 ⌘O / Ctrl+O，关闭当前标签页 ⌘W / Ctrl+W。有对话框时不响应。
  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      const modifier = platform === 'mac' ? event.metaKey && !event.ctrlKey : event.ctrlKey && !event.metaKey
      if (!modifier || event.altKey || event.shiftKey) return
      const key = event.key.toLowerCase()
      if (key !== 'o' && key !== 'w') return
      event.preventDefault()
      if (modalOpen || event.repeat) return
      if (key === 'o') void controller.openDialog()
      else controller.closeActiveTab()
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [controller, platform, modalOpen])

  // macOS 原生菜单的“关闭标签页”（⌘W）和“设置…”（⌘,）：焦点在正文 iframe 里时网页收不到键盘事件，所以由 Rust 发事件过来。
  // 和键盘快捷键一样，有对话框时不响应。事件处理函数里读最新状态，所以监听只注册一次。
  const settingsOpenRef = useRef(settingsOpen)
  useEffect(() => {
    settingsOpenRef.current = settingsOpen
  }, [settingsOpen])
  useEffect(() => {
    let stopped = false
    const unlisteners: Unlisten[] = []
    // 注册是异步的：如果在等待期间已经卸载，注册完成后立刻取消
    const track = (registration: Promise<Unlisten>) =>
      registration.then(
        (unlisten) => {
          if (stopped) unlisten()
          else unlisteners.push(unlisten)
        },
        (error: unknown) => console.error('Failed to listen for a menu event', error),
      )
    void track(
      api.onCloseActiveTab(() => {
        if (!settingsOpenRef.current && !controller.store.getState().blocked) controller.closeActiveTab()
      }),
    )
    void track(
      api.onOpenSettings(() => {
        if (!controller.store.getState().blocked) setSettingsOpen(true)
      }),
    )
    return () => {
      stopped = true
      for (const unlisten of unlisteners.splice(0)) unlisten()
    }
  }, [api, controller])

  // 窗口标题跟随当前邮件（D11 补充）
  const title = windowTitle(current)
  useEffect(() => {
    if (!windowControl) return
    windowControl.setTitle(title).catch((error: unknown) => console.error('Failed to update the window title', error))
  }, [windowControl, title])

  // 设置变化后立刻写到 <html>（在绘制之前）
  useLayoutEffect(() => {
    applyDocumentSettings(document.documentElement, settings)
  }, [settings])

  // 同步原生窗口的底色和标题栏主题；跟随系统时，系统外观变化也要更新
  useEffect(() => {
    if (!windowControl) return
    if (settings.mode !== 'system') {
      void syncWindow(windowControl, settings.mode, settings.palette, false)
      return
    }
    const watcher = watchSystemDark((dark) => void syncWindow(windowControl, 'system', settings.palette, dark))
    void syncWindow(windowControl, 'system', settings.palette, watcher.current)
    return watcher.stop
  }, [windowControl, settings.mode, settings.palette])

  // 立即生效并保存；选中的就是当前值时什么也不做
  const update = useCallback(
    <K extends SettingKey>(key: K, value: AppSettings[K]) => {
      if (settings[key] === value) return
      setSettings((prev) => ({ ...prev, [key]: value }))
      if (key === 'language') void i18n.changeLanguage(value)
      void persistSetting(store, key, value)
    },
    [i18n, store, settings],
  )

  const paletteText = settings.language === 'zh-CN' ? 'zh' : 'en'
  const palettes = useMemo(
    () => PALETTES.map((p) => ({ id: p.id, name: p.name[paletteText], description: p.blurb[paletteText] })),
    [paletteText],
  )

  const unnamed = t('attachments.unnamed')
  const tabs: TabItem[] = viewer.tabs.map((tab) => ({
    id: tab.id,
    label: displayName(tab.kind === 'mail' ? tab.trail[0].fileName : tab.fileName),
    title: tab.kind === 'mail' ? tab.trail[0].fileName : tab.fileName,
    status: tab.kind === 'error' ? 'error' : 'ok',
  }))

  let content
  if (current?.kind === 'mail') {
    content = <MailView key={current.id} tab={current} controller={controller} />
  } else if (current?.kind === 'error') {
    const keys = ERROR_KEYS[openFailureCategory(current.code)]
    content = (
      <OpenErrorState
        reasonLabel={t(keys.reason)}
        title={t(keys.title)}
        fileName={current.fileName}
        body={t(keys.body)}
        actions={
          <>
            <Button variant="secondary" icon="folder" onClick={() => void controller.openDialog()}>
              {t('error.openAnother')}
            </Button>
            <Button onClick={() => controller.closeTab(current.id)}>{t('tabs.close')}</Button>
          </>
        }
      />
    )
  }

  const dragging = viewer.dragging
    ? { title: t('drop.title', { count: viewer.dragging.count }), body: t('drop.body') }
    : undefined

  const trail = current?.kind === 'mail' ? current.trail : []

  return (
    <AppShell>
      <TabBar
        label={t('tabs.label')}
        tabs={tabs}
        activeId={viewer.activeId ?? ''}
        onSelect={controller.selectTab}
        onClose={controller.closeTab}
        closeLabel={(tab) => t('tabs.closeNamed', { name: tab.label })}
        addLabel={t('tabs.new')}
        onAdd={() => void controller.openDialog()}
        panelId="main"
      />
      <Toolbar
        start={
          current && (
            <>
              <Button variant="secondary" icon="folder" title={`${t('toolbar.openTip')} (${shortcut}O)`} onClick={() => void controller.openDialog()}>
                <span className="tool-label">{t('toolbar.open')}</span>
              </Button>
              <Breadcrumbs
                label={t('breadcrumbs.label')}
                items={trail.map((level) => ({ id: String(level.handle), label: displayName(level.fileName || unnamed), title: level.fileName || unnamed }))}
                onNavigate={(index) => current && controller.navigate(current.id, index)}
                backLabel={t('toolbar.back')}
              />
            </>
          )
        }
        end={<IconButton icon="settings" label={t('toolbar.settings')} onClick={() => setSettingsOpen(true)} />}
      />
      <MainArea id="main" labelledBy={tabs.length > 1 && current ? `tab-${current.id}` : undefined}>
        {content ?? (
          <EmptyState
            title={t('empty.title')}
            body={t('empty.body')}
            openLabel={t('empty.open')}
            onOpen={() => void controller.openDialog()}
            shortcutHint={
              <>
                {t('empty.orPress')} <Kbd>{`${shortcut}O`}</Kbd>
              </>
            }
            privacyNote={t('empty.privacy')}
            dragging={dragging}
          />
        )}
        {current && dragging && <DropOverlay {...dragging} />}
        <Toasts toasts={viewer.toasts} controller={controller} />
      </MainArea>
      {viewer.blocked && (
        <ExecutableBlockedDialog
          title={t('executable.title', { name: viewer.blocked.fileName || unnamed })}
          body={t('executable.body')}
          note={t('executable.extra')}
          cancelLabel={t('common.cancel')}
          saveLabel={t('attachments.save')}
          onSave={() => void controller.saveBlocked()}
          onCancel={controller.dismissBlocked}
        />
      )}
      {settingsOpen && (
        <SettingsDialog
          labels={{
            title: t('settings.title'),
            close: t('common.close'),
            done: t('common.done'),
            language: t('settings.language'),
            languageHint: t('settings.languageHint'),
            appearance: t('settings.appearance'),
            mode: t('settings.mode'),
            palette: t('settings.palette'),
            selected: t('settings.selected'),
            appearanceHint: t('settings.appearanceHint'),
          }}
          languages={LANGUAGE_OPTIONS}
          language={settings.language}
          onLanguageChange={(v) => update('language', v)}
          modes={[
            { value: 'light', label: t('settings.modeLight'), icon: 'sun' },
            { value: 'dark', label: t('settings.modeDark'), icon: 'moon' },
            { value: 'system', label: t('settings.modeSystem'), icon: 'monitor' },
          ]}
          mode={settings.mode}
          onModeChange={(v) => update('mode', v)}
          palettes={palettes}
          palette={settings.palette}
          onPaletteChange={(v) => update('palette', v)}
          onClose={() => setSettingsOpen(false)}
        />
      )}
    </AppShell>
  )
}
