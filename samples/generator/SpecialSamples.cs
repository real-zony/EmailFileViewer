using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using EmailFileViewer.SampleGenerator.Formats;
using MimeKit;
using MimeKit.Cryptography;

namespace EmailFileViewer.SampleGenerator;

/// <summary>
/// winmail.dat（TNEF）和 S/MIME 样本。
/// </summary>
internal static class SpecialSamples
{
    public const string TnefDocxFileName = "meeting-minutes.docx";

    /// <summary>
    /// 不透明签名的 EML 里被签名的 PDF 附件的文件名。
    /// </summary>
    public const string OpaqueSignedAttachmentName = "signed-report.pdf";

    private const string DocxMimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    public static byte[] TnefDocx() => OfficeFiles.CreateDocx(
        "Meeting minutes",
        "This docx was packed inside a winmail.dat (TNEF) attachment.",
        "It is a placeholder document with no real content.");

    public static void WriteAll(SampleOutput output)
    {
        WriteTnef(output);
        WriteSmime(output);
    }

    // Outlook 用富文本格式发信时，附件会被打包进 winmail.dat，邮件里只剩这一个附件
    private static void WriteTnef(SampleOutput output)
    {
        var tnef = Tnef.Create(
            "Meeting minutes",
            "This message body is carried inside the TNEF stream.",
            new Tnef.TnefFile(TnefDocxFileName, DocxMimeType, TnefDocx()));

        var message = MimeSamples.NewMessage("tnef-winmail-docx", "Outlook winmail.dat carrying a docx", Personas.Bob, Personas.BaseDate.AddHours(11));
        message.To.Add(Personas.Alice);
        message.Headers.Add("X-MS-TNEF-Correlator", "<tnef-winmail-docx@samples.efv.invalid>");
        message.Body = new Multipart("mixed")
        {
            new TextPart("plain") { Text = "The only attachment of this message is winmail.dat. It contains a docx file (meeting-minutes.docx).\n" },
            new MimePart("application", "ms-tnef")
            {
                Content = new MimeContent(new MemoryStream(tnef)),
                ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
                ContentTransferEncoding = ContentEncoding.Base64,
                FileName = "winmail.dat",
            },
        };
        output.Write("tnef-winmail-docx.eml", message);
    }

    private static void WriteSmime(SampleOutput output)
    {
        // 证书在运行时现做，私钥只存在于内存里，不写盘、不提交。应用不做签名验证和解密
        using var certificate = CreateSelfSignedCertificate("alice.zhang@example.com");
        using var context = new TemporarySecureMimeContext();

        var signedMessage = MimeSamples.NewMessage("smime-signed", "S/MIME signed message", Personas.Alice, Personas.BaseDate.AddHours(12));
        signedMessage.To.Add(Personas.Bob);
        var signedContent = new TextPart("plain")
        {
            Text = "This message carries a detached S/MIME signature (multipart/signed).\n"
                + "The signature was made with a throw-away self-signed certificate, so it can never be validated.\n",
        };

        var signer = new CmsSigner(certificate) { DigestAlgorithm = DigestAlgorithm.Sha256 };
        signedMessage.Body = MultipartSigned.Create(context, signer, signedContent);
        output.Write("smime-signed.eml", signedMessage);

        // 不透明签名：邮件顶层只有一个 application/pkcs7-mime（signed-data），正文和附件都包在签名数据里
        var opaqueMessage = MimeSamples.NewMessage("smime-signed-opaque", "S/MIME opaque-signed message", Personas.Alice, Personas.BaseDate.AddHours(12).AddMinutes(30));
        opaqueMessage.To.Add(Personas.Bob);
        opaqueMessage.Body = ApplicationPkcs7Mime.Sign(context, signer, CreateOpaqueSignedContent());
        output.Write("smime-signed-opaque.eml", opaqueMessage);

        var encryptedMessage = MimeSamples.NewMessage("smime-encrypted", "S/MIME encrypted message", Personas.Alice, Personas.BaseDate.AddHours(13));
        encryptedMessage.To.Add(Personas.Bob);
        var secretContent = new TextPart("plain")
        {
            Text = "SECRET-BODY: this text only exists inside the S/MIME envelope.\n",
        };

        var recipients = new CmsRecipientCollection { new CmsRecipient(certificate) };
        encryptedMessage.Body = ApplicationPkcs7Mime.Encrypt(context, recipients, secretContent);
        output.Write("smime-encrypted.eml", encryptedMessage);

        // 自检：签名结构正确、加密内容能用同一张证书解开
        VerifySmime(output.Root, certificate, context);
    }

