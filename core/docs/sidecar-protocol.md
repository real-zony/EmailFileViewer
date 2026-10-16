# Sidecar 通信协议

Tauri 外壳（Rust）通过 stdio 与 sidecar（`EmailFileViewer.Sidecar`，.NET NativeAOT）通信，协议是 **每行一个 JSON** 的请求/响应。
本文是协议的完整说明，也是 Rust 端（任务 5.3）的实现依据。文中的 JSON 示例由测试（`ProtocolDocumentTests`）逐个解析成协议 DTO 并核对，文档与实现不一致时测试会失败。

当前协议版本：**1**（`system.ping` 返回）。

## 1. 启动与退出

```text
EmailFileViewer.Sidecar --stdio
```

- **stdout 只输出协议消息**，每条一行，UTF-8 无 BOM，行尾固定是 `\n`（Windows 上也是，不是 `\r\n`，见“9. 平台差异”）。诊断日志一律写 **stderr**（UTF-8，只在发生未预期的错误时才写）。
- **就绪判断**：进程启动后发送 `system.ping`，收到 `{"protocolVersion":1}` 即表示 sidecar 已就绪。进程启动本身不输出任何内容。
- **退出**：关闭 sidecar 的 stdin（EOF）。sidecar 会先处理完已经收到的请求并写出响应，然后关闭所有文档、删除本次会话的临时目录，以退出码 0 退出。
- 收到 SIGINT（Ctrl+C）或 SIGTERM 时也走同样的清理，退出码 130。
- stdout 被对方关闭（写入时 IO 错误）时停止处理，退出码 1。
- `--smoke <目录>` 命令仍然可用（解析目录里的全部邮件，用于 CI 验证发布产物），与 `--stdio` 无关。

## 2. 消息格式

三种消息，都是单行 JSON 对象，字段名 camelCase：

请求（Rust → sidecar）：

```json request
{"id": 1, "method": "system.ping"}
```

成功响应（sidecar → Rust）：

```json response system.ping
{"id": 1, "result": {"protocolVersion": 1}}
```

失败响应：

```json error
{"id": 7, "error": {"code": "document_not_found", "message": "文档 \"d9\" 不存在，可能没有打开过或者已经关闭。"}}
```

规则：

- `id`：**整数或字符串**，由调用方分配。响应原样带回同一个 `id`（类型也不变），调用方靠它把响应对应到请求。`id` 在调用方这一侧必须唯一（同时进行中的请求之间）。
- `method`：方法名，非空字符串。
- `params`：对象。没有必填字段的方法（`system.ping`）可以省略它，或者写 `null`、`{}`。多余的字段会被忽略。
- 响应里 `result` 和 `error` 有且只有一个。
- 编码：中文等非 ASCII 字符**原样输出**，不会转义成 `\uXXXX`；控制字符和换行仍然会被转义，所以一条消息永远只占一行。`message` 里的 HTML 字符（`<`、`>`、`&`）也不转义。协议里的 JSON 不会被嵌进 HTML。
- `error.code` 是给程序判断的稳定字符串；`error.message` 是给开发者看的说明（中文），**不要拿它做判断，也不要原样显示给用户**。
- 响应的先后顺序**不保证**和请求一致（见“并发”），只能按 `id` 对应。

### 无法处理的行

sidecar 读到的行不是合法请求时（不是 JSON、不是对象、`id` 缺失或类型不对），响应里的 `id` 是 `null`，错误码是 `invalid_request`：

```text
this is not json
```

```json error
{"id": null, "error": {"code": "invalid_request", "message": "这一行不是合法的 JSON。"}}
```

`id` 有效但 `method` 缺失或不是非空字符串时，响应带上这个 `id`，错误码同样是 `invalid_request`。空行被忽略，不产生响应。
无论哪种错误，sidecar 都继续处理后面的请求。

方法不存在：

```json request
{"id": 8, "method": "no.such.method"}
```

```json error
{"id": 8, "error": {"code": "method_not_found", "message": "没有方法 \"no.such.method\"。"}}
```

`params` 缺少必填字段、类型不对、或者路径不是绝对路径时返回 `invalid_params`：

```json request
{"id": 9, "method": "email.open", "params": {}}
```

```json error
{"id": 9, "error": {"code": "invalid_params", "message": "params.path 缺失，或者不是非空字符串。"}}
```

