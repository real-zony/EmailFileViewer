# 样本邮件库

测试和手工验收用的 EML / MSG 样本，场景来自 `openspec/changes/add-desktop-viewer/specs/` 里的规格（email-opening、email-viewing、email-attachments、app-appearance），下面按类别逐个对应。
样本只使用自己构造的，或明确允许再分发的（MIT）邮件，不含真实的私人邮件。

## 目录约定

| 位置 | 内容 | 规则 |
|------|------|------|
| `samples/*.eml`、`samples/*.msg` | 能正常解析的样本 | 必须全部通过 smoke：`dotnet run --project core/src/EmailFileViewer.Sidecar -- --smoke samples` 退出码为 0 |
| `samples/invalid/` | 故意无法解析、截断或根本不是邮件的文件 | smoke 不递归，不会读到这里；用于测试错误提示 |
| `samples/assets/` | 营销邮件里远程图片、样式表和追踪像素指向的文件，以及 `photo.jpg` | 地址统一是 `https://raw.githubusercontent.com/real-zony/EmailFileViewer/main/samples/assets/<文件>`，所以要合并到 `main` 之后这些地址才能真正打开 |
| `samples/generator/` | 样本生成器（独立的 csproj，不在 `core/EmailFileViewer.sln` 里） | 见下面"重新生成" |
| `samples/licenses/`、`samples/ATTRIBUTION.md` | 协议原文、上游样本的来源记录 | 新增上游样本必须在 `ATTRIBUTION.md` 登记来源 URL（固定到提交）、仓库和协议 |

文件名带类别前缀：`headers-`、`body-`、`charset-`、`cid-`、`marketing-`、`html-`、`link-`、`smime-`、`attachment(s)-`、`nested-`、`tnef-`、`format-`；没有前缀的（`simple-*.eml`、`Email*.msg` 等）是上游原名的样本。

`samples/.gitattributes` 把整个目录标成 `-text`：Windows 上的换行转换会改变 EML 的字节，破坏 S/MIME 签名，也会让附件的原始字节和预期值对不上。

## 重新生成

自己构造的样本由 `samples/generator/` 生成，在仓库根目录执行：

```bash
dotnet run --project samples/generator -- samples
```

参数是输出目录。生成器只覆盖自己负责的文件（上表里标为"生成"的样本、`assets/` 和 `invalid/`），不会删除上游样本。
生成完成后它会把写出的文件重新读一遍做自检（字符集解码结果、附件文件名、嵌套层数、winmail.dat 能解出 docx、S/MIME 签名结构和解密、`invalid/` 里的文件确实打不开等），任何一项不符都会报错并以非零码退出。

需要 .NET SDK（版本见根目录 `global.json`），依赖的库写在 `samples/generator/EmailFileViewer.SampleGenerator.csproj` 里：MimeKit、MsgKit（MIT）、MsgReader、OpenMcdf。

### 重新生成后的差异

重新运行后，下面这些文件的字节会变，其余文件与提交的版本逐字节一致：

| 文件 | 会变的原因 |
|------|-----------|
| `smime-signed.eml`、`smime-signed-opaque.eml`、`smime-encrypted.eml` | 每次运行现做一张自签名证书（RSA 2048），私钥只在内存里，不写盘、不提交；证书、签名时间和会话密钥每次都不同。内容（主题、正文、签名和加密的结构）不变 |
| `body-rtf-only.msg`、`body-html-in-rtf.msg`、`nested-msg-two-levels.msg` | MsgKit 保存时写入当前时间和随机的 EntryId / InstanceKey。主题、正文、附件不变 |
| `smime-msg-encrypted.msg`、`smime-msg-signed-opaque.msg`、`smime-msg-signed-clear.msg` | 同上，另外 smime.p7m 里的 CMS 数据用运行时现做的自签名证书生成（私钥不写盘），每次都不同 |
| `attachment-msg-file.eml`、`format-msg-disguised-as-eml.eml`、`invalid/truncated.msg` | 它们由 `body-rtf-only.msg` 派生，跟着一起变 |

multipart 边界（`----=_efv_01` 之类）、Message-Id、Content-Id、日期、PNG 和 docx/xlsx 都是固定值，不会变；换行固定为 CRLF，与运行平台无关。

## 样本清单

"来源"一列：**上游**表示取自 MimeKit / MsgReader 测试集（详见 `ATTRIBUTION.md`，内容未改动），**生成**表示由生成器构造。
场景引用写成"规格名 / 需求或场景名"。

### 邮件头与日期

