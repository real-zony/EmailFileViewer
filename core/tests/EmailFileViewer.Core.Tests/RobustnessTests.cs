using System.Text;
using EmailFileViewer.Core.Models;
using EmailFileViewer.Core.Parsing;

namespace EmailFileViewer.Core.Tests;

/// <summary>
/// 第一批遗留问题的补完：开头有空行的 EML、RTF 转换失败时回退到纯文本、修好的 RTF 样本。
/// </summary>
public class RobustnessTests
{
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n\r\n")]
    [InlineData("  \t\r\n\r\n   \r\n")]
    [InlineData("﻿")]
    [InlineData("﻿\r\n\r\n")]
    [Trait("Scenario", "email-opening / 按内容识别文件格式 / EML 开头有空行（可能还有 BOM）")]
    public async Task LeadingBlankLinesAndBom_AreSkippedBeforeParsing(string prefix)
    {
        using var temp = new TempDirectory();
        var path = temp.WriteMail(
            "leading-blank.eml",
            prefix + "From: Alice <alice@example.com>\nSubject: after the blank lines\nDate: Mon, 5 Oct 2026 09:00:00 -0700\n\nbody text\n");

        using var parsed = await EmailParser.ParseFileAsync(path);

        Assert.Equal("after the blank lines", parsed.Document.Subject);
        MailAssert.AddressesEqual(parsed.Document.From, ("Alice", "alice@example.com"));
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 16, 0, 0, TimeSpan.Zero), parsed.Document.SentAtUtc);
        Assert.Equal("body text\n", MailAssert.PlainTextOf(parsed.Document));
    }

    [Fact]
    [Trait("Scenario", "email-opening / 按内容识别文件格式 / 只有空行的文件仍然是不支持的格式")]
    public async Task OnlyBlankLines_StillUnsupported()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteMail("blank-only.eml", "\n\n\n");

        await MailAssert.ParseFailsAsync(path, EmailParseErrorKind.UnsupportedFormat);
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("From: a@b.c", 0)]
    [InlineData("\r\nFrom: a@b.c", 2)]
    [InlineData("\n\n  \nFrom: a@b.c", 5)]
    [InlineData("   \t\r\nFrom: a@b.c", 6)]
    [InlineData("\n\n\n", 0)]
    public void GetMimeStartOffset_SkipsBlankLines(string text, int expected)
    {
        Assert.Equal(expected, EmailFormatDetector.GetMimeStartOffset(Encoding.ASCII.GetBytes(text)));
    }

    [Fact]
    public void GetMimeStartOffset_SkipsBomAndTheBlankLinesAfterIt()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. "\r\nFrom: a@b.c"u8.ToArray()];

        Assert.Equal(5, EmailFormatDetector.GetMimeStartOffset(bytes));
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 正文格式选择 / RTF 转换失败时回退到纯文本")]
    public void RtfConversionFailure_FallsBackToPlainText()
    {
        var built = EmailBodyBuilder.Build(
            () => null,
            () => @"{\rtf1 broken",
            () => "plain <fallback>",
            resolver: null,
            convertRtf: _ => throw new InvalidOperationException("模拟的转换失败"));

        Assert.Equal(BodyFormat.PlainText, built.Body.SourceFormat);
        Assert.Contains("plain &lt;fallback&gt;", built.Body.Html);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 正文格式选择 / RTF 转换失败而且没有纯文本时判为已损坏")]
    public void RtfConversionFailure_WithoutPlainText_IsCorrupted()
    {
        var failure = new InvalidOperationException("模拟的转换失败");

        var exception = Assert.Throws<EmailParseException>(() => EmailBodyBuilder.Build(
            () => null,
            () => @"{\rtf1 broken",
            () => "  ",
            resolver: null,
            convertRtf: _ => throw failure));

        Assert.Equal(EmailParseErrorKind.Corrupted, exception.Kind);
        Assert.Same(failure, exception.InnerException);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 正文格式选择 / RTF 转换成功时不会去读纯文本")]
    public void RtfConversionSuccess_DoesNotReadPlainText()
    {
        var built = EmailBodyBuilder.Build(
            () => null,
            () => @"{\rtf1 ok}",
            () => throw new InvalidOperationException("不应该读纯文本"),
            resolver: null,
            convertRtf: _ => "<p>converted</p>");

        Assert.Equal(BodyFormat.Rtf, built.Body.SourceFormat);
        Assert.Equal("<p>converted</p>", built.Body.Html);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 正文格式选择 / RTF 转换取消时不回退，取消异常照常抛出")]
    public void RtfConversionCancellation_IsNotSwallowed()
    {
        Assert.Throws<OperationCanceledException>(() => EmailBodyBuilder.Build(
            () => null,
            () => @"{\rtf1 x}",
            () => "plain",
            resolver: null,
            convertRtf: _ => throw new OperationCanceledException()));
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 正文格式选择 / 只有 RTF 正文的 MSG，RTF 里的 \\uN? 中文转义被正确还原")]
    public async Task RtfOnlyMsg_ChineseUnicodeEscapesAreRestored()
    {
        using var parsed = await MailAssert.ParseSampleAsync("body-rtf-only.msg");

        Assert.Equal(BodyFormat.Rtf, parsed.Document.Body.SourceFormat);
        Assert.Contains("Chinese text: 中文段落。", parsed.Document.Body.Html);
    }
}
