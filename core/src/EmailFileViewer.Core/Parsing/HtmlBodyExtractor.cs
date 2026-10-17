using System.Text;

namespace EmailFileViewer.Core.Parsing;

/// <summary>
/// 一份 HTML 文档拆出来的两部分。
/// </summary>
/// <param name="HeadStyles"><c>&lt;head&gt;</c> 里的 <c>&lt;style&gt;</c> 元素（原样拼接），没有 body 标签时为空。</param>
/// <param name="BodyContent"><c>&lt;body&gt;</c> 标签之内的内容；没有 body 标签时是整份文本。</param>
internal readonly record struct HtmlBodyParts(string HeadStyles, string BodyContent);

/// <summary>
/// 在把几份 HTML 拼成一份时，从每份完整文档里取出 <c>&lt;body&gt;</c> 里的内容。
/// </summary>
/// <remarks>
/// 只负责定位边界：跳过注释、引号里的 <c>&gt;</c> 和原始文本元素（脚本、样式等）里的伪标签。
/// 取出的内容之后还要整体经过 <see cref="HtmlSanitizer"/>，所以这里即使把边界找错，也只影响显示效果，不影响安全。
/// body 标签自身的属性（背景色、边距等）不保留。
/// </remarks>
internal static class HtmlBodyExtractor
{
    // 里面的内容是原始文本，不会有标签：跳过它们，免得其中的 "</body>" 之类字样被误认
    private static readonly string[] RawTextElements = ["script", "style", "title", "textarea", "xmp"];

    /// <summary>
    /// 拆出 head 里的样式和 body 里的内容。
    /// </summary>
    /// <param name="html">一份完整的 HTML 文档或片段。</param>
    /// <returns>拆分结果。</returns>
    internal static HtmlBodyParts Extract(string html)
    {
        var styles = new StringBuilder();
        var bodyStart = -1;
        var bodyEnd = -1;
        var index = 0;

        while (index < html.Length)
        {
            var open = html.IndexOf('<', index);
            if (open < 0)
            {
                break;
            }

            if (string.CompareOrdinal(html, open, "<!--", 0, 4) == 0)
            {
                var commentEnd = html.IndexOf("-->", open + 4, StringComparison.Ordinal);
                index = commentEnd < 0 ? html.Length : commentEnd + 3;
                continue;
            }

            var isEndTag = open + 1 < html.Length && html[open + 1] == '/';
            var nameStart = open + (isEndTag ? 2 : 1);
            var nameEnd = nameStart;
            while (nameEnd < html.Length && (char.IsAsciiLetterOrDigit(html[nameEnd]) || html[nameEnd] is '-' or ':'))
            {
                nameEnd++;
            }

            if (nameEnd == nameStart)
            {
                index = open + 1;
                continue;
            }

            var tagEnd = FindTagEnd(html, nameEnd);
            if (tagEnd < 0)
            {
                break;
            }

            var name = html.AsSpan(nameStart, nameEnd - nameStart);
            index = tagEnd + 1;

            if (isEndTag)
            {
                if (name.Equals("body", StringComparison.OrdinalIgnoreCase))
                {
                    bodyEnd = open;
                }

                continue;
            }

            if (bodyStart < 0 && name.Equals("body", StringComparison.OrdinalIgnoreCase))
            {
                bodyStart = tagEnd + 1;
                continue;
            }

            if (IsRawTextElement(name))
            {
                var elementEnd = FindElementEnd(html, name.ToString(), tagEnd + 1);
                if (bodyStart < 0 && name.Equals("style", StringComparison.OrdinalIgnoreCase))
                {
                    styles.Append(html, open, elementEnd - open);
                }

                index = elementEnd;
            }
        }

        if (bodyStart < 0)
        {
            return new HtmlBodyParts(string.Empty, html);
        }

        if (bodyEnd < bodyStart)
        {
            bodyEnd = html.Length;
        }

        return new HtmlBodyParts(styles.ToString(), html[bodyStart..bodyEnd]);
    }

    private static bool IsRawTextElement(ReadOnlySpan<char> name)
    {
        foreach (var element in RawTextElements)
        {
            if (name.Equals(element, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // 属性值用引号括起来时，里面的 > 不算标签结束；只有紧跟在等号后面的引号才算属性值的开头
    private static int FindTagEnd(string html, int start)
    {
        var quote = '\0';
        var previousSignificant = '\0';
        for (var i = start; i < html.Length; i++)
        {
            var c = html[i];
            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                    previousSignificant = c;
                }

                continue;
            }

            if (c == '>')
            {
                return i;
            }

            if ((c == '"' || c == '\'') && previousSignificant == '=')
            {
                quote = c;
                continue;
            }

            if (!char.IsWhiteSpace(c))
            {
                previousSignificant = c;
            }
        }

        return -1;
    }

    // 返回结束标签 </name ...> 之后的位置；找不到结束标签就一直到文本结尾
    private static int FindElementEnd(string html, string name, int start)
    {
        var close = html.IndexOf("</" + name, start, StringComparison.OrdinalIgnoreCase);
        if (close < 0)
        {
            return html.Length;
        }

        var end = html.IndexOf('>', close);
        return end < 0 ? html.Length : end + 1;
    }
}
