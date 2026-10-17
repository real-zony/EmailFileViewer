using System.Net;
using System.Text;
using MimeKit;
using MimeKit.Text;

namespace EmailFileViewer.Core.Parsing;

/// <summary>
/// 某一种格式的正文片段，按它们在邮件里出现的顺序排列。
/// </summary>
/// <remarks>
/// 大多数邮件只有一个文本部分。Apple Mail 这类客户端在插入图片时，会在 <c>multipart/mixed</c> 里交替放多个文本部分和内嵌图片，
/// 例如 <c>text/html</c>、<c>image/jpeg</c>（inline）、<c>text/html</c>，这时片段列表里有多个文本和夹在它们之间的图片。
/// </remarks>
internal sealed class BodySegments
{
    private readonly IReadOnlyList<MimePart> _items;

    internal BodySegments(IReadOnlyList<MimePart> items)
    {
        _items = items;
        TextCount = items.Count(item => item is TextPart);
    }

    /// <summary>
    /// 没有任何片段。
    /// </summary>
    internal static BodySegments Empty { get; } = new([]);

    /// <summary>
    /// 片段：文本部分，以及夹在文本部分之间的内嵌图片。第一个和最后一个一定是文本。
    /// </summary>
    internal IReadOnlyList<MimePart> Items => _items;

    /// <summary>
    /// 文本部分的个数。
    /// </summary>
    internal int TextCount { get; }

    /// <summary>
    /// 有两个以上文本部分，需要拼接成一份正文。
    /// </summary>
    internal bool IsSegmented => TextCount >= 2;

    /// <summary>
    /// 第一个文本部分，没有时为 <c>null</c>。
    /// </summary>
    internal TextPart? FirstText => _items.OfType<TextPart>().FirstOrDefault();

    /// <summary>
    /// 夹在文本之间的内嵌图片。
    /// </summary>
    internal IEnumerable<MimePart> Images => _items.Where(item => item is not TextPart);
}

/// <summary>
/// 在 MIME 结构里找出某种格式的正文片段。
/// </summary>
/// <remarks>
/// 沿用 MimeKit 的取法：<c>multipart/alternative</c> 里从后往前取第一个含有该格式的成员，<c>multipart/related</c> 只看根部分；
/// 其他的 multipart（<c>mixed</c> 等）按顺序全部走一遍，所以同一格式的多个文本部分都会被收进来。
/// 声明为附件的文本部分不是正文；多个文本部分里带文件名的，当作附件，不并入正文。
/// </remarks>
internal static class MimeBodyFinder
{
    /// <summary>
    /// 找出一种格式的正文片段。
    /// </summary>
    /// <param name="root">邮件的顶层 MIME 实体。</param>
    /// <param name="format">要找的正文格式。</param>
    /// <returns>片段；邮件里没有这种格式的正文时为 <see cref="BodySegments.Empty"/>。</returns>
    internal static BodySegments Find(MimeEntity? root, TextFormat format)
    {
        var items = new List<MimePart>();
        Collect(root, format, items);
        return Normalize(items);
    }

    private static void Collect(MimeEntity? entity, TextFormat format, List<MimePart> items)
    {
        switch (entity)
        {
            case TextPart text:
                if (!text.IsAttachment && IsFormat(text, format))
                {
                    items.Add(text);
                }

                break;
            case MultipartRelated related:
                Collect(related.Root, format, items);
                break;
            case Multipart alternative when alternative.ContentType.IsMimeType("multipart", "alternative"):
                for (var i = alternative.Count - 1; i >= 0; i--)
                {
                    var candidate = new List<MimePart>();
                    Collect(alternative[i], format, candidate);
                    if (candidate.Any(item => item is TextPart))
                    {
                        items.AddRange(candidate);
                        return;
                    }
                }

                break;
            case Multipart multipart:
                foreach (var child in multipart)
                {
                    Collect(child, format, items);
                }

                break;
            case MimePart part when IsInlineImage(part):
                items.Add(part);
                break;
        }
    }

    // 整理收集到的内容：文本部分里只保留没有文件名的（全都有文件名时只留第一个），再去掉首尾没有被文本夹住的图片
    private static BodySegments Normalize(List<MimePart> items)
    {
        var texts = items.OfType<TextPart>().ToList();
        if (texts.Count == 0)
        {
            return BodySegments.Empty;
        }

        var unnamed = texts.Where(text => string.IsNullOrWhiteSpace(text.FileName)).ToList();
        var chosen = unnamed.Count > 0 ? unnamed : texts.Take(1).ToList();
        var kept = new HashSet<MimePart>(chosen);
        var filtered = items.Where(item => item is not TextPart || kept.Contains(item)).ToList();

        var first = filtered.FindIndex(item => item is TextPart);
        var last = filtered.FindLastIndex(item => item is TextPart);
        return new BodySegments(filtered[first..(last + 1)]);
    }

    // 内嵌图片：image/*，而且没有声明为附件（inline 或没有 Content-Disposition）
    private static bool IsInlineImage(MimePart part)
    {
        return part.ContentType.IsMimeType("image", "*") && part.ContentDisposition?.IsAttachment != true;
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
}

/// <summary>
/// 把多个正文片段拼成一份 HTML（还没有清理）。
/// </summary>
/// <remarks>
/// 内嵌图片写成 <c>&lt;img src="cid:..."&gt;</c>，Content-ID 是调用方分配的临时值；
/// 拼好的文本之后要整体经过 <see cref="HtmlSanitizer"/>，由它把这些地址换成 <c>data:</c> URI，并把被用到的图片从附件列表里去掉。
/// </remarks>
internal static class MimeBodyComposer
{
    /// <summary>
    /// 拼接 HTML 片段：每份文档取 body 内的内容，head 里的样式放在最前面。
    /// </summary>
    /// <param name="segments">HTML 正文片段。</param>
    /// <param name="imageContentIds">图片对应的临时 Content-ID。</param>
    /// <returns>拼接后的 HTML。</returns>
    internal static string ComposeHtml(BodySegments segments, IReadOnlyDictionary<MimePart, string> imageContentIds)
    {
        var styles = new StringBuilder();
        var content = new StringBuilder();
        foreach (var item in segments.Items)
        {
            if (item is TextPart text)
            {
                var parts = HtmlBodyExtractor.Extract(TextPartDecoder.GetText(text));
                styles.Append(parts.HeadStyles);
                content.Append(parts.BodyContent);
            }
            else
            {
                content.Append(ImageTag(item, imageContentIds));
            }
        }

        return styles.Append(content).ToString();
    }

    /// <summary>
    /// 拼接纯文本片段：每段文本先转成 HTML（转义并保留换行）再依次拼接。
    /// </summary>
    /// <param name="segments">纯文本正文片段。</param>
    /// <param name="imageContentIds">图片对应的临时 Content-ID。</param>
    /// <returns>拼接后的 HTML。</returns>
    internal static string ComposePlainTextHtml(BodySegments segments, IReadOnlyDictionary<MimePart, string> imageContentIds)
    {
        var content = new StringBuilder();
        foreach (var item in segments.Items)
        {
            content.Append(item is TextPart text
                ? PlainTextHtml.Convert(TextPartDecoder.GetText(text))
                : ImageTag(item, imageContentIds));
        }

        return content.ToString();
    }

    private static string ImageTag(MimePart image, IReadOnlyDictionary<MimePart, string> imageContentIds)
    {
        var alt = WebUtility.HtmlEncode(image.FileName ?? string.Empty);
        return $"<img src=\"cid:{imageContentIds[image]}\" alt=\"{alt}\" style=\"max-width:100%;height:auto\">";
    }
}
