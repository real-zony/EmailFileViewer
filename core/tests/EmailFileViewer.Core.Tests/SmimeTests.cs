using EmailFileViewer.Core.Models;

namespace EmailFileViewer.Core.Tests;

/// <summary>
/// 对应规格 email-viewing 中的“加密与签名邮件提示”。S/MIME 只按 MIME 类型识别，不验证签名、不解密。
/// </summary>
public class SmimeTests
{
    [Fact]
    [Trait("Scenario", "email-viewing / 加密与签名邮件提示 / 签名邮件")]
    public async Task MultipartSigned_IsMarkedSignedAndBodyIsShown()
    {
        using var parsed = await MailAssert.ParseSampleAsync("smime-signed.eml");
        var document = parsed.Document;

        Assert.True(document.IsSigned);
        Assert.False(document.IsEncrypted);
        Assert.Equal("S/MIME signed message", document.Subject);
        Assert.Equal(BodyFormat.PlainText, document.Body.SourceFormat);
        Assert.False(string.IsNullOrWhiteSpace(MailAssert.PlainTextOf(document)));
        Assert.Empty(document.Attachments);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 加密与签名邮件提示 / 签名邮件（Thunderbird 发出的真实邮件）")]
    public async Task ThunderbirdSignedMail_IsSignedWithBodyAndOnlyTheRealAttachment()
    {
        using var parsed = await MailAssert.ParseSampleAsync("smime-signed-thunderbird.eml");
        var document = parsed.Document;

        Assert.True(document.IsSigned);
        Assert.False(document.IsEncrypted);
        Assert.Equal(BodyFormat.PlainText, document.Body.SourceFormat);
        Assert.Equal("signed.jpg", Assert.Single(document.Attachments).FileName);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 加密与签名邮件提示 / 加密邮件")]
    public async Task EnvelopedData_IsMarkedEncryptedWithHeadersButNoBody()
    {
        using var parsed = await MailAssert.ParseSampleAsync("smime-encrypted.eml");
        var document = parsed.Document;

        Assert.True(document.IsEncrypted);
        Assert.False(document.IsSigned);
        Assert.Equal(BodyFormat.None, document.Body.SourceFormat);
        Assert.Null(document.Body.Html);
        Assert.Equal("S/MIME encrypted message", document.Subject);
        Assert.NotEmpty(document.From);
        Assert.NotEmpty(document.To);
        Assert.NotNull(document.SentAtUtc);
        Assert.Empty(document.Attachments);
    }

    [Theory]
    [InlineData("application/pkcs7-mime; smime-type=enveloped-data; name=smime.p7m", true, false)]
    [InlineData("application/x-pkcs7-mime; smime-type=enveloped-data; name=smime.p7m", true, false)]
    [InlineData("application/pkcs7-mime; name=smime.p7m", true, false)]
    [InlineData("application/pkcs7-mime; name=message.P7M", true, false)]
    [InlineData("application/pkcs7-mime; smime-type=signed-data; name=smime.p7m", false, true)]
    [InlineData("application/x-pkcs7-mime; smime-type=signed-data; name=smime.p7m", false, true)]
    [Trait("Scenario", "email-viewing / 加密与签名邮件提示 / 按 MIME 类型识别，不创建 S/MIME 上下文")]
    public async Task Pkcs7Mime_IsClassifiedByContentType(string contentType, bool expectedEncrypted, bool expectedSigned)
    {
        using var temp = new TempDirectory();
        var path = temp.WriteMail(
            "pkcs7.eml",
            $"""
            From: a@example.com
            Subject: pkcs7
            MIME-Version: 1.0
            Content-Type: {contentType}
            Content-Transfer-Encoding: base64

            AAECAwQFBgcICQoLDA0ODw==
            """);

        using var parsed = await EmailParser.ParseFileAsync(path);

        Assert.Equal(expectedEncrypted, parsed.Document.IsEncrypted);
        Assert.Equal(expectedSigned, parsed.Document.IsSigned);
        Assert.Equal(BodyFormat.None, parsed.Document.Body.SourceFormat);
        Assert.Empty(parsed.Document.Attachments);
    }

    [Theory]
    [InlineData("application/pkcs7-mime; smime-type=certs-only; name=certs.p7c")]
    [InlineData("application/pkcs7-mime; smime-type=compressed-data; name=smime.p7z")]
    [InlineData("application/pkcs7-mime")]
    [Trait("Scenario", "email-viewing / 加密与签名邮件提示 / 既不是签名也不是加密的 pkcs7-mime 不加标志，作为普通附件列出")]
    public async Task OtherPkcs7Mime_IsNeitherSignedNorEncrypted(string contentType)
    {
        using var temp = new TempDirectory();
        var path = temp.WriteMail(
            "pkcs7-other.eml",
            $"""
            From: a@example.com
            Subject: pkcs7 other
            MIME-Version: 1.0
            Content-Type: {contentType}
            Content-Transfer-Encoding: base64

            AAECAwQFBgcICQoLDA0ODw==
            """);

        using var parsed = await EmailParser.ParseFileAsync(path);

        Assert.False(parsed.Document.IsEncrypted);
        Assert.False(parsed.Document.IsSigned);
        Assert.Single(parsed.Document.Attachments);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 加密与签名邮件提示 / 普通邮件没有签名和加密标志")]
    public async Task OrdinaryMail_HasNoSmimeFlags()
    {
        using var parsed = await MailAssert.ParseSampleAsync("attachments-pdf-xlsx-jpg.eml");

        Assert.False(parsed.Document.IsSigned);
        Assert.False(parsed.Document.IsEncrypted);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 加密与签名邮件提示 / 签名部分不是第一个成员时仍按 multipart/signed 处理")]
    public async Task MultipartSignedWithPgpSignature_IsSignedAndSignatureIsNotListed()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteMail(
            "pgp-signed.eml",
            """
            From: a@example.com
            Subject: pgp signed
            MIME-Version: 1.0
            Content-Type: multipart/signed; protocol="application/pgp-signature"; micalg=pgp-sha256; boundary="s"

            --s
            Content-Type: text/plain

            signed text
            --s
            Content-Type: application/pgp-signature; name="signature.asc"

            -----BEGIN PGP SIGNATURE-----
            abc
            -----END PGP SIGNATURE-----
            --s--
            """);

        using var parsed = await EmailParser.ParseFileAsync(path);

        Assert.True(parsed.Document.IsSigned);
        Assert.Equal("signed text", MailAssert.PlainTextOf(parsed.Document));
        Assert.Empty(parsed.Document.Attachments);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 加密与签名邮件提示 / 加密邮件（MSG，消息类 IPM.Note.SMIME）")]
    public async Task EncryptedMsg_IsMarkedEncryptedWithHeadersButNoBody()
    {
        using var parsed = await MailAssert.ParseSampleAsync("smime-msg-encrypted.msg");
        var document = parsed.Document;

        Assert.True(document.IsEncrypted);
        Assert.False(document.IsSigned);
        Assert.Equal(BodyFormat.None, document.Body.SourceFormat);
        Assert.Equal("S/MIME encrypted MSG", document.Subject);
        MailAssert.AddressesEqual(document.From, ("Alice Zhang", "alice.zhang@example.com"));
        MailAssert.AddressesEqual(document.To, ("Bob Li", "bob.li@example.org"));
        Assert.Empty(document.Attachments);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 加密与签名邮件提示 / 不透明签名的邮件（MSG，消息类 IPM.Note.SMIME，smime.p7m 是 signedData）")]
    public async Task OpaqueSignedMsg_IsMarkedSigned()
    {
        using var parsed = await MailAssert.ParseSampleAsync("smime-msg-signed-opaque.msg");

        Assert.True(parsed.Document.IsSigned);
        Assert.False(parsed.Document.IsEncrypted);
        Assert.Equal("S/MIME opaque-signed MSG", parsed.Document.Subject);
        // 被签名的正文和附件在 OpaqueSignedTests 里验证；smime.p7m 本身不是附件
        Assert.DoesNotContain(parsed.Document.Attachments, attachment => attachment.FileName == "smime.p7m");
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 加密与签名邮件提示 / 明文签名的邮件（MSG，消息类 IPM.Note.SMIME.MultipartSigned），正文取自 smime.p7m 里的邮件")]
    public async Task ClearSignedMsg_IsMarkedSignedAndShowsTheContentOfTheSignedMessage()
    {
        using var parsed = await MailAssert.ParseSampleAsync("smime-msg-signed-clear.msg");
        var document = parsed.Document;

        Assert.True(document.IsSigned);
        Assert.False(document.IsEncrypted);
        Assert.Equal("S/MIME clear-signed MSG", document.Subject);
        Assert.Equal("CLEAR-SIGNED-BODY: the text is readable without the signature.\n", MailAssert.PlainTextOf(document));
        Assert.Empty(document.Attachments);
    }

    [Theory]
    [InlineData("smime-msg-encrypted.msg")]
    [InlineData("smime-msg-signed-opaque.msg")]
    [InlineData("smime-msg-signed-clear.msg")]
    [Trait("Scenario", "email-viewing / 查看时不产生网络请求 / S/MIME 的 MSG 不走 MsgReader 的证书验证，原文件不被改动")]
    public async Task SmimeMsg_IsOpenedAsAPlainNote_AndTheFileOnDiskIsUntouched(string sampleName)
    {
        var path = SampleFiles.GetPath(sampleName);
        var before = await File.ReadAllBytesAsync(path);

        using var parsed = await EmailParser.ParseFileAsync(path);

        // MsgReader 看到的消息类已经被改成普通邮件，所以不会进入它自己的 S/MIME 处理（那条路径会对证书链做联网验证）
        Assert.Equal(MsgReader.Outlook.MessageType.Email, parsed.MsgMessage!.Type);
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
    }

    // ---- 签名里包着加密 ----

    [Fact]
    [Trait("Scenario", "email-viewing / 加密与签名邮件提示 / 签名里包着加密的邮件同时标记为已签名和已加密")]
    public async Task MultipartSignedWrappingEncryptedData_IsMarkedSignedAndEncrypted_WithoutListingTheCarrier()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteMail(
            "signed-encrypted.eml",
            """
            From: a@example.com
            To: b@example.com
            Subject: signed then encrypted
            MIME-Version: 1.0
            Content-Type: multipart/signed; protocol="application/pkcs7-signature"; micalg=sha-256; boundary="s"

            --s
            Content-Type: application/pkcs7-mime; smime-type=enveloped-data; name=smime.p7m
            Content-Transfer-Encoding: base64

            AAECAwQFBgcICQoLDA0ODw==
            --s
            Content-Type: application/pkcs7-signature; name=smime.p7s
            Content-Transfer-Encoding: base64

            AAECAwQFBgcICQoLDA0ODw==
            --s--
            """);

        using var parsed = await EmailParser.ParseFileAsync(path);
        var document = parsed.Document;

        Assert.True(document.IsSigned);
        Assert.True(document.IsEncrypted);
        Assert.Equal(BodyFormat.None, document.Body.SourceFormat);
        Assert.Null(document.Body.Html);
        Assert.Equal("signed then encrypted", document.Subject);
        Assert.Empty(document.Attachments);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 加密与签名邮件提示 / 明文签名的内容是普通邮件时仍然只标记为已签名")]
    public async Task MultipartSignedWithOrdinaryContent_IsStillOnlySigned()
    {
        using var parsed = await MailAssert.ParseSampleAsync("smime-signed.eml");

        Assert.True(parsed.Document.IsSigned);
        Assert.False(parsed.Document.IsEncrypted);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 加密与签名邮件提示 / 不透明签名里包着加密的邮件同时标记为已签名和已加密")]
    public async Task OpaqueSignedWrappingEncryptedData_IsMarkedSignedAndEncrypted_WithoutListingTheCarrier()
    {
        using var temp = new TempDirectory();
        var encrypted = TestCms.Pkcs7Entity("enveloped-data", [0, 1, 2, 3, 4, 5, 6, 7]);
        var path = TestCms.WriteSignedMail(temp, TestCms.SignEntity(encrypted));

        using var parsed = await EmailParser.ParseFileAsync(path);

        Assert.True(parsed.Document.IsSigned);
        Assert.True(parsed.Document.IsEncrypted);
        Assert.Equal(BodyFormat.None, parsed.Document.Body.SourceFormat);
        Assert.Empty(parsed.Document.Attachments);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 加密与签名邮件提示 / 明文签名里又包着不透明签名时取出里面的正文")]
    public async Task MultipartSignedWrappingOpaqueSignedData_ShowsTheInnerBody()
    {
        using var temp = new TempDirectory();
        var inner = TestCms.SignEntity("Content-Type: text/plain; charset=utf-8\n\nINNER-SIGNED-BODY\n");
        var path = temp.WriteMail(
            "signed-signed.eml",
            $"""
            From: a@example.com
            To: b@example.com
            Subject: double signed
            MIME-Version: 1.0
            Content-Type: multipart/signed; protocol="application/pkcs7-signature"; micalg=sha-256; boundary="s"

            --s
            {TestCms.Pkcs7Entity("signed-data", inner).ReplaceLineEndings("\n")}
            --s
            Content-Type: application/pkcs7-signature; name=smime.p7s
            Content-Transfer-Encoding: base64

            AAECAwQFBgcICQoLDA0ODw==
            --s--
            """);

        using var parsed = await EmailParser.ParseFileAsync(path);

        Assert.True(parsed.Document.IsSigned);
        Assert.False(parsed.Document.IsEncrypted);
        Assert.Contains("INNER-SIGNED-BODY", MailAssert.PlainTextOf(parsed.Document));
        Assert.Empty(parsed.Document.Attachments);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 加密与签名邮件提示 / MSG 明文签名里包着加密的内容同时标记为已签名和已加密")]
    public async Task MsgClearSignedWrappingEncryptedData_IsMarkedSignedAndEncrypted_WithoutListingTheCarrier()
    {
        // 用样本 MSG 做模板，把 smime.p7m 的内容换成“被签名的内容是加密数据”的 multipart/signed 邮件
        var inner = (
            "From: a@example.com\r\nTo: b@example.com\r\nSubject: signed then encrypted\r\nMIME-Version: 1.0\r\n"
            + "Content-Type: multipart/signed; protocol=\"application/pkcs7-signature\"; micalg=sha-256; boundary=\"s\"\r\n\r\n"
            + "--s\r\nContent-Type: application/pkcs7-mime; smime-type=enveloped-data; name=smime.p7m\r\nContent-Transfer-Encoding: base64\r\n\r\nAAECAwQFBgcICQoLDA0ODw==\r\n"
            + "--s\r\nContent-Type: application/pkcs7-signature; name=smime.p7s\r\nContent-Transfer-Encoding: base64\r\n\r\nAAECAwQFBgcICQoLDA0ODw==\r\n"
            + "--s--\r\n").Select(c => (byte)c).ToArray();

        var buffer = new MemoryStream(File.ReadAllBytes(SampleFiles.GetPath("smime-msg-signed-clear.msg")));
        using (var root = OpenMcdf.RootStorage.Open(buffer, OpenMcdf.StorageModeFlags.LeaveOpen))
        {
            using var carrier = root.OpenStorage("__attach_version1.0_#00000000").OpenStream("__substg1.0_37010102");
            carrier.SetLength(inner.Length);
            carrier.Position = 0;
            carrier.Write(inner);
        }

        using var temp = new TempDirectory();
        var path = temp.WriteBytes("signed-encrypted.msg", buffer.ToArray());

        using var parsed = await EmailParser.ParseFileAsync(path);

        Assert.True(parsed.Document.IsSigned);
        Assert.True(parsed.Document.IsEncrypted);
        Assert.Equal(BodyFormat.None, parsed.Document.Body.SourceFormat);
        Assert.DoesNotContain(parsed.Document.Attachments, attachment => string.Equals(attachment.FileName, "smime.p7m", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(parsed.Document.Attachments);
    }
}
