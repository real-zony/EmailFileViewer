# Design

## Context

- 仓库是全新的，目前只有 OpenSpec 配置和子代理定义（`agents/`），没有任何代码。动机见 proposal.md 的 Why。
- 本仓库是**客户端**，已在 GitHub 公开（`real-zony/EmailFileViewer`，MIT 协议）；付费的云端 PDF 转换放在独立的私有仓库，不进入本仓库。
- 后续的 `add-cloud-pdf-export` 会复用本次的解析结果生成"可打印 HTML"并上传渲染，所以解析层的输出要从一开始就设计成可复用的规范化模型。
- 开发者以 .NET 为主要技术栈；CI 使用 GitHub Actions。
- 行为要求见 `specs/` 下的六个规格，本文只解释"怎么做"和"为什么这样做"。

## Goals / Non-Goals

**Goals:**

- 解析邮件的代码运行在独立进程中，畸形文件即使让解析器崩溃也不影响界面。
- 不可信的邮件内容永远不进入拥有应用权限的上下文。
- 解析结果是一个与界面无关的规范化文档模型，查看器和将来的 PDF 导出共用。
- 三个平台的构建、签名、发布全部由 CI 自动完成，可以从标签复现。

**Non-Goals:**

- Windows / Linux 的 arm64 安装包（后续按需求再加）。
- 自动更新、Mac App Store / Microsoft Store 上架。
- 在客户端本地生成 PDF。

## Decisions

### D1. Tauri 2 外壳 + .NET sidecar 进程

```
+--------------------------------------------------------------+
| Tauri app process (Rust)                                     |
|                                                              |
|  +------------------------------+   commands   +----------+  |
|  | main webview (React UI)      | -----------> | Rust     |  |
|  |  - has Tauri IPC             |              | host     |  |
|  |  +------------------------+  |              |  dialogs |  |
|  |  | sandboxed iframe       |  |              |  opener  |  |
|  |  | (email body, no IPC)   |  |              |  single- |  |
|  |  +------------------------+  |              |  instance|  |
|  +------------------------------+              +----+-----+  |
|                                                     |        |
+-----------------------------------------------------|--------+
                                                      | stdio, JSON lines
                                                      v
                                   +---------------------------------+
                                   | sidecar process (.NET 10)       |
                                   |  EmailFileViewer.Core           |
                                   |   - format detection            |
                                   |   - MimeKit (EML) / MsgReader   |
                                   |   - normalize -> EmailDocument  |
                                   |   - HTML sanitize / rewrite     |
                                   |  no network, no secrets         |
                                   +---------------------------------+
```

- **为什么不是纯 .NET（Photino.NET 等）**：Tauri 自带的打包、文件关联、单实例、签名公证流程省掉大量平台相关工作。
- **为什么不是纯 Rust**：Rust 生态缺少成熟的 MSG 解析（OLE 复合文档、压缩 RTF、RTF 内嵌 HTML、嵌套 msg），MimeKit + MsgReader 是现成最好的组合。
- **为什么是独立进程而不是 NativeAOT 导出 C 接口供 Rust 调用**：进程隔离让解析器崩溃不会拖垮界面（对应 `email-opening` 的"解析过程中崩溃"场景）；跨进程只传 JSON，不需要处理跨语言的内存管理。

### D2. 进程间通信：stdio 上的 JSON 行协议

