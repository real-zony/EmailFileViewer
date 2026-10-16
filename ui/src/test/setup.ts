// Vitest 全局设置：jest-dom 断言，并在每个测试后卸载组件
import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterEach } from 'vitest'

afterEach(() => cleanup())
