using MimeKit;

namespace EmailFileViewer.Core;

/// <summary>
/// 读取邮件文件的主题和附件数量。
/// </summary>
/// <remarks>
/// 只用于 NativeAOT 可行性验证，后续会被完整的解析入口取代。
/// </remarks>
public static class EmailSummaryReader
{
    private const int SignatureLength = 8;

    // OLE 复合文档签名，Outlook MSG 文件以它开头
    private static ReadOnlySpan<byte> OleSignature => [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    /// <summary>
    /// 判断文件头是否为 OLE 复合文档签名。
    /// </summary>
    /// <param name="header">文件开头的字节，不足 8 字节时一律返回 <c>false</c>。</param>
    /// <returns>是 OLE 复合文档时返回 <c>true</c>。</returns>
    public static bool IsOleCompoundFile(ReadOnlySpan<byte> header)
    {
        return header.StartsWith(OleSignature);
    }

    /// <summary>
    /// 读取指定文件的摘要。文件头是 OLE 签名时按 MSG 解析，否则按 MIME 解析。
    /// </summary>
    /// <param name="filePath">邮件文件路径。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>邮件主题和附件数量。</returns>
    public static async Task<EmailSummary> ReadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);

        var header = new byte[SignatureLength];
        var headerLength = await stream.ReadAtLeastAsync(header, SignatureLength, throwOnEndOfStream: false, cancellationToken);
        stream.Position = 0;

        if (IsOleCompoundFile(header.AsSpan(0, headerLength)))
        {
            return ReadMsg(stream, cancellationToken);
        }

        return await ReadMimeAsync(stream, cancellationToken);
    }

    private static EmailSummary ReadMsg(Stream stream, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var message = new MsgReader.Outlook.Storage.Message(stream, FileAccess.Read, leaveStreamOpen: true);
        return new EmailSummary(message.Subject ?? string.Empty, message.Attachments.Count);
    }

    private static async Task<EmailSummary> ReadMimeAsync(Stream stream, CancellationToken cancellationToken)
    {
        var message = await MimeMessage.LoadAsync(stream, cancellationToken);
        return new EmailSummary(message.Subject ?? string.Empty, message.Attachments.Count());
    }
}
