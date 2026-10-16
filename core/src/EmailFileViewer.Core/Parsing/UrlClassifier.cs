namespace EmailFileViewer.Core.Parsing;

/// <summary>
/// 链接地址的种类。
/// </summary>
internal enum UrlKind
{
    /// <summary>空白。</summary>
    Empty,

    /// <summary>只有 <c>#</c> 片段，指向文档内部。</summary>
    Fragment,

    /// <summary>没有协议的相对地址（文件名、路径）。</summary>
    Relative,

    /// <summary><c>cid:</c> 内嵌资源。</summary>
    Cid,

    /// <summary>需要联网的地址：http、https 或以 <c>//</c> 开头。</summary>
    Remote,

    /// <summary>允许的图片 <c>data:</c> URI。</summary>
    DataImage,

    /// <summary>会执行脚本或访问本地资源的地址，必须删除。</summary>
    Dangerous,

    /// <summary>其他协议，例如 mailto、tel。</summary>
    Other,
}

/// <summary>
/// 判断地址的种类。判断前先按浏览器的规则整理：去掉首尾的控制字符和空格，去掉中间的制表符和换行。
/// </summary>
internal static class UrlClassifier
{
    private static readonly HashSet<string> DangerousSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "javascript", "vbscript", "livescript", "mocha", "file", "jar",
    };

    // 允许出现在 src 等位置的 data: 图片类型
    private static readonly HashSet<string> AllowedDataImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/jpeg", "image/jpg", "image/pjpeg", "image/gif", "image/webp", "image/bmp",
        "image/x-icon", "image/vnd.microsoft.icon", "image/avif", "image/apng", "image/svg+xml",
    };

    /// <summary>
    /// 判断地址的种类。
    /// </summary>
    /// <param name="url">地址，已经解码过 HTML 实体。</param>
    /// <returns>种类。</returns>
    internal static UrlKind Classify(string? url)
    {
        var cleaned = Clean(url);
        if (cleaned.Length == 0)
        {
            return UrlKind.Empty;
        }

        if (cleaned[0] == '#')
        {
            return UrlKind.Fragment;
        }

        // 浏览器把反斜杠当成斜杠，\\host 和 //host 一样是协议相对地址
        if (cleaned.Length >= 2 && cleaned[0] is '/' or '\\' && cleaned[1] is '/' or '\\')
        {
            return UrlKind.Remote;
        }

        var scheme = GetScheme(cleaned);
        if (scheme is null)
        {
            return UrlKind.Relative;
        }

        if (scheme.Equals("cid", StringComparison.OrdinalIgnoreCase))
        {
            return UrlKind.Cid;
        }

        if (scheme.Equals("http", StringComparison.OrdinalIgnoreCase) || scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
        {
            return UrlKind.Remote;
        }

        if (scheme.Equals("data", StringComparison.OrdinalIgnoreCase))
        {
            return IsAllowedDataImage(cleaned) ? UrlKind.DataImage : UrlKind.Dangerous;
        }

        return DangerousSchemes.Contains(scheme) ? UrlKind.Dangerous : UrlKind.Other;
    }

    /// <summary>
    /// 去掉首尾的控制字符和空格，以及中间的制表符和换行（浏览器解析地址时也会忽略它们）。
    /// </summary>
    /// <param name="url">地址。</param>
    /// <returns>整理后的地址。</returns>
    internal static string Clean(string? url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return string.Empty;
        }

        var span = url.AsSpan();
        var start = 0;
        var end = span.Length;
        while (start < end && span[start] <= ' ')
        {
            start++;
        }

        while (end > start && span[end - 1] <= ' ')
        {
            end--;
        }

        var trimmed = span[start..end];
        return trimmed.IndexOfAny('\t', '\n', '\r') < 0
            ? trimmed.ToString()
            : trimmed.ToString().Replace("\t", string.Empty).Replace("\n", string.Empty).Replace("\r", string.Empty);
    }

    // 协议名：字母开头，后面是字母、数字、加号、减号、点，然后是冒号
    private static string? GetScheme(string url)
    {
        var colon = url.IndexOf(':');
        if (colon <= 0 || !char.IsAsciiLetter(url[0]))
        {
            return null;
        }

        foreach (var c in url.AsSpan(0, colon))
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '+' or '-' or '.'))
            {
                return null;
            }
        }

        return url[..colon];
    }

    private static bool IsAllowedDataImage(string url)
    {
        var rest = url.AsSpan("data:".Length);
        var end = rest.IndexOfAny(';', ',');
        if (end < 0)
        {
            return false;
        }

        return AllowedDataImageTypes.Contains(rest[..end].Trim().ToString());
    }
}
