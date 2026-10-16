using EmailFileViewer.Core;

namespace EmailFileViewer.Sidecar.Protocol;

/// <summary>
/// 协议里的错误码。出现在错误响应的 <c>error.code</c> 里，Rust 端和界面按它判断是什么错误，<c>message</c> 只给开发者看。
/// </summary>
internal static class ErrorCodes
{
    /// <summary>文件不是邮件（对应 <see cref="EmailParseErrorKind.UnsupportedFormat"/>）。</summary>
    internal const string UnsupportedFormat = "unsupported_format";

    /// <summary>文件已损坏，无法解析（对应 <see cref="EmailParseErrorKind.Corrupted"/>）。</summary>
    internal const string Corrupted = "corrupted";

    /// <summary>文件无法读取：不存在、没有权限或发生 IO 错误（对应 <see cref="EmailParseErrorKind.Unreadable"/>）。</summary>
    internal const string Unreadable = "unreadable";

    /// <summary>没有这个 <c>docId</c> 的文档：从未打开、已经关闭。</summary>
    internal const string DocumentNotFound = "document_not_found";

    /// <summary>文档里没有这个 <c>attachmentId</c> 的附件。</summary>
    internal const string AttachmentNotFound = "attachment_not_found";

    /// <summary>要当作嵌套邮件打开的附件不是邮件。</summary>
    internal const string NotNestedEmail = "not_nested_email";

    /// <summary>附件是可执行文件或脚本，只能保存，不能提取出来用系统程序打开。</summary>
    internal const string ExecutableRefused = "executable_refused";

    /// <summary>无法写入目标位置：文件夹不存在、没有权限、磁盘已满等。</summary>
    internal const string WriteFailed = "write_failed";

    /// <summary><c>params</c> 缺少必需的字段，或字段的类型、取值不对。</summary>
    internal const string InvalidParams = "invalid_params";

    /// <summary>这一行不是合法的请求：不是 JSON、不是对象、缺少 <c>id</c> 或 <c>method</c>。</summary>
    internal const string InvalidRequest = "invalid_request";

    /// <summary>没有这个方法。</summary>
    internal const string MethodNotFound = "method_not_found";

    /// <summary>处理请求时发生了未预期的错误；只影响这一个请求，sidecar 继续运行。</summary>
    internal const string InternalError = "internal_error";

    /// <summary>
    /// 全部错误码。
    /// </summary>
    internal static IReadOnlyList<string> All { get; } =
    [
        UnsupportedFormat,
        Corrupted,
        Unreadable,
        DocumentNotFound,
        AttachmentNotFound,
        NotNestedEmail,
        ExecutableRefused,
        WriteFailed,
        InvalidParams,
        InvalidRequest,
        MethodNotFound,
        InternalError,
    ];

    /// <summary>
    /// 把解析失败的类别换成错误码。
    /// </summary>
    /// <param name="kind">解析失败的类别。</param>
    /// <returns>错误码。</returns>
    internal static string From(EmailParseErrorKind kind)
    {
        return kind switch
        {
            EmailParseErrorKind.UnsupportedFormat => UnsupportedFormat,
            EmailParseErrorKind.Corrupted => Corrupted,
            EmailParseErrorKind.Unreadable => Unreadable,
            _ => InternalError,
        };
    }
}
