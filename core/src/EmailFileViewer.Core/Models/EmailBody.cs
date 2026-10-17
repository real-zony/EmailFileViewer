namespace EmailFileViewer.Core.Models;

/// <summary>
/// 邮件正文：原始格式加处理后的 HTML。
/// </summary>
/// <param name="SourceFormat">正文来自哪种格式。</param>
/// <param name="Html">用来显示的 HTML；没有正文时为 <c>null</c>。</param>
public sealed record EmailBody(BodyFormat SourceFormat, string? Html)
{
    /// <summary>
    /// 没有正文。
    /// </summary>
    public static EmailBody Empty { get; } = new(BodyFormat.None, null);
}
