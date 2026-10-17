# NativeAOT 可行性实验记录（任务 1.2）

实验日期：2026-10-16。目的：验证 MimeKit + MsgReader 能否用 NativeAOT 发布，并记录构建和发布过程中的全部 IL/AOT/裁剪警告，供任务 1.4 决定 sidecar 的发布方式。

## 1. 环境与版本

| 项目 | 值 |
|------|----|
| 机器 | macOS（Darwin 25.6.0），Apple Silicon，Xcode/clang 已安装 |
| .NET SDK | 10.0.401（`global.json` 固定 10.0.100，`rollForward: latestFeature`） |
| NativeAOT 运行时包 / ILCompiler | `microsoft.netcore.app.runtime.nativeaot.osx-arm64` 10.0.12、`runtime.osx-arm64.microsoft.dotnet.ilcompiler` 10.0.12 |
| MimeKit | 4.18.1 |
| MsgReader | 6.2.0（依赖 OpenMcdf 3.3.0、RtfPipe 2.0.7677.4303、Microsoft.Maui.Graphics 10.0.110、UTF.Unknown 2.7.0、Microsoft.IO.RecyclableMemoryStream 3.0.1） |
| 项目设置 | Core：`IsAotCompatible=true`；Sidecar：`IsAotCompatible=true`、`PublishAot=true`、`InvariantGlobalization=true` |

## 2. 命令

```bash
# 构建（Debug 和 Release 各一次，均为 --no-incremental，以便看到完整警告）
dotnet build core --no-incremental
dotnet build core -c Release --no-incremental

# 发布（任务要求的命令）
dotnet publish core/src/EmailFileViewer.Sidecar -c Release -r osx-arm64

# 发布，并让第三方程序集的警告逐条展示（而不是每个程序集合并成一条）
dotnet publish core/src/EmailFileViewer.Sidecar -c Release -r osx-arm64 -p:TrimmerSingleWarn=false -v:n

# 运行原生可执行文件
core/src/EmailFileViewer.Sidecar/bin/Release/net10.0/osx-arm64/publish/EmailFileViewer.Sidecar --smoke samples
```

## 3. 警告清单

### 3.1 构建阶段

| 命令 | 警告数 |
|------|--------|
| `dotnet build core --no-incremental`（Debug） | 0 |
| `dotnet build core -c Release --no-incremental` | 0 |

Core 和 Sidecar 打开了裁剪、单文件和 AOT 分析器，我们自己的代码（最小解析入口和 smoke 命令）没有触发任何分析器警告。

### 3.2 发布阶段（smoke 实际走到的代码）

| 命令 | IL/AOT/裁剪警告数 |
|------|-------------------|
| `dotnet publish ... -r osx-arm64`（默认） | 0 |
| 同上，加 `-p:TrimmerSingleWarn=false -v:n` | 0 |

为了确认"0"不是工具链把警告吞掉了，另做了一个对照：在临时目录建一个 `PublishAot` 控制台项目，写入 `Type.GetType(args[0])` 和 `JsonSerializer.Serialize(new { A = 1 })`，发布时分析器和 ILC 都正常报出 IL2057、IL2026、IL3050。说明这套工具链会报警告，MimeKit 和 MsgReader 在 smoke 走到的路径上确实没有。

### 3.3 NuGet 审计警告（不是 IL 警告，但属于依赖风险）

第一次还原时出现以下警告，来源是 `System.Security.Cryptography.Pkcs`（MimeKit 依赖 10.0.0，MsgReader 依赖 10.0.12，最终解析为 10.0.12）：

```
warning NU1902: Package 'System.Security.Cryptography.Pkcs' 10.0.12 has a known moderate severity vulnerability, https://github.com/advisories/GHSA-2fmv-rv2c-59x7
warning NU1903: Package 'System.Security.Cryptography.Pkcs' 10.0.12 has a known high severity vulnerability, https://github.com/advisories/GHSA-846f-f764-76mx
```

