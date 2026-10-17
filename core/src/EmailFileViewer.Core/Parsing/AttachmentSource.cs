using EmailFileViewer.Core.Models;
using MimeKit;
using MsgStorageAttachment = MsgReader.Outlook.Storage.Attachment;
using MsgStorageMessage = MsgReader.Outlook.Storage.Message;

namespace EmailFileViewer.Core.Parsing;

/// <summary>
/// 一个附件在解析库里的来源，负责把附件的原始字节写出去。
/// </summary>
/// <remarks>
/// <see cref="EmailAttachment"/> 只是可序列化的元数据；保存附件和打开嵌套邮件时，通过 Id 找到对应的来源再读内容。
/// </remarks>
internal abstract class AttachmentSource
{
    protected AttachmentSource(string id, string? fileName, string? mimeType, long size, bool isNestedEmail)
    {
        Id = id;
        FileName = fileName;
        MimeType = mimeType;
        Size = size;
        IsNestedEmail = isNestedEmail;
    }

    internal string Id { get; }

    internal string? FileName { get; }

    internal string? MimeType { get; }

    internal long Size { get; }

    internal bool IsNestedEmail { get; }

    /// <summary>
    /// 正文已经通过 <c>cid:</c> 或文件名引用了它（内嵌图片），不应出现在附件列表里。
    /// </summary>
    internal bool IsInlineConsumed { get; set; }

    internal EmailAttachment ToModel() => new(Id, FileName, Size, MimeType, IsNestedEmail);

    /// <summary>
    /// 把附件的原始字节写入目标流。
    /// </summary>
    internal abstract Task WriteToAsync(Stream destination, CancellationToken cancellationToken);

    /// <summary>
    /// 读出全部字节。只在需要放进 <c>data:</c> URI 的内嵌图片上使用。
    /// </summary>
    internal byte[] ReadAllBytes()
    {
        using var buffer = new MemoryStream();
        WriteToAsync(buffer, CancellationToken.None).GetAwaiter().GetResult();
        return buffer.ToArray();
    }
}

/// <summary>
/// MIME 里的普通附件：写出的是按传输编码（Base64、QP 等）解码后的字节。
/// </summary>
internal sealed class MimePartAttachment : AttachmentSource
{
    private readonly MimePart _part;

    private MimePartAttachment(string id, MimePart part, long size)
        : base(id, GetFileName(part), part.ContentType.MimeType, size, IsEmailFile(part))
    {
        _part = part;
    }

    internal static MimePartAttachment Create(string id, MimePart part)
    {
        long size = 0;
        if (part.Content is not null)
        {
            using var counter = new CountingStream();
            part.Content.DecodeTo(counter);
            size = counter.Length;
        }

        return new MimePartAttachment(id, part, size);
    }

    internal override async Task WriteToAsync(Stream destination, CancellationToken cancellationToken)
    {
        if (_part.Content is not null)
        {
            await _part.Content.DecodeToAsync(destination, cancellationToken);
        }
    }

    // 没有文件名的嵌套邮件起一个，保存时才有名字可用
    private static string? GetFileName(MimePart part)
    {
        if (!string.IsNullOrWhiteSpace(part.FileName))
        {
            return part.FileName;
        }

        return IsEmailFile(part) ? NestedEmailRules.BuildEmailFileName(null) : null;
    }

    // 嵌套邮件：扩展名是 .eml / .msg，或者声明的 MIME 类型是邮件。传输编码不是 7bit/8bit/binary 的 message/rfc822 MimeKit 不会解析成 MessagePart，也算在内
    private static bool IsEmailFile(MimePart part)
    {
        return NestedEmailRules.IsEmailFileName(part.FileName)
            || part.ContentType.IsMimeType("message", "rfc822")
            || part.ContentType.IsMimeType("application", "vnd.ms-outlook");
    }
}

/// <summary>
/// MIME 里的 <c>message/rfc822</c> 部分：写出的是重新序列化后的邮件（CRLF 换行）。
/// </summary>
internal sealed class MimeMessageAttachment : AttachmentSource
{
    private static readonly FormatOptions WriteOptions = CreateWriteOptions();

