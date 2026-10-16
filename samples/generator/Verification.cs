using System.Text;
using MimeKit;

namespace EmailFileViewer.SampleGenerator;

/// <summary>
/// 生成后的自检：把写出的文件重新读一遍，确认样本确实具备它声称的特征。
/// 任何一项不符都会抛异常，生成器以非零退出码结束。
/// </summary>
internal static class Verification
{
    public static MimeMessage LoadEml(string root, string relativePath)
    {
        using var stream = File.OpenRead(Path.Combine(root, relativePath));
        return MimeMessage.Load(stream);
    }

    public static void Require(bool condition, string description)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"样本自检失败：{description}");
        }
    }

    public static void RequireEqual(string expected, string? actual, string description)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"样本自检失败：{description}\n  期望：{expected}\n  实际：{actual}");
        }
    }

    public static void VerifyCharsetAndHeaderSamples(string root)
    {
        var gbk = LoadEml(root, "charset-gbk.eml");
        RequireEqual(CharsetSamples.GbkSubject, gbk.Subject, "charset-gbk.eml 的主题");
        RequireEqual(CharsetSamples.GbkFromName, gbk.From.Mailboxes.Single().Name, "charset-gbk.eml 的发件人名称");
        RequireEqual(CharsetSamples.GbkBody, NormalizeNewLines(gbk.TextBody), "charset-gbk.eml 的正文");
        Require(string.Equals(BodyAsText(gbk).ContentType.Charset, "gbk", StringComparison.OrdinalIgnoreCase), "charset-gbk.eml 声明了 charset=gbk");

        var gb18030 = LoadEml(root, "charset-gb18030.eml");
        RequireEqual(CharsetSamples.Gb18030Subject, gb18030.Subject, "charset-gb18030.eml 的主题");
        RequireEqual(CharsetSamples.Gb18030Body, NormalizeNewLines(gb18030.TextBody), "charset-gb18030.eml 的正文");

        var big5 = LoadEml(root, "charset-big5-undeclared.eml");
        RequireEqual(CharsetSamples.Big5Subject, big5.Subject, "charset-big5-undeclared.eml 的主题");
        var big5Part = BodyAsText(big5);
        Require(big5Part.ContentType.Charset is null, "charset-big5-undeclared.eml 的 Content-Type 没有 charset 参数");
        using (var decoded = new MemoryStream())
        {
            big5Part.Content!.DecodeTo(decoded);
            var text = Encoding.GetEncoding("big5").GetString(decoded.ToArray());
            RequireEqual(CharsetSamples.Big5Body, NormalizeNewLines(text), "charset-big5-undeclared.eml 按 Big5 解码后的正文");
        }

        var isoJp = LoadEml(root, "charset-iso-2022-jp.eml");
        RequireEqual(CharsetSamples.IsoJpSubject, isoJp.Subject, "charset-iso-2022-jp.eml 的主题");
        RequireEqual(CharsetSamples.IsoJpFromName, isoJp.From.Mailboxes.Single().Name, "charset-iso-2022-jp.eml 的发件人名称");
        RequireEqual(CharsetSamples.IsoJpBody, NormalizeNewLines(isoJp.TextBody), "charset-iso-2022-jp.eml 的正文");

        var shiftJis = LoadEml(root, "charset-shift-jis.eml");
        RequireEqual(CharsetSamples.ShiftJisSubject, shiftJis.Subject, "charset-shift-jis.eml 的主题");
        RequireEqual(CharsetSamples.ShiftJisBody, NormalizeNewLines(shiftJis.TextBody), "charset-shift-jis.eml 的正文");

        var rfc2047 = LoadEml(root, "headers-rfc2047.eml");
        RequireEqual(CharsetSamples.Rfc2047ExpectedSubject, rfc2047.Subject, "headers-rfc2047.eml 的主题");
        RequireEqual(CharsetSamples.Rfc2047ExpectedFromName, rfc2047.From.Mailboxes.Single().Name, "headers-rfc2047.eml 的发件人名称");
        RequireEqual(CharsetSamples.Rfc2047ToName, rfc2047.To.Mailboxes.First().Name, "headers-rfc2047.eml 的第一个收件人名称");

        // 原始文件里必须真的有两种编码字
        var raw = File.ReadAllText(Path.Combine(root, "headers-rfc2047.eml"), Encoding.ASCII);
        Require(raw.Contains("=?GB2312?B?", StringComparison.Ordinal) && raw.Contains("=?UTF-8?Q?", StringComparison.Ordinal), "headers-rfc2047.eml 同时含 GB2312(B) 和 UTF-8(Q) 编码字");
    }

    public static void VerifyMimeSamples(string root)
    {
        var timezone = LoadEml(root, "headers-timezone-bcc-replyto-no-cc.eml");
        Require(timezone.Cc.Count == 0 && timezone.Bcc.Count == 1 && timezone.ReplyTo.Count == 1, "时区样本有 Bcc 和 Reply-To、没有 Cc");
        RequireEqual("2026-10-05T09:00:00.0000000-07:00", timezone.Date.ToString("o"), "时区样本的日期");
        Require(File.ReadAllText(Path.Combine(root, "headers-timezone-bcc-replyto-no-cc.eml")).Contains("Date: Mon, 5 Oct 2026 09:00:00 -0700\r\n", StringComparison.Ordinal), "时区样本的 Date 头逐字等于 Mon, 5 Oct 2026 09:00:00 -0700");

        var normal = LoadEml(root, "headers-normal.eml");
        Require(normal.To.Count == 2 && normal.Cc.Count == 1, "普通邮件头样本有两个收件人和一个抄送");

        var both = LoadEml(root, "body-html-and-plain.eml");
        Require(both.HtmlBody is not null && both.TextBody is not null, "body-html-and-plain.eml 同时有 HTML 和纯文本");

        var plain = LoadEml(root, "body-plain-only.eml");
        Require(plain.HtmlBody is null && plain.TextBody is not null, "body-plain-only.eml 只有纯文本");

        var cid = LoadEml(root, "cid-inline-image.eml");
        Require(cid.BodyParts.Count(part => part.ContentId is not null) == 2, "cid-inline-image.eml 有两个带 Content-ID 的图片");
        Require(cid.HtmlBody!.Contains("cid:logo@samples.efv.invalid", StringComparison.Ordinal), "cid-inline-image.eml 的 HTML 引用了 cid:logo");
        var cidAttachments = cid.Attachments.OfType<MimePart>().ToList();
        Require(cidAttachments.Count == 1 && cidAttachments[0].FileName == "quarterly-report.docx", "cid-inline-image.eml 只有 docx 一个普通附件");

        var marketing = LoadEml(root, "marketing-remote-content.eml");
        var html = marketing.HtmlBody!;
        Require(html.Split("raw.githubusercontent.com/real-zony/EmailFileViewer/main/samples/assets/").Length - 1 >= 10, "营销邮件的远程资源都指向 samples/assets");
        Require(html.Contains("width=\"600\"", StringComparison.Ordinal) && html.Contains("fonts.googleapis.com", StringComparison.Ordinal) && html.Contains("@font-face", StringComparison.Ordinal), "营销邮件含 600px 表格、远程字体");

        var malicious = LoadEml(root, "html-malicious.eml").HtmlBody!;
        foreach (var token in new[]
                 {
                     "<script", "onerror=", "onload=", "javascript:", "<form", "http-equiv=\"refresh\"", "<iframe", "<object",
                     "window.__TAURI_INTERNALS__", "window.__TAURI__", "window.parent.postMessage", "efv_probe_should_fail",
                 })
        {
            Require(malicious.Contains(token, StringComparison.Ordinal), $"html-malicious.eml 含 {token}");
        }

        var mismatch = LoadEml(root, "link-text-mismatch.eml").HtmlBody!;
        Require(mismatch.Contains("<a href=\"https://example.com/not-paypal\">paypal.com</a>", StringComparison.Ordinal), "link-text-mismatch.eml 含文字为 paypal.com、地址为 https://example.com/not-paypal 的链接");

        var unsafeNames = LoadEml(root, "attachments-unsafe-names.eml").Attachments.OfType<MimePart>().Select(part => part.FileName).ToList();
        foreach (var name in new[] { "../../.bashrc", "CON.txt", "a:b*c?.txt" })
        {
            Require(unsafeNames.Contains(name), $"attachments-unsafe-names.eml 含附件名 {name}");
        }

        var duplicates = LoadEml(root, "attachments-duplicate-names.eml").Attachments.OfType<MimePart>().ToList();
        Require(duplicates.Count == 2 && duplicates.All(part => part.FileName == "report.pdf"), "attachments-duplicate-names.eml 有两个 report.pdf");

        var executables = LoadEml(root, "attachments-executables.eml").Attachments.OfType<MimePart>().Select(part => part.FileName).ToList();
        foreach (var name in new[] { "installer.exe", "run-me.bat", "setup.ps1", "install.sh", "launcher.command" })
        {
            Require(executables.Contains(name), $"attachments-executables.eml 含 {name}");
        }

        var nested = LoadEml(root, "nested-rfc822-two-levels.eml");
        var level2 = ChildMessages(nested).Single();
        var level3 = ChildMessages(level2).Single();
        RequireEqual("Level 3 (innermost) message", level3.Subject, "两层 message/rfc822 的最内层主题");

        var tnefMessage = LoadEml(root, "tnef-winmail-docx.eml");
        var tnefPart = tnefMessage.BodyParts.OfType<MimeKit.Tnef.TnefPart>().Single();
        // ExtractAttachments 除了文件，还会把 TNEF 里的正文作为没有文件名的文本部分一并返回
        var extracted = tnefPart.ExtractAttachments().OfType<MimePart>().Where(part => part.FileName is not null).ToList();
        Require(extracted.Count == 1 && extracted[0].FileName == SpecialSamples.TnefDocxFileName, "winmail.dat 里能解出一个 docx");
        using (var tnefContent = new MemoryStream())
        {
            extracted[0].Content!.DecodeTo(tnefContent);
            Require(tnefContent.ToArray().AsSpan().SequenceEqual(SpecialSamples.TnefDocx()), "winmail.dat 里解出的 docx 与原文件字节一致");
        }

        var withEml = LoadEml(root, "attachment-eml-file.eml");
        Require(withEml.Attachments.Count() == 2, "attachment-eml-file.eml 有两个附件");
    }

    public static void VerifyMsgSamples(string root)
    {
        var rtfOnlyPath = Path.Combine(root, MsgSamples.RtfOnlyFileName);
        using (var storage = OpenMcdf.RootStorage.OpenRead(rtfOnlyPath))
        {
            var names = storage.EnumerateEntries().Select(entry => entry.Name).ToList();
            Require(names.Contains("__substg1.0_10090102"), "body-rtf-only.msg 有压缩的 RTF 正文");
            Require(!names.Any(name => name.StartsWith("__substg1.0_1000", StringComparison.Ordinal) || name.StartsWith("__substg1.0_1013", StringComparison.Ordinal)), "body-rtf-only.msg 没有纯文本和 HTML 正文流");
        }

        using (var rtfOnly = new MsgReader.Outlook.Storage.Message(rtfOnlyPath, FileAccess.Read))
        {
            Require(string.IsNullOrEmpty(rtfOnly.BodyText) && string.IsNullOrEmpty(rtfOnly.BodyHtml), "body-rtf-only.msg 读出来没有文本和 HTML 正文");
            Require(rtfOnly.BodyRtf is not null && rtfOnly.BodyRtf.Contains(MsgSamples.RtfOnlyMarker, StringComparison.Ordinal) && !rtfOnly.BodyRtf.Contains("\\fromhtml", StringComparison.Ordinal), "body-rtf-only.msg 的 RTF 正文不是 HTML 封装");
        }

        var htmlInRtfPath = Path.Combine(root, MsgSamples.HtmlInRtfFileName);
        using (var storage = OpenMcdf.RootStorage.OpenRead(htmlInRtfPath))
        {
            var names = storage.EnumerateEntries().Select(entry => entry.Name).ToList();
            Require(names.Contains("__substg1.0_10090102"), "body-html-in-rtf.msg 有压缩的 RTF 正文");
            Require(!names.Any(name => name.StartsWith("__substg1.0_1000", StringComparison.Ordinal) || name.StartsWith("__substg1.0_1013", StringComparison.Ordinal)), "body-html-in-rtf.msg 没有纯文本和 PR_HTML 正文流");
        }

        using (var htmlInRtf = new MsgReader.Outlook.Storage.Message(htmlInRtfPath, FileAccess.Read))
        {
            Require(htmlInRtf.BodyRtf is not null && htmlInRtf.BodyRtf.Contains("\\fromhtml1", StringComparison.Ordinal), "body-html-in-rtf.msg 的 RTF 带 \\fromhtml1");
            Require(htmlInRtf.BodyHtml is not null && htmlInRtf.BodyHtml.Contains(MsgSamples.HtmlInRtfMarker, StringComparison.Ordinal), "body-html-in-rtf.msg 能还原出 HTML 正文");
        }

        using var nested = new MsgReader.Outlook.Storage.Message(Path.Combine(root, MsgSamples.NestedFileName), FileAccess.Read);
        RequireEqual("Level 1 (outer) MSG with two nested levels", nested.Subject, "嵌套 MSG 的外层主题");
        var level2 = nested.Attachments.OfType<MsgReader.Outlook.Storage.Message>().Single();
        RequireEqual("Level 2 (middle) MSG", level2.Subject, "嵌套 MSG 的中层主题");
        var level3 = level2.Attachments.OfType<MsgReader.Outlook.Storage.Message>().Single();
        RequireEqual("Level 3 (innermost) MSG", level3.Subject, "嵌套 MSG 的最内层主题");
        Require(level3.Attachments.OfType<MsgReader.Outlook.Storage.Attachment>().Single().FileName == "level3-note.txt", "嵌套 MSG 最内层有 level3-note.txt");
    }

    public static void VerifyFormatMismatchAndInvalidSamples(string root)
    {
        byte[] oleSignature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
        byte[] pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

        Require(File.ReadAllBytes(Path.Combine(root, "format-msg-disguised-as-eml.eml")).AsSpan().StartsWith(oleSignature), "format-msg-disguised-as-eml.eml 的内容是 MSG（OLE 签名）");
        Require(File.ReadAllText(Path.Combine(root, "format-eml-disguised-as-msg.msg")).StartsWith("From: ", StringComparison.Ordinal), "format-eml-disguised-as-msg.msg 的内容是 EML");

        var attachedMsg = LoadEml(root, "attachment-msg-file.eml").Attachments.OfType<MimePart>().Single();
        Require(attachedMsg.FileName == "forwarded-message.msg", "attachment-msg-file.eml 的附件是 forwarded-message.msg");

        var invalid = Path.Combine(root, "invalid");
        Require(new FileInfo(Path.Combine(invalid, "empty.eml")).Length == 0, "invalid/empty.eml 是空文件");

        var truncatedMsgPath = Path.Combine(invalid, "truncated.msg");
        Require(File.ReadAllBytes(truncatedMsgPath).AsSpan().StartsWith(oleSignature), "invalid/truncated.msg 保留了 OLE 签名");
        Require(Throws(() => new MsgReader.Outlook.Storage.Message(truncatedMsgPath, FileAccess.Read).Dispose()), "invalid/truncated.msg 无法被 MsgReader 解析");

        Require(File.ReadAllBytes(Path.Combine(invalid, "not-an-email.png")).AsSpan().StartsWith(pngSignature), "invalid/not-an-email.png 是 PNG");
        Require(Throws(() => LoadEml(invalid, "png-disguised-as-eml.eml")), "invalid/png-disguised-as-eml.eml 无法被 MimeKit 解析");
        Require(Throws(() => LoadEml(invalid, "empty.eml")), "invalid/empty.eml 无法被 MimeKit 解析");

        // MimeKit 对截断的 EML 很宽容，不会抛异常；这里只确认文件确实被截断了（没有最后的结束边界）
        var full = File.ReadAllBytes(Path.Combine(root, "attachments-pdf-xlsx-jpg.eml"));
        var truncated = File.ReadAllBytes(Path.Combine(invalid, "truncated-half.eml"));
        Require(truncated.Length == full.Length / 2 && !Encoding.ASCII.GetString(truncated).Contains("------=_efv_01--", StringComparison.Ordinal), "invalid/truncated-half.eml 只剩一半内容，没有结束边界");
    }

    private static bool Throws(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (Exception)
        {
            return true;
        }
    }

    private static TextPart BodyAsText(MimeMessage message)
    {
        return message.Body as TextPart ?? throw new InvalidOperationException("样本自检失败：正文应该是单个文本部分");
    }

    private static IEnumerable<MimeMessage> ChildMessages(MimeMessage message)
    {
        return (message.Body as Multipart ?? throw new InvalidOperationException("样本自检失败：正文应该是 multipart"))
            .OfType<MessagePart>()
            .Select(part => part.Message)
            .OfType<MimeMessage>();
    }

    private static string NormalizeNewLines(string? text) => (text ?? string.Empty).ReplaceLineEndings("\n");
}