## 3. 错误码

| 错误码 | 含义 | 常见于 |
|--------|------|--------|
| `unsupported_format` | 文件不是邮件（图片、空文件、其他类型的 OLE 文档，或声明为邮件的附件内容不是邮件） | `email.open`、`email.openNested` |
| `corrupted` | 文件已损坏，格式看起来是邮件但无法解析（例如被截断的 MSG） | `email.open`、`email.openNested` |
| `unreadable` | 文件无法读取：不存在、是目录、没有读取权限、发生 IO 错误 | `email.open`、`email.openNested` |
| `document_not_found` | 没有这个 `docId` 的文档：没打开过，或已经关闭 | 所有带 `docId` 的方法 |
| `attachment_not_found` | 文档里没有这个 `attachmentId` 的附件 | `email.openNested`、`attachment.save`、`attachment.extractForOpen` |
| `not_nested_email` | 附件不是邮件（`isNestedEmail` 为 `false`），不能作为嵌套邮件打开 | `email.openNested` |
| `executable_refused` | 附件是可执行文件或脚本，只能保存，不能提取出来用系统程序打开 | `attachment.extractForOpen` |
| `write_failed` | 无法写入目标位置：文件夹不存在、没有权限、磁盘已满、路径不合法、临时目录不可用等 | `attachment.save`、`attachment.saveAll`、`attachment.extractForOpen` |
| `invalid_params` | `params` 缺少必填字段，或字段的类型、取值不对（包括需要绝对路径却给了相对路径） | 所有方法 |
| `invalid_request` | 这一行不是合法的请求：不是 JSON、不是对象、`id` 缺失或类型不对、`method` 缺失 | 任意一行 |
| `method_not_found` | 没有这个方法 | 任意请求 |
| `internal_error` | 处理请求时发生了未预期的错误（bug）。只影响这一个请求，sidecar 继续运行；细节写在 stderr | 任意请求 |

前三个与 Core 的 `EmailParseException` 的三类错误一一对应（`UnsupportedFormat`、`Corrupted`、`Unreadable`）。
`attachment.saveAll` 的 `failed` 列表里的 `code` 只会是 `write_failed` 或 `internal_error`。

## 4. EmailDocument

`email.open` 和 `email.openNested` 的 `document` 字段，即 Core 的 `EmailDocument`。缺失的字段用空数组或 `null` 表示，不用空字符串占位。

| 字段 | 类型 | 说明 |
|------|------|------|
| `from`、`to`、`cc`、`bcc`、`replyTo` | `EmailAddress[]` | `EmailAddress` 是 `{ "name": string \| null, "address": string \| null }`，两项至少有一项不是 `null` |
| `subject` | string \| null | 主题 |
| `sentAtUtc` | string \| null | 发送时间，统一换算成 UTC，带 `Z` 的 ISO 8601（例如 `2026-10-01T02:00:00Z`）；没有可解析的日期时为 `null` |
| `dateHeaderRaw` | string \| null | 原始 `Date` 邮件头，界面悬停时用它显示邮件原本的时区；MSG 通常没有 |
| `body` | `{ "sourceFormat": string, "html": string \| null }` | `sourceFormat` 是 `None`、`Html`、`Rtf`、`PlainText` 之一，表示正文来自哪种格式；`html` 是**已经清理过**的 HTML（`cid:` 图片已换成 `data:` URI），没有正文时为 `null` |
| `attachments` | `EmailAttachment[]` | 附件列表，不含已内嵌进正文的图片 |
| `isSigned` | boolean | 带有 S/MIME 数字签名；**签名是否有效不验证**，界面要注明“未验证” |
| `isEncrypted` | boolean | S/MIME 加密，无法显示正文（`body.sourceFormat` 为 `None`） |
| `hasRemoteContent` | boolean | 正文引用了远程资源（图片、样式表、字体） |

`EmailAttachment`：`{ "id": string, "fileName": string \| null, "size": number, "mimeType": string \| null, "isNestedEmail": boolean }`。
`id` 只在所属文档内有意义，是不透明字符串，同一个文件每次解析得到的 `id` 都一样。`size` 是字节数。

## 5. 方法

### `system.ping`

确认 sidecar 已就绪。

- 参数：无
- 结果：`{ "protocolVersion": number }`

