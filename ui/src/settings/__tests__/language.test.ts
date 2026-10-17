import { describe, expect, it } from 'vitest'
import { DEFAULT_LANGUAGE, LANGUAGES, LANGUAGE_OPTIONS, isLanguage, resolveSystemLanguage } from '../language.ts'
import cases from '../language-cases.json'

describe('app-localization：默认跟随系统语言', () => {
  it('场景“系统语言为简体中文”：zh-CN 使用简体中文', () => {
    expect(resolveSystemLanguage('zh-CN')).toBe('zh-CN')
  })

  it('场景“系统语言为德语”：de-DE 使用英文', () => {
    expect(resolveSystemLanguage('de-DE')).toBe('en')
  })

  it('简体中文的各种写法都使用简体中文', () => {
    for (const tag of ['zh-Hans', 'zh-CN', 'zh-Hans-CN', 'zh-SG', 'zh', 'zh_CN', 'zh_CN.UTF-8', 'ZH-cn']) {
      expect(resolveSystemLanguage(tag), tag).toBe('zh-CN')
    }
  })

  it('繁体中文没有对应资源，使用英文', () => {
    for (const tag of ['zh-TW', 'zh-HK', 'zh-MO', 'zh-Hant', 'zh-Hant-TW', 'zh-Hant-CN', 'zh_TW.UTF-8']) {
      expect(resolveSystemLanguage(tag), tag).toBe('en')
    }
  })

  it('文字子标签优先于地区：zh-Hans-HK 是简体，zh-Hant-CN 是繁体', () => {
    expect(resolveSystemLanguage('zh-Hans-HK')).toBe('zh-CN')
    expect(resolveSystemLanguage('zh-Hant-CN')).toBe('en')
  })

  it('其他语言、空值和无法识别的值一律英文', () => {
    for (const tag of ['en-US', 'fr-FR', 'ja-JP', 'yue-HK', 'zha', '', null, undefined]) {
      expect(resolveSystemLanguage(tag), String(tag)).toBe('en')
    }
  })

  // 与 Rust 侧 src-tauri/src/settings.rs 共用同一份用例，保证两边的判断一致
  it.each(cases)('共用用例：$locale -> $expected', ({ locale, expected }) => {
    expect(resolveSystemLanguage(locale)).toBe(expected)
  })
})

describe('语言定义', () => {
  it('支持英文和简体中文，默认英文；语言选项用各自的语言书写', () => {
    expect(LANGUAGES).toEqual(['en', 'zh-CN'])
    expect(DEFAULT_LANGUAGE).toBe('en')
    expect(LANGUAGE_OPTIONS.map((o) => [o.value, o.label])).toEqual([
      ['en', 'English'],
      ['zh-CN', '简体中文'],
    ])
    expect(isLanguage('zh-CN')).toBe(true)
    expect(isLanguage('zh')).toBe(false)
    expect(isLanguage(undefined)).toBe(false)
  })
})
