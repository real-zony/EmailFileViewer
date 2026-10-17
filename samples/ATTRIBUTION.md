# 样本来源与协议

`samples/` 里的文件分两类：

- **取自上游的样本**：下面的"上游样本清单"逐个列出来源。它们来自两个开源仓库的测试数据，两个仓库都使用 MIT 协议，允许再分发，协议原文见 `licenses/`。
- **本项目自己构造的样本**：由 `samples/generator/` 生成，不含上游邮件内容，来源和用途见 [README.md](README.md)。生成器本身用到的第三方库也列在下面。

不属于上面两类的文件只有本文件、`README.md`、`.gitattributes` 和 `licenses/`。

## 上游仓库

| 仓库 | 协议 | 固定的提交 | 协议原文 |
|------|------|-----------|----------|
| [jstedfast/MimeKit](https://github.com/jstedfast/MimeKit) | MIT | `d7fc9633e987e0259f58cbfe3f82a6f10106fd24` | [licenses/MimeKit-LICENSE.txt](licenses/MimeKit-LICENSE.txt) |
| [Sicos1977/MSGReader](https://github.com/Sicos1977/MSGReader) | MIT | `b9b04a7aef354c12872b48c29da87ba247f6b46b` | [licenses/MSGReader-license.txt](licenses/MSGReader-license.txt) |

文件内容没有做任何修改。下表的"Git blob"是文件的 `git hash-object` 结果（前 10 位），与上游该提交下同一文件的 blob 一致，可用来核对。部分文件只改了名字（扩展名由 `.txt` 改为 `.eml`，或加上能看出类别的前缀），"来源 URL"里是上游的原始路径。

## 上游样本清单

| 文件 | 格式 | 来源仓库 | 来源 URL | 协议 | Git blob | 说明 |
|------|------|----------|----------|------|----------|------|
| `simple-multipart.eml` | EML | MimeKit | https://github.com/jstedfast/MimeKit/blob/d7fc9633e987e0259f58cbfe3f82a6f10106fd24/UnitTests/TestData/messages/simple-multipart.eml | MIT | `9bf9229e04` | 带 1 个附件的多部分邮件，地址为 example.com |
| `simple-embedded-message.eml` | EML | MimeKit | https://github.com/jstedfast/MimeKit/blob/d7fc9633e987e0259f58cbfe3f82a6f10106fd24/UnitTests/TestData/messages/simple-embedded-message.eml | MIT | `b51f9150f9` | 含 `message/rfc822` 嵌套邮件，地址为 example.com |
| `smime-signed-thunderbird.eml` | EML | MimeKit | https://github.com/jstedfast/MimeKit/blob/d7fc9633e987e0259f58cbfe3f82a6f10106fd24/UnitTests/TestData/smime/thunderbird-signed.txt | MIT | `2ec43a9ad3` | Thunderbird 24 用 S/MIME 证书签名的邮件（`multipart/signed`，含一张 jpg 附件）；上游原名 `thunderbird-signed.txt` |
| `charset-iso-2022-jp-thunderbird.eml` | EML | MimeKit | https://github.com/jstedfast/MimeKit/blob/d7fc9633e987e0259f58cbfe3f82a6f10106fd24/UnitTests/TestData/messages/japanese.txt | MIT | `0f95472ad0` | Thunderbird 发出的 ISO-2022-JP 邮件，主题是拆成两段的 RFC 2047 编码字；上游原名 `japanese.txt` |
| `charset-koi8-r-raw-header.eml` | EML | MimeKit | https://github.com/jstedfast/MimeKit/blob/d7fc9633e987e0259f58cbfe3f82a6f10106fd24/UnitTests/TestData/compliance/raw-koi8r-header.eml | MIT | `844b7d88bc` | 邮件头里直接写了 KOI8-R 原始字节、没有 RFC 2047 编码；上游原名 `raw-koi8r-header.eml` |
| `EmailWithMultilineSubject.eml` | EML | MSGReader | https://github.com/Sicos1977/MSGReader/blob/b9b04a7aef354c12872b48c29da87ba247f6b46b/MsgReaderTests/SampleFiles/EmailWithMultilineSubject.eml | MIT | `e1c1bc4d58` | 主题跨多行折叠的纯文本邮件 |
| `TestWithAttachmentsAndInlines.eml` | EML | MSGReader | https://github.com/Sicos1977/MSGReader/blob/b9b04a7aef354c12872b48c29da87ba247f6b46b/MsgReaderTests/SampleFiles/TestWithAttachmentsAndInlines.eml | MIT | `dfffef44da` | Outlook 发出的邮件：一张 `cid:` 内嵌 PNG、4 个文本附件、2 个作为附件的 `message/rfc822`；头里是 Windows-1252 编码字 |
| `HtmlSampleEmailWithAttachment.msg` | Outlook MSG | MSGReader | https://github.com/Sicos1977/MSGReader/blob/b9b04a7aef354c12872b48c29da87ba247f6b46b/MsgReaderTests/SampleFiles/HtmlSampleEmailWithAttachment.msg | MIT | `6488f24587` | HTML 封装在 RTF 中（没有 PR_HTML 流，RTF 带 `\fromhtml1`），带一个 PDF 附件 |
| `RtfSampleEmail.msg` | Outlook MSG | MSGReader | https://github.com/Sicos1977/MSGReader/blob/b9b04a7aef354c12872b48c29da87ba247f6b46b/MsgReaderTests/SampleFiles/RtfSampleEmail.msg | MIT | `2b6dc89c43` | 纯文本加 RTF 正文（RTF 不是 HTML 封装），没有 HTML，无附件 |
| `EmailWithMsgAttachment.msg` | Outlook MSG | MSGReader | https://github.com/Sicos1977/MSGReader/blob/b9b04a7aef354c12872b48c29da87ba247f6b46b/MsgReaderTests/SampleFiles/EmailWithMsgAttachment.msg | MIT | `d40b8bbb2a` | 附件是另一封 MSG（嵌套邮件）；HTML 封装在 RTF 中 |
| `EmailWithInnerMailAndAttachments.msg` | Outlook MSG | MSGReader | https://github.com/Sicos1977/MSGReader/blob/b9b04a7aef354c12872b48c29da87ba247f6b46b/MsgReaderTests/SampleFiles/EmailWithInnerMailAndAttachments.msg | MIT | `0167c284d4` | 外层邮件带 2 个 PDF 和一封内层 MSG，内层也带 2 个 PDF；HTML 封装在 RTF 中，无发件人和收件人，正文是日文字体的 Word HTML |
| `EmailWithAttachments.msg` | Outlook MSG | MSGReader | https://github.com/Sicos1977/MSGReader/blob/b9b04a7aef354c12872b48c29da87ba247f6b46b/MsgReaderTests/SampleFiles/EmailWithAttachments.msg | MIT | `b92b476ec3` | 有 PR_HTML 正文（没有 RTF）、收件人 / 抄送 / 密送各一个，一张普通附件和一张按 Content-ID 内嵌的 jpg；地址是虚构的 neverland.com |
| `TxtSampleEmail.msg` | Outlook MSG | MSGReader | https://github.com/Sicos1977/MSGReader/blob/b9b04a7aef354c12872b48c29da87ba247f6b46b/MsgReaderTests/SampleFiles/TxtSampleEmail.msg | MIT | `7a6c66147b` | 纯文本邮件，Outlook 附带了 `\fromtext` 的 RTF；正文是《战争与和平》开头（公共领域） |
| `RtfWithShortRussianString.msg` | Outlook MSG | MSGReader | https://github.com/Sicos1977/MSGReader/blob/b9b04a7aef354c12872b48c29da87ba247f6b46b/MsgReaderTests/SampleFiles/RtfWithShortRussianString.msg | MIT | `db60f679be` | 俄文（cp1251）HTML 封装在 RTF 中，主题为空，无发件人和收件人 |

## 上游测试数据里的真实地址

上游测试数据里有些是已公开的真实姓名和邮箱，不是本项目构造的。它们原样保留，不再改动：

- `RtfSampleEmail.msg`、`HtmlSampleEmailWithAttachment.msg`、`TxtSampleEmail.msg`：发件人和收件人是 Chris Wilson（leeds.gov.uk 的地址）。
- `EmailWithMsgAttachment.msg`：发件人和收件人是 Daniel Triendl（cp-solutions.at 的地址）。
- `smime-signed-thunderbird.eml`：发件人和收件人是 MimeKit 作者 Jeffrey Stedfast（gnome.org 的地址）；签名部分带着他的公开 S/MIME 证书，证书里有同一个姓名和邮箱。
- `charset-iso-2022-jp-thunderbird.eml`：邮箱已被上游替换成 `x@x.com`，但姓名（Atsushi Eno、Jeffrey Stedfast）保留。

其余上游样本的地址都是虚构的或匿名的（example.com、neverland.com、domain.ch，或没有地址）。

## 没有采用的上游文件

- MimeKit 的 `UnitTests/TestData/tnef/*.tnef`：是裸的 TNEF 数据流而不是邮件文件，并且里面带真实的姓名和邮箱（例如 TNEF 库作者的地址）。winmail.dat 样本改为自己构造，见 README。
- MimeKit 的 `tnef/ukr.eml`、`tnef/rich-text.eml`：前者含真实租户标识，后者约 1.1 MB，都不采用。
- MsgReader 的 `UTF-8_Test.eml`、`TestWithInlineHavingContentDisposition.eml` 等：邮件头里有完整的投递链路和真实域名，不采用。

## 自己构造的样本和生成器依赖

自己构造的样本由 `samples/generator/` 生成，来源和用途见 [README.md](README.md)。生成器本身依赖下面几个库，只在生成时使用，不随样本再分发：

| 库 | 协议 | 用途 | 协议依据 |
|----|------|------|----------|
| MimeKit 4.18.1 | MIT | 生成 EML、S/MIME 签名和加密 | [licenses/MimeKit-LICENSE.txt](licenses/MimeKit-LICENSE.txt) |
| MsgReader 6.2.0 | MIT | 生成后读回 MSG 做自检 | [licenses/MSGReader-license.txt](licenses/MSGReader-license.txt) |
| MsgKit 3.0.6 | MIT | 生成 MSG | [licenses/MsgKit-LICENSE.txt](licenses/MsgKit-LICENSE.txt)，原文取自 [README 的 License Information](https://github.com/Sicos1977/MsgKit/blob/496322312a3aeeca30953296dfe7e71967b839d0/README.md#license-information)（提交 `496322312a3aeeca30953296dfe7e71967b839d0`，即 NuGet 3.0.6 对应的提交）；NuGet 包元数据的协议表达式也是 `MIT` |
| OpenMcdf 3.3.0 | MPL-2.0 | 读写复合文档，用来把附件改写成嵌入邮件 | 随 MsgKit 一起引入；只在生成时使用，样本里没有它的代码 |

生成器产出的文件不包含上述库的源代码，只包含按公开格式写出的数据。
