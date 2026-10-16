using System.Text;
using System.Text.Unicode;
using MimeKit;

namespace EmailFileViewer.Core.Parsing;

/// <summary>
/// 处理没有经过 RFC 2047 编码、直接写了 8 位字节的邮件头（老的中文、俄文邮件客户端常这样发信）。
/// </summary>
/// <remarks>
/// MimeKit 对这种邮件头先试 UTF-8，不行就按 ISO-8859-1 解，非西欧语言会变成乱码。
/// 这里判断是否出现了这种邮件头，并给出应该用来重新解析的编码。
/// </remarks>
internal static class MimeHeaderEncodingResolver
{
    private const int Latin1CodePage = 28591;

    /// <summary>
    /// 判断是否需要用别的编码重新解析邮件头。
    /// </summary>
    /// <remarks>
    /// 只处理含高位字节、又不是合法 UTF-8 的邮件头。编码优先参考正文声明的字符集（老邮件的邮件头和正文通常用同一种编码），
    /// 正文没有声明时再根据邮件头的字节识别。
    /// </remarks>
    /// <param name="message">用默认选项解析出的邮件。</param>
    /// <returns>应该设为 <see cref="ParserOptions.CharsetEncoding"/> 的编码；不需要重新解析时为 <c>null</c>。</returns>
    internal static Encoding? Resolve(MimeMessage message)
    {
        var rawBytes = new List<byte>();
        foreach (var header in message.Headers)
        {
            var raw = header.RawValue;
            if (CharsetGuesser.IsAscii(raw) || Utf8.IsValid(raw))
            {
                continue;
            }

            rawBytes.AddRange(raw);
        }

        if (rawBytes.Count == 0)
        {
            return null;
        }

        var candidate = GetDeclaredBodyEncoding(message) ?? CharsetGuesser.Detect(rawBytes.ToArray());

        // 默认处理已经等价于 ISO-8859-1，UTF-8 和 ASCII 对非 UTF-8 的字节没有意义
        if (candidate is null || candidate.CodePage is Latin1CodePage or 65001 or 20127)
        {
            return null;
        }

        return candidate;
    }

    // 第一个声明了字符集（而且不是只会写 us-ascii / utf-8 的客户端随手填的那几种）的文本部分
    private static Encoding? GetDeclaredBodyEncoding(MimeMessage message)
    {
        foreach (var part in message.BodyParts.OfType<TextPart>())
        {
            var encoding = part.ContentType.CharsetEncoding;
            if (encoding is not null && encoding.CodePage is not (65001 or 20127))
            {
                return encoding;
            }
        }

        return null;
    }
}