| 文件 | 来源 | 内容和用途 | 对应的规格场景 |
|------|------|-----------|----------------|
| `headers-normal.eml` | 生成 | 发件人、两个收件人、一个抄送，名称和地址都有 | email-viewing / 显示邮件头 / 普通邮件 |
| `headers-timezone-bcc-replyto-no-cc.eml` | 生成 | `Date: Mon, 5 Oct 2026 09:00:00 -0700`，有 Bcc 和 Reply-To，**没有** Cc | email-viewing / 显示邮件头（密送、回复地址）；缺少某个邮件头；日期按本地时区显示 / 跨时区邮件（UTC+8 下应显示 2026-10-06 00:00） |
| `headers-rfc2047.eml` | 生成 | 主题和发件人名称里各有一个 `=?GB2312?B?` 和一个 `=?UTF-8?Q?` 编码字，中间夹着没有编码的 ASCII；第一个收件人名称是 UTF-8 的 Q 编码 | email-viewing / 字符集解码（RFC 2047 编码的邮件头） |
| `EmailWithMultilineSubject.eml` | 上游 | 主题跨多行折叠 | email-viewing / 显示邮件头 |
| `TestWithAttachmentsAndInlines.eml` | 上游 | Windows-1252 编码字的名称，发件人、收件人、抄送是同一个人 | email-viewing / 显示邮件头；字符集解码 |

### 正文格式

| 文件 | 来源 | 内容和用途 | 对应的规格场景 |
|------|------|-----------|----------------|
| `body-html-and-plain.eml` | 生成 | `multipart/alternative`，HTML 版和纯文本版的文字不同（`HTML-VERSION` / `PLAIN-TEXT-VERSION`），可以直接看出显示的是哪一个 | email-viewing / 正文格式选择 / 同时有 HTML 和纯文本 |
| `body-plain-only.eml` | 生成 | 只有纯文本：多段、连续两个空行、空格和制表符缩进、`>` 引用行、`<b>` 与 `&` 等需要转义的字符、中文 | email-viewing / 正文格式选择（纯文本保留换行） |
| `body-multipart-mixed-inline-images.eml` | 生成 | **Apple Mail 式的分段正文**：`multipart/alternative` 里的 HTML 备选是一个 `multipart/mixed`，里面交替放着三段 `text/html`（标记 `SEGMENT-ONE`、`SEGMENT-TWO`、`SEGMENT-THREE`，第二段含中文）和两张 `Content-Disposition: inline` 的图片（`photo.jpg`、`logo.png`，带 Content-ID 但 HTML 没有引用），最后是真正的附件 `itinerary.pdf`；纯文本备选只有 `PLAIN-ALTERNATIVE`。预期：正文按顺序是三段文字，图片以 `data:` 图片插在它们之间，附件列表只有 `itinerary.pdf`，纯文本备选不显示 | email-viewing / 正文格式选择（分段正文按顺序拼接）；email-attachments / 列出附件（夹在正文里的内嵌图片不列出） |
| `body-rtf-only.msg` | 生成 | **只有 RTF 正文**：没有 PR_BODY 纯文本流、没有 PR_HTML，RTF 不是 HTML 封装。含粗体、斜体、下划线、颜色、两个段落、项目符号、等宽字体行、超链接和 `\uN?` 转义的中文（`Chinese text: 中文段落。`，写成 `\u20013?\u25991?\u27573?\u33853?\u12290?`）；标记文字 `RTF-ONLY-BODY` 和 `END-OF-RTF-BODY` | email-viewing / 正文格式选择 / 只有 RTF 正文的 MSG；email-opening / 通过文件对话框打开（`.msg`） |
| `body-html-in-rtf.msg` | 生成 | **HTML 封装在 RTF 中**：HTML 正文只存在于 PR_RTF_COMPRESSED 里（带 `\fromhtml1`），没有 PR_HTML 流、没有纯文本流。HTML 有标题、粗体斜体、链接、中文、列表、表格，标记文字 `HTML-IN-RTF-BODY` 和 `END-OF-HTML-IN-RTF-BODY`。封装用的是 MsgKit 的简化形式（整段 HTML 放进一个 `\htmltag` 组），不同于 Outlook 的逐标签封装；Outlook 生成的形式见下面几个上游样本 | email-viewing / 正文格式选择（HTML 正文优先）；MSG 里 RTF 封装的 HTML 要还原成 HTML 显示 |
| `RtfSampleEmail.msg` | 上游 | 纯文本 + RTF 正文，没有 HTML（真实 Outlook 的 MSG 通常都带纯文本流，所以这个不是"只有 RTF"，用来测 RTF 优先于纯文本） | email-viewing / 正文格式选择（RTF 转换） |
| `HtmlSampleEmailWithAttachment.msg` | 上游 | HTML 封装在 RTF 中（`\fromhtml1`，没有 PR_HTML 流）+ 一个 PDF 附件 | email-viewing / 正文格式选择；email-attachments / 列出附件 |
| `EmailWithMsgAttachment.msg`、`EmailWithInnerMailAndAttachments.msg`、`RtfWithShortRussianString.msg` | 上游 | 同样是 HTML 封装在 RTF 中；`RtfWithShortRussianString.msg` 是 cp1251 俄文，主题为空，无发件人 | email-viewing / 正文格式选择；字符集解码 |
| `EmailWithAttachments.msg` | 上游 | 有 PR_HTML 正文（没有 RTF）；收件人、抄送、密送各一个；一张普通附件和一张按 Content-ID 内嵌的 jpg（HTML 里写的是 `src="tinkerbell.jpg"`，不是 `cid:`） | email-viewing / 显示邮件头（密送）、内嵌图片；email-attachments / 内嵌图片不重复列出（MSG 版） |
| `TxtSampleEmail.msg` | 上游 | 纯文本邮件，RTF 流是 `\fromtext`（Outlook 对纯文本邮件自动生成的封装） | email-viewing / 正文格式选择（不要把 `\fromtext` 的 RTF 当成富文本） |

