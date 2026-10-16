# 展示组件说明

`src/components/` 下的组件只负责展示：数据、文案和回调都通过 props 传入。组件里**不调用 Tauri / IPC，不包含业务逻辑，也不写死界面文字**。界面文字由调用方通过 i18n 取得后传进来（资源在 `src/i18n/locales/{en,zh-CN}.json`，用 `useTranslation()` 的 `t('key')`；key 在编译期检查，`npm run i18n` 检查两种语言的 key 和 `{{参数}}` 一致）；需要代入数字或文件名的文案，props 接收函数，例如 `moreLabel={(n) => t('header.more', { n })}`。

设计依据是已确认的草稿 `docs/design/ui-draft/`（`README.md` 是设计规范，`index.html` 是原型）。开发模式下访问 `http://localhost:1420/?showcase` 可以打开组件展示页，用静态数据重现草稿的关键界面，页面顶部可以切换界面、模式、配色和 800px 窄窗口。也可以直接带参数打开某个状态，例如 `?showcase&screen=settings&mode=dark&palette=plum`。生产构建不包含展示页。

统一从 `components/index.ts` 导入：

```tsx
import { AttachmentList, BodySheet, MessageHeader } from './components/index.ts'
```

## 主题与样式

- 颜色令牌的唯一数据源是 `src/theme/palettes.ts`。`npm run themes` 由它生成 `src/theme/themes.css`（提交到仓库）；`npm run themes:check` 和单元测试都会检查两者是否一致。**不要手改 `themes.css`。**
- 与颜色无关的令牌（字号、间距、圆角、动效）和全部组件样式在 `src/styles/app.css`。类名与草稿相同。
- 切换主题只需要改 `<html>` 上的两个属性：`data-mode`（`light` / `dark` / `system`）和 `data-palette`（方案 id）。`index.html` 里写的 `system`、`verdigris` 只是没有 Tauri 时（浏览器里 `npm run dev`）的默认值：正式运行时 Rust 在创建窗口前读取已保存的设置，通过 `initialization_script` 在首次绘制前覆盖这三个属性（`data-mode`、`data-palette`、`lang`），`main.tsx` 渲染前再同步应用一次。运行时切换由 `App` 负责（`src/settings/`）。
- 邮件纸面颜色不是主题令牌：`.sheet__body` 和 `.sheet__frame` 上的 `--efv-paper: #ffffff` 是固定值，iframe 上设置了 `color-scheme: light`。
- 窄窗口规则写在 `@container win (max-width: 860px)` 中。`.win` 就是整个 WebView，所以效果等同于媒体查询。
- 焦点环由 `.win :focus-visible` 统一提供；`prefers-reduced-motion` 时所有动效时长都会归零。
- 新增配色方案：在 `palettes.ts` 中加一项，然后运行 `npm run themes`、`npm run contrast` 和 `npm test`。

## 组件

### 布局：`AppShell`、`MainArea`、`ViewerLayout`

- 用途：`AppShell` 是 `.win`，对话框遮罩相对它定位。`MainArea` 是工具栏下方的内容区，通知和拖放遮罩相对它定位。`ViewerLayout` 是邮件查看布局：`top` 部分（邮件头和附件）不随正文滚走，正文只在 iframe 内部滚动（D7）。
- props：
  - `MainArea { id?, labelledBy? }`：传入 `labelledBy`（当前标签的 `tab-<id>`）时，作为 `tabpanel`。
  - `ViewerLayout { top, body }`

```tsx
<AppShell>
  <TabBar … panelId="main" />
  <Toolbar start={…} end={…} />
  <MainArea id="main" labelledBy={`tab-${activeId}`}>
    <ViewerLayout top={<><MessageHeader …/><AttachmentList …/></>} body={<BodySheet …>{iframe}</BodySheet>} />
  </MainArea>
  {dialog}
</AppShell>
```

### `Button`、`IconButton`、`Kbd`

- props：
  - `Button { variant?: 'primary' | 'secondary' | 'ghost' | 'warn' | 'link', size?: 'sm' | 'md' | 'lg', icon?, ref?, …button 属性 }`，默认为 `ghost` / `md` / `type="button"`。
  - `IconButton { icon, label, title? }`：`label` 是可访问名称，同时作为默认的 `title`。
- 主操作用 `primary`；工具栏上的“打开…”用 `secondary`；远程内容提示条里的按钮用 `warn`。

```tsx
<Button variant="primary" icon="folder" onClick={openDialog}>{t('empty.open')}</Button>
<IconButton icon="settings" label={t('toolbar.settings')} onClick={openSettings} />
```

### `Icon`

- `Icon { name, className? }`，始终 `aria-hidden`。可用的名称见 `IconName`。

### `SegmentedControl`、`ChoiceGroup`、`PalettePicker`

