# Tasks

## 1. .NET 脚手架与 NativeAOT 实验（design D3）

- [x] 1.1 在 `core/` 下创建 `EmailFileViewer.sln`，包含 `src/EmailFileViewer.Core`、`src/EmailFileViewer.Sidecar`、`tests/EmailFileViewer.Core.Tests` 三个 net10.0 项目；验证 `dotnet build core` 和 `dotnet test core` 均通过
- [x] 1.2 Core 引入 MimeKit 和 MsgReader，Sidecar 开启 `IsAotCompatible`、`PublishAot` 和裁剪分析；实现 `--smoke <dir>` 命令（逐个解析目录中的文件，输出主题和附件数，任一文件失败时以非零码退出），先用从 MimeKit/MsgReader 测试集取来的少量样本跑通；验证在 macOS arm64 上 `dotnet publish -r osx-arm64` 成功、smoke 命令通过，并把全部 IL/AOT 警告记录下来
- [x] 1.3 新增 `.github/workflows/core.yml`：在 windows-latest、ubuntu-22.04、macos-latest、macos-15-intel 上执行测试、发布 sidecar、运行 smoke；验证四个平台的任务全部通过
- [x] 1.4 根据 1.2 和 1.3 的结果决定使用 NativeAOT 还是自包含 JIT 发布，把结论和依据补进 design.md 的 D3；验证 `core.yml` 使用的是选定的发布方式且通过

## 2. 样本邮件库

- [x] 2.1 从 MimeKit 和 MsgReader 的测试集中挑选可再分发（MIT）的样本放入 `samples/`，并在 `samples/ATTRIBUTION.md` 中记录来源和协议；验证每个文件都有来源记录
- [x] 2.2 构造缺失的样本：GBK 正文、未声明字符集的 Big5、RFC 2047 编码的邮件头、只有 RTF 正文的 MSG、HTML 封装在 RTF 中的 MSG、`cid:` 内嵌图片、多张远程图片的营销邮件、MSG 内嵌 MSG、EML 内嵌 `message/rfc822`、winmail.dat、S/MIME 签名、S/MIME 加密、带脚本/事件属性/`javascript:` 链接/IPC 探测代码的恶意 HTML、链接文字与地址不符、文件名含 `../` 和 Windows 保留名的附件、可执行附件、截断损坏的文件、扩展名为 `.eml` 的 MSG；无法生成的类别在 README 中注明；验证 `samples/README.md` 中列出的每个类别都至少对应一个文件或一条缺口说明

## 3. 解析核心 EmailFileViewer.Core（design D4、D6）

- [x] 3.1 实现格式识别（OLE 复合文档签名判定为 MSG，否则按 MIME 解析）和三类错误（不支持的格式、已损坏、无法读取）；验证单元测试覆盖：扩展名为 `.eml` 的 MSG、PNG 文件、截断文件、无读权限的文件
- [x] 3.2 定义 `EmailDocument` 模型，实现 EML 到模型的映射（地址的名称和邮箱、带时区偏移的 ISO 8601 日期、缺失字段为空）；验证单元测试覆盖 `email-viewing` 中"显示邮件头"和"缺少某个邮件头"两个场景
- [x] 3.3 实现 MSG 到模型的映射，包括 RTF 中封装的 HTML 和纯 RTF 转 HTML；验证只有 RTF 正文的 MSG 样本能输出保留段落的 HTML
- [ ] 3.4 注册 `CodePagesEncodingProvider`，处理声明的字符集、RFC 2047 邮件头，以及字符集缺失时的自动识别；验证 GBK、Big5（未声明）和 RFC 2047 样本的主题和正文与预期文本完全一致
- [x] 3.5 实现正文选择优先级（HTML、RTF、纯文本），纯文本转义后保留换行；验证单元测试覆盖 `email-viewing` 中"正文格式选择"的全部场景
- [ ] 3.6 实现附件列表：排除被正文 `cid:` 引用的内嵌图片、标记嵌套邮件、展开 winmail.dat；验证单元测试覆盖 `email-attachments` 中"列出附件""打开嵌套邮件""展开 winmail.dat"的场景
- [ ] 3.7 识别 S/MIME 签名和加密并设置对应标志，加密邮件不尝试解出正文；验证签名和加密样本的标志与正文结果正确
- [ ] 3.8 实现 HTML 处理：删除脚本、事件属性和 `javascript:` 链接；`cid:` 转为 `data:` URI；检测远程资源并设置标志；给链接加 `target="_blank"` 并把 `title` 设为实际地址；验证恶意 HTML 样本处理后不含任何脚本或事件属性，营销邮件样本被标记为包含远程内容，链接的 `title` 等于 `href`
- [ ] 3.9 实现附件文件名清理（去掉路径成分、替换非法字符、处理 Windows 保留名）、重名时生成 `name (1).ext`、写入前校验最终路径在目标目录内、按扩展名识别可执行和脚本类型（列表见 design D6）；验证单元测试覆盖 `../../.bashrc`、`CON.txt`、重名和每个平台的可执行扩展名