- 每行一个 JSON 消息，形如 `{id, method, params}` / `{id, result}` / `{id, error: {code, message}}`。
- 主要方法：`email.open(path)`、`email.openNested(docId, attachmentId)`、`attachment.save(docId, attachmentId, targetPath)`、`attachment.saveAll(docId, targetDir)`、`attachment.extractForOpen(docId, attachmentId)`、`document.close(docId)`。
- sidecar 在内存中按 `docId` 缓存已解析的文档，保存和打开附件时不必重新解析；关闭文档时释放。
- **附件字节不经过 stdio**：由 Rust 弹出保存对话框拿到路径，再让 sidecar 直接写文件。stdio 上只传元数据和正文 HTML。
- 内嵌图片在 v1 中直接转成 `data:` URI 写进正文 HTML，实现最简单；如果遇到图片很大的邮件导致消息过大，再改成自定义协议按需读取。
- Rust 端负责监管 sidecar：每个请求有超时；进程退出时，所有进行中的请求以"解析失败"返回，下次请求时自动重启进程。
- **未选方案**：localhost HTTP 需要管理端口，而且本机任何进程都能调用；命名管道在三个平台上的实现各不相同。

### D3. sidecar 发布方式：优先 NativeAOT，不行就退回自包含发布

- NativeAOT 的好处：体积小、启动快（打开邮件要秒开）；macOS 上不需要 JIT 相关的 entitlements，公证更简单。
- MsgReader 依赖 RtfPipe、OpenMcdf、Microsoft.Maui.Graphics，是否兼容 AOT 未经验证，**第一个任务就是做实验**：开启 `IsAotCompatible` 和裁剪分析，在三个平台上发布 AOT 版本，并用样本邮件跑通。
- 退路：用自包含、单文件、开启裁剪的普通发布（JIT）。代价是体积更大，macOS 上需要为 sidecar 配置 JIT 相关的 entitlements。
- **实验结论（任务 1.4）：采用 NativeAOT。** 依据：
  - smoke 走到的代码路径上，build 和 publish 都是 0 条 IL/AOT/裁剪警告。
  - CI 在 win-x64、linux-x64、osx-arm64、osx-x64 四个平台上都完成了测试、AOT 发布和 smoke；Linux 产物经 `ldd` 和 `LD_DEBUG` 确认不依赖 libicu。
  - sidecar 体积：实验时 macOS arm64 未压缩约 6.2 MB；四个平台打包后（zip）约 2.9–4.4 MB。第 3、4 组完成后（加入字符集识别、CMS 解码、stdio 协议）约 10 MB。
- **已知的 AOT 警告**（对 MimeKit、MsgReader、OpenMcdf、RtfPipe 做全量分析得到，细节见 `core/docs/aot-spike.md`）：
  - RtfPipe `ControlTag.Negate`：IL2075 和 IL3050 各 1 条，任务 3.3 调用 RtfPipe 时会出现。实测 `\b0`、`\i0` 等输出正确，接受。
  - MimeKit 默认 S/MIME 上下文：IL2026。任务 3.7 只按 MIME 类型识别签名和加密，**不要创建 S/MIME 上下文**，以避开这条路径。
  - MsgReader 的 Reactions JSON 和 `MailMessage.WriteTo` 反射：我们不调用，不受影响。
  - 之后每组任务若新增其他 IL/AOT 警告，需要在审核时逐条确认。
- 不论哪种方式，sidecar 都以 Tauri `externalBin` 的方式打包，文件名带目标三元组；macOS 通用版本用 `lipo` 把两个架构的 sidecar 合并成一个。

### D4. 解析核心：规范化的 EmailDocument 模型