两条都是拒绝服务漏洞，`10.0.13` 起修复。处理方式：在 `core/Directory.Packages.props` 打开 `CentralPackageTransitivePinningEnabled`，并把 `System.Security.Cryptography.Pkcs` 固定为 10.0.13。之后还原、构建、发布都没有该警告。MimeKit/MsgReader 升级到自带修复版本后可以去掉这个固定。

### 3.4 补充实验 A：把第三方程序集整个作为根来分析

smoke 只会让 ILC 编译"从入口点可达"的代码，所以 0 警告只代表这条路径干净。为了看出后续任务（3.x）一旦走到更多功能会遇到什么，在 **临时目录的副本** 里给 Sidecar 加了下面的配置（没有改仓库里的文件），让 ILC 分析这些程序集里的全部代码：

```xml
<ItemGroup>
  <TrimmerRootAssembly Include="MimeKit" />
  <TrimmerRootAssembly Include="MsgReader" />
  <TrimmerRootAssembly Include="OpenMcdf" />
  <TrimmerRootAssembly Include="RtfPipe" />
  <TrimmerRootAssembly Include="Microsoft.Maui.Graphics" />
  <TrimmerRootAssembly Include="EmailFileViewer.Core" />
</ItemGroup>
```

用 `-p:TrimmerSingleWarn=false` 发布成功，共 **11 条** 警告，逐条如下（去掉了行尾的项目路径）：

