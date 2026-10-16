using EmailFileViewer.Core.Models;
using EmailFileViewer.Core.Parsing;

namespace EmailFileViewer.Core.Tests;

/// <summary>
/// 对应规格 email-viewing 中的“正文格式选择”：HTML、RTF、纯文本的优先级，RTF 转换，纯文本转义并保留换行。
/// </summary>
public class BodySelectionTests
{
    private const string SimpleRtf = @"{\rtf1\ansi\deff0{\fonttbl{\f0 Arial;}}\f0\fs22 RTF-PARAGRAPH-ONE\par RTF-PARAGRAPH-TWO\par}";

    [Fact]
    [Trait("Scenario", "email-viewing / 正文格式选择 / 同时有 HTML 和纯文本")]
    public async Task HtmlAndPlainAlternatives_HtmlIsSelected()
    {
        using var parsed = await MailAssert.ParseSampleAsync("body-html-and-plain.eml");
        var body = parsed.Document.Body;

        Assert.Equal(BodyFormat.Html, body.SourceFormat);
        Assert.Contains("HTML-VERSION", body.Html);
        Assert.Contains("<b>HTML</b>", body.Html);
        Assert.DoesNotContain("PLAIN-TEXT-VERSION", body.Html);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 正文格式选择 / 只有 RTF 正文的 MSG")]
    public async Task RtfOnlyMsg_BodyIsRtfConvertedToHtml()
    {
        using var parsed = await MailAssert.ParseSampleAsync("body-rtf-only.msg");
        var body = parsed.Document.Body;

        Assert.Equal(BodyFormat.Rtf, body.SourceFormat);
        Assert.Contains("RTF-ONLY-BODY", body.Html);
        Assert.Contains("<p ", body.Html);
        Assert.DoesNotContain("\\rtf", body.Html);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 正文格式选择 / MSG 同时有 HTML（封装在 RTF 中）和纯文本")]
    public async Task MsgWithHtmlAndPlainText_HtmlIsSelected()
    {
        using var parsed = await MailAssert.ParseSampleAsync("EmailWithAttachments.msg");

        Assert.Equal(BodyFormat.Html, parsed.Document.Body.SourceFormat);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 正文格式选择 / EML 里的 RTF 部分优先于纯文本")]
    public async Task EmlWithRtfAndPlainAlternatives_RtfIsSelected()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteMail(
            "rtf-and-plain.eml",
            $$"""
            From: a@example.com
            Subject: rtf and plain
            MIME-Version: 1.0
            Content-Type: multipart/alternative; boundary="b1"

            --b1
            Content-Type: text/plain; charset=utf-8

            PLAIN-VERSION
            --b1
            Content-Type: text/rtf; charset=us-ascii

            {{SimpleRtf}}
            --b1--
            """);

        using var parsed = await EmailParser.ParseFileAsync(path);

        Assert.Equal(BodyFormat.Rtf, parsed.Document.Body.SourceFormat);
        Assert.Contains("RTF-PARAGRAPH-ONE", parsed.Document.Body.Html);
        Assert.Contains("RTF-PARAGRAPH-TWO", parsed.Document.Body.Html);
        Assert.DoesNotContain("PLAIN-VERSION", parsed.Document.Body.Html);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 正文格式选择 / 三种格式都有时 HTML 优先")]
    public async Task HtmlRtfAndPlain_HtmlWins()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteMail(
            "all-three.eml",
            $$"""
            From: a@example.com
            Subject: all three
            MIME-Version: 1.0
            Content-Type: multipart/alternative; boundary="b1"

            --b1
            Content-Type: text/plain; charset=utf-8

            PLAIN-VERSION
            --b1
            Content-Type: text/rtf; charset=us-ascii

            {{SimpleRtf}}
            --b1
            Content-Type: text/html; charset=utf-8

            <p>HTML-VERSION</p>
            --b1--
            """);

        using var parsed = await EmailParser.ParseFileAsync(path);

        Assert.Equal(BodyFormat.Html, parsed.Document.Body.SourceFormat);
        Assert.Contains("HTML-VERSION", parsed.Document.Body.Html);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 正文格式选择 / HTML 部分只有空白时退到纯文本")]
    public async Task BlankHtmlPart_FallsBackToPlainText()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteMail(
            "blank-html.eml",
            """
            From: a@example.com
            Subject: blank html
            MIME-Version: 1.0
            Content-Type: multipart/alternative; boundary="b1"

            --b1
            Content-Type: text/plain; charset=utf-8

            PLAIN-VERSION
            --b1
            Content-Type: text/html; charset=utf-8

             
            --b1--
            """);

        using var parsed = await EmailParser.ParseFileAsync(path);

        Assert.Equal("PLAIN-VERSION", MailAssert.PlainTextOf(parsed.Document));
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 正文格式选择 / 纯文本正文保留换行")]
    public async Task PlainTextBody_KeepsLineBreaksAndWhitespace()
    {
        using var parsed = await MailAssert.ParseSampleAsync("body-plain-only.eml");
        var text = MailAssert.PlainTextOf(parsed.Document);

        // 两个连续空行、空格缩进、制表符缩进都要原样保留
        Assert.Contains("Line two of the same paragraph (a hard line break inside the paragraph).\n\n\nThird paragraph comes after TWO blank lines.\n", text);
        Assert.Contains("\n    Indented line with four spaces.\n", text);
        Assert.Contains("\n\tIndented line with a tab.\n", text);
        Assert.Contains("中文段落，用来确认 UTF-8 纯文本也能正确显示。", text);

        var html = parsed.Document.Body.Html!;
        Assert.Contains("white-space:pre-wrap", html);
        Assert.StartsWith("<pre ", html);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 正文格式选择 / 纯文本里的 HTML 特殊字符被转义")]
    public async Task PlainTextBody_SpecialCharactersAreEscaped()
    {
        using var parsed = await MailAssert.ParseSampleAsync("body-plain-only.eml");
        var html = parsed.Document.Body.Html!;

        Assert.Contains("&lt;b&gt;not bold&lt;/b&gt; &amp; ", html);
        Assert.Contains("&gt; quoted line one", html);
        Assert.Contains("a=1&amp;b=2", html);
        Assert.DoesNotContain("<b>", html);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 正文格式选择 / 纯文本里的脚本标签不会变成标签")]
    public async Task PlainTextBodyWithScriptTag_IsNotExecutableMarkup()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteMail(
            "script-in-plain.eml",
            """
            From: a@example.com
            Subject: script in plain text
            Content-Type: text/plain; charset=utf-8

            <script>alert(1)</script><img src=x onerror=alert(2)>
            """);

        using var parsed = await EmailParser.ParseFileAsync(path);

        var html = parsed.Document.Body.Html!;
        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("<img", html);
        Assert.Equal("<script>alert(1)</script><img src=x onerror=alert(2)>", MailAssert.PlainTextOf(parsed.Document));
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 正文格式选择 / 邮件没有正文")]
    public async Task MailWithoutBody_BodyIsNone()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteMail("no-body.eml", "From: a@example.com\nSubject: nothing\nContent-Type: text/plain\n\n");

        using var parsed = await EmailParser.ParseFileAsync(path);

        Assert.Equal(BodyFormat.None, parsed.Document.Body.SourceFormat);
        Assert.Null(parsed.Document.Body.Html);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 正文格式选择 / 加密邮件没有可显示的正文")]
    public async Task EncryptedMail_BodyIsNone()
    {
        using var parsed = await MailAssert.ParseSampleAsync("smime-encrypted.eml");

        Assert.Equal(BodyFormat.None, parsed.Document.Body.SourceFormat);
        Assert.Null(parsed.Document.Body.Html);
    }

    [Theory]
    [InlineData("a < b && c > d", "a &lt; b &amp;&amp; c &gt; d")]
    [InlineData("\"quoted\" and 'single'", "\"quoted\" and 'single'")]
    [InlineData("line1\r\nline2\rline3", "line1\nline2\nline3")]
    [InlineData("&amp; stays escaped once", "&amp;amp; stays escaped once")]
    public void PlainTextHtml_Convert_EscapesAndNormalizesLineEndings(string text, string expectedInner)
    {
        var html = PlainTextHtml.Convert(text);

        Assert.EndsWith("</pre>", html);
        Assert.Contains(">" + expectedInner + "</pre>", html);
    }

    [Fact]
    public void PlainTextHtml_Convert_TextStartingWithNewline_KeepsTheLeadingBlankLine()
    {
        // 浏览器会丢掉 <pre> 开始标签后紧跟的第一个换行，所以要多补一个，才能保住原文开头的空行
        var html = PlainTextHtml.Convert("\nsecond line");

        Assert.EndsWith(">\n\nsecond line</pre>", html);
    }
}