```json request
{"id": 1, "method": "system.ping"}
```

```json response system.ping
{"id": 1, "result": {"protocolVersion": 1}}
```

### `email.open`

打开一个 EML 或 MSG 文件（按内容判断格式，不看扩展名），解析后缓存在 sidecar 里。

- 参数：`path`（string，必填）邮件文件的路径
- 结果：`{ "docId": string, "document": EmailDocument }`
- 错误：`unsupported_format`、`corrupted`、`unreadable`、`invalid_params`

文件读完就不再占用。同一个文件可以打开多次，每次得到不同的 `docId`，互相独立。

```json request
{"id": 2, "method": "email.open", "params": {"path": "/Users/alice/Mail/quarterly-report.eml"}}
```

```json response email.open
{
  "id": 2,
  "result": {
    "docId": "d1",
    "document": {
      "from": [{"name": "Alice Zhang", "address": "alice.zhang@example.com"}],
      "to": [{"name": "Bob Li", "address": "bob.li@example.org"}],
      "cc": [],
      "bcc": [],
      "replyTo": [],
      "subject": "季度报告 Quarterly report",
      "sentAtUtc": "2026-10-01T02:00:00Z",
      "dateHeaderRaw": "Thu, 01 Oct 2026 10:00:00 +0800",
      "body": {"sourceFormat": "PlainText", "html": "<pre style=\"margin:0;white-space:pre-wrap;overflow-wrap:anywhere;\">您好，报告见附件。\n</pre>"},
      "attachments": [
        {"id": "2", "fileName": "report.pdf", "size": 48213, "mimeType": "application/pdf", "isNestedEmail": false},
        {"id": "3", "fileName": "forwarded.eml", "size": 2210, "mimeType": "message/rfc822", "isNestedEmail": true}
      ],
      "isSigned": false,
      "isEncrypted": false,
      "hasRemoteContent": false
    }
  }
}
```

（上面的示例为了方便阅读做了缩进和换行，实际传输时每条消息都在一行内。）

文件不是邮件时：

```json request
{"id": 3, "method": "email.open", "params": {"path": "/Users/alice/Pictures/photo.png"}}
```

```json error
{"id": 3, "error": {"code": "unsupported_format", "message": "文件不是邮件。"}}
```

### `email.openNested`

打开一个嵌套邮件附件（`isNestedEmail` 为 `true`：MSG 里的 MSG、EML 里的 `message/rfc822`、作为附件的 `.eml` / `.msg` 文件）。

- 参数：`docId`（string，必填）外层文档；`attachmentId`（string，必填）嵌套邮件附件的 `id`
- 结果：`{ "docId": string, "document": EmailDocument }`，`docId` 是**新分配的**
- 错误：`document_not_found`、`attachment_not_found`、`not_nested_email`、`unsupported_format`、`corrupted`、`unreadable`（附件声明为邮件但内容不是有效邮件时）、`invalid_params`

嵌套邮件是独立的文档，有自己的 `docId`，和外层互不影响：关闭外层，嵌套邮件仍然可用；各自用 `document.close` 关闭。可以继续对嵌套邮件调用 `email.openNested`，层数不限。

```json request
{"id": 4, "method": "email.openNested", "params": {"docId": "d1", "attachmentId": "3"}}
```

```json response email.openNested
{
  "id": 4,
  "result": {
    "docId": "d2",
    "document": {
      "from": [{"name": "Erin Xu", "address": "erin.xu@example.org"}],
      "to": [{"name": "Bob Li", "address": "bob.li@example.org"}],
      "cc": [],
      "bcc": [],
      "replyTo": [],
      "subject": "Original message",
      "sentAtUtc": null,
      "dateHeaderRaw": null,
      "body": {"sourceFormat": "Html", "html": "<p>Forwarded as an attachment.</p>"},
      "attachments": [],
      "isSigned": false,
      "isEncrypted": false,
      "hasRemoteContent": false
    }
  }
}
```

附件不是邮件时：

```json request
{"id": 5, "method": "email.openNested", "params": {"docId": "d1", "attachmentId": "2"}}
```

```json error
{"id": 5, "error": {"code": "not_nested_email", "message": "Id 为 \"2\" 的附件不是邮件，不能作为嵌套邮件打开。"}}
```

### `document.close`

关闭文档，释放解析结果。