```
ILC : Trim analysis warning IL2026: MimeKit.Cryptography.CryptographyContext.Create(String): Using member 'MimeKit.Cryptography.DefaultSecureMimeContext.DefaultSecureMimeContext()' which has 'RequiresUnreferencedCodeAttribute' can break functionality when trimming application code. Uses Reflection to load System.Data.SQLite on Windows or Mono.Data.Sqlite on Linux / macOS.
ILC : AOT analysis warning IL3050: MsgReader.Rtf.DocumentInfo.StringItems.get: Using member 'System.Collections.ArrayList.ToArray(Type)' which has 'RequiresDynamicCodeAttribute' can break functionality when AOT compiling. The code for an array of the specified type might not be available.
ILC : Trim analysis warning IL2026: MsgReader.Outlook.ReactionHelper.GetReactionsFromOwnerReactionsHistory(Byte[]): Using member 'System.Text.Json.JsonSerializer.Deserialize<IList`1<Reaction>>(String,JsonSerializerOptions)' which has 'RequiresUnreferencedCodeAttribute' can break functionality when trimming application code. JSON serialization and deserialization might require types that cannot be statically analyzed. Use the overload that takes a JsonTypeInfo or JsonSerializerContext, or make sure all of the required types are preserved.
ILC : AOT analysis warning IL3050: MsgReader.Outlook.ReactionHelper.GetReactionsFromOwnerReactionsHistory(Byte[]): Using member 'System.Text.Json.JsonSerializer.Deserialize<IList`1<Reaction>>(String,JsonSerializerOptions)' which has 'RequiresDynamicCodeAttribute' can break functionality when AOT compiling. JSON serialization and deserialization might require types that cannot be statically analyzed and might need runtime code generation. Use System.Text.Json source generation for native AOT applications.
ILC : Trim analysis warning IL2026: MsgReader.Helpers.MailMessageExtension.WriteTo(MailMessage,Stream): Using member 'System.Reflection.Assembly.GetType(String)' which has 'RequiresUnreferencedCodeAttribute' can break functionality when trimming application code. Types might be removed by trimming. If the type name is a string literal, consider using Type.GetType instead.
ILC : Trim analysis warning IL2075: MsgReader.Helpers.MailMessageExtension.WriteTo(MailMessage,Stream): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.NonPublicConstructors' in call to 'System.Type.GetConstructor(BindingFlags,Binder,Type[],ParameterModifier[])'. The return value of method 'System.Reflection.Assembly.GetType(String)' does not have matching annotations. The source value must declare at least the same requirements as those declared on the target location it is assigned to.
ILC : Trim analysis warning IL2026: MsgReader.Helpers.MailMessageExtension.WriteTo(MailMessage,Stream): Using member 'System.Reflection.Assembly.GetType(String)' which has 'RequiresUnreferencedCodeAttribute' can break functionality when trimming application code. Types might be removed by trimming. If the type name is a string literal, consider using Type.GetType instead.
ILC : AOT analysis warning IL3050: MsgReader.Helpers.MailMessageExtension.WriteTo(MailMessage,Stream): Using member 'System.Reflection.MethodInfo.MakeGenericMethod(Type[])' which has 'RequiresDynamicCodeAttribute' can break functionality when AOT compiling. The native code for this instantiation might not be available at runtime.
ILC : Trim analysis warning IL2075: MsgReader.Helpers.MailMessageExtension.WriteTo(MailMessage,Stream): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.NonPublicMethods' in call to 'System.Type.GetMethod(String,BindingFlags)'. The return value of method 'System.Object.GetType()' does not have matching annotations. The source value must declare at least the same requirements as those declared on the target location it is assigned to.
ILC : Trim analysis warning IL2075: RtfPipe.ControlTag.Negate(ControlWord`1<Boolean>): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicConstructors' in call to 'System.Type.GetConstructor(Type[])'. The return value of method 'System.Object.GetType()' does not have matching annotations. The source value must declare at least the same requirements as those declared on the target location it is assigned to.
ILC : AOT analysis warning IL3050: RtfPipe.ControlTag.Negate(ControlWord`1<Boolean>): Using member 'System.Linq.Expressions.Expression.Lambda(Expression,ParameterExpression[])' which has 'RequiresDynamicCodeAttribute' can break functionality when AOT compiling. Delegate creation requires dynamic code generation.
```

汇总：

| # | 代码 | 程序集 | 类型.方法 | 说明 |
|---|------|--------|-----------|------|
| 1 | IL2026 | MimeKit | `CryptographyContext.Create(String)` -> `DefaultSecureMimeContext()` | 默认 S/MIME 上下文通过反射加载 System.Data.SQLite / Mono.Data.Sqlite。只在使用默认 S/MIME 上下文（验证签名、解密）时才会走到 |
| 2 | IL3050 | MsgReader | `Rtf.DocumentInfo.StringItems.get` | `ArrayList.ToArray(Type)` 需要运行时生成数组类型的代码 |
| 3 | IL2026 | MsgReader | `Outlook.ReactionHelper.GetReactionsFromOwnerReactionsHistory(Byte[])` | 用反射式 `System.Text.Json` 反序列化 Outlook "反应"数据 |
| 4 | IL3050 | MsgReader | 同上 | 同上，动态代码角度的同一调用 |
| 5 | IL2026 | MsgReader | `Helpers.MailMessageExtension.WriteTo(MailMessage, Stream)` | `Assembly.GetType(string)` 按名字找 `System.Net.Mail` 的内部类型 |
| 6 | IL2075 | MsgReader | 同上 | 对按名字取到的类型调用 `GetConstructor`，类型信息可能被裁掉 |
| 7 | IL2026 | MsgReader | 同上 | 第二处 `Assembly.GetType(string)` |
| 8 | IL3050 | MsgReader | 同上 | `MakeGenericMethod`，AOT 下对应实例化可能不存在 |
| 9 | IL2075 | MsgReader | 同上 | 对 `GetType()` 的结果调用 `GetMethod` 取非公开方法 |
| 10 | IL2075 | RtfPipe | `ControlTag.Negate(ControlWord<bool>)` | 对 `GetType()` 的结果调用 `GetConstructor` |
| 11 | IL3050 | RtfPipe | `ControlTag.Negate(ControlWord<bool>)` | `Expression.Lambda` 需要运行时生成委托 |

按功能归类：

- **MimeKit 默认 S/MIME 上下文（#1）**：任务 3.7 只需要识别签名和加密并设置标志，不要调用 `Verify` / `Decrypt` / 默认的 `CryptographyContext`，就不会走到。
- **MsgReader 内部（#2 到 #9）**：#3、#4 是"反应"数据，#5 到 #9 是把 EML 转成 `System.Net.Mail.MailMessage` 后再写出的辅助方法，都不在我们要用的路径上；#2 是 RTF 文档信息的字符串集合属性，是否被 MsgReader 的 RTF 处理用到，尚未核实。
- **RtfPipe（#10、#11）**：RTF 转 HTML 用的就是 RtfPipe，这一对警告在补充实验 B 中确实出现在可达路径上。

### 3.5 补充实验 B：smoke 之外再走到正文、RTF 转 HTML、附件字节

仍在临时目录的副本里（不在仓库中），把探测代码加到 Core 和 Sidecar：读取发件人、收件人数量、发送时间、`BodyText` / `BodyHtml` / `BodyRtf`、附件文件名和字节数、嵌套 MSG，对 `BodyRtf` 直接调用 `RtfPipe.Rtf.ToHtml`；EML 读取 `TextBody` / `HtmlBody`、各部分内容解码，以及 `message/rfc822` 部分。同样用 `dotnet publish -r osx-arm64 -p:TrimmerSingleWarn=false` 发布。

- 发布警告：只有 2 条，就是上表的 #10（IL2075）和 #11（IL3050），都在 RtfPipe 的 `ControlTag.Negate`。说明纯 RTF 正文转 HTML（任务 3.3）会走到它们。
- 运行结果：6 个样本全部成功，输出正确。例如 `RtfSampleEmail.msg`（只有 RTF 正文）转出 37201 字符的 HTML，开头是 `<div style="font-size:12pt;font-family:&quot;Times New Roman...`；`EmailWithMsgAttachment.msg` 的嵌套 MSG 也能读出；`HtmlSampleEmailWithAttachment.msg` 的附件 `SampleAttachment.pdf` 读出 169480 字节。
- 专门验证 #10、#11 所在的路径：直接转换 `{\rtf1 ... {\b bold\b0 normal \i ital\i0 \ul under\ulnone \strike s\strike0}}`，AOT 二进制输出 `plain <strong>bold</strong> normal <em>ital</em> <u>under</u> <s>s</s>`，`\b0`、`\i0`、`\ulnone`、`\strike0` 这些取消标记都正确处理，没有运行时异常。

