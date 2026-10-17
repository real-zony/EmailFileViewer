using System.Globalization;
using System.Text;

namespace EmailFileViewer.SampleGenerator;

/// <summary>
/// 逐行手写 MIME 报文的小工具，用于需要精确控制字节的样本。换行一律是 CRLF。
/// </summary>
internal sealed class RawMail
{
    private readonly MemoryStream _stream = new();

    public RawMail Header(string name, string value)
    {
        if (name.Any(c => c > 127) || value.Any(c => c > 127))
        {
            throw new InvalidOperationException($"邮件头 {name} 只能包含 ASCII；非 ASCII 内容要用 RFC 2047 编码字。");
        }

        Write(Encoding.ASCII.GetBytes($"{name}: {value}\r\n"));
        return this;
    }

    /// <summary>
    /// 写入值是原始字节的邮件头，不做 RFC 2047 编码。用来构造老邮件里常见的“邮件头直接写 GBK 等 8 位字节”的情况。
    /// </summary>
    public RawMail RawHeader(string name, byte[] valueBytes)
    {
        if (name.Any(c => c > 127))
        {
            throw new InvalidOperationException($"邮件头名称 {name} 只能包含 ASCII。");
        }

        Write(Encoding.ASCII.GetBytes($"{name}: "));
        Write(valueBytes);
        Write("\r\n"u8);
        return this;
    }

    public RawMail EndHeaders()
    {
        Write("\r\n"u8);
        return this;
    }

    /// <summary>原样写入正文字节（8bit / 7bit）。结尾不是 CRLF 时补一个。</summary>
    public RawMail RawBody(byte[] body)
    {
        Write(body);
        if (!body.AsSpan().EndsWith("\r\n"u8))
        {
            Write("\r\n"u8);
        }

        return this;
    }

    /// <summary>以每行 76 个字符的 Base64 写入正文。</summary>
    public RawMail Base64Body(byte[] body)
    {
        var encoded = Convert.ToBase64String(body);
        for (var i = 0; i < encoded.Length; i += 76)
        {
            Write(Encoding.ASCII.GetBytes(encoded.Substring(i, Math.Min(76, encoded.Length - i)) + "\r\n"));
        }

        return this;
    }

    public byte[] ToArray() => _stream.ToArray();

    private void Write(ReadOnlySpan<byte> bytes) => _stream.Write(bytes);
}

/// <summary>
/// RFC 2047 编码字。过长的文字会拆成多个编码字，每个不超过 75 个字符，之间用折行空白隔开。
/// </summary>
internal static class Rfc2047
{
    private const int MaxWordLength = 75;

    public static string EncodeB(string charset, string text) => Encode(charset, 'B', text, bytes => Convert.ToBase64String(bytes));

    public static string EncodeQ(string charset, string text) => Encode(charset, 'Q', text, EncodeQBytes);

    private static string Encode(string charset, char encodingLetter, string text, Func<byte[], string> encodeBytes)
    {
        var encoding = Encoding.GetEncoding(charset, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        var prefix = $"=?{charset}?{encodingLetter}?";
        var words = new List<string>();
        var chunk = new StringBuilder();

        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            var element = (string)enumerator.Current;
            var candidate = chunk + element;
            var candidateWord = $"{prefix}{encodeBytes(encoding.GetBytes(candidate))}?=";
            if (candidateWord.Length > MaxWordLength && chunk.Length > 0)
            {
                words.Add($"{prefix}{encodeBytes(encoding.GetBytes(chunk.ToString()))}?=");
                chunk.Clear();
            }

            chunk.Append(element);
        }

        if (chunk.Length > 0)
        {
            words.Add($"{prefix}{encodeBytes(encoding.GetBytes(chunk.ToString()))}?=");
        }

        return string.Join("\r\n ", words);
    }

    private static string EncodeQBytes(byte[] bytes)
    {
        var builder = new StringBuilder();
        foreach (var b in bytes)
        {
            if (b is (>= (byte)'A' and <= (byte)'Z') or (>= (byte)'a' and <= (byte)'z') or (>= (byte)'0' and <= (byte)'9'))
            {
                builder.Append((char)b);
            }
            else if (b == (byte)' ')
            {
                builder.Append('_');
            }
            else
            {
                builder.Append('=').Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return builder.ToString();
    }
}
