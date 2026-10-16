using System.Text;

namespace EmailFileViewer.Core.Parsing;

/// <summary>
/// 能被正文引用的内嵌资源（图片）。
/// </summary>
/// <param name="Source">对应的附件来源，被引用后会标记为“已内嵌”，不再出现在附件列表里。</param>
/// <param name="ContentId">Content-ID，不带尖括号；没有时为 <c>null</c>。</param>
/// <param name="AllowNameLookup">是否允许正文用文件名（而不是 <c>cid:</c>）引用它。</param>
internal sealed record InlineCandidate(AttachmentSource Source, string? ContentId, bool AllowNameLookup);

/// <summary>
/// 把正文里引用内嵌资源的地址换成 <c>data:</c> URI。
/// </summary>
/// <remarks>
/// 只内嵌图片；每个资源只编码一次，被多处引用时复用同一个字符串。
/// </remarks>
internal sealed class InlineResourceResolver
{
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".svg", ".ico", ".avif", ".apng"];

    private readonly IReadOnlyList<InlineCandidate> _candidates;
    private readonly Dictionary<InlineCandidate, string> _dataUris = [];

    internal InlineResourceResolver(IReadOnlyList<InlineCandidate> candidates)
    {
        _candidates = candidates;
    }

    /// <summary>
    /// 尝试解析一个引用。
    /// </summary>
    /// <param name="reference">正文里的地址：<c>cid:xxx</c>，或者（MSG 中）附件的文件名。</param>
    /// <returns>对应的 <c>data:</c> URI；找不到资源或资源不是图片时为 <c>null</c>。</returns>
    internal string? TryResolve(string reference)
    {
        var cleaned = UrlClassifier.Clean(reference);
        var candidate = UrlClassifier.Classify(cleaned) switch
        {
            UrlKind.Cid => FindByContentId(cleaned["cid:".Length..]),
            UrlKind.Relative => FindByName(cleaned),
            _ => null,
        };

        if (candidate is null)
        {
            return null;
        }

        if (_dataUris.TryGetValue(candidate, out var cached))
        {
            candidate.Source.IsInlineConsumed = true;
            return cached;
        }

        var mimeType = GetImageMimeType(candidate.Source);
        if (mimeType is null)
        {
            return null;
        }

        var dataUri = $"data:{mimeType};base64,{Convert.ToBase64String(candidate.Source.ReadAllBytes())}";
        _dataUris[candidate] = dataUri;
        candidate.Source.IsInlineConsumed = true;
        return dataUri;
    }

    private InlineCandidate? FindByContentId(string rawId)
    {
        var id = Unescape(rawId).Trim().Trim('<', '>');
        if (id.Length == 0)
        {
            return null;
        }

        return _candidates.FirstOrDefault(c => string.Equals(c.ContentId, id, StringComparison.Ordinal))
            ?? _candidates.FirstOrDefault(c => string.Equals(c.ContentId, id, StringComparison.OrdinalIgnoreCase));
    }

    private InlineCandidate? FindByName(string reference)
    {
        // 去掉查询串和片段，再解码 %XX
        var end = reference.IndexOfAny(['?', '#']);
        var name = Unescape(end < 0 ? reference : reference[..end]).Trim();
        if (name.Length == 0)
        {
            return null;
        }

        return _candidates.FirstOrDefault(c => c.AllowNameLookup
            && (string.Equals(c.ContentId, name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(c.Source.FileName, name, StringComparison.OrdinalIgnoreCase)));
    }

    private static string Unescape(string value)
    {
        try
        {
            return Uri.UnescapeDataString(value);
        }
        catch (UriFormatException)
        {
            return value;
        }
    }

    // 声明的类型是 image/* 就用它；类型缺失或是 application/octet-stream 时看扩展名。都不是图片就不内嵌
    private static string? GetImageMimeType(AttachmentSource source)
    {
        var declared = source.MimeType;
        if (declared is not null && declared.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return declared.ToLowerInvariant();
        }

        var fileName = source.FileName;
        if (fileName is null)
        {
            return null;
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!ImageExtensions.Contains(extension))
        {
            return null;
        }

        return extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".svg" => "image/svg+xml",
            ".ico" => "image/x-icon",
            _ => "image/" + extension.TrimStart('.'),
        };
    }
}
