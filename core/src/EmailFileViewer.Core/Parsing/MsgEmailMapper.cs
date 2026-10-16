using EmailFileViewer.Core.Models;
using MsgReader.Outlook;
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

    /// <summary>
    /// 映射邮件头和正文。
    /// </summary>
    /// <param name="message">MsgReader 解析出的邮件。</param>
    /// <returns>邮件文档。</returns>
    internal static EmailDocument Map(MsgStorageMessage message)
    {
        // 只有经过互联网发送的邮件才带原始传输头，通常没有
        var transportHeaders = message.Headers;

        return new EmailDocument
        {
            From = MapSender(message),
            To = MapRecipients(message, RecipientType.To),
            Cc = MapRecipients(message, RecipientType.Cc),
            Bcc = MapRecipients(message, RecipientType.Bcc),
            ReplyTo = MapReplyTo(transportHeaders?.ReplyTo),
            Subject = NullIfBlank(message.Subject),
            SentAtUtc = message.SentOn?.ToUniversalTime(),
            DateHeaderRaw = NullIfBlank(transportHeaders?.Date),
            Body = EmailBodyBuilder.Build(
                () => message.BodyHtml,
                () => GetRealRtf(message),
                () => message.BodyText),
        };
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
