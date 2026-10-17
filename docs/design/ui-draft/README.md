# EmailFileViewer UI 草稿（任务 6.2）

状态：**第二轮草稿，等待用户复审（任务 6.3）**。用户明确确认之前不要开始第 7 组前端实现。

第一轮评审的结论已写进 `design.md` 的 D7，本稿已按结论更新：单窗口加标签页；打开失败的文件也占一个标签页；嵌套邮件在当前标签页内逐层进入；正文在 iframe 内部滚动；不做“重新阻止”和“最近打开”；修改了隐私文案；新增外观设置（明暗模式加预设配色方案，见 `app-appearance` 规格）。

## 怎么看

直接用浏览器打开 `index.html`，完全离线，不依赖 CDN 或在线字体。

- 顶部深色条是草稿导航，不属于应用。可以切换界面、**模式**（浅色/深色/跟随系统）、**配色**（5 套方案）、语言、窗口宽度（1200/800）和“显示标注”。模式和配色可以任意组合，所有界面在任意组合下都能查看。
- 应用内的“设置”对话框和草稿导航共用同一份状态。在设置里换配色，整个原型会立即跟着变。
- 可以用带参数的链接直接打开某个状态：`index.html?mode=dark&palette=plum&lang=zh&width=narrow&notes=1#settings`。

| 文件 | 用途 | 迁移到 `ui/` 吗 |
|---|---|---|
| `palettes.js` | **配色方案的唯一数据源**：5 套方案，每套都有浅色和深色两组完整的颜色令牌 | **迁移**（改写成 TS 模块） |
| `build-themes.js` | 用 `palettes.js` 生成 `themes.css`（`node build-themes.js`） | **迁移**，作为构建步骤 |
| `themes.css` | 生成的主题 CSS：每个方案、每种模式一组令牌块 | 构建产物，不手改 |
| `check-contrast.js` | 对比度检查（`node check-contrast.js`），有不达标项时退出码为 1 | **迁移**，在 CI 中运行（D7） |
| `styles.css` | 与颜色无关的令牌（字号、间距、圆角、动效）和全部组件样式 | **迁移** |
| `app.js` | 原型渲染。每个 `function Xxx()` 对应一个 React 组件；`STR` 中是中英文文案初稿 | 只作参考 |
| `mail-bodies.js` | 示例邮件正文 | 不迁移，可以改成测试夹具 |
| `draft.css`、`index.html` | 草稿导航、标注和方案对比板 | 不迁移（`index.html` 中首次绘制前应用设置的脚本可以参考） |

## 设计方向

**一张信纸放在安静的桌面上，信纸和桌面之间有一道看得见的接缝。**

- 应用界面是“桌面”，配色方案只改变桌面。邮件正文是“信纸”，**在任何方案、任何模式下都是白底**，按邮件自己的样式显示，纯文本邮件也一样（深色文字、浅色底）。
- 纸面顶部是一条 4px 的斜纹“接缝”，颜色取当前方案的强调色，用来标出信任边界：接缝以上由应用绘制，可信；接缝以下是沙箱 iframe 里的邮件内容，不可信。远程内容提示条放在接缝以上，邮件正文无法伪造它的位置。没有发件人内容可以显示时（加密邮件、错误），不画接缝。
- 斜纹只出现在接缝、空状态的信封插图和文件拖入时的拖放框。这是整套界面唯一的装饰性图形。
- 只用系统字体。邮箱地址、正文标签和附件类型用等宽字体，方便分辨 l/I、0/O 这类仿冒字符。

## 外观：模式与配色方案

### 5 套预设方案

