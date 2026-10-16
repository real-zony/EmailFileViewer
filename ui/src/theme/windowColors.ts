// 生成 src-tauri/src/window_colors.json 的纯函数，构建脚本和测试共用。
// Rust 在创建主窗口前读取它，把窗口底色设成所选方案当前模式下的 desk。
// 本文件会被 Node 直接执行，只能使用可擦除的 TS 语法。
import { DEFAULT_PALETTE, PALETTES, type Palette } from './palettes.ts'

export function buildWindowColorsJson(palettes: readonly Palette[] = PALETTES): string {
  const out = {
    default: DEFAULT_PALETTE,
    palettes: Object.fromEntries(palettes.map((p) => [p.id, { light: p.light.desk, dark: p.dark.desk }])),
  }
  return `${JSON.stringify(out, null, 2)}\n`
}
