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

    private static void VerifySmime(string root, X509Certificate2 certificate, TemporarySecureMimeContext context)
    {
        var signed = Verification.LoadEml(root, "smime-signed.eml");
        var signedBody = signed.Body as MultipartSigned ?? throw new InvalidOperationException("样本自检失败：smime-signed.eml 应该是 multipart/signed");
        var signatures = signedBody.Verify(context);
        Verification.Require(signatures.Count == 1 && signatures.All(signature => signature.Verify(verifySignatureOnly: true)), "smime-signed.eml 的签名结构正确");

        var encrypted = Verification.LoadEml(root, "smime-encrypted.eml");
        var envelope = encrypted.Body as ApplicationPkcs7Mime ?? throw new InvalidOperationException("样本自检失败：smime-encrypted.eml 应该是 application/pkcs7-mime");
        Verification.Require(envelope.SecureMimeType == SecureMimeType.EnvelopedData, "smime-encrypted.eml 是 enveloped-data");

        using var pfx = new MemoryStream(certificate.Export(X509ContentType.Pfx, "efv"));
        context.Import(pfx, "efv");
        var decrypted = envelope.Decrypt(context) as TextPart ?? throw new InvalidOperationException("样本自检失败：smime-encrypted.eml 解密后应该是文本");
        Verification.Require(decrypted.Text.StartsWith("SECRET-BODY", StringComparison.Ordinal), "smime-encrypted.eml 解密后的正文");
    }

    private static X509Certificate2 CreateSelfSignedCertificate(string email)
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