| id | 名称 | 定位 |
|---|---|---|
| `verdigris` | Verdigris / 铜绿 | **默认方案**。偏冷的矿物灰，青绿色强调色。 |
| `graphite` | Graphite / 石墨 | 纯中性灰，不用彩色强调（主按钮是近黑或近白），界面完全不和邮件抢颜色。 |
| `indigo` | Indigo / 靛蓝 | 蓝灰底色，墨蓝强调色，接近多数邮件客户端的习惯。 |
| `plum` | Plum / 梅紫 | 偏暖的石灰色，低饱和的梅紫强调色。更柔和，但依然克制。 |
| `contrast` | High contrast / 高对比度 | 黑白配色，边框更重，所有文字对比度不低于 7:1。供视力不便或强光环境下使用。 |

为什么这样选：

- 5 套方案在底色冷暖（冷灰、中性灰、蓝灰、暖灰、纯黑白）和强调色色相（青绿、无彩、蓝、紫）上都明显不同，但饱和度都压得很低。
- 有意避开了黄、橙、红作为强调色，因为琥珀和红已被语义色占用（琥珀表示“需要你做决定”，红表示“没能完成”）。否则在某个方案里，主按钮会和警告长得一样。
- **语义色在所有方案中保持同一套色相和含义**。浅色和深色各一组，只有高对比度方案把它们加深或提亮到 7:1 以上。

### 规则

- 模式：`light`、`dark`、`system`，默认 `system`。跟随系统的切换完全由 CSS 的 `prefers-color-scheme` 完成，系统外观变化时立即生效，不需要 JS 监听。
- 方案：默认 `verdigris`。切换方案不改变当前模式（规格“切换配色方案”）。
- **邮件纸面颜色不是主题令牌。**`styles.css` 中 `.sheet__body`、`.sheet__frame` 的 `--efv-paper: #ffffff` 是一个固定常量，不在 `palettes.js` 里，任何方案都不能覆盖。iframe 上设置了 `color-scheme: light`，系统的深色模式也不会传进正文。
- 不支持自定义取色，也不支持导入导出主题。

### 实现方式（给 coder）

1. **数据源**：`palettes.js` 的 `PALETTES` 数组，每项为 `{ id, name: {en, zh}, blurb: {en, zh}, light: {...}, dark: {...} }`。令牌名省略 `--efv-` 前缀，语义色由共享的 `SEMANTIC` 补齐（方案可以覆盖，目前只有高对比度方案覆盖了）。实现时改写成 `ui/src/theme/palettes.ts`，`build-themes` 和 `check-contrast` 都从这里读取。
2. **生成 CSS**：`build-themes.js` 为每个方案输出三段（见 `themes.css`）：

   ```css
   [data-palette="plum"] { /* 浅色令牌 */ }
   [data-palette="plum"][data-mode="dark"] { /* 深色令牌 */ }
   @media (prefers-color-scheme: dark) { [data-palette="plum"][data-mode="system"] { /* 深色令牌 */ } }
   ```

   另外还有与方案无关的模式级值（`color-scheme`、阴影、遮罩、接缝透明度），以及一个 `:root` 兜底（默认方案的浅色值），用于属性还没设置时。选择器有意不写成 `:root[...]`，这样任何元素都可以带自己的 `data-palette` 和 `data-mode`。设置对话框里的方案预览就是这样实时渲染的。
3. **根元素**：`<html data-mode="system" data-palette="verdigris">`。切换时只改这两个属性，所有颜色通过 CSS 变量即时生效，不需要重新渲染 React 树。
4. **首次绘制前应用（不闪烁）**：
   - Rust 端在创建窗口前，从 store 插件的设置文件中读出 `mode`、`palette`、`lang`，用 `WebviewWindowBuilder::initialization_script` 注入一小段脚本，在任何页面脚本之前给 `<html>` 设好这两个属性和 `lang`。
   - 同时用 `background_color` 把窗口背景设为对应方案、对应模式的 `--efv-desk`，避免 WebView 加载前闪出白底。模式是 `system` 时，Rust 端按当前系统主题选择颜色。
   - `index.html` 的 `<head>` 里保留一段同步脚本作为后备（原型中有示范），读不到注入值时就使用默认值。
   - 前端启动后再从 store 插件异步读取一次，只用来同步 React 状态，不应该引起可见的变化。
