# 样本来源与协议

本目录中的邮件样本取自下面两个开源仓库的测试数据，两个仓库均使用 MIT 协议，允许再分发。
协议原文见 `licenses/` 目录。文件内容未做任何修改，下载时固定在记录的提交上，便于核对。

| 仓库 | 协议 | 固定的提交 | 协议原文 |
|------|------|-----------|----------|
| [jstedfast/MimeKit](https://github.com/jstedfast/MimeKit) | MIT | `d7fc9633e987e0259f58cbfe3f82a6f10106fd24` | [licenses/MimeKit-LICENSE.txt](licenses/MimeKit-LICENSE.txt) |
| [Sicos1977/MSGReader](https://github.com/Sicos1977/MSGReader) | MIT | `b9b04a7aef354c12872b48c29da87ba247f6b46b` | [licenses/MSGReader-license.txt](licenses/MSGReader-license.txt) |

## 文件清单

| 文件 | 格式 | 来源仓库 | 来源 URL | 协议 | 说明 |
|------|------|----------|----------|------|------|
| `simple-multipart.eml` | EML | MimeKit | https://github.com/jstedfast/MimeKit/blob/d7fc9633e987e0259f58cbfe3f82a6f10106fd24/UnitTests/TestData/messages/simple-multipart.eml | MIT | 带 1 个附件的多部分邮件，地址为 example.com |
| `simple-embedded-message.eml` | EML | MimeKit | https://github.com/jstedfast/MimeKit/blob/d7fc9633e987e0259f58cbfe3f82a6f10106fd24/UnitTests/TestData/messages/simple-embedded-message.eml | MIT | 含 `message/rfc822` 嵌套邮件，地址为 example.com |
| `EmailWithMultilineSubject.eml` | EML | MSGReader | https://github.com/Sicos1977/MSGReader/blob/b9b04a7aef354c12872b48c29da87ba247f6b46b/MsgReaderTests/SampleFiles/EmailWithMultilineSubject.eml | MIT | 主题跨多行折叠的纯文本邮件 |
| `HtmlSampleEmailWithAttachment.msg` | Outlook MSG | MSGReader | https://github.com/Sicos1977/MSGReader/blob/b9b04a7aef354c12872b48c29da87ba247f6b46b/MsgReaderTests/SampleFiles/HtmlSampleEmailWithAttachment.msg | MIT | HTML 正文，带附件 |
| `RtfSampleEmail.msg` | Outlook MSG | MSGReader | https://github.com/Sicos1977/MSGReader/blob/b9b04a7aef354c12872b48c29da87ba247f6b46b/MsgReaderTests/SampleFiles/RtfSampleEmail.msg | MIT | RTF 正文，无附件 |
| `EmailWithMsgAttachment.msg` | Outlook MSG | MSGReader | https://github.com/Sicos1977/MSGReader/blob/b9b04a7aef354c12872b48c29da87ba247f6b46b/MsgReaderTests/SampleFiles/EmailWithMsgAttachment.msg | MIT | 附件是另一封 MSG（嵌套邮件） |

## 注意

- 三个 MSG 文件中的发件人、收件人是上游测试数据里已公开的地址，不是本项目构造的。任务 2.1 整理样本库时需要再评估是否保留，或换成自己构造的等价样本。
- 任务 2.2 中需要自己构造的样本（GBK、Big5、winmail.dat、S/MIME 等）不放在本文件中，另在 `samples/README.md` 里记录。
