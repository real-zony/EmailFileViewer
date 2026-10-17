import { describe, expect, it } from 'vitest'
import { fixture } from '../../dev/fixtures.ts'
import { BODY_CSP_BLOCKED, BODY_CSP_REMOTE, BODY_SANDBOX, bodyCsp, buildBodyDocument } from '../bodyDocument.ts'

// 这两条字符串是 D5 和任务 7.5 的规定，写死在测试里，改了就要有人明确改规格
const BLOCKED = "default-src 'none'; img-src data:; style-src 'unsafe-inline'; font-src data:; base-uri 'none'; form-action 'none'"
const ALLOWED =
  "default-src 'none'; img-src data: https: http:; style-src 'unsafe-inline' https: http:; font-src data: https: http:; media-src https: http:; base-uri 'none'; form-action 'none'"

const parse = (srcdoc: string) => new DOMParser().parseFromString(srcdoc, 'text/html')
const directives = (csp: string) => new Map(csp.split(';').map((d) => d.trim().split(/\s+(.*)/s) as [string, string]))

describe('iframe 的 sandbox 和 CSP（D5）', () => {
  it('sandbox 严格等于 "allow-popups allow-popups-to-escape-sandbox"，不带 allow-scripts、allow-same-origin、allow-forms', () => {
    expect(BODY_SANDBOX).toBe('allow-popups allow-popups-to-escape-sandbox')
    const tokens = BODY_SANDBOX.split(' ')
    for (const forbidden of ['allow-scripts', 'allow-same-origin', 'allow-forms', 'allow-top-navigation', 'allow-modals', 'allow-downloads']) {
      expect(tokens).not.toContain(forbidden)
    }
  })

  it('默认 CSP：阻止一切远程资源', () => {
    expect(BODY_CSP_BLOCKED).toBe(BLOCKED)
    expect(bodyCsp(false)).toBe(BLOCKED)
  })

  it('用户允许后的 CSP：img-src、style-src、font-src 加入 https: http:，新增 media-src https: http:，其余不变', () => {
    expect(BODY_CSP_REMOTE).toBe(ALLOWED)
    expect(bodyCsp(true)).toBe(ALLOWED)
    const blocked = directives(BLOCKED)
    const allowed = directives(ALLOWED)
    // 默认 CSP 没有 media-src（回落到 default-src 'none'，音视频被阻止）；放宽后只多出这一条
    expect(blocked.has('media-src')).toBe(false)
    expect([...allowed.keys()].filter((name) => name !== 'media-src')).toEqual([...blocked.keys()])
    expect(allowed.get('media-src')).toBe('https: http:')
    for (const [name, value] of allowed) {
      if (name === 'media-src') continue
      if (['img-src', 'style-src', 'font-src'].includes(name)) expect(value, name).toBe(`${blocked.get(name)} https: http:`)
      else expect(value, name).toBe(blocked.get(name))
    }
  })
})

