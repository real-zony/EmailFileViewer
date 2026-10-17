// @vitest-environment node
import { spawnSync } from 'node:child_process'
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { afterAll, describe, expect, it } from 'vitest'
import { checkLocales, flatten, logicalEntries, placeholders } from '../check.ts'
import { createI18n, resources } from '../index.ts'
import en from '../locales/en.json'
import zhCN from '../locales/zh-CN.json'

const uiRoot = fileURLToPath(new URL('../../../', import.meta.url))

describe('界面文案资源', () => {
  // 复数 key（key_one / key_other）合并成一个逻辑 key：英文有 one + other，中文只有 other
  const logical = (messages: unknown) => logicalEntries(flatten(messages).entries)

  it('英文和简体中文的 key 集合完全一致（复数 key 按逻辑 key 比较）', () => {
    expect([...logical(zhCN).keys()].sort()).toEqual([...logical(en).keys()].sort())
  })

  it('每个带插值的文案，两种语言的参数一致', () => {
    const a = logical(en)
    const b = logical(zhCN)
    for (const [key, entry] of a) expect([...b.get(key)!.placeholders].sort(), key).toEqual([...entry.placeholders].sort())
    // 至少有这些带参数的文案，防止检查形同虚设
    expect([...a.get('header.more')!.placeholders]).toEqual(['n'])
    expect([...a.get('toast.partialTitle')!.placeholders].sort()).toEqual(['all', 'ok'])
    expect([...a.get('drop.title')!.placeholders]).toEqual(['count'])
  })

  it('真实资源通过完整检查', () => {
    expect(checkLocales({ en, 'zh-CN': zhCN }, 'en')).toEqual([])
  })
})

describe('checkLocales', () => {
  const base = { a: { b: 'Hello {{name}}', c: 'Plain' }, d: '{{n}} files · {{size}}' }

  it('缺少 key、多出 key 都会报错', () => {
    const errors = checkLocales({ en: base, 'zh-CN': { a: { b: '你好 {{name}}' }, e: 'x' } }, 'en')
    expect(errors).toContain('[zh-CN] 缺少 key: a.c')
    expect(errors).toContain('[zh-CN] 缺少 key: d')
    expect(errors.some((e) => e.includes('多出的 key') && e.endsWith('e'))).toBe(true)
  })

  it('插值参数不一致会报错：缺参数、多参数、改了参数名', () => {
    const missing = checkLocales({ en: base, 'zh-CN': { a: { b: '你好', c: '普通' }, d: '{{n}} 个文件 · {{size}}' } }, 'en')
    expect(missing.some((e) => e.includes('a.b') && e.includes('插值参数'))).toBe(true)
    const renamed = checkLocales({ en: base, 'zh-CN': { a: { b: '你好 {{user}}', c: '普通' }, d: '{{n}} 个文件 · {{size}}' } }, 'en')
    expect(renamed.some((e) => e.includes('a.b') && e.includes('插值参数'))).toBe(true)
    const extra = checkLocales({ en: base, 'zh-CN': { a: { b: '你好 {{name}}', c: '普通 {{x}}' }, d: '{{n}} 个文件 · {{size}}' } }, 'en')
    expect(extra.some((e) => e.includes('a.c') && e.includes('插值参数'))).toBe(true)
  })

  it('参数顺序不同不算不一致（中文语序常常不同）', () => {
    expect(checkLocales({ en: { t: 'Opened {{ok}} of {{all}}' }, 'zh-CN': { t: '已打开 {{all}} 个中的 {{ok}} 个' } }, 'en')).toEqual([])
  })

  it('识别 {{name, format}}、{{- name}} 和 $t(key)', () => {
    expect(placeholders('{{count, number}} {{- html}} $t(common.close) {{ spaced }}')).toEqual(['$t(common.close)', 'count', 'html', 'spaced'])
  })

  it('单花括号（草稿里的 {n} 写法）、空文案、非字符串值都会报错', () => {
    const errors = checkLocales({ en: { a: 'x {n}', b: 'ok', c: 'ok' }, 'zh-CN': { a: 'x', b: ' ', c: 3 as unknown as string } }, 'en')
    expect(errors.some((e) => e.includes('[en] a') && e.includes('单花括号'))).toBe(true)
    expect(errors.some((e) => e.includes('[zh-CN] b') && e.includes('为空'))).toBe(true)
    expect(errors.some((e) => e.includes('[zh-CN] c') && e.includes('必须是字符串'))).toBe(true)
  })

  it('复数 key：英文的 _one + _other 与中文只有 _other 视为一致', () => {
    const en = { n: { x_one: 'One {{count}} file', x_other: '{{count}} files' } }
    expect(checkLocales({ en, 'zh-CN': { n: { x_other: '{{count}} 个文件' } } }, 'en')).toEqual([])
  })

  it('复数 key 缺少 _other、整组缺失、参数不一致都会报错', () => {
    const en = { n: { x_one: 'One {{count}} file', x_other: '{{count}} files' } }
    const noOther = checkLocales({ en, 'zh-CN': { n: { x_one: '{{count}} 个文件' } } }, 'en')
    expect(noOther.some((e) => e.includes('[zh-CN]') && e.includes('n.x') && e.includes('_other'))).toBe(true)
    const missing = checkLocales({ en, 'zh-CN': { n: { y: 'z' } } }, 'en')
    expect(missing).toContain('[zh-CN] 缺少 key: n.x')
    const renamed = checkLocales({ en, 'zh-CN': { n: { x_other: '{{n}} 个文件' } } }, 'en')
    expect(renamed.some((e) => e.includes('n.x') && e.includes('插值参数'))).toBe(true)
  })

  it('缺少基准语言时报错', () => {
    expect(checkLocales({ 'zh-CN': {} }, 'en')).toHaveLength(1)
  })
})