5. **保存**：修改立即写入 store 插件（`appearance.mode`、`appearance.palette`），重启后保留（规格“重启后保持设置”）。
6. **派生值**：悬停和按下的叠加色由 `color-mix(in srgb, var(--efv-ink) 6% / 10%, transparent)` 派生，不进入方案数据。三个平台的 WebView 都支持 `color-mix`（WebView2/Chromium 111 起、WKWebView 16.2 起、WebKitGTK 2.40 起）。
7. **CI**：运行 `check-contrast` 并检查退出码；同时检查 `themes.css` 和 `palettes` 是否一致（重新生成后 `git diff --exit-code`）。

## 设计令牌

### 与颜色无关（`styles.css` 的 `:root`，各方案共用）

- 字体：`--efv-font-ui`（`system-ui` 等系统栈，含中文字体）；`--efv-font-mono`（`ui-monospace`、SF Mono、Cascadia Mono 等）。
- 字号：`--efv-text-2xs` 到 `--efv-text-2xl`，分别是 11 / 12 / 13 / 14 / 16 / 20 / 22 px。界面默认 13px，主题 22px（窄窗口下 19px）。
- 行高：1.25 / 1.45 / 1.6。字重：400 / 500 / 600。
- 间距：`--efv-space-0` 到 `--efv-space-12`，分别是 2 / 4 / 8 / 12 / 16 / 20 / 24 / 32 / 48 px。
- 圆角：sm 4、md 6、lg 8、xl 12，以及 pill。
- 动效：fast 120ms、base 180ms、slow 260ms，缓动 `--efv-ease-out`。`prefers-reduced-motion` 时全部归零。
- 布局：工具栏高 44px，标签栏高 36px，内容左右边距 20px（窄窗口下 14px），接缝高 4px。
- 模式级（`themes.css`）：`--efv-scrim`、`--efv-shadow-card`、`--efv-shadow-sheet`、`--efv-shadow-pop`、`--efv-seam-opacity`。

### 颜色令牌的角色与用途限制

每个方案都要完整提供下面这些令牌。**用途限制是硬性规定**，对比度检查就是按这些用途写的。

| 令牌 | 角色 | 可以用于 |
|---|---|---|
| `desk` | 窗口底色、工具栏、当前标签页 | 背景 |
| `surface` | 正文上方的应用区、分段控件底色 | 背景 |
| `raised` | 卡片、对话框、次要按钮 | 背景 |
| `sunken` | 标签栏 | 背景 |
| `ink` | 主要文字 | 任何文字 |
| `ink-2` | 次要文字：标签、大小、提示 | 任何文字（在以上 4 种背景上都 ≥ 4.5:1） |
| `ink-3` | **只能用于图标、分隔符和装饰线**（≥ 3:1） | **不能用于文字**。在 `sunken` 上最低只有 3.6:1 |
| `line` | 卡片边框、分隔线 | **只能装饰**。不能单独承担“看出这是一个控件”的作用 |
| `line-strong` | 输入类控件和次要按钮的边框 | 只能装饰，控件靠文字和底色来辨认 |
| `accent` | 主按钮底色、链接文字、选中边框、接缝 | 文字（在 desk/surface/raised 上 ≥ 4.5:1）。链接一律加下划线，因为石墨和高对比度方案的强调色接近文字颜色 |
| `accent-hover` | 主按钮悬停 | 背景 |
| `accent-ink` | 主按钮上的文字 | 只能放在 `accent` 或 `accent-hover` 上 |
| `accent-soft` / `accent-soft-ink` | 选中状态的底色和文字、拖放状态、邮件类附件图标 | 必须成对使用 |
| `focus` | 焦点环（2px，偏移 2px） | 在所有背景上 ≥ 3:1 |
| `warn-bg` / `warn-line` / `warn-ink` / `warn-icon` | 需要用户做决定：远程内容提示、可执行附件 | `warn-ink` 用于文字，`warn-icon` 只能用于图标 |
| `danger-bg` / `danger-line` / `danger-ink` / `danger-icon` | 没能完成：打开失败 | 同上 |

