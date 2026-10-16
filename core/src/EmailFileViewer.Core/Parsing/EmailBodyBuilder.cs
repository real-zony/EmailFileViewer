using EmailFileViewer.Core.Models;

namespace EmailFileViewer.Core.Parsing;

/// <summary>
/// 选出的正文，以及正文是否引用了远程资源。
/// </summary>
/// <param name="Body">正文。</param>
/// <param name="HasRemoteContent">正文引用了需要联网的资源。</param>
internal readonly record struct BuiltBody(EmailBody Body, bool HasRemoteContent);

/// <summary>
/// 按 HTML、RTF、纯文本的优先级选择正文，并统一成清理过的 HTML。
/// </summary>
internal static class EmailBodyBuilder
{
    /// <summary>
    /// 选出正文。三种来源按优先级依次取，前面的有内容就不再读后面的。
    /// </summary>
    /// <remarks>
    /// HTML 和 RTF 转出的 HTML 都要经过 <see cref="HtmlSanitizer"/>；纯文本转义后包进保留换行的容器。
    /// 内容为 <c>null</c> 或只有空白的来源视为不存在。RTF 转换失败时退回纯文本，没有纯文本才算邮件已损坏。
    /// </remarks>
    /// <param name="getHtml">读取 HTML 正文。</param>
    /// <param name="getRtf">读取 RTF 正文。</param>
    /// <param name="getPlainText">读取纯文本正文。</param>
    /// <param name="resolver">解析内嵌资源的对象，没有时为 <c>null</c>。</param>
    /// <param name="convertRtf">RTF 转 HTML 的函数；默认用 RtfPipe，测试里用来模拟转换失败。</param>
    /// <returns>正文；三种来源都没有内容时正文为 <see cref="EmailBody.Empty"/>。</returns>
    /// <exception cref="EmailParseException">RTF 转换失败，而且没有纯文本可以替代，类别为 <see cref="EmailParseErrorKind.Corrupted"/>。</exception>
    internal static BuiltBody Build(
        Func<string?> getHtml,
        Func<string?> getRtf,
        Func<string?> getPlainText,
        InlineResourceResolver? resolver,
        Func<string, string>? convertRtf = null)
    {
        var html = getHtml();
        if (!string.IsNullOrWhiteSpace(html))
        {
            var sanitized = HtmlSanitizer.Sanitize(html, resolver);
            return new BuiltBody(new EmailBody(BodyFormat.Html, sanitized.Html), sanitized.HasRemoteContent);
        }

        Exception? rtfFailure = null;
        var rtf = getRtf();
        if (!string.IsNullOrWhiteSpace(rtf))
        {
            try
            {
                var converted = (convertRtf ?? ConvertWithRtfPipe)(rtf);
                var sanitized = HtmlSanitizer.Sanitize(converted, resolver: null);
                return new BuiltBody(new EmailBody(BodyFormat.Rtf, sanitized.Html), sanitized.HasRemoteContent);
            }
            catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException))
            {
                rtfFailure = ex;
            }
        }

        var plainText = getPlainText();
        if (!string.IsNullOrWhiteSpace(plainText))
        {
            return new BuiltBody(new EmailBody(BodyFormat.PlainText, PlainTextHtml.Convert(plainText)), HasRemoteContent: false);
        }

        if (rtfFailure is not null)
        {
            throw new EmailParseException(EmailParseErrorKind.Corrupted, "RTF 正文无法转换成 HTML，也没有纯文本正文可以替代。", rtfFailure);
        }

        return new BuiltBody(EmailBody.Empty, HasRemoteContent: false);
    }

    private static string ConvertWithRtfPipe(string rtf)
    {
        return RtfPipe.Rtf.ToHtml(rtf);
    }
}
