using MimeKit;
using MimeKit.Tnef;

namespace EmailFileViewer.Core.Parsing;

/// <summary>
/// 遍历 MIME 结构，找出应该列给用户的附件，以及可能被正文引用的内嵌资源。
/// </summary>
/// <remarks>
/// <para>Id 按部分在结构里的位置生成，写法和 IMAP 的 BODY[1.2] 一致：顶层是 multipart 时子部分依次是 <c>1</c>、<c>2</c>……，
/// 子 multipart 的成员是 <c>2.1</c>；顶层是单个部分时 Id 为 <c>1</c>。winmail.dat 里解出的第 k 个文件是 <c>{winmail.dat 的 Id}.t{k}</c>。
/// 同一个文件每次解析得到的 Id 都一样。</para>
/// <para>不列出的部分：正文文本、multipart/signed 的签名部分、承载 S/MIME 内容的 pkcs7-mime、winmail.dat 本身（解出了文件时）。
/// winmail.dat 解不出任何文件时仍然列出它本身。没有文件名也不是附件的文本部分（例如 multipart/alternative 里的另一种正文）不算附件。</para>
/// </remarks>
internal sealed class MimeAttachmentCollector
{
    // TNEF 签名 0x223E9F78，小端存放
    private static readonly byte[] TnefSignature = [0x78, 0x9F, 0x3E, 0x22];

    private readonly ISet<MimeEntity> _bodyParts;
    private readonly MimePart? _carrier;
    private readonly IReadOnlyDictionary<MimePart, string> _segmentImageContentIds;
    private readonly List<AttachmentSource> _sources = [];
    private readonly List<InlineCandidate> _candidates = [];

    private MimeAttachmentCollector(ISet<MimeEntity> bodyParts, MimePart? carrier, IReadOnlyDictionary<MimePart, string> segmentImageContentIds)
    {
        _bodyParts = bodyParts;
        _carrier = carrier;
        _segmentImageContentIds = segmentImageContentIds;
    }

    /// <summary>
    /// 收集附件。
    /// </summary>
    /// <param name="body">邮件的顶层 MIME 实体。</param>
    /// <param name="bodyParts">已经被选作正文的文本部分。</param>
    /// <param name="carrier">承载 S/MIME 内容的 pkcs7-mime 部分，没有时为 <c>null</c>。</param>
    /// <param name="segmentImageContentIds">拼接进正文的内嵌图片，以及给它们分配的临时 Content-ID；这些图片除了自己的 Content-ID，还可以用临时值引用。</param>
    /// <returns>按结构顺序排列的附件来源，以及可被正文引用的内嵌资源。</returns>
    internal static (IReadOnlyList<AttachmentSource> Sources, IReadOnlyList<InlineCandidate> Candidates) Collect(
        MimeEntity? body,
        ISet<MimeEntity> bodyParts,
        MimePart? carrier,
        IReadOnlyDictionary<MimePart, string> segmentImageContentIds)
    {
        var collector = new MimeAttachmentCollector(bodyParts, carrier, segmentImageContentIds);
        if (body is not null)
        {
            collector.Walk(body, body is Multipart ? string.Empty : "1");
        }

        return (collector._sources, collector._candidates);
    }

    private void Walk(MimeEntity entity, string path)
    {
        switch (entity)
        {
            case Multipart multipart:
                WalkMultipart(multipart, path);
                break;
            case MessagePart messagePart:
                _sources.Add(MimeMessageAttachment.Create(path, messagePart));
                break;
            case TnefPart tnef:
                AddTnef(tnef, path);
                break;
            case MimePart part:
                AddPart(part, path);
                break;
        }
    }

    private void WalkMultipart(Multipart multipart, string path)
    {
        // multipart/signed 的第一个成员是被签名的内容，其余是签名，不是附件
        var count = multipart.ContentType.IsMimeType("multipart", "signed") ? Math.Min(1, multipart.Count) : multipart.Count;
        for (var i = 0; i < count; i++)
        {
            Walk(multipart[i], path.Length == 0 ? (i + 1).ToString() : $"{path}.{i + 1}");
        }
    }

    private void AddPart(MimePart part, string path)
    {
        if (ReferenceEquals(part, _carrier) || _bodyParts.Contains(part))
        {
            return;
        }

        if (part is TextPart && !IsAttachmentText(part))
        {
            return;
        }

        var source = MimePartAttachment.Create(path, part);
        _sources.Add(source);

        if (!string.IsNullOrWhiteSpace(part.ContentId))
        {
            _candidates.Add(new InlineCandidate(source, part.ContentId, AllowNameLookup: false));
        }

        if (_segmentImageContentIds.TryGetValue(part, out var segmentContentId))
        {
            _candidates.Add(new InlineCandidate(source, segmentContentId, AllowNameLookup: false));
        }
    }

    private static bool HasTnefSignature(MimePart tnef)
    {
        if (tnef.Content is null)
        {
            return false;
        }

        using var buffer = new MemoryStream();
        tnef.Content.DecodeTo(buffer);
        return buffer.Length >= 4 && buffer.GetBuffer().AsSpan(0, 4).SequenceEqual(TnefSignature);
    }

    // 文本部分只有声明为附件或者带文件名时才算附件
    private static bool IsAttachmentText(MimePart part)
    {
        return part.ContentDisposition?.IsAttachment == true || !string.IsNullOrWhiteSpace(part.FileName);
    }

    // 展开 winmail.dat：只要有文件名的成员（TNEF 里的正文会作为没有文件名的文本部分一起返回）。解不开、或者解不出任何文件，就当普通附件
    private void AddTnef(TnefPart tnef, string path)
    {
        List<MimeEntity> extracted;
        try
        {
            // TNEF 数据以固定的 4 字节签名开头；签名不对说明不是有效的 TNEF，MimeKit 对这种数据不报错、只是什么都解不出来
            if (!HasTnefSignature(tnef))
            {
                _sources.Add(MimePartAttachment.Create(path, tnef));
                return;
            }

            extracted = tnef.ExtractAttachments().ToList();
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException))
        {
            _sources.Add(MimePartAttachment.Create(path, tnef));
            return;
        }

        var listedCount = _sources.Count;
        for (var i = 0; i < extracted.Count; i++)
        {
            if (extracted[i] is MimePart { FileName: not null } member)
            {
                _sources.Add(MimePartAttachment.Create($"{path}.t{i}", member));
            }
        }

        // 一个文件也没解出来（只有正文，或内容损坏）：保留 winmail.dat 本身，用户至少可以保存它
        if (_sources.Count == listedCount)
        {
            _sources.Add(MimePartAttachment.Create(path, tnef));
        }
    }
}
