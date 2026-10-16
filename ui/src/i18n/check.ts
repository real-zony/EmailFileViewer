// i18n 资源的一致性检查，供 scripts/check-i18n.ts 和测试共用。
// 本文件会被 Node 直接执行，只能使用可擦除的 TS 语法，导入路径要带 .ts 后缀。

// 展平成 "a.b.c" -> 文案。叶子必须是字符串；数组、数字、null 等直接报错。
export function flatten(messages: unknown, prefix = ''): { entries: Map<string, string>; problems: string[] } {
  const entries = new Map<string, string>()
  const problems: string[] = []
  const walk = (node: unknown, path: string) => {
    if (typeof node === 'string') {
      entries.set(path, node)
    } else if (typeof node === 'object' && node !== null && !Array.isArray(node)) {
      for (const [k, v] of Object.entries(node)) walk(v, path === '' ? k : `${path}.${k}`)
    } else {
      problems.push(`${path === '' ? '(root)' : path}: 值必须是字符串或嵌套对象`)
    }
  }
  walk(messages, prefix)
  return { entries, problems }
}

const PLACEHOLDER = /\{\{\s*-?\s*([^{},]+?)\s*(?:,[^{}]*)?\}\}/g
const NESTING = /\$t\(\s*([^)\s,]+)/g

// 文案中的插值参数名和嵌套引用，去重排序。{{name}}、{{name, format}}、{{- name}}、$t(key) 都能识别。
export function placeholders(text: string): string[] {
  const names = new Set<string>()
  for (const m of text.matchAll(PLACEHOLDER)) names.add(m[1])
  for (const m of text.matchAll(NESTING)) names.add(`$t(${m[1]})`)
  return [...names].sort()
}

// 去掉 {{...}} 之后还剩下花括号，说明写成了单花括号（例如草稿里的 {n}），i18next 不会代入
function strayBraces(text: string): boolean {
  return /[{}]/.test(text.replace(/\{\{[^{}]*\}\}/g, ''))
}

export function checkLocales(locales: Record<string, unknown>, reference: string): string[] {
  const errors: string[] = []
  if (!(reference in locales)) return [`缺少基准语言 ${reference} 的资源文件`]

  const flat = new Map<string, Map<string, string>>()
  for (const [lang, messages] of Object.entries(locales)) {
    const { entries, problems } = flatten(messages)
    for (const p of problems) errors.push(`[${lang}] ${p}`)
    flat.set(lang, entries)
    for (const [key, text] of entries) {
      if (text.trim() === '') errors.push(`[${lang}] ${key}: 文案为空`)
      if (strayBraces(text)) errors.push(`[${lang}] ${key}: 含有单花括号，插值请写成 {{name}}`)
    }
  }

  const base = flat.get(reference)!
  for (const [lang, entries] of flat) {
    if (lang === reference) continue
    for (const key of base.keys()) {
      if (!entries.has(key)) errors.push(`[${lang}] 缺少 key: ${key}`)
    }
    for (const key of entries.keys()) {
      if (!base.has(key)) errors.push(`[${lang}] 多出的 key（${reference} 中没有）: ${key}`)
    }
    for (const [key, text] of entries) {
      const baseText = base.get(key)
      if (baseText === undefined) continue
      const a = placeholders(baseText).join(', ')
      const b = placeholders(text).join(', ')
      if (a !== b) errors.push(`[${lang}] ${key}: 插值参数与 ${reference} 不一致（${reference}: ${a || '无'}；${lang}: ${b || '无'}）`)
    }
  }
  return errors
}
