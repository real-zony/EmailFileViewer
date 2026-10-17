using System.Security.Cryptography.Pkcs;
using MimeKit;

namespace EmailFileViewer.Core.Parsing;

/// <summary>
/// 从不透明签名（<c>application/pkcs7-mime; smime-type=signed-data</c>）里取出被签名的内容。
/// </summary>
/// <remarks>
/// 不透明签名把邮件内容包在签名数据里面，不解开就看不到正文。这里只解码 CMS 结构并读出内容，
/// <b>不调用 <c>CheckSignature</c>、不读取证书、不建证书链</b>，所以不会产生任何网络请求；签名有效与否不验证，界面上会注明“未验证”。
/// </remarks>
internal static class SignedContent
{
    // CMS 的 id-data：被签名的内容是一个普通的 MIME 实体（RFC 8551）
    private const string DataContentTypeOid = "1.2.840.113549.1.7.1";

    /// <summary>
    /// 取出 MIME 里 <c>application/pkcs7-mime</c> 部分承载的被签名内容。
    /// </summary>
    /// <param name="carrier">承载 signed-data 的部分。</param>
    /// <returns>被签名的 MIME 实体，调用方负责释放；无法解开（不是有效的 CMS、内容为空、签名与内容分离）时为 <c>null</c>。</returns>
    internal static MimeEntity? TryRead(MimePart carrier)
    {
        if (carrier.Content is null)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        carrier.Content.DecodeTo(buffer);
        return TryRead(buffer.ToArray());
    }

    /// <summary>
    /// 从 CMS 的字节里取出被签名的内容，并按 MIME 实体解析。
    /// </summary>
    /// <param name="cms">CMS（PKCS#7）数据，例如 MSG 里 smime.p7m 附件的内容。</param>
    /// <returns>被签名的 MIME 实体，调用方负责释放；无法解开时为 <c>null</c>。</returns>
    internal static MimeEntity? TryRead(byte[] cms)
    {
        try
        {
            var signedCms = new SignedCms();
            signedCms.Decode(cms);

            var contentInfo = signedCms.ContentInfo;
            if (contentInfo.ContentType.Value != DataContentTypeOid || contentInfo.Content.Length == 0)
            {
                return null;
            }

            using var stream = new MemoryStream(contentInfo.Content);
            return MimeEntity.Load(MimeParserOptions.Create(), stream);
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException))
        {
            // CMS 结构损坏（CryptographicException）或内容不是合法的 MIME（FormatException）：当作解不开，邮件仍然标记为已签名
            return null;
        }
    }
}
