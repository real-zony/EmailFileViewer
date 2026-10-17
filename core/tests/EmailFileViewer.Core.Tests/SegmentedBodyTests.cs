using System.Text.RegularExpressions;
using EmailFileViewer.Core.Models;

namespace EmailFileViewer.Core.Tests;

/// <summary>
/// 对应规格 email-viewing 中的“正文格式选择”和 email-attachments 中的“列出附件”：
/// Apple Mail 这类客户端会在 multipart/mixed 里交替放多个正文部分和内嵌图片，要按顺序拼成一份正文，夹在中间的图片内嵌进去、不列为附件。
/// </summary>
public partial class SegmentedBodyTests
{
    private const string TinyPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    [GeneratedRegex(@"<img src=""data:(?<type>[^;""]+);base64,(?<data>[A-Za-z0-9+/=]+)""")]
    private static partial Regex DataImage();

    [Fact]
    [Trait("Scenario", "email-viewing / 正文格式选择 / Apple Mail 式的分段正文：三段文字按顺序出现，图片内嵌在中间")]
    public async Task AppleMailStyleSample_HasAllThreeSegmentsInOrderWithImagesBetween()
    {
        using var parsed = await MailAssert.ParseSampleAsync("body-multipart-mixed-inline-images.eml");
        var document = parsed.Document;
        var html = Assert.IsType<string>(document.Body.Html);

        Assert.Equal(BodyFormat.Html, document.Body.SourceFormat);
        Assert.False(document.HasRemoteContent);
        Assert.DoesNotContain("PLAIN-ALTERNATIVE", html);

        var one = html.IndexOf("SEGMENT-ONE", StringComparison.Ordinal);
        var jpeg = html.IndexOf("data:image/jpeg;base64,", StringComparison.Ordinal);
        var two = html.IndexOf("SEGMENT-TWO", StringComparison.Ordinal);
        var png = html.IndexOf("data:image/png;base64,", StringComparison.Ordinal);
        var three = html.IndexOf("SEGMENT-THREE", StringComparison.Ordinal);
        Assert.True(0 <= one && one < jpeg && jpeg < two && two < png && png < three, $"顺序不对：{one} {jpeg} {two} {png} {three}");

        // 第二段的中文没有乱码
        Assert.Contains("两张图片之间的文字", html);
    }