- 三者都是 WAI-ARIA radiogroup，共用 `useRovingRadio`：只有选中项在 Tab 顺序中，方向键和 Home/End 切换并立即触发 `onChange`。
- props：
  - `SegmentedControl { label? | labelledBy?, options: { value, label, icon? }[], value, onChange }`
  - `ChoiceGroup { label? | labelledBy?, options: { value, label, lang?, description? }[], value, onChange }`：语言选项用各自的语言书写，并传入 `lang`。
  - `PalettePicker { label? | labelledBy?, palettes: { id, name, description? }[], value, mode, selectedLabel, onChange }`：每张卡片里的 `PalettePreview` 带着自己的 `data-palette` 和 `data-mode`，用该方案的令牌实时渲染。`mode` 传当前的外观模式。

### `TabBar`

- props：`{ label, tabs: { id, label, title?, status?: 'ok' | 'error' }[], activeId, onSelect, onClose, closeLabel: (tab) => string, addLabel, onAdd, panelId? }`
- 少于 2 个标签页时返回 `null`（只开一个文件时隐藏标签栏）。
- 键盘：← → 切换并激活，Home / End 跳到首尾。关闭按钮不在 Tab 顺序中，关闭当前标签页的 `Ctrl/⌘+W` 由应用层实现。
- 失败的文件传 `status: 'error'`，显示红色图标。

### `Toolbar`、`Breadcrumbs`

- `Toolbar { start?, end? }` 只负责布局。
- `Breadcrumbs { label, items: { id, label, title? }[], onNavigate(index), backLabel }`：少于 2 级时不渲染。返回按钮调用 `onNavigate(最后一级 - 1)`；最后一级为 `aria-current="page"`。放在 `Toolbar` 的 `start` 中，跟在“打开…”后面。
- 窄窗口下，带 `.tool-label` 的按钮文字会隐藏，只显示图标。

### `MessageHeader`、`AddressRow`、`AddressText`

- props：
  - `MessageHeader { subject?, showFullSubjectLabel, showLessSubjectLabel, fromLabel, from?, dateLabel, dateText?, dateTime?, dateTitle?, children?, badges? }`：`subject`、`from`（一个地址或地址数组）、`dateText` 缺失或为空时，对应的标题或整行不渲染
  - `AddressRow { label, addresses, collapseAfter = 3, moreLabel(n), lessLabel, defaultExpanded? }`
- `dateText` 是调用方按界面语言和本地时区格式化好的文字（7.4），`dateTime` 是 ISO 值，`dateTitle` 是原始 Date 头。
- `children` 里放 `AddressRow`（顺序：回复至、收件人、抄送、密送）。`AddressRow` 渲染为两个网格单元，**必须放在 `MessageHeader` 里**。没有地址时整行不渲染。
- 主题按实际排版判断是否超过两行（ResizeObserver），超过时才出现“显示完整主题”。
- 折叠状态是组件内部状态。切换标签页时给 `MessageHeader` 加 `key={docId}`，每封邮件就会各自重置。

### `Badge`、`BadgeList`

- `Badge { icon, children, muted?, details?: { buttonLabel, content } }`：签名徽标用 `icon="seal"`、`muted={t('signature.notVerified')}`，并在 `details` 里放说明；加密徽标用 `icon="lock"`。徽标始终是中性色。气泡在按 Esc 或点击外部时关闭。
- 传给 `MessageHeader` 时用 `badges={<BadgeList>…</BadgeList>}`。

### `AttachmentList`、`AttachmentItem`

- props：`AttachmentList { attachments: { id, fileName, size, kind }[], labels, onOpen, onSave, onSaveAll, onOpenMessage, onBlockedOpen, collapseAfter = 6 }`
- `kind` 由调用方决定：`message` 表示嵌套邮件；`executable` 按 D6 的扩展名列表判断（这是业务逻辑，不在组件里）。
- 交互：
  - 普通文件：“打开”和保存图标按钮。
  - 嵌套邮件：“打开”会调用 `onOpenMessage`，在当前标签页内进入下一层。
  - 可执行文件：没有“打开”按钮，显示“仅可保存”。卡片本身可以获得焦点，在卡片上按回车或双击都会调用 `onBlockedOpen`，由调用方弹出 `ExecutableBlockedDialog`。
- 没有附件时不渲染；只有 1 个附件时不显示“全部保存”；超过 6 个时先显示 6 个。
- `labels.summary`（例如“4 files · 5.2 MB”）由调用方拼好。`formatFileSize` 可以直接使用（按 1024 进制）。

### `BodySheet`、`PlaceholderSheet`

- props：
  - `BodySheet { label, railText, formatLabel, notice?, children }`
  - `PlaceholderSheet { icon, title, body }`
- **`notice` 在接缝以上，`children` 在接缝以下**。`children` 只能放沙箱 iframe（7.5），应用不能在接缝以下绘制任何东西，也不能在 iframe 上叠加浮层。
- iframe 使用 `className="sheet__frame"`，样式（白底、占满剩余空间、`color-scheme: light`）已经写好。
- 加密邮件不使用 `BodySheet`，改用 `PlaceholderSheet icon="lock"`：虚线框，不画接缝。

### `Notice`、`RemoteContentNotice`