### 字符集

预期的主题和正文由生成器自检，下表给出主题和关键文字，便于写测试断言。

| 文件 | 来源 | 声明方式 | 预期主题 | 预期的正文要点 |
|------|------|---------|----------|---------------|
| `charset-gbk.eml` | 生成 | `charset="gbk"`，Base64；主题和发件人名称是 GBK 的编码字 | `测试邮件：GBK 编码的中文主题`；发件人名称 `张三` | 正文以 `您好，` 开头，包含 `常用字：你好世界，邮件查看器，附件，发件人，收件人。` |
| `charset-gb18030.eml` | 生成 | `charset=gb18030`，8bit 原始字节 | `GB18030 样本：包含四字节字符` | 正文含四字节字符 `𠮷`（U+20BB7）、`𠀀`（U+20000）和 `€` |
| `charset-big5-undeclared.eml` | 生成 | **未声明字符集**：`Content-Type: text/plain` 没有 charset 参数，8bit 原始 Big5 字节；主题是声明了 Big5 的编码字 | `公司內部公告：專案進度報告` | 正文以 `各位同仁大家好：` 开头，是约 300 字的繁体中文公告，包含 `專案`、`週`、`準時` 等字。UTF.Unknown 对它的检测结果是 big5（置信度 0.99） |
| `charset-iso-2022-jp.eml` | 生成 | `charset=ISO-2022-JP`，7bit，带换码序列；主题和发件人名称是 ISO-2022-JP 的编码字 | `文字コードのテスト：ISO-2022-JP`；发件人名称 `山田太郎` | 正文以 `お世話になっております。` 开头 |
| `charset-shift-jis.eml` | 生成 | `charset=Shift_JIS`，Base64 | `文字コードのテスト：Shift_JIS` | 正文以 `お疲れ様です。` 开头 |
| `charset-iso-2022-jp-thunderbird.eml` | 上游 | Thunderbird 发出的真实 ISO-2022-JP，主题拆成两个编码字 | `日本語メールテスト (testing Japanese emails)` | 第一行是 `Let's see if both subject and body works fine...`，其后是日文四行 |
| `charset-koi8-r-raw-header.eml` | 上游 | 邮件头里直接写 KOI8-R 原始字节，没有 RFC 2047；正文声明 `koi8-r` | `Откуда он появился?`（按 KOI8-R 解读；MimeKit 默认会按 Latin-1 解成 `ïÔËÕÄÁ ÏÎ ÐÏÑ×ÉÌÓÑ?`，Core 参考正文声明的字符集重新解析邮件头） | 正文同上，换行结尾 |
| `charset-gbk-raw-header.eml` | 生成 | **8 位原始邮件头**：`From` 名称和 `Subject` 直接写 GBK 字节，没有 RFC 2047 编码字（老的中文邮件客户端常这样发信）；正文声明 `charset="gb2312"`，Base64 | `老邮件：邮件头直接写 GBK 字节`；发件人名称 `李四`（`li.si@example.com`） | 正文以 `您好，` 开头，第二段说明邮件头是原始 GBK 字节、需要参考正文声明的 gb2312 来解码 |
| `headers-rfc2047.eml` | 生成 | 见上一节 | `月度报告 Report （含附件 — résumé）`；发件人名称 `李 Li 明 (Ming)`；第一个收件人名称 `Müller 先生` | 正文是 UTF-8，列出了上面三个预期值 |

所属规格场景：email-viewing / 字符集解码（GBK 编码的中文邮件、未声明字符集），以及"字符集缺失或声明错误时尝试自动识别"。
两个 8 位原始邮件头样本（`charset-koi8-r-raw-header.eml`、`charset-gbk-raw-header.eml`）测的是邮件头本身没有声明字符集的情况。

### 内嵌图片与远程内容

