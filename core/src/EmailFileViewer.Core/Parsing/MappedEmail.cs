using EmailFileViewer.Core.Models;

namespace EmailFileViewer.Core.Parsing;

/// <summary>
/// 映射的结果：邮件文档，加上文档里列出的每个附件的来源（按 Id 索引）。
/// </summary>
/// <param name="Document">邮件文档。</param>
/// <param name="Attachments">附件来源，键是 <see cref="EmailAttachment.Id"/>；只含列在文档里的附件。</param>
/// <param name="Resources">映射过程中额外打开、需要和邮件一起释放的对象（例如从 MSG 的 smime.p7m 里解析出的 MIME 邮件）。</param>
internal sealed record MappedEmail(
    EmailDocument Document,
    IReadOnlyDictionary<string, AttachmentSource> Attachments,
    IReadOnlyList<IDisposable>? Resources = null);
