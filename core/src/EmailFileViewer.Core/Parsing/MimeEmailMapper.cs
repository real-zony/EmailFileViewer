using EmailFileViewer.Core.Models;
using MimeKit;
using MimeKit.Text;

namespace EmailFileViewer.Core.Parsing;

/// <summary>
/// 把 MimeKit 解析出的 <see cref="MimeMessage"/> 映射成 <see cref="EmailDocument"/>。
/// </summary>
internal static class MimeEmailMapper
{
    /// <summary>
    /// 映射邮件头、正文和附件。
    /// </summary>
    /// <param name="message">MimeKit 解析出的邮件。</param>
    /// <returns>邮件文档，以及文档里列出的附件的来源。</returns>
    internal static MappedEmail Map(MimeMessage message)
    {
        var dateHeaderRaw = NullIfBlank(message.Headers[HeaderId.Date]);

        var htmlPart = FindTextPart(message.Body, TextFormat.Html);
        var rtfPart = FindTextPart(message.Body, TextFormat.RichText);
        var plainPart = FindTextPart(message.Body, TextFormat.Plain);
        var bodyParts = new HashSet<MimeEntity>(ReferenceEqualityComparer.Instance);
        foreach (var part in new[] { htmlPart, rtfPart, plainPart })
        {
            if (part is not null)
            {
                bodyParts.Add(part);
            }
        }

        var smime = SmimeDetector.Detect(message.Body);
        var (sources, candidates) = MimeAttachmentCollector.Collect(message.Body, bodyParts, smime.Carrier);
        var resolver = new InlineResourceResolver(candidates);

        var body = EmailBodyBuilder.Build(
            () => htmlPart is null ? null : TextPartDecoder.GetText(htmlPart),
            () => rtfPart is null ? null : TextPartDecoder.GetText(rtfPart),
            () => plainPart is null ? null : TextPartDecoder.GetText(plainPart),
            resolver);

        // 正文通过 cid: 引用了的内嵌图片不再列为附件；要在正文处理完之后才知道哪些被引用
        var listed = sources.Where(source => !source.IsInlineConsumed).ToList();

        var document = new EmailDocument
        {
            From = MapAddresses(message.From),
            To = MapAddresses(message.To),
            Cc = MapAddresses(message.Cc),
            Bcc = MapAddresses(message.Bcc),
            ReplyTo = MapAddresses(message.ReplyTo),
            Subject = NullIfBlank(message.Subject),
            SentAtUtc = GetSentAtUtc(message, dateHeaderRaw),
            DateHeaderRaw = dateHeaderRaw,
            Body = body.Body,
            Attachments = listed.Select(source => source.ToModel()).ToList(),
            IsSigned = smime.IsSigned,
            IsEncrypted = smime.IsEncrypted,
            HasRemoteContent = body.HasRemoteContent,
        };

        return new MappedEmail(document, listed.ToDictionary(source => source.Id));
    }

    // MimeKit 在 Date 头缺失或无法解析时返回 DateTimeOffset.MinValue
    private static DateTimeOffset? GetSentAtUtc(MimeMessage message, string? dateHeaderRaw)
    {
        if (dateHeaderRaw is null || message.Date == DateTimeOffset.MinValue)
        {
            return null;
        }

        return message.Date.ToUniversalTime();
    }

    // 地址组（如 undisclosed-recipients:;）展开成组内的成员，空组不产生任何地址
    private static List<EmailAddress> MapAddresses(InternetAddressList list)
    {
        var result = new List<EmailAddress>();
        AddAddresses(result, list);
        return result;
    }

    private static void AddAddresses(List<EmailAddress> result, IEnumerable<InternetAddress> addresses)
    {
        foreach (var address in addresses)
        {
            switch (address)
            {
                case MailboxAddress mailbox:
                    var mapped = EmailAddress.Create(mailbox.Name, mailbox.Address);
                    if (mapped is not null)
                    {
                        result.Add(mapped);
                    }

                    break;
                case GroupAddress group:
                    AddAddresses(result, group.Members);
                    break;
            }
        }
    }

    // 沿用 MimeKit 的 HtmlBody / TextBody 取法：只看顶层结构里排在最前面的文本部分，不把附件当正文
    private static string? GetBodyText(MimeMessage message, TextFormat format)
    {
        var part = FindTextPart(message.Body, format);
        return part is null ? null : TextPartDecoder.GetText(part);
    }

    private static TextPart? FindTextPart(MimeEntity? body, TextFormat format)
    {
        switch (body)
        {
            case Multipart multipart:
                return multipart.TryGetValue(format, out var found) ? found : null;
            case TextPart { IsAttachment: false } textPart:
                return IsFormat(textPart, format) ? textPart : null;
            default:
                return null;
        }
    }

    private static bool IsFormat(TextPart part, TextFormat format)
    {
        return format switch
        {
            TextFormat.Html => part.IsHtml,
            TextFormat.RichText => part.IsRichText,
            TextFormat.Plain => part.IsPlain,
            _ => false,
        };
    }

    private static string? NullIfBlank(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
