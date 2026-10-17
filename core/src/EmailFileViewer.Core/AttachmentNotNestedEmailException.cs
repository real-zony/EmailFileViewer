namespace EmailFileViewer.Core;

/// <summary>
/// 要把一个不是邮件的附件当作嵌套邮件打开时抛出。
/// </summary>
public sealed class AttachmentNotNestedEmailException : Exception
{
    /// <summary>
    /// 创建异常。
    /// </summary>
    /// <param name="attachmentId">附件 Id。</param>
    public AttachmentNotNestedEmailException(string attachmentId)
        : base($"Id 为 \"{attachmentId}\" 的附件不是邮件，不能作为嵌套邮件打开。")
    {
        AttachmentId = attachmentId;
    }

    /// <summary>
    /// 附件 Id。
    /// </summary>
    public string AttachmentId { get; }
}