### 各方案的取值

以下表格由 `node check-contrast.js --tokens` 生成，取值以 `palettes.js` 为准。

#### Verdigris / 铜绿（`verdigris`，默认）

| Token | Light | Dark |
|---|---|---|
| `--efv-desk` | `#e6eae8` | `#121715` |
| `--efv-surface` | `#f1f4f2` | `#1a201e` |
| `--efv-raised` | `#ffffff` | `#222927` |
| `--efv-sunken` | `#d8dfdb` | `#0c100f` |
| `--efv-ink` | `#16201c` | `#e3e9e6` |
| `--efv-ink-2` | `#4a5752` | `#a6b2ad` |
| `--efv-ink-3` | `#67736e` | `#85918c` |
| `--efv-line` | `#cbd3cf` | `#2f3835` |
| `--efv-line-strong` | `#a9b5b0` | `#46524e` |
| `--efv-accent` | `#17695b` | `#5ec2ae` |
| `--efv-accent-hover` | `#10574b` | `#7ad3c1` |
| `--efv-accent-ink` | `#ffffff` | `#072019` |
| `--efv-accent-soft` | `#dcede8` | `#173832` |
| `--efv-accent-soft-ink` | `#0f4f44` | `#9fe0d2` |
| `--efv-focus` | `#17695b` | `#7ad8c5` |

#### Graphite / 石墨（`graphite`）

| Token | Light | Dark |
|---|---|---|
| `--efv-desk` | `#e8e8e7` | `#161616` |
| `--efv-surface` | `#f3f3f2` | `#1e1e1e` |
| `--efv-raised` | `#ffffff` | `#272727` |
| `--efv-sunken` | `#dadad8` | `#0f0f0f` |
| `--efv-ink` | `#1b1b1b` | `#e8e8e8` |
| `--efv-ink-2` | `#4f4f4e` | `#ababab` |
| `--efv-ink-3` | `#6b6b6a` | `#8a8a8a` |
| `--efv-line` | `#cfcfcd` | `#333333` |
| `--efv-line-strong` | `#ababa9` | `#4b4b4b` |
| `--efv-accent` | `#2d2d2d` | `#e4e4e4` |
| `--efv-accent-hover` | `#121212` | `#ffffff` |
| `--efv-accent-ink` | `#ffffff` | `#141414` |
| `--efv-accent-soft` | `#e1e1e0` | `#363636` |
| `--efv-accent-soft-ink` | `#1b1b1b` | `#f2f2f2` |
| `--efv-focus` | `#1b1b1b` | `#f2f2f2` |

#### Indigo / 靛蓝（`indigo`）

| Token | Light | Dark |
|---|---|---|
| `--efv-desk` | `#e7e9ef` | `#121420` |
| `--efv-surface` | `#f2f3f7` | `#191c2a` |
| `--efv-raised` | `#ffffff` | `#212436` |
| `--efv-sunken` | `#d9dce6` | `#0b0d16` |
| `--efv-ink` | `#161a2b` | `#e3e5f0` |
| `--efv-ink-2` | `#4a5068` | `#a7abc2` |
| `--efv-ink-3` | `#676d84` | `#868ba3` |
| `--efv-line` | `#cdd1dc` | `#2d3146` |
| `--efv-line-strong` | `#a9afc0` | `#444a64` |
| `--efv-accent` | `#3949a0` | `#8f9cf0` |
| `--efv-accent-hover` | `#2c3a86` | `#a9b3f5` |
| `--efv-accent-ink` | `#ffffff` | `#10142e` |
| `--efv-accent-soft` | `#e0e4f6` | `#222a52` |
| `--efv-accent-soft-ink` | `#26337a` | `#c3cbfa` |
| `--efv-focus` | `#3949a0` | `#a9b3f5` |