- `EmailFileViewer.Core` 是一个不依赖界面、不访问网络的类库，输出 `EmailDocument`：邮件头（地址列表、日期带时区偏移的 ISO 8601）、正文（类型 + 处理后的 HTML）、附件列表（id、文件名、大小、MIME 类型、是否为嵌套邮件）、标志位（已签名、已加密、包含远程内容）。
- **格式识别**：文件头是 OLE 复合文档签名 `D0 CF 11 E0 A1 B1 1A E1` 的按 MSG 处理，否则按 MIME 解析；两者都失败则返回"不支持的格式"。
- **字符集**：启动时注册 `CodePagesEncodingProvider`，否则 .NET 不认识 GBK、Big5 等编码；字符集缺失时交给 MimeKit 的回退机制和 MsgReader 已依赖的字符集识别库。
- **正文选择**：优先 HTML（MSG 中封装在 RTF 里的 HTML 由 MsgReader 解出），其次把纯 RTF 转成 HTML，最后把纯文本转义后包进 `<pre>` 风格的容器。
- **winmail.dat**：EML 中的 TNEF 部分用 MimeKit 解出其中的附件，作为普通附件返回。
- **HTML 处理**（用 HTML 解析器而不是正则）：删除脚本、事件属性和 `javascript:` 链接，作为第二道防线；把 `cid:` 替换成 `data:` URI；检测远程资源引用并设置"包含远程内容"标志；给所有链接加上 `target="_blank"`，并把 `title` 设为实际地址，这样悬停时不需要脚本也能看到真实地址。
- 将来 PDF 导出复用同一个模型和 HTML，查看器和 PDF 看到的是同一份内容。

### D5. 信任边界：沙箱 iframe + CSP + 能力限制

```
 untrusted                          trusted
 +--------------------------+      +----------------------------------+
 | iframe srcdoc=<email>    |      | main webview (app origin)        |
 |  sandbox="allow-popups   | ---> |  Tauri IPC, capabilities scoped  |
 |   allow-popups-to-escape |  X   |  to this window + local origin   |
 |   -sandbox"              |      |                                  |
 |  no allow-scripts        |      |  new-window request (link click) |
 |  no allow-same-origin    |      |   -> Rust: scheme in             |
 |  no allow-forms          |      |      {http, https, mailto}?      |
 |  CSP default: img-src    |      |   -> open in system browser      |
 |   data: only             |      |   -> deny in-app window          |
 +--------------------------+      +----------------------------------+
```

- **沙箱 iframe 是安全边界**，D4 中的 HTML 清理只是第二道防线。不给 `allow-scripts` 和 `allow-same-origin`，正文就是一个不透明来源、不能运行脚本的文档。
- **远程内容**：正文文档内带一条 `<meta>` CSP，默认 `img-src data:; style-src 'unsafe-inline'; font-src data:`，阻止一切远程资源；用户对当前邮件选择"加载"时，用放宽后的 CSP（允许 `https:` / `http:` 的图片、样式、字体）重新渲染这一封。选择不持久化。
- **链接**：所有链接都是 `target="_blank"`，点击时产生新窗口请求，由 Rust 拦截：只放行 `http`、`https`、`mailto`，交给系统默认程序打开，并拒绝在应用内创建窗口。**三个平台上 Tauri 的新窗口拦截行为需要实验确认**（见风险）。
- Tauri 2 的 capabilities 只授予主窗口的本地来源；所有 Rust 命令只接受文档 id 和由 Rust 自己弹出的对话框得到的路径，不接受前端传入的任意文件路径来写文件。

### D6. 附件的保存与打开

- 文件名清理放在 Core 中：去掉路径成分，替换三个平台的非法字符，处理 Windows 保留名（`CON`、`NUL` 等）；写入前再确认最终路径位于目标文件夹之内。
- "全部保存"遇到重名时生成 `name (1).ext` 这样的新名称，不覆盖已有文件。
- 打开附件：sidecar 把附件写到本次会话专用的临时目录，Rust 用系统默认程序打开；临时目录在应用退出和下次启动时清理。
- 可执行文件和脚本按扩展名拒绝直接打开（Windows：`.exe .bat .cmd .com .msi .ps1 .vbs .js .jse .wsf .scr .lnk .hta .cpl`；macOS：`.app .command .pkg`；Linux：`.sh .desktop .AppImage .run`），只允许保存。
  - 实现后补充了这些同样会执行代码或常被用来投递恶意软件的类型：
    - 代码和脚本：`.jar .vbe .ws .wsh .psm1 .psd1 .ps1xml .reg .msc`
    - 快捷方式：`.scf .url .pif`
    - 安装包：`.msix .msixbundle .appx .appxbundle .appinstaller .application`
    - Excel 加载项：`.xll`
    - 可挂载的磁盘映像：`.iso .img .vhd .vhdx`
  - 判断时按清理后文件名的最后一个扩展名，不区分大小写。
