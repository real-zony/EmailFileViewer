using EmailFileViewer.Core.Models;
using MimeKit;
using MimeKit.Text;

namespace EmailFileViewer.Core.Parsing;

/// <summary>
/// 一段 MIME 内容映射出来的结果：正文、列出的附件和 S/MIME 标志。
/// </summary>
/// <param name="Body">选出的正文，以及是否引用了远程资源。</param>
/// <param name="Listed">要列给用户的附件（不含已内嵌进正文的图片）。</param>
/// <param name="IsSigned">带有数字签名。</param>
/// <param name="IsEncrypted">已加密，正文无法显示。</param>
/// <param name="Resources">映射过程中额外解析出来、需要和邮件一起释放的对象（例如从不透明签名里取出的 MIME 实体）。</param>
internal sealed record MappedContent(
    BuiltBody Body,
    IReadOnlyList<AttachmentSource> Listed,
    bool IsSigned,
    bool IsEncrypted,
    IReadOnlyList<IDisposable> Resources);

/// <summary>
/// 把 MimeKit 解析出的 <see cref="MimeMessage"/> 映射成 <see cref="EmailDocument"/>。
/// </summary>
internal static class MimeEmailMapper
{
    // 不透明签名可以一层套一层，限制层数，免得恶意构造的数据把递归撑爆
    private const int MaxSignedDepth = 3;

    /// <summary>
    /// 映射邮件头、正文和附件。
    /// </summary>
    /// <param name="message">MimeKit 解析出的邮件。</param>
    /// <returns>邮件文档，以及文档里列出的附件的来源。</returns>
    internal static MappedEmail Map(MimeMessage message)
    {
        var dateHeaderRaw = NullIfBlank(message.Headers[HeaderId.Date]);
        var content = MapContent(message.Body);

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
            Body = content.Body.Body,
            Attachments = content.Listed.Select(source => source.ToModel()).ToList(),
            IsSigned = content.IsSigned,
            IsEncrypted = content.IsEncrypted,
            HasRemoteContent = content.Body.HasRemoteContent,
        };

        return new MappedEmail(document, content.Listed.ToDictionary(source => source.Id), content.Resources);
    }

    /// <summary>
    /// 映射一个 MIME 实体的正文、附件和 S/MIME 标志。
    /// </summary>
    /// <remarks>
    /// 不透明签名（<c>signed-data</c>）先解开，被签名的内容当作邮件正文来映射；解不开时正文为空，邮件仍标记为已签名。
    /// </remarks>
    /// <param name="root">邮件的顶层 MIME 实体，可以为 <c>null</c>。</param>
    /// <param name="signedDepth">已经解开的不透明签名层数。</param>
    /// <returns>映射结果。</returns>
    internal static MappedContent MapContent(MimeEntity? root, int signedDepth = 0)
    {
        var smime = SmimeDetector.Detect(root);

        if (smime is { IsSigned: true, Carrier: { } carrier } && signedDepth < MaxSignedDepth && SignedContent.TryRead(carrier) is { } signed)
        {
            var inner = MapContent(signed, signedDepth + 1);
            return inner with { IsSigned = true, Resources = [.. inner.Resources, signed] };
        }

        var htmlSegments = MimeBodyFinder.Find(root, TextFormat.Html);
        var rtfSegments = MimeBodyFinder.Find(root, TextFormat.RichText);
        var plainSegments = MimeBodyFinder.Find(root, TextFormat.Plain);

        var bodyParts = new HashSet<MimeEntity>(ReferenceEqualityComparer.Instance);
        foreach (var text in GetDisplayedTexts(htmlSegments).Concat(GetDisplayedTexts(plainSegments)))
        {
            bodyParts.Add(text);
        }

        if (rtfSegments.FirstText is { } rtfText)
        {
            bodyParts.Add(rtfText);
        }

        // 拼接进正文的内嵌图片：给每张分配一个临时 Content-ID，拼好的 HTML 通过它引用图片，随后和 cid: 图片一样被换成 data: URI
        var imageContentIds = new Dictionary<MimePart, string>();
        foreach (var image in GetSegmentImages(htmlSegments).Concat(GetSegmentImages(plainSegments)))
        {
            imageContentIds.TryAdd(image, $"efv-inline-{imageContentIds.Count + 1}@segment.invalid");
        }

        var (sources, candidates) = MimeAttachmentCollector.Collect(root, bodyParts, smime.Carrier, imageContentIds);
        var resolver = new InlineResourceResolver(candidates);

        var body = EmailBodyBuilder.Build(
            () => htmlSegments.IsSegmented
                ? MimeBodyComposer.ComposeHtml(htmlSegments, imageContentIds)
                : GetText(htmlSegments.FirstText),
            () => GetText(rtfSegments.FirstText),
            () => plainSegments.IsSegmented ? null : GetText(plainSegments.FirstText),
            resolver,
            getPlainTextHtml: () => plainSegments.IsSegmented ? MimeBodyComposer.ComposePlainTextHtml(plainSegments, imageContentIds) : null);

        // 正文通过 cid: 引用了的内嵌图片不再列为附件；要在正文处理完之后才知道哪些被引用
        var listed = sources.Where(source => !source.IsInlineConsumed).ToList();

        return new MappedContent(body, listed, smime.IsSigned, smime.IsEncrypted, []);
    }

    // 实际会显示的文本部分：多个片段时全部显示，否则只显示第一个
    private static IEnumerable<TextPart> GetDisplayedTexts(BodySegments segments)
    {
        return segments.IsSegmented ? segments.Items.OfType<TextPart>() : segments.Items.OfType<TextPart>().Take(1);
    }

    private static IEnumerable<MimePart> GetSegmentImages(BodySegments segments)
    {
        return segments.IsSegmented ? segments.Images : [];
    }

    private static string? GetText(TextPart? part)
    {
        return part is null ? null : TextPartDecoder.GetText(part);
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

    private static string? NullIfBlank(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
