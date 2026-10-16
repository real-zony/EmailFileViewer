// 组件展示页：只在开发模式下通过 ?showcase 访问，用静态数据重现草稿的关键界面，便于逐一对照。
// 这里的英文文案只用于展示；正式界面的文字由 i18n（7.1）提供。
import { useEffect, useState, type ReactNode } from 'react'
import {
  AddressRow,
  AppShell,
  AttachmentList,
  Badge,
  BadgeList,
  BodySheet,
  Breadcrumbs,
  Button,
  EmptyState,
  ExecutableBlockedDialog,
  IconButton,
  Kbd,
  MainArea,
  MessageHeader,
  OpenErrorState,
  PlaceholderSheet,
  RemoteContentNotice,
  SettingsDialog,
  TabBar,
  Toast,
  ToastRegion,
  Toolbar,
  ViewerLayout,
  formatFileSize,
  type Address,
  type AttachmentLabels,
  type AttachmentView,
  type TabItem,
} from '../components/index.ts'
import { DEFAULT_PALETTE, PALETTES, type Mode, type PaletteId } from '../theme/palettes.ts'
import * as bodies from './sampleBodies.ts'
import './showcase.css'

type Screen = 'empty' | 'drag' | 'viewer' | 'settings' | 'error' | 'signed' | 'encrypted' | 'exe'

const SCREENS: { id: Screen; label: string }[] = [
  { id: 'empty', label: '空状态' },
  { id: 'drag', label: '拖入文件' },
  { id: 'viewer', label: '邮件查看（远程已阻止）' },
  { id: 'settings', label: '设置' },
  { id: 'error', label: '错误标签页 + 汇总通知' },
  { id: 'signed', label: '已签名' },
  { id: 'encrypted', label: '加密' },
  { id: 'exe', label: '可执行附件被拒' },
]

const A = (name: string, address: string): Address => ({ name, address })

interface SampleDoc {
  file: string
  subject: string
  from: Address
  replyTo?: Address[]
  to: Address[]
  cc?: Address[]
  bcc?: Address[]
  dateText: string
  dateTime: string
  dateTitle: string
  format: string
  attachments: AttachmentView[]
  body: () => string
  remote?: boolean
  signed?: boolean
  encrypted?: boolean
}

const att = (id: string, fileName: string, size: number, kind: AttachmentView['kind'] = 'file'): AttachmentView => ({ id, fileName, size, kind })

