using System.Text;
using System.Text.RegularExpressions;

namespace EmailFileViewer.Core.Parsing;

/// <summary>
/// 处理样式表和 <c>style</c> 属性里的地址：<c>url()</c> 与 <c>@import</c>。
/// </summary>
/// <remarks>
/// 危险地址换成 <c>about:blank</c>，<c>cid:</c> 和 MSG 里按文件名引用的内嵌图片换成 <c>data:</c> URI，
/// 需要联网的地址保持不变并记为远程内容。不展开 CSS 转义（如 <c>\75rl(</c>），这只是第二道防线，真正的边界是沙箱和 CSP。
/// <c>url()</c> 用手写的单遍扫描找，不用正则：恶意构造的样式能让回溯式正则的耗时随长度平方增长。
/// </remarks>
internal sealed partial class CssSanitizer
{
    /// <summary>
    /// 单段样式（一个 <c>style</c> 元素的文本或一个 <c>style</c> 属性的值）最多处理的字符数，超过就整段丢弃。
    /// 正常邮件的样式表在几十 KB 以内；1 MiB 已经远超实际需要，又不会让一封邮件的样式处理变成负担。
    /// </summary>
    internal const int MaxStyleTextLength = 1024 * 1024;

    private const string BlankUrl = "url(\"about:blank\")";

    private const string UrlOpen = "url(";

    private const string ImageSetOpen = "image-set(";

    private readonly InlineResourceResolver? _resolver;

    internal CssSanitizer(InlineResourceResolver? resolver)
    {
        _resolver = resolver;
    }

    /// <summary>
    /// 样式里是否出现了需要联网的地址。
    /// </summary>
    internal bool HasRemoteContent { get; private set; }

