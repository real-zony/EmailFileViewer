import type { Mode } from '../theme/palettes.ts'
import { Icon } from './Icon.tsx'
import { useRovingRadio } from './useRovingRadio.ts'

export interface PaletteOption<T extends string> {
  id: T
  name: string
  /** 一句话说明，作为悬停提示 */
  description?: string
}

export interface PalettePreviewProps {
  palette: string
  mode: Mode
}

// 用该方案自己的令牌画一个迷你界面：外层带 data-palette / data-mode，themes.css 在这里解析出该方案的颜色
export function PalettePreview({ palette, mode }: PalettePreviewProps) {
  return (
    <span className="pal__preview" data-palette={palette} data-mode={mode} aria-hidden="true">
      <span className="pp-tabs"><i className="pp-tab pp-tab--on" /><i className="pp-tab" /></span>
      <span className="pp-body">
        <span className="pp-text"><i className="pp-l1" /><i className="pp-l2" /></span>
        <span className="pp-sheet"><i className="pp-seam" /></span>
        <span className="pp-btn" />
      </span>
    </span>
  )
}

export interface PalettePickerProps<T extends string> {
  label?: string
  labelledBy?: string
  palettes: readonly PaletteOption<T>[]
  value: T
  /** 预览按哪种模式渲染，一般传当前的外观模式 */
  mode: Mode
  /** 选中项的屏幕阅读器文字，例如 "Selected" */
  selectedLabel: string
  onChange: (id: T) => void
}

export function PalettePicker<T extends string>({ label, labelledBy, palettes, value, mode, selectedLabel, onChange }: PalettePickerProps<T>) {
  const itemProps = useRovingRadio(palettes.map((p) => p.id), value, onChange)
  return (
    <div className="palettes" role="radiogroup" aria-label={label} aria-labelledby={labelledBy}>
      {palettes.map((p) => {
        const on = p.id === value
        return (
          <button key={p.id} type="button" className="pal" title={p.description} {...itemProps(p.id)}>
            <PalettePreview palette={p.id} mode={mode} />
            <span className="pal__meta">
              <span className="pal__name">{p.name}</span>
              {on && (
                <span className="pal__check">
                  <Icon name="check" />
                  <span className="sr-only">{selectedLabel}</span>
                </span>
              )}
            </span>
          </button>
        )
      })}
    </div>
  )
}
