import { Fragment } from 'react'
import { Trans, useTranslation } from 'react-i18next'
import { Button, FileName, Toast, ToastRegion } from '../components/index.ts'
import type { ViewerController } from './controller.ts'
import { openFailureCategory, operationReason, type OpenFailureCategory } from './errors.ts'
import type { Toast as ToastState } from './store.ts'

// 汇总通知里最多列出几个文件，其余用“另外 N 个”
const MAX_LISTED = 5

export interface ToastsProps {
  toasts: readonly ToastState[]
  controller: ViewerController
}

// 窗口右上角的通知：一批文件部分打开失败的汇总、保存全部的结果、操作失败。不自动消失，由用户关闭。
export function Toasts({ toasts, controller }: ToastsProps) {
  const { t } = useTranslation()
  if (toasts.length === 0) return null

  const reasons: Record<OpenFailureCategory, string> = {
    unsupported: t('error.unsupported.reason'),
    corrupt: t('error.corrupt.reason'),
    unreadable: t('error.unreadable.reason'),
    generic: t('error.generic.reason'),
  }
  const unnamed = t('attachments.unnamed')
  const dismiss = (id: number) => (
    <Button size="sm" onClick={() => controller.dismissToast(id)}>
      {t('common.dismiss')}
    </Button>
  )

  return (
    <ToastRegion label={t('toast.label')}>
      {toasts.map((toast) => {
        switch (toast.kind) {
          case 'partialOpen': {
            const listed = toast.failures.slice(0, MAX_LISTED)
            const first = toast.failures[0]
            return (
              <Toast
                key={toast.id}
                variant="warning"
                title={t('toast.partialTitle', { ok: toast.opened, all: toast.total })}
                actions={
                  <>
                    {first && (
                      <Button
                        variant="secondary"
                        size="sm"
                        onClick={() => {
                          controller.selectTab(first.tabId)
                          controller.dismissToast(toast.id)
                        }}
                      >
                        {t('common.show')}
                      </Button>
                    )}
                    {dismiss(toast.id)}
                  </>
                }
              >
                {listed.map((failure) => (
                  <Fragment key={failure.tabId}>
                    <Trans
                      t={t}
                      i18nKey="toast.failedLine"
                      values={{ reason: reasons[openFailureCategory(failure.code)] }}
                      components={{ file: <FileName name={failure.fileName} /> }}
                    />
                    <br />
                  </Fragment>
                ))}
                {toast.failures.length > MAX_LISTED && t('toast.more', { n: toast.failures.length - MAX_LISTED })}
              </Toast>
            )
          }

          case 'operationFailed': {
            const name = toast.name || unnamed
            return (
              <Toast key={toast.id} title={t(`toast.${toast.operation}`, { name })} actions={dismiss(toast.id)}>
                {t(`reason.${operationReason(toast.code)}`)}
              </Toast>
            )
          }

          case 'savedAll': {
            const failed = toast.total - toast.saved
            return (
              <Toast
                key={toast.id}
                variant={failed > 0 ? 'warning' : 'success'}
                title={failed > 0 ? t('toast.savedSome', { saved: toast.saved, total: toast.total }) : t('toast.savedAll', { count: toast.saved })}
                actions={dismiss(toast.id)}
              >
                {failed > 0 && (
                  <>
                    {t(`reason.${operationReason(toast.code ?? '')}`)}
                    <br />
                    {toast.failedNames.map((name, i) => (
                      <Fragment key={i}>
                        <FileName name={name || unnamed} />
                        <br />
                      </Fragment>
                    ))}
                  </>
                )}
              </Toast>
            )
          }
        }
      })}
    </ToastRegion>
  )
}
