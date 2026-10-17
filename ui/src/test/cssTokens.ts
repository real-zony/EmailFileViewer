// 测试用的最小 CSS 级联计算：给定 <html> 上的 data-mode / data-palette 和系统是否为深色，
// 算出 themes.css 里每个令牌最终的值。jsdom 不会计算 @media 和级联，所以这里自己算。
// 只支持 themes.css 用到的语法：选择器由 :root 和 [attr="value"] 组成，
// 加上 @media (prefers-color-scheme: dark) 一层嵌套。

interface Rule {
  selectors: string[][]
  decls: Record<string, string>
  dark: boolean
  order: number
}

export interface RootAttributes {
  'data-mode'?: string
  'data-palette'?: string
}

function parseDeclarations(body: string): Record<string, string> {
  const decls: Record<string, string> = {}
  for (const part of body.split(';')) {
    const i = part.indexOf(':')
    if (i === -1) continue
    const name = part.slice(0, i).trim()
    // 值里可能有冒号（例如 url(...)），只按第一个冒号拆分
    if (name) decls[name] = part.slice(i + 1).trim()
  }
  return decls
}

function parseSelector(text: string): string[] {
  // 复合选择器拆成 ':root' 或 '[attr="value"]' 的列表
  return text.trim().match(/:root|\[[^\]]+\]/g) ?? []
}

export function parseThemeCss(css: string): Rule[] {
  const rules: Rule[] = []
  const source = css.replace(/\/\*[\s\S]*?\*\//g, '')
  let order = 0
  const readBlock = (start: number): number => {
    // 返回与 start 处的 '{' 匹配的 '}' 的位置
    let depth = 0
    for (let i = start; i < source.length; i++) {
      if (source[i] === '{') depth++
      else if (source[i] === '}' && --depth === 0) return i
    }
    throw new Error('CSS 花括号不匹配')
  }
  const parse = (from: number, to: number, dark: boolean) => {
    let i = from
    while (i < to) {
      const open = source.indexOf('{', i)
      if (open === -1 || open >= to) break
      const prelude = source.slice(i, open).trim()
      const close = readBlock(open)
      if (prelude.startsWith('@media')) {
        if (!/prefers-color-scheme:\s*dark/.test(prelude)) throw new Error(`不支持的媒体查询: ${prelude}`)
        parse(open + 1, close, true)
      } else {
        rules.push({
          selectors: prelude.split(',').map(parseSelector),
          decls: parseDeclarations(source.slice(open + 1, close)),
          dark,
          order: order++,
        })
      }
      i = close + 1
    }
  }
  parse(0, source.length, false)
  return rules
}

function matches(selector: string[], attrs: RootAttributes): boolean {
  return selector.every((part) => {
    if (part === ':root') return true
    const m = /^\[([\w-]+)="([^"]*)"\]$/.exec(part)
    if (!m) throw new Error(`不支持的选择器: ${part}`)
    return attrs[m[1] as keyof RootAttributes] === m[2]
  })
}

// <html> 元素上最终生效的声明（含 --efv-* 令牌和 color-scheme）
export function resolveRootStyle(css: string, attrs: RootAttributes, systemDark: boolean): Record<string, string> {
  const winners = new Map<string, { specificity: number; order: number; value: string }>()
  for (const rule of parseThemeCss(css)) {
    if (rule.dark && !systemDark) continue
    // 选择器列表里取匹配到的最高优先级；优先级 = 选择器的部件数（这里每个部件都是一个类/属性级别）
    const specificity = Math.max(-1, ...rule.selectors.filter((s) => matches(s, attrs)).map((s) => s.length))
    if (specificity < 0) continue
    for (const [name, value] of Object.entries(rule.decls)) {
      const cur = winners.get(name)
      if (!cur || specificity > cur.specificity || (specificity === cur.specificity && rule.order > cur.order)) {
        winners.set(name, { specificity, order: rule.order, value })
      }
    }
  }
  return Object.fromEntries([...winners].map(([k, v]) => [k, v.value]))
}
