namespace EmailFileViewer.Core.Models;

/// <summary>
/// 邮件正文的原始格式，也就是处理后的 HTML 是从哪种正文得来的。
/// </summary>
public enum BodyFormat
{
    /// <summary>
    /// 邮件没有可显示的正文。
    /// </summary>
    None,

    /// <summary>
    /// HTML 正文。MSG 中封装在 RTF 里的 HTML 也算这一种。
    /// </summary>
    Html,

    /// <summary>
    /// RTF 正文，已经转换成 HTML。
    /// </summary>
    Rtf,

    /// <summary>
    /// 纯文本正文，已经转义并包进保留换行的容器。
    /// </summary>
    PlainText,
}
