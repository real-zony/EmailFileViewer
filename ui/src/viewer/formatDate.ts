import type { Language } from '../settings/language.ts'

const LOCALES: Record<Language, string> = { en: 'en-US', 'zh-CN': 'zh-CN' }

// 日期格式沿用草稿：英文 "Tue, Oct 6, 2026, 12:00 AM"；中文 "2026年10月6日周二 00:00"（24 小时制）
const OPTIONS: Record<Language, Intl.DateTimeFormatOptions> = {
  en: { weekday: 'short', year: 'numeric', month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' },
  'zh-CN': { year: 'numeric', month: 'long', day: 'numeric', weekday: 'short', hour: '2-digit', minute: '2-digit', hourCycle: 'h23' },
}

/**
 * 把 sidecar 给的 UTC 时间（带 Z 的 ISO 8601）按界面语言格式化，并转换到本地时区。
 * `timeZone` 只在测试中用来固定时区；不传就用系统时区。无法解析时返回 null。
 */
export function formatMailDate(isoUtc: string, language: Language, timeZone?: string): string | null {
  const date = new Date(isoUtc)
  if (Number.isNaN(date.getTime())) return null
  return new Intl.DateTimeFormat(LOCALES[language], { ...OPTIONS[language], timeZone }).format(date)
}
