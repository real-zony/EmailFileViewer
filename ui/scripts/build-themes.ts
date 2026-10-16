// 生成 src/theme/themes.css，以及供 Rust 读取窗口底色的 src-tauri/src/window_colors.json。
//   node scripts/build-themes.ts          写入文件
//   node scripts/build-themes.ts --check  只比较，不一致时退出码为 1（CI 使用）
import { readFileSync, writeFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { buildThemesCss } from '../src/theme/themeCss.ts'
import { buildWindowColorsJson } from '../src/theme/windowColors.ts'

const targets = [
  { path: fileURLToPath(new URL('../src/theme/themes.css', import.meta.url)), label: 'src/theme/themes.css', expected: buildThemesCss() },
  { path: fileURLToPath(new URL('../../src-tauri/src/window_colors.json', import.meta.url)), label: 'src-tauri/src/window_colors.json', expected: buildWindowColorsJson() },
]

if (process.argv.includes('--check')) {
  for (const t of targets) {
    let actual = ''
    try {
      actual = readFileSync(t.path, 'utf8')
    } catch {
      // 文件不存在时按不一致处理
    }
    if (actual !== t.expected) {
      console.error(`${t.label} 与 src/theme/palettes.ts 不一致，请运行 npm run themes 重新生成。`)
      process.exitCode = 1
    } else {
      console.log(`${t.label} 与配色数据一致。`)
    }
  }
} else {
  for (const t of targets) {
    writeFileSync(t.path, t.expected)
    console.log(`已写入 ${t.label}`)
  }
}
