using EmailFileViewer.Core.Models;
using MimeKit;
using MsgReader.Outlook;
using MsgStorageAttachment = MsgReader.Outlook.Storage.Attachment;
using MsgStorageMessage = MsgReader.Outlook.Storage.Message;

namespace EmailFileViewer.Core.Parsing;

/// <summary>
/// 把 MsgReader 解析出的 MSG 映射成 <see cref="EmailDocument"/>。
/// </summary>
internal static class MsgEmailMapper
{
    // Outlook 给纯文本邮件自动生成的 RTF 封装会带这个标记，它不是真正的富文本，正文应当按纯文本显示
    private const string FromTextMarker = "\\fromtext";

    private const int FromTextSearchLength = 512;

    private const string SmimeCarrierFileName = "smime.p7m";

    /// <summary>
    /// 映射邮件头、正文和附件。
    /// </summary>
    /// <remarks>
    /// 附件 Id 是 <c>a{序号}</c>，序号是它在 MSG 附件列表里的位置。
    /// 被 HTML 引用的内嵌图片（<c>cid:</c> 或文件名）不列出，并换成 <c>data:</c> URI；外部引用类型的附件读不到内容，也不列出。
    /// </remarks>
    /// <param name="message">MsgReader 解析出的邮件。</param>
    /// <param name="smime">消息类标明的 S/MIME 类型。</param>
    /// <returns>邮件文档，以及文档里列出的附件的来源。</returns>
    internal static MappedEmail Map(MsgStorageMessage message, MsgSmimeKind smime)
    {
        // 只有经过互联网发送的邮件才带原始传输头，通常没有
        var transportHeaders = message.Headers;

        var (sources, candidates) = CollectAttachments(message, smime);
        var resolver = new InlineResourceResolver(candidates);

        var body = EmailBodyBuilder.Build(
            () => message.BodyHtml,
            () => GetRealRtf(message),
            () => message.BodyText,
            resolver);

        var listed = sources.Where(source => !source.IsInlineConsumed).ToList();
        var attachments = listed.ToDictionary(source => source.Id);
        var resources = new List<IDisposable>();
        var hasRemoteContent = body.HasRemoteContent;
        var innerEncrypted = false;

        // 明文签名的 MSG 把整封 multipart/signed 邮件放在 smime.p7m 里，正文和附件都在里面
        if (smime == MsgSmimeKind.ClearSigned && body.Body.SourceFormat == BodyFormat.None && TryLoadClearSignedContent(message) is { } inner)
        {
            resources.Add(inner);
            var innerMapped = MimeEmailMapper.Map(inner);
            body = new BuiltBody(innerMapped.Document.Body, innerMapped.Document.HasRemoteContent);
            hasRemoteContent = innerMapped.Document.HasRemoteContent;
            innerEncrypted = innerMapped.Document.IsEncrypted;
            listed.AddRange(innerMapped.Attachments.Values);
            foreach (var (id, source) in innerMapped.Attachments)
            {
                attachments[id] = source;
            }
        }

        var (isSigned, isEncrypted) = GetSmimeFlags(message, smime);

        // 明文签名里被签名的内容本身是加密数据：同时是已签名和已加密
        isEncrypted |= innerEncrypted;

        // 不透明签名的 MSG 把正文和附件包在 smime.p7m 的 CMS 数据里，取出来当作邮件内容映射；取不出来就只有邮件头和签名标志
        if (isSigned && smime == MsgSmimeKind.OpaqueOrEncrypted && TryLoadOpaqueSignedContent(message) is { } signedEntity)
        {
            resources.Add(signedEntity);
            var signedContent = MimeEmailMapper.MapContent(signedEntity, signedDepth: 1);
            resources.AddRange(signedContent.Resources);
            if (signedContent.Body.Body.SourceFormat != BodyFormat.None)
            {
                body = signedContent.Body;
                hasRemoteContent = signedContent.Body.HasRemoteContent;
            }

            foreach (var source in signedContent.Listed)
            {
                listed.Add(source);
                attachments[source.Id] = source;
            }

            isEncrypted |= signedContent.IsEncrypted;
        }

        var document = new EmailDocument
        {
            From = MapSender(message),
            To = MapRecipients(message, RecipientType.To),
            Cc = MapRecipients(message, RecipientType.Cc),
            Bcc = MapRecipients(message, RecipientType.Bcc),
            ReplyTo = MapReplyTo(transportHeaders?.ReplyTo),
            Subject = NullIfBlank(message.Subject),
            SentAtUtc = message.SentOn?.ToUniversalTime(),
            DateHeaderRaw = NullIfBlank(transportHeaders?.Date),
            Body = body.Body,
            Attachments = listed.Select(source => source.ToModel()).ToList(),
            IsSigned = isSigned,
            IsEncrypted = isEncrypted,
            HasRemoteContent = hasRemoteContent,
        };

        return new MappedEmail(document, attachments, resources);
    }