const DOCS: Record<'aw26' | 'q4' | 'board' | 'venue', SampleDoc> = {
  aw26: {
    file: 'AW26 trade preview.msg',
    subject: 'Autumn/Winter ’26 trade preview: catalogue, price list and order form inside',
    from: A('Halden & Moss Trade', 'trade@haldenmoss.example'),
    replyTo: [A('Ines Varga', 'ines.varga@haldenmoss.example')],
    to: [A('Mira Okafor', 'mira@okafor-outfitters.example'), A('Okafor Outfitters Orders', 'orders@okafor-outfitters.example')],
    cc: [A('Ines Varga', 'ines.varga@haldenmoss.example')],
    dateText: 'Tue, Oct 6, 2026, 12:00 AM',
    dateTime: '2026-10-05T09:00:00-07:00',
    dateTitle: 'Mon, 5 Oct 2026 09:00:00 -0700',
    format: 'HTML',
    remote: true,
    body: () => bodies.marketing(false),
    attachments: [
      att('a1', 'AW26-trade-catalogue.pdf', 5033165),
      att('a2', 'AW26-wholesale-price-list.xlsx', 319488),
      att('a3', 'Order-form-AW26.docx', 90112),
      att('a4', 'RE: Pallet delivery dates.msg', 47104, 'message'),
    ],
  },
  q4: {
    file: '第四季度对账通知.eml',
    subject: '关于 2026 年第四季度供应商对账及发票开具安排的通知',
    from: A('陈思远', 'chen.siyuan@lanxi-trading.example'),
    to: [A('华东区供应商联络组', 'suppliers-east@lanxi-trading.example'), A('王立新', 'wang.lixin@hengyuan-pack.example')],
    cc: [A('刘静（财务部）', 'liu.jing@lanxi-trading.example')],
    dateText: 'Mon, Oct 12, 2026, 10:24 AM',
    dateTime: '2026-10-12T10:24:00+08:00',
    dateTitle: 'Mon, 12 Oct 2026 10:24:00 +0800',
    format: 'HTML',
    signed: true,
    body: bodies.chinese,
    attachments: [att('q1', '2026年第四季度对账单（华东区）.xlsx', 190464), att('q2', '开票信息确认函.pdf', 246784)],
  },
  board: {
    file: 'Board pack Q3.eml',
    subject: 'Board pack for 14 October: please review before Tuesday',
    from: A('Teodor Lind', 't.lind@corvane.example'),
    to: [A('Amara Nwosu', 'amara.nwosu@corvane.example'), A('Felix Haas', 'felix.haas@corvane.example'), A('Mira Okafor', 'mira@okafor-outfitters.example')],
    dateText: 'Sat, Oct 10, 2026, 12:41 AM',
    dateTime: '2026-10-09T18:41:00+02:00',
    dateTitle: 'Fri, 9 Oct 2026 18:41:00 +0200',
    format: 'HTML',
    encrypted: true,
    body: () => '',
    attachments: [],
  },
  venue: {
    file: 'RE- RE- FW- Venue contract, catering and AV for the Lisbon partner summit (final, signed).msg',
    subject:
      'RE: RE: FW: Venue contract, catering numbers and AV requirements for the Lisbon partner summit — final countersigned copies, revised floor plan (rev C) and the outstanding questions from Thursday’s call',
    from: A('Sofia Almeida', 'sofia.almeida@tejo-events.example'),
    to: [
      A('Daniel Reyes', 'daniel.reyes@northbeam-partners.example'), A('Priya Raman', 'priya.raman@northbeam-partners.example'),
      A('Jonas Eklund', 'j.eklund@northbeam-partners.example'), A('Aiko Tanaka', 'aiko.tanaka@northbeam-partners.example'),
      A('Marcus Webb', 'marcus.webb@northbeam-partners.example'), A('Lucía Fernández', 'lucia.fernandez@northbeam-partners.example'),
      A('', 'events@northbeam-partners.example'), A('张伟', 'zhang.wei@northbeam-partners.example'),
    ],
    cc: [A('Catering Desk', 'catering@palacio-ribeira.example'), A('Rui Matos', 'rui.matos@palacio-ribeira.example'), A('AV Bookings', 'av@lumen-av.example'), A('Hannah Osei', 'hannah.osei@northbeam-partners.example')],
    bcc: [A('', 'archive@tejo-events.example')],
    dateText: 'Sat, Oct 3, 2026, 12:55 AM',
    dateTime: '2026-10-02T17:55:00+01:00',
    dateTitle: 'Fri, 2 Oct 2026 17:55:00 +0100',
    format: 'Plain text',
    body: () => bodies.plain('Hi all,\n\nAttached are the countersigned venue contract and revision C of the floor plan.\n\nThe floor plan viewer is the venue\'s own tool. The PDF has the same drawings if you\'d rather not install anything.\n\nBest,\nSofia'),
    attachments: [
      att('v1', 'Lisbon-partner-summit_venue-contract_FINAL_countersigned_2026-10-02.pdf', 2516582),
      att('v2', 'Floor plan rev C (ground floor, terrace and breakout rooms).pdf', 6396314),
      att('v3', 'FW: Catering quote v3.msg', 389120, 'message'),
      att('v4', 'venue-floorplan-viewer-setup.exe', 14889779, 'executable'),
      att('v5', 'Catering numbers by day.xlsx', 59392),
      att('v6', 'AV-requirements-main-hall.docx', 114688),
      att('v7', 'terrace-north.jpg', 3460300),
      att('v8', 'install-fonts.command', 2048, 'executable'),
    ],
  },
}

const ATT_LABELS = (doc: SampleDoc): AttachmentLabels => ({
  heading: 'Attachments',
  summary: `${doc.attachments.length} files · ${formatFileSize(doc.attachments.reduce((s, a) => s + a.size, 0))}`,
  saveAll: 'Save all…',
  open: 'Open',
  openMessageHint: 'Opens here, in a new level of this tab',
  save: 'Save…',
  saveNamed: (name) => `Save ${name}…`,
  saveOnly: 'Save only',
  messageKind: 'Email',
  showAll: (n) => `Show all ${n}`,
  showFewer: 'Show fewer',
})

