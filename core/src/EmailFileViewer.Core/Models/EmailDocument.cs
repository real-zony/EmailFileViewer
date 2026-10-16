using System.Text.Json.Serialization;
using EmailFileViewer.Core.Json;

namespace EmailFileViewer.Core.Models;

/// <summary>
/// 与界面无关的邮件文档，用于以 JSON 发给前端。
/// </summary>
/// <remarks>
/// 缺失的字段用空列表或 <c>null</c> 表示，不用空字符串占位。
/// 读取附件内容所需的底层对象不在这里，由 <see cref="ParsedEmail"/> 持有。
/// </remarks>
public sealed record EmailDocument
{
    /// <summary>
    /// 发件人。
    /// </summary>
    public IReadOnlyList<EmailAddress> From { get; init; } = [];

    /// <summary>
    /// 收件人。
    /// </summary>
    public IReadOnlyList<EmailAddress> To { get; init; } = [];

    /// <summary>
    /// 抄送。
    /// </summary>
    public IReadOnlyList<EmailAddress> Cc { get; init; } = [];

    /// <summary>
    /// 密送。
    /// </summary>
    public IReadOnlyList<EmailAddress> Bcc { get; init; } = [];

    /// <summary>
    /// 回复地址。
    /// </summary>
    public IReadOnlyList<EmailAddress> ReplyTo { get; init; } = [];

    /// <summary>
    /// 主题，邮件没有主题时为 <c>null</c>。
    /// </summary>
    public string? Subject { get; init; }

    /// <summary>
    /// 发送时间，统一换算成 UTC（偏移量为零），JSON 中输出为带 <c>Z</c> 的 ISO 8601；邮件没有可解析的日期时为 <c>null</c>。
    /// </summary>
    [JsonConverter(typeof(UtcDateTimeOffsetJsonConverter))]
    public DateTimeOffset? SentAtUtc { get; init; }

    /// <summary>
    /// 原始 Date 邮件头，界面悬停时用它显示邮件原本的时区。MSG 没有原始邮件头时为 <c>null</c>。
    /// </summary>
    public string? DateHeaderRaw { get; init; }

    /// <summary>
    /// 正文。
    /// </summary>
    public EmailBody Body { get; init; } = EmailBody.Empty;

    /// <summary>
    /// 附件列表。
    /// </summary>
    public IReadOnlyList<EmailAttachment> Attachments { get; init; } = [];

    /// <summary>
    /// 邮件带有数字签名（S/MIME）。签名是否有效不验证。
    /// </summary>
    public bool IsSigned { get; init; }

    /// <summary>
    /// 邮件已加密（S/MIME），无法显示正文。
    /// </summary>
    public bool IsEncrypted { get; init; }

    /// <summary>
    /// 正文引用了远程资源（图片、样式表、字体等）。
    /// </summary>
    public bool HasRemoteContent { get; init; }
}
