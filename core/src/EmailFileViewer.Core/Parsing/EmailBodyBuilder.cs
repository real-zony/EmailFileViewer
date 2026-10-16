using EmailFileViewer.Core.Models;

namespace EmailFileViewer.Core.Parsing;

/// <summary>
/// 按 HTML、RTF、纯文本的优先级选择正文，并统一成 HTML。
/// </summary>
internal static class EmailBodyBuilder
{
    /// <summary>
    /// 选出正文。三种来源按优先级依次取，前面的有内容就不再读后面的。
    /// </summary>
    /// <remarks>
    /// HTML 原样放入，不做清理、不替换 <c>cid:</c>、不检测远程内容；RTF 转成 HTML；纯文本转义后包进保留换行的容器。
    /// 内容为 <c>null</c> 或只有空白的来源视为不存在。
    /// </remarks>
    /// <param name="getHtml">读取 HTML 正文。</param>
    /// <param name="getRtf">读取 RTF 正文。</param>
    /// <param name="getPlainText">读取纯文本正文。</param>
    /// <returns>正文；三种来源都没有内容时返回 <see cref="EmailBody.Empty"/>。</returns>
    internal static EmailBody Build(Func<string?> getHtml, Func<string?> getRtf, Func<string?> getPlainText)
    {
        var html = getHtml();
        if (!string.IsNullOrWhiteSpace(html))
        {
            return new EmailBody(BodyFormat.Html, html);
        }

        var rtf = getRtf();
        if (!string.IsNullOrWhiteSpace(rtf))
        {
            return new EmailBody(BodyFormat.Rtf, RtfPipe.Rtf.ToHtml(rtf));
        }

        var plainText = getPlainText();
        if (!string.IsNullOrWhiteSpace(plainText))
        {
            return new EmailBody(BodyFormat.PlainText, PlainTextHtml.Convert(plainText));
        }

        return EmailBody.Empty;
    }
}
