using System.Formats.Asn1;
using System.Text;

namespace EmailFileViewer.Core.Tests;

/// <summary>
/// 构造测试用的 CMS signedData 和 S/MIME 邮件，不需要证书和私钥。
/// </summary>
/// <remarks>
/// 构造出来的 signedData 没有签名者，签名当然也无法验证；Core 只解码、不验证，所以足够测试“取出被签名的内容”。
/// </remarks>
internal static class TestCms
{
    private const string SignedDataOid = "1.2.840.113549.1.7.2";
    private const string DataOid = "1.2.840.113549.1.7.1";

    /// <summary>
    /// 构造一个 signedData，把内容放进去。
    /// </summary>
    /// <param name="content">被签名的内容。</param>
    /// <param name="detached">为 <c>true</c> 时不放入内容（签名与内容分离）。</param>
    /// <returns>DER 编码的 CMS 数据。</returns>
    public static byte[] SignedData(byte[] content, bool detached = false)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            writer.WriteObjectIdentifier(SignedDataOid);
            using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0)))
            using (writer.PushSequence())
            {
                writer.WriteInteger(1);
                using (writer.PushSetOf())
                {
                }

                using (writer.PushSequence())
                {
                    writer.WriteObjectIdentifier(DataOid);
                    if (!detached)
                    {
                        using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0)))
                        {
                            writer.WriteOctetString(content);
                        }
                    }
                }

                using (writer.PushSetOf())
                {
                }
            }
        }

        return writer.Encode();
    }

    /// <summary>
    /// 构造一个把 MIME 实体文本签进去的 signedData。
    /// </summary>
    /// <param name="entityText">被签名的 MIME 实体（头部加正文），换行会统一成 CRLF。</param>
    /// <returns>DER 编码的 CMS 数据。</returns>
    public static byte[] SignEntity(string entityText)
    {
        return SignedData(Encoding.UTF8.GetBytes(entityText.ReplaceLineEndings("\r\n")));
    }

    /// <summary>
    /// 把一段 CMS 数据再签一层：外层的 signedData 里装的是一个 <c>application/pkcs7-mime; smime-type=signed-data</c> 实体。
    /// </summary>
    /// <param name="innerCms">里面一层的 CMS 数据。</param>
    /// <returns>外层的 CMS 数据。</returns>
    public static byte[] SignAgain(byte[] innerCms)
    {
        return SignEntity(Pkcs7Entity("signed-data", innerCms));
    }

    /// <summary>
    /// 构造一个 <c>application/pkcs7-mime</c> 的 MIME 实体文本（头部加 Base64 正文）。
    /// </summary>
    /// <param name="smimeType"><c>smime-type</c> 参数的值。</param>
    /// <param name="cms">CMS 数据。</param>
    /// <returns>MIME 实体文本。</returns>
    public static string Pkcs7Entity(string smimeType, byte[] cms)
    {
        return $"Content-Type: application/pkcs7-mime; smime-type={smimeType}; name=smime.p7m\r\n"
            + "Content-Transfer-Encoding: base64\r\n"
            + "\r\n"
            + Convert.ToBase64String(cms, Base64FormattingOptions.InsertLineBreaks);
    }

    /// <summary>
    /// 写出一封顶层是不透明签名的邮件。
    /// </summary>
    /// <param name="temp">临时目录。</param>
    /// <param name="cms">邮件正文部分的 CMS 数据。</param>
    /// <returns>邮件文件的完整路径。</returns>
    public static string WriteSignedMail(TempDirectory temp, byte[] cms)
    {
        return temp.WriteMail(
            "opaque-signed.eml",
            "From: Alice <alice@example.com>\n"
            + "To: Bob <bob@example.com>\n"
            + "Subject: opaque signed\n"
            + "Date: Thu, 1 Oct 2026 10:00:00 +0000\n"
            + "MIME-Version: 1.0\n"
            + Pkcs7Entity("signed-data", cms)
            + "\n");
    }
}