## 4. Sidecar 宿主与通信协议（design D2）

- [ ] 4.1 实现 stdio 上的 JSON 行协议：请求、响应、错误的消息格式，按方法名分发，按 `docId` 缓存文档；单个请求抛出未处理异常时返回错误响应、进程不退出；验证基于内存流的协议测试通过，其中包括一个故意抛出异常的请求
- [ ] 4.2 实现 `email.open`、`email.openNested`、`document.close`；验证协议测试能打开普通邮件、打开两层嵌套邮件，关闭文档后再访问该文档返回"文档不存在"错误
- [ ] 4.3 实现 `attachment.save`、`attachment.saveAll`、`attachment.extractForOpen`（写入会话专用的临时目录，可执行类型直接拒绝），以及启动时清理上次残留的临时目录；验证保存的文件与原附件字节一致、重名时不覆盖、可执行附件的提取请求返回拒绝错误
- [ ] 4.4 编写 `core/docs/sidecar-protocol.md`，列出每个方法的参数、返回值和错误码；验证文档中的示例消息能被协议测试原样解析

## 5. Tauri 外壳（design D1、D5）

- [ ] 5.1 创建 Tauri 2 应用（`src-tauri/`）和 React + TypeScript + Vite 前端（`ui/`），前端只放一个无样式的占位页面，不涉及视觉设计；在根目录 README 中写明本地开发的环境要求和启动步骤；验证按 README 执行 `cargo tauri dev` 能在 macOS 上打开窗口
- [ ] 5.2 配置 sidecar 为 `externalBin`（文件名带目标三元组），编写构建脚本把 `dotnet publish` 的产物复制到 `src-tauri/binaries/`；验证 `cargo tauri build` 生成的应用包中含有 sidecar 且能启动
- [ ] 5.3 在 Rust 中实现 sidecar 监管：按需启动、JSON 行收发、请求超时、进程退出时让所有进行中的请求以"解析失败"返回并在下次请求时重启；验证用一个会崩溃的假 sidecar 编写的 Rust 测试通过
- [ ] 5.4 实现 Rust 命令：打开文件（路径只来自 Rust 弹出的对话框、拖放事件或启动参数）、打开嵌套邮件、关闭文档、保存附件、全部保存、用系统程序打开附件；验证在占位页面上手动调用每个命令都能得到预期结果
- [ ] 5.5 配置 `.eml` / `.msg` 文件关联，接入单实例插件，处理 macOS 的 `RunEvent::Opened` 以及 Windows/Linux 的启动参数，再次打开文件时把窗口带到前台；验证在 macOS 上双击文件能启动应用，应用运行时再双击另一个文件由已有实例打开且只有一个进程
- [ ] 5.6 接入窗口的文件拖放事件，只接受文件路径，忽略文本和链接；验证拖入多个文件时每个都被打开，拖入文本时不报错
- [ ] 5.7 配置 capabilities，只授予主窗口的本地来源；实现新窗口请求拦截，只放行 `http`、`https`、`mailto` 并交给系统默认程序，拒绝在应用内创建窗口；在占位页面中用沙箱 iframe 加载恶意 HTML 样本，在三个平台上做实验；验证 iframe 内无法调用任何 IPC、点击链接在系统浏览器中打开；某个平台拦截无效时实现退路（链接不可点击，显示实际地址并可复制），并把各平台的结果补进 design.md 的风险部分