    // smime.p7m 的内容是一封完整的 MIME 邮件；解析失败就当没有（只是看不到正文，邮件头和签名标志仍然正常）
    private static MimeMessage? TryLoadClearSignedContent(MsgStorageMessage message)
    {
        var carrier = message.Attachments
            .OfType<MsgStorageAttachment>()
            .FirstOrDefault(a => string.Equals(a.FileName, SmimeCarrierFileName, StringComparison.OrdinalIgnoreCase));
        if (carrier?.Data is not { Length: > 0 } data)
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(data);
            return MimeMessage.Load(MimeParserOptions.Create(), stream);
        }
        catch (Exception ex) when (ex is FormatException or IOException)
        {
            return null;
        }
    }

    // smime.p7m 的内容是 CMS signedData，被签名的内容是一个 MIME 实体
    private static MimeEntity? TryLoadOpaqueSignedContent(MsgStorageMessage message)
    {
        var carrier = message.Attachments
            .OfType<MsgStorageAttachment>()
            .FirstOrDefault(a => string.Equals(a.FileName, SmimeCarrierFileName, StringComparison.OrdinalIgnoreCase));
        return carrier?.Data is { Length: > 0 } data ? SignedContent.TryRead(data) : null;
    }

    private static (List<AttachmentSource> Sources, List<InlineCandidate> Candidates) CollectAttachments(MsgStorageMessage message, MsgSmimeKind smime)
    {
        var sources = new List<AttachmentSource>();
        var candidates = new List<InlineCandidate>();

        for (var index = 0; index < message.Attachments.Count; index++)
        {
            var id = $"a{index}";
            switch (message.Attachments[index])
            {
                case MsgStorageMessage embedded:
                    sources.Add(MsgEmbeddedMessageAttachment.Create(id, embedded));
                    break;
                case MsgStorageAttachment { IsExternalReference: true }:
                    // 只保存了一个路径，没有内容；路径来自不可信的文件，不能去读
                    break;
                case MsgStorageAttachment attachment:
                    AddDataAttachment(id, attachment, smime, sources, candidates);
                    break;
            }
        }

        return (sources, candidates);
    }

    private static void AddDataAttachment(
        string id,
        MsgStorageAttachment attachment,
        MsgSmimeKind smime,
        List<AttachmentSource> sources,
        List<InlineCandidate> candidates)
    {
        var fileName = NullIfBlank(attachment.FileName);

        // S/MIME 邮件的 smime.p7m 是签名或加密内容的载体，不是用户附件
        if (smime != MsgSmimeKind.None && string.Equals(fileName, SmimeCarrierFileName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var mimeType = NullIfBlank(attachment.MimeType) ?? (fileName is null ? null : MimeTypes.GetMimeType(fileName));
        var isNestedEmail = NestedEmailRules.IsEmailFileName(fileName)
            || string.Equals(mimeType, "message/rfc822", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mimeType, "application/vnd.ms-outlook", StringComparison.OrdinalIgnoreCase);

        var source = new MsgDataAttachment(id, attachment, fileName, mimeType, isNestedEmail);
        sources.Add(source);

        var contentId = NullIfBlank(attachment.ContentId);
        if (contentId is not null || attachment.IsInline)
        {
            candidates.Add(new InlineCandidate(source, contentId, AllowNameLookup: attachment.IsInline));
        }
    }

    // MsgReader 的 Sender 已经处理过 Exchange 内部地址；Sender 缺失时才用“代表发件人”
    private static List<EmailAddress> MapSender(MsgStorageMessage message)
    {
        var sender = EmailAddress.Create(message.Sender?.DisplayName, message.Sender?.Email)
            ?? EmailAddress.Create(message.SenderRepresenting?.DisplayName, message.SenderRepresenting?.Email);

        return sender is null ? [] : [sender];
    }

    // 收件人类型不是抄送或密送的（包括会议资源、会议室和未知类型）一律算收件人，免得丢掉
    private static List<EmailAddress> MapRecipients(MsgStorageMessage message, RecipientType type)
    {
        var result = new List<EmailAddress>();
        foreach (var recipient in message.Recipients)
        {
            var recipientType = recipient.Type;
            var belongsToList = type switch
            {
                RecipientType.Cc => recipientType == RecipientType.Cc,
                RecipientType.Bcc => recipientType == RecipientType.Bcc,
                _ => recipientType != RecipientType.Cc && recipientType != RecipientType.Bcc,
            };

            if (!belongsToList)
            {
                continue;
            }

            var mapped = EmailAddress.Create(recipient.DisplayName, recipient.Email);
            if (mapped is not null)
            {
                result.Add(mapped);
            }
        }

        return result;
    }

    private static List<EmailAddress> MapReplyTo(MsgReader.Mime.Header.RfcMailAddress? replyTo)
    {
        if (replyTo is null)
        {
            return [];
        }

        var mapped = EmailAddress.Create(replyTo.DisplayName, replyTo.Address);
        return mapped is null ? [] : [mapped];
    }

    // 明文签名就是签名；其余的 S/MIME 看 smime.p7m 的 CMS 类型，signedData 是不透明签名，否则按加密处理
    private static (bool IsSigned, bool IsEncrypted) GetSmimeFlags(MsgStorageMessage message, MsgSmimeKind smime)
    {
        switch (smime)
        {
            case MsgSmimeKind.ClearSigned:
                return (true, false);
            case MsgSmimeKind.OpaqueOrEncrypted:
                var carrier = message.Attachments
                    .OfType<MsgStorageAttachment>()
                    .FirstOrDefault(a => string.Equals(a.FileName, SmimeCarrierFileName, StringComparison.OrdinalIgnoreCase));
                var isSignedData = carrier?.Data is { Length: > 0 } data && CmsContentType.IsSignedData(data);
                return isSignedData ? (true, false) : (false, true);
            default:
                return (false, false);
        }
    }

    private static string? GetRealRtf(MsgStorageMessage message)
    {
        var rtf = message.BodyRtf;
        if (rtf is null)
        {
            return null;
        }

        var head = rtf.AsSpan(0, Math.Min(rtf.Length, FromTextSearchLength));
        return head.Contains(FromTextMarker, StringComparison.Ordinal) ? null : rtf;
    }

    private static string? NullIfBlank(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
