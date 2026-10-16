// 重新生成界面的固定示例数据：用真实的 sidecar 打开 samples/ 下的一批代表性样本，
// 把 EmailDocument 保存成 src/dev/fixtures/*.json（提交到仓库）。浏览器里 `npm run dev` 的模拟实现和
// 组件测试都读这些文件，所以它们和 sidecar 的真实输出保持一致。
//
//   node scripts/generate-fixtures.ts                  用 `dotnet run` 启动 sidecar 并重新生成
//   node scripts/generate-fixtures.ts --sidecar <路径>  用已经发布好的 sidecar 可执行文件
//   node scripts/generate-fixtures.ts --check          只比较，不写文件；和仓库中的不一致时退出码为 1
//
// 需要 .NET SDK（版本见仓库根目录的 global.json）。样本由 samples/generator 生成，S/MIME 样本每次重新生成
// 都会变，但提交在仓库里的样本是固定的，所以同一份样本得到的 fixture 也是固定的。
// 本文件由 Node 直接执行，只能使用可擦除的 TS 语法，导入路径要带 .ts 后缀。
import { spawn } from 'node:child_process'
import { existsSync, mkdirSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { basename, join, resolve } from 'node:path'
import { createInterface } from 'node:readline'
import { fileURLToPath } from 'node:url'

const repoRoot = resolve(fileURLToPath(new URL('../..', import.meta.url)))
const outDir = resolve(fileURLToPath(new URL('../src/dev/fixtures/', import.meta.url)))

interface Sample {
  /** fixture 文件名（不含 .json），也是模拟实现里的名字 */
  name: string
  /** 相对 samples/ 的路径 */
  file: string
  /** 这个 fixture 用来展示什么 */
  covers: string
}

// 至少覆盖：营销邮件（远程内容）、恶意 HTML、链接文字与地址不符、cid 内嵌图片、GBK、纯文本、只有 RTF 正文、
// 签名（含不透明签名）、加密、多附件、可执行附件、不安全文件名、两层嵌套、winmail.dat、分段正文
const SAMPLES: readonly Sample[] = [
  { name: 'marketing-remote', file: 'marketing-remote-content.eml', covers: '营销邮件：多张远程图片、追踪像素、远程样式表和字体' },
  { name: 'html-malicious', file: 'html-malicious.eml', covers: '恶意 HTML（已被 sidecar 清理过）' },
  { name: 'link-mismatch', file: 'link-text-mismatch.eml', covers: '链接文字与实际地址不符' },
  { name: 'cid-inline', file: 'cid-inline-image.eml', covers: 'cid 内嵌图片已换成 data: URI，附件列表只有 docx' },
  { name: 'charset-gbk', file: 'charset-gbk.eml', covers: 'GBK 编码的中文邮件' },
  { name: 'charset-big5', file: 'charset-big5-undeclared.eml', covers: '未声明字符集的 Big5' },
  { name: 'plain-text', file: 'body-plain-only.eml', covers: '纯文本正文' },
  { name: 'rtf-only', file: 'body-rtf-only.msg', covers: '只有 RTF 正文的 MSG' },
  { name: 'signed', file: 'smime-signed.eml', covers: 'S/MIME 明文签名' },
  { name: 'signed-opaque', file: 'smime-signed-opaque.eml', covers: 'S/MIME 不透明签名（正文和附件在签名数据里）' },
  { name: 'encrypted', file: 'smime-encrypted.eml', covers: 'S/MIME 加密，没有正文' },
  { name: 'attachments-multi', file: 'attachments-pdf-xlsx-jpg.eml', covers: '多个普通附件（pdf、xlsx、jpg）' },
  { name: 'attachments-executables', file: 'attachments-executables.eml', covers: '可执行和脚本类附件' },
  { name: 'attachments-unsafe-names', file: 'attachments-unsafe-names.eml', covers: '不安全的附件文件名（7 个，超过折叠阈值）' },
  { name: 'nested-two-levels', file: 'nested-rfc822-two-levels.eml', covers: '两层 message/rfc822 嵌套（EML）' },
  { name: 'nested-msg-two-levels', file: 'nested-msg-two-levels.msg', covers: '两层嵌入 MSG' },
  { name: 'winmail', file: 'tnef-winmail-docx.eml', covers: 'winmail.dat 展开成 docx' },
  { name: 'segmented-body', file: 'body-multipart-mixed-inline-images.eml', covers: '分段正文：三段 HTML 夹着内嵌图片' },
  { name: 'headers-normal', file: 'headers-normal.eml', covers: '发件人、两个收件人、一个抄送' },
  { name: 'headers-timezone', file: 'headers-timezone-bcc-replyto-no-cc.eml', covers: 'Date 为 -0700，有密送和回复至，没有抄送' },
  { name: 'rfc2047', file: 'headers-rfc2047.eml', covers: 'RFC 2047 编码的邮件头' },
]

// 打不开的文件：记录 sidecar 真实返回的错误码
const FAILURES: readonly Sample[] = [
  { name: 'error-unsupported', file: 'invalid/not-an-email.png', covers: 'unsupported_format：一张图片' },
  { name: 'error-corrupted', file: 'invalid/truncated.msg', covers: 'corrupted：被截断的 MSG' },
  { name: 'error-unreadable', file: 'invalid/does-not-exist.eml', covers: 'unreadable：文件不存在' },
]

const MAX_NESTING = 4

type Json = Record<string, unknown>

interface Pending {
  resolve: (value: Json) => void
  reject: (reason: Error) => void
  timer: NodeJS.Timeout
}

class SidecarClient {
  #child
  #nextId = 1
  #pending = new Map<number, Pending>()

  constructor(command: string, args: string[]) {
    this.#child = spawn(command, args, { cwd: repoRoot, stdio: ['pipe', 'pipe', 'inherit'] })
    this.#child.on('error', (error) => this.#failAll(error))
    this.#child.on('exit', (code) => this.#failAll(new Error(`sidecar 已退出（退出码 ${code}）`)))
    const lines = createInterface({ input: this.#child.stdout })
    lines.on('line', (line) => {
      // `dotnet run` 在需要编译时可能把编译输出也写到 stdout，只认 JSON 行
      if (!line.startsWith('{')) return
      let message: Json
      try {
        message = JSON.parse(line) as Json
      } catch {
        return
      }
      const pending = typeof message.id === 'number' ? this.#pending.get(message.id) : undefined
      if (!pending) return
      this.#pending.delete(message.id as number)
      clearTimeout(pending.timer)
      pending.resolve(message)
    })
  }

  #failAll(error: Error) {
    for (const [id, pending] of this.#pending) {
      clearTimeout(pending.timer)
      pending.reject(error)
      this.#pending.delete(id)
    }
  }

  /** 返回 result；sidecar 返回错误时抛出带 code 的 Error */
  async call(method: string, params?: Json, timeoutMs = 120_000): Promise<Json> {
    const id = this.#nextId++
    const response = await new Promise<Json>((resolvePromise, reject) => {
      const timer = setTimeout(() => {
        this.#pending.delete(id)
        reject(new Error(`${method} 超时`))
      }, timeoutMs)
      this.#pending.set(id, { resolve: resolvePromise, reject, timer })
      this.#child.stdin.write(`${JSON.stringify({ id, method, params })}\n`)
    })
    if (response.error) {
      const { code, message } = response.error as { code: string; message: string }
      throw Object.assign(new Error(message), { code })
    }
    return response.result as Json
  }

  async close() {
    const exited = new Promise<void>((done) => this.#child.once('exit', () => done()))
    this.#child.stdin.end()
    await exited
  }
}

interface EmailAttachment {
  id: string
  fileName: string | null
  isNestedEmail: boolean
}

interface FixtureNode {
  fileName: string
  document?: Json
  error?: { code: string; message: string }
  /** attachmentId -> 嵌套邮件。嵌套邮件也可能打不开 */
  nested?: Record<string, FixtureNode>
}

async function readNode(client: SidecarClient, docId: string, document: Json, fileName: string, depth: number): Promise<FixtureNode> {
  const node: FixtureNode = { fileName, document }
  const nestedAttachments = (document.attachments as EmailAttachment[]).filter((a) => a.isNestedEmail)
  if (nestedAttachments.length > 0 && depth < MAX_NESTING) {
    node.nested = {}
    for (const attachment of nestedAttachments) {
      const name = attachment.fileName ?? 'attachment'
      try {
        const opened = (await client.call('email.openNested', { docId, attachmentId: attachment.id })) as { docId: string; document: Json }
        node.nested[attachment.id] = await readNode(client, opened.docId, opened.document, name, depth + 1)
        await client.call('document.close', { docId: opened.docId })
      } catch (error) {
        const { code, message } = error as Error & { code?: string }
        if (!code) throw error
        node.nested[attachment.id] = { fileName: name, error: { code, message } }
      }
    }
  }
  return node
}

async function buildFixture(client: SidecarClient, sample: Sample): Promise<FixtureNode> {
  const path = join(repoRoot, 'samples', sample.file)
  const fileName = basename(sample.file)
  try {
    const opened = (await client.call('email.open', { path })) as { docId: string; document: Json }
    const node = await readNode(client, opened.docId, opened.document, fileName, 0)
    await client.call('document.close', { docId: opened.docId })
    return node
  } catch (error) {
    const { code, message } = error as Error & { code?: string }
    if (!code) throw error
    return { fileName, error: { code, message } }
  }
}

function serialize(node: FixtureNode): string {
  return `${JSON.stringify(node, null, 2)}\n`
}

async function main() {
  const args = process.argv.slice(2)
  const check = args.includes('--check')
  const sidecarIndex = args.indexOf('--sidecar')
  const [command, commandArgs] =
    sidecarIndex !== -1
      ? [resolve(args[sidecarIndex + 1]), ['--stdio']]
      : ['dotnet', ['run', '--project', 'core/src/EmailFileViewer.Sidecar', '--', '--stdio']]

  const client = new SidecarClient(command, commandArgs)
  const generated = new Map<string, string>()
  try {
    // 就绪确认（首次编译可能较慢）
    await client.call('system.ping', undefined, 300_000)
    for (const sample of [...SAMPLES, ...FAILURES]) {
      const node = await buildFixture(client, sample)
      if (SAMPLES.includes(sample) && !node.document) {
        throw new Error(`${sample.file} 应该能打开，但 sidecar 返回了 ${node.error?.code}`)
      }
      if (FAILURES.includes(sample) && !node.error) throw new Error(`${sample.file} 应该打不开，但 sidecar 成功解析了它`)
      generated.set(`${sample.name}.json`, serialize(node))
      console.log(`${sample.name.padEnd(26)} ${sample.file}`)
    }
  } finally {
    await client.close()
  }

  if (check) {
    const problems: string[] = []
    for (const [file, text] of generated) {
      const path = join(outDir, file)
      if (!existsSync(path)) problems.push(`缺少 ${file}`)
      else if (readFileSync(path, 'utf8') !== text) problems.push(`${file} 与 sidecar 当前的输出不一致`)
    }
    for (const file of existsSync(outDir) ? readdirSync(outDir) : []) {
      if (file.endsWith('.json') && !generated.has(file)) problems.push(`${file} 不在样本清单里`)
    }
    if (problems.length > 0) {
      for (const p of problems) console.error(p)
      process.exitCode = 1
    } else {
      console.log(`fixture 与 sidecar 输出一致（${generated.size} 个）。`)
    }
    return
  }

  mkdirSync(outDir, { recursive: true })
  for (const file of readdirSync(outDir)) {
    if (file.endsWith('.json') && !generated.has(file)) rmSync(join(outDir, file))
  }
  for (const [file, text] of generated) writeFileSync(join(outDir, file), text)
  console.log(`已写入 ${generated.size} 个 fixture 到 ${outDir}`)
}

await main()
