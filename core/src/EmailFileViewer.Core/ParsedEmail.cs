using EmailFileViewer.Core.Models;
using EmailFileViewer.Core.Parsing;
using MimeKit;
using MsgStorageMessage = MsgReader.Outlook.Storage.Message;

namespace EmailFileViewer.Core;

/// <summary>
/// 一次解析的结果：可序列化的 <see cref="EmailDocument"/>，加上读取附件内容所需的底层对象。
/// </summary>
/// <remarks>
/// 调用方用完后要释放它。底层对象（MIME 消息或 MSG 存储）在释放前一直保留在内存中，
/// 保存附件和打开嵌套邮件时直接使用，不需要重新读取文件，原文件也不会被占用。
/// 嵌套邮件是独立的 <see cref="ParsedEmail"/>，和外层邮件互不影响，各自释放。
/// </remarks>
public sealed class ParsedEmail : IDisposable
{
    private readonly IReadOnlyDictionary<string, AttachmentSource> _attachments;
    private readonly Stream? _msgStream;
    private readonly IReadOnlyList<IDisposable> _extraResources;
    private bool _isDisposed;

    internal ParsedEmail(MappedEmail mapped, MimeMessage? mimeMessage, MsgStorageMessage? msgMessage, Stream? msgStream)
    {
        Document = mapped.Document;
        _attachments = mapped.Attachments;
        _extraResources = mapped.Resources ?? [];
        MimeMessage = mimeMessage;
        MsgMessage = msgMessage;
        _msgStream = msgStream;
    }

    /// <summary>
    /// 与界面无关的邮件文档。
    /// </summary>
    public EmailDocument Document { get; }

    /// <summary>
    /// 底层的 MIME 消息，文件是 EML 时不为 <c>null</c>。
    /// </summary>
    internal MimeMessage? MimeMessage { get; }

    /// <summary>
    /// 底层的 MSG 消息，文件是 MSG 时不为 <c>null</c>。
    /// </summary>
    internal MsgStorageMessage? MsgMessage { get; }

    /// <summary>
    /// 把一个附件的原始字节写入目标流。
    /// </summary>
    /// <remarks>
    /// 写出的是解码后的文件内容（Base64、QP 等传输编码已经还原）；嵌套邮件写出的是一份独立的 .eml（CRLF 换行）或 .msg 文件。
    /// 方法不会关闭 <paramref name="destination"/>。
    /// </remarks>
    /// <param name="attachmentId"><see cref="EmailAttachment.Id"/>。</param>
    /// <param name="destination">目标流。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <exception cref="AttachmentNotFoundException">没有这个 Id 的附件。</exception>
    /// <exception cref="ObjectDisposedException">已经释放。</exception>
    public async Task WriteAttachmentAsync(string attachmentId, Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var source = FindAttachment(attachmentId);
        await source.WriteToAsync(destination, cancellationToken);
    }

    /// <summary>
    /// 把一个嵌套邮件附件解析成新的 <see cref="ParsedEmail"/>，可以逐层打开。
    /// </summary>
    /// <remarks>
    /// 适用于 <see cref="EmailAttachment.IsNestedEmail"/> 为 <c>true</c> 的附件：<c>message/rfc822</c>、MSG 嵌入的 MSG，以及作为附件的 .eml / .msg 文件。
    /// 返回的邮件有自己的附件 Id（相对于它自己），调用方负责释放。
    /// </remarks>
    /// <param name="attachmentId"><see cref="EmailAttachment.Id"/>。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>嵌套邮件的解析结果。</returns>
    /// <exception cref="AttachmentNotFoundException">没有这个 Id 的附件。</exception>
    /// <exception cref="AttachmentNotNestedEmailException">这个附件不是邮件。</exception>
    /// <exception cref="EmailParseException">附件的内容不是有效的邮件，类别同 <see cref="EmailParser.ParseFileAsync"/>。</exception>
    /// <exception cref="ObjectDisposedException">已经释放。</exception>
    public async Task<ParsedEmail> OpenNestedEmailAsync(string attachmentId, CancellationToken cancellationToken = default)
    {
        var source = FindAttachment(attachmentId);
        if (!source.IsNestedEmail)
        {
            throw new AttachmentNotNestedEmailException(attachmentId);
        }

        // 先写进内存再按内容解析，和打开一个磁盘上的文件走同一条路径，作为附件的 .eml / .msg 文件也一样处理
        using var buffer = new MemoryStream();
        await source.WriteToAsync(buffer, cancellationToken);
        buffer.Position = 0;
        return await EmailParser.ParseStreamAsync(buffer, source.FileName ?? attachmentId, cancellationToken);
    }

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
        foreach (var resource in _extraResources)
        {
            resource.Dispose();
        }
    }

    private AttachmentSource FindAttachment(string attachmentId)
    {
        ArgumentNullException.ThrowIfNull(attachmentId);
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        return _attachments.TryGetValue(attachmentId, out var source)
            ? source
            : throw new AttachmentNotFoundException(attachmentId);
    }
}