    private readonly MessagePart _part;

    private MimeMessageAttachment(string id, MessagePart part, string? fileName, long size)
        : base(id, fileName, part.ContentType.MimeType, size, isNestedEmail: true)
    {
        _part = part;
    }

    internal static MimeMessageAttachment Create(string id, MessagePart part)
    {
        var declaredName = part.ContentDisposition?.FileName ?? part.ContentType.Name;
        var fileName = string.IsNullOrWhiteSpace(declaredName)
            ? NestedEmailRules.BuildEmailFileName(part.Message?.Subject)
            : declaredName;

        long size = 0;
        if (part.Message is not null)
        {
            using var counter = new CountingStream();
            part.Message.WriteTo(WriteOptions, counter);
            size = counter.Length;
        }

        return new MimeMessageAttachment(id, part, fileName, size);
    }

    internal override async Task WriteToAsync(Stream destination, CancellationToken cancellationToken)
    {
        if (_part.Message is not null)
        {
            await _part.Message.WriteToAsync(WriteOptions, destination, cancellationToken);
        }
    }

    private static FormatOptions CreateWriteOptions()
    {
        var options = FormatOptions.Default.Clone();
        options.NewLineFormat = NewLineFormat.Dos;
        return options;
    }
}

/// <summary>
/// MSG 里按值保存的普通附件。
/// </summary>
internal sealed class MsgDataAttachment : AttachmentSource
{
    private readonly MsgStorageAttachment _attachment;

    internal MsgDataAttachment(string id, MsgStorageAttachment attachment, string? fileName, string? mimeType, bool isNestedEmail)
        : base(id, fileName, mimeType, attachment.Data?.LongLength ?? 0, isNestedEmail)
    {
        _attachment = attachment;
    }

    internal override async Task WriteToAsync(Stream destination, CancellationToken cancellationToken)
    {
        var data = _attachment.Data;
        if (data is { Length: > 0 })
        {
            await destination.WriteAsync(data, cancellationToken);
        }
    }
}

/// <summary>
/// MSG 里嵌入的另一封 MSG：写出的是一个独立的 .msg 文件。
/// </summary>
internal sealed class MsgEmbeddedMessageAttachment : AttachmentSource
{
    private readonly MsgStorageMessage _message;

    private MsgEmbeddedMessageAttachment(string id, MsgStorageMessage message, string? fileName, long size)
        : base(id, fileName, "application/vnd.ms-outlook", size, isNestedEmail: true)
    {
        _message = message;
    }

    internal static MsgEmbeddedMessageAttachment Create(string id, MsgStorageMessage message)
    {
        var fileName = string.IsNullOrWhiteSpace(message.FileName)
            ? NestedEmailRules.BuildEmailFileName(message.Subject, ".msg")
            : message.FileName;

        // 复合文档需要可随机读写的流，所以先存进内存，既能得到大小，也不会因为目标流不支持定位而失败
        using var buffer = new MemoryStream();
        message.Save(buffer);
        return new MsgEmbeddedMessageAttachment(id, message, fileName, buffer.Length);
    }

    internal override async Task WriteToAsync(Stream destination, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        _message.Save(buffer);
        buffer.Position = 0;
        await buffer.CopyToAsync(destination, cancellationToken);
    }
}

/// <summary>
/// 判断附件是不是一封邮件，以及给没有文件名的嵌套邮件起名字。
/// </summary>
internal static class NestedEmailRules
{
    internal static bool IsEmailFileName(string? fileName)
    {
        return !string.IsNullOrEmpty(fileName)
            && (fileName.EndsWith(".eml", StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith(".msg", StringComparison.OrdinalIgnoreCase));
    }

    internal static string BuildEmailFileName(string? subject, string extension = ".eml")
    {
        return (string.IsNullOrWhiteSpace(subject) ? "message" : subject.Trim()) + extension;
    }
}
