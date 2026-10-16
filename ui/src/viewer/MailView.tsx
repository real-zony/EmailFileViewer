import { useTranslation } from 'react-i18next'
import {
  AddressRow,
  AttachmentList,
  Badge,
  BadgeList,
  BodySheet,
  MessageHeader,
  PlaceholderSheet,
  RemoteContentNotice,
  ViewerLayout,
  formatFileSize,
  type AttachmentLabels,
} from '../components/index.ts'
import { isLanguage } from '../settings/language.ts'
import { BodyFrame } from './BodyFrame.tsx'
import type { ViewerController } from './controller.ts'
import { formatMailDate } from './formatDate.ts'
import { toAddresses, toAttachmentViews } from './mailModel.ts'
import type { MailTab } from './store.ts'

export interface MailViewProps {
  tab: MailTab
  controller: ViewerController
}

// 一个标签页里当前这一级邮件：邮件头、附件、正文。正文在 iframe 内部滚动，邮件头和附件固定在上方（D7）。
export function MailView({ tab, controller }: MailViewProps) {
  const { t, i18n } = useTranslation()
  const level = tab.trail[tab.trail.length - 1]
  const { handle, document: doc, remoteAllowed } = level
  const language = isLanguage(i18n.language) ? i18n.language : 'en'

  // 日期按界面语言格式化，显示在本地时区；悬停显示原始 Date 头。无法解析时退回显示原始文字
  const formatted = doc.sentAtUtc ? formatMailDate(doc.sentAtUtc, language) : null
  const dateText = formatted ?? doc.dateHeaderRaw ?? undefined
  const more = (n: number) => t('header.more', { n })
  const lessLabel = t('header.showLess')

  const attachments = toAttachmentViews(doc.attachments, t('attachments.unnamed'))
  const totalSize = formatFileSize(attachments.reduce((sum, a) => sum + a.size, 0))
  const attachmentLabels: AttachmentLabels = {
    heading: t('attachments.title'),
    summary: attachments.length === 1 ? t('attachments.summaryOne', { size: totalSize }) : t('attachments.summary', { n: attachments.length, size: totalSize }),
    saveAll: t('attachments.saveAll'),
    open: t('attachments.open'),
    openMessageHint: t('attachments.openMessageTip'),
    save: t('attachments.save'),
    saveNamed: (name) => t('attachments.saveNamed', { name }),
    saveOnly: t('attachments.saveOnly'),
    messageKind: t('attachments.emailKind'),
    showAll: (n) => t('attachments.showAll', { n }),
    showFewer: t('attachments.showFewer'),
  }

  const badges =
    doc.isSigned || doc.isEncrypted ? (
      <BadgeList>
        {doc.isSigned && (
          <Badge icon="seal" muted={t('signature.notVerified')} details={{ buttonLabel: t('signature.about'), content: t('signature.help') }}>
            {t('signature.signed')}
          </Badge>
        )}
        {doc.isEncrypted && <Badge icon="lock">{t('encrypted.badge')}</Badge>}
      </BadgeList>
    ) : undefined

  const format = doc.body.sourceFormat
  const formatLabel = { Html: t('body.format.html'), Rtf: t('body.format.rtf'), PlainText: t('body.format.text'), None: t('body.format.none') }[format]

  const body =
    doc.isEncrypted && format === 'None' ? (
      <PlaceholderSheet icon="lock" title={t('encrypted.title')} body={t('encrypted.body')} />
    ) : (
      <BodySheet
        label={t('body.frame')}
        railText={t('body.label')}
        formatLabel={formatLabel}
        notice={
          doc.hasRemoteContent ? (
            <RemoteContentNotice
              state={remoteAllowed ? 'loaded' : 'blocked'}
              blockedTitle={t('remote.title')}
              blockedDescription={t('remote.why')}
              scopeLabel={t('remote.scope')}
              loadLabel={t('remote.load')}
              loadedText={t('remote.loaded')}
              onLoad={() => controller.allowRemote(tab.id, handle)}
            />
          ) : undefined
        }
      >
        <BodyFrame html={doc.body.html} format={format} remoteAllowed={remoteAllowed} title={t('body.frame')} />
      </BodySheet>
    )

  // key 取文档句柄：切换标签页或进入嵌套邮件时，邮件头和附件列表的折叠状态一起重置
  return (
    <ViewerLayout
      top={
        <>
          <MessageHeader
            key={`header-${handle}`}
            subject={doc.subject ?? undefined}
            showFullSubjectLabel={t('header.showSubject')}
            showLessSubjectLabel={t('header.hideSubject')}
            fromLabel={t('header.from')}
            from={toAddresses(doc.from)}
            dateLabel={t('header.date')}
            dateText={dateText}
            dateTime={doc.sentAtUtc ?? undefined}
            dateTitle={doc.dateHeaderRaw ?? undefined}
            badges={badges}
          >
            <AddressRow label={t('header.replyTo')} addresses={toAddresses(doc.replyTo)} moreLabel={more} lessLabel={lessLabel} />
            <AddressRow label={t('header.to')} addresses={toAddresses(doc.to)} moreLabel={more} lessLabel={lessLabel} />
            <AddressRow label={t('header.cc')} addresses={toAddresses(doc.cc)} moreLabel={more} lessLabel={lessLabel} />
            <AddressRow label={t('header.bcc')} addresses={toAddresses(doc.bcc)} moreLabel={more} lessLabel={lessLabel} />
          </MessageHeader>
          <AttachmentList
            key={`attachments-${handle}`}
            attachments={attachments}
            labels={attachmentLabels}
            onOpen={(id) => void controller.openAttachment(handle, id)}
            onSave={(id) => void controller.saveAttachment(handle, id)}
            onSaveAll={() => void controller.saveAllAttachments(handle)}
            onOpenMessage={(id) => void controller.openNested(tab.id, handle, id)}
            onBlockedOpen={(id) => controller.showBlocked(handle, id)}
          />
        </>
      }
      body={body}
    />
  )
}
