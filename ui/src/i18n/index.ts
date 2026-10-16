import i18next, { type i18n } from 'i18next'
import type { Language } from '../settings/language.ts'
import en from './locales/en.json'
import zhCN from './locales/zh-CN.json'

export const resources = {
  en: { translation: en },
  'zh-CN': { translation: zhCN },
} as const satisfies Record<Language, unknown>

// 以英文资源为准，t('...') 的 key 在编译期检查
declare module 'i18next' {
  interface CustomTypeOptions {
    defaultNS: 'translation'
    resources: { translation: typeof en }
  }
}

// 每次创建独立的实例（通过 I18nextProvider 传给组件，不用全局实例），资源内联，初始化是同步的：
// 首次渲染时文字就是对的，不会先出现 key 或另一种语言。
export function createI18n(language: Language): i18n {
  const instance = i18next.createInstance()
  void instance.init({
    resources,
    lng: language,
    fallbackLng: 'en',
    initAsync: false,
    interpolation: { escapeValue: false }, // React 已经做了转义；文件名等内容不能被改写
  })
  return instance
}
