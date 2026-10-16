using System.Formats.Asn1;

namespace EmailFileViewer.Core.Parsing;

/// <summary>
/// 只看 CMS（PKCS#7）数据最外层的内容类型，用来区分“签名”和“加密”。不验证签名，也不解密。
/// </summary>
/// <remarks>
/// 不用 System.Security.Cryptography.Pkcs 的 <c>SignedCms</c>：它会解析证书，MsgReader 的 S/MIME 路径更会联网验证证书链。
/// </remarks>
internal static class CmsContentType
{
    private const string SignedDataOid = "1.2.840.113549.1.7.2";

    /// <summary>
    /// 判断 CMS 数据是不是 signedData（带签名，没有加密）。
    /// </summary>
    /// <param name="cms">smime.p7m 的内容。</param>
    /// <returns>是 signedData 时返回 <c>true</c>；是 envelopedData、无法解析或其他类型时返回 <c>false</c>。</returns>
    internal static bool IsSignedData(ReadOnlySpan<byte> cms)
    {
        try
        {
            var reader = new AsnReader(cms.ToArray(), AsnEncodingRules.BER);
            var contentInfo = reader.ReadSequence();
            return contentInfo.ReadObjectIdentifier() == SignedDataOid;
        }
        catch (AsnContentException)
        {
            return false;
        }
    }
}
