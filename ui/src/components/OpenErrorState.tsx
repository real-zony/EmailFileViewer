import type { ReactNode } from 'react'
import { FileName } from './FileName.tsx'
import { Icon } from './Icon.tsx'

export interface OpenErrorStateProps {
  /** 原因类别标签，例如 "Damaged file" */
  reasonLabel: string
  title: string
  fileName: string
  body: string
  /** 操作按钮：打开其他文件、重试（仅无法读取）、关闭标签页 */
  actions: ReactNode
}

// 打开失败：显示在失败文件自己的标签页里，必定带文件名和原因类别
export function OpenErrorState({ reasonLabel, title, fileName, body, actions }: OpenErrorStateProps) {
  return (
    <div className="viewer">
      <div className="state" role="alert">
        <div className="state__icon state__icon--danger">
          <Icon name="fileX" />
        </div>
        <span className="reason">{reasonLabel}</span>
        <h1 className="state__title">{title}</h1>
        <span className="filechip" title={fileName}>
          <Icon name="file" />
          <FileName name={fileName} />
        </span>
        <p className="state__body">{body}</p>
        <div className="state__actions">{actions}</div>
      </div>
    </div>
  )
}
