// 界面语言：支持的语言、系统语言到界面语言的映射。
// 本文件会被 Node 直接执行（scripts/check-i18n.ts），只能使用可擦除的 TS 语法。
// Rust 侧 src-tauri/src/settings.rs 里有同样的规则，两边共用 language-cases.json 做测试。

export const LANGUAGES = ['en', 'zh-CN'] as const
export type Language = (typeof LANGUAGES)[number]

export const DEFAULT_LANGUAGE: Language = 'en'

export function isLanguage(value: unknown): value is Language {
  return typeof value === 'string' && (LANGUAGES as readonly string[]).includes(value)
}

// 语言选项用各自的语言书写，不随界面语言翻译
export const LANGUAGE_OPTIONS: readonly { value: Language; label: string; lang: string }[] = [
  { value: 'en', label: 'English', lang: 'en' },
  { value: 'zh-CN', label: '简体中文', lang: 'zh-CN' },
]

// 没有繁体中文资源，这些地区的中文按英文处理
const TRADITIONAL_REGIONS = new Set(['tw', 'hk', 'mo'])

// 系统首选语言 -> 界面语言。
// 只有简体中文使用中文界面，其他一律英文：
//   - 有文字子标签时以它为准：Hans 为简体，其他（Hant 等）为繁体；
//   - 没有文字子标签时，TW / HK / MO 为繁体，其余（含只写 zh）为简体。
// 兼容 zh_CN、zh_CN.UTF-8、zh_CN@pinyin 这类 POSIX 写法，大小写不敏感。
export function resolveSystemLanguage(locale: string | null | undefined): Language {
  if (!locale) return DEFAULT_LANGUAGE
  const tag = locale.split(/[.@]/)[0].replace(/_/g, '-').toLowerCase()
  const [primary, ...rest] = tag.split('-')
  if (primary !== 'zh') return DEFAULT_LANGUAGE

  let script: string | undefined
  let region: string | undefined
  for (const sub of rest) {
    if (sub.length === 1) break // 扩展或私有用途子标签，后面的都不看
    if (script === undefined && region === undefined && /^[a-z]{4}$/.test(sub)) script = sub
    else if (region === undefined && /^([a-z]{2}|\d{3})$/.test(sub)) region = sub
  }

  if (script !== undefined) return script === 'hans' ? 'zh-CN' : DEFAULT_LANGUAGE
  if (region !== undefined && TRADITIONAL_REGIONS.has(region)) return DEFAULT_LANGUAGE
  return 'zh-CN'
}
