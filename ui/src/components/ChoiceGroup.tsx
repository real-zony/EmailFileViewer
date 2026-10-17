import { useRovingRadio } from './useRovingRadio.ts'

export interface ChoiceOption<T extends string> {
  value: T
  label: string
  /** 选项文字本身的语言，例如语言选项用各自的语言书写 */
  lang?: string
  description?: string
}

export interface ChoiceGroupProps<T extends string> {
  label?: string
  labelledBy?: string
  options: readonly ChoiceOption<T>[]
  value: T
  onChange: (value: T) => void
}

// 卡片式单选（例如界面语言）
export function ChoiceGroup<T extends string>({ label, labelledBy, options, value, onChange }: ChoiceGroupProps<T>) {
  const itemProps = useRovingRadio(options.map((o) => o.value), value, onChange)
  return (
    <div className="choice-row" role="radiogroup" aria-label={label} aria-labelledby={labelledBy}>
      {options.map((o) => (
        <button key={o.value} type="button" className="choice" {...itemProps(o.value)}>
          <span className="choice__dot" aria-hidden="true" />
          <span className="choice__text">
            <span className="choice__name" lang={o.lang}>{o.label}</span>
            {o.description && <span className="choice__sub">{o.description}</span>}
          </span>
        </button>
      ))}
    </div>
  )
}