- 解析 S/MIME 的 MSG 前，Core 先在内存副本中把消息类改回 `IPM.Note` 再交给 MsgReader。原因是 MsgReader 处理 S/MIME 时会调用证书链验证，可能联网检查吊销状态，违反"查看时不产生网络请求"。

### D7. 前端：React + TypeScript + Vite（建议的默认，待确认）

- 选择理由：生态最大，组件库和 i18n 方案成熟，编码子代理也最熟悉。
- 备选：Svelte（更轻）、Vue。对 Tauri 来说三者都可以，这里主要考虑的是长期维护和协作成本。
- 国际化用成熟的 i18n 库；系统语言通过 Tauri 的 os 插件读取；语言设置用 store 插件保存；日期用 `Intl.DateTimeFormat` 按界面语言格式化，时区转换基于 sidecar 返回的带偏移量的时间。
- **UI 评审结论（2026-10-17，用户确认）**：
  - **单窗口加标签页**：每个打开的文件占一个标签页，只开一个文件时隐藏标签栏。再次打开文件时，只需在已运行的实例中新增标签页并激活窗口；Tauri 的权限也只需授予这一个主窗口。数据层仍按"每封邮件一个 `docId`"设计。
  - **打开失败的文件也占一个标签页**，在标签页内显示错误；一次打开多个文件时，另加一条汇总通知。
  - **嵌套邮件在当前标签页内逐层进入**，用面包屑返回，不另开标签页。
  - **正文在 iframe 内部滚动**：邮件头和附件区固定在上方，不往沙箱中注入测高脚本，保持 D5 的隔离。
  - v1 不提供"重新阻止远程内容"（关掉标签页再打开即恢复阻止），也不做"最近打开"列表（不在本地保存文件路径）。
  - 空状态的隐私说明不写 "never uploaded"，改为 "Messages are read on this computer. Nothing is uploaded unless you choose to."，为以后需要用户同意才上传的云端导出留出余地。
- **外观（`app-appearance`）**：
  - 设计令牌统一用 `--efv-` 前缀的 CSS 变量。
  - 明暗模式和配色方案分别写在根元素的两个属性上（例如 `data-mode` 和 `data-palette`），每套方案为两种模式各提供一组令牌值。"跟随系统"通过 `prefers-color-scheme` 实现。
  - 设置用 store 插件保存；启动时在首次绘制之前应用，避免闪烁。
  - CI 中运行对比度检查脚本，逐套方案、逐种模式校验文字和背景的对比度。
  - 邮件正文的纸面颜色不使用主题令牌，始终是浅色底。

### D8. UI 草稿评审：前端实现前的必经关卡

- 所有前端界面先由 ui-designer（定义见 `agents/ui-designer.md`）产出草稿：设计令牌（颜色、字体、间距、圆角）、核心组件、以及至少这几个示例界面：空状态/拖放区、邮件查看（邮件头 + 正文 + 附件）、远程内容提示条、错误状态。
- **草稿交给用户评审，确认后才开始前端编码**；评审提出的修改先改草稿，再实现。
- 草稿位于 `docs/design/ui-draft/`（静态原型加 `README.md` 设计规范）。第一轮评审后用户要求增加主题功能，草稿补上外观设置和全部预设配色后需要再审一次。
- 后端部分（sidecar、Rust 外壳、CI）不受这道关卡阻塞，可以并行推进。

### D9. 仓库结构