describe('buildBodyDocument', () => {
  const email = '<p id="mine">hello <a href="https://example.com" target="_blank" title="https://example.com">link</a></p>'

  it('srcdoc 原样包含 sidecar 已清理过的 HTML，前端不再改写它', () => {
    for (const name of ['html-malicious', 'link-mismatch', 'marketing-remote', 'cid-inline', 'segmented-body', 'rtf-only', 'plain-text', 'charset-gbk']) {
      const html = fixture(name).document!.body.html!
      expect(buildBodyDocument(html, 'Html', false), name).toContain(html)
      expect(buildBodyDocument(html, 'Html', true), name).toContain(html)
    }
  })

  it('默认和允许后的 srcdoc 只有 CSP 这一处不同，正文部分完全相同', () => {
    const blocked = buildBodyDocument(email, 'Html', false)
    const allowed = buildBodyDocument(email, 'Html', true)
    expect(blocked).not.toBe(allowed)
    expect(blocked.replace(BLOCKED, '<CSP>')).toBe(allowed.replace(ALLOWED, '<CSP>'))
    expect(parse(blocked).querySelector('meta[http-equiv="Content-Security-Policy"]')!.getAttribute('content')).toBe(BLOCKED)
    expect(parse(allowed).querySelector('meta[http-equiv="Content-Security-Policy"]')!.getAttribute('content')).toBe(ALLOWED)
  })

  it('CSP meta 在文档最前面：它是 head 里的第一个元素，位置在邮件内容之前', () => {
    const srcdoc = buildBodyDocument(email, 'Html', false)
    expect(srcdoc.startsWith(`<!doctype html><html><head><meta http-equiv="Content-Security-Policy" content="${BLOCKED}">`)).toBe(true)
    expect(srcdoc.indexOf('Content-Security-Policy')).toBeLessThan(srcdoc.indexOf(email))
    const first = parse(srcdoc).head.firstElementChild!
    expect(first.tagName).toBe('META')
    expect(first.getAttribute('http-equiv')).toBe('Content-Security-Policy')
    expect(first.getAttribute('content')).toBe(BLOCKED)
  })

  it('x-dns-prefetch-control 关闭预解析：紧跟在 CSP meta 之后，同样在邮件内容之前，默认和允许后都有', () => {
    const dnsMeta = '<meta http-equiv="x-dns-prefetch-control" content="off">'
    for (const remote of [false, true]) {
      const srcdoc = buildBodyDocument(email, 'Html', remote)
      expect(srcdoc.startsWith(`<!doctype html><html><head><meta http-equiv="Content-Security-Policy" content="${bodyCsp(remote)}">${dnsMeta}`), String(remote)).toBe(true)
      expect(srcdoc.indexOf(dnsMeta)).toBeLessThan(srcdoc.indexOf(email))
      const metas = [...parse(srcdoc).head.querySelectorAll('meta')]
      expect(metas.map((m) => m.getAttribute('http-equiv'))).toEqual(['Content-Security-Policy', 'x-dns-prefetch-control'])
      expect(metas[1].getAttribute('content')).toBe('off')
    }
  })

  // 邮件内容放在 body 里，在 CSP meta 之后：不论它写了什么，第一条 CSP 都不变
  it.each([
    ['自带更宽松的 CSP meta', '<meta http-equiv="Content-Security-Policy" content="default-src *; img-src *"><p>x</p>'],
    ['自带 <base>', '<base href="https://evil.example/"><p>x</p>'],
    ['整份 HTML 文档（html、head、body 标签）', '<html lang="x"><head><meta http-equiv="Content-Security-Policy" content="img-src *"><title>t</title></head><body onload="x()"><p>x</p></body></html>'],
    ['试图提前结束 head 和 style', '</style></head><body><p>x</p></body></html><meta http-equiv="Content-Security-Policy" content="img-src *">'],
    ['未闭合的注释和标签', '<!-- never closed <meta http-equiv="Content-Security-Policy" content="img-src *"><div'],
    ['<meta http-equiv="refresh">', '<meta http-equiv="refresh" content="0;url=https://evil.example/"><p>x</p>'],
  ])('邮件内容%s，也改不了我们的 CSP', (_label, hostile) => {
    for (const remote of [false, true]) {
      const srcdoc = buildBodyDocument(hostile, 'Html', remote)
      const ours = srcdoc.indexOf(`<meta http-equiv="Content-Security-Policy" content="${bodyCsp(remote)}">`)
      expect(ours).toBe('<!doctype html><html><head>'.length)
      expect(srcdoc).toContain(hostile)
      const doc = parse(srcdoc)
      const metas = [...doc.querySelectorAll('meta[http-equiv="Content-Security-Policy"]')]
      expect(metas[0]).toBe(doc.head.firstElementChild)
      expect(metas[0].getAttribute('content')).toBe(bodyCsp(remote))
    }
  })

  it('没有正文时是一个空的纸面', () => {
    const doc = parse(buildBodyDocument(null, 'None', false))
    expect(doc.body.innerHTML).toBe('')
    expect(doc.head.firstElementChild!.getAttribute('http-equiv')).toBe('Content-Security-Policy')
  })

  it('所有正文都在浅色纸面上，显式设置深色文字，不受应用主题影响', () => {
    for (const format of ['Html', 'Rtf', 'PlainText', 'None'] as const) {
      const srcdoc = buildBodyDocument(email, format, false)
      expect(srcdoc, format).toContain('color-scheme:light')
      expect(srcdoc, format).toContain('color:#1f2421')
      // 不设置 html / body 的背景，也不设置 body 的颜色：邮件自己的 bgcolor / text 属性优先级低于作者样式，设置了会被盖掉
      expect(srcdoc, format).not.toMatch(/(html|body)\s*\{[^}]*background/)
      expect(srcdoc, format).not.toMatch(/body\s*\{[^}]*\bcolor\s*:/)
    }
  })

  it('纯文本和 RTF 是 Core 生成的片段，由这里加边距；HTML 邮件按自己的样式显示', () => {
    expect(buildBodyDocument(email, 'PlainText', false)).toContain('padding:22px 28px')
    expect(buildBodyDocument(email, 'Rtf', false)).toContain('padding:22px 28px')
    expect(buildBodyDocument(email, 'Html', false)).not.toContain('padding:22px 28px')
  })
})
