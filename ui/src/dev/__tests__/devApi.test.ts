import { afterEach, describe, expect, it, vi } from 'vitest'
import { createDevApi } from '../devApi.ts'

afterEach(() => {
  delete window.__efvDev
})

describe('window.__efvDev（浏览器里调试用的挂钩）', () => {
  it('可以触发原生菜单的两个事件：关闭标签页（⌘W）和设置（⌘,）', async () => {
    const api = createDevApi()
    const closeTab = vi.fn()
    const openSettings = vi.fn()
    await api.onCloseActiveTab(closeTab)
    await api.onOpenSettings(openSettings)

    window.__efvDev!.closeActiveTab()
    expect(closeTab).toHaveBeenCalledTimes(1)
    expect(openSettings).not.toHaveBeenCalled()

    window.__efvDev!.openSettings()
    expect(openSettings).toHaveBeenCalledTimes(1)
  })
})
