using MimeKit;

namespace EmailFileViewer.Core.Parsing;

/// <summary>
/// S/MIME 检测结果。
/// </summary>
/// <param name="IsSigned">邮件带有数字签名。</param>
/// <param name="IsEncrypted">邮件已加密，正文无法显示。</param>
/// <param name="Carrier">承载签名内容或加密内容的 <c>application/pkcs7-mime</c> 部分，它不是用户附件，也不是可显示的正文；没有时为 <c>null</c>。</param>
internal readonly record struct SmimeInfo(bool IsSigned, bool IsEncrypted, MimePart? Carrier)
{
    internal static SmimeInfo None { get; } = new(false, false, null);
}

/// <summary>
/// 只按 MIME 类型识别 S/MIME 签名和加密，不解析 CMS 结构，也不创建 MimeKit 的 S/MIME 上下文。
/// </summary>
/// <remarks>
/// 创建上下文会通过反射加载 SQLite，NativeAOT 下不可用（设计文档 D3 的 IL2026）；签名也不验证。
/// </remarks>
internal static class SmimeDetector
{
    // 明文签名里被签名的内容还可以是签名或加密，这样一层套一层最多往里看几层
    private const int MaxSignedNesting = 3;

    /// <summary>
    /// 检测邮件顶层的 S/MIME 结构。
    /// </summary>
    /// <remarks>
    /// 明文签名（<c>multipart/signed</c>）的第一个成员是被签名的内容：它本身是加密数据（签名里包着加密）时，邮件同时是已签名和已加密，
    /// 加密数据所在的部分作为载体，不再当成附件列出；它是不透明签名时，载体交给调用方解开。
    /// </remarks>
    /// <param name="body">邮件的顶层 MIME 实体。</param>
    /// <returns>检测结果。</returns>
    internal static SmimeInfo Detect(MimeEntity? body)
    {
        return Detect(body, MaxSignedNesting);
    }

    private static SmimeInfo Detect(MimeEntity? body, int remainingNesting)
    {
        if (body is null)
        {
            return SmimeInfo.None;
        }

        var contentType = body.ContentType;
        if (contentType.IsMimeType("multipart", "signed"))
        {
            var signedContent = body is Multipart { Count: > 0 } multipart && remainingNesting > 0
                ? Detect(multipart[0], remainingNesting - 1)
                : SmimeInfo.None;
            return new SmimeInfo(IsSigned: true, IsEncrypted: signedContent.IsEncrypted, Carrier: signedContent.Carrier);
        }

        if (body is not MimePart part
            || !(contentType.IsMimeType("application", "pkcs7-mime") || contentType.IsMimeType("application", "x-pkcs7-mime")))
        {
            return SmimeInfo.None;
        }

        string? smimeType = null;
        contentType.Parameters.TryGetValue("smime-type", out smimeType);

        if (string.Equals(smimeType, "signed-data", StringComparison.OrdinalIgnoreCase))
        {
            return new SmimeInfo(IsSigned: true, IsEncrypted: false, Carrier: part);
        }

        if (string.Equals(smimeType, "enveloped-data", StringComparison.OrdinalIgnoreCase)
            || (string.IsNullOrEmpty(smimeType) && HasP7mFileName(part)))
        {
            return new SmimeInfo(IsSigned: false, IsEncrypted: true, Carrier: part);
        }

        return SmimeInfo.None;
    }

    private static bool HasP7mFileName(MimePart part)
    {
        var name = part.FileName ?? part.ContentType.Name;
        return name is not null && name.EndsWith(".p7m", StringComparison.OrdinalIgnoreCase);
    }
}