## 4. 运行结果

### 4.1 smoke 命令（仓库里的最小实现）

原生可执行文件 `--smoke samples`，退出码 0，输出（制表符分隔：文件名、主题、附件数）：

```
EmailWithMsgAttachment.msg	FW: Msg Attachment Test	1
EmailWithMultilineSubject.eml	One Two Three Four Five Six Seven Eight Nine Ten Eleven Twelve Thirteen Fourteen Fifteen	0
HtmlSampleEmailWithAttachment.msg	HtmlSampleEmailWithAttachment	1
RtfSampleEmail.msg	RtfSampleEmail	0
simple-embedded-message.eml	test of simple embedded message/rfc822 part	0
simple-multipart.eml	this is a simple multipart message	1
```

- 失败路径：在临时目录放入一个只有前 4096 字节的 MSG（被截断）和一个正常 EML、一个 `README.md`，原生可执行文件输出 `ERROR  truncated.msg  OpenMcdf.FileFormatException: FAT chain index is greater than the sector count.`，仍然处理完其他文件，`README.md` 被忽略，最后退出码为 1。
- 目录不存在返回 1，没有参数返回 2。
- 目录里没有 `.eml` / `.msg` 文件时也返回 1，避免 CI 里样本目录写错路径却显示通过。

### 4.2 二进制

| 项目 | 值 |
|------|----|
| 可执行文件大小 | 6,494,632 字节（约 6.2 MiB） |
| 同目录的 `.dSYM` 调试符号 | 约 20 MB，不应随应用分发 |
| 格式 | Mach-O 64-bit executable arm64 |
| 动态库依赖 | 只有 macOS 系统库（libSystem、Foundation、CryptoKit、Security 等） |
| `--smoke samples` 耗时 | 约 0.024 秒（6 个文件，包含进程启动） |