| 文件 | 来源 | 内容和用途 | 对应的规格场景 |
|------|------|-----------|----------------|
| `cid-inline-image.eml` | 生成 | HTML 通过 `cid:logo@samples.efv.invalid` 和 `cid:signature@samples.efv.invalid` 引用两张 PNG（前者带 `Content-Disposition: inline`，后者只有 Content-ID）；另有一个普通附件 `quarterly-report.docx`。结构是 `mixed > alternative > [text, related > [html, 图, 图]]`，docx 在 `mixed` 下 | email-viewing / 内嵌图片 / 带签名图片的邮件；email-attachments / 列出附件 / 内嵌图片不重复列出（附件列表只应显示 docx） |
| `marketing-remote-content.eml` | 生成 | 营销邮件：600px 宽表格布局；7 张可见远程图片（logo、横幅、3 张商品图、2 个社交图标）；1 个追踪像素 `pixel.gif?campaign=...&uid=...`；`<body background>` 和 CSS `background-image` 的远程背景图；远程样式表 `marketing.css`（加载后标题变红）；远程字体（Google Fonts 的 `<link>`、`@import` 和 `@font-face`）；追踪链接 `utm_*`；退订链接；另有纯文本版 | email-viewing / 远程内容默认不加载（两个场景）；查看时不产生网络请求 / 离线打开邮件；正文链接在系统浏览器中打开 |
| `TestWithAttachmentsAndInlines.eml` | 上游 | 一张 `cid:` 内嵌 PNG（`Outlook-facebook.png`）+ 4 个文本附件 + 2 个 `message/rfc822` 附件 | email-viewing / 内嵌图片；email-attachments / 内嵌图片不重复列出、打开嵌套邮件 |

远程资源里，指向 `raw.githubusercontent.com/real-zony/EmailFileViewer/main/samples/assets/` 的文件都在 `assets/`；`fonts.googleapis.com` 和 `fonts.gstatic.com` 的地址是第三方字体的典型写法，其中 woff2 路径是虚构的，只用来被阻止。

### 沙箱、链接与安全

| 文件 | 来源 | 内容和用途 | 对应的规格场景 |
|------|------|-----------|----------------|
| `html-malicious.eml` | 生成 | 恶意 HTML 探针。包含 `<script>`；`onerror`、`onload`（body 和 svg）、`onclick`、`onmouseover`、`onfocus` 事件属性；`javascript:` 链接（含大小写混写）；`<form>`；`<meta http-equiv="refresh">`；`<iframe>`（含 `srcdoc`）；`<object>`、`<embed>`；`<base>`；远程 `<link rel=stylesheet>`；脚本里尝试调用 `window.__TAURI_INTERNALS__`、`window.__TAURI__` 和 `window.parent.postMessage`。**所有探测只调用不存在的命令 `efv_probe_should_fail`**，目标地址都是 `efv-probe.invalid`，不做任何破坏性操作。页面里有三处可见标记：`MARKER-START`、`NO-SCRIPT-RAN`（脚本若执行会变成 `SCRIPT-RAN: ...`）、`MARKER-END`；另含一个应保留的普通 https 链接 | email-viewing / 正文在沙箱中渲染（正文包含脚本；正文尝试调用应用接口）；正文链接在系统浏览器中打开 |
| `link-text-mismatch.eml` | 生成 | 四个链接：显示文字 `paypal.com` 实际指向 `https://example.com/not-paypal`；显示 URL 文字但主机不同；`title` 属性伪造成 paypal 地址；一个文字与地址一致的对照 | email-viewing / 正文链接在系统浏览器中打开 / 链接文字与实际地址不符（悬停应显示实际地址） |
| `links-anchors-and-schemes.eml` | 生成 | **链接验收样本**，HTML 正文（另有一句话的纯文本备选）。顶部是目录，下面是 5 个很长的段落（`id` 依次为 `section-1`、`section-2`、`section-3`、`section-4`、`bottom`，每段至少 420px 高，页内锚点能看出滚动效果；最后一段是 `bottom`，里面有回到顶部的 `href="#"`）。含以下链接，括号里是应用里的**预期行为**：<br>① 页内锚点 `#section-1`～`#section-4`、`#`、`#bottom`（这一条带 `target="_blank"`）、SVG 里的 `<a xlink:href="#section-2">`：**在正文里滚动**，不打开新窗口，正文不消失；<br>② `https://example.com/page`、`http://example.com/plain`、`mailto:someone@example.com`，以及 SVG 里的 `<a href="https://example.com/svg">` 和图像映射 `<area href="https://example.com/area">`：**交给系统默认程序打开**（浏览器、邮件客户端），正文保持不变；<br>③ `tel:+10000000000`、`file:///etc/hosts`、相对链接 `page.html`、`?x=1`、`/root`、空链接 `href=""`：**点击后没有任何反应**，正文不消失（`file:` 在清理时就被去掉，空 `href` 也被去掉，其余由应用拦截） | email-viewing / 正文链接在系统浏览器中打开（页内锚点、各种协议的链接、相对链接和空链接） |
| `smime-signed.eml` | 生成 | `multipart/signed`，`protocol="application/pkcs7-signature"`，SHA-256，签名文件 `smime.p7s`。证书是运行时现做的自签名证书，私钥不提交；应用不验证签名 | email-viewing / 加密与签名邮件提示 / 签名邮件 |
| `smime-signed-opaque.eml` | 生成 | **EML 版不透明签名**：邮件头明文，顶层只有一个 `application/pkcs7-mime; smime-type=signed-data; name=smime.p7m`，被签名的内容（`multipart/mixed`：HTML 与纯文本两个备选，标记 `OPAQUE-SIGNED-HTML` / `OPAQUE-SIGNED-PLAIN`，外加附件 `signed-report.pdf`）包在 CMS 里。证书是运行时现做的自签名证书，私钥不提交；应用只解码 CMS 取出内容，不验证签名 | email-viewing / 加密与签名邮件提示 / 签名邮件（不透明签名，正文和附件照常显示）；查看时不产生网络请求 |
| `smime-signed-thunderbird.eml` | 上游 | Thunderbird 24 签名的真实邮件，`multipart/signed`（SHA-1），正文 + 一张 jpg 附件 + `smime.p7s` | 同上 |
| `smime-encrypted.eml` | 生成 | `application/pkcs7-mime; smime-type=enveloped-data; name=smime.p7m`，邮件头（发件人、收件人、主题、日期）是明文，正文只在加密信封里，解密后以 `SECRET-BODY` 开头 | email-viewing / 加密与签名邮件提示 / 加密邮件 |
| `smime-msg-encrypted.msg` | 生成 | **MSG 版加密邮件**：消息类 `IPM.Note.SMIME`，内容在名为 `smime.p7m` 的附件里（CMS enveloped-data，正文 `SECRET-BODY…`） | 同上（MSG） |
| `smime-msg-signed-opaque.msg` | 生成 | **MSG 版不透明签名**：消息类 `IPM.Note.SMIME`，`smime.p7m` 是 CMS signed-data，被签名的内容（`multipart/mixed`：纯文本正文 `OPAQUE-SIGNED-BODY…` 加文本附件 `opaque-note.txt`）包在签名里面 | email-viewing / 加密与签名邮件提示 / 签名邮件（MSG）；应用解码 CMS 取出内容，正文和附件照常显示，不验证签名 |
| `smime-msg-signed-clear.msg` | 生成 | **MSG 版明文签名**：消息类 `IPM.Note.SMIME.MultipartSigned`，`smime.p7m` 里是完整的 multipart/signed 邮件，正文 `CLEAR-SIGNED-BODY…` 可读 | 同上；应用取 smime.p7m 里邮件的正文 |