const noop = () => {}

const MAIN_TABS: TabItem[] = [
  { id: 'aw26', label: 'AW26 trade preview', title: 'AW26 trade preview.msg' },
  { id: 'q4', label: '第四季度对账通知', title: '第四季度对账通知.eml' },
  { id: 'board', label: 'Board pack Q3', title: 'Board pack Q3.eml' },
]

function MessageView({ doc, remoteLoaded, onLoadRemote }: { doc: SampleDoc; remoteLoaded: boolean; onLoadRemote: () => void }) {
  const badges =
    doc.signed || doc.encrypted ? (
      <BadgeList>
        {doc.signed && (
          <Badge
            icon="seal"
            muted="signature not verified"
            details={{
              buttonLabel: 'About this signature',
              content:
                "This message has a digital signature, but EmailFileViewer doesn't check signatures. It can't confirm who sent the message or that it wasn't changed.",
            }}
          >
            Signed
          </Badge>
        )}
        {doc.encrypted && <Badge icon="lock">Encrypted</Badge>}
      </BadgeList>
    ) : undefined
  const more = (n: number) => `+${n} more`
  return (
    <ViewerLayout
      top={
        <>
          <MessageHeader
            subject={doc.subject}
            showFullSubjectLabel="Show full subject"
            showLessSubjectLabel="Show less"
            fromLabel="From"
            from={doc.from}
            dateLabel="Date"
            dateText={doc.dateText}
            dateTime={doc.dateTime}
            dateTitle={doc.dateTitle}
            badges={badges}
          >
            <AddressRow label="Reply to" addresses={doc.replyTo ?? []} moreLabel={more} lessLabel="Show less" />
            <AddressRow label="To" addresses={doc.to} moreLabel={more} lessLabel="Show less" />
            <AddressRow label="Cc" addresses={doc.cc ?? []} moreLabel={more} lessLabel="Show less" />
            <AddressRow label="Bcc" addresses={doc.bcc ?? []} moreLabel={more} lessLabel="Show less" />
          </MessageHeader>
          <AttachmentList
            attachments={doc.attachments}
            labels={ATT_LABELS(doc)}
            onOpen={noop}
            onSave={noop}
            onSaveAll={noop}
            onOpenMessage={noop}
            onBlockedOpen={noop}
          />
        </>
      }
      body={
        doc.encrypted ? (
          <PlaceholderSheet
            icon="lock"
            title="This message is encrypted"
            body="EmailFileViewer can't decrypt S/MIME messages, so the content can't be shown. The details above were sent unencrypted. To read it, open the file in a mail app that has the recipient's certificate."
          />
        ) : (
          <BodySheet
            label="Message content"
            railText="Message content · shown in isolation, scripts off"
            formatLabel={doc.format}
            notice={
              doc.remote ? (
                <RemoteContentNotice
                  state={remoteLoaded ? 'loaded' : 'blocked'}
                  blockedTitle="Remote images and styles are blocked."
                  blockedDescription="Loading them lets the sender see that you opened this message."
                  scopeLabel="This message only"
                  loadLabel="Load remote content"
                  loadedText="Remote content is loaded for this message. Other messages still block it."
                  onLoad={onLoadRemote}
                />
              ) : undefined
            }
          >
            {/* 7.5 会把这里换成真正的正文组件（CSP 在 sidecar / 前端拼装） */}
            <iframe
              className="sheet__frame"
              sandbox="allow-popups allow-popups-to-escape-sandbox"
              referrerPolicy="no-referrer"
              title="Message content"
              srcDoc={remoteLoaded && doc.remote ? bodies.marketing(true) : doc.body()}
            />
          </BodySheet>
        )
      }
    />
  )
}

