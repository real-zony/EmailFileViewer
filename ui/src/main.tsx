import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import App from './App.tsx'
import { createSettingsStore, createWindowControl } from './platform/tauri.ts'
import { applyDocumentSettings } from './settings/appearance.ts'
import { readInitialSettings } from './settings/initial.ts'
import './theme/themes.css'
import './styles/app.css'

// 首次渲染之前同步读取并应用设置，避免语言和主题闪变。
// Rust 已经通过 initialization_script 在 <html> 上设置过同样的属性，这里是浏览器调试时的兜底。
const initialSettings = readInitialSettings()
applyDocumentSettings(document.documentElement, initialSettings)

const root = createRoot(document.getElementById('root')!)

// 组件展示页只在开发模式下可用（?showcase）。生产构建中 import.meta.env.DEV 为 false，这段代码和展示页都会被剔除。
if (import.meta.env.DEV && new URLSearchParams(window.location.search).has('showcase')) {
  void import('./showcase/Showcase.tsx').then(({ Showcase }) => {
    root.render(
      <StrictMode>
        <Showcase />
      </StrictMode>,
    )
  })
} else {
  root.render(
    <StrictMode>
      <App initialSettings={initialSettings} store={createSettingsStore()} windowControl={createWindowControl()} />
    </StrictMode>,
  )
}
