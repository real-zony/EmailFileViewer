using EmailFileViewer.Core.Parsing;

namespace EmailFileViewer.Core.Tests;

/// <summary>
/// 对应任务 3.8 的 HTML 处理：删除脚本、事件属性、危险地址和被禁止的元素，链接加固，内嵌资源换成 data: URI。
/// 规格场景：email-viewing / 正文在沙箱中渲染、内嵌图片、正文链接在系统浏览器中打开。
/// </summary>
public class HtmlSanitizerTests
{
    private static SanitizedHtml Run(string html, InlineResourceResolver? resolver = null)
    {
        var result = HtmlSanitizer.Sanitize(html, resolver);
        SafeHtml.AssertSafe(result.Html);
        return result;
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 正文在沙箱中渲染 / 正文包含脚本")]
    public void Script_IsRemovedWithItsContent_RestOfBodyIsKept()
    {
        var result = Run("<p>before</p><script>alert('x'); document.write('<b>')</script><p>after</p><SCRIPT SRC=https://evil.example/x.js></SCRIPT>");

        Assert.Equal("<p>before</p><p>after</p>", result.Html);
        Assert.False(result.HasRemoteContent);
    }

    [Theory]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("<body onload=alert(1)>text")]
    [InlineData("<div onclick=\"a()\" onmouseover='b()' ONFOCUS=c() OnLoad=d()>x</div>")]
    [InlineData("<svg onload=alert(1)><rect/></svg>")]
    [InlineData("<a href=\"https://a.example\" onclick=\"x()\">link</a>")]
    [InlineData("<input autofocus onfocus=alert(1)>")]
    [Trait("Scenario", "email-viewing / 正文在沙箱中渲染 / 正文包含事件属性")]
    public void EventAttributes_AreRemoved(string html)
    {
        var result = Run(html);

        Assert.DoesNotContain("alert", result.Html);
        Assert.DoesNotContain(" on", result.Html, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("<a href=\"javascript:alert(1)\">x</a>")]
    [InlineData("<a href=\"JaVaScRiPt:alert(1)\">x</a>")]
    [InlineData("<a href=\"  javascript:alert(1)\">x</a>")]
    [InlineData("<a href=\"java\tscript:alert(1)\">x</a>")]
    [InlineData("<a href=\"&#106;avascript:alert(1)\">x</a>")]
    [InlineData("<a href=\"&#x6A;ava&Tab;script&colon;alert(1)\">x</a>")]
    [InlineData("<a href=\"vbscript:msgbox(1)\">x</a>")]
    [InlineData("<a href=\"data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==\">x</a>")]
    [InlineData("<a href=\"data:image/svg+xml;base64,PHN2Zz48L3N2Zz4=\">x</a>")]
    [InlineData("<a href=\"file:///etc/passwd\">x</a>")]
    [InlineData("<img src=\"javascript:alert(1)\">")]
    [InlineData("<img src=\"data:text/html,<script>alert(1)</script>\">")]
    [InlineData("<iframe src=\"javascript:alert(1)\"></iframe>")]
    [InlineData("<form action=\"javascript:alert(1)\"><input type=submit></form>")]
    [InlineData("<button formaction=\"javascript:alert(1)\">go</button>")]
    [InlineData("<svg><a xlink:href=\"javascript:alert(1)\"><text>x</text></a></svg>")]
    [InlineData("<body background=\"javascript:alert(1)\">x")]
    [InlineData("<video poster=\"javascript:alert(1)\"></video>")]
    [InlineData("<div style=\"background:url(javascript:alert(1))\">x</div>")]
    [InlineData("<div style=\"background: URL( 'vbscript:x' )\">x</div>")]
    [InlineData("<div style=\"width:expression(alert(1))\">x</div>")]
    [InlineData("<style>body { background: url(\"javascript:alert(1)\"); }</style>")]
    [Trait("Scenario", "email-viewing / 正文在沙箱中渲染 / 正文包含危险地址")]
    public void DangerousUrls_AreRemoved(string html)
    {
        var result = Run(html);

        Assert.DoesNotContain("javascript", result.Html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("vbscript", result.Html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data:text", result.Html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("file:", result.Html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("svg+xml", result.Html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expression(", result.Html, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("<iframe src=\"https://a.example\">fallback text</iframe>", "")]
    [InlineData("<frameset><frame src=\"https://a.example\"></frameset>", "")]
    [InlineData("<object data=\"https://a.example/x.swf\">fallback <b>kept</b></object>", "fallback <b>kept</b>")]
    [InlineData("<embed src=\"https://a.example/x.swf\">", "")]
    [InlineData("<applet code=\"X.class\">alt</applet>", "alt")]
    [InlineData("<base href=\"https://evil.example/\"><p>x</p>", "<p>x</p>")]
    [InlineData("<meta http-equiv=\"refresh\" content=\"0;url=https://evil.example\"><p>x</p>", "<p>x</p>")]
    [InlineData("<meta http-equiv=\"Set-Cookie\" content=\"a=b\"><p>x</p>", "<p>x</p>")]
    [InlineData("<noscript><img src=\"https://track.example/p.gif\"></noscript><p>x</p>", "<p>x</p>")]
    [InlineData("<template><img src=x onerror=alert(1)></template><p>x</p>", "<p>x</p>")]
    [InlineData("<svg><set attributeName=\"href\" to=\"javascript:alert(1)\"/><a><text>x</text></a></svg>", "<svg><a><text>x</text></a></svg>")]
    [InlineData("<link rel=\"preload\" href=\"https://a.example/x\"><p>x</p>", "<p>x</p>")]
    [InlineData("<link rel=\"icon\" href=\"https://a.example/favicon.ico\"><p>x</p>", "<p>x</p>")]
    [Trait("Scenario", "email-viewing / 正文在沙箱中渲染 / 正文包含会加载外部文档的元素")]
    public void ForbiddenElements_AreRemoved(string html, string expected)
    {
        var result = Run(html);

        Assert.Equal(expected, result.Html);
        Assert.False(result.HasRemoteContent);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 正文在沙箱中渲染 / 表单保留，但去掉提交地址")]
    public void Form_KeepsFieldsButLosesAction()
    {
        var result = Run("<form action=\"https://evil.example/collect\" method=\"post\"><input name=\"q\" formaction=\"https://evil.example/\"><button type=submit>Go</button></form>");

        Assert.Equal("<form method=\"post\"><input name=\"q\"><button type=\"submit\">Go</button></form>", result.Html);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 正文在沙箱中渲染 / 注释和 DOCTYPE 被丢弃")]
    public void CommentsAndDoctype_AreDropped()
    {
        var result = Run("<!DOCTYPE html><!--[if gte mso 9]><xml><o:x/></xml><![endif]--><p>text<!-- hidden <script>alert(1)</script> --></p>");

        Assert.Equal("<p>text</p>", result.Html);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 正文在沙箱中渲染 / 重复的属性以第一个为准")]
    public void DuplicateAttributes_FirstWins()
    {
        var result = Run("<a href=\"javascript:alert(1)\" href=\"https://good.example/\">x</a>");

        Assert.DoesNotContain("good.example", result.Html);
        Assert.DoesNotContain("javascript", result.Html);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 正文在沙箱中渲染 / 文本和属性值正确转义，非 ASCII 字符保持原样")]
    public void TextAndAttributes_AreEscaped_NonAsciiIsKept()
    {
        var result = Run("<p title=\"a &quot;quoted&quot; &lt;value&gt;\">中文 &amp; &lt;b&gt; &nbsp;é</p>");

        Assert.Equal("<p title=\"a &quot;quoted&quot; &lt;value&gt;\">中文 &amp; &lt;b&gt; &nbsp;é</p>", result.Html);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 正文在沙箱中渲染 / style 里的内容不会被浏览器重新解析成标签")]
    public void StyleElement_KeepsCssButNeverEmitsLessThan()
    {
        var result = Run("<style>a > b { color: red } p::before { content: \"<img src=x onerror=alert(1)>\" }</style><p>x</p>");

        Assert.Contains("a > b { color: red }", result.Html);
        Assert.DoesNotContain("<img", result.Html);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 正文在沙箱中渲染 / Outlook 的 <!-- --> 包装的样式表保留规则")]
    public void StyleElementWrappedInHtmlComment_KeepsTheRules()
    {
        var result = Run("<style><!--\np.MsoNormal { margin: 0cm }\n@font-face { font-family: Calibri }\n--></style><p class=\"MsoNormal\">x</p>");

        Assert.Equal("<style>\np.MsoNormal { margin: 0cm }\n@font-face { font-family: Calibri }\n</style><p class=\"MsoNormal\">x</p>", result.Html);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 正文在沙箱中渲染 / 拼接出 <!-- 的写法也不会让样式表里留下 <")]
    public void StyleElement_ConcatenationTricksNeverLeaveALessThan()
    {
        var result = Run("<style>a { b: \"<<!--!--img\" }</style>");

        const string Open = "<style>";
        const string Close = "</style>";
        var content = result.Html[Open.Length..^Close.Length];
        Assert.DoesNotContain('<', content);
        Assert.StartsWith(Open, result.Html);
        Assert.EndsWith(Close, result.Html);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 正文在沙箱中渲染 / textarea 与 title 里的标记只是文本")]
    public void RcdataElements_ContentIsEscapedText()
    {
        var result = Run("<title>a <b>b</b></title><textarea><img src=x onerror=alert(1)></textarea>");

        Assert.DoesNotContain("<img", result.Html);
        Assert.DoesNotContain("<b>", result.Html);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 正文链接在系统浏览器中打开 / 链接加 target 和 rel，title 写成实际地址")]
    public void Links_GetTargetRelAndTitleWithTheActualAddress()
    {
        var result = Run("<a href=\"https://example.com/a?x=1&amp;y=2\" target=\"_self\" rel=\"opener\" title=\"https://www.paypal.com/\">paypal.com</a><map><area href=\"https://example.com/area\" title=\"fake\"></map>");

        Assert.Contains("<a href=\"https://example.com/a?x=1&amp;y=2\" target=\"_blank\" rel=\"noopener noreferrer\" title=\"https://example.com/a?x=1&amp;y=2\">paypal.com</a>", result.Html);
        Assert.Contains("<area href=\"https://example.com/area\" target=\"_blank\" rel=\"noopener noreferrer\" title=\"https://example.com/area\">", result.Html);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 正文链接在系统浏览器中打开 / mailto 链接同样处理，没有 href 的锚点不动")]
    public void MailtoLinksAreHardened_AnchorsWithoutHrefAreLeftAlone()
    {
        var result = Run("<a href=\"mailto:bob@example.com\">mail</a><a name=\"top\" title=\"keep\">anchor</a>");

        Assert.Contains("<a href=\"mailto:bob@example.com\" target=\"_blank\" rel=\"noopener noreferrer\" title=\"mailto:bob@example.com\">mail</a>", result.Html);
        Assert.Contains("<a name=\"top\" title=\"keep\">anchor</a>", result.Html);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 正文链接在系统浏览器中打开 / 页内锚点链接保持原样，免得点击后去开新窗口")]
    public void FragmentOnlyLinks_AreLeftAlone()
    {
        var result = Run("<a href=\"#section\">jump</a>");

        Assert.Equal("<a href=\"#section\">jump</a>", result.Html);
    }

    [Theory]
    [InlineData("<img src=\"http://a.example/x.png\">")]
    [InlineData("<img src=\"https://a.example/x.png\">")]
    [InlineData("<img src=\"//a.example/x.png\">")]
    [InlineData("<img src=\" HTTPS://a.example/x.png\">")]
    [InlineData("<img src=\"\\\\a.example\\x.png\">")]
    [InlineData("<img src=\"data:image/gif;base64,R0lGOD\" srcset=\"https://a.example/x.png 2x\">")]
    [InlineData("<img srcset=\"data:image/gif;base64,R0lGOD 1x, https://a.example/x.png 2x\">")]
    [InlineData("<table background=\"https://a.example/bg.png\"><tr><td>x</td></tr></table>")]
    [InlineData("<td background=\"//a.example/bg.png\">x</td>")]
    [InlineData("<video poster=\"https://a.example/p.png\"></video>")]
    [InlineData("<video><source src=\"https://a.example/v.mp4\"></video>")]
    [InlineData("<input type=\"image\" src=\"https://a.example/b.png\">")]
    [InlineData("<link rel=\"stylesheet\" href=\"https://a.example/style.css\">")]
    [InlineData("<link rel=\"Stylesheet preload\" href=\"//a.example/style.css\">")]
    [InlineData("<div style=\"background-image: url(https://a.example/x.png)\">x</div>")]
    [InlineData("<div style=\"background: url('//a.example/x.png') no-repeat\">x</div>")]
    [InlineData("<div style='background:url(\"http://a.example/x.png\")'>x</div>")]
    [InlineData("<style>@import url(https://a.example/x.css);</style>")]
    [InlineData("<style>@import 'https://a.example/x.css';</style>")]
    [InlineData("<style>@import \"//a.example/x.css\";</style>")]
    [InlineData("<style>@font-face { font-family: X; src: url(https://fonts.example/x.woff2) format('woff2'); }</style>")]
    [InlineData("<style>.a { background: url(https://a.example/x.png) }</style>")]
    [InlineData("<svg><image href=\"https://a.example/x.png\"/></svg>")]
    [InlineData("<svg><image xlink:href=\"https://a.example/x.png\"/></svg>")]
    [InlineData("<svg><use href=\"https://a.example/sprite.svg#icon\"/></svg>")]
    [Trait("Scenario", "email-viewing / 远程内容默认不加载 / 营销邮件中的远程图片")]
    public void RemoteResources_AreDetected(string html)
    {
        var result = Run(html);

        Assert.True(result.HasRemoteContent, html);
    }

    [Theory]
    [InlineData("<p>plain text</p>")]
    [InlineData("<a href=\"https://a.example/\">a link is not remote content</a>")]
    [InlineData("<area href=\"https://a.example/\">")]
    [InlineData("<img src=\"data:image/png;base64,iVBORw0KGgo=\">")]
    [InlineData("<img src=\"logo.png\">")]
    [InlineData("<img src=\"/logo.png\">")]
    [InlineData("<img src=\"cid:unresolved@example\">")]
    [InlineData("<img src=\"\">")]
    [InlineData("<div style=\"color: red; background: #fff url(data:image/png;base64,iVBORw0KGgo=)\">x</div>")]
    [InlineData("<style>.a { color: red } /* https://a.example/x.png */ @media print { .b { display: none } }</style>")]
    [InlineData("<form action=\"https://a.example/post\"><input name=\"q\"></form>")]
    [InlineData("<a href=\"mailto:a@example.com\">mail</a>")]
    [InlineData("<link rel=\"icon\" href=\"https://a.example/favicon.ico\">")]
    [InlineData("<meta http-equiv=\"refresh\" content=\"0;url=https://a.example\">")]
    [InlineData("<iframe src=\"https://a.example\"></iframe>")]
    [InlineData("<script src=\"https://a.example/x.js\"></script>")]
    [Trait("Scenario", "email-viewing / 远程内容默认不加载 / 没有远程资源的邮件不显示提示")]
    public void NoRemoteResources_AreNotFlagged(string html)
    {
        var result = Run(html);

        Assert.False(result.HasRemoteContent, html);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 内嵌图片 / cid 地址换成 data: URI，每张图只编码一次")]
    public void CidReferences_AreReplacedByDataUris_InAttributesAndCss()
    {
        var logo = new FakeAttachment("2", "logo.png", "image/png", [1, 2, 3, 4]);
        var banner = new FakeAttachment("3", "banner.jpg", "image/jpeg", [9, 8, 7]);
        var resolver = new InlineResourceResolver(
        [
            new InlineCandidate(logo, "logo@example", AllowNameLookup: false),
            new InlineCandidate(banner, "banner@example", AllowNameLookup: false),
        ]);

        var result = Run(
            "<img src=\"cid:logo@example\"><body background=\"cid:banner@example\"><div style=\"background:url(cid:logo@example)\"></div><style>.x{background:url('cid:logo@example')}</style><img src=\"cid:missing@example\">",
            resolver);

        var logoUri = "data:image/png;base64," + Convert.ToBase64String([1, 2, 3, 4]);
        var bannerUri = "data:image/jpeg;base64," + Convert.ToBase64String([9, 8, 7]);
        Assert.Contains($"<img src=\"{logoUri}\">", result.Html);
        Assert.Contains($"background=\"{bannerUri}\"", result.Html);
        Assert.Contains($"style=\"background:url(&quot;{logoUri}&quot;)\"", result.Html);
        Assert.Contains($".x{{background:url(\"{logoUri}\")}}", result.Html);
        Assert.Contains("<img src=\"cid:missing@example\">", result.Html);
        Assert.True(logo.IsInlineConsumed);
        Assert.True(banner.IsInlineConsumed);
        Assert.False(result.HasRemoteContent);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 内嵌图片 / 不是图片的 cid 资源不内嵌")]
    public void CidReferenceToNonImage_IsNotEmbedded()
    {
        var script = new FakeAttachment("2", "payload.html", "text/html", "<script>alert(1)</script>"u8.ToArray());
        var resolver = new InlineResourceResolver([new InlineCandidate(script, "payload@example", AllowNameLookup: false)]);

        var result = Run("<img src=\"cid:payload@example\">", resolver);

        Assert.DoesNotContain("data:", result.Html);
        Assert.False(script.IsInlineConsumed);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 内嵌图片 / 类型是 octet-stream 但扩展名是图片时按扩展名内嵌")]
    public void CidReferenceToOctetStreamImage_UsesTheExtension()
    {
        var image = new FakeAttachment("2", "photo.JPG", "application/octet-stream", [5, 5]);
        var resolver = new InlineResourceResolver([new InlineCandidate(image, "p@example", AllowNameLookup: false)]);

        var result = Run("<img src=\"cid:p@example\">", resolver);

        Assert.Contains("data:image/jpeg;base64,", result.Html);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 内嵌图片 / MSG 中按文件名引用的内嵌图片换成 data: URI")]
    public void FileNameReferences_AreReplaced_OnlyWhenNameLookupIsAllowed()
    {
        var hidden = new FakeAttachment("a1", "tinkerbell.jpg", "image/jpeg", [1, 1]);
        var visible = new FakeAttachment("a0", "peterpan.jpg", "image/jpeg", [2, 2]);
        var resolver = new InlineResourceResolver(
        [
            new InlineCandidate(hidden, "tinkerbell.jpg", AllowNameLookup: true),
            new InlineCandidate(visible, null, AllowNameLookup: false),
        ]);

        var result = Run("<img src=\"tinkerbell.jpg\"><img src=\"peterpan.jpg\"><img src=\"Tinkerbell.JPG?x=1\">", resolver);

        Assert.Equal(2, result.Html.Split("data:image/jpeg;base64,AQE=").Length - 1);
        Assert.Contains("<img src=\"peterpan.jpg\">", result.Html);
        Assert.True(hidden.IsInlineConsumed);
        Assert.False(visible.IsInlineConsumed);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 内嵌图片 / srcset 里的 cid 和危险地址")]
    public void Srcset_IsSanitizedPerCandidate()
    {
        var logo = new FakeAttachment("2", "logo.png", "image/png", [1]);
        var resolver = new InlineResourceResolver([new InlineCandidate(logo, "logo@example", AllowNameLookup: false)]);

        var result = Run("<img srcset=\"cid:logo@example 1x, javascript:alert(1) 2x, https://a.example/b.png 3x, data:image/png;base64,AAAA 4x\">", resolver);

        Assert.Equal(
            "<img srcset=\"data:image/png;base64,AQ== 1x, https://a.example/b.png 3x, data:image/png;base64,AAAA 4x\">",
            result.Html);
        Assert.True(result.HasRemoteContent);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 正文在沙箱中渲染 / 空输入")]
    public void EmptyInput_GivesEmptyOutput()
    {
        var result = Run(string.Empty);

        Assert.Equal(string.Empty, result.Html);
        Assert.False(result.HasRemoteContent);
    }
}