    /// <summary>
    /// 不透明签名的 EML 里被签名的内容：HTML 加纯文本两个备选，外加一个 PDF 附件。
    /// </summary>
    /// <remarks>
    /// 内部的 multipart 边界写死：签名覆盖的是这些字节，生成之后不能再改。
    /// </remarks>
    internal static Multipart CreateOpaqueSignedContent()
    {
        var alternative = new Multipart("alternative")
        {
            new TextPart("plain") { Text = "OPAQUE-SIGNED-PLAIN: the plain-text alternative inside the signature.\n" },
            new TextPart("html") { Text = "<html><body><h1>OPAQUE-SIGNED-HTML</h1><p>The body and the attachment of this message are wrapped inside the <b>signature</b>.</p></body></html>" },
        };
        alternative.Boundary = "----=_efv_opaque_alt";

        var mixed = new Multipart("mixed")
        {
            alternative,
            new MimePart("application", "pdf")
            {
                Content = new MimeContent(new MemoryStream(OfficeFiles.CreatePdf("Attachment inside an opaque signature"))),
                ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
                ContentTransferEncoding = ContentEncoding.Base64,
                FileName = OpaqueSignedAttachmentName,
            },
        };
        mixed.Boundary = "----=_efv_opaque_mixed";
        return mixed;
    }

    private static void VerifySmime(string root, X509Certificate2 certificate, TemporarySecureMimeContext context)
    {
        var signed = Verification.LoadEml(root, "smime-signed.eml");
        var signedBody = signed.Body as MultipartSigned ?? throw new InvalidOperationException("样本自检失败：smime-signed.eml 应该是 multipart/signed");
        var signatures = signedBody.Verify(context);
        Verification.Require(signatures.Count == 1 && signatures.All(signature => signature.Verify(verifySignatureOnly: true)), "smime-signed.eml 的签名结构正确");

        var opaque = Verification.LoadEml(root, "smime-signed-opaque.eml");
        var opaqueBody = opaque.Body as ApplicationPkcs7Mime ?? throw new InvalidOperationException("样本自检失败：smime-signed-opaque.eml 应该是 application/pkcs7-mime");
        Verification.Require(opaqueBody.SecureMimeType == SecureMimeType.SignedData, "smime-signed-opaque.eml 是 signed-data");
        using (var cms = new MemoryStream())
        {
            opaqueBody.Content!.DecodeTo(cms);
            var signedCms = new System.Security.Cryptography.Pkcs.SignedCms();
            signedCms.Decode(cms.ToArray());
            signedCms.CheckSignature(verifySignatureOnly: true);
            using var inner = new MemoryStream(signedCms.ContentInfo.Content);
            var innerText = Encoding.UTF8.GetString(inner.ToArray());
            Verification.Require(
                innerText.Contains("OPAQUE-SIGNED-PLAIN", StringComparison.Ordinal)
                && innerText.Contains("OPAQUE-SIGNED-HTML", StringComparison.Ordinal)
                && innerText.Contains(OpaqueSignedAttachmentName, StringComparison.Ordinal),
                "smime-signed-opaque.eml 的签名数据里有正文和附件");
        }

        var encrypted = Verification.LoadEml(root, "smime-encrypted.eml");
        var envelope = encrypted.Body as ApplicationPkcs7Mime ?? throw new InvalidOperationException("样本自检失败：smime-encrypted.eml 应该是 application/pkcs7-mime");
        Verification.Require(envelope.SecureMimeType == SecureMimeType.EnvelopedData, "smime-encrypted.eml 是 enveloped-data");

        using var pfx = new MemoryStream(certificate.Export(X509ContentType.Pfx, "efv"));
        context.Import(pfx, "efv");
        var decrypted = envelope.Decrypt(context) as TextPart ?? throw new InvalidOperationException("样本自检失败：smime-encrypted.eml 解密后应该是文本");
        Verification.Require(decrypted.Text.StartsWith("SECRET-BODY", StringComparison.Ordinal), "smime-encrypted.eml 解密后的正文");
    }

    internal static X509Certificate2 CreateSelfSignedCertificate(string email)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN=EFV Sample Signer, E={email}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            System.Security.Cryptography.X509Certificates.X509KeyUsageFlags.DigitalSignature
            | System.Security.Cryptography.X509Certificates.X509KeyUsageFlags.NonRepudiation
            | System.Security.Cryptography.X509Certificates.X509KeyUsageFlags.KeyEncipherment,
            critical: true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.4")], critical: false));
        var alternativeNames = new SubjectAlternativeNameBuilder();
        alternativeNames.AddEmailAddress(email);
        request.CertificateExtensions.Add(alternativeNames.Build());

        using var created = request.CreateSelfSigned(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2036, 1, 1, 0, 0, 0, TimeSpan.Zero));

        // 重新导入一次，确保私钥可导出，MimeKit 转换成 BouncyCastle 对象时需要
        return X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pfx, "efv"), "efv", X509KeyStorageFlags.Exportable);
    }
}