#### Plum / 梅紫（`plum`）

| Token | Light | Dark |
|---|---|---|
| `--efv-desk` | `#ebe7e4` | `#18141a` |
| `--efv-surface` | `#f5f2f0` | `#201b22` |
| `--efv-raised` | `#ffffff` | `#29232b` |
| `--efv-sunken` | `#ddd7d3` | `#100d11` |
| `--efv-ink` | `#221b1f` | `#ece4ea` |
| `--efv-ink-2` | `#574c52` | `#b5a9b2` |
| `--efv-ink-3` | `#74686e` | `#958890` |
| `--efv-line` | `#d4ccc8` | `#352d37` |
| `--efv-line-strong` | `#b3a9a5` | `#4d434f` |
| `--efv-accent` | `#6e3f78` | `#d3a3dc` |
| `--efv-accent-hover` | `#5a3063` | `#e2bde9` |
| `--efv-accent-ink` | `#ffffff` | `#2a1230` |
| `--efv-accent-soft` | `#efe3f1` | `#3a2840` |
| `--efv-accent-soft-ink` | `#552c5e` | `#ecd2f1` |
| `--efv-focus` | `#6e3f78` | `#e2bde9` |

#### 语义色（前四套方案共用）

| Token | Light | Dark |
|---|---|---|
| `--efv-warn-bg` | `#fbf1dc` | `#2b2412` |
| `--efv-warn-line` | `#e5c584` | `#5a4719` |
| `--efv-warn-ink` | `#6b4700` | `#f2cb7a` |
| `--efv-warn-icon` | `#9a6500` | `#e9b44c` |
| `--efv-danger-bg` | `#fbe8e5` | `#31191a` |
| `--efv-danger-line` | `#eab5ad` | `#6a2d28` |
| `--efv-danger-ink` | `#8c2217` | `#ffaa9d` |
| `--efv-danger-icon` | `#bd3526` | `#ff7c6c` |

#### High contrast / 高对比度（`contrast`，含加强的语义色）

| Token | Light | Dark |
|---|---|---|
| `--efv-desk` | `#ffffff` | `#000000` |
| `--efv-surface` | `#f2f2f2` | `#0e0e0e` |
| `--efv-raised` | `#ffffff` | `#141414` |
| `--efv-sunken` | `#e4e4e4` | `#1c1c1c` |
| `--efv-ink` | `#000000` | `#ffffff` |
| `--efv-ink-2` | `#262626` | `#dcdcdc` |
| `--efv-ink-3` | `#4d4d4d` | `#b3b3b3` |
| `--efv-line` | `#767676` | `#8c8c8c` |
| `--efv-line-strong` | `#3d3d3d` | `#cccccc` |
| `--efv-accent` | `#0a4a9e` | `#8dbcff` |
| `--efv-accent-hover` | `#073a7c` | `#b3d2ff` |
| `--efv-accent-ink` | `#ffffff` | `#000000` |
| `--efv-accent-soft` | `#dce8fa` | `#0c2a55` |
| `--efv-accent-soft-ink` | `#052e63` | `#d6e6ff` |
| `--efv-focus` | `#000000` | `#ffffff` |
| `--efv-warn-bg` | `#fff3d1` | `#231a00` |
| `--efv-warn-line` | `#7a5200` | `#c99a2e` |
| `--efv-warn-ink` | `#3d2800` | `#ffdc8a` |
| `--efv-warn-icon` | `#6b4700` | `#ffc94a` |
| `--efv-danger-bg` | `#ffe8e5` | `#2a0b08` |
| `--efv-danger-line` | `#9b1d10` | `#ff8a7a` |
| `--efv-danger-ink` | `#6b0c03` | `#ffbdb3` |
| `--efv-danger-icon` | `#9b1d10` | `#ff8a7a` |

## 对比度检查

运行方式：`node check-contrast.js` 输出完整报告，`--md` 输出下面的汇总表，`--tokens` 输出上面的取值表。

