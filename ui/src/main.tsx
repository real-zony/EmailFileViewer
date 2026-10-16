import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import App from './App.tsx'
import './theme/themes.css'
import './styles/app.css'

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
      <App />
    </StrictMode>,
  )
}
