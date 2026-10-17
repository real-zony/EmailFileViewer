import { Icon, type IconName } from './Icon.tsx'
import { useRovingRadio } from './useRovingRadio.ts'

export interface SegmentedOption<T extends string> {
  value: T
  label: string
  icon?: IconName
}

export interface SegmentedControlProps<T extends string> {
  /** 整组的可访问名称（与 labelledBy 二选一） */
  label?: string
  labelledBy?: string
  options: readonly SegmentedOption<T>[]
  value: T
  onChange: (value: T) => void
}

// 分段单选（例如外观模式）。方向键切换并立即生效。
export function SegmentedControl<T extends string>({ label, labelledBy, options, value, onChange }: SegmentedControlProps<T>) {
  const itemProps = useRovingRadio(options.map((o) => o.value), value, onChange)
  return (
    <div className="segmented" role="radiogroup" aria-label={label} aria-labelledby={labelledBy}>
      {options.map((o) => (
        <button key={o.value} type="button" className="segmented__opt" {...itemProps(o.value)}>
          {o.icon && <Icon name={o.icon} />}
          <span>{o.label}</span>
        </button>
      ))}
    </div>
  )
}
