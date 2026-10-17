// 对比度检查：逐套方案、逐种模式校验，有不达标项时退出码为 1。
//   node scripts/check-contrast.ts        完整报告
//   node scripts/check-contrast.ts --md   Markdown 汇总表
import { checkContrast, MIN_RATIO } from '../src/theme/contrast.ts'
import { PALETTES } from '../src/theme/palettes.ts'

const results = checkContrast()
const failures = results.filter((r) => !r.ok)
const fmt = (n: number) => n.toFixed(1)

if (process.argv.includes('--md')) {
  console.log('| Palette | Mode | Lowest text pair (≥ 4.5) | Lowest icon / focus pair (≥ 3) | Result |')
  console.log('|---|---|---|---|---|')
  for (const p of PALETTES) {
    for (const mode of ['light', 'dark'] as const) {
      const rs = results.filter((r) => r.palette === p.id && r.mode === mode)
      const low = (level: 'text' | 'ui') => {
        const x = rs.filter((r) => r.level === level).sort((a, b) => a.ratio - b.ratio)[0]
        return `${fmt(x.ratio)} (\`${x.fg}\` on \`${x.bg}\`)`
      }
      const ok = rs.every((r) => r.ok)
      console.log(`| ${p.name.en} ${p.name.zh} | ${mode} | ${low('text')} | ${low('ui')} | ${ok ? 'pass' : '**FAIL**'} |`)
    }
  }
} else {
  for (const r of results) {
    if (!r.ok || process.argv.includes('--all')) {
      console.log(`${r.ok ? 'ok  ' : 'FAIL'}  ${r.palette}/${r.mode}  ${fmt(r.ratio).padStart(5)}:1 (min ${MIN_RATIO[r.level]})  ${r.fg} ${r.fgColor} on ${r.bg} ${r.bgColor}  ${r.use}`)
    }
  }
  console.log(`${results.length} 组已检查，${failures.length} 组不达标。`)
}

process.exitCode = failures.length > 0 ? 1 : 0
