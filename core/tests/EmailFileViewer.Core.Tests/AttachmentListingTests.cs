using System.IO.Compression;

namespace EmailFileViewer.Core.Tests;

/// <summary>
/// 对应规格 email-attachments 中的“列出附件”，以及“保存单个附件”需要的原始字节（写出的内容与原附件一致）。
/// </summary>
public class AttachmentListingTests
{
    [Fact]
    [Trait("Scenario", "email-attachments / 列出附件 / 带两个附件的邮件（pdf、xlsx、jpg）")]
    public async Task PdfXlsxJpg_AreListedWithNameTypeAndDecodedSize()
    {
        using var parsed = await MailAssert.ParseSampleAsync("attachments-pdf-xlsx-jpg.eml");
        var attachments = parsed.Document.Attachments;

        Assert.Equal(["report.pdf", "budget.xlsx", "photo.jpg"], attachments.Select(a => a.FileName));
        Assert.Equal(["application/pdf", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "image/jpeg"], attachments.Select(a => a.MimeType));
        Assert.All(attachments, attachment => Assert.False(attachment.IsNestedEmail));

        // 大小是解码后的字节数，必须和写出的内容长度一致
        foreach (var attachment in attachments)
        {
            var bytes = await MailAssert.ReadAttachmentAsync(parsed, attachment.Id);
            Assert.Equal(attachment.Size, bytes.Length);
            Assert.True(bytes.Length > 0);
        }
    }

    [Fact]
    [Trait("Scenario", "email-attachments / 保存单个附件 / 写出的字节与原附件完全一致")]
    public async Task WrittenBytes_AreIdenticalToOriginalAttachment()
    {
        using var parsed = await MailAssert.ParseSampleAsync("attachments-pdf-xlsx-jpg.eml");

        var photo = await MailAssert.ReadAttachmentAsync(parsed, MailAssert.AttachmentNamed(parsed.Document, "photo.jpg").Id);
        Assert.Equal(await File.ReadAllBytesAsync(SampleFiles.GetPath("assets/photo.jpg")), photo);

        var pdf = await MailAssert.ReadAttachmentAsync(parsed, MailAssert.AttachmentNamed(parsed.Document, "report.pdf").Id);
        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(pdf, 0, 5));

