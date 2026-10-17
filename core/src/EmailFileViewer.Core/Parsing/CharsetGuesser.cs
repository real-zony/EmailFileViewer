using System.Text;
using System.Text.Unicode;
using UtfUnknown;

namespace EmailFileViewer.Core.Parsing;

/// <summary>
/// 根据字节内容猜测文本的字符集。邮件正文和邮件头没有声明字符集（或声明明显有误）时使用。
/// </summary>
internal static class CharsetGuesser
{
    // 识别只看前面这么多字节，足够判断，也避免长邮件拖慢速度
    private const int DetectionByteLimit = 256 * 1024;

    // 识别结果的可信度低于它时不采用
    private const float MinimumConfidence = 0.5f;

    /// <summary>
    /// 判断字节是否全是 7 位 ASCII。
    /// </summary>
    /// <param name="bytes">要检查的字节。</param>
    /// <returns>没有任何高位字节时返回 <c>true</c>。</returns>
    internal static bool IsAscii(ReadOnlySpan<byte> bytes)
    {
        return bytes.IndexOfAnyExceptInRange((byte)0x00, (byte)0x7F) < 0;
    }

    /// <summary>
    /// 猜测字节的字符集。
    /// </summary>
    /// <remarks>
    /// 纯 ASCII 用哪种编码解码结果都一样，返回 <c>null</c>；合法的 UTF-8 直接认定为 UTF-8；其余交给 UTF.Unknown。
    /// </remarks>
    /// <param name="bytes">要识别的字节。</param>
    /// <returns>识别出的编码；不需要识别或识别不出来时为 <c>null</c>。</returns>
    internal static Encoding? Detect(byte[] bytes)
    {
        if (IsAscii(bytes))
        {
            return null;
        }

        if (Utf8.IsValid(bytes))
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }

        var detected = CharsetDetector.DetectFromBytes(bytes.AsSpan(0, Math.Min(bytes.Length, DetectionByteLimit))).Detected;
        return detected is { Encoding: not null } && detected.Confidence >= MinimumConfidence ? detected.Encoding : null;
    }
}
