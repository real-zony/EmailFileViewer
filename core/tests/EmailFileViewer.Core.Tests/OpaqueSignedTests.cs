using System.Text;
using EmailFileViewer.Core.Models;

namespace EmailFileViewer.Core.Tests;

/// <summary>
/// 对应规格 email-viewing 中的“加密与签名邮件提示 / 签名邮件”：不透明签名的邮件要正常显示正文和附件。
/// Core 只用 <c>SignedCms.Decode</c> 取出被签名的内容，不验证签名、不建证书链。
/// </summary>
public class OpaqueSignedTests
{
    private const string TextEntity = "Content-Type: text/plain; charset=utf-8\n\nSIGNED-TEXT-BODY\n";

    [Fact]
    [Trait("Scenario", "email-viewing / 加密与签名邮件提示 / 签名邮件（EML 不透明签名：HTML 优先，PDF 附件照常列出）")]
    public async Task OpaqueSignedEml_ShowsTheBodyAndTheAttachmentInsideTheSignature()
    {
        using var parsed = await MailAssert.ParseSampleAsync("smime-signed-opaque.eml");
        var document = parsed.Document;

        Assert.True(document.IsSigned);
        Assert.False(document.IsEncrypted);
        Assert.Equal("S/MIME opaque-signed message", document.Subject);
        MailAssert.AddressesEqual(document.From, ("Alice Zhang", "alice.zhang@example.com"));
        MailAssert.AddressesEqual(document.To, ("Bob Li", "bob.li@example.org"));
        Assert.NotNull(document.SentAtUtc);

        Assert.Equal(BodyFormat.Html, document.Body.SourceFormat);
        Assert.Contains("OPAQUE-SIGNED-HTML", document.Body.Html);
        Assert.DoesNotContain("OPAQUE-SIGNED-PLAIN", document.Body.Html);

        var attachment = Assert.Single(document.Attachments);
        Assert.Equal("signed-report.pdf", attachment.FileName);
        Assert.Equal("application/pdf", attachment.MimeType);
        Assert.False(attachment.IsNestedEmail);

        var bytes = await MailAssert.ReadAttachmentAsync(parsed, attachment.Id);
        Assert.Equal(attachment.Size, bytes.Length);
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 加密与签名邮件提示 / 签名邮件（MSG 不透明签名：纯文本正文，文本附件照常列出）")]
    public async Task OpaqueSignedMsg_ShowsTheBodyAndTheAttachmentInsideTheSignature()
    {
        using var parsed = await MailAssert.ParseSampleAsync("smime-msg-signed-opaque.msg");
        var document = parsed.Document;

        Assert.True(document.IsSigned);
        Assert.False(document.IsEncrypted);
        Assert.Equal("S/MIME opaque-signed MSG", document.Subject);
        MailAssert.AddressesEqual(document.From, ("Alice Zhang", "alice.zhang@example.com"));

        Assert.Equal("OPAQUE-SIGNED-BODY: the content is wrapped inside the signature.\n", MailAssert.PlainTextOf(document));

        var attachment = Assert.Single(document.Attachments);
        Assert.Equal("opaque-note.txt", attachment.FileName);
        var bytes = await MailAssert.ReadAttachmentAsync(parsed, attachment.Id);
        Assert.Equal(attachment.Size, bytes.Length);
        Assert.Equal("This attachment is wrapped inside the opaque signature.\n", Encoding.UTF8.GetString(bytes).ReplaceLineEndings("\n"));
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 加密与签名邮件提示 / 签名邮件（smime.p7m 是载体，不当作附件）")]
    public async Task OpaqueSigned_TheCarrierIsNotListedAsAnAttachment()
    {
        foreach (var sample in new[] { "smime-signed-opaque.eml", "smime-msg-signed-opaque.msg" })
        {
            using var parsed = await MailAssert.ParseSampleAsync(sample);

            Assert.DoesNotContain(parsed.Document.Attachments, attachment => string.Equals(attachment.FileName, "smime.p7m", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 加密与签名邮件提示 / 签名邮件（被签名的内容和普通邮件一样经过 HTML 清理）")]
    public async Task SignedContent_IsSanitizedLikeAnyOtherBody()
    {
        using var temp = new TempDirectory();
        var cms = TestCms.SignEntity(
            """
            Content-Type: text/html; charset=utf-8

            <html><body onload="steal()"><p>SIGNED-HTML</p><script>alert(1)</script><img src="https://example.com/pixel.gif"></body></html>
            """);

        using var parsed = await EmailParser.ParseFileAsync(TestCms.WriteSignedMail(temp, cms));
        var document = parsed.Document;

        Assert.True(document.IsSigned);
        Assert.Equal(BodyFormat.Html, document.Body.SourceFormat);
        Assert.Contains("SIGNED-HTML", document.Body.Html);
        Assert.DoesNotContain("<script", document.Body.Html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onload", document.Body.Html, StringComparison.OrdinalIgnoreCase);
        Assert.True(document.HasRemoteContent);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 加密与签名邮件提示 / 签名邮件（被签名的内容里的 cid 图片内嵌，不列为附件）")]
    public async Task SignedContent_InlineImagesAreEmbeddedAndNotListed()
    {
        using var temp = new TempDirectory();
        var cms = TestCms.SignEntity(
            """
            Content-Type: multipart/related; boundary="r"

            --r
            Content-Type: text/html; charset=utf-8

            <p>SIGNED-WITH-IMAGE</p><img src="cid:pic@example.com">
            --r
            Content-Type: image/png
            Content-Transfer-Encoding: base64
            Content-ID: <pic@example.com>

            iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==
            --r--
            """);

        using var parsed = await EmailParser.ParseFileAsync(TestCms.WriteSignedMail(temp, cms));

        Assert.Contains("data:image/png;base64,", parsed.Document.Body.Html);
        Assert.DoesNotContain("cid:", parsed.Document.Body.Html);
        Assert.Empty(parsed.Document.Attachments);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 加密与签名邮件提示 / 签名邮件（签名与内容分离、没有内容时只有标志）")]
    public async Task DetachedSignedData_HasNoBodyButIsStillMarkedSigned()
    {
        using var temp = new TempDirectory();
        var cms = TestCms.SignedData(Encoding.UTF8.GetBytes(TextEntity), detached: true);

        using var parsed = await EmailParser.ParseFileAsync(TestCms.WriteSignedMail(temp, cms));

        Assert.True(parsed.Document.IsSigned);
        Assert.False(parsed.Document.IsEncrypted);
        Assert.Equal(BodyFormat.None, parsed.Document.Body.SourceFormat);
        Assert.Empty(parsed.Document.Attachments);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 加密与签名邮件提示 / 签名邮件（CMS 数据损坏时只有标志，不报错）")]
    public async Task CorruptCms_HasNoBodyButIsStillMarkedSigned()
    {
        using var temp = new TempDirectory();

        using var parsed = await EmailParser.ParseFileAsync(TestCms.WriteSignedMail(temp, [0x30, 0x03, 0x02, 0x01, 0x01]));

        Assert.True(parsed.Document.IsSigned);
        Assert.Equal(BodyFormat.None, parsed.Document.Body.SourceFormat);
        Assert.Empty(parsed.Document.Attachments);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 加密与签名邮件提示 / 签名邮件（一层套一层的签名逐层解开）")]
    public async Task SignedInsideSigned_IsUnwrappedLayerByLayer()
    {
        using var temp = new TempDirectory();
        var cms = TestCms.SignAgain(TestCms.SignEntity(TextEntity));

        using var parsed = await EmailParser.ParseFileAsync(TestCms.WriteSignedMail(temp, cms));

        Assert.True(parsed.Document.IsSigned);
        Assert.Equal("SIGNED-TEXT-BODY\n", MailAssert.PlainTextOf(parsed.Document));
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(40, false)]
    [Trait("Scenario", "email-viewing / 加密与签名邮件提示 / 签名邮件（嵌套层数有上限，恶意构造的多层签名不会撑爆递归）")]
    public async Task NestedSignatures_AreLimitedToThreeLayers(int layers, bool expectBody)
    {
        using var temp = new TempDirectory();
        var cms = TestCms.SignEntity(TextEntity);
        for (var i = 1; i < layers; i++)
        {
            cms = TestCms.SignAgain(cms);
        }

        using var parsed = await EmailParser.ParseFileAsync(TestCms.WriteSignedMail(temp, cms));

        Assert.True(parsed.Document.IsSigned);
        Assert.Equal(expectBody ? BodyFormat.PlainText : BodyFormat.None, parsed.Document.Body.SourceFormat);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 加密与签名邮件提示 / 先加密再签名：标为已签名也已加密，不尝试解出正文")]
    public async Task SignedEnvelopeOfEncryptedData_IsMarkedSignedAndEncrypted()
    {
        using var temp = new TempDirectory();
        var cms = TestCms.SignEntity(TestCms.Pkcs7Entity("enveloped-data", [0x30, 0x03, 0x02, 0x01, 0x00]));

        using var parsed = await EmailParser.ParseFileAsync(TestCms.WriteSignedMail(temp, cms));

        Assert.True(parsed.Document.IsSigned);
        Assert.True(parsed.Document.IsEncrypted);
        Assert.Equal(BodyFormat.None, parsed.Document.Body.SourceFormat);
        Assert.Empty(parsed.Document.Attachments);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 查看时不产生网络请求 / 不透明签名的邮件只解码，不验证")]
    public async Task OpaqueSignedSamples_AreParsedWithoutTouchingTheFiles()
    {
        foreach (var sample in new[] { "smime-signed-opaque.eml", "smime-msg-signed-opaque.msg" })
        {
            var path = SampleFiles.GetPath(sample);
            var before = await File.ReadAllBytesAsync(path);

            using var parsed = await EmailParser.ParseFileAsync(path);

            Assert.True(parsed.Document.IsSigned);
            Assert.Equal(before, await File.ReadAllBytesAsync(path));
        }
    }
}