    [GeneratedRegex("""@import\s+(?:"(?<d>[^"]*)"|'(?<s>[^']*)')""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ImportString();

    [GeneratedRegex(@"expression\s*\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Expression();

    [GeneratedRegex(@"(behaviou?r|-moz-binding)\s*:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LegacyBinding();

    /// <summary>
    /// 处理一段 CSS。
    /// </summary>
    /// <param name="css">样式表文本或 <c>style</c> 属性的值。</param>
    /// <returns>处理后的 CSS。</returns>
    internal string Process(string css)
    {
        if (css.Length == 0)
        {
            return css;
        }

        // 太长的样式整段丢弃：不处理，也不输出
        if (css.Length > MaxStyleTextLength)
        {
            return string.Empty;
        }

        var result = ReplaceUrlFunctions(css);
        result = ProcessImageSets(result);
        result = ImportString().Replace(result, ReplaceImportString);
        result = Expression().Replace(result, "x-removed(");
        result = LegacyBinding().Replace(result, "x-removed:");
        return result;
    }

    // 逐个找出 url(...)，匹配规则和旧正则 url\(\s*("..."|'...'|不含引号和空白开头的内容)\s*\) 完全一致（测试里随机对比过）。
    // 总耗时与长度成正比：不带引号的地址，后面没有 ")" 时用最后一个 ")" 的位置直接判定失败，有 ")" 就一定匹配成功并跳过整段；
    // 带引号的地址，每个起点只扫到下一个相同的引号
    private string ReplaceUrlFunctions(string css)
    {
        StringBuilder? builder = null;
        var copiedUntil = 0;
        var searchFrom = 0;
        var lastCloseParen = css.LastIndexOf(')');

        while (searchFrom < css.Length)
        {
            var start = css.IndexOf(UrlOpen, searchFrom, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                break;
            }

            if (!TryMatchUrl(css, start, lastCloseParen, out var end, out var urlStart, out var urlLength))
            {
                searchFrom = start + 1;
                continue;
            }

            var replacement = ResolveUrlFunction(css.Substring(urlStart, urlLength));
            if (replacement is not null)
            {
                builder ??= new StringBuilder(css.Length);
                builder.Append(css, copiedUntil, start - copiedUntil).Append(replacement);
                copiedUntil = end;
            }

            searchFrom = end;
        }

        if (builder is null)
        {
            return css;
        }

        return builder.Append(css, copiedUntil, css.Length - copiedUntil).ToString();
    }

    private static bool TryMatchUrl(string css, int start, int lastCloseParen, out int end, out int urlStart, out int urlLength)
    {
        end = 0;
        urlStart = 0;
        urlLength = 0;

        var index = start + UrlOpen.Length;
        while (index < css.Length && char.IsWhiteSpace(css[index]))
        {
            index++;
        }

        if (index >= css.Length)
        {
            return false;
        }

        var first = css[index];
        if (first is '"' or '\'')
        {
            var closeQuote = css.IndexOf(first, index + 1);
            if (closeQuote < 0)
            {
                return false;
            }

            var afterQuote = closeQuote + 1;
            while (afterQuote < css.Length && char.IsWhiteSpace(css[afterQuote]))
            {
                afterQuote++;
            }

            if (afterQuote >= css.Length || css[afterQuote] != ')')
            {
                return false;
            }

            urlStart = index + 1;
            urlLength = closeQuote - urlStart;
            end = afterQuote + 1;
            return true;
        }

        // 不带引号：到第一个 ")" 为止，开头不能是 ")"，末尾的空白不算地址
        if (first == ')' || index > lastCloseParen)
        {
            return false;
        }

        var closeParen = css.IndexOf(')', index);
        var urlEnd = closeParen;
        while (urlEnd > index + 1 && char.IsWhiteSpace(css[urlEnd - 1]))
        {
            urlEnd--;
        }

        urlStart = index;
        urlLength = urlEnd - index;
        end = closeParen + 1;
        return true;
    }

    // image-set() 里除了 url()，还可以直接写字符串地址（image-set("a.png" 1x)），带 -webkit- 前缀的写法同样适用。
    // 括号里的每个字符串都按地址判断：危险的换成 about:blank，远程的记为远程内容。每个函数只扫描一遍，嵌套的 image-set( 包含在外层的扫描范围里
    private string ProcessImageSets(string css)
    {
        var functionStart = css.IndexOf(ImageSetOpen, StringComparison.OrdinalIgnoreCase);
        if (functionStart < 0)
        {
            return css;
        }

        StringBuilder? builder = null;
        var copiedUntil = 0;

        while (functionStart >= 0)
        {
            var index = functionStart + ImageSetOpen.Length;
            var depth = 1;
            while (index < css.Length && depth > 0)
            {
                var current = css[index];
                if (current is '"' or '\'')
                {
                    var closeQuote = FindClosingQuote(css, index);
                    if (closeQuote < css.Length)
                    {
                        var literal = css.Substring(index + 1, closeQuote - index - 1);
                        switch (UrlClassifier.Classify(literal))
                        {
                            case UrlKind.Dangerous:
                                builder ??= new StringBuilder(css.Length);
                                builder.Append(css, copiedUntil, index + 1 - copiedUntil).Append("about:blank");
                                copiedUntil = closeQuote;
                                break;
                            case UrlKind.Remote:
                                HasRemoteContent = true;
                                break;
                        }
                    }

                    index = closeQuote + 1;
                    continue;
                }

                if (current == '(')
                {
                    depth++;
                }
                else if (current == ')')
                {
                    depth--;
                }

                index++;
            }

            functionStart = index >= css.Length ? -1 : css.IndexOf(ImageSetOpen, index, StringComparison.OrdinalIgnoreCase);
        }

        return builder is null ? css : builder.Append(css, copiedUntil, css.Length - copiedUntil).ToString();
    }

    // 返回结束引号的位置，反斜杠转义的字符跳过；没有结束引号时返回字符串长度
    private static int FindClosingQuote(string css, int openQuote)
    {
        var quote = css[openQuote];
        for (var i = openQuote + 1; i < css.Length; i++)
        {
            if (css[i] == '\\')
            {
                i++;
            }
            else if (css[i] == quote)
            {
                return i;
            }
        }

        return css.Length;
    }

    // 返回 null 表示保持原样
    private string? ResolveUrlFunction(string url)
    {
        switch (UrlClassifier.Classify(url))
        {
            case UrlKind.Dangerous:
                return BlankUrl;
            case UrlKind.Remote:
                HasRemoteContent = true;
                return null;
            case UrlKind.Cid or UrlKind.Relative:
                if (_resolver is null)
                {
                    return null;
                }

                var dataUri = _resolver.TryResolve(url, out var overBudget);
                if (dataUri is not null)
                {
                    return $"url(\"{dataUri}\")";
                }

                // 内嵌图片超出总量上限：这一处引用不再内嵌，样式里的地址换成空白地址
                return overBudget ? BlankUrl : null;
            default:
                return null;
        }
    }

    private string ReplaceImportString(Match match)
    {
        switch (UrlClassifier.Classify(FirstGroup(match)))
        {
            case UrlKind.Dangerous:
                return "@import \"about:blank\"";
            case UrlKind.Remote:
                HasRemoteContent = true;
                return match.Value;
            default:
                return match.Value;
        }
    }

    private static string FirstGroup(Match match)
    {
        foreach (var name in (string[])["d", "s", "u"])
        {
            if (match.Groups[name].Success)
            {
                return match.Groups[name].Value;
            }
        }

        return string.Empty;
    }
}
