// 测试用的假环境：系统外观（matchMedia）和原生窗口控制
import type { WindowControl } from '../settings/windowControl.ts'

// 可控的系统外观。jsdom 没有 matchMedia，这里安装一个，并允许测试切换系统的深色状态。
export function installSystemAppearance(initialDark: boolean) {
  let dark = initialDark
  const listeners = new Set<(event: MediaQueryListEvent) => void>()
  Object.defineProperty(window, 'matchMedia', {
    configurable: true,
    writable: true,
    value: (query: string) => ({
      media: query,
      get matches() {
        return query.includes('prefers-color-scheme: dark') ? dark : false
      },
      addEventListener: (_type: string, listener: (event: MediaQueryListEvent) => void) => listeners.add(listener),
      removeEventListener: (_type: string, listener: (event: MediaQueryListEvent) => void) => listeners.delete(listener),
    }),
  })
  return {
    get dark() {
      return dark
    },
    /** 模拟用户在系统设置里切换浅色 / 深色 */
    setDark(value: boolean) {
      dark = value
      for (const listener of [...listeners]) listener({ matches: value } as MediaQueryListEvent)
    },
    listenerCount: () => listeners.size,
    restore() {
      Reflect.deleteProperty(window, 'matchMedia')
    },
  }
}

export function createFakeWindowControl() {
  const backgrounds: string[] = []
  const themes: ('light' | 'dark' | null)[] = []
  const control: WindowControl = {
    setBackgroundColor: (color) => {
      backgrounds.push(color)
      return Promise.resolve()
    },
    setTheme: (theme) => {
      themes.push(theme)
      return Promise.resolve()
    },
  }
  return { control, backgrounds, themes }
}
