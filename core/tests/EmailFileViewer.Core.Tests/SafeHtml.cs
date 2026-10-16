using EmailFileViewer.Core.Parsing;
using MimeKit.Text;

namespace EmailFileViewer.Core.Tests;

/// <summary>
/// 清理后 HTML 的结构化检查：用 MimeKit 的分词器重新解析输出，逐个标签和属性检查，不靠字符串匹配。
/// </summary>
internal static class SafeHtml
{
    private static readonly string[] ForbiddenTags =
    [
        "script", "iframe", "frame", "frameset", "object", "embed", "applet", "base", "noscript", "template", "param",
    ];

    private static readonly string[] UrlAttributes =
    [
        "href", "src", "action", "formaction", "xlink:href", "background", "poster", "data", "srcset", "ping", "manifest",
    ];

    /// <summary>
    /// 断言 HTML 里没有脚本、事件属性、危险地址和被禁止的元素。
    /// </summary>
    /// <param name="html">清理后的 HTML。</param>
    public static void AssertSafe(string html)
    {
        var tokenizer = new HtmlTokenizer(new StringReader(html));
        var inStyle = false;
        while (tokenizer.ReadNextToken(out var token))
        {
            switch (token)
            {
                case HtmlTagToken tag when !tag.IsEndTag:
                    Assert.DoesNotContain(ForbiddenTags, forbidden => forbidden.Equals(tag.Name, StringComparison.OrdinalIgnoreCase));
                    AssertAttributes(tag);
                    inStyle = tag.Name.Equals("style", StringComparison.OrdinalIgnoreCase);
                    break;
                case HtmlTagToken:
                    inStyle = false;
                    break;
                case HtmlScriptDataToken:
                    Assert.Fail("输出里不应该有脚本内容");
                    break;
                case HtmlDataToken data when inStyle:
                    AssertSafeCss(data.Data);
                    break;
                case HtmlCommentToken:
                    Assert.Fail("输出里不应该有注释");
                    break;
            }
        }
    }

    private static void AssertAttributes(HtmlTagToken tag)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var attribute in tag.Attributes)
        {
            Assert.DoesNotContain("on", attribute.Name[..Math.Min(2, attribute.Name.Length)], StringComparison.OrdinalIgnoreCase);
            Assert.True(names.Add(attribute.Name), $"属性 {attribute.Name} 重复");

            if (attribute.Name.Equals("style", StringComparison.OrdinalIgnoreCase) && attribute.Value is not null)
            {
                AssertSafeCss(attribute.Value);
            }

            if (UrlAttributes.Contains(attribute.Name, StringComparer.OrdinalIgnoreCase) && attribute.Value is not null)
            {
                foreach (var url in attribute.Value.Split(',', ' '))
                {
                    Assert.NotEqual(UrlKind.Dangerous, UrlClassifier.Classify(url));
                }
            }
        }

        if (tag.Name.Equals("meta", StringComparison.OrdinalIgnoreCase))
        {
            Assert.DoesNotContain(tag.Attributes, a => a.Name.Equals("http-equiv", StringComparison.OrdinalIgnoreCase));
        }
    }

    private static void AssertSafeCss(string css)
    {
        Assert.DoesNotContain("javascript:", css, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("vbscript:", css, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expression(", css, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<", css, StringComparison.Ordinal);
    }
}