三个 `smime-msg-*.msg` 是 MsgKit 先按普通邮件保存、再用 OpenMcdf 改写消息类得到的。**不要用 MsgReader 直接打开它们**：MsgReader 对这类消息会验证证书链，可能联网；Core 在交给 MsgReader 之前先在内存副本里把消息类改回 `IPM.Note`。

### 外观（深色模式下的邮件正文）

| 文件 | 来源 | 对应的规格场景 |
|------|------|----------------|
| `marketing-remote-content.eml` | 生成 | app-appearance / 外观不改变邮件内容 / 深色模式下查看 HTML 邮件（浅色底的营销邮件，自带背景色） |
| `body-html-and-plain.eml`、`HtmlSampleEmailWithAttachment.msg` | 生成 / 上游 | 同上（没有自带背景色的 HTML 正文，应显示在浅色底上） |
| `body-plain-only.eml`、`TxtSampleEmail.msg` | 生成 / 上游 | app-appearance / 外观不改变邮件内容 / 深色模式下查看纯文本邮件 |

### 附件

| 文件 | 来源 | 内容和用途 | 对应的规格场景 |
|------|------|-----------|----------------|
| `attachments-pdf-xlsx-jpg.eml` | 生成 | 三个附件：`report.pdf`、`budget.xlsx`、`photo.jpg`，都是有效文件（PDF 能打开，xlsx 有两列三行，jpg 是 180x60 的图） | email-attachments / 列出附件 / 带两个附件的邮件；保存单个附件 / 保存 PDF 附件；用系统默认程序打开附件 / 打开图片附件 |
| `attachments-duplicate-names.eml` | 生成 | 两个同名的 `report.pdf`，内容不同（`Report A`、`Report B`），用来确认保存时不会互相覆盖 | email-attachments / 保存全部附件 / 目标文件夹已有同名文件 |
| `attachments-unsafe-names.eml` | 生成 | 7 个文件名：`../../.bashrc`、`..\..\Windows\evil.txt`、`/tmp/absolute-path.txt`、`CON.txt`、`NUL`、`a:b*c?.txt`、`x<y>z|w.txt`。内容都是无害的占位文字 | email-attachments / 附件文件名的安全处理 / 文件名包含路径穿越 |
| `attachments-executables.eml` | 生成 | 6 个可执行和脚本类附件：`installer.exe`、`run-me.bat`、`setup.ps1`、`install.sh`、`launcher.command`、`Tool.app`。**内容只是纯文本占位，不是真实的可执行代码**（`.app` 在 macOS 上是目录，这里只能用文件名模拟） | email-attachments / 用系统默认程序打开附件 / 打开可执行附件 |
| `simple-multipart.eml` | 上游 | 带 1 个附件 | email-attachments / 列出附件 |

