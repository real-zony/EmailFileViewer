import { useCallback, useEffect, useLayoutEffect, useMemo, useState } from 'react'
import { I18nextProvider, useTranslation } from 'react-i18next'
import { AppShell, EmptyState, IconButton, MainArea, SettingsDialog, Toolbar } from './components/index.ts'
import { createI18n } from './i18n/index.ts'
import { applyDocumentSettings } from './settings/appearance.ts'
import { LANGUAGE_OPTIONS } from './settings/language.ts'
import { persistSetting, type SettingsStore } from './settings/store.ts'
import type { AppSettings, SettingKey } from './settings/types.ts'
import { syncWindow, watchSystemDark, type WindowControl } from './settings/windowControl.ts'
import { PALETTES } from './theme/palettes.ts'

export interface AppProps {
  /** 启动时读到的设置（Rust 注入，或浏览器调试时的默认值） */
  initialSettings: AppSettings
  store: SettingsStore
  /** 原生窗口控制；不在 Tauri 中时为 null */
  windowControl?: WindowControl | null
}

// 每个 App 持有自己的 i18n 实例，初始语言就是启动时的设置，首次渲染文字即正确
export default function App({ initialSettings, store, windowControl = null }: AppProps) {
  const [i18n] = useState(() => createI18n(initialSettings.language))
  return (
    <I18nextProvider i18n={i18n}>
      <AppContent initialSettings={initialSettings} store={store} windowControl={windowControl} />
    </I18nextProvider>
  )
}

function AppContent({ initialSettings, store, windowControl }: Required<AppProps>) {
  const { t, i18n } = useTranslation()
  const [settings, setSettings] = useState(initialSettings)
  const [settingsOpen, setSettingsOpen] = useState(false)

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

  return (
    <AppShell>
      <Toolbar end={<IconButton icon="settings" label={t('toolbar.settings')} onClick={() => setSettingsOpen(true)} />} />
      <MainArea>
        <EmptyState
          title={t('empty.title')}
          body={t('empty.body')}
          openLabel={t('empty.open')}
          // 打开文件、拖放和快捷键提示在 7.3 接入
          onOpen={() => {}}
          privacyNote={t('empty.privacy')}
        />
      </MainArea>
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
