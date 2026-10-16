using MimeKit;

namespace EmailFileViewer.Core.Parsing;

/// <summary>
/// 按文件内容判断文件是不是邮件：OLE 复合文档按 MSG 处理，其他内容必须像 RFC 5322 邮件才按 MIME 处理。
/// </summary>
/// <remarks>
/// MimeKit 非常宽松，二进制垃圾和空文件也可能“解析成功”，所以解析前后各做一次合理性检查。
/// </remarks>
internal static class EmailFormatDetector
{
    /// <summary>
    /// 检查文件头所用的字节数，所有合理性检查只看这么多。
    /// </summary>
    internal const int SniffLength = 1024;

    private static ReadOnlySpan<byte> OleSignature => [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    private static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];

    // 解析后至少要有其中一个，才认为是邮件
    private static readonly HeaderId[] CommonHeaders =
    [
        HeaderId.From,
        HeaderId.To,
        HeaderId.Date,
        HeaderId.Subject,
        HeaderId.MessageId,
        HeaderId.MimeVersion,
        HeaderId.Received,
        HeaderId.ContentType,
    ];

    /// <summary>
    /// 判断文件头是否为 OLE 复合文档签名。
    /// </summary>
    /// <param name="header">文件开头的字节，不足 8 字节时一律返回 <c>false</c>。</param>
    /// <returns>是 OLE 复合文档时返回 <c>true</c>。</returns>
    internal static bool IsOleCompoundFile(ReadOnlySpan<byte> header)
    {
        return header.StartsWith(OleSignature);
    }

    /// <summary>
    /// 在交给 MimeKit 之前检查文件开头是否像一封 RFC 5322 邮件。
    /// </summary>
    /// <remarks>
    /// 要求第一个非空行是 <c>name: value</c> 形式的邮件头字段；开头允许有 UTF-8 BOM 和空行，
    /// 也允许先有一行 mbox 起始行（以 <c>From </c> 开头）再接邮件头字段。
    /// </remarks>
    /// <param name="header">文件开头的字节，最多看 <see cref="SniffLength"/> 个。</param>
    /// <exception cref="EmailParseException">文件为空、含有 NUL 字节或第一行不是邮件头字段，类别为 <see cref="EmailParseErrorKind.UnsupportedFormat"/>。</exception>
    internal static void EnsureLooksLikeMime(ReadOnlySpan<byte> header)
    {
        var window = header.Length > SniffLength ? header[..SniffLength] : header;

        if (window.Contains((byte)0))
        {
            throw new EmailParseException(EmailParseErrorKind.UnsupportedFormat, "文件开头含有 NUL 字节，是二进制文件，不是邮件。");
        }

        if (window.StartsWith(Utf8Bom))
        {
            window = window[Utf8Bom.Length..];
        }

        if (!TryReadNonEmptyLine(ref window, out var line))
        {
            throw new EmailParseException(EmailParseErrorKind.UnsupportedFormat, "文件为空，或开头只有空白字符。");
        }

        // Thunderbird 另存的 EML 等文件开头会有一行 mbox 的起始行（From - Wed Jul 22 ...），MimeKit 会忽略它，这里也跳过
        if (!IsHeaderField(line) && line.StartsWith("From "u8))
        {
            TryReadNonEmptyLine(ref window, out line);
        }

        if (!IsHeaderField(line))
        {
            throw new EmailParseException(EmailParseErrorKind.UnsupportedFormat, "文件的第一个非空行不是邮件头字段（name: value）。");
        }
    }

    /// <summary>
    /// 计算应该从文件的哪个位置开始交给 MimeKit 解析：跳过开头的 UTF-8 BOM 和空行。
    /// </summary>
    /// <remarks>
    /// MimeKit 把开头的空行当成邮件头已经结束，整封邮件会被当成正文；UTF-8 BOM 会粘在第一个邮件头的名字上。
    /// 所以这两样都要先跳过。
    /// </remarks>
    /// <param name="header">文件开头的字节，最多看 <see cref="SniffLength"/> 个。</param>
    /// <returns>起始偏移量；开头没有需要跳过的内容或者前 <see cref="SniffLength"/> 个字节里没有非空行时，返回 BOM 的长度或 0。</returns>
    internal static int GetMimeStartOffset(ReadOnlySpan<byte> header)
    {
        var window = header.Length > SniffLength ? header[..SniffLength] : header;
        var bomLength = window.StartsWith(Utf8Bom) ? Utf8Bom.Length : 0;
        window = window[bomLength..];

        var offset = bomLength;
        while (!window.IsEmpty)
        {
            var lineEnd = window.IndexOf((byte)'\n');
            var line = (lineEnd < 0 ? window : window[..lineEnd]).TrimEnd((byte)'\r');
            if (line.IndexOfAnyExcept(" \t"u8) >= 0)
            {
                return offset;
            }

            if (lineEnd < 0)
            {
                break;
            }

            offset += lineEnd + 1;
            window = window[(lineEnd + 1)..];
        }

        return bomLength;
    }

    /// <summary>
    /// 检查 MimeKit 解析出的消息是否至少含有一个常见邮件头。
    /// </summary>
    /// <param name="message">解析结果。</param>
    /// <exception cref="EmailParseException">一个常见邮件头都没有，类别为 <see cref="EmailParseErrorKind.UnsupportedFormat"/>。</exception>
    internal static void EnsureHasCommonHeader(MimeMessage message)
    {
        foreach (var headerId in CommonHeaders)
        {
            if (message.Headers.Contains(headerId))
            {
                return;
            }
        }

        throw new EmailParseException(EmailParseErrorKind.UnsupportedFormat, "文件里没有任何常见的邮件头（From、To、Date、Subject 等）。");
    }

    // 空行指没有任何内容或只有空白字符的行。读出下一个非空行（不含行尾换行符）并把窗口移到它后面；没有非空行时返回 false
    private static bool TryReadNonEmptyLine(ref ReadOnlySpan<byte> window, out ReadOnlySpan<byte> line)
    {
        while (!window.IsEmpty)
        {
            var lineEnd = window.IndexOf((byte)'\n');
            var current = lineEnd < 0 ? window : window[..lineEnd];
            window = lineEnd < 0 ? default : window[(lineEnd + 1)..];

            current = current.TrimEnd((byte)'\r');
            if (current.IndexOfAnyExcept(" \t"u8) >= 0)
            {
                line = current;
                return true;
            }
        }

        line = default;
        return false;
    }

    // RFC 5322：字段名由可打印的 ASCII 字符（不含冒号）组成，后面紧跟冒号；旧语法允许冒号前有空白
    private static bool IsHeaderField(ReadOnlySpan<byte> line)
    {
        var colon = line.IndexOf((byte)':');
        if (colon <= 0)
        {
            return false;
        }

        var name = line[..colon].TrimEnd(" \t"u8);
        if (name.IsEmpty)
        {
            return false;
        }

        foreach (var value in name)
        {
            if (value is < 0x21 or > 0x7E)
            {
                return false;
            }
        }

        return true;
    }
}