### 嵌套邮件与 winmail.dat

| 文件 | 来源 | 内容和用途 | 对应的规格场景 |
|------|------|-----------|----------------|
| `nested-rfc822-two-levels.eml` | 生成 | **两层** `message/rfc822`：外层 → 中层 → 内层，每层主题和正文不同（`Level 1/2/3`），最内层还带一个 `level3-note.txt` 附件 | email-attachments / 打开嵌套邮件 / 多层嵌套 |
| `simple-embedded-message.eml` | 上游 | 一层 `message/rfc822` | email-attachments / 打开嵌套邮件 |
| `attachment-eml-file.eml` | 生成 | 作为附件的 `.eml`：`forwarded-as-file.eml`（`application/octet-stream`，普通文件）和 `forwarded-as-rfc822.eml`（`message/rfc822`，`Content-Disposition: attachment`） | email-attachments / 打开嵌套邮件（作为附件的 `.eml` 文件） |
| `attachment-msg-file.eml` | 生成 | 作为附件的 `.msg`：`forwarded-message.msg`（`application/vnd.ms-outlook`，内容是 `body-rtf-only.msg`） | email-attachments / 打开嵌套邮件（作为附件的 `.msg` 文件） |
| `nested-msg-two-levels.msg` | 生成 | **两层**嵌入 MSG：外层 → 中层 → 内层（`Level 1/2/3`），最内层带 `level3-note.txt`。附件是真正的嵌入邮件（PR_ATTACH_METHOD = 5，`__substg1.0_3701000D` 存储），不是按值保存的 `.msg` 文件。MsgKit 没有嵌入邮件的 API，生成器用 OpenMcdf 改写复合文档，布局对照了 Outlook 生成的 `EmailWithMsgAttachment.msg` | email-attachments / 打开嵌套邮件 / MSG 中附带另一封 MSG；多层嵌套 |
| `EmailWithMsgAttachment.msg` | 上游 | MSG 内嵌一层 MSG | email-attachments / 打开嵌套邮件 / MSG 中附带另一封 MSG |
| `EmailWithInnerMailAndAttachments.msg` | 上游 | 外层带 2 个 PDF + 一封内层 MSG（内层也带 2 个 PDF） | 同上 |
| `tnef-winmail-docx.eml` | 生成 | 附件只有 `winmail.dat`（`application/ms-tnef`），里面封装了一个 `meeting-minutes.docx`（有效的最小 docx）。TNEF 由生成器按 MS-OXTNEF 写出，带 TNEF 版本、代码页、消息类、主题、正文、附件的 renddata / 文件名 / 数据 / MAPI 属性（文件名、MIME 类型、扩展名）；已确认 MimeKit 能解出这个 docx 且字节一致 | email-attachments / 展开 winmail.dat / Outlook 发出的 winmail.dat |

### 打开方式与格式识别

| 文件 | 来源 | 内容和用途 | 对应的规格场景 |
|------|------|-----------|----------------|
| `format-msg-disguised-as-eml.eml` | 生成 | 扩展名是 `.eml`，内容是 MSG（`body-rtf-only.msg` 的副本） | email-opening / 按内容识别文件格式 / 扩展名与内容不符 |
| `format-eml-disguised-as-msg.msg` | 生成 | 反过来：扩展名是 `.msg`，内容是 EML（`body-plain-only.eml` 的副本） | 同上 |
| 根目录任意几个样本 | — | 双击、拖放、命令行传参、多选 | email-opening / 通过文件对话框、拖放、系统文件关联打开；单实例转发；一次打开多个文件 |

### 无法解析的文件（`invalid/`）

smoke 不递归，不会读到这里。

| 文件 | 内容和用途 | 解析结果（MimeKit 4.18.1 / MsgReader 6.2.0） | 对应的规格场景 |
|------|-----------|----------------------------------------------|----------------|
| `truncated.msg` | `body-rtf-only.msg` 截成前一半，OLE 签名还在，复合文档的目录和扇区表被截掉 | MsgReader 抛 `EndOfStreamException` | email-opening / 无法识别或损坏的文件 / 解析过程中崩溃；一次打开多个文件（和两个正常文件一起拖入） |
| `truncated-half.eml` | `attachments-pdf-xlsx-jpg.eml` 截成前一半，断在第二个附件（xlsx）的 Base64 数据中间，没有结束边界 | **MimeKit 不会抛异常**：能解出头部、完整的第一个附件和被截断的第二个附件（xlsx）。smoke 的结果是成功。它测的是"解析器容忍截断，应用不要崩"；要测错误提示请用 `truncated.msg` | 同上 |
| `not-an-email.png` | 一张 PNG | 按内容判断不是 OLE，按 MIME 解析会抛 `FormatException`（Failed to parse message headers） | email-opening / 无法识别或损坏的文件 / 打开一张图片 |
| `png-disguised-as-eml.eml` | 同一张 PNG，但扩展名是 `.eml`，用来确认应用按内容而不是扩展名判断 | `FormatException` | 同上 |
| `empty.eml` | 0 字节的空文件 | `FormatException`（End of stream） | 同上 |