        var xlsx = await MailAssert.ReadAttachmentAsync(parsed, MailAssert.AttachmentNamed(parsed.Document, "budget.xlsx").Id);
        using var zip = new ZipArchive(new MemoryStream(xlsx));
        Assert.Contains(zip.Entries, entry => entry.FullName == "[Content_Types].xml");
    }

    [Fact]
    [Trait("Scenario", "email-attachments / 列出附件 / 附件 Id 稳定")]
    public async Task AttachmentIds_AreStableAcrossParsesAndUnique()
    {
        using var first = await MailAssert.ParseSampleAsync("attachments-pdf-xlsx-jpg.eml");
        using var second = await MailAssert.ParseSampleAsync("attachments-pdf-xlsx-jpg.eml");

        var firstIds = first.Document.Attachments.Select(a => a.Id).ToArray();
        Assert.Equal(firstIds, second.Document.Attachments.Select(a => a.Id));
        Assert.Equal(firstIds.Length, firstIds.Distinct().Count());
        Assert.All(firstIds, id => Assert.False(string.IsNullOrWhiteSpace(id)));
    }

    [Fact]
    [Trait("Scenario", "email-attachments / 列出附件 / 内嵌图片不重复列出")]
    public async Task CidInlineImages_AreNotListed_OnlyTheDocxIs()
    {
        using var parsed = await MailAssert.ParseSampleAsync("cid-inline-image.eml");

        var attachment = Assert.Single(parsed.Document.Attachments);
        Assert.Equal("quarterly-report.docx", attachment.FileName);

        // 两张内嵌图片（一张有 Content-Disposition: inline，一张只有 Content-ID）都换成了 data: URI
        var html = parsed.Document.Body.Html!;
        Assert.DoesNotContain("cid:", html);
        Assert.Equal(2, CountOccurrences(html, "src=\"data:image/png;base64,"));
    }

    [Fact]
    [Trait("Scenario", "email-attachments / 列出附件 / 没有被正文引用的内嵌图片仍然列出（正文完全没有引用时两张都列出）")]
    public async Task InlineImageNotReferencedByBody_IsStillListed()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteMail("unreferenced-inline.eml", RelatedMail(
            "<html><body><p>No images here</p></body></html>",
            "<referenced@example>",
            "<unreferenced@example>"));

        using var parsed = await EmailParser.ParseFileAsync(path);

        Assert.Equal(["referenced.png", "unreferenced.png"], parsed.Document.Attachments.Select(a => a.FileName));
    }

    [Fact]
    [Trait("Scenario", "email-attachments / 列出附件 / 正文用多种方式引用同一张内嵌图片，只内嵌并隐藏")]
    public async Task CidReferencedFromImgBackgroundAndCss_AreReplacedAndHidden()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteMail("cid-everywhere.eml", RelatedMail(
            """
            <html><head><style>.hero { background-image: url('cid:referenced@example'); }</style></head>
            <body background="cid:referenced@example">
            <img src="cid:referenced@example"><img src="CID:%3Creferenced%40example%3E" alt="case and percent-encoding">
            <div style="background:url(cid:referenced@example) no-repeat" class="hero"></div>
            </body></html>
            """,
            "<referenced@example>",
            "<unreferenced@example>"));

        using var parsed = await EmailParser.ParseFileAsync(path);
        var html = parsed.Document.Body.Html!;

        Assert.DoesNotContain("cid:", html, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(5, CountOccurrences(html, "data:image/png;base64,"));
        Assert.Equal(["unreferenced.png"], parsed.Document.Attachments.Select(a => a.FileName));
        Assert.False(parsed.Document.HasRemoteContent);
    }

    [Fact]
    [Trait("Scenario", "email-attachments / 列出附件 / 重名附件各自列出")]
    public async Task DuplicateNames_AreListedSeparatelyWithDifferentContent()
    {
        using var parsed = await MailAssert.ParseSampleAsync("attachments-duplicate-names.eml");
        var attachments = parsed.Document.Attachments;

        Assert.Equal(2, attachments.Count);
        Assert.All(attachments, attachment => Assert.Equal("report.pdf", attachment.FileName));
        Assert.NotEqual(attachments[0].Id, attachments[1].Id);

        var first = System.Text.Encoding.Latin1.GetString(await MailAssert.ReadAttachmentAsync(parsed, attachments[0].Id));
        var second = System.Text.Encoding.Latin1.GetString(await MailAssert.ReadAttachmentAsync(parsed, attachments[1].Id));
        Assert.Contains("Report A", first);
        Assert.Contains("Report B", second);
    }

    [Fact]
    [Trait("Scenario", "email-attachments / 列出附件 / 文件名原样列出，清理发生在保存时")]
    public async Task UnsafeNames_AreListedAsReceived()
    {
        using var parsed = await MailAssert.ParseSampleAsync("attachments-unsafe-names.eml");

        Assert.Equal(
            ["../../.bashrc", "..\\..\\Windows\\evil.txt", "/tmp/absolute-path.txt", "CON.txt", "NUL", "a:b*c?.txt", "x<y>z|w.txt"],
            parsed.Document.Attachments.Select(a => a.FileName));
    }

    [Fact]
    [Trait("Scenario", "email-attachments / 列出附件 / 可执行附件也列出")]
    public async Task ExecutableAttachments_AreListed()
    {
        using var parsed = await MailAssert.ParseSampleAsync("attachments-executables.eml");

        Assert.Equal(
            ["installer.exe", "run-me.bat", "setup.ps1", "install.sh", "launcher.command", "Tool.app"],
            parsed.Document.Attachments.Select(a => a.FileName));
    }

    [Theory]
    [InlineData("simple-multipart.eml", 1)]
    [InlineData("body-plain-only.eml", 0)]
    [InlineData("smime-signed.eml", 0)]
    [InlineData("smime-encrypted.eml", 0)]
    [Trait("Scenario", "email-attachments / 列出附件 / 附件数量")]
    public async Task AttachmentCount_MatchesSample(string sampleName, int expectedCount)
    {
        using var parsed = await MailAssert.ParseSampleAsync(sampleName);

        Assert.Equal(expectedCount, parsed.Document.Attachments.Count);
    }

    [Fact]
    [Trait("Scenario", "email-attachments / 列出附件 / 上游样本：4 个文本附件加 2 个 message/rfc822，cid 图片不列出")]
    public async Task TextAttachmentsAndRfc822Parts_AreListed()
    {
        using var parsed = await MailAssert.ParseSampleAsync("TestWithAttachmentsAndInlines.eml");
        var attachments = parsed.Document.Attachments;

        Assert.Equal(6, attachments.Count);
        Assert.Equal(4, attachments.Count(a => a.MimeType == "text/plain"));
        Assert.Equal(2, attachments.Count(a => a.MimeType == "message/rfc822"));
        Assert.DoesNotContain(attachments, a => a.MimeType == "image/png");
    }

    [Fact]
    [Trait("Scenario", "email-attachments / 列出附件 / 签名邮件不列出签名文件")]
    public async Task SignedMail_SignaturePartIsNotListed()
    {
        using var parsed = await MailAssert.ParseSampleAsync("smime-signed-thunderbird.eml");

        var attachment = Assert.Single(parsed.Document.Attachments);
        Assert.Equal("signed.jpg", attachment.FileName);
    }

    [Fact]
    [Trait("Scenario", "email-attachments / 列出附件 / MSG 的普通附件")]
    public async Task MsgAttachment_IsListedWithNameTypeAndSize()
    {
        using var parsed = await MailAssert.ParseSampleAsync("HtmlSampleEmailWithAttachment.msg");

        var attachment = Assert.Single(parsed.Document.Attachments);
        Assert.Equal("SampleAttachment.pdf", attachment.FileName);
        Assert.Equal("application/pdf", attachment.MimeType);
        Assert.Equal(169480, attachment.Size);
        Assert.False(attachment.IsNestedEmail);

        var bytes = await MailAssert.ReadAttachmentAsync(parsed, attachment.Id);
        Assert.Equal(169480, bytes.Length);
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));
    }

    [Fact]
    [Trait("Scenario", "email-attachments / 列出附件 / MSG 中按文件名引用的隐藏内嵌图片不列出，并换成 data: URI")]
    public async Task MsgHiddenInlineImageReferencedByFileName_IsNotListedAndIsEmbedded()
    {
        using var parsed = await MailAssert.ParseSampleAsync("EmailWithAttachments.msg");

        Assert.Equal(["peterpan.jpg"], parsed.Document.Attachments.Select(a => a.FileName));
        var html = parsed.Document.Body.Html!;
        Assert.DoesNotContain("tinkerbell.jpg", html);
        Assert.Contains("src=\"data:image/jpeg;base64,/9j/", html);
    }

    [Fact]
    [Trait("Scenario", "email-attachments / 列出附件 / 附件 Id 不存在")]
    public async Task UnknownAttachmentId_Throws()
    {
        using var parsed = await MailAssert.ParseSampleAsync("attachments-pdf-xlsx-jpg.eml");

        var exception = await Assert.ThrowsAsync<AttachmentNotFoundException>(() => MailAssert.ReadAttachmentAsync(parsed, "no-such-id"));
        Assert.Equal("no-such-id", exception.AttachmentId);
    }

    [Fact]
    [Trait("Scenario", "email-attachments / 列出附件 / 邮件已释放后不能再读附件")]
    public async Task AfterDispose_WritingAnAttachmentThrows()
    {
        var parsed = await MailAssert.ParseSampleAsync("attachments-pdf-xlsx-jpg.eml");
        var id = parsed.Document.Attachments[0].Id;
        parsed.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => MailAssert.ReadAttachmentAsync(parsed, id));
    }

    [Fact]
    [Trait("Scenario", "email-attachments / 列出附件 / 取消写出")]
    public async Task CanceledToken_WhileWritingAttachment_Throws()
    {
        using var parsed = await MailAssert.ParseSampleAsync("attachments-pdf-xlsx-jpg.eml");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => parsed.WriteAttachmentAsync(parsed.Document.Attachments[0].Id, new MemoryStream(), cancellation.Token));
    }

    // 一封 multipart/related 邮件：HTML 正文加两张 PNG（referenced.png 和 unreferenced.png）
    private static string RelatedMail(string html, string referencedContentId, string unreferencedContentId)
    {
        const string Png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";
        return $$"""
            From: a@example.com
            Subject: related
            MIME-Version: 1.0
            Content-Type: multipart/related; boundary="rel"

            --rel
            Content-Type: text/html; charset=utf-8

            {{html}}
            --rel
            Content-Type: image/png; name="referenced.png"
            Content-Disposition: inline; filename="referenced.png"
            Content-ID: {{referencedContentId}}
            Content-Transfer-Encoding: base64

            {{Png}}
            --rel
            Content-Type: image/png; name="unreferenced.png"
            Content-Disposition: inline; filename="unreferenced.png"
            Content-ID: {{unreferencedContentId}}
            Content-Transfer-Encoding: base64

            {{Png}}
            --rel--
            """;
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
