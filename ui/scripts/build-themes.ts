// 生成 src/theme/themes.css。
//   node scripts/build-themes.ts          写入文件
//   node scripts/build-themes.ts --check  只比较，不一致时退出码为 1（CI 使用）
import { readFileSync, writeFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { buildThemesCss } from '../src/theme/themeCss.ts'

const target = fileURLToPath(new URL('../src/theme/themes.css', import.meta.url))
const expected = buildThemesCss()

if (process.argv.includes('--check')) {
  let actual = ''
  try {
    actual = readFileSync(target, 'utf8')
  } catch {
    // 文件不存在时按不一致处理
  }
  if (actual !== expected) {
    console.error('src/theme/themes.css 与 src/theme/palettes.ts 不一致，请运行 npm run themes 重新生成。')
    process.exitCode = 1
  } else {
    console.log('themes.css 与配色数据一致。')
  }
} else {
  writeFileSync(target, expected)
  console.log('已写入 src/theme/themes.css')
}