## 6. UI 设计草稿（评审关卡，design D8）

- [x] 6.1 由 ui-designer 按 `agents/ui-designer.md` 产出设计草稿：设计令牌（颜色、字体、间距、圆角，含深色模式）、核心组件、示例界面（空状态/拖放区、邮件查看含邮件头/正文/附件、远程内容提示条、签名与加密提示、错误状态），以及多个文件的展示方案（多窗口或标签页）；验证草稿以可在浏览器中查看的静态原型交付
- [x] 6.2 按第一轮评审结论更新草稿：增加外观设置（浅色/深色/跟随系统，以及 4–6 套预设配色，每套都有明暗两种变体，邮件正文纸面不受主题影响）；按 design.md D7 的评审结论去掉多窗口备选方案和已解决的开放问题，改掉 "never uploaded" 文案；验证每套配色的明暗变体都能在原型中切换查看，`README.md` 写明每套配色的令牌值及对比度检查结果
- [x] 6.3 **把更新后的草稿交给用户复审，在用户明确确认之前不得开始第 7 组任务**；评审意见先改草稿再复审；验证用户已明确确认

## 7. 前端实现（依赖 6.3）

- [ ] 7.1 搭建 i18n 基础（英文和简体中文两套资源），所有界面文字都通过 key 引用；增加一个检查脚本，两种语言的 key 不一致时失败；验证该脚本在 CI 中运行并通过
- [ ] 7.2 按确认的草稿实现设计令牌和核心组件；验证组件渲染测试（Vitest + Testing Library）通过，并逐一对照草稿检查
- [ ] 7.3 实现空状态、打开文件按钮和拖放区；验证组件测试中模拟的 IPC 调用参数正确
- [ ] 7.4 实现邮件头视图（名称加邮箱、缺失字段隐藏、按界面语言格式化本地时间）；验证测试覆盖 `email-viewing` 中"日期按本地时区显示"的跨时区场景
- [ ] 7.5 实现正文视图：沙箱 iframe（不带 `allow-scripts`、`allow-same-origin`、`allow-forms`）、默认严格 CSP、远程内容提示条，用户允许后只对当前邮件放宽 CSP 并重新渲染；验证测试断言 iframe 的 sandbox 属性和两种 CSP 字符串，并用营销邮件样本手动确认远程图片默认不加载、允许后加载、切换到其他邮件后又恢复阻止
- [ ] 7.6 实现签名和加密邮件的提示（签名邮件注明"未验证签名"）；验证签名和加密样本分别显示对应提示
- [ ] 7.7 实现附件列表、保存、全部保存、打开、打开嵌套邮件，以及可执行附件被拒绝时的提示；验证组件测试覆盖每个操作，并用样本手动确认 `email-attachments` 的各个场景
- [ ] 7.8 实现错误提示（不支持的格式、已损坏、无法读取，并显示文件名），多个文件中部分失败时互不影响；验证测试覆盖 `email-opening` 中"无法识别或损坏的文件"和"一次打开多个文件"的场景
- [ ] 7.9 实现单窗口标签页（只有一个文件时隐藏标签栏；打开失败的文件也占一个标签页，并另加汇总通知；嵌套邮件在当前标签页内逐层进入，用面包屑返回）；验证一次打开三个文件时三封邮件都能独立查看和关闭，其中一个失败时显示为错误标签页

