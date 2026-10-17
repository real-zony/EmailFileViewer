using System.Text;
using System.Text.Unicode;
using MimeKit;
using MimeKit.Text;

namespace EmailFileViewer.Core.Parsing;

/// <summary>
/// 把 MIME 文本部分解码成字符串，邮件没有声明字符集或声明明显有误时自动识别编码。
/// </summary>
/// <remarks>
/// MimeKit 在没有声明字符集时只会依次尝试 UTF-8 和 ISO-8859-1，Big5、GBK 等编码的正文会变成乱码，所以这里补一步识别。
/// </remarks>
internal static class TextPartDecoder
{
    private const int UsAsciiCodePage = 20127;

    private const int Utf8CodePage = 65001;

    private const int Latin1CodePage = 28591;

    private const int Windows1252CodePage = 1252;

    /// <summary>
    /// 读取文本部分的内容。
    /// </summary>
    /// <param name="part">文本部分。</param>
    /// <returns>解码后的文本，换行为 <c>\n</c>；format=flowed 的纯文本会还原成普通段落。</returns>
    internal static string GetText(TextPart part)
    {
        var encoding = ResolveEncoding(part);
        var text = ReadText(part, encoding);

        if (!part.IsFlowed)
        {
            return text;
        }

        var converter = new FlowedToText();
        if (part.ContentType.Parameters.TryGetValue("delsp", out string? delsp))
        {
            converter.DeleteSpace = string.Equals(delsp, "yes", StringComparison.OrdinalIgnoreCase);
        }

        return converter.Convert(text);
    }

    private static string ReadText(TextPart part, Encoding? encoding)
    {
        if (encoding is null)
        {
            return part.Text;
        }

        try
        {
            return part.GetText(encoding);
        }
        catch (DecoderFallbackException)
        {
            return part.Text;
        }
    }

    // 返回 null 表示交给 MimeKit 的默认处理（纯 ASCII、UTF-8 或识别不出来）
    private static Encoding? ResolveEncoding(TextPart part)
    {
        var declared = part.ContentType.CharsetEncoding;
        if (declared is not null)
        {
            if (!IsEasilyMislabeled(declared))
            {
                return declared;
            }

            var declaredBytes = ReadContentBytes(part);
            return IsPlausible(declared, declaredBytes) ? declared : CharsetGuesser.Detect(declaredBytes) ?? declared;
        }

        // 没声明字符集（或声明的名字 .NET 不认识）：先看 BOM 和 HTML 里的 meta 声明，再看字节本身
        if (part.TryDetectEncoding(out var hinted, out _))
        {
            return hinted;
        }

        return CharsetGuesser.Detect(ReadContentBytes(part));
    }

    // 这三种声明经常写错：邮件客户端习惯性地写 us-ascii 或 iso-8859-1，实际内容却是别的编码
    private static bool IsEasilyMislabeled(Encoding encoding)
    {
        return encoding.CodePage is UsAsciiCodePage or Utf8CodePage or Latin1CodePage or Windows1252CodePage;
    }

    private static bool IsPlausible(Encoding declared, byte[] bytes)
    {
        var isAscii = CharsetGuesser.IsAscii(bytes);
        return declared.CodePage switch
        {
            UsAsciiCodePage => isAscii,
            Utf8CodePage => Utf8.IsValid(bytes),
            // 声明单字节西文编码，内容却是合法的 UTF-8 多字节序列，几乎一定是标错了
            _ => isAscii || !Utf8.IsValid(bytes),
        };
    }

    private static byte[] ReadContentBytes(TextPart part)
    {
        if (part.Content is null)
        {
            return [];
        }

        using var buffer = new MemoryStream();
        part.Content.DecodeTo(buffer);
        return buffer.ToArray();
    }
}