```
/
+-- ui/                         React + TS + Vite frontend
+-- src-tauri/                  Tauri 2 shell (Rust)
|   +-- binaries/               sidecar binaries (CI output, not committed)
+-- core/
|   +-- EmailFileViewer.sln
|   +-- src/EmailFileViewer.Core/
|   +-- src/EmailFileViewer.Sidecar/
|   +-- tests/EmailFileViewer.Core.Tests/
+-- samples/                    synthetic / redistributable test emails
+-- .github/workflows/
+-- openspec/  agents/  CLAUDE.md
```

- 样本邮件只使用自己构造的或明确允许再分发的邮件，不使用真实的私人邮件；需要覆盖：营销类 HTML、只有 RTF 正文的 MSG、GBK/Big5、`cid:` 图片、嵌套邮件、winmail.dat、S/MIME 签名和加密、畸形文件。

### D10. CI 与发布

```
                         push tag v*
                              |
     +---------------+--------+--------+------------------+
     |               |                 |                  |
 windows-latest  ubuntu-22.04     macos-latest      macos-15-intel
 (win-x64)       (linux-x64)      (osx-arm64)       (osx-x64)
     |               |                 |                  |
 publish sidecar publish sidecar  publish sidecar   publish sidecar
 + core tests    + core tests     + core tests      + core tests
 + smoke run     + smoke run      + smoke run       + smoke run
     |               |                 |                  |
 tauri build     tauri build      tauri build       tauri build
 + sign          AppImage, deb    + sign, notarize  + sign, notarize
     |               |                 |                  |
     +---------------+--------+--------+------------------+
                              v
               draft GitHub Release + SHA-256 checksums
```

- Linux 在 **ubuntu-22.04** 上构建，不用 `ubuntu-latest`：glibc 只向后兼容，在旧系统上编出来的程序才能在更多发行版上运行。
- macOS Intel 使用 `macos-15-intel`（GitHub 计划 2027 年秋退役，届时换成 `macos-26-intel` 或在 arm64 上交叉编译）。
- 冒烟测试：每个平台都用刚发布的 sidecar 实际解析全部样本邮件，确认 AOT / 裁剪后的程序行为正确。
- 签名：macOS 的 Developer ID 证书和公证凭据放在 GitHub Secrets，由 `tauri-action` 完成签名和公证；CI 中额外用 `codesign -dv` 检查 sidecar 是否也已签名。Windows 通过 Tauri 的自定义 `signCommand` 接入云签名服务（Certum 或 SignPath，见待定问题）。
- 普通的推送和 PR 只跑构建和测试，不签名、不发布。

### D11. 前端与 Rust 的接口

前端只认 Rust 分配的**文档句柄**（整数），不接触 sidecar 的 docId，也**从不向 Rust 传入文件路径**。所有打开文件的来源（对话框、拖放、启动参数、单实例转发）都由 Rust 处理，结果以事件的形式通知前端。

```
 sources (Rust)                         Rust -> UI events
 +--------------------+                 +-------------------------------+
 | open dialog        |                 | efv://document-opened         |
 | drag-drop (window) | --> open batch  | efv://document-open-failed    |
 | argv at startup    |     (batchId)   | efv://open-batch-finished     |
 | single-instance    |                 +-------------------------------+
 +--------------------+
```

**命令**（`invoke`）。出错时抛出 `{ code, message }`：

| 命令 | 参数 | 返回 |
|------|------|------|
| `app_ready` | — | `void`：前端注册好事件监听后调用；在此之前 Rust 把启动参数和单实例转来的文件先排队 |
| `open_with_dialog` | — | `void`：弹出多选对话框，结果通过事件返回；用户取消时不发任何事件 |
| `open_nested` | `handle, attachmentId` | `{ handle, document }`：在当前标签页内逐层进入 |
| `close_document` | `handle` | `void` |
| `save_attachment` | `handle, attachmentId` | `{ path } \| null`：Rust 弹出保存对话框，默认文件名是清理后的附件名；取消时返回 null |
| `save_all_attachments` | `handle` | `{ saved: [{attachmentId, path}], failed: [{attachmentId, code}] } \| null`：Rust 弹出选择文件夹的对话框 |
| `open_attachment` | `handle, attachmentId` | `void`：可执行类型抛出 `executable_refused` |

