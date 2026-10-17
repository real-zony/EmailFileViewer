using EnvelopedCms = System.Security.Cryptography.Pkcs.EnvelopedCms;
using SignedCms = System.Security.Cryptography.Pkcs.SignedCms;
using System.Text;
using MimeKit;
using MimeKit.Cryptography;
using MsgKit;
using OpenMcdf;

namespace EmailFileViewer.SampleGenerator;

/// <summary>
/// S/MIME 的 MSG 样本。Outlook 把 S/MIME 邮件存成消息类 <c>IPM.Note.SMIME</c>（加密或不透明签名）或
/// <c>IPM.Note.SMIME.MultipartSigned</c>（明文签名），实际内容放在名为 smime.p7m 的附件里。
/// </summary>
/// <remarks>
/// MsgKit 没有设置消息类的 API，所以先按普通邮件保存，再用 OpenMcdf 把 PR_MESSAGE_CLASS 改掉。
/// 不用 MsgReader 读回这些文件：MsgReader 遇到 S/MIME 的 MSG 会验证证书链，可能联网。自检只用 OpenMcdf 和 System.Security.Cryptography.Pkcs。
/// 证书在运行时现做，私钥只在内存里，所以每次重新生成这几个文件的字节都不同。
/// </remarks>
internal static class SmimeMsgSamples
{
    public const string EncryptedFileName = "smime-msg-encrypted.msg";

    public const string OpaqueSignedFileName = "smime-msg-signed-opaque.msg";

    public const string ClearSignedFileName = "smime-msg-signed-clear.msg";

    /// <summary>
    /// 不透明签名的 MSG 里被签名的文本附件的文件名。
    /// </summary>
    public const string OpaqueNoteFileName = "opaque-note.txt";

    private const string MessageClassStream = "__substg1.0_001A001F";

    public static void WriteAll(SampleOutput output)
    {
        using var certificate = SpecialSamples.CreateSelfSignedCertificate("alice.zhang@example.com");
        using var context = new TemporarySecureMimeContext();
        var signer = new CmsSigner(certificate) { DigestAlgorithm = DigestAlgorithm.Sha256 };
        var recipients = new CmsRecipientCollection { new CmsRecipient(certificate) };
        var sentOn = new DateTime(2026, 10, 1, 6, 0, 0, DateTimeKind.Utc);

        var secret = new TextPart("plain") { Text = "SECRET-BODY: this text only exists inside the S/MIME envelope.\n" };
        output.Write(
            EncryptedFileName,
            CreateCarrierMsg("IPM.Note.SMIME", "S/MIME encrypted MSG", sentOn, ContentBytes(ApplicationPkcs7Mime.Encrypt(context, recipients, secret))));

        // 被签名的内容：纯文本正文加一个文本附件，附件和正文一起包在签名里
        var opaqueContent = new Multipart("mixed")
        {
            new TextPart("plain") { Text = "OPAQUE-SIGNED-BODY: the content is wrapped inside the signature.\n" },
            MimeSamples.TextAttachment(OpaqueNoteFileName, "This attachment is wrapped inside the opaque signature.\n"),
        };
        opaqueContent.Boundary = "----=_efv_opaque_msg";
        output.Write(
            OpaqueSignedFileName,
            CreateCarrierMsg("IPM.Note.SMIME", "S/MIME opaque-signed MSG", sentOn.AddMinutes(1), ContentBytes(ApplicationPkcs7Mime.Sign(context, signer, opaqueContent))));

        // 明文签名：smime.p7m 里是完整的 multipart/signed 邮件，正文和附件都在里面
        var clearMessage = MimeSamples.NewMessage("smime-msg-signed-clear", "S/MIME clear-signed MSG", Personas.Alice, Personas.BaseDate.AddHours(14));
        clearMessage.To.Add(Personas.Bob);
        clearMessage.Body = MultipartSigned.Create(context, signer, new TextPart("plain") { Text = "CLEAR-SIGNED-BODY: the text is readable without the signature.\n" });
        output.Write(
            ClearSignedFileName,
            CreateCarrierMsg("IPM.Note.SMIME.MultipartSigned", "S/MIME clear-signed MSG", sentOn.AddMinutes(2), output.Serialize(clearMessage)));
    }

