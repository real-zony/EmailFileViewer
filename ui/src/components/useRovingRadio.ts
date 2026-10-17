import { useRef, type KeyboardEvent } from 'react'

// WAI-ARIA radiogroup 的键盘行为：roving tabindex，方向键 / Home / End 移动并选中。
// SegmentedControl、ChoiceGroup、PalettePicker 共用。
export function useRovingRadio<T extends string>(values: readonly T[], value: T, onChange: (next: T) => void) {
  const refs = useRef(new Map<T, HTMLElement>())

  const move = (from: T, delta: number | 'first' | 'last') => {
    const i = values.indexOf(from)
    const n = values.length
    const j = delta === 'first' ? 0 : delta === 'last' ? n - 1 : (i + delta + n) % n
    const next = values[j]
    onChange(next)
    refs.current.get(next)?.focus()
  }

  return (item: T) => ({
    role: 'radio' as const,
    'aria-checked': item === value,
    // 没有选中项时，第一项可以获得焦点
    tabIndex: item === value || (!values.includes(value) && item === values[0]) ? 0 : -1,
    ref: (el: HTMLElement | null) => {
      if (el) refs.current.set(item, el)
      else refs.current.delete(item)
    },
    onClick: () => onChange(item),
    onKeyDown: (e: KeyboardEvent) => {
      const key = e.key
      if (key === 'ArrowRight' || key === 'ArrowDown') move(item, 1)
      else if (key === 'ArrowLeft' || key === 'ArrowUp') move(item, -1)
      else if (key === 'Home') move(item, 'first')
      else if (key === 'End') move(item, 'last')
      else return
      e.preventDefault()
    },
  })
}
