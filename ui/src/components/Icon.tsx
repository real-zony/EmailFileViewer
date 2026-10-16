import type { ReactNode } from 'react'

// 线性图标，24×24，描边继承 currentColor。路径与草稿 app.js 的 ICON 一致。
const ICONS = {
  folder: <path d="M3.5 7.5a2 2 0 0 1 2-2h3.6l2 2h7.4a2 2 0 0 1 2 2v7a2 2 0 0 1-2 2h-13a2 2 0 0 1-2-2z" />,
  save: <path d="M12 4.5v10m0 0-4-4m4 4 4-4M5 19.5h14" />,
  settings: <><path d="M4 7h9M17 7h3M4 17h3M11 17h9" /><circle cx="15" cy="7" r="2" /><circle cx="9" cy="17" r="2" /></>,
  clip: <path d="m19.5 11.2-7.4 7.4a4.6 4.6 0 0 1-6.5-6.5l8-8a3.1 3.1 0 0 1 4.4 4.4l-8 8a1.5 1.5 0 0 1-2.2-2.2l7.4-7.4" />,
  shield: <path d="M12 3.2 19 6v5.3c0 4.3-2.9 7.9-7 9.5-4.1-1.6-7-5.2-7-9.5V6z" />,
  lock: <><rect x="5" y="10.5" width="14" height="10" rx="2" /><path d="M8.5 10.5V7.8a3.5 3.5 0 0 1 7 0v2.7" /></>,
  seal: <><circle cx="12" cy="9.5" r="5.5" /><path d="m9 14.2-1.2 7 4.2-2.2 4.2 2.2-1.2-7" /></>,
  alert: <><path d="M10.3 4.4 2.9 17.6A2 2 0 0 0 4.6 20.5h14.8a2 2 0 0 0 1.7-2.9L13.7 4.4a2 2 0 0 0-3.4 0z" /><path d="M12 9.5v4.5M12 17.2v.1" /></>,
  imageOff: <><rect x="3.5" y="5" width="17" height="14" rx="2" /><path d="m3.5 15.5 4.5-4.5 4 4 2.5-2.5 6 6M3 3l18 18" /></>,
  x: <path d="M6.5 6.5l11 11M17.5 6.5l-11 11" />,
  back: <path d="M14.5 6 8.5 12l6 6" />,
  envelope: <><rect x="3.5" y="5.5" width="17" height="13" rx="2" /><path d="m4 7 8 6 8-6" /></>,
  file: <><path d="M6.5 3.5h7l4.5 4.5v12.5h-11.5z" /><path d="M13.5 3.5V8H18" /></>,
  fileX: <><path d="M6.5 3.5h7l4.5 4.5v12.5h-11.5z" /><path d="M13.5 3.5V8H18M10 12.5l4 4M14 12.5l-4 4" /></>,
  plus: <path d="M12 5.5v13M5.5 12h13" />,
  info: <><circle cx="12" cy="12" r="8.5" /><path d="M12 11v5.2M12 7.9v.1" /></>,
  refresh: <path d="M19.5 12a7.5 7.5 0 1 1-2.2-5.3M19.5 4.5v4h-4" />,
  check: <path d="m5.5 12.5 4 4 9-9" />,
  sun: <><circle cx="12" cy="12" r="4" /><path d="M12 2.8v2M12 19.2v2M21.2 12h-2M4.8 12h-2M18.5 5.5l-1.4 1.4M6.9 17.1l-1.4 1.4M18.5 18.5l-1.4-1.4M6.9 6.9 5.5 5.5" /></>,
  moon: <path d="M19.5 14.5A7.5 7.5 0 0 1 9.5 4.5a7.5 7.5 0 1 0 10 10z" />,
  monitor: <><rect x="3.5" y="4.5" width="17" height="12" rx="2" /><path d="M9 20h6M12 16.5V20" /></>,
} satisfies Record<string, ReactNode>

export type IconName = keyof typeof ICONS

export interface IconProps {
  name: IconName
  className?: string
}

// 图标始终是装饰性的（aria-hidden）；可访问名称由所在的按钮或文字提供
export function Icon({ name, className }: IconProps) {
  return (
    <svg
      className={className}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.6}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      focusable="false"
    >
      {ICONS[name]}
    </svg>
  )
}