export function Showcase() {
  const [screen, setScreen] = useState<Screen>(() => {
    const s = new URLSearchParams(window.location.search).get('screen')
    return SCREENS.some((x) => x.id === s) ? (s as Screen) : 'viewer'
  })
  const [mode, setMode] = useState<Mode>(() => (new URLSearchParams(window.location.search).get('mode') as Mode) || 'system')
  const [palette, setPalette] = useState<PaletteId>(
    () => (PALETTES.find((p) => p.id === new URLSearchParams(window.location.search).get('palette'))?.id ?? DEFAULT_PALETTE),
  )
  const [narrow, setNarrow] = useState(() => new URLSearchParams(window.location.search).get('width') === 'narrow')
  const [language, setLanguage] = useState<'en' | 'zh-CN'>('en')
  const [remoteLoaded, setRemoteLoaded] = useState(false)
  const [errorTab, setErrorTab] = useState('err')
  const [toast, setToast] = useState(true)

  // 根元素属性就是正式实现中的切换方式
  useEffect(() => {
    document.documentElement.dataset.mode = mode
    document.documentElement.dataset.palette = palette
  }, [mode, palette])

  const go = (s: Screen) => {
    setScreen(s)
    setRemoteLoaded(false)
    setToast(true)
    setErrorTab('err')
  }

  const settingsButton = <IconButton icon="settings" label="Settings" onClick={() => go('settings')} />
  const openButton = <Button variant="secondary" icon="folder" title="Open email files (Ctrl+O)"><span className="tool-label">Open…</span></Button>

  let tabs: TabItem[] | null = null
  let activeTab = 'aw26'
  let content: ReactNode
  let overlay: ReactNode = null
  let toolbarStart: ReactNode = openButton

  switch (screen) {
    case 'empty':
    case 'drag':
      toolbarStart = null
      content = (
        <EmptyState
          title="Open an email file"
          body="Drop .eml or .msg files anywhere in this window, or choose them from your computer."
          openLabel="Open file…"
          onOpen={() => go('viewer')}
          shortcutHint={<>or press <Kbd>Ctrl+O</Kbd></>}
          privacyNote="Messages are read on this computer. Nothing is uploaded unless you choose to."
          dragging={screen === 'drag' ? { title: 'Release to open 3 files', body: 'Each file opens in its own tab. Text and links are ignored.' } : undefined}
        />
      )
      break
    case 'viewer':
    case 'settings':
      tabs = MAIN_TABS
      content = <MessageView doc={DOCS.aw26} remoteLoaded={remoteLoaded} onLoadRemote={() => setRemoteLoaded(true)} />
      if (screen === 'settings') {
        overlay = (
          <SettingsDialog
            labels={{
              title: 'Settings', close: 'Close', done: 'Done',
              language: 'Language', languageHint: 'Changes apply right away. Email content is never translated.',
              appearance: 'Appearance', mode: 'Mode', palette: 'Colour scheme', selected: 'Selected',
              appearanceHint: 'Applies to the app only. Messages always appear on a light page, the way the sender designed them.',
            }}
            languages={[{ value: 'en', label: 'English', lang: 'en' }, { value: 'zh-CN', label: '简体中文', lang: 'zh-CN' }]}
            language={language}
            onLanguageChange={setLanguage}
            modes={[{ value: 'light', label: 'Light', icon: 'sun' }, { value: 'dark', label: 'Dark', icon: 'moon' }, { value: 'system', label: 'Match system', icon: 'monitor' }]}
            mode={mode}
            onModeChange={setMode}
            palettes={PALETTES.map((p) => ({ id: p.id, name: p.name.en, description: p.blurb.en }))}
            palette={palette}
            onPaletteChange={setPalette}
            about={[
              { label: 'Version', value: '0.1.0 · draft' },
              { label: 'License', value: 'MIT · free and open source' },
              { label: 'Source code', value: <button type="button" className="btn btn--link">github.com/real-zony/EmailFileViewer</button> },
            ]}
            onClose={() => go('viewer')}
          />
        )
      }
      break
    case 'error':
      tabs = [MAIN_TABS[0], MAIN_TABS[1], { id: 'err', label: 'Invoice_88213', title: 'Invoice_88213.msg', status: 'error' }]
      activeTab = errorTab
      content =
        errorTab === 'err' ? (
          <OpenErrorState
            reasonLabel="Damaged file"
            title="This file is damaged"
            fileName="Invoice_88213.msg"
            body="Parts of the file are missing or malformed, so the message can't be read. If someone sent it to you, ask them for a new copy."
            actions={
              <>
                <Button variant="secondary" icon="folder">Open another file</Button>
                <Button>Close tab</Button>
              </>
            }
          />
        ) : (
          <MessageView doc={errorTab === 'q4' ? DOCS.q4 : DOCS.aw26} remoteLoaded={false} onLoadRemote={noop} />
        )
      if (toast) {
        overlay = (
          <ToastRegion label="Notifications">
            <Toast
              title="Opened 2 of 3 files"
              actions={
                <>
                  <Button variant="secondary" size="sm" onClick={() => setErrorTab('err')}>Show</Button>
                  <Button size="sm" onClick={() => setToast(false)}>Dismiss</Button>
                </>
              }
            >
              Invoice_88213.msg couldn't be opened: the file is damaged.
            </Toast>
          </ToastRegion>
        )
      }
      break
    case 'signed':
      tabs = MAIN_TABS
      activeTab = 'q4'
      content = <MessageView doc={DOCS.q4} remoteLoaded={false} onLoadRemote={noop} />
      break
    case 'encrypted':
      tabs = MAIN_TABS
      activeTab = 'board'
      content = <MessageView doc={DOCS.board} remoteLoaded={false} onLoadRemote={noop} />
      break
    case 'exe':
      content = <MessageView doc={DOCS.venue} remoteLoaded={false} onLoadRemote={noop} />
      toolbarStart = (
        <>
          {openButton}
          <Breadcrumbs
            label="Breadcrumb"
            backLabel="Back"
            items={[{ id: 'p', label: 'Lisbon summit thread' }, { id: 'c', label: 'RE- RE- FW- Venue contract' }]}
            onNavigate={noop}
          />
        </>
      )
      overlay = (
        <ExecutableBlockedDialog
          title="“venue-floorplan-viewer-setup.exe” can't be opened here"
          body="Programs and scripts can change things on your computer, so EmailFileViewer won't open them. You can save the file and decide what to do with it yourself."
          note="Run it only if you trust the sender and were expecting this file."
          cancelLabel="Cancel"
          saveLabel="Save…"
          onSave={noop}
          onCancel={() => go('viewer')}
        />
      )
      break
  }

  const isToast = screen === 'error'
  const tabMap: Record<string, Screen> = { aw26: 'viewer', q4: 'signed', board: 'encrypted' }

  return (
    <div className="sc">
      <header className="sc__bar">
        <strong>EmailFileViewer · 组件展示页</strong>
        <label>
          界面
          <select value={screen} onChange={(e) => go(e.target.value as Screen)}>
            {SCREENS.map((s) => <option key={s.id} value={s.id}>{s.label}</option>)}
          </select>
        </label>
        <label>
          模式
          <select value={mode} onChange={(e) => setMode(e.target.value as Mode)}>
            <option value="light">浅色</option>
            <option value="dark">深色</option>
            <option value="system">跟随系统</option>
          </select>
        </label>
        <label>
          配色
          <select value={palette} onChange={(e) => setPalette(e.target.value as PaletteId)}>
            {PALETTES.map((p) => <option key={p.id} value={p.id}>{p.name.zh}</option>)}
          </select>
        </label>
        <label>
          <input type="checkbox" checked={narrow} onChange={(e) => setNarrow(e.target.checked)} /> 800px 窄窗口
        </label>
      </header>
      <div className={narrow ? 'sc__frame sc__frame--narrow' : 'sc__frame'}>
        <AppShell>
          {tabs && (
            <TabBar
              label="Open files"
              tabs={tabs}
              activeId={activeTab}
              onSelect={(id) => (screen === 'error' ? setErrorTab(id) : go(tabMap[id] ?? 'viewer'))}
              onClose={noop}
              closeLabel={(t) => `Close ${t.label}`}
              addLabel="Open file"
              onAdd={noop}
              panelId="sc-panel"
            />
          )}
          <Toolbar start={toolbarStart} end={settingsButton} />
          <MainArea id="sc-panel" labelledBy={tabs ? `tab-${activeTab}` : undefined}>
            {content}
            {isToast && overlay}
          </MainArea>
          {!isToast && overlay}
        </AppShell>
      </div>
    </div>
  )
}
