namespace EmailFileViewer.Core;

/// <summary>
/// 邮件文件无法打开时抛出，<see cref="Kind"/> 说明原因类别。
/// </summary>
public sealed class EmailParseException : Exception
{
    /// <summary>
    /// 创建异常。
    /// </summary>
    /// <param name="kind">原因类别。</param>
    /// <param name="message">给开发者看的详细说明。界面上显示的文字由 <paramref name="kind"/> 决定，不直接使用它。</param>
    /// <param name="innerException">引发该错误的底层异常。</param>
    public EmailParseException(EmailParseErrorKind kind, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
    }

    /// <summary>
    /// 原因类别。
    /// </summary>
    public EmailParseErrorKind Kind { get; }
}
