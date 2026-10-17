namespace EmailFileViewer.Core.Models;

/// <summary>
/// 附件的元数据。附件内容不放在这里，保存和打开时再按 <see cref="Id"/> 读取。
/// </summary>
/// <remarks>
/// 这一批只定义结构，附件列表在 <see cref="EmailDocument.Attachments"/> 中暂时为空，由后续任务填充。
/// </remarks>
/// <param name="Id">附件在所属邮件内的标识。</param>
/// <param name="FileName">文件名，附件没有文件名时为 <c>null</c>。</param>
/// <param name="Size">内容大小，单位为字节。</param>
/// <param name="MimeType">MIME 类型，未知时为 <c>null</c>。</param>
/// <param name="IsNestedEmail">是否为嵌套的邮件（可以继续打开）。</param>
public sealed record EmailAttachment(string Id, string? FileName, long Size, string? MimeType, bool IsNestedEmail);