Linux 上不依赖 libicu 的效果（`InvariantGlobalization`）：在 macOS 上用 Docker（Ubuntu 24.04 容器，系统里装有 libicu 74）发布 `linux-arm64` 做过验证。`ldd` 只显示 `libm`、`libc`，并且用 `LD_DEBUG=libs` 运行 smoke 时没有加载任何 libicu。对照组（`-p:InvariantGlobalization=false`）的 `ldd` 同样看不到 libicu，但 `LD_DEBUG=libs` 能看到运行时加载了 `libicudata`、`libicuuc`、`libicui18n`，所以 `ldd` 单独不足以证明，`.github/workflows/core.yml` 里同时做了两项检查。`linux-x64` 的结果由任务 1.3 的 CI 给出。

## 5. 实验中发现的其他问题

1. **MsgReader 解析 MSG 前必须已注册 `CodePagesEncodingProvider`。** 否则 `MsgReader.Rtf.Font` 的静态构造函数调用 `Encoding.GetEncoding(name)` 抛出 `TypeInitializationException`（单元测试里最初就是这样失败的）。目前 Sidecar 在 `Program.cs` 启动时注册，测试项目用 `[ModuleInitializer]` 注册。任务 3.4 建议把注册放到 Core 内部，让任何调用方都不会漏。
2. **MimeKit 的 `Attachments` 不会把没有 `Content-Disposition` 的 `message/rfc822` 部分算作附件。** `simple-embedded-message.eml` 含一个嵌套邮件，当前摘要显示附件数 0。任务 3.6 做附件列表和嵌套邮件标记时要自己遍历 `BodyParts`。
3. **MsgReader 的 `SentOn` 显示为本机时区的偏移**（实验机上是 `+08:00`；MSG 的时间属性一般按 UTC 存储，未逐个核实）。任务 3.2/3.3 要输出"带时区偏移的 ISO 8601"，需要核对 MSG 这边实际返回的 `Kind`/偏移，必要时自己转成 UTC。
4. **样本里有真实的发件人地址。** 三个 MSG 来自 MsgReader 测试集，内含上游公开的地址（见 `samples/ATTRIBUTION.md`），任务 2.1 整理样本时再评估。

## 6. 初步结论

1. **NativeAOT 可行（在 smoke 实际走到的路径上）。** `dotnet publish -r osx-arm64` 成功，构建和发布均为 0 条 IL/AOT/裁剪警告；原生可执行文件对 6 个样本（3 个真正的 Outlook MSG、3 个 EML，含附件、嵌套 MSG、嵌套 `message/rfc822`）全部解析正确，退出码 0；体积 6.2 MiB，进程启动加解析 6 个文件约 24 ms。
2. **没有任何警告指向 smoke 走到的代码。** 把分析范围扩大到 MimeKit、MsgReader、OpenMcdf、RtfPipe 的全部代码后有 11 条警告，其中只有 RtfPipe 的 2 条（`ControlTag.Negate`）在读取正文、把纯 RTF 转 HTML 时会被走到；该路径在 AOT 二进制中实测正常。
3. **后续需要留意的路径：** (a) 纯 RTF 转 HTML（RtfPipe，运行正常，但依赖反射和表达式树，升级 RtfPipe 后要重新验证）；(b) 任务 3.7 不要调用 MimeKit 的默认 S/MIME 上下文；(c) TNEF（winmail.dat）、S/MIME 标志识别、字符集自动识别（UTF.Unknown）本次没有走到，任务 3.4、3.6、3.7 实现后需要再发布一次，看有没有新增警告。
4. **建议（供任务 1.4 参考，不在本任务范围内做决定）：** 目前没有阻止 NativeAOT 的证据，倾向先保持 NativeAOT；最终是否采用，取决于任务 1.3 的四平台 CI 结果，以及任务 3.x 做完后再发布一次的警告和运行结果。退路（自包含 JIT 发布）暂时不需要启用。

## 7. 如何复现补充实验

补充实验 A 只需在 Sidecar 项目里临时加上 3.4 节的 `TrimmerRootAssembly` 项，用 `-p:TrimmerSingleWarn=false` 发布，实验结束后删掉。补充实验 B 的探测代码没有放进仓库；任务 3.x 的真实解析代码落地后，用 `TrimmerSingleWarn=false` 发布一次即可得到更有意义的结果，不需要再单独保留探测代码。
