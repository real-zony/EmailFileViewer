using EmailFileViewer.Core.Models;
using MimeKit;
using MsgStorageMessage = MsgReader.Outlook.Storage.Message;

namespace EmailFileViewer.Core;

/// <summary>
/// 一次解析的结果：可序列化的 <see cref="EmailDocument"/>，加上后续读取附件内容所需的底层对象。
/// </summary>
/// <remarks>
/// 调用方用完后要释放它。底层对象（MIME 消息或 MSG 存储）在释放前一直保留在内存中，
/// 保存附件和打开嵌套邮件时直接使用，不需要重新读取文件。
/// </remarks>
public sealed class ParsedEmail : IDisposable
{
    private readonly Stream? _msgStream;
    private bool _isDisposed;

    internal ParsedEmail(EmailDocument document, int rawAttachmentCount, MimeMessage? mimeMessage, MsgStorageMessage? msgMessage, Stream? msgStream)
    {
        Document = document;
        RawAttachmentCount = rawAttachmentCount;
        MimeMessage = mimeMessage;
        MsgMessage = msgMessage;
        _msgStream = msgStream;
    }

    /// <summary>
    /// 与界面无关的邮件文档。
    /// </summary>
    public EmailDocument Document { get; }

    /// <summary>
    /// 解析库报告的原始附件数量，没有排除内嵌图片，也没有展开 winmail.dat。
    /// </summary>
    /// <remarks>
    /// 临时属性，只给 <c>--smoke</c> 命令输出用。附件列表实现后由 <see cref="EmailDocument.Attachments"/> 取代。
    /// </remarks>
    public int RawAttachmentCount { get; }

    /// <summary>
    /// 底层的 MIME 消息，文件是 EML 时不为 <c>null</c>。
    /// </summary>
    internal MimeMessage? MimeMessage { get; }

    /// <summary>
    /// 底层的 MSG 消息，文件是 MSG 时不为 <c>null</c>。
    /// </summary>
    internal MsgStorageMessage? MsgMessage { get; }

    /// <summary>
    /// 释放底层对象。
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        MimeMessage?.Dispose();
        MsgMessage?.Dispose();
        _msgStream?.Dispose();
    }
}