- 参数：`docId`（string，必填）
- 结果：`{}`
- 错误：`document_not_found`、`invalid_params`

关闭之后，对这个 `docId` 的任何访问（包括再次 `document.close`）都返回 `document_not_found`；调用方可以把关闭时的 `document_not_found` 当作成功。
关闭**不会**删除这个文档已经提取到临时目录的文件（外部程序可能还在使用），它们留到会话结束时清理。

```json request
{"id": 6, "method": "document.close", "params": {"docId": "d2"}}
```

```json response document.close
{"id": 6, "result": {}}
```

### `attachment.save`

把一个附件保存到用户选的位置。附件字节**不经过 stdio**，由 sidecar 直接写文件。

- 参数：`docId`（string，必填）；`attachmentId`（string，必填）；`targetPath`（string，必填，**绝对路径**）用户在系统保存对话框里选的完整路径
- 结果：`{ "path": string }` 写出的文件的完整路径
- 错误：`document_not_found`、`attachment_not_found`、`write_failed`、`invalid_params`

行为：

- 保存的内容与附件原始字节完全一致（传输编码已还原；嵌套邮件写出的是一份独立的 `.eml` 或 `.msg`）。
- `targetPath` 已有同名文件时**直接覆盖**，因为系统对话框已经确认过。文件名不做任何清理（路径是用户自己选的）。
- 先写同目录下的临时文件（`.efv-<随机>.tmp`），写完再替换目标。中途失败时目标文件保持原样，临时文件会被删除，不会留下残缺文件。
- 目标所在的文件夹必须已经存在，不会创建。
- 可执行文件和脚本也可以保存（只是不能直接打开）。

```json request
{"id": 10, "method": "attachment.save", "params": {"docId": "d1", "attachmentId": "2", "targetPath": "/Users/alice/Downloads/report.pdf"}}
```

```json response attachment.save
{"id": 10, "result": {"path": "/Users/alice/Downloads/report.pdf"}}
```

目标文件夹不存在或没有权限时：

```json error
{"id": 10, "error": {"code": "write_failed", "message": "无法保存到 \"/Users/alice/Nowhere/report.pdf\"：Could not find a part of the path."}}
```

### `attachment.saveAll`

把全部附件保存到一个文件夹，**不覆盖任何已有文件**。

- 参数：`docId`（string，必填）；`targetDir`（string，必填，**绝对路径**）用户选的文件夹，必须已经存在
- 结果：`{ "saved": [{ "attachmentId": string, "path": string }], "failed": [{ "attachmentId": string, "code": string, "message": string }] }`
- 错误（整个请求失败）：`document_not_found`、`write_failed`（文件夹不存在）、`invalid_params`

行为（对每个附件依次执行）：

1. 清理文件名：去掉路径成分（`../../.bashrc` 变成 `.bashrc`）、替换各平台的非法字符、处理 Windows 保留名（`CON.txt` 变成 `_CON.txt`）、限制长度；没有文件名时用 `attachment`。
2. 目标文件夹里已有同名文件（或前面的附件刚用了这个名字）时生成 `name (1).ext`、`name (2).ext`……
3. 确认最终路径在目标文件夹之内。
4. 用“只创建、不覆盖”的方式创建文件。取名之后、创建之前别的进程抢先创建了同名文件时，创建会失败，这时重新取名，绝不覆盖。
5. 单个附件失败（磁盘满等）记入 `failed`，不影响其他附件；失败附件留下的残缺文件会被删除。`failed[].code` 是 `write_failed` 或 `internal_error`。

文档没有附件时返回两个空数组。可执行文件和脚本同样会被保存。

```json request
{"id": 11, "method": "attachment.saveAll", "params": {"docId": "d1", "targetDir": "/Users/alice/Downloads"}}
```

```json response attachment.saveAll
{
  "id": 11,
  "result": {
    "saved": [
      {"attachmentId": "2", "path": "/Users/alice/Downloads/report (1).pdf"}
    ],
    "failed": [
      {"attachmentId": "3", "code": "write_failed", "message": "无法写入文件：There is not enough space on the disk."}
    ]
  }
}
```

### `attachment.extractForOpen`

把一个附件写到本次会话的临时目录，供 Rust 用系统默认程序打开。