- `Notice { tone: 'warn' | 'quiet', icon, title, description?, scope?, action? }`，带 `role="status"`。
- `RemoteContentNotice { state: 'blocked' | 'loaded', blockedTitle, blockedDescription, scopeLabel, loadLabel, loadedText, onLoad }`。`loaded` 状态下收起为一行确认文字；v1 没有“重新阻止”。
- 只放在 `BodySheet` 的 `notice` 插槽里。

### `Dialog`、`ExecutableBlockedDialog`、`SettingsDialog`

- `Dialog { role?: 'dialog' | 'alertdialog', title, icon?, headerEnd?, children?, actions?, onClose, initialFocusRef?, className? }`
  - 模态对话框：按 Esc 或点击遮罩时调用 `onClose`，Tab 在对话框内循环，关闭后焦点回到打开前的元素。
  - 渲染在 `AppShell` 内、`MainArea` 之后。
- `ExecutableBlockedDialog { title, body, note, cancelLabel, saveLabel, onSave, onCancel }`：`alertdialog`，**初始焦点在“取消”上**，没有“仍然打开”。
- `SettingsDialog { labels, languages, language, onLanguageChange, modes, mode, onModeChange, palettes, palette, onPaletteChange, about?, onClose }`
  - 打开时焦点在关闭按钮上。
  - 所有选项立即生效，保存由调用方负责（8.2–8.4）。
  - `modes` 建议传 `[{ value: 'light', icon: 'sun' }, { value: 'dark', icon: 'moon' }, { value: 'system', icon: 'monitor' }]`，再加上各自的 `label`。
  - `palettes` 从 `PALETTES` 映射而来，名称按界面语言取 `name.en` 或 `name.zh`，`description` 取 `blurb`。

### `ToastRegion`、`Toast`

- `ToastRegion { label }` 放在 `MainArea` 里（位于右上角）。
- `Toast { title, children?, actions? }` 带 `role="status"`，**不自动消失**。目前只用于“打开 N 个文件中的 M 个”的汇总。

### `EmptyState`、`DropOverlay`

- `EmptyState { title, body, openLabel, onOpen, shortcutHint?, privacyNote, dragging?: { title, body } }`：整个内容区都是拖放目标。文件悬停在窗口上方时传入 `dragging`，用于显示文件数量。
- `DropOverlay { title, body }`：已有邮件打开时使用，放在 `MainArea` 里。
- 快捷键提示按平台显示：`<>{t('empty.orPress')} <Kbd>{isMac ? '⌘O' : 'Ctrl+O'}</Kbd></>`。

### `OpenErrorState`

- props：`{ reasonLabel, title, fileName, body, actions }`，带 `role="alert"`。
- 显示在失败文件自己的标签页里。`actions` 一般是“打开其他文件”和“关闭标签页”；“无法读取”还要加上“重试”（`variant="primary" icon="refresh"`）。

### `FileName`、`formatFileSize`、`splitFileName`

- `FileName { name }`：主体部分截断，扩展名始终可见。完整名称放在父元素的 `title` 里。

## 对照草稿时需要注意

- 原型中的系统标题栏（`.win__titlebar`）、导出 PDF 占位（`.future-slot`）、链接提示框示意（`.tooltip-mock`）都只用于评审，没有迁移。
- 草稿中的单选使用原生 `<input type="radio">`，这里改成了按钮加 `role="radio"`，以便统一实现 roving tabindex 和方向键行为。语言卡片的圆点改成了 CSS 绘制的 `.choice__dot`，外观与草稿一致。
- 标签页的选中样式从 `.tab[aria-selected]` 改为 `.tab--active`，因为 `role="tab"` 在内部的按钮上，而不在外层容器上。
- 附件列表的折叠在草稿中用的是 CSS `nth-child`，这里改为只渲染前 6 个。
- 展示页中的英文文案只用于对照，正式文案以 i18n 资源为准。

## 测试与脚本

| 命令 | 作用 |
|---|---|
| `npm test` | Vitest + Testing Library（jsdom）。组件测试在 `src/components/__tests__/`，主题测试在 `src/theme/__tests__/` |
| `npm run typecheck` | `tsc -b`（app、node、test 三个工程） |
| `npm run lint` | oxlint；CI 中加 `--deny-warnings` |
| `npm run themes` / `themes:check` | 生成 `themes.css` 和 Rust 读取窗口底色的 `src-tauri/src/window_colors.json`，或检查它们是否与数据源一致 |
| `npm run i18n` | 界面文案检查（`scripts/check-i18n.ts`）：两种语言的 key 集合和插值参数必须一致 |
| `npm run contrast` | 对比度检查（`scripts/check-contrast.ts`）；加 `--md` 输出 Markdown 汇总表，加 `--all` 列出每一组 |

`scripts/*.ts` 用 Node 的类型擦除直接运行（Node ≥ 22.18，CI 用 24），所以被这些脚本导入的 `src/theme/*.ts` 只能使用可擦除的 TS 语法（`tsconfig` 中开启了 `erasableSyntaxOnly`），导入路径要带 `.ts` 后缀。