    /// <summary>
    /// 自检：消息类正确，smime.p7m 附件在，CMS 数据的类型符合预期。
    /// </summary>
    public static void Verify(string root)
    {
        Check(root, EncryptedFileName, "IPM.Note.SMIME", "S/MIME encrypted MSG", cms =>
        {
            new EnvelopedCms().Decode(cms);
        });
        Check(root, OpaqueSignedFileName, "IPM.Note.SMIME", "S/MIME opaque-signed MSG", cms =>
        {
            var signedCms = new SignedCms();
            signedCms.Decode(cms);
            var content = Encoding.UTF8.GetString(signedCms.ContentInfo.Content);
            Verification.Require(content.Contains("OPAQUE-SIGNED-BODY", StringComparison.Ordinal) && content.Contains(OpaqueNoteFileName, StringComparison.Ordinal), "不透明签名的 smime.p7m 里有正文和附件");
        });
        Check(root, ClearSignedFileName, "IPM.Note.SMIME.MultipartSigned", "S/MIME clear-signed MSG", content =>
        {
            using var stream = new MemoryStream(content);
            var inner = MimeMessage.Load(stream);
            Verification.Require(inner.Body is MultipartSigned && inner.TextBody!.Contains("CLEAR-SIGNED-BODY", StringComparison.Ordinal), "明文签名的 smime.p7m 是 multipart/signed 邮件");
        });
    }

    private static void Check(string root, string fileName, string expectedClass, string expectedSubject, Action<byte[]> checkCarrier)
    {
        using var stream = File.OpenRead(Path.Combine(root, fileName));
        using var storage = RootStorage.Open(stream);

        Verification.RequireEqual(expectedClass, ReadString(storage, MessageClassStream), $"{fileName} 的消息类");
        Verification.RequireEqual(expectedSubject, ReadString(storage, "__substg1.0_0037001F"), $"{fileName} 的主题");

        var attachment = storage.OpenStorage("__attach_version1.0_#00000000");
        Verification.RequireEqual("smime.p7m", ReadString(attachment, "__substg1.0_3707001F"), $"{fileName} 的附件名");
        using var data = attachment.OpenStream("__substg1.0_37010102");
        var bytes = new byte[data.Length];
        data.ReadExactly(bytes);
        checkCarrier(bytes);
    }

    private static string ReadString(Storage storage, string streamName)
    {
        using var stream = storage.OpenStream(streamName);
        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return Encoding.Unicode.GetString(bytes);
    }

    private static byte[] ContentBytes(MimePart part)
    {
        using var buffer = new MemoryStream();
        part.Content!.DecodeTo(buffer);
        return buffer.ToArray();
    }

    private static byte[] CreateCarrierMsg(string messageClass, string subject, DateTime sentOnUtc, byte[] carrier)
    {
        using var email = new Email(new Sender("alice.zhang@example.com", "Alice Zhang"), subject)
        {
            SentOn = sentOnUtc,
        };
        email.Recipients.AddTo("bob.li@example.org", "Bob Li");
        email.Attachments.Add(new MemoryStream(carrier), "smime.p7m");

        using var saved = new MemoryStream();
        email.Save(saved);

        using (var root = RootStorage.Open(saved, StorageModeFlags.LeaveOpen))
        using (var stream = root.OpenStream(MessageClassStream))
        {
            var bytes = Encoding.Unicode.GetBytes(messageClass);
            stream.Position = 0;
            stream.SetLength(bytes.Length);
            stream.Write(bytes);
        }

        return saved.ToArray();
    }
}
