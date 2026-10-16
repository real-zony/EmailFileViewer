using System.Text.RegularExpressions;

namespace EmailFileViewer.Core.Parsing;

/// <summary>
/// 处理样式表和 <c>style</c> 属性里的地址：<c>url()</c> 与 <c>@import</c>。
/// </summary>
/// <remarks>
/// 危险地址换成 <c>about:blank</c>，<c>cid:</c> 和 MSG 里按文件名引用的内嵌图片换成 <c>data:</c> URI，
/// 需要联网的地址保持不变并记为远程内容。不展开 CSS 转义（如 <c>\75rl(</c>），这只是第二道防线，真正的边界是沙箱和 CSP。
/// </remarks>
internal sealed partial class CssSanitizer
{
    private const string BlankUrl = "url(\"about:blank\")";

    private readonly InlineResourceResolver? _resolver;

    internal CssSanitizer(InlineResourceResolver? resolver)
    {
        _resolver = resolver;
    }

    /// <summary>
    /// 样式里是否出现了需要联网的地址。
    /// </summary>
    internal bool HasRemoteContent { get; private set; }

    [GeneratedRegex("""url\(\s*(?:"(?<d>[^"]*)"|'(?<s>[^']*)'|(?<u>[^)'"\s][^)]*?))\s*\)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlFunction();

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

        var result = UrlFunction().Replace(css, ReplaceUrlFunction);
        result = ImportString().Replace(result, ReplaceImportString);
        result = Expression().Replace(result, "x-removed(");
        result = LegacyBinding().Replace(result, "x-removed:");
        return result;
    }

    private string ReplaceUrlFunction(Match match)
    {
        var url = FirstGroup(match);
        switch (UrlClassifier.Classify(url))
        {
            case UrlKind.Dangerous:
                return BlankUrl;
            case UrlKind.Remote:
                HasRemoteContent = true;
                return match.Value;
            case UrlKind.Cid or UrlKind.Relative:
                var dataUri = _resolver?.TryResolve(url);
                return dataUri is null ? match.Value : $"url(\"{dataUri}\")";
            default:
                return match.Value;
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