- 每个方案、每种模式检查 31 组“前景/背景”，总共 310 组。每组都对应界面里一个真实用法，脚本中的 `PAIRS` 写明了用在哪里。
- 文字组要求 ≥ 4.5:1：`ink` 和 `ink-2` 放在 4 种背景上、链接、主按钮文字、选中状态、警示和错误文字。
- 图标和焦点组要求 ≥ 3:1：`ink-3` 图标、焦点环放在 4 种背景上、选中边框、警示和错误图标。
- 大号文字（主题、对话框标题）都使用 `ink`，已经包含在 ≥ 4.5:1 的检查里。

**结果：310 组全部达标，没有不达标项。**

| Palette | Mode | Lowest text pair (≥ 4.5) | Lowest icon / focus pair (≥ 3) | Result |
|---|---|---|---|---|
| Verdigris 铜绿 | light | 5.4 (`accent` on `desk`) | 3.6 (`ink-3` on `sunken`) | pass |
| Verdigris 铜绿 | dark | 6.8 (`ink-2` on `raised`) | 4.5 (`ink-3` on `raised`) | pass |
| Graphite 石墨 | light | 5.9 (`ink-2` on `sunken`) | 3.8 (`ink-3` on `sunken`) | pass |
| Graphite 石墨 | dark | 6.5 (`ink-2` on `raised`) | 4.3 (`ink-3` on `raised`) | pass |
| Indigo 靛蓝 | light | 5.8 (`ink-2` on `sunken`) | 3.7 (`ink-3` on `sunken`) | pass |
| Indigo 靛蓝 | dark | 6.0 (`accent` on `raised`) | 4.6 (`ink-3` on `raised`) | pass |
| Plum 梅紫 | light | 5.8 (`ink-2` on `sunken`) | 3.7 (`ink-3` on `sunken`) | pass |
| Plum 梅紫 | dark | 6.8 (`ink-2` on `raised`) | 4.5 (`ink-3` on `raised`) | pass |
| High contrast 高对比度 | light | 7.5 (`accent` on `surface`) | 6.6 (`ink-3` on `sunken`) | pass |
| High contrast 高对比度 | dark | 9.4 (`accent` on `raised`) | 7.3 (`accent` on `accent-soft`) | pass |

和第一轮相比的调整：

- `ink-3` 第一轮可以用于次要文字；**现在只能用于图标和分隔线**，按 3:1 检查（铜绿浅色在 `desk` 上是 4.1:1，在 `sunken` 上是 3.6:1，都不够文字的 4.5:1）。
- `warn-icon`（浅色）从 `#a26b00` 加深为 `#9a6500`，在 `warn-bg` 上从 4.0:1 提高到 4.4:1，留出余量。
- 链接改为始终带下划线，因为在石墨和高对比度方案中，强调色和文字颜色接近，单靠颜色区分不了。
- 邮件正文的颜色不在检查范围内：那是邮件自己的样式，应用不负责。

## 组件清单（React props 约定）

类名直接沿用 `styles.css`。`docId` 对应 sidecar 的文档 id。

```ts
type Address = { name?: string; address: string };
type Attachment = { id: string; fileName: string; size: number; mimeType: string; isMessage: boolean };
type OpenErrorKind = "unsupported" | "corrupt" | "unreadable";
type Mode = "light" | "dark" | "system";
type PaletteId = "verdigris" | "graphite" | "indigo" | "plum" | "contrast";
```