Core 对这几个文件的判断（`EmailParseException.Kind`）：`not-an-email.png`、`png-disguised-as-eml.eml`（开头有 NUL 字节）和 `empty.eml`（空文件）是"不支持的格式"，`truncated.msg` 是"文件已损坏"，`truncated-half.eml` 能解析（只丢了截断部分）。文件不存在、没有读权限、路径是目录是"无法读取"。

### 素材（`assets/`）

全部由生成器写出，PNG 是纯色块图案，体积很小。

| 文件 | 说明 |
|------|------|
| `logo.png` | 120x40 PNG。营销邮件顶部的远程 logo；`cid-inline-image.eml` 里的 cid 内嵌 logo 和 `invalid/not-an-email.png` 是它的副本 |
| `hero.png` | 300x100 PNG。营销邮件的横幅（显示宽度 600），同时被 `<body background>` 和 CSS `background-image` 引用 |
| `product-1.png`、`product-2.png`、`product-3.png` | 90x90 PNG，营销邮件里的三张商品图 |
| `social-1.png`、`social-2.png` | 24x24 PNG，营销邮件页脚的社交图标；`social-1.png` 也是 `cid-inline-image.eml` 里的签名图 |
| `pixel.gif` | 1x1 透明 GIF，营销邮件末尾的追踪像素 |
| `marketing.css` | 营销邮件引用的远程样式表，加载后标题会变红 |
| `photo.jpg` | 180x60 JPEG。邮件不引用它，`attachments-pdf-xlsx-jpg.eml` 的 `photo.jpg` 附件是它的副本，方便对照"保存的内容与原始字节一致" |

### 其他上游样本

| 文件 | 来源 | 用途 |
|------|------|------|
| `RtfSampleEmail.msg`、`HtmlSampleEmailWithAttachment.msg`、`EmailWithMsgAttachment.msg` | 上游 | 见上文各节；这三个是任务 1.2 引入的，已有测试引用，不要改名或删除 |
| `simple-multipart.eml`、`EmailWithMultilineSubject.eml` | 上游 | 同上，已有测试引用 |

## 对解析核心的提示

写样本和跑 smoke 时发现的行为，以及 Core 现在的处理方式：

- MimeKit 的 TNEF 解析（`TnefPart`）和 MsgReader 解析 MSG 都需要先注册代码页编码提供程序（`CodePagesEncodingProvider`），否则两者都会抛 `TypeInitializationException`。**现在由 Core 在解析入口自己注册**，调用方（sidecar、测试）不需要也不应该再注册。
- `TnefPart.ExtractAttachments()` 除了文件，还会把 TNEF 里的正文作为没有文件名的文本部分一并返回。Core 只取有文件名的成员，winmail.dat 本身不列出。
- MimeKit 的 `MimeMessage.Attachments` 不包含没有 `Content-Disposition` 的 `message/rfc822` 部分。Core 自己遍历 MIME 结构，这类部分、`Content-Disposition: attachment` 的 rfc822、作为附件的 `.eml` / `.msg` 文件都列为嵌套邮件。
- 签名邮件的签名文件 `smime.p7s`（`multipart/signed` 的第二个成员）和加密邮件的信封 `smime.p7m` 不是用户附件，Core 不列出。
- 不透明签名（`application/pkcs7-mime; smime-type=signed-data`，EML 和 MSG 都有）：Core 用 `SignedCms.Decode` 取出被签名的 MIME 实体，当作邮件内容解析正文和附件。**只解码，不调用 `CheckSignature`，不读证书、不建证书链**，所以没有网络请求，也不知道签名是否有效（界面注明"未验证"）。
- Apple Mail 这类客户端会在 `multipart/mixed` 里交替放多个正文部分和内嵌图片。Core 按顺序拼接这些非附件的文本部分（HTML 取 `<body>` 内的内容，纯文本先转成 HTML），夹在中间的 `image/*`（inline 或没有声明为附件）以 `data:` 图片插入对应位置，不列为附件；拼好的 HTML 照样经过清理。首尾没有被文本夹住的图片仍作为附件列出。
- 邮件头里直接写 8 位字节（没有 RFC 2047）而且不是 UTF-8 时，MimeKit 会按 ISO-8859-1 解出乱码。Core 检测到这种情况就用正文声明的字符集（没有就根据邮件头的字节识别）设为 `ParserOptions.CharsetEncoding` 重新解析。
- 文件开头有空行（或 UTF-8 BOM）时，MimeKit 会把整封邮件当成正文；Core 先跳过它们再交给 MimeKit。
- MSG 的 S/MIME 邮件见上面的 `smime-msg-*.msg`。

