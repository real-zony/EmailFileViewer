# Proposal

## Why

很多人收到 `.msg` / `.eml` 邮件文件却没有 Outlook，尤其是 macOS 和 Linux 用户，文件根本打不开；现有同类工具大多只支持 Mac、按次买断，且几乎没有覆盖 Windows/Linux 的跨平台方案。我们先做一个免费、开源、跨平台的桌面查看器，作为整个产品的基础和获客入口，后续的付费云端 PDF 导出建立在它之上。

## What Changes

- 新建跨平台桌面应用：Tauri 2 作为外壳（Rust + 系统 WebView），支持 Windows、macOS、Linux。
- 新建 .NET 解析核心，以 sidecar 进程形式随应用分发，通过 stdio 与 Tauri 通信；使用 MimeKit 解析 EML、MsgReader 解析 MSG。
- 打开邮件：文件对话框、拖放、双击文件（文件关联）；应用已运行时，新打开的文件交给已有实例处理。
- 查看邮件：显示邮件头（发件人、收件人、抄送、日期、主题）和正文（HTML / RTF / 纯文本），正确处理内嵌图片和各类字符集（含 GBK、Big5 等旧式编码）。
- 安全渲染：邮件正文在沙箱中渲染，禁止脚本；远程内容默认不加载，用户可按封邮件手动允许。查看邮件本身不产生任何网络请求。
- 附件：列出附件，支持单个或全部保存，支持打开嵌套邮件（msg 内的 msg、eml 内的 `message/rfc822`）及 winmail.dat 中的附件。
- 界面语言：默认英文，支持简体中文，跟随系统语言。
- 外观：浅色、深色、跟随系统三种模式，加上 4–6 套预设配色；只改变应用界面，不改变邮件正文。
- 分发：GitHub Actions 三平台矩阵构建，产出签名的安装包（macOS 签名并公证、Windows 签名、Linux 提供 AppImage 和 deb）。

不在本次范围：

- PDF 导出及一切付费功能、license 激活、收款（后续单独的 change）。
- 云端转换 API（放在独立的私有仓库）。
- 自动更新、打印、`.emlx` / `.oft` / `.pst` / `.mbox` 等其他格式、移动端。

## Capabilities

### New Capabilities

- `email-opening`: 用户把邮件文件交给应用的各种方式（对话框、拖放、文件关联、命令行参数、单实例转发），以及文件无法识别或已损坏时的处理。
- `email-viewing`: 邮件头和正文的展示，包括正文格式回退、字符集、内嵌图片、沙箱渲染、远程内容控制，以及签名/加密邮件的提示。
- `email-attachments`: 附件的列出、保存和嵌套邮件的打开。
- `app-localization`: 界面语言的选择与切换（英文默认，简体中文）。
- `app-appearance`: 明暗模式与预设配色方案的选择、持久保存和可读性要求，以及外观不影响邮件正文的约束。
- `app-distribution`: 三个平台的安装包形态、签名与公证要求，以及支持的最低系统版本。

### Modified Capabilities

（无，项目中尚无已有规格）

## Impact

- **代码结构**：新增 Tauri 应用（Rust 外壳 + 前端）、.NET 解决方案（解析核心 + sidecar 宿主 + 测试）、GitHub Actions 工作流。
- **依赖**：Tauri 2、.NET 10（LTS）、MimeKit、MsgReader；前端框架在 design 中确定。
- **外部前置条件**：Apple Developer Program 账号（macOS 签名与公证）；Windows 代码签名渠道（Certum 云签名或 SignPath 开源签名，个人开发者无法使用 Azure Artifact Signing）。
- **仓库定位**：本仓库（客户端）开源，MIT 协议；服务端代码不进入本仓库。
- **后续工作**：`add-cloud-pdf-export` 将复用本次的解析核心生成"可打印 HTML"。
