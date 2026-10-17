using System.Diagnostics;
using System.Text;
using EmailFileViewer.Core.Models;
using EmailFileViewer.Core.Parsing;

namespace EmailFileViewer.Core.Tests;

/// <summary>
/// 整分支审查后对 HTML/CSS 清理的加固：病态输入不能拖垮解析（回溯、体积膨胀），以及几处漏判（link 的 rel、style 自闭合、image-set、SVG href）。
/// </summary>
/// <remarks>
/// 病态输入都在测试里现场生成，不往 samples/ 提交大文件。性能类测试的时间上限留了很大余量（修复前同样的输入要几十秒），
/// 只用来发现“又变成平方级”，不是性能基准。
/// </remarks>
public class SanitizerHardeningTests
{
    // 修复前 240KB 的输入要 31.7 秒；线性实现只要几毫秒。10 秒既挡得住平方级，又不会因为 CI 机器慢而误报
    private static readonly TimeSpan PathologicalLimit = TimeSpan.FromSeconds(10);

    private static SanitizedHtml Run(string html, InlineResourceResolver? resolver = null, int? maxOutputLength = null)
    {
        var result = maxOutputLength is { } max
            ? HtmlSanitizer.Sanitize(html, resolver, max)
            : HtmlSanitizer.Sanitize(html, resolver);
        SafeHtml.AssertSafe(result.Html);
        return result;
    }

    private static TimeSpan Measure(Action action)
    {
        var stopwatch = Stopwatch.StartNew();
        action();
        return stopwatch.Elapsed;
    }

    // ---- I4：CSS 的平方级回溯 ----