## 8. 界面语言与外观（app-localization、app-appearance）

- [ ] 8.1 通过 os 插件读取系统语言，首次启动时 `zh-CN` 使用简体中文、其他语言使用英文；验证测试覆盖 `zh-CN` 和 `de-DE` 两个场景
- [ ] 8.2 在设置中提供语言切换，立即生效，并用 store 插件持久保存；验证切换后界面立即变化、重启后保持，邮件主题和正文不受影响
- [ ] 8.3 实现明暗模式（浅色、深色、跟随系统，默认跟随系统；跟随系统时实时响应系统外观变化），在首次绘制前应用设置以避免闪烁；验证测试覆盖 `app-appearance` 中"明暗模式"的三个场景
- [ ] 8.4 实现预设配色方案切换，与明暗模式组合生效，模式和方案都用 store 插件持久保存；验证测试覆盖"预设配色方案"和"外观设置持久保存"的场景，并确认深色模式下 HTML 和纯文本邮件正文仍为浅色底
- [ ] 8.5 增加对比度检查脚本，逐套方案、逐种模式校验文字与背景（≥4.5:1）以及大号文字和图标（≥3:1）；验证脚本在 CI 中运行并通过，故意把某个颜色改成低对比度时会失败

## 9. 打包、签名与发布（app-distribution，design D10）

- [ ] 9.1 配置安装包：Windows 用 NSIS 生成 `.exe` 并内置 WebView2 引导安装，macOS 生成 arm64 和 x64 两个 `.dmg`（或用 `lipo` 合并 sidecar 后生成通用版），Linux 生成 AppImage 和声明了依赖的 `.deb`；验证在 CI 中三个平台都产出这些文件
- [ ] 9.2 新增 `.github/workflows/release.yml`：推送 `v*` 标签时在四个平台上构建，创建草稿 Release 并附上 SHA-256 校验文件；在 `docs/release.md` 中写明发版步骤和需要配置的 Secrets；验证推送测试标签 `v0.0.1-test` 后生成的草稿 Release 包含全部安装包和校验文件
- [ ] 9.3 **（需要用户操作）** 准备 Apple Developer Program 账号和 Windows 签名渠道（Certum 或 SignPath），把凭据配置进 GitHub Secrets；验证 `docs/release.md` 中列出的 Secrets 都已配置
- [ ] 9.4 在 release 流程中加入 macOS 签名和公证，并对应用和 sidecar 执行 `codesign -dv` 和 `spctl -a -vv` 检查；验证在一台干净的 Mac 上从浏览器下载 dmg 后首次打开不出现拦截提示
- [ ] 9.5 通过 `signCommand` 接入 Windows 签名，对安装程序、主程序和 sidecar 执行 `signtool verify /pa`；验证在 Windows 上运行安装程序时显示发布者名称
- [ ] 9.6 验证卸载：在 Windows 和通过 deb 安装的 Ubuntu 上卸载后，应用文件被删除且 `.eml` / `.msg` 不再关联本应用

## 10. 端到端验收

- [ ] 10.1 在各最低平台（Windows 10 1809 且未装 .NET 和 WebView2、macOS 15、Ubuntu 22.04、Debian 13、Fedora 43）上安装正式构建的安装包，用全部样本逐条核对六个规格中的场景；把结果写入 `docs/verification/add-desktop-viewer.md`，验证每个场景都有通过记录或已登记的问题
- [ ] 10.2 安全与隐私验收：在三个平台上用恶意 HTML 样本确认脚本不执行、IPC 无法调用；在断网状态下打开全部样本，并用抓包或系统网络监视确认未允许远程内容时没有任何网络请求；验证结果写入同一份验收记录

## Workflow follow-up

- 评审通过后归档本 change（`/opsx:archive`），把六个规格同步到 `openspec/specs/`。
- 下一个 change：`add-cloud-pdf-export`（客户端导出流程和云端 API 契约），服务端在私有仓库中另起 change。