describe('npm run i18n（scripts/check-i18n.ts）', () => {
  const tmp = mkdtempSync(join(tmpdir(), 'efv-i18n-'))
  afterAll(() => rmSync(tmp, { recursive: true, force: true }))

  function run(args: string[] = []) {
    return spawnSync(process.execPath, ['scripts/check-i18n.ts', ...args], { cwd: uiRoot, encoding: 'utf8' })
  }
  function locales(name: string, files: Record<string, unknown>) {
    const dir = join(tmp, name)
    mkdirSync(dir)
    for (const [lang, content] of Object.entries(files)) writeFileSync(join(dir, `${lang}.json`), JSON.stringify(content))
    return dir
  }

  it('仓库里的资源通过，退出码为 0', () => {
    const r = run()
    expect(r.status, r.stderr).toBe(0)
  })

  it('两种语言的 key 不一致时失败（退出码 1）', () => {
    const r = run(['--dir', locales('keys', { en: { a: 'A', b: 'B' }, 'zh-CN': { a: '甲' } })])
    expect(r.status).toBe(1)
    expect(r.stderr).toContain('缺少 key: b')
  })

  it('插值参数不一致时失败（退出码 1）', () => {
    const r = run(['--dir', locales('params', { en: { a: '{{n}} files' }, 'zh-CN': { a: '{{count}} 个文件' } })])
    expect(r.status).toBe(1)
    expect(r.stderr).toContain('插值参数')
  })

  it('缺少某种界面语言的资源文件时失败', () => {
    const r = run(['--dir', locales('missing', { en: { a: 'A' } })])
    expect(r.status).toBe(1)
    expect(r.stderr).toContain('zh-CN')
  })
})

describe('createI18n', () => {
  it('同步初始化：创建后立刻能取到对应语言的文字', () => {
    expect(createI18n('zh-CN').t('empty.title')).toBe('打开邮件文件')
    expect(createI18n('en').t('empty.title')).toBe('Open an email file')
  })

  it('插值：文件名等内容原样代入，不被转义', () => {
    const i18n = createI18n('en')
    expect(i18n.t('executable.title', { name: 'a&b <1>.exe' })).toBe('“a&b <1>.exe” can\'t be opened here')
    expect(createI18n('zh-CN').t('toast.partialTitle', { ok: 2, all: 3 })).toBe('已打开 3 个文件中的 2 个')
  })

  it('复数：英文按 count 选 one / other，中文只有一种形式', () => {
    const en = createI18n('en')
    expect(en.t('drop.title', { count: 1 })).toBe('Release to open 1 file')
    expect(en.t('drop.title', { count: 3 })).toBe('Release to open 3 files')
    expect(createI18n('zh-CN').t('drop.title', { count: 1 })).toBe('松开以打开 1 个文件')
    expect(createI18n('zh-CN').t('drop.title', { count: 3 })).toBe('松开以打开 3 个文件')
  })

  it('切换语言立即生效', async () => {
    const i18n = createI18n('zh-CN')
    await i18n.changeLanguage('en')
    expect(i18n.t('toolbar.settings')).toBe('Settings')
  })

  it('资源对象包含且仅包含两种界面语言', () => {
    expect(Object.keys(resources)).toEqual(['en', 'zh-CN'])
  })
})