    [Fact]
    [Trait("Scenario", "email-attachments / 列出附件 / 夹在分段正文中间的内嵌图片不列为附件，真正的附件照常列出")]
    public async Task AppleMailStyleSample_InlineImagesAreNotListedButTheRealAttachmentIs()
    {
        using var parsed = await MailAssert.ParseSampleAsync("body-multipart-mixed-inline-images.eml");

        var attachment = Assert.Single(parsed.Document.Attachments);
        Assert.Equal("itinerary.pdf", attachment.FileName);
        Assert.Equal("application/pdf", attachment.MimeType);
        var bytes = await MailAssert.ReadAttachmentAsync(parsed, attachment.Id);
        Assert.Equal(attachment.Size, bytes.Length);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 正文格式选择 / 分段正文里的图片是原图的字节")]
    public async Task AppleMailStyleSample_EmbeddedImagesAreTheOriginalBytes()
    {
        using var parsed = await MailAssert.ParseSampleAsync("body-multipart-mixed-inline-images.eml");

        var images = DataImage().Matches(parsed.Document.Body.Html!);

        Assert.Equal(2, images.Count);
        Assert.Equal("image/jpeg", images[0].Groups["type"].Value);
        Assert.Equal([0xFF, 0xD8, 0xFF], Convert.FromBase64String(images[0].Groups["data"].Value)[..3]);
        Assert.Equal("image/png", images[1].Groups["type"].Value);
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], Convert.FromBase64String(images[1].Groups["data"].Value)[..4]);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 正文格式选择 / 分段正文只保留一份文档结构（不重复 html、head、body）")]
    public async Task AppleMailStyleSample_BodyIsAFragmentWithoutRepeatedDocumentTags()
    {
        using var parsed = await MailAssert.ParseSampleAsync("body-multipart-mixed-inline-images.eml");
        var html = parsed.Document.Body.Html!;

        Assert.DoesNotContain("<html", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<body", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<head", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 正文格式选择 / 顶层就是 multipart/mixed 的分段正文")]
    public async Task TopLevelMixed_HtmlImageHtml_JoinsTheSegmentsAndEmbedsTheImage()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteMail("mixed.eml", MixedMail(
            Html("<p>FIRST-PART</p>"),
            InlineImage("a.png"),
            Html("<p>SECOND-PART</p>")));

        using var parsed = await EmailParser.ParseFileAsync(path);
        var html = parsed.Document.Body.Html!;

        Assert.Equal(BodyFormat.Html, parsed.Document.Body.SourceFormat);
        Assert.True(html.IndexOf("FIRST-PART", StringComparison.Ordinal) < html.IndexOf("data:image/png", StringComparison.Ordinal));
        Assert.True(html.IndexOf("data:image/png", StringComparison.Ordinal) < html.IndexOf("SECOND-PART", StringComparison.Ordinal));
        Assert.Empty(parsed.Document.Attachments);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 正文格式选择 / 分段正文的纯文本版：先转成 HTML（转义并保留换行）再拼接")]
    public async Task PlainTextSegments_AreConvertedToHtmlAndJoined()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteMail("plain-mixed.eml", MixedMail(
            Plain("first line\nuses <b>tags</b> & more"),
            InlineImage("a.png"),
            Plain("last line")));

        using var parsed = await EmailParser.ParseFileAsync(path);
        var html = parsed.Document.Body.Html!;

        Assert.Equal(BodyFormat.PlainText, parsed.Document.Body.SourceFormat);
        Assert.Equal(2, Regex.Matches(html, "<pre\\b").Count);
        Assert.Contains("first line\nuses &lt;b&gt;tags&lt;/b&gt; &amp; more", html);
        Assert.DoesNotContain("<b>", html);
        Assert.True(html.IndexOf("first line", StringComparison.Ordinal) < html.IndexOf("data:image/png", StringComparison.Ordinal));
        Assert.True(html.IndexOf("data:image/png", StringComparison.Ordinal) < html.IndexOf("last line", StringComparison.Ordinal));
        Assert.Empty(parsed.Document.Attachments);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 正文格式选择 / 分段正文最终的 HTML 也要经过清理")]
    public async Task SegmentedBody_IsSanitizedAsAWhole()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteMail("hostile.eml", MixedMail(
            Html("<p>SAFE-ONE</p>", head: "<style>.keep{color:red}</style><title>drop me</title>"),
            InlineImage("a.png"),
            Html("<p onclick=\"evil()\">SAFE-TWO</p><script>alert(1)</script><a href=\"javascript:evil()\">x</a>"),
            Html("<img src=\"https://example.com/tracker.gif\">")));

        using var parsed = await EmailParser.ParseFileAsync(path);
        var html = parsed.Document.Body.Html!;

        Assert.Contains("SAFE-ONE", html);
        Assert.Contains("SAFE-TWO", html);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onclick", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
        Assert.True(parsed.Document.HasRemoteContent);

        // head 里的样式保留（放在最前面），标题不会变成正文里的文字
        Assert.Contains(".keep{color:red}", html);
        Assert.DoesNotContain("drop me", html);
    }

    [Fact]
    [Trait("Scenario", "email-attachments / 列出附件 / 没有被文本夹住的图片仍然是附件")]
    public async Task ImagesNotBetweenTexts_RemainAttachments()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteMail("edges.eml", MixedMail(
            InlineImage("leading.png"),
            Html("<p>ONE</p>"),
            InlineImage("between.png"),
            Html("<p>TWO</p>"),
            InlineImage("trailing.png")));

        using var parsed = await EmailParser.ParseFileAsync(path);

        Assert.Single(Regex.Matches(parsed.Document.Body.Html!, "data:image/png"));
        Assert.Equal(new string?[] { "leading.png", "trailing.png" }, parsed.Document.Attachments.Select(a => a.FileName).ToArray());
    }

    [Fact]
    [Trait("Scenario", "email-attachments / 列出附件 / 只有一个正文和一张 inline 图片时，图片仍是附件（行为不变）")]
    public async Task SingleBodyWithInlineImage_KeepsTheImageAsAttachment()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteMail("single.eml", MixedMail(Html("<p>ONLY-PART</p>"), InlineImage("photo.png")));

        using var parsed = await EmailParser.ParseFileAsync(path);

