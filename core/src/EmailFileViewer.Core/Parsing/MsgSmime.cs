using System.Diagnostics.CodeAnalysis;
using System.Text;
using OpenMcdf;

namespace EmailFileViewer.Core.Parsing;

/// <summary>
/// MSG 的 S/MIME 类型，由消息类（PR_MESSAGE_CLASS）决定。
/// </summary>
internal enum MsgSmimeKind
{
    /// <summary>不是 S/MIME 邮件。</summary>
    None,

    /// <summary>明文签名（<c>IPM.Note.SMIME.MultipartSigned</c>）：内容以 multipart/signed 的形式放在 smime.p7m 附件里。</summary>
    ClearSigned,

    /// <summary>不透明签名或加密（<c>IPM.Note.SMIME</c>）：内容在 smime.p7m 附件的 CMS 数据里。</summary>
    OpaqueOrEncrypted,
}

/// <summary>
/// 识别 S/MIME 的 MSG，并在内存副本里把消息类改回普通邮件，让 MsgReader 不走它自己的 S/MIME 处理。
/// </summary>
/// <remarks>
/// MsgReader 遇到 S/MIME 的 MSG 会用 <c>SignedCms</c> 解析并对每张证书调用 <c>X509Certificate2.Verify()</c>，
/// 后者构建证书链时可能联网查询吊销状态，违反“查看邮件时不产生网络请求”。所以在交给 MsgReader 之前先改掉消息类。
/// 改的只是内存里的副本，不会写回原文件。
/// </remarks>
internal static class MsgSmime
{
    private const string UnicodeClassStream = "__substg1.0_001A001F";
    private const string AnsiClassStream = "__substg1.0_001A001E";
    private const string PlainNoteClass = "IPM.Note";

    /// <summary>
    /// 读取消息类；是 S/MIME 的就改成普通邮件。
    /// </summary>
    /// <param name="buffer">MSG 文件内容，位置会被重置到开头。</param>
    /// <returns>S/MIME 类型。</returns>
    internal static MsgSmimeKind NeutralizeInPlace(MemoryStream buffer)
    {
        buffer.Position = 0;
        var kind = MsgSmimeKind.None;
        using (var root = RootStorage.Open(buffer, StorageModeFlags.LeaveOpen))
        {
            if (TryOpenClassStream(root, out var stream, out var encoding))
            {
                using (stream)
                {
                    var bytes = new byte[stream.Length];
                    stream.ReadExactly(bytes);
                    kind = GetKind(encoding.GetString(bytes).Trim('\0', ' '));

                    if (kind != MsgSmimeKind.None)
                    {
                        var replacement = encoding.GetBytes(PlainNoteClass);
                        stream.Position = 0;
                        stream.SetLength(replacement.Length);
                        stream.Write(replacement);
                    }
                }
            }
        }

        buffer.Position = 0;
        return kind;
    }

    private static bool TryOpenClassStream(RootStorage root, [NotNullWhen(true)] out CfbStream? stream, out Encoding encoding)
    {
        if (root.TryOpenStream(UnicodeClassStream, out stream))
        {
            encoding = Encoding.Unicode;
            return true;
        }

        encoding = Encoding.Latin1;
        return root.TryOpenStream(AnsiClassStream, out stream);
    }

    private static MsgSmimeKind GetKind(string messageClass)
    {
        if (messageClass.Equals("IPM.Note.SMIME.MultipartSigned", StringComparison.OrdinalIgnoreCase))
        {
            return MsgSmimeKind.ClearSigned;
        }

        return messageClass.Equals("IPM.Note.SMIME", StringComparison.OrdinalIgnoreCase)
            ? MsgSmimeKind.OpaqueOrEncrypted
            : MsgSmimeKind.None;
    }
}
