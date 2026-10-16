using System.Text;
using System.Text.RegularExpressions;
using MimeKit.Text;

namespace EmailFileViewer.Core.Parsing;

/// <summary>
/// 清理后的 HTML。
/// </summary>
/// <param name="Html">清理后的 HTML。</param>
/// <param name="HasRemoteContent">正文引用了需要联网的资源。</param>
internal readonly record struct SanitizedHtml(string Html, bool HasRemoteContent);

/// <summary>
/// 用 MimeKit 的 HTML 分词器清理邮件 HTML，作为沙箱 iframe 之外的第二道防线。
/// </summary>
/// <remarks>
/// <para>做的事：删除脚本、事件属性、危险地址（<c>javascript:</c>、<c>vbscript:</c>、非图片的 <c>data:</c> 等）和会加载外部文档的元素；
/// 把 <c>cid:</c> 与 MSG 中按文件名引用的内嵌图片换成 <c>data:</c> URI；检测远程资源；给链接加上 <c>target</c>、<c>rel</c> 和显示实际地址的 <c>title</c>。</para>
/// <para>输出按规范重新序列化（不用 <c>HtmlWriter</c>：它会把所有非 ASCII 字符写成数字实体，中文正文会膨胀好几倍）：
/// 标签名和属性名必须合法，属性值一律加引号并转义，文本一律转义，样式表只输出不含 <c>&lt;</c> 的原始文本。
/// 注释、DOCTYPE 和原始文本元素（脚本、iframe、noscript 等）整个丢弃，避免分词器与浏览器解析不一致造成的绕过。</para>
/// </remarks>
internal sealed partial class HtmlSanitizer
{
    // 连同内容一起删除
    private static readonly HashSet<string> RemoveWithContent = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "iframe", "noscript", "noembed", "noframes", "xmp", "template",
    };

    // 只删除标签本身，里面的内容（例如 object 的备用内容）保留
    private static readonly HashSet<string> RemoveTagOnly = new(StringComparer.OrdinalIgnoreCase)
    {
        "base", "frame", "frameset", "object", "embed", "applet", "param", "plaintext",
        "animate", "animatemotion", "animatetransform", "animatecolor", "set", "portal",
    };

    // 值是地址、会被加载的属性
    private static readonly HashSet<string> LoadingUrlAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "src", "background", "poster", "lowsrc", "dynsrc", "data", "codebase",
    };

    // 值是地址、点击后跳转的属性
    private static readonly HashSet<string> NavigationUrlAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "href", "xlink:href", "cite", "longdesc", "usemap", "profile",
    };

    // 无论值是什么都删除的属性
    private static readonly HashSet<string> RemovedAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "action", "formaction", "ping", "srcdoc", "manifest",
    };

    // href 指向要加载的资源（样式表、SVG 图片），而不是点击跳转
    private static readonly HashSet<string> LoadingHrefElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "link", "image", "use", "feimage", "mpath",
    };

    private readonly InlineResourceResolver? _resolver;
    private readonly CssSanitizer _css;
    private bool _hasRemoteContent;

    private HtmlSanitizer(InlineResourceResolver? resolver)
    {
        _resolver = resolver;
        _css = new CssSanitizer(resolver);
    }

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_:.-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidName();

    /// <summary>
    /// 清理一段 HTML。
    /// </summary>
    /// <param name="html">邮件里的 HTML，可以是完整文档也可以是片段。</param>
    /// <param name="resolver">解析内嵌资源的对象；没有内嵌资源可用时为 <c>null</c>。</param>
    /// <returns>清理后的 HTML，以及是否引用了远程资源。</returns>
    internal static SanitizedHtml Sanitize(string html, InlineResourceResolver? resolver)
    {
        var sanitizer = new HtmlSanitizer(resolver);
        var output = sanitizer.Run(html);
        return new SanitizedHtml(output, sanitizer._hasRemoteContent || sanitizer._css.HasRemoteContent);
    }

    private string Run(string html)
    {
        var builder = new StringBuilder(html.Length);
        var tokenizer = new HtmlTokenizer(new StringReader(html)) { DecodeCharacterReferences = true };

        string? skipUntilEndOf = null;
        var inStyle = false;

        while (tokenizer.ReadNextToken(out var token))
        {
            if (skipUntilEndOf is not null)
            {
                if (token is HtmlTagToken { IsEndTag: true } endTag && string.Equals(endTag.Name, skipUntilEndOf, StringComparison.OrdinalIgnoreCase))
                {
                    skipUntilEndOf = null;
                }

                continue;
            }

            switch (token)
            {
                case HtmlTagToken tag:
                    HandleTag(builder, tag, ref skipUntilEndOf, ref inStyle);
                    break;
                case HtmlScriptDataToken:
                    break;
                case HtmlDataToken data:
                    if (inStyle)
                    {
                        builder.Append(ToRawStyleText(_css.Process(data.Data)));
                    }
                    else
                    {
                        AppendEscaped(builder, data.Data, isAttribute: false);
                    }

                    break;
                default:
                    // 注释、DOCTYPE、CDATA 一律丢弃
                    break;
            }
        }

        return builder.ToString();
    }

    private void HandleTag(StringBuilder builder, HtmlTagToken tag, ref string? skipUntilEndOf, ref bool inStyle)
    {
        var name = tag.Name;
        if (!ValidName().IsMatch(name))
        {
            return;
        }

        if (tag.IsEndTag)
        {
            if (RemoveWithContent.Contains(name) || RemoveTagOnly.Contains(name) || IsDroppedLink(name, tag))
            {
                return;
            }

            if (name.Equals("style", StringComparison.OrdinalIgnoreCase))
            {
                inStyle = false;
            }

            builder.Append("</").Append(name).Append('>');
            return;
        }

        if (RemoveWithContent.Contains(name))
        {
            if (!tag.IsEmptyElement)
            {
                skipUntilEndOf = name;
            }

            return;
        }

        if (RemoveTagOnly.Contains(name) || ShouldDropElement(name, tag))
        {
            return;
        }

        var attributes = SanitizeAttributes(name, tag);

        if (name.Equals("style", StringComparison.OrdinalIgnoreCase) && !tag.IsEmptyElement)
        {
            inStyle = true;
        }

        builder.Append('<').Append(name);
        foreach (var (attributeName, attributeValue) in attributes)
        {
            builder.Append(' ').Append(attributeName);
            if (attributeValue is not null)
            {
                builder.Append("=\"");
                AppendEscaped(builder, attributeValue, isAttribute: true);
                builder.Append('"');
            }
        }

        builder.Append(tag.IsEmptyElement ? "/>" : ">");
    }

    // 样式表是原始文本，浏览器不会解码实体。先去掉 Outlook 常见的 <!-- --> 包装（CSS 里它们没有意义，留着还会让第一条规则失效），
    // 再把剩下的每个 < 转义成 CSS 的 \3C，保证输出里没有 <，免得在 SVG 等上下文里被浏览器当成标签
    private static string ToRawStyleText(string css)
    {
        return css
            .Replace("<!--", string.Empty, StringComparison.Ordinal)
            .Replace("-->", string.Empty, StringComparison.Ordinal)
            .Replace("<", "\\3C ", StringComparison.Ordinal);
    }

    // 文本里转义 & < >；属性值里还要转义双引号。不间断空格写成实体，免得源码里看不见
    private static void AppendEscaped(StringBuilder builder, string value, bool isAttribute)
    {
        foreach (var c in value)
        {
            switch (c)
            {
                case '&':
                    builder.Append("&amp;");
                    break;
                case '<':
                    builder.Append("&lt;");
                    break;
                case '>':
                    builder.Append("&gt;");
                    break;
                case '"' when isAttribute:
                    builder.Append("&quot;");
                    break;
                case '\u00A0':
                    builder.Append("&nbsp;");
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }
    }

    // meta 只允许不带 http-equiv 的（refresh、set-cookie 等都不要）；link 只允许样式表
    private static bool ShouldDropElement(string name, HtmlTagToken tag)
    {
        if (name.Equals("meta", StringComparison.OrdinalIgnoreCase))
        {
            return tag.Attributes.Any(a => a.Name.Equals("http-equiv", StringComparison.OrdinalIgnoreCase));
        }

        return IsDroppedLink(name, tag);
    }

    private static bool IsDroppedLink(string name, HtmlTagToken tag)
    {
        if (!name.Equals("link", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rel = tag.Attributes.FirstOrDefault(a => a.Name.Equals("rel", StringComparison.OrdinalIgnoreCase))?.Value;
        return rel is null || !rel.Split([' ', '\t', '\n', '\r', '\f'], StringSplitOptions.RemoveEmptyEntries)
            .Contains("stylesheet", StringComparer.OrdinalIgnoreCase);
    }

    private List<(string Name, string? Value)> SanitizeAttributes(string tagName, HtmlTagToken tag)
    {
        var result = new List<(string Name, string? Value)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var attribute in tag.Attributes)
        {
            var name = attribute.Name;

            // 重复的属性以第一个为准，和浏览器一致
            if (!ValidName().IsMatch(name) || !seen.Add(name))
            {
                continue;
            }

            if (name.StartsWith("on", StringComparison.OrdinalIgnoreCase) || RemovedAttributes.Contains(name))
            {
                continue;
            }

            var value = attribute.Value;
            if (value is not null)
            {
                value = SanitizeAttributeValue(tagName, name, value, out var keep);
                if (!keep)
                {
                    continue;
                }
            }

            result.Add((name, value));
        }

        if (tagName.Equals("a", StringComparison.OrdinalIgnoreCase) || tagName.Equals("area", StringComparison.OrdinalIgnoreCase))
        {
            ApplyLinkAttributes(result);
        }

        return result;
    }

    private string SanitizeAttributeValue(string tagName, string name, string value, out bool keep)
    {
        keep = true;

        if (name.Equals("style", StringComparison.OrdinalIgnoreCase))
        {
            return _css.Process(value);
        }

        if (name.Equals("srcset", StringComparison.OrdinalIgnoreCase))
        {
            var srcset = SanitizeSrcset(value);
            keep = srcset.Length > 0;
            return srcset;
        }

        var isLoadingHref = LoadingHrefElements.Contains(tagName)
            && (name.Equals("href", StringComparison.OrdinalIgnoreCase) || name.Equals("xlink:href", StringComparison.OrdinalIgnoreCase));

        if (LoadingUrlAttributes.Contains(name) || isLoadingHref)
        {
            return SanitizeLoadingUrl(value, out keep);
        }

        if (NavigationUrlAttributes.Contains(name))
        {
            var kind = UrlClassifier.Classify(value);
            keep = kind is not (UrlKind.Dangerous or UrlKind.DataImage);
            return value;
        }

        return value;
    }

    // 会被加载的地址：危险的删掉，cid / 文件名引用换成 data:，远程的记下来
    private string SanitizeLoadingUrl(string value, out bool keep)
    {
        keep = true;
        switch (UrlClassifier.Classify(value))
        {
            case UrlKind.Dangerous:
                keep = false;
                return value;
            case UrlKind.Remote:
                _hasRemoteContent = true;
                return value;
            case UrlKind.Cid or UrlKind.Relative:
                return _resolver?.TryResolve(value) ?? value;
            default:
                return value;
        }
    }

    // srcset：逗号分隔的“地址 描述符”列表。地址本身可能含逗号（data: URI），所以按空白切分地址
    private string SanitizeSrcset(string value)
    {
        var candidates = new List<string>();
        var span = value.AsSpan();
        var i = 0;
        while (i < span.Length)
        {
            while (i < span.Length && (char.IsWhiteSpace(span[i]) || span[i] == ','))
            {
                i++;
            }

            var urlStart = i;
            while (i < span.Length && !char.IsWhiteSpace(span[i]))
            {
                i++;
            }

            var url = span[urlStart..i].ToString();
            var trailingCommas = url.Length - url.TrimEnd(',').Length;
            url = url.TrimEnd(',');

            var descriptor = string.Empty;
            if (trailingCommas == 0)
            {
                var descriptorStart = i;
                while (i < span.Length && span[i] != ',')
                {
                    i++;
                }

                descriptor = span[descriptorStart..i].ToString().Trim();
            }

            if (url.Length == 0)
            {
                continue;
            }

            var sanitized = SanitizeLoadingUrl(url, out var keep);
            if (keep)
            {
                candidates.Add(descriptor.Length == 0 ? sanitized : $"{sanitized} {descriptor}");
            }
        }

        return string.Join(", ", candidates);
    }

    // 链接：在新窗口打开，不带来源信息，title 写成实际地址，这样悬停时不需要脚本也能看到真实地址
    private static void ApplyLinkAttributes(List<(string Name, string? Value)> attributes)
    {
        var href = attributes.FirstOrDefault(a => a.Name.Equals("href", StringComparison.OrdinalIgnoreCase)).Value;
        if (string.IsNullOrWhiteSpace(href) || UrlClassifier.Classify(href) == UrlKind.Fragment)
        {
            return;
        }

        attributes.RemoveAll(a => a.Name.Equals("target", StringComparison.OrdinalIgnoreCase)
            || a.Name.Equals("rel", StringComparison.OrdinalIgnoreCase)
            || a.Name.Equals("title", StringComparison.OrdinalIgnoreCase));
        attributes.Add(("target", "_blank"));
        attributes.Add(("rel", "noopener noreferrer"));
        attributes.Add(("title", href));
    }
}
