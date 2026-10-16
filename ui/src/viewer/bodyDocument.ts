// 邮件正文的 iframe 文档（design.md D5）。
// 安全边界是沙箱 iframe；这里的 CSP 阻止远程资源，sidecar 的 HTML 清理是第二道防线，前端不再清理。
import type { BodySourceFormat } from '../api/types.ts'

/**
 * iframe 的 sandbox 属性，严格等于这个值。只留 allow-popups 系列是为了让链接点击产生新窗口请求，
 * 由 Rust 拦截后交给系统浏览器（5.7）。没有 allow-scripts、allow-same-origin、allow-forms：
 * 正文是一个不透明来源、不能运行脚本、不能提交表单的文档。
 */
export const BODY_SANDBOX = 'allow-popups allow-popups-to-escape-sandbox'

/** 默认的 CSP：阻止一切远程资源，只允许 data: 图片和字体、内联样式 */
export const BODY_CSP_BLOCKED = "default-src 'none'; img-src data:; style-src 'unsafe-inline'; font-src data:; base-uri 'none'; form-action 'none'"

/** 用户对当前这封邮件选择“加载远程内容”之后：图片、样式表、字体可以来自 https: 和 http:，其余不变 */
export const BODY_CSP_REMOTE =
  "default-src 'none'; img-src data: https: http:; style-src 'unsafe-inline' https: http:; font-src data: https: http:; base-uri 'none'; form-action 'none'"

export function bodyCsp(remoteAllowed: boolean): string {
  return remoteAllowed ? BODY_CSP_REMOTE : BODY_CSP_BLOCKED
}

// 纸面的基础样式：始终是浅色底和深色文字，不受应用主题影响。
// 只设置根元素的颜色和字体，不设置 body 的背景或颜色——邮件自己的 <body bgcolor> / text 属性
// 优先级低于作者样式，设置了会把它们盖掉。邮件自己的样式写在后面，照常生效。
const BASE_STYLE =
  ':root{color-scheme:light;color:#1f2421;font:14px/1.6 system-ui,-apple-system,"Segoe UI",Ubuntu,"PingFang SC","Microsoft YaHei UI",sans-serif}'
// 纯文本和 RTF 的 HTML 是 Core 生成的片段，没有自己的版式，由这里加上边距；HTML 邮件按自己的样式显示
const CONVERTED_STYLE = 'body{margin:0;padding:22px 28px}pre{font:inherit}'

/**
 * 拼出 iframe 的 srcdoc。`<meta http-equiv="Content-Security-Policy">` 永远是文档里的第一个元素，
 * 邮件内容在它后面原样放进 body：邮件里再带 CSP meta 也只会让限制更严，改不了它。
 */
export function buildBodyDocument(html: string | null, format: BodySourceFormat, remoteAllowed: boolean): string {
  const style = format === 'PlainText' || format === 'Rtf' ? BASE_STYLE + CONVERTED_STYLE : BASE_STYLE
  return (
    `<!doctype html><html><head><meta http-equiv="Content-Security-Policy" content="${bodyCsp(remoteAllowed)}">` +
    `<style>${style}</style></head><body>${html ?? ''}</body></html>`
  )
}