**事件**（`listen`）：

| 事件 | 内容 |
|------|------|
| `efv://document-opened` | `{ batchId, handle, fileName, document }` |
| `efv://document-open-failed` | `{ batchId, fileName, code, message }` |
| `efv://open-batch-finished` | `{ batchId, opened, failed }`，用于多文件部分失败时的汇总通知 |

**其他约定**：
- `document` 的结构与 sidecar 协议中的 `EmailDocument` 完全一致（camelCase），以 `core/docs/sidecar-protocol.md` 为准。
- 错误码沿用 sidecar 的错误码，另外增加：`sidecar_crashed`（解析进程崩溃，前端按"文件已损坏或无法解析"显示）、`timeout`、`dialog_failed`。
- 拖放遮罩：前端通过 `getCurrentWebview().onDragDropEvent` 的 enter、over、leave 显示遮罩和文件数量。真正的打开由 Rust 在窗口的拖放事件中处理，前端收到 drop 时只需隐藏遮罩。
- 外部链接：由 5.7 的新窗口拦截处理，前端不需要命令。

## Risks / Trade-offs

- [MsgReader 或 MimeKit 不兼容 AOT] → 按 D3 退回自包含的 JIT 发布，并补上 macOS entitlements；把这个实验排在第一个任务。
- [Tauri 的新窗口拦截在某个平台上行为不一致，链接无法交给系统浏览器] → 先做实验；退路是让链接不可点击，但显示实际地址并提供"复制链接"操作。
  - **macOS 实验结果（任务 5.7，2026-10-17，WKWebView）：拦截有效，不需要退路。**
    - Tauri 的 `WebviewWindowBuilder::on_new_window` 在 macOS 上对应 WKWebView 的 `createWebViewWithConfiguration` 代理。（读 wry 源码）没有设置回调时，wry 返回 nil，新窗口请求被静默丢弃；设置回调后，回调的返回值决定结果，我们始终返回 `Deny`。
    - 在沙箱 iframe（`allow-popups allow-popups-to-escape-sandbox`）里点击 `target="_blank"` 的 `http` 链接：回调被调用，链接交给 opener，**系统默认浏览器（Chrome）收到了请求**，应用内没有出现新窗口（应用的窗口数不变）。`window.open` 同样。
    - `file://` 链接：回调没有被调用（没有日志），也没有任何程序被启动，推测是 WebKit 在更早的地方拒绝了。
    - 没有 `target` 的链接在 iframe 内导航：被应用 CSP 的 `frame-src`（回退到 `default-src 'self'`）拦下，既不在 iframe 里加载，也不会打开外部程序。sidecar 给所有链接加了 `target="_blank"`，所以正常邮件不会出现这种情况。
    - 实验方法的限制：本机没有辅助功能权限，无法合成真实的鼠标点击，所以点击是由主窗口的脚本通过 `webview.eval`（WKWebView 把它当作用户手势）在 iframe 内触发的，iframe 为此多加了 `allow-same-origin`。沙箱标志不影响 `createWebView` 代理这条路径，但**真实鼠标点击还没有在 macOS 上手动确认**。
    - **Windows 和 Linux 无法在本机实验，留到 10.2 实机验证**（WebView2 的 `NewWindowRequested`、WebKitGTK 的 `create` 信号）。