- 参数：`docId`（string，必填）；`attachmentId`（string，必填）
- 结果：`{ "path": string }` 提取出的文件的完整路径
- 错误：`document_not_found`、`attachment_not_found`、`executable_refused`、`write_failed`、`invalid_params`

行为：

- **可执行文件和脚本直接拒绝**（`executable_refused`），什么都不写。按清理后文件名的最后一个扩展名判断，不区分大小写，`invoice.pdf.exe` 也算可执行。扩展名清单见设计文档 D6：Windows（`.exe .bat .cmd .com .msi .ps1 .vbs .js .jse .wsf .scr .lnk .hta .cpl`）、macOS（`.app .command .pkg`）、Linux（`.sh .desktop .AppImage .run`），以及 `.jar .vbe .ws .wsh .psm1 .psd1 .ps1xml .reg .msc .scf .url .pif .msix .msixbundle .appx .appxbundle .appinstaller .application .xll .iso .img .vhd .vhdx`。不论来自哪个系统，只要扩展名在表里就拒绝。
- 文件写在 `<系统临时目录>/EmailFileViewer/sessions/<会话 Id>/<docId>/<清理后的文件名>`，同一个文档里两个附件重名时第二个是 `name (1).ext`。先写临时文件再改名，打开程序不会读到写了一半的文件。
- 同一个附件再次提取时，直接返回上次的路径（文件还在的话）。
- 路径的使用方式由 Rust 决定；sidecar 不负责打开文件。

```json request
{"id": 12, "method": "attachment.extractForOpen", "params": {"docId": "d1", "attachmentId": "2"}}
```

```json response attachment.extractForOpen
{"id": 12, "result": {"path": "/var/folders/ab/cd/T/EmailFileViewer/sessions/0a1b2c3d4e5f60718293a4b5c6d7e8f9/d1/report.pdf"}}
```

```json error
{"id": 13, "error": {"code": "executable_refused", "message": "附件 \"installer.exe\" 是可执行文件或脚本，只能保存，不能直接打开。"}}
```

## 6. 并发与顺序

- **不同请求可以同时处理**，响应的先后顺序不保证，按 `id` 对应。同时处理的请求个数有上限（处理器个数，2 到 8 个），多出来的排队等待。
- **同一个文档的请求串行执行，并且按 sidecar 读到它们的先后顺序执行**（每个文档一个队列）。例如先发 `attachment.save` 再发 `document.close`，保存一定先完成；排在 `document.close` 后面的请求会得到 `document_not_found`。
  不同文档之间、以及不针对文档的请求（`system.ping`、`email.open`）互不影响。
- 排队只管“sidecar 收到的顺序”。因为 `docId` 要等 `email.open` 的响应才知道，调用方不可能在拿到 `docId` 之前就发出针对它的请求。
- **每条消息完整地占一行**：所有响应通过同一把锁写出，并发的响应不会交错。
- **单个请求出错只影响它自己**：可预料的失败变成对应的错误码；未处理的异常变成该请求的 `internal_error`，异常详情写到 stderr，进程继续运行，后面的请求照常处理。

## 7. 文档与临时目录的生命周期

- `docId` 由 sidecar 分配（`d1`、`d2`……，进程内递增，不重复使用），在 `email.open` 和 `email.openNested` 的结果里返回。它只在这个 sidecar 进程内有效；sidecar 重启后，之前所有的 `docId` 都失效（调用方需要重新打开）。
- 解析结果缓存在内存里，保存和提取附件时不需要重新读文件，原文件也不被占用。文档一直保留到 `document.close` 或进程退出。
- 本次会话的临时目录 `<系统临时目录>/EmailFileViewer/sessions/<会话 Id>/` 在启动时创建（Unix 上只有当前用户可访问），目录里有一个一直被独占打开的 `.lock` 文件。
  - **启动时**删除其他会话留下的目录。加得上 `.lock` 锁的才算“已经结束的会话”；锁被占着的说明那个会话还活着（多个实例同时运行），不会动它。
  - **退出时**（stdin EOF、SIGINT、SIGTERM）删除本会话的目录。删除失败（例如文件仍被外部程序打开，Windows 上常见）直接忽略，留到下次启动时清理。被强行结束（SIGKILL）的会话也在下次启动时清理。
- 系统临时目录不可写时，sidecar 仍然能解析邮件、保存附件，只有 `attachment.extractForOpen` 返回 `write_failed`。