        Assert.DoesNotContain("data:image", parsed.Document.Body.Html);
        Assert.Equal("photo.png", Assert.Single(parsed.Document.Attachments).FileName);
    }

    [Fact]
    [Trait("Scenario", "email-attachments / 列出附件 / 声明为附件的图片、PDF 和带文件名的文本不并入正文")]
    public async Task AttachmentsBetweenSegments_AreNotMergedIntoTheBody()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteMail("between.eml", MixedMail(
            Html("<p>ONE</p>"),
            AttachedImage("attached.png"),
            Raw("application/pdf", "report.pdf", "inline", "JVBERi0="),
            Raw("text/plain; charset=utf-8", "notes.txt", "attachment", "NOTES-TEXT"),
            Raw("text/html; charset=utf-8", "ad.html", "inline", "<p>AD-HTML</p>"),
            Html("<p>TWO</p>")));

        using var parsed = await EmailParser.ParseFileAsync(path);
        var html = parsed.Document.Body.Html!;

        Assert.Contains("ONE", html);
        Assert.Contains("TWO", html);
        Assert.DoesNotContain("data:image", html);
        Assert.DoesNotContain("NOTES-TEXT", html);
        Assert.DoesNotContain("AD-HTML", html);
        Assert.Equal(
            new string?[] { "attached.png", "report.pdf", "notes.txt", "ad.html" },
            parsed.Document.Attachments.Select(a => a.FileName).ToArray());
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 正文格式选择 / 邮件列表在正文后面追加的页脚（第二个 text/plain）一起显示")]
    public async Task TwoPlainTextParts_AreConcatenated()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteMail("footer.eml", MixedMail(Plain("MAIN-BODY"), Plain("-- \nLIST-FOOTER")));

        using var parsed = await EmailParser.ParseFileAsync(path);
        var html = parsed.Document.Body.Html!;

        Assert.Equal(BodyFormat.PlainText, parsed.Document.Body.SourceFormat);
        Assert.True(html.IndexOf("MAIN-BODY", StringComparison.Ordinal) < html.IndexOf("LIST-FOOTER", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 正文格式选择 / 每个备选里各自挑一个：纯文本备选不影响分段的 HTML")]
    public async Task AlternativeWithSegmentedMixed_PrefersTheSegmentedHtml()
    {
        using var temp = new TempDirectory();
        var mixed = MixedMail(Html("<p>H-ONE</p>"), InlineImage("a.png"), Html("<p>H-TWO</p>"), boundary: "inner", wrapInMessage: false);
        var path = temp.WriteMail("alternative.eml", $"""
            From: a@example.com
            Subject: apple style
            MIME-Version: 1.0
            Content-Type: multipart/alternative; boundary="outer"

            --outer
            Content-Type: text/plain

            PLAIN-ALTERNATIVE
            --outer
            {mixed}
            --outer--
            """);

        using var parsed = await EmailParser.ParseFileAsync(path);

        Assert.Equal(BodyFormat.Html, parsed.Document.Body.SourceFormat);
        Assert.Contains("H-ONE", parsed.Document.Body.Html);
        Assert.Contains("H-TWO", parsed.Document.Body.Html);
        Assert.DoesNotContain("PLAIN-ALTERNATIVE", parsed.Document.Body.Html);
        Assert.Empty(parsed.Document.Attachments);
    }

    // ---- 构造邮件的辅助方法 ----

    private static string MixedMail(params string[] parts) => MixedMail(parts, "b", wrapInMessage: true);

    private static string MixedMail(string part1, string part2, string part3, string boundary, bool wrapInMessage) =>
        MixedMail([part1, part2, part3], boundary, wrapInMessage);

    private static string MixedMail(IReadOnlyList<string> parts, string boundary, bool wrapInMessage)
    {
        var body = $"Content-Type: multipart/mixed; boundary=\"{boundary}\"\n\n"
            + string.Concat(parts.Select(part => $"--{boundary}\n{part}\n"))
            + $"--{boundary}--";

        return wrapInMessage ? "From: a@example.com\nSubject: segments\nMIME-Version: 1.0\n" + body : body;
    }

    private static string Html(string content, string head = "") =>
        $"Content-Type: text/html; charset=utf-8\n\n<html><head>{head}</head><body style=\"margin:0\">{content}</body></html>";

    private static string Plain(string content) => $"Content-Type: text/plain; charset=utf-8\n\n{content}";

    private static string InlineImage(string fileName) =>
        $"Content-Type: image/png\nContent-Disposition: inline; filename={fileName}\nContent-Transfer-Encoding: base64\n\n{TinyPng}";

    private static string AttachedImage(string fileName) =>
        $"Content-Type: image/png\nContent-Disposition: attachment; filename={fileName}\nContent-Transfer-Encoding: base64\n\n{TinyPng}";

    private static string Raw(string contentType, string fileName, string disposition, string body) =>
        $"Content-Type: {contentType}\nContent-Disposition: {disposition}; filename={fileName}\n\n{body}";
}
