using System.Text;

namespace EmailFileViewer.Core.Parsing;

/// <summary>
/// 把纯文本正文转成 HTML：转义特殊字符，再包进保留换行和空白的容器。
/// </summary>
internal static class PlainTextHtml
{
    private const string OpenTag = "<pre style=\"margin:0;white-space:pre-wrap;overflow-wrap:anywhere;\">";

    private const string CloseTag = "</pre>";

    /// <summary>
    /// 转换纯文本。换行统一为 <c>\n</c>，只转义 <c>&amp;</c>、<c>&lt;</c>、<c>&gt;</c>，文本中的引号保持原样。
    /// </summary>
    /// <param name="text">纯文本正文。</param>
    /// <returns>可直接放进正文文档的 HTML 片段。</returns>
    internal static string Convert(string text)
    {
        var normalized = text.ReplaceLineEndings("\n");
        var builder = new StringBuilder(OpenTag.Length + normalized.Length + CloseTag.Length + 16);
        builder.Append(OpenTag);

        // HTML 规定 <pre> 开始标签后紧跟的第一个换行会被忽略，正文以换行开头时补一个，免得少一个空行
        if (normalized.StartsWith('\n'))
        {
            builder.Append('\n');
        }

        foreach (var character in normalized)
        {
            switch (character)
            {
                case '&':
                    builder.Append("&amp;");
                    break;
                case '<':
                    builder.Append("&lt;");
                    break;
                case '>':
                    builder.Append("&gt;");
                    break;
                default:
                    builder.Append(character);
                    break;
            }
        }

        builder.Append(CloseTag);
        return builder.ToString();
    }
}
