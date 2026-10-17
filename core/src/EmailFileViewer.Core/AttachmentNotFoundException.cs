namespace EmailFileViewer.Core;

/// <summary>
/// 按 Id 找不到附件时抛出。
/// </summary>
public sealed class AttachmentNotFoundException : Exception
{
    /// <summary>
    /// 创建异常。
    /// </summary>
    /// <param name="attachmentId">找不到的附件 Id。</param>
    public AttachmentNotFoundException(string attachmentId)
        : base($"邮件里没有 Id 为 \"{attachmentId}\" 的附件。")
    {
        AttachmentId = attachmentId;
    }

    /// <summary>
    /// 找不到的附件 Id。
    /// </summary>
    public string AttachmentId { get; }
}