    [Theory]
    [InlineData(60_000)]
    [InlineData(200_000)]
    [Trait("Scenario", "email-viewing / 正文在沙箱中渲染 / 病态样式不拖垮解析（url( 重复）")]
    public void UnterminatedUrlRepeated_InStyleAttribute_IsLinear(int repeats)
    {
        // 每个 url( 都没有结束括号：回溯式正则对每个起点都扫到末尾
        var html = "<div style=\"" + string.Concat(Enumerable.Repeat("url(a", repeats)) + "\">x</div>";

        var elapsed = Measure(() => Run(html));

        Assert.True(elapsed < PathologicalLimit, $"耗时 {elapsed.TotalSeconds:F1} 秒");
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 正文在沙箱中渲染 / 病态样式不拖垮解析（style 元素里 url( 重复）")]
    public void UnterminatedUrlRepeated_InStyleElement_IsLinear()
    {
        var html = "<style>" + string.Concat(Enumerable.Repeat("a{background:url(b", 40_000)) + "</style><p>after</p>";

        var elapsed = Measure(() => Run(html));

        Assert.True(elapsed < PathologicalLimit, $"耗时 {elapsed.TotalSeconds:F1} 秒");
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 正文在沙箱中渲染 / 病态样式不拖垮解析（url( 后面一长串空白）")]
    public void UrlFollowedByLongWhitespaceRun_IsLinear()
    {
        var html = "<div style=\"url(a" + new string(' ', 400_000) + "b\">x</div>";

        var elapsed = Measure(() => Run(html));

        Assert.True(elapsed < PathologicalLimit, $"耗时 {elapsed.TotalSeconds:F1} 秒");
    }

    [Theory]
    [InlineData("url(\"")]
    [InlineData("url('")]
    [InlineData("url(\"a\" ")]
    [InlineData("@import \"")]
    [InlineData("@import '")]
    [InlineData("expression(")]
    [InlineData("behavior :")]
    [InlineData("image-set(\"a\" ")]
    [Trait("Scenario", "email-viewing / 正文在沙箱中渲染 / 病态样式不拖垮解析（其他重复片段）")]
    public void OtherRepeatedFragments_AreLinear(string fragment)
    {
        var html = "<style>" + string.Concat(Enumerable.Repeat(fragment, 50_000)) + "</style><p>after</p>";

        var elapsed = Measure(() => Run(html));

        Assert.True(elapsed < PathologicalLimit, $"耗时 {elapsed.TotalSeconds:F1} 秒");
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 正文在沙箱中渲染 / 病态样式不拖垮解析（image-set 嵌套）")]
    public void NestedImageSet_IsLinear()
    {
        var css = string.Concat(Enumerable.Repeat("image-set(", 100_000)) + "\"https://a.example/x.png\"" + new string(')', 100_000);

        var elapsed = Measure(() => Run($"<div style=\"{css}\">x</div>"));

        Assert.True(elapsed < PathologicalLimit, $"耗时 {elapsed.TotalSeconds:F1} 秒");
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 正文在沙箱中渲染 / 过长的样式整段丢弃")]
    public void StyleTextLongerThanTheLimit_IsDropped_TheRestOfTheBodyIsKept()
    {
        var repeats = CssSanitizer.MaxStyleTextLength / 3 + 10;
        var bigRules = string.Concat(Enumerable.Repeat("a{}", repeats));
        var html = $"<style>{bigRules}</style><style>.keep{{color:red}}</style><div style=\"{bigRules}\">body-text</div>";

        var result = Run(html);

        Assert.DoesNotContain("a{}", result.Html);
        Assert.Contains(".keep{color:red}", result.Html);
        Assert.Contains("body-text", result.Html);
        Assert.True(result.Html.Length < 1000, $"输出长度 {result.Html.Length}");
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 正文在沙箱中渲染 / 刚好不超过长度上限的样式照常处理")]
    public void StyleTextAtTheLimit_IsStillProcessed()
    {
        var padding = new string(' ', CssSanitizer.MaxStyleTextLength - "a{b:url(javascript:x)}".Length);
        var css = "a{b:url(javascript:x)}" + padding;
        Assert.Equal(CssSanitizer.MaxStyleTextLength, css.Length);

        var processed = new CssSanitizer(null).Process(css);

        Assert.StartsWith("a{b:url(\"about:blank\")}", processed);
    }

    // 手写的 url() 扫描要和旧正则 url\(\s*("..."|'...'|不含引号和空白开头的内容)\s*\) 给出相同的结果
    [Theory]
    [InlineData("a{b:url(http://x/y.png)}", "a{b:url(http://x/y.png)}", true)]
    [InlineData("a{b:url( \"http://x/y.png\" )}", "a{b:url( \"http://x/y.png\" )}", true)]
    [InlineData("a{b:URL('http://x/y.png')}", "a{b:URL('http://x/y.png')}", true)]
    [InlineData("a{b:url(  http://x/y.png   )}", "a{b:url(  http://x/y.png   )}", true)]
    // 不带引号的地址到第一个 ")" 为止，所以 alert(1) 里的括号会让多出来的 ")" 留在后面——旧正则就是这样
    [InlineData("a{b:url(javascript:alert(1))}", "a{b:url(\"about:blank\"))}", false)]
    [InlineData("a{b:url(  JavaScript:alert(1)  )}", "a{b:url(\"about:blank\")  )}", false)]
    [InlineData("a{b:url(javascript:alert(1)", "a{b:url(\"about:blank\")", false)]
    [InlineData("a{b:url('javascript:x')}", "a{b:url(\"about:blank\")}", false)]
    [InlineData("a{b:url(\"vbscript:x\"  )}", "a{b:url(\"about:blank\")}", false)]
    [InlineData("a{b:url(data:text/html,<b>)}", "a{b:url(\"about:blank\")}", false)]
    [InlineData("a{b:url(x.png) url(javascript:y)}", "a{b:url(x.png) url(\"about:blank\")}", false)]
    [InlineData("a{b:url(javascript:y) url(http://z/q.png)}", "a{b:url(\"about:blank\") url(http://z/q.png)}", true)]
    // 不匹配：没有结束括号、引号没闭合、引号后面还有别的东西——和旧正则一样原样保留
    [InlineData("a{b:url(javascript:alert(1", "a{b:url(javascript:alert(1", false)]
    [InlineData("a{b:url(\"javascript:x)}", "a{b:url(\"javascript:x)}", false)]
    [InlineData("a{b:url(\"javascript:x\"y)}", "a{b:url(\"javascript:x\"y)}", false)]
    [InlineData("a{b:url()}", "a{b:url()}", false)]
    [InlineData("a{b:url(   )}", "a{b:url(   )}", false)]
    [Trait("Scenario", "email-viewing / 正文在沙箱中渲染 / url() 的识别规则")]
    public void UrlFunction_IsMatchedLikeBefore(string css, string expected, bool expectedRemote)
    {
        var sanitizer = new CssSanitizer(null);

        var processed = sanitizer.Process(css);

        Assert.Equal(expected, processed);
        Assert.Equal(expectedRemote, sanitizer.HasRemoteContent);
    }

    // 随机拼出各种括号、引号、空白的组合，和旧正则（这里作为参照实现）逐个对比，确认改写没有改变匹配规则
    [Fact]
    [Trait("Scenario", "email-viewing / 正文在沙箱中渲染 / url() 手写扫描与旧正则等价（随机对比）")]
    public void UrlFunctionScanner_AgreesWithTheOldRegex_OnRandomInput()
    {
        string[] pieces = ["url(", "URL(", "Url (", "\"", "'", ")", " ", "  ", "\t", "\n", "a", "b c", "javascript:x", "http://h/p.png", "data:text/html,x", "(", "\\", ";"];
        var oldRegex = new System.Text.RegularExpressions.Regex(
            """url\(\s*(?:"(?<d>[^"]*)"|'(?<s>[^']*)'|(?<u>[^)'"\s][^)]*?))\s*\)""",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        var random = new Random(20261017);

        for (var i = 0; i < 30_000; i++)
        {
            var css = string.Concat(Enumerable.Range(0, random.Next(1, 14)).Select(_ => pieces[random.Next(pieces.Length)]));

            var expectedRemote = false;
            var expected = oldRegex.Replace(css, match =>
            {
                var url = new[] { "d", "s", "u" }.Select(name => match.Groups[name]).First(group => group.Success).Value;
                switch (UrlClassifier.Classify(url))
                {
                    case UrlKind.Dangerous:
                        return "url(\"about:blank\")";
                    case UrlKind.Remote:
                        expectedRemote = true;
                        return match.Value;
                    default:
                        return match.Value;
                }
            });

            var sanitizer = new CssSanitizer(null);
            var actual = sanitizer.Process(css);

            // image-set 的字符串地址是新增的识别，参照实现里没有；随机片段里不放 image-set，不会触发
            Assert.True(expected == actual, $"输入：{css.Replace("\n", "\\n").Replace("\t", "\\t")}\n旧正则：{expected}\n手写扫描：{actual}");
            Assert.Equal(expectedRemote, sanitizer.HasRemoteContent);
        }
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 内嵌图片 / CSS 里的 cid 地址仍然换成 data: URI")]
    public void CidInCss_IsStillInlined()
    {
        var image = new FakeAttachment("2", "logo.png", "image/png", [1, 2, 3]);
        var resolver = new InlineResourceResolver([new InlineCandidate(image, "logo@example", AllowNameLookup: false)]);

        var processed = new CssSanitizer(resolver).Process("a{b:url( 'cid:logo@example' ) c:url(cid:missing)}");

        Assert.Equal($"a{{b:url(\"data:image/png;base64,{Convert.ToBase64String([1, 2, 3])}\") c:url(cid:missing)}}", processed);
        Assert.True(image.IsInlineConsumed);
    }

    // ---- I5：内嵌图片的总量和正文长度 ----

    [Fact]
    [Trait("Scenario", "email-viewing / 内嵌图片 / 同一张图被反复引用时总量有上限")]
    public void RepeatedCidReferences_StopBeingInlinedOnceTheBudgetIsUsedUp()
    {
        var bytes = new byte[300];
        new Random(7).NextBytes(bytes);
        var image = new FakeAttachment("2", "pic.png", "image/png", bytes);
        var dataUriLength = ("data:image/png;base64," + Convert.ToBase64String(bytes)).Length;

        // 预算只够内嵌 2 次
        var resolver = new InlineResourceResolver([new InlineCandidate(image, "pic@example", AllowNameLookup: false)], maxEmittedLength: dataUriLength * 2 + 10);

        var html = string.Concat(Enumerable.Repeat("<img src=\"cid:pic@example\" alt=\"x\">", 5))
            + "<div style=\"background:url(cid:pic@example)\">t</div><body background=\"cid:pic@example\">";
        var result = Run(html, resolver);

        Assert.Equal(2, CountOccurrences(result.Html, "data:image/png;base64,"));
        Assert.DoesNotContain("cid:", result.Html);

        // 超出预算的 img 去掉 src，CSS 里的地址换成空白地址
        Assert.Equal(5, CountOccurrences(result.Html, "<img "));
        Assert.Equal(3, CountOccurrences(result.Html, "<img alt=\"x\">"));
        Assert.Contains("url(&quot;about:blank&quot;)", result.Html);
        Assert.False(image.IsInlineConsumed, "有引用没能内嵌，图片要留在附件列表里");
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 内嵌图片 / 一张图本身就超过总量上限时不读取也不内嵌")]
    public void ImageLargerThanTheWholeBudget_IsNeverEmbedded()
    {
        var big = new FakeAttachment("2", "big.png", "image/png", new byte[10_000]);
        var small = new FakeAttachment("3", "small.png", "image/png", [1, 2, 3]);
        var resolver = new InlineResourceResolver(
            [
                new InlineCandidate(big, "big@example", AllowNameLookup: false),
                new InlineCandidate(small, "small@example", AllowNameLookup: false),
            ],
            maxEmittedLength: 1_000);

        var result = Run("<img src=\"cid:big@example\"><img src=\"cid:small@example\">", resolver);

        Assert.Equal("<img><img src=\"data:image/png;base64,AQID\">", result.Html);
        Assert.False(big.IsInlineConsumed);
        Assert.True(small.IsInlineConsumed);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 内嵌图片 / 找不到的 cid 不受预算影响")]
    public void MissingCid_IsLeftAloneEvenWhenTheBudgetIsUsedUp()
    {
        var image = new FakeAttachment("2", "pic.png", "image/png", [1]);
        var resolver = new InlineResourceResolver([new InlineCandidate(image, "pic@example", AllowNameLookup: false)], maxEmittedLength: 0);

        var result = Run("<img src=\"cid:missing@example\">", resolver);

        Assert.Equal("<img src=\"cid:missing@example\">", result.Html);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 正文在沙箱中渲染 / 正文 HTML 超过长度上限时在上限处截断")]
    public void OutputLongerThanTheLimit_IsCutOff()
    {
        var html = string.Concat(Enumerable.Repeat("<p>0123456789</p>", 1_000));

        var result = Run(html, maxOutputLength: 5_000);

        Assert.True(result.Html.Length is > 5_000 and < 5_100, $"输出长度 {result.Html.Length}");
        Assert.StartsWith("<p>0123456789</p><p>0123456789</p>", result.Html);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 正文在沙箱中渲染 / 没有超过长度上限的正文完整输出")]
    public void OutputWithinTheLimit_IsComplete()
    {
        var html = string.Concat(Enumerable.Repeat("<p>0123456789</p>", 100));

        var result = Run(html, maxOutputLength: 5_000);

        Assert.Equal(html, result.Html);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 内嵌图片 / 审查复现：1MB 图片被引用上千次，正文不会膨胀到上亿字符")]
    public async Task OneMegabyteImageReferencedThousandsOfTimes_KeepsTheBodyBounded_AndTheImageStaysListed()
    {
        var image = new byte[1_000_000];
        new Random(1).NextBytes(image);
        var base64 = Convert.ToBase64String(image, Base64FormattingOptions.InsertLineBreaks);
        var body = string.Concat(Enumerable.Repeat("<img src=cid:a>", 1_000));
        using var temp = new TempDirectory();
        var path = temp.WriteMail(
            "cid-flood.eml",
            $"From: a@b.c\nTo: d@e.f\nSubject: s\nMIME-Version: 1.0\nContent-Type: multipart/related; boundary=XX\n\n--XX\nContent-Type: text/html; charset=utf-8\n\n{body}\n--XX\nContent-Type: image/png; name=a.png\nContent-ID: <a>\nContent-Transfer-Encoding: base64\n\n{base64}\n--XX--\n");

        var stopwatch = Stopwatch.StartNew();
        using var parsed = await EmailParser.ParseFileAsync(path);
        var elapsed = stopwatch.Elapsed;

        var html = Assert.IsType<string>(parsed.Document.Body.Html);

        // 预算 16 MiB，加上 1000 个标签本身的几十 KB；修复前这里是约 1.27 亿字符
        Assert.True(html.Length < InlineResourceResolver.MaxInlineDataUriLength + 200_000, $"正文长度 {html.Length}");
        Assert.True(html.Length > InlineResourceResolver.MaxInlineDataUriLength / 2, "预算以内的引用仍然应该内嵌");
        Assert.True(elapsed < PathologicalLimit, $"耗时 {elapsed.TotalSeconds:F1} 秒");

        // 有引用没能内嵌，图片仍然在附件列表里
        var attachment = Assert.Single(parsed.Document.Attachments);
        Assert.Equal("a.png", attachment.FileName);
        Assert.Equal(image.Length, attachment.Size);
    }

    // ---- I3：link 的 rel ----

    [Theory]
    [InlineData("stylesheet preconnect")]
    [InlineData("preconnect stylesheet")]
    [InlineData("stylesheet dns-prefetch")]
    [InlineData("STYLESHEET Prefetch Preload")]
    [InlineData("alternate stylesheet")]
    [InlineData("  stylesheet\tmodulepreload ")]
    [InlineData("stylesheet")]
    [Trait("Scenario", "email-viewing / 远程内容默认不加载 / link 的 rel 只保留 stylesheet")]
    public void LinkRel_IsRewrittenToExactlyStylesheet(string rel)
    {
        var result = Run($"<link rel=\"{rel}\" href=\"https://tracker.example/a.css\">");

        Assert.Equal("<link href=\"https://tracker.example/a.css\" rel=\"stylesheet\">", result.Html);
        Assert.True(result.HasRemoteContent);
    }

    [Theory]
    [InlineData("preconnect")]
    [InlineData("dns-prefetch")]
    [InlineData("preload")]
    [InlineData("icon")]
    [InlineData("")]
    [Trait("Scenario", "email-viewing / 远程内容默认不加载 / 不是样式表的 link 整个删除")]
    public void LinkWithoutStylesheet_IsRemoved(string rel)
    {
        var result = Run($"<link rel=\"{rel}\" href=\"https://tracker.example/\">");

        Assert.Equal(string.Empty, result.Html);
        Assert.False(result.HasRemoteContent);
    }

    // ---- 链接的 title ----

    [Fact]
    [Trait("Scenario", "email-viewing / 正文链接在系统浏览器中打开 / title 显示浏览器实际导航的地址")]
    public void LinkTitle_UsesTheCleanedHref()
    {
        var result = Run("<a href=\"  https://exa&#10;mple.com/pa&#9;th \">x</a>");

        Assert.Contains("title=\"https://example.com/path\"", result.Html);
        Assert.DoesNotContain("title=\"  ", result.Html);
        Assert.Contains("target=\"_blank\"", result.Html);
    }

    // ---- 远程内容漏判 ----

    [Theory]
    [InlineData("<style/>body{background:url(http://evil.example/a.png)}</style><p>x</p>", "<style></style>body{background:url(http://evil.example/a.png)}</style><p>x</p>")]
    [InlineData("<STYLE />@import \"https://evil.example/a.css\";</style><p>x</p>", "<STYLE></STYLE>@import \"https://evil.example/a.css\";</style><p>x</p>")]
    [InlineData("<svg><style/><img src=x onerror=alert(1)></style></svg>", "<svg><style></style><img src=\"x\"></style></svg>")]
    [Trait("Scenario", "email-viewing / 正文在沙箱中渲染 / 自闭合写法的 style 只输出一个空的 style，不进入样式模式")]
    public void SelfClosingStyle_BecomesAnEmptyStyleElement_AndDoesNotStartStyleMode(string html, string expected)
    {
        var result = Run(html);

        // 后面的文字当普通内容处理（转义、清理标签），不会作为 CSS 漏过检查；也就没有远程内容可言
        Assert.Equal(expected, result.Html);
        Assert.False(result.HasRemoteContent);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 正文在沙箱中渲染 / 自闭合写法的 style 之后的真正的 style 照常处理")]
    public void SelfClosingStyle_DoesNotAffectALaterRealStyleElement()
    {
        var result = Run("<style/><p>x</p><style>a{background:url(javascript:x)} b{background:url(http://evil.example/b.png)}</style>");

        Assert.Equal("<style></style><p>x</p><style>a{background:url(\"about:blank\")} b{background:url(http://evil.example/b.png)}</style>", result.Html);
        Assert.True(result.HasRemoteContent);
    }

    // ---- 样式里的 <!-- --> 标记要先去掉再检查地址 ----

    [Theory]
    [InlineData("<style>a{b:url(h<!--ttp://evil.example/c.png)}</style>", "<style>a{b:url(http://evil.example/c.png)}</style>")]
    [InlineData("<style>@import \"ht<!--tp://evil.example/c.css\";</style>", "<style>@import \"http://evil.example/c.css\";</style>")]
    [InlineData("<style>a{b:url(http:/-->/evil.example/c.png)}</style>", "<style>a{b:url(http://evil.example/c.png)}</style>")]
    // 删掉一个标记后两边的字符又拼出新的标记
    [InlineData("<style>a{b:url(h<<!---->!--ttp://evil.example/c.png)}</style>", "<style>a{b:url(http://evil.example/c.png)}</style>")]
    [InlineData("<style>a{b:url(h<!<!----ttp://evil.example/c.png)}</style>", "<style>a{b:url(http://evil.example/c.png)}</style>")]
    [InlineData("<style>a{b:image-set(\"h<!--ttp://evil.example/c.png\" 1x)}</style>", "<style>a{b:image-set(\"http://evil.example/c.png\" 1x)}</style>")]
    [Trait("Scenario", "email-viewing / 远程内容默认不加载 / 样式里被注释标记隔开的远程地址同样识别")]
    public void CommentMarkersInsideStyleText_AreRemovedBeforeTheAddressesAreChecked(string html, string expected)
    {
        var result = Run(html);

        Assert.Equal(expected, result.Html);
        Assert.True(result.HasRemoteContent);
    }

    [Theory]
    [InlineData("<style>a{b:url(http://evil.example/<x>.png)}</style>")]
    [InlineData("<style>@import \"http://evil.example/<x>.css\";</style>")]
    [InlineData("<style>a{b:image-set(\"http://evil.example/<x>.png\" 1x)}</style>")]
    [Trait("Scenario", "email-viewing / 远程内容默认不加载 / 分词器在 < 处拆开的样式文字也按整体检查")]
    public void StyleTextSplitByTheTokenizerAtALessThanSign_IsCheckedAsAWhole(string html)
    {
        var result = Run(html);

        Assert.True(result.HasRemoteContent, result.Html);
        Assert.DoesNotContain("<", result.Html[7..^8]);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 正文在沙箱中渲染 / 被注释标记隔开的危险地址同样被换掉")]
    public void CommentMarkersInsideStyleText_DangerousUrlSplitByAMarkerIsNeutralized()
    {
        var result = Run("<style>a{b:url(java<!--script:alert(1))}</style>");

        Assert.DoesNotContain("javascript", result.Html);
        Assert.Contains("about:blank", result.Html);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 正文在沙箱中渲染 / Outlook 的注释包装仍然被去掉")]
    public void OutlookCommentWrapper_IsStillRemoved()
    {
        var result = Run("<style><!-- a{color:red} --></style>");

        Assert.Equal("<style> a{color:red} </style>", result.Html);
    }

    [Theory]
    [InlineData("<!--")]
    [InlineData("-->")]
    [InlineData("<!--x-->")]
    [Trait("Scenario", "email-viewing / 正文在沙箱中渲染 / 病态样式不拖垮解析（注释标记重复）")]
    public void RepeatedCommentMarkers_AreLinear(string marker)
    {
        var html = "<style>" + string.Concat(Enumerable.Repeat(marker, 250_000)) + "a{}</style>";

        var elapsed = Measure(() => Run(html));

        Assert.True(elapsed < PathologicalLimit, $"耗时 {elapsed.TotalSeconds:F1} 秒");
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 正文在沙箱中渲染 / 病态样式不拖垮解析（层层嵌套的注释标记）")]
    public void NestedCommentMarkers_AreLinear_AndFullyRemoved()
    {
        // <!<!<!...-- --... 每删掉最里面的一个标记，外面的又拼成一个新的标记
        const int depth = 100_000;
        var html = "<style>a{b:url(h" + string.Concat(Enumerable.Repeat("<!", depth)) + string.Concat(Enumerable.Repeat("--", depth)) + "ttp://evil.example/c.png)}</style>";

        var result = default(SanitizedHtml);
        var elapsed = Measure(() => result = Run(html));

        Assert.True(elapsed < PathologicalLimit, $"耗时 {elapsed.TotalSeconds:F1} 秒");
        Assert.True(result.HasRemoteContent);
        Assert.Equal("<style>a{b:url(http://evil.example/c.png)}</style>", result.Html);
    }

    [Theory]
    [InlineData("<div style=\"background-image:image-set('https://evil.example/is.png' 1x)\">a</div>")]
    [InlineData("<div style=\"background-image:-webkit-image-set(&quot;//evil.example/is.png&quot; 1x, url(a.png) 2x)\">a</div>")]
    [InlineData("<style>a{background:IMAGE-SET( \"http://evil.example/is.png\" 1x)}</style>")]
    [Trait("Scenario", "email-viewing / 远程内容默认不加载 / image-set 里的字符串地址是远程内容")]
    public void ImageSetStrings_AreRemoteContent(string html)
    {
        var result = Run(html);

        Assert.True(result.HasRemoteContent, result.Html);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 正文在沙箱中渲染 / image-set 里的危险地址被换掉")]
    public void ImageSetStrings_DangerousUrlsAreNeutralized_OthersAreKept()
    {
        var processed = new CssSanitizer(null).Process("a{b:image-set(\"javascript:alert(1)\" 1x, 'x.png' 2x, \"a)b.png\" 3x)}");

        Assert.Equal("a{b:image-set(\"about:blank\" 1x, 'x.png' 2x, \"a)b.png\" 3x)}", processed);
    }

    [Theory]
    [InlineData("<svg><pattern href=\"https://evil.example/p.svg#p\"/></svg>")]
    [InlineData("<svg><linearGradient xlink:href=\"https://evil.example/g.svg#g\"/></svg>")]
    [InlineData("<svg><radialGradient href=\"//evil.example/g.svg#g\"/></svg>")]
    [InlineData("<svg><filter href=\"https://evil.example/f.svg#f\"/></svg>")]
    [InlineData("<svg><text><textPath href=\"https://evil.example/t.svg#t\">x</textPath></text></svg>")]
    [InlineData("<svg><cursor xlink:href=\"https://evil.example/c.png\"/></svg>")]
    [Trait("Scenario", "email-viewing / 远程内容默认不加载 / SVG 渐变、图案、滤镜的 href 是远程内容")]
    public void SvgReferenceElements_RemoteHrefIsRemoteContent(string html)
    {
        var result = Run(html);

        Assert.True(result.HasRemoteContent, result.Html);
    }

    [Theory]
    [InlineData("<svg><pattern href=\"#p\"/><filter xlink:href=\"#f\"/></svg>")]
    [InlineData("<svg><linearGradient href=\"javascript:alert(1)\"/></svg>")]
    [Trait("Scenario", "email-viewing / 远程内容默认不加载 / SVG 内部引用和危险地址不算远程内容")]
    public void SvgReferenceElements_LocalOrDangerousHrefIsNotRemoteContent(string html)
    {
        var result = Run(html);

        Assert.False(result.HasRemoteContent);
        Assert.DoesNotContain("javascript", result.Html);
    }

    // ---- 链接：SVG 的 xlink:href、MathML 的 href、空 href ----

    [Theory]
    [InlineData("<svg><a xlink:href=\"https://evil.example/\"><text>t</text></a></svg>", "<svg><a xlink:href=\"https://evil.example/\" target=\"_blank\" rel=\"noopener noreferrer\" title=\"https://evil.example/\"><text>t</text></a></svg>")]
    [InlineData("<svg><a xlink:href=\"foo.html\"><text>t</text></a></svg>", "<svg><a xlink:href=\"foo.html\" target=\"_blank\" rel=\"noopener noreferrer\" title=\"foo.html\"><text>t</text></a></svg>")]
    [InlineData("<svg><a xlink:href=\"#x\"><text>t</text></a></svg>", "<svg><a xlink:href=\"about:srcdoc#x\"><text>t</text></a></svg>")]
    [InlineData("<svg><a xlink:href=\"#\"><text>t</text></a></svg>", "<svg><a xlink:href=\"about:srcdoc#\"><text>t</text></a></svg>")]
    [InlineData("<svg><a xlink:href=\"#x\" target=\"_blank\"><text>t</text></a></svg>", "<svg><a xlink:href=\"about:srcdoc#x\"><text>t</text></a></svg>")]
    [InlineData("<svg><a href=\"#y\"><text>t</text></a></svg>", "<svg><a href=\"about:srcdoc#y\"><text>t</text></a></svg>")]
    [InlineData("<svg><a xlink:href=\"\"><text>t</text></a></svg>", "<svg><a><text>t</text></a></svg>")]
    [InlineData("<svg><a xlink:href=\"  \"><text>t</text></a></svg>", "<svg><a><text>t</text></a></svg>")]
    [InlineData("<svg><a xlink:href><text>t</text></a></svg>", "<svg><a><text>t</text></a></svg>")]
    [InlineData("<svg><a xlink:href=\"javascript:alert(1)\"><text>t</text></a></svg>", "<svg><a><text>t</text></a></svg>")]
    [Trait("Scenario", "email-viewing / 正文链接在系统浏览器中打开 / SVG 里的 a 用 xlink:href 时和 href 同样处理")]
    public void SvgAnchorXlinkHref_IsHandledLikeHref(string html, string expected)
    {
        var result = Run(html);

        Assert.Equal(expected, result.Html);
    }

    [Theory]
    // href 优先：href 是锚点就是页内跳转，xlink:href 照样处理
    [InlineData("<svg><a href=\"#x\" xlink:href=\"https://evil.example/\"><text>t</text></a></svg>", "<svg><a href=\"about:srcdoc#x\" xlink:href=\"https://evil.example/\"><text>t</text></a></svg>")]
    [InlineData("<svg><a xlink:href=\"#x\" href=\"#y\" target=\"_blank\"><text>t</text></a></svg>", "<svg><a xlink:href=\"about:srcdoc#x\" href=\"about:srcdoc#y\"><text>t</text></a></svg>")]
    // href 是外部地址：加 target、rel，title 取 href；xlink:href 是锚点也改写
    [InlineData("<svg><a xlink:href=\"#x\" href=\"https://a.example/p\"><text>t</text></a></svg>", "<svg><a xlink:href=\"about:srcdoc#x\" href=\"https://a.example/p\" target=\"_blank\" rel=\"noopener noreferrer\" title=\"https://a.example/p\"><text>t</text></a></svg>")]
    // 一个为空，另一个照常
    [InlineData("<svg><a href=\"\" xlink:href=\"https://a.example/\"><text>t</text></a></svg>", "<svg><a xlink:href=\"https://a.example/\" target=\"_blank\" rel=\"noopener noreferrer\" title=\"https://a.example/\"><text>t</text></a></svg>")]
    [InlineData("<svg><a href=\"https://a.example/\" xlink:href=\"\"><text>t</text></a></svg>", "<svg><a href=\"https://a.example/\" target=\"_blank\" rel=\"noopener noreferrer\" title=\"https://a.example/\"><text>t</text></a></svg>")]
    // 一个危险（删掉），另一个照常
    [InlineData("<svg><a href=\"javascript:alert(1)\" xlink:href=\"#x\"><text>t</text></a></svg>", "<svg><a xlink:href=\"about:srcdoc#x\"><text>t</text></a></svg>")]
    [Trait("Scenario", "email-viewing / 正文链接在系统浏览器中打开 / SVG 的 a 同时有 href 和 xlink:href 时两个都处理")]
    public void SvgAnchorWithBothHrefAttributes_BothAreHandled(string html, string expected)
    {
        var result = Run(html);

        Assert.Equal(expected, result.Html);
    }

    [Theory]
    [InlineData("<math><mi href=\"foo\">x</mi><mtext href=\"#x\">y</mtext></math>", "<math><mi>x</mi><mtext>y</mtext></math>")]
    [InlineData("<math href=\"foo\"><mrow xlink:href=\"foo\" HREF=\"bar\">x</mrow></math>", "<math><mrow>x</mrow></math>")]
    [InlineData("<math><mrow><a href=\"https://a.example/\">x</a></mrow></math>", "<math><mrow><a>x</a></mrow></math>")]
    [InlineData("<math><mi href=\"\">x</mi></math>", "<math><mi>x</mi></math>")]
    [InlineData("<math><mi class=\"c\" href=\"foo\" id=\"i\">x</mi></math>", "<math><mi class=\"c\" id=\"i\">x</mi></math>")]
    [Trait("Scenario", "email-viewing / 正文链接在系统浏览器中打开 / MathML 元素上的 href 一律删掉")]
    public void HrefOnMathMlElements_IsRemoved(string html, string expected)
    {
        var result = Run(html);

        Assert.Equal(expected, result.Html);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 正文链接在系统浏览器中打开 / MathML 之外的链接照常处理")]
    public void LinksOutsideMathMl_AreHandledNormally()
    {
        var result = Run("<a href=\"https://a.example/1\">before</a><math><mi href=\"foo\">x</mi></math><a href=\"https://a.example/2\">after</a><math/><a href=\"#z\">self-closed</a>");

        Assert.Equal(
            "<a href=\"https://a.example/1\" target=\"_blank\" rel=\"noopener noreferrer\" title=\"https://a.example/1\">before</a>"
            + "<math><mi>x</mi></math>"
            + "<a href=\"https://a.example/2\" target=\"_blank\" rel=\"noopener noreferrer\" title=\"https://a.example/2\">after</a>"
            + "<math/><a href=\"about:srcdoc#z\">self-closed</a>",
            result.Html);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 正文链接在系统浏览器中打开 / 嵌套的 math 要全部关闭后才恢复链接")]
    public void NestedMath_StripsHrefUntilTheOutermostEnds()
    {
        var result = Run("<math><mrow><math><mi href=\"a\">1</mi></math><mi href=\"b\">2</mi></mrow></math><span href=\"c\">3</span><a href=\"d\">4</a>");

        Assert.Equal(
            "<math><mrow><math><mi>1</mi></math><mi>2</mi></mrow></math><span href=\"c\">3</span>"
            + "<a href=\"d\" target=\"_blank\" rel=\"noopener noreferrer\" title=\"d\">4</a>",
            result.Html);
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
