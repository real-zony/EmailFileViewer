import { afterEach, describe, expect, it, vi } from 'vitest'
import { createFakeWindowControl, installSystemAppearance } from '../../test/fakes.ts'
import { PALETTES } from '../../theme/palettes.ts'
import { nativeTheme, resolveMode, windowBackground } from '../appearance.ts'
import { syncWindow, watchSystemDark } from '../windowControl.ts'

describe('窗口底色与模式解析', () => {
  it('跟随系统时按系统外观解析，固定模式不受系统影响', () => {
    expect(resolveMode('system', true)).toBe('dark')
    expect(resolveMode('system', false)).toBe('light')
    expect(resolveMode('light', true)).toBe('light')
    expect(resolveMode('dark', false)).toBe('dark')
  })

  it('窗口底色是所选方案当前模式的 desk，每套方案、两种模式都有', () => {
    for (const p of PALETTES) {
      expect(windowBackground(p.id, 'light')).toBe(p.light.desk)
      expect(windowBackground(p.id, 'dark')).toBe(p.dark.desk)
    }
    expect(() => windowBackground('neon' as never, 'light')).toThrow(/unknown palette/)
  })

  it('原生标题栏主题：固定模式强制，跟随系统时为 null', () => {
    expect(nativeTheme('light')).toBe('light')
    expect(nativeTheme('dark')).toBe('dark')
    expect(nativeTheme('system')).toBeNull()
  })
})

describe('syncWindow', () => {
  it('同时更新底色和标题栏主题', async () => {
    const win = createFakeWindowControl()
    await syncWindow(win.control, 'dark', 'plum', false)
    expect(win.backgrounds).toEqual(['#18141a'])
    expect(win.themes).toEqual(['dark'])
  })

  it('跟随系统：底色按系统外观，标题栏交还系统', async () => {
    const win = createFakeWindowControl()
    await syncWindow(win.control, 'system', 'verdigris', true)
    await syncWindow(win.control, 'system', 'verdigris', false)
    expect(win.backgrounds).toEqual(['#121715', '#e6eae8'])
    expect(win.themes).toEqual([null, null])
  })

  it('一项失败不影响另一项，也不抛出（只记录错误）', async () => {
    const error = vi.spyOn(console, 'error').mockImplementation(() => {})
    const backgrounds: string[] = []
    await syncWindow(
      { setTheme: () => Promise.reject(new Error('denied')), setBackgroundColor: (c) => (backgrounds.push(c), Promise.resolve()), setTitle: () => Promise.resolve() },
      'light',
      'indigo',
      false,
    )
    expect(backgrounds).toEqual(['#e7e9ef'])
    expect(error).toHaveBeenCalledTimes(1)
    error.mockRestore()
  })
})

describe('watchSystemDark', () => {
  let system: ReturnType<typeof installSystemAppearance>
  afterEach(() => system?.restore())

  it('返回当前值，系统外观变化时通知，停止后不再通知', () => {
    system = installSystemAppearance(true)
    const seen: boolean[] = []
    const watcher = watchSystemDark((dark) => seen.push(dark))
    expect(watcher.current).toBe(true)
    system.setDark(false)
    system.setDark(true)
    expect(seen).toEqual([false, true])
    watcher.stop()
    expect(system.listenerCount()).toBe(0)
    system.setDark(false)
    expect(seen).toEqual([false, true])
  })

  it('环境没有 matchMedia 时不报错', () => {
    const watcher = watchSystemDark(() => {})
    expect(watcher.current).toBe(false)
    watcher.stop()
  })
})
