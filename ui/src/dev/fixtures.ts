// 固定的示例数据：`node scripts/generate-fixtures.ts` 用真实的 sidecar 打开 samples/ 下的样本生成。
// 开发模式的模拟实现和测试都从这里取，不要手改 fixtures/*.json。
import type { MockFile } from '../api/mockApi.ts'

const modules = import.meta.glob<MockFile>('./fixtures/*.json', { eager: true, import: 'default' })

/** fixture 名（文件名去掉 .json）-> 文件。成功的有 document，打不开的有 error */
export const FIXTURES: Readonly<Record<string, MockFile>> = Object.fromEntries(
  Object.entries(modules)
    .map(([path, file]) => [path.replace(/^.*\/(.+)\.json$/, '$1'), file] as const)
    .sort(([a], [b]) => a.localeCompare(b)),
)

export function fixture(name: string): MockFile {
  const file = FIXTURES[name]
  if (!file) throw new Error(`没有名为 ${name} 的 fixture（可用：${Object.keys(FIXTURES).join('、')}）`)
  return file
}
