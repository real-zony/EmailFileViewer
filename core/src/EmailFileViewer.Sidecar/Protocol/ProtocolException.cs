namespace EmailFileViewer.Sidecar.Protocol;

/// <summary>
/// 请求处理中可以预料的失败。会原样变成错误响应，不写诊断日志。
/// </summary>
internal sealed class ProtocolException : Exception
{
    /// <summary>
    /// 创建异常。
    /// </summary>
    /// <param name="code">错误码，见 <see cref="ErrorCodes"/>。</param>
    /// <param name="message">给开发者看的说明。</param>
    /// <param name="innerException">引发这个错误的底层异常。</param>
    internal ProtocolException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    /// <summary>
    /// 错误码。
    /// </summary>
    internal string Code { get; }
}
