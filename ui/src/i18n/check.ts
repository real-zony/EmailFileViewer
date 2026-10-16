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

// 复数形式：i18next 用 key_one / key_other 这样的后缀区分，不同语言需要的形式不同（英文 one + other，中文只有 other）。
// 比较 key 时把它们合并成同一个逻辑 key；参数取各形式的并集，并要求每个复数 key 都有 _other（i18next 的兜底形式）。
const PLURAL_SUFFIX = /^(.+)_(zero|one|two|few|many|other)$/

export interface Entry {
  key: string
  text: string
  /** 该逻辑 key 下全部形式的后缀；普通 key 为空 */
  forms: Set<string>
  placeholders: Set<string>
}

export function logicalEntries(entries: Map<string, string>): Map<string, Entry> {
  const logical = new Map<string, Entry>()
  for (const [key, text] of entries) {
    const m = PLURAL_SUFFIX.exec(key)
    const name = m ? m[1] : key
    let entry = logical.get(name)
    if (!entry) {
      entry = { key: name, text, forms: new Set(), placeholders: new Set() }
      logical.set(name, entry)
    }
    if (m) entry.forms.add(m[2])
    for (const p of placeholders(text)) entry.placeholders.add(p)
  }
  return logical
}

export function checkLocales(locales: Record<string, unknown>, reference: string): string[] {
  const errors: string[] = []
  if (!(reference in locales)) return [`缺少基准语言 ${reference} 的资源文件`]

  const flat = new Map<string, Map<string, Entry>>()
  for (const [lang, messages] of Object.entries(locales)) {
    const { entries, problems } = flatten(messages)
    for (const p of problems) errors.push(`[${lang}] ${p}`)
    for (const [key, text] of entries) {
      if (text.trim() === '') errors.push(`[${lang}] ${key}: 文案为空`)
      if (strayBraces(text)) errors.push(`[${lang}] ${key}: 含有单花括号，插值请写成 {{name}}`)
    }
    const logical = logicalEntries(entries)
    for (const entry of logical.values()) {
      if (entry.forms.size > 0 && !entry.forms.has('other')) errors.push(`[${lang}] ${entry.key}: 复数 key 缺少 _other 形式`)
    }
    flat.set(lang, logical)
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
    for (const [key, entry] of entries) {
      const baseEntry = base.get(key)
      if (baseEntry === undefined) continue
      const a = [...baseEntry.placeholders].sort().join(', ')
      const b = [...entry.placeholders].sort().join(', ')
      if (a !== b) errors.push(`[${lang}] ${key}: 插值参数与 ${reference} 不一致（${reference}: ${a || '无'}；${lang}: ${b || '无'}）`)
    }
  }
  return errors
}