- [Tauri IPC 能被 iframe 中的内容访问] → capabilities 只授予主窗口本地来源；用专门构造的恶意样本邮件在三个平台上验证 iframe 中拿不到 IPC。
  - **macOS 实验结果（任务 5.7，2026-10-17）：iframe 里拿不到 IPC。** 用 `samples/html-malicious.eml` 的原始 HTML（含脚本、事件属性、`javascript:` 链接）放进沙箱 iframe（`sandbox="allow-popups allow-popups-to-escape-sandbox"`，meta CSP 与前端一致）：脚本一个都没有执行，没有收到任何 `postMessage`。
  - 纵深防御（仅测试用，给 iframe 加 `allow-scripts`，并临时去掉应用 CSP 让脚本能跑）：iframe 内 `window.__TAURI_INTERNALS__` 和 `window.__TAURI__` 都是 `undefined`（macOS 上初始化脚本只注入主框架），访问 `parent.__TAURI_INTERNALS__` 抛 `SecurityError`；直接 `fetch('ipc://localhost/app_ready')` 被 IPC 协议拒绝（HTTP 500，缺少 `Tauri-Invoke-Key`，这个密钥只有主框架知道）。
  - 注意：`initialization_script` 在 **Windows 上会进入所有子框架**（Tauri 文档），那里 iframe 内可能存在 `__TAURI_INTERNALS__`，防线是 capabilities 只授予本地来源（沙箱 iframe 的来源是不透明的）。**Windows 和 Linux 留到 10.2 实机验证。**
- [三个平台的 WebView 对 sandbox / srcdoc / CSP 的实现有差异] → 样本邮件在三个平台上都跑界面测试；差异只影响查看器，PDF 将来在服务端统一渲染。
  - **macOS 实验结果（任务 5.7，2026-10-17）：`srcdoc` 的 iframe 继承应用的 CSP**，邮件正文的 meta CSP 只能在其基础上进一步收紧，所以应用 CSP 必须允许邮件需要的 `data:`、`https:`、`http:` 图片和字体以及内联样式，否则用户允许远程内容后也加载不了。实测：
    - 主框架里运行时插入的内联脚本被 `script-src 'self'` 拦截；`srcdoc` 的 iframe 即使带 `allow-scripts`，内联脚本也被继承的 CSP 拦截（去掉应用 CSP 后同一个 iframe 里的脚本会执行，说明拦截来自 CSP）。
    - iframe 里的内联样式属性、`<style>` 元素、`data:` 图片都正常；默认的邮件 CSP 下远程图片没有发出请求，放宽成允许 `http:` 之后图片请求到达了服务器。
    - **Tauri 会给页面里的 `<style>` 元素加 nonce 并写进 `style-src`，nonce 一出现，`'unsafe-inline'` 就被忽略，iframe 里的邮件样式会全部失效。** 因此 `tauri.conf.json` 里设置了 `dangerousDisableAssetCspModification: ["style-src"]`，之后页面里有没有 `<style>` 都不影响邮件样式（实验页面里特意放了一个 `<style>`）。
    - Windows（WebView2）和 Linux（WebKitGTK）对 sandbox / srcdoc / CSP 的行为**留到 10.2 实机验证**。
- [超大邮件（几十 MB 的内嵌图片）导致 JSON 消息过大、界面卡顿] → v1 先用 `data:` URI，设定单条消息大小的监控；超过阈值时改用自定义协议按需加载。
- [macOS 最低支持 15 会排除较旧的 Mac] → 这是 .NET 10 官方支持范围决定的；后续可以在 macOS 13/14 上实测，确认能运行后再放宽说明。
- [Windows 签名服务对个人开发者的身份审核耗时较长] → 尽早申请；审核通过前的内部测试版本不签名，只在 GitHub 上以预发布形式提供。
- [三个平台的 WebView 渲染不一致，同一封邮件显示略有不同] → 查看器接受这种差异；需要一致输出的场景（PDF）交给服务端。

## Migration Plan

全新项目，没有迁移。发布节奏：先发不签名的 `v0.x` 预发布版本用于内部验证，签名和公证流程跑通后发布 `v1.0`。

## Open Questions

- 应用的正式名称和 bundle identifier：工程中先用占位值，配置签名和文件关联之前确定。
- Windows 签名渠道选 Certum 还是 SignPath：两者都通过 `signCommand` 接入，不影响其余设计。
