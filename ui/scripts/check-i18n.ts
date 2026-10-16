// 检查界面文案资源：两种语言的 key 集合必须一致，带插值的文案参数也必须一致。
//   node scripts/check-i18n.ts                 检查 src/i18n/locales
//   node scripts/check-i18n.ts --dir <目录>    检查指定目录（测试用）
// 有问题时退出码为 1（CI 使用）。
import { readdirSync, readFileSync } from 'node:fs'
import { basename, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { checkLocales } from '../src/i18n/check.ts'
import { LANGUAGES } from '../src/settings/language.ts'

const REFERENCE = 'en'
const dirArg = process.argv.indexOf('--dir')
const dir = dirArg !== -1 ? process.argv[dirArg + 1] : fileURLToPath(new URL('../src/i18n/locales/', import.meta.url))

const errors: string[] = []
const locales: Record<string, unknown> = {}
for (const file of readdirSync(dir).filter((f) => f.endsWith('.json')).sort()) {
  const lang = basename(file, '.json')
  try {
    locales[lang] = JSON.parse(readFileSync(join(dir, file), 'utf8'))
  } catch (error) {
    errors.push(`[${lang}] 不是有效的 JSON: ${(error as Error).message}`)
  }
}
for (const lang of LANGUAGES) {
  if (!(lang in locales) && !errors.some((e) => e.startsWith(`[${lang}]`))) errors.push(`缺少界面语言 ${lang} 的资源文件 ${lang}.json`)
}
for (const lang of Object.keys(locales)) {
  if (!(LANGUAGES as readonly string[]).includes(lang)) errors.push(`${lang}.json 不对应任何支持的界面语言（见 src/settings/language.ts）`)
}
if (errors.length === 0) errors.push(...checkLocales(locales, REFERENCE))

if (errors.length > 0) {
  for (const e of errors) console.error(e)
  console.error(`i18n 检查失败：${errors.length} 个问题。`)
  process.exitCode = 1
} else {
  console.log(`i18n 检查通过：${Object.keys(locales).join('、')} 的 key 和插值参数一致。`)
}
