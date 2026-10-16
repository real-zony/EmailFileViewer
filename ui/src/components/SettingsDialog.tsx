import { useId, useRef, type ReactNode } from 'react'
import type { Mode } from '../theme/palettes.ts'
import { Button, IconButton } from './Button.tsx'
import { ChoiceGroup, type ChoiceOption } from './ChoiceGroup.tsx'
import { Dialog } from './Dialog.tsx'
import { PalettePicker, type PaletteOption } from './PalettePicker.tsx'
import { SegmentedControl, type SegmentedOption } from './SegmentedControl.tsx'

export interface SettingsLabels {
  title: string
  close: string
  done: string
  language: string
  languageHint: string
  appearance: string
  mode: string
  palette: string
  /** 选中配色的屏幕阅读器文字 */
  selected: string
  appearanceHint: string
}

export interface SettingsDialogProps<L extends string, P extends string> {
  labels: SettingsLabels
  languages: readonly ChoiceOption<L>[]
  language: L
  onLanguageChange: (value: L) => void
  modes: readonly SegmentedOption<Mode>[]
  mode: Mode
  onModeChange: (value: Mode) => void
  palettes: readonly PaletteOption<P>[]
  palette: P
  onPaletteChange: (value: P) => void
  /** “关于”部分的键值对，例如版本、许可、源代码 */
  about?: readonly { label: string; value: ReactNode }[]
  onClose: () => void
}

// 设置对话框：语言、外观（模式 + 配色方案）、关于。所有选项立即生效，由调用方负责保存。
export function SettingsDialog<L extends string, P extends string>(props: SettingsDialogProps<L, P>) {
  const { labels } = props
  const closeRef = useRef<HTMLButtonElement>(null)
  const langId = useId()
  const modeId = useId()
  const palId = useId()
  return (
    <Dialog
      className="settings"
      title={labels.title}
      onClose={props.onClose}
      initialFocusRef={closeRef}
      headerEnd={<IconButton ref={closeRef} icon="x" label={labels.close} onClick={props.onClose} />}
      actions={<Button variant="primary" onClick={props.onClose}>{labels.done}</Button>}
    >
      <div className="field">
        <span className="field__label" id={langId}>{labels.language}</span>
        <ChoiceGroup labelledBy={langId} options={props.languages} value={props.language} onChange={props.onLanguageChange} />
        <p className="field__hint">{labels.languageHint}</p>
      </div>
      <div className="field">
        <span className="field__label">{labels.appearance}</span>
        <span className="field__sub" id={modeId}>{labels.mode}</span>
        <SegmentedControl labelledBy={modeId} options={props.modes} value={props.mode} onChange={props.onModeChange} />
        <span className="field__sub" id={palId}>{labels.palette}</span>
        <PalettePicker
          labelledBy={palId}
          palettes={props.palettes}
          value={props.palette}
          mode={props.mode}
          selectedLabel={labels.selected}
          onChange={props.onPaletteChange}
        />
        <p className="field__hint">{labels.appearanceHint}</p>
      </div>
      {props.about && props.about.length > 0 && (
        <>
          <hr className="rule" />
          <dl className="kv">
            {props.about.map((row) => (
              <div key={row.label} className="kv__row">
                <dt>{row.label}</dt>
                <dd>{row.value}</dd>
              </div>
            ))}
          </dl>
        </>
      )}
    </Dialog>
  )
}
