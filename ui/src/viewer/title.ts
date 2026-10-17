// 窗口标题（D11 补充）：有邮件时是“<主题，没有主题就用文件名> — EmailFileViewer”，空状态时只有应用名。
// 不依赖 React，标题怎么随标签页和嵌套层级变化都在这里。
import type { Level, Tab } from './store.ts'

export const APP_TITLE = 'EmailFileViewer'

/** 标题只显示一行：换行和连续空白折成一个空格 */
function oneLine(text: string | null | undefined): string {
  return (text ?? '').replace(/\s+/g, ' ').trim()
}

/**
 * 跟随正在看的那一封邮件，嵌套时是当前这一层：主题；没有主题用这一层的文件名（嵌套邮件是附件名）；
 * 还没有就用最外层文件的名字，保证有邮件时标题总能区分是哪个文件。
 */
function mailName(trail: readonly Level[]): string {
  const current = trail[trail.length - 1]
  return oneLine(current.document.subject) || oneLine(current.fileName) || oneLine(trail[0].fileName)
}

/** 当前标签页对应的窗口标题；没有标签页（空状态）时是应用名。打开失败的文件占一个标签页，标题用它的文件名 */
export function windowTitle(tab: Tab | null): string {
  if (!tab) return APP_TITLE
  const name = tab.kind === 'mail' ? mailName(tab.trail) : oneLine(tab.fileName)
  return name ? `${name} — ${APP_TITLE}` : APP_TITLE
}