### 附件列表的预期结果

Core 列出的附件（Id 是不透明字符串，这里只列文件名）：

| 文件 | 附件 |
|------|------|
| `attachments-pdf-xlsx-jpg.eml` | `report.pdf`、`budget.xlsx`、`photo.jpg` |
| `attachments-duplicate-names.eml` | 两个 `report.pdf`（内容不同） |
| `attachments-unsafe-names.eml` | 7 个原样的文件名（清理发生在保存时） |
| `attachments-executables.eml` | 6 个可执行和脚本附件 |
| `cid-inline-image.eml` | 只有 `quarterly-report.docx`（两张 cid 图片内嵌，不列出） |
| `TestWithAttachmentsAndInlines.eml` | 4 个文本附件、2 个声明为 `message/rfc822` 的部分（内容其实不是邮件，打开会报"不支持的格式"）；cid 的 PNG 不列出 |
| `nested-rfc822-two-levels.eml` | 一个嵌套邮件（中层），中层里一个嵌套邮件（内层），内层有 `level3-note.txt` |
| `simple-embedded-message.eml` | 一个嵌套邮件 |
| `attachment-eml-file.eml` | `forwarded-as-file.eml`、`forwarded-as-rfc822.eml`，都是嵌套邮件 |
| `attachment-msg-file.eml` | `forwarded-message.msg`，嵌套邮件 |
| `tnef-winmail-docx.eml` | 只有 `meeting-minutes.docx` |
| `smime-signed.eml`、`smime-encrypted.eml`、`smime-msg-encrypted.msg`、`smime-msg-signed-clear.msg` | 没有附件 |
| `smime-signed-opaque.eml` | 只有 `signed-report.pdf`（在签名数据里面，`smime.p7m` 本身不列出） |
| `smime-msg-signed-opaque.msg` | 只有 `opaque-note.txt`（在签名数据里面，`smime.p7m` 本身不列出） |
| `body-multipart-mixed-inline-images.eml` | 只有 `itinerary.pdf`（夹在三段正文之间的两张 inline 图片拼进正文，不列出） |
| `smime-signed-thunderbird.eml` | 只有 `signed.jpg` |
| `EmailWithAttachments.msg` | 只有 `peterpan.jpg`（`tinkerbell.jpg` 是被 HTML 按文件名引用的隐藏图片，内嵌，不列出） |
| `HtmlSampleEmailWithAttachment.msg` | `SampleAttachment.pdf` |
| `EmailWithMsgAttachment.msg` | 一个嵌套 MSG |
| `EmailWithInnerMailAndAttachments.msg` | `OUTER 1.pdf`、`OUTER 2.pdf`、一个嵌套 MSG（里面有 `INNER 1.pdf`、`INNER 2.pdf`） |
| `nested-msg-two-levels.msg` | 一个嵌套 MSG（中层），中层里一个嵌套 MSG（内层），内层有 `level3-note.txt` |

## 缺口与局限

必须覆盖的类别都有对应的文件，没有完全缺失的类别。下面几项是样本本身的局限：

1. **没有 Outlook 真实导出的、带完整 MAPI 属性的 winmail.dat。** `tnef-winmail-docx.eml` 里的 TNEF 是生成器按规范写的最小实现，MimeKit 能解析，但没有用真实 Outlook 验证。MimeKit 测试集里的 `.tnef` 文件是裸数据流且含真实姓名，MSGReader 里没有 winmail.dat，所以没有采用（详见 `ATTRIBUTION.md`）。
2. **`body-rtf-only.msg`、`body-html-in-rtf.msg` 和 `nested-msg-two-levels.msg` 没有用真实 Outlook 打开验证。** 它们由 MsgKit 生成，嵌套邮件还经过手工改写复合文档，已通过 MsgReader 读回验证，并对照了 Outlook 生成的上游样本的属性布局。`body-html-in-rtf.msg` 的 RTF 封装比 Outlook 的简单，Outlook 形式的 HTML 封装请用上游的 `HtmlSampleEmailWithAttachment.msg` 等。
3. **三个 `smime-msg-*.msg` 没有用真实 Outlook 打开验证**，它们按 MS-OXMSG 和 MsgReader 对这类消息的读取逻辑构造（消息类 + smime.p7m 附件）。`smime-signed-opaque.eml` 和 `smime-msg-signed-opaque.msg` 的签名数据由 MimeKit（BouncyCastle）生成，用 `System.Security.Cryptography.Pkcs` 读回验证过；没有用 Outlook、Thunderbird、Apple Mail 等客户端生成的不透明签名邮件验证。
4. **`Tool.app` 只是文件名模拟。** `.app` 在 macOS 上是目录包，邮件里的附件只能是一个文件。
5. **远程资源的地址要合并到 `main` 之后才能真正加载。** 在那之前"用户选择加载"后图片会是裂图，属于预期。
