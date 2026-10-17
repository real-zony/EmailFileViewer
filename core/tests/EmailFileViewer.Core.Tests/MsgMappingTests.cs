using EmailFileViewer.Core.Models;

namespace EmailFileViewer.Core.Tests;

/// <summary>
/// MSG 到文档模型的映射：邮件头、日期换算和正文（包括 RTF 封装的 HTML、纯 RTF 转 HTML）。
/// </summary>
public class MsgMappingTests
{
    [Fact]
    [Trait("Scenario", "email-viewing / 显示邮件头 / 普通邮件（MSG）")]
    public async Task RtfOnlyMsg_HeadersHaveNameAndAddress()
    {
        using var parsed = await MailAssert.ParseSampleAsync("body-rtf-only.msg");
        var document = parsed.Document;

        MailAssert.AddressesEqual(document.From, ("Alice Zhang", "alice.zhang@example.com"));
        MailAssert.AddressesEqual(document.To, ("Bob Li", "bob.li@example.org"));
        MailAssert.AddressesEqual(document.Cc, ("Dave Chen", "dave.chen@example.com"));
        Assert.Equal("RTF-only body sample", document.Subject);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 显示邮件头 / 普通邮件（MSG 的收件人、抄送、密送）")]
    public async Task ToCcBcc_AreSeparatedByRecipientType_NameIsNullWhenMissing()
    {
        using var parsed = await MailAssert.ParseSampleAsync("EmailWithAttachments.msg");
        var document = parsed.Document;

        MailAssert.AddressesEqual(document.From, ("Kees", "peterpan@neverland.com"));
        MailAssert.AddressesEqual(document.To, (null, "crocodile@neverland.com"));
        MailAssert.AddressesEqual(document.Cc, (null, "tinkerbel@neverland.com"));
        MailAssert.AddressesEqual(document.Bcc, (null, "wendy@neverland.com"));
        Assert.Empty(document.ReplyTo);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 缺少某个邮件头 / MSG 没有发件人、日期和主题")]
    public async Task MsgWithoutSenderDateAndSubject_FieldsAreEmptyOrNull()
    {
        using var parsed = await MailAssert.ParseSampleAsync("RtfWithShortRussianString.msg");
        var document = parsed.Document;

        Assert.Empty(document.From);
        Assert.Empty(document.To);
        Assert.Null(document.Subject);
        Assert.Null(document.SentAtUtc);
        Assert.Null(document.DateHeaderRaw);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 日期按本地时区显示 / MSG 的发送时间换算成 UTC")]
    public async Task SentOn_IsConvertedToUtcRegardlessOfMachineTimeZone()
    {
        using var parsed = await MailAssert.ParseSampleAsync("body-rtf-only.msg");

        // MsgReader 返回的是带本机时区偏移的时间，换算后的时间点必须是固定的 UTC 值
        var sentAtUtc = Assert.IsType<DateTimeOffset>(parsed.Document.SentAtUtc);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 2, 0, 0, TimeSpan.Zero), sentAtUtc);
        Assert.Equal(TimeSpan.Zero, sentAtUtc.Offset);
        Assert.Null(parsed.Document.DateHeaderRaw);
    }

    [Theory]
    [InlineData("EmailWithAttachments.msg", 2016, 12, 3, 18, 8, 54)]
    [InlineData("nested-msg-two-levels.msg", 2026, 9, 30, 10, 0, 0)]
    [Trait("Scenario", "email-viewing / 日期按本地时区显示 / MSG 的发送时间换算成 UTC")]
    public async Task SentOn_OtherSamples_AreFixedUtcInstants(string sampleName, int year, int month, int day, int hour, int minute, int second)
    {
        using var parsed = await MailAssert.ParseSampleAsync(sampleName);

        var expected = new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.Zero);
        Assert.Equal(expected, parsed.Document.SentAtUtc);
        Assert.Equal(TimeSpan.Zero, parsed.Document.SentAtUtc!.Value.Offset);
    }

