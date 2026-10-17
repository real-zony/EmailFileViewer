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
/// 但每处引用输出的都是一整份 <c>data:</c> URI，同一张大图被几千处引用会让正文成倍膨胀，
/// 所以输出的 URI 总长度有上限（<see cref="MaxInlineDataUriLength"/>），超出后的引用不再内嵌；有引用没能内嵌的图片仍然列在附件列表里。
/// </remarks>
internal sealed class InlineResourceResolver
{
    /// <summary>
    /// 一封邮件正文里所有内嵌 <c>data:</c> URI 的总长度上限（字符数，也就是字节数）。
    /// 16 MiB 足够放下常见邮件里全部的 logo 和插图（通常几百 KB），又能把最坏情况的正文限制在几十 MB 以内。
    /// </summary>
    internal const int MaxInlineDataUriLength = 16 * 1024 * 1024;

    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".svg", ".ico", ".avif", ".apng"];

    private readonly IReadOnlyList<InlineCandidate> _candidates;
    private readonly long _maxEmittedLength;
    private readonly Dictionary<InlineCandidate, string> _dataUris = [];
    private readonly HashSet<InlineCandidate> _overBudget = [];
    private long _emittedLength;

    /// <summary>
    /// 创建。
    /// </summary>
    /// <param name="candidates">可被正文引用的内嵌资源。</param>
    /// <param name="maxEmittedLength">输出的 <c>data:</c> URI 总长度上限，默认 <see cref="MaxInlineDataUriLength"/>（测试里用来缩小，免得构造巨型输入）。</param>
    internal InlineResourceResolver(IReadOnlyList<InlineCandidate> candidates, long maxEmittedLength = MaxInlineDataUriLength)
    {
        _candidates = candidates;
        _maxEmittedLength = maxEmittedLength;
    }

    /// <summary>
    /// 尝试解析一个引用。
    /// </summary>
    /// <param name="reference">正文里的地址：<c>cid:xxx</c>，或者（MSG 中）附件的文件名。</param>
    /// <param name="overBudget">找到了图片，但内嵌之后总长度会超过 <see cref="MaxInlineDataUriLength"/>，所以没有内嵌。这时返回 <c>null</c>，也不把图片标记为“已内嵌”。</param>
    /// <returns>对应的 <c>data:</c> URI；找不到资源、资源不是图片或超出上限时为 <c>null</c>。</returns>
    internal string? TryResolve(string reference, out bool overBudget)
    {
        overBudget = false;
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
            if (!TryReserve(cached.Length))
            {
                return OverBudget(candidate, out overBudget);
            }

            MarkConsumed(candidate);
            return cached;
        }

        var mimeType = GetImageMimeType(candidate.Source);
        if (mimeType is null)
        {
            return null;
        }

        // 先按声明的大小估算，超出上限的大图不必读进内存
        var prefix = $"data:{mimeType};base64,";
        if (_emittedLength + prefix.Length + EstimateBase64Length(candidate.Source.Size) > _maxEmittedLength)
        {
            return OverBudget(candidate, out overBudget);
        }

        var dataUri = prefix + Convert.ToBase64String(candidate.Source.ReadAllBytes());
        if (!TryReserve(dataUri.Length))
        {
            return OverBudget(candidate, out overBudget);
        }

        _dataUris[candidate] = dataUri;
        MarkConsumed(candidate);
        return dataUri;
    }

    // 有引用没能内嵌的图片要留在附件列表里，所以之后即使又有引用成功内嵌，也不再把它标为“已内嵌”
    private string? OverBudget(InlineCandidate candidate, out bool overBudget)
    {
        overBudget = true;
        _overBudget.Add(candidate);
        candidate.Source.IsInlineConsumed = false;
        return null;
    }

    private void MarkConsumed(InlineCandidate candidate)
    {
        if (!_overBudget.Contains(candidate))
        {
            candidate.Source.IsInlineConsumed = true;
        }
    }

    private bool TryReserve(int length)
    {
        if (_emittedLength + length > _maxEmittedLength)
        {
            return false;
        }

        _emittedLength += length;
        return true;
    }

    private static long EstimateBase64Length(long byteCount)
    {
        return (byteCount + 2) / 3 * 4;
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