| 组件 | 主要 props | 说明 |
|---|---|---|
| `AppWindow` | `children` | `.win`。真实应用里的标题栏由系统绘制 |
| `TabBar` | `tabs: {id, label, title, status: "ok" \| "error"}[]`、`activeId`、`onSelect`、`onClose`、`onOpenFile` | **只有 1 个标签页时不渲染**。打开失败的文件也是一个标签页（`status: "error"`，红色图标）。按 WAI-ARIA tabs 模式实现：方向键切换，roving tabindex |
| `Toolbar` | `crumbs?`、`onOpen`、`onBack`、`onOpenSettings` | 窄窗口下只显示图标 |
| `Breadcrumbs` | `items: {docId, label}[]`、`onNavigate(index)` | 嵌套邮件逐层进入时显示；最后一级为 `aria-current="page"` |
| `MessageHeader` | `subject`、`from`、`replyTo?`、`to?`、`cc?`、`bcc?`、`date`（ISO，带偏移）、`rawDate?`、`signed?`、`encrypted?` | 缺少的字段整行不渲染；主题超过两行时折叠 |
| `AddressRow` | `field`、`addresses`、`collapseAfter = 3` | 超出部分折叠为 “+N more”，按钮带 `aria-expanded` |
| `AddressText` | `address`、`emphasis?` | 没有名称，或名称与地址相同时，只显示地址 |
| `SignatureBadge` | `onExplain` | 中性色，不用绿色 |
| `AttachmentList` | `docId`、`attachments`、`onOpen`、`onSave`、`onSaveAll`、`onOpenMessage` | 超过 6 个时先显示 6 个；只有 1 个附件时不显示“全部保存” |
| `AttachmentItem` | `attachment`、`kind: "file" \| "message" \| "executable"` | 可执行文件只显示“仅可保存”，触发默认动作时弹出 `ExecutableBlockedDialog`；邮件类附件点“打开”在当前标签页内进入下一层 |
| `FileName` | `name` | 主体部分截断，扩展名始终可见 |
| `BodySheet` | `html`、`format`、`remoteContent: "none" \| "blocked" \| "loaded"`、`onLoadRemote` | 结构依次是 `.sheet__chrome`、`.sheet__seam`、`.sheet__body > iframe`。纸面颜色固定，不使用主题令牌 |
| `RemoteContentNotice` | `state`、`onLoad` | 只能放在 `.sheet__chrome` 里。v1 没有“重新阻止” |
| `EncryptedPlaceholder` | — | 虚线框，不画接缝 |
| `EmptyState` | `onOpen`、`isDragging`、`dragCount?` | 没有“最近打开”列表 |
| `DropOverlay` | `count` | 已有邮件打开时盖在内容区上 |
| `OpenErrorState` | `kind`、`fileName`、`inTab?`、`onRetry?`、`onOpenAnother`、`onCloseTab?` | 在失败文件自己的标签页里显示 |
| `OpenReportToast` | `opened`、`total`、`failures`、`onShow`、`onDismiss` | 一次打开多个文件且有失败时显示；不自动消失 |
| `ExecutableBlockedDialog` | `fileName`、`onSave`、`onCancel` | `alertdialog`，初始焦点在“取消”上 |
| `SettingsDialog` | `language`、`mode`、`palette`，以及三个 `onChange` | 分为“语言”“外观”“关于”三部分。打开时焦点落在关闭按钮上 |
| `ModeSwitch` | `value: Mode`、`onChange` | 带图标的三段式单选组（`role="radiogroup"`），方向键切换 |
| `PalettePicker` | `value: PaletteId`、`mode: Mode`、`onChange` | 每个方案一张卡片，内含 `PalettePreview`；选中的卡片显示强调色边框和对勾，并带有 `sr-only` 的“已选择”文字。方案说明放在卡片的 `title` 里 |
| `PalettePreview` | `palette: PaletteId`、`mode: Mode` | 外层元素带 `data-palette` 和 `data-mode`，内部用该方案自己的令牌画一个迷你界面（标签栏、文字、白纸面和接缝、主按钮）。`aria-hidden` |
| `Button` | `variant`、`size`、`iconOnly?` | 只有图标的按钮必须有 `aria-label` 和 `title` |

## 页面结构与布局

