namespace EmailFileViewer.Core;

/// <summary>
/// 邮件摘要，仅包含主题和附件数量。
/// </summary>
/// <remarks>
/// 只用于 NativeAOT 可行性验证，后续会被完整的邮件文档模型取代。
/// </remarks>
/// <param name="Subject">邮件主题，邮件没有主题时为空字符串。</param>
/// <param name="AttachmentCount">附件数量。</param>
public sealed record EmailSummary(string Subject, int AttachmentCount);
