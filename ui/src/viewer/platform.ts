export type Platform = 'mac' | 'other'

// 快捷键提示和修饰键按平台区分：macOS 用 ⌘，其余用 Ctrl
export function detectPlatform(): Platform {
  const hint = (navigator as Navigator & { userAgentData?: { platform?: string } }).userAgentData?.platform ?? navigator.platform ?? navigator.userAgent
  return /mac/i.test(hint) ? 'mac' : 'other'
}