```
.win
├─ [系统标题栏]                      "<文件名> — EmailFileViewer"
├─ TabBar                            ≥2 个标签页时才显示
├─ Toolbar        [打开…] [‹ 面包屑]  ……  [设置]
└─ .win__main
   └─ .viewer（overflow: hidden）
      ├─ .viewer__top                 邮件头 + 附件，不随正文滚走
      │                               最高占 55%，超出时自身滚动，底边有滚动阴影提示
      └─ BodySheet（flex: 1；min-height: 200px）
         ├─ .sheet__chrome            应用层
         ├─ .sheet__seam
         └─ .sheet__body > iframe     正文在 iframe 内部滚动（D7）
对话框：.scrim 盖住整个 webview
```

- 断点只有一个：窗口宽度 ≤ 860px（真实应用中用 `@media (max-width: 860px)`；原型用容器查询）。窄窗口下：日期单独一行，工具栏只显示图标，边距 14px，主题字号 19px，附件变为 2 列。
- 建议的最小窗口尺寸：720 × 520。
- 键盘：所有可交互元素都有 `:focus-visible` 焦点环。快捷键：`Ctrl/⌘+O` 打开，`Ctrl/⌘+W` 关闭标签页（关闭最后一个后回到空状态，不退出应用），`Ctrl+Tab` 切换标签页，`Alt+←` / `⌘[` 返回上一级嵌套邮件，`⌘,` 打开设置，`Esc` 关闭对话框。

## 多文件：单窗口加标签页（已采用，D7）

- 每个文件占一个标签页，只有 1 个时隐藏标签栏。再次打开文件时，在已运行的实例里新增标签页并激活窗口。
- 打开失败的文件也占一个标签页，在标签页里显示错误；一次打开多个文件时，另加一条汇总通知。
- 嵌套邮件在当前标签页内逐层进入，用面包屑返回。
- 原型中“多文件方案（已定）”一页保留了对比板作为决策记录，多窗口方案已标为“未采用”。

## 实现要点

1. **iframe 与周围界面。**
   - iframe 使用 `sandbox="allow-popups allow-popups-to-escape-sandbox"` 和 `srcdoc`。纸面背景固定为白色，并设置 `color-scheme: light`。
   - 正文在 iframe 内部滚动，不注入测高脚本（D7）。
   - 应用绘制的任何东西都不能放在 `.sheet__seam` 以下，也不能叠加在 iframe 上。
2. **提示的层级**（从高到低）：模态对话框 `.scrim` → 窗口级通知 `.toasts` → 拖放遮罩 → 邮件头里的徽标（已签名/已加密）→ 正文上方的提示条（远程内容）。一类信息只出现在对应的层级里。
3. **远程内容。**“加载”只对当前这个 docId 生效，只保存在内存里；关掉标签页再打开就恢复阻止。重新渲染时更换 CSP meta，不改 sandbox 属性。
4. **链接。**只依靠 sidecar 写入的 `title`，由系统显示原生提示框，界面不做自定义状态栏。
5. **日期**用 `Intl.DateTimeFormat` 按界面语言格式化并转换到本地时区；原始 Date 头放在 `title` 里。**文件大小**按 1024 进制计算，小于 10 MB 时保留一位小数。
6. **文案。**句首大写；会弹出对话框的按钮带省略号；错误文案说明发生了什么、下一步怎么做，不道歉。隐私说明按 D7 改为 “Messages are read on this computer. Nothing is uploaded unless you choose to.” / “邮件只在本机读取。除非你主动选择，否则不会上传任何内容。”。完整中英文文案在 `app.js` 的 `STR` 中。
7. **原生菜单（建议）。**macOS 上提供“文件 → 打开… / 关闭标签页”和“EmailFileViewer → 设置…”。
8. **导出 PDF（不在 v1）。**位置在工具栏右侧、设置按钮之前，同时出现在原生“文件”菜单里。v1 不渲染这个控件；原型中的 `.future-slot` 只用来评审，不要迁移。

## 需要用户决定的开放问题

1. **应用名称和图标**：目前继续使用 EmailFileViewer 作为占位。正式名称会影响窗口标题、空状态和关于部分的文案。