    [Theory]
    [InlineData("HtmlSampleEmailWithAttachment.msg", "Mon, 23 Oct 2017 10:05:24 +0100", 2017, 10, 23, 9, 5, 24)]
    [InlineData("RtfSampleEmail.msg", "Mon, 23 Oct 2017 08:31:09 +0100", 2017, 10, 23, 7, 31, 9)]
    [InlineData("EmailWithMsgAttachment.msg", "Tue, 22 Jul 2025 12:15:09 +0000", 2025, 7, 22, 12, 15, 9)]
    [Trait("Scenario", "email-viewing / 日期按本地时区显示 / MSG 带原始传输头时保留原始 Date")]
    public async Task MsgWithTransportHeaders_KeepsRawDateHeader(string sampleName, string expectedRaw, int year, int month, int day, int hour, int minute, int second)
    {
        using var parsed = await MailAssert.ParseSampleAsync(sampleName);

        Assert.Equal(expectedRaw, parsed.Document.DateHeaderRaw);
        Assert.Equal(new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.Zero), parsed.Document.SentAtUtc);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 正文格式选择 / 只有 RTF 正文的 MSG")]
    public async Task RtfOnlyMsg_BodyIsConvertedToHtmlKeepingParagraphsAndFormatting()
    {
        using var parsed = await MailAssert.ParseSampleAsync("body-rtf-only.msg");
        var body = parsed.Document.Body;

        Assert.Equal(BodyFormat.Rtf, body.SourceFormat);
        var html = Assert.IsType<string>(body.Html);
        Assert.Contains("RTF-ONLY-BODY", html);
        Assert.Contains("END-OF-RTF-BODY", html);

        // 每个 RTF 段落 (\par) 对应一个 <p>，一共 8 段
        Assert.Equal(8, CountOccurrences(html, "<p "));
        Assert.Contains("<strong>bold</strong>", html);
        Assert.Contains("<em>italic</em>", html);
        Assert.Contains("<u>underlined</u>", html);
        Assert.Contains("href=\"https://example.com/rtf\"", html);
        Assert.Contains("First bullet item", html);

        // 输出里不能残留 RTF 控制字
        Assert.DoesNotContain("\\rtf", html);
        Assert.DoesNotContain("\\par", html);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 正文格式选择 / MSG 中封装在 RTF 里的 HTML 还原成 HTML（MsgKit 生成）")]
    public async Task HtmlEncapsulatedInRtf_IsRestoredAsHtml()
    {
        using var parsed = await MailAssert.ParseSampleAsync("body-html-in-rtf.msg");
        var body = parsed.Document.Body;

        Assert.Equal(BodyFormat.Html, body.SourceFormat);
        var html = Assert.IsType<string>(body.Html);
        Assert.Contains("<h1>HTML-IN-RTF-BODY</h1>", html);
        Assert.Contains("中文段落", html);
        Assert.Contains("<table", html);
        Assert.Contains("END-OF-HTML-IN-RTF-BODY", html);
        Assert.DoesNotContain("\\htmltag", html);
    }

    [Theory]
    [InlineData("HtmlSampleEmailWithAttachment.msg")]
    [InlineData("EmailWithMsgAttachment.msg")]
    [Trait("Scenario", "email-viewing / 正文格式选择 / MSG 中封装在 RTF 里的 HTML 还原成 HTML（Outlook 生成）")]
    public async Task OutlookHtmlEncapsulatedInRtf_IsRestoredAsHtml(string sampleName)
    {
        using var parsed = await MailAssert.ParseSampleAsync(sampleName);
        var body = parsed.Document.Body;

        Assert.Equal(BodyFormat.Html, body.SourceFormat);
        var html = Assert.IsType<string>(body.Html);
        Assert.Contains("<html", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\\rtf", html);
        Assert.DoesNotContain("\\fromhtml", html);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 正文格式选择 / MSG 有 PR_HTML 正文")]
    public async Task MsgWithHtmlStream_BodyIsHtml()
    {
        using var parsed = await MailAssert.ParseSampleAsync("EmailWithAttachments.msg");

        Assert.Equal(BodyFormat.Html, parsed.Document.Body.SourceFormat);
        Assert.Contains("Hello Neverland html", parsed.Document.Body.Html);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 正文格式选择 / MSG 同时有 RTF 和纯文本时用 RTF")]
    public async Task MsgWithRtfAndPlainText_RtfWins()
    {
        using var parsed = await MailAssert.ParseSampleAsync("RtfSampleEmail.msg");

        Assert.Equal(BodyFormat.Rtf, parsed.Document.Body.SourceFormat);
        Assert.Contains("BOOK ONE: 1805", parsed.Document.Body.Html);
        Assert.DoesNotContain("\\rtf", parsed.Document.Body.Html);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 正文格式选择 / Outlook 给纯文本邮件生成的 \\fromtext 封装 RTF 按纯文本显示")]
    public async Task MsgWithFromTextRtf_BodyIsPlainText()
    {
        using var parsed = await MailAssert.ParseSampleAsync("TxtSampleEmail.msg");

        var text = MailAssert.PlainTextOf(parsed.Document);
        Assert.StartsWith("BOOK ONE: 1805", text);
        Assert.Contains("CHAPTER I", text);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
