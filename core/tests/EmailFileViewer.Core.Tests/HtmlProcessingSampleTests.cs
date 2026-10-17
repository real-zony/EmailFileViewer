using System.Text.RegularExpressions;
using EmailFileViewer.Core.Models;
using MimeKit.Text;

namespace EmailFileViewer.Core.Tests;

/// <summary>
/// 任务 3.8 的样本验收：恶意 HTML、营销邮件、链接文字与地址不符、内嵌图片。
/// </summary>
public partial class HtmlProcessingSampleTests
{
    [GeneratedRegex(@"MARKER-(START|END)")]
    private static partial Regex Marker();

    [GeneratedRegex(@"\son[a-z]+\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex EventAttribute();

    // 属性值或 url() 里以脚本协议开头（正文里“javascript: link”这样的说明文字不算）
    [GeneratedRegex(@"(=|\()\s*[""']?\s*(javascript|vbscript):", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptUrlValue();

    [Fact]
    [Trait("Scenario", "email-viewing / 正文在沙箱中渲染 / 正文包含脚本（html-malicious.eml）")]
    public async Task MaliciousSample_HasNoScriptEventAttributeDangerousUrlOrForbiddenElement()
    {
        using var parsed = await MailAssert.ParseSampleAsync("html-malicious.eml");
        var body = parsed.Document.Body;

        Assert.Equal(BodyFormat.Html, body.SourceFormat);
        var html = Assert.IsType<string>(body.Html);
        SafeHtml.AssertSafe(html);

        // 字符串层面再核对一遍：探针里用到的每一种危险写法都不能出现（正文里写着“onclick”的说明文字不算，只看属性写法）
        foreach (var forbidden in new[]
                 {
                     "<script", "</script", "<iframe", "<object", "<embed", "<base", "http-equiv",
                     "srcdoc", "__TAURI", "postMessage", "efv_probe_should_fail", "<!--",
                 })
        {
            Assert.DoesNotContain(forbidden, html, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotMatch(EventAttribute(), html);
        Assert.DoesNotMatch(ScriptUrlValue(), html);

        // 正文的其余部分正常显示：三个标记和一个普通 https 链接
        Assert.Contains("MARKER-START", html);
        Assert.Contains("NO-SCRIPT-RAN", html);
        Assert.Contains("MARKER-END", html);
        Assert.Equal(2, Marker().Count(html));
        Assert.Contains("<form method=\"post\">", html);
        Assert.Contains("href=\"https://example.com/ok\"", html);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 远程内容默认不加载 / 营销邮件中的远程图片（marketing-remote-content.eml）")]
    public async Task MarketingSample_IsFlaggedAsHavingRemoteContent()
    {
        using var parsed = await MailAssert.ParseSampleAsync("marketing-remote-content.eml");

        Assert.True(parsed.Document.HasRemoteContent);
        SafeHtml.AssertSafe(parsed.Document.Body.Html!);

        // 远程地址本身不改写，由 CSP 负责拦截
        Assert.Contains("https://raw.githubusercontent.com/real-zony/EmailFileViewer/main/samples/assets/hero.png", parsed.Document.Body.Html);
        Assert.Contains("pixel.gif", parsed.Document.Body.Html);
    }

    [Theory]
    [InlineData("headers-normal.eml")]
    [InlineData("body-plain-only.eml")]
    [InlineData("body-html-and-plain.eml")]
    [InlineData("link-text-mismatch.eml")]
    [InlineData("cid-inline-image.eml")]
    [InlineData("body-rtf-only.msg")]
    [InlineData("body-html-in-rtf.msg")]
    [InlineData("EmailWithAttachments.msg")]
    [Trait("Scenario", "email-viewing / 远程内容默认不加载 / 普通邮件没有远程内容标志")]
    public async Task OrdinaryMail_IsNotFlaggedAsHavingRemoteContent(string sampleName)
    {
        using var parsed = await MailAssert.ParseSampleAsync(sampleName);

        Assert.False(parsed.Document.HasRemoteContent);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 正文链接在系统浏览器中打开 / 链接文字与实际地址不符（link-text-mismatch.eml）")]
    public async Task LinkTextMismatchSample_EveryLinkTitleEqualsItsHref()
    {
        using var parsed = await MailAssert.ParseSampleAsync("link-text-mismatch.eml");
        var links = ReadLinks(parsed.Document.Body.Html!);

        Assert.Equal(4, links.Count);
        Assert.All(links, link =>
        {
            Assert.Equal(link.Href, link.Title);
            Assert.Equal("_blank", link.Target);
            Assert.Equal("noopener noreferrer", link.Rel);
        });

        // 样本里第 3 个链接故意把 title 伪造成 paypal 地址，处理后被实际地址覆盖
        Assert.Equal("https://example.com/not-paypal", links[2].Title);
        Assert.Equal("https://example.com/not-paypal", links[0].Href);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 正文链接在系统浏览器中打开 / 恶意样本里保留的普通链接也被加固")]
    public async Task MaliciousSample_KeptLinkIsHardened()
    {
        using var parsed = await MailAssert.ParseSampleAsync("html-malicious.eml");
        var links = ReadLinks(parsed.Document.Body.Html!);

        var link = Assert.Single(links, l => l.Href is not null);
        Assert.Equal("https://example.com/ok", link.Href);
        Assert.Equal("https://example.com/ok", link.Title);
        Assert.Equal("_blank", link.Target);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 内嵌图片 / 带签名图片的邮件（cid-inline-image.eml）")]
    public async Task CidInlineSample_ImagesBecomeDataUris()
    {
        using var parsed = await MailAssert.ParseSampleAsync("cid-inline-image.eml");
        var html = parsed.Document.Body.Html!;

        var sources = ReadImageSources(html);
        Assert.Equal(2, sources.Count);
        Assert.All(sources, source => Assert.StartsWith("data:image/png;base64,", source));
        // data: URI 里的内容是完整的、可解码的 PNG
        foreach (var source in sources)
        {
            var bytes = Convert.FromBase64String(source["data:image/png;base64,".Length..]);
            Assert.Equal([0x89, 0x50, 0x4E, 0x47], bytes[..4]);
        }
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 内嵌图片 / MSG 中按文件名引用的图片（EmailWithAttachments.msg）")]
    public async Task MsgSample_FileNameReferencedImageBecomesDataUri()
    {
        using var parsed = await MailAssert.ParseSampleAsync("EmailWithAttachments.msg");

        var source = Assert.Single(ReadImageSources(parsed.Document.Body.Html!));
        Assert.StartsWith("data:image/jpeg;base64,", source);
        Assert.Equal([0xFF, 0xD8, 0xFF], Convert.FromBase64String(source["data:image/jpeg;base64,".Length..])[..3]);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 正文在沙箱中渲染 / RTF 转出的 HTML 里的 javascript: 链接也被清理")]
    public async Task RtfConvertedHtml_IsSanitizedToo()
    {
        // RTF 里的超链接字段会被 RtfPipe 转成 <a href>，地址可以是 javascript:
        const string Rtf = @"{\rtf1\ansi{\fonttbl{\f0 Arial;}}\f0 Click {\field{\*\fldinst{HYPERLINK ""javascript:alert(1)""}}{\fldrslt here}} and {\field{\*\fldinst{HYPERLINK ""https://example.com/ok""}}{\fldrslt there}}\par}";
        using var temp = new TempDirectory();
        var path = temp.WriteMail(
            "rtf-links.eml",
            $$"""
            From: a@example.com
            Subject: rtf links
            Content-Type: text/rtf; charset=us-ascii

            {{Rtf}}
            """);

        using var parsed = await EmailParser.ParseFileAsync(path);

        Assert.Equal(BodyFormat.Rtf, parsed.Document.Body.SourceFormat);
        SafeHtml.AssertSafe(parsed.Document.Body.Html!);
        Assert.DoesNotContain("javascript", parsed.Document.Body.Html);
        var link = Assert.Single(ReadLinks(parsed.Document.Body.Html!), l => l.Href is not null);
        Assert.Equal("https://example.com/ok", link.Title);
    }

    private sealed record Link(string? Href, string? Title, string? Target, string? Rel);

    private static List<Link> ReadLinks(string html)
    {
        var links = new List<Link>();
        var tokenizer = new HtmlTokenizer(new StringReader(html));
        while (tokenizer.ReadNextToken(out var token))
        {
            if (token is HtmlTagToken { IsEndTag: false } tag && tag.Name.Equals("a", StringComparison.OrdinalIgnoreCase))
            {
                links.Add(new Link(Attribute(tag, "href"), Attribute(tag, "title"), Attribute(tag, "target"), Attribute(tag, "rel")));
            }
        }

        return links;
    }

    private static List<string> ReadImageSources(string html)
    {
        var sources = new List<string>();
        var tokenizer = new HtmlTokenizer(new StringReader(html));
        while (tokenizer.ReadNextToken(out var token))
        {
            if (token is HtmlTagToken { IsEndTag: false } tag && tag.Name.Equals("img", StringComparison.OrdinalIgnoreCase))
            {
                sources.Add(Attribute(tag, "src")!);
            }
        }

        return sources;
    }

    private static string? Attribute(HtmlTagToken tag, string name)
    {
        return tag.Attributes.FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;
    }
}