## 8. 给调用方（Rust 端）的建议

- **启动**：用 `--stdio` 启动，立即发送 `system.ping`，收到响应才算就绪。首次启动可能因为系统的安全扫描（macOS 的 Gatekeeper、Windows Defender）而较慢，就绪超时建议 15 秒。
- **请求超时**（建议值，由调用方实现）：`system.ping` 5 秒；`email.open`、`email.openNested` 30 秒；`document.close` 5 秒；`attachment.save`、`attachment.extractForOpen` 60 秒；`attachment.saveAll` 120 秒。超过超时就认为 sidecar 卡住，结束进程；所有进行中的请求按失败返回，下次请求时重新启动，并且丢掉所有旧的 `docId`。
- **进程退出**：sidecar 意外退出（崩溃）时，所有进行中的请求都应以失败返回，下次请求时自动重启。畸形文件让解析器崩溃只会影响 sidecar，不会拖垮界面。
- **路径**：`email.open` 的 `path` 只来自 Rust 弹出的对话框、拖放事件或启动参数；`targetPath`、`targetDir` 是 Rust 弹出保存对话框得到的绝对路径。前端传来的路径不要直接转发。
- **正常关闭**：关闭 sidecar 的 stdin，然后等待进程退出（它需要清理临时目录）；等不到再强行结束。
- **`internal_error`**：表示 sidecar 有 bug，可以提示用户“处理失败”，并把 stderr 的内容记录下来。

## 9. 平台差异

协议在 Windows、macOS、Linux 上完全一致，下面是容易踩坑的几点。

- **行尾**：sidecar 不经过 `Console.Out`（它在 Windows 上会把换行写成 `\r\n`），直接把字节写到 stdout，每条消息固定以单个 `\n` 结尾，行内没有 `\r`（JSON 字符串里的控制字符都被转义了）。调用方按 `\n` 切分即可；为稳妥起见，也可以去掉行尾可能多出来的 `\r`。
- **请求的行尾**可以是 `\n` 或 `\r\n`，输入结束前的最后一行没有行尾也会被处理。
- **编码**：stdin、stdout、stderr 都是 UTF-8（无 BOM），sidecar 不依赖也不修改控制台代码页。
- **路径**：`targetPath`、`targetDir` 必须是完整路径，否则返回 `invalid_params`。
  - Windows：`C:\Users\alice\x.pdf`、`C:/Users/alice/x.pdf`、UNC 路径 `\\server\share\x.pdf`、带 `\\?\` 前缀的路径（例如 Rust 的 `canonicalize` 的结果 `\\?\C:\Users\alice\x.pdf`）都可以；没有盘符的 `\Users\alice\x.pdf` 和只有盘符的 `C:x.pdf` 算相对路径，会被拒绝。路径里 `/` 和 `\` 混用也可以。
  - macOS、Linux：以 `/` 开头的路径。
  - 返回的 `path` 是规范化之后的路径（`..` 已折叠，Windows 上分隔符统一为 `\`），不一定和请求里写的字符完全一样。
- **大小写**：Windows 和 macOS 的文件系统默认不区分大小写，重名判断也就不区分（`Report.PDF` 已经存在时，`report.pdf` 会被保存成 `report (1).pdf`）；Linux 区分大小写。
- **文件被占用**（主要是 Windows）：
  - 保存时，刚写完的临时文件可能被杀毒软件短暂占用，目标文件也可能正被别的程序打开（例如 PDF 阅读器）。sidecar 改名遇到“文件正在使用”或“拒绝访问”时会重试，总共最多等约 0.6 秒，仍然不行才返回 `write_failed`，原文件保持不变。
  - 退出时，某个提取出来的文件仍被外部程序打开，会话目录就删不掉。这不算错误：退出码仍然是 0，残留的目录在下一次启动时清理（那时锁文件已经释放）。
  - 会话目录里的 `.lock` 文件以独占方式打开，别的进程打不开它；启动时清理其他会话的目录，就是靠“能不能打开这个锁文件”判断那个会话是否还活着。
- **信号**：SIGINT/SIGTERM（Windows 上对应 Ctrl+C、关闭控制台）会走正常的退出流程；强行结束进程（`TerminateProcess`、SIGKILL）不会清理，留到下次启动。
