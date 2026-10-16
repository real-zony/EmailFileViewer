using System.Text;
using EmailFileViewer.Core.Parsing;
using MimeKit;

namespace EmailFileViewer.Core.Tests;

/// <summary>
/// 解析结果对象本身：smoke 命令用到的原始附件数、释放，以及代码页注册。
/// </summary>
public class ParsedEmailTests
{
    [Theory]
    [InlineData("simple-multipart.eml", "this is a simple multipart message", 1)]
    [InlineData("EmailWithMultilineSubject.eml", "One Two Three Four Five Six Seven Eight Nine Ten Eleven Twelve Thirteen Fourteen Fifteen", 0)]
    [InlineData("HtmlSampleEmailWithAttachment.msg", "HtmlSampleEmailWithAttachment", 1)]
    [InlineData("EmailWithMsgAttachment.msg", "FW: Msg Attachment Test", 1)]
    [InlineData("RtfSampleEmail.msg", "RtfSampleEmail", 0)]
    public async Task ParseFileAsync_Samples_ReturnSubjectAndRawAttachmentCount(string sampleName, string expectedSubject, int expectedAttachmentCount)
    {
        using var parsed = await MailAssert.ParseSampleAsync(sampleName);

        Assert.Equal(expectedSubject, parsed.Document.Subject);
        Assert.Equal(expectedAttachmentCount, parsed.Document.Attachments.Count);
    }

    [Theory]
    [InlineData("headers-normal.eml", true)]
    [InlineData("body-rtf-only.msg", false)]
    public async Task ParseFileAsync_KeepsUnderlyingObjectsForLaterAttachmentAccess(string sampleName, bool isMime)
    {
        using var parsed = await MailAssert.ParseSampleAsync(sampleName);

        Assert.Equal(isMime, parsed.MimeMessage is not null);
        Assert.Equal(!isMime, parsed.MsgMessage is not null);
    }

    [Fact]
    public async Task MimeAttachmentContent_IsReadableAfterParseReturned()
    {
        // 解析方法返回时文件流已经关闭，保存附件时才读内容，所以内容必须已经在内存里
        using var parsed = await MailAssert.ParseSampleAsync("attachments-pdf-xlsx-jpg.eml");

        var photo = Assert.Single(parsed.MimeMessage!.Attachments.OfType<MimePart>(), part => part.FileName == "photo.jpg");
        using var content = new MemoryStream();
        Assert.NotNull(photo.Content);
        await photo.Content.DecodeToAsync(content);

        Assert.Equal(await File.ReadAllBytesAsync(SampleFiles.GetPath("assets/photo.jpg")), content.ToArray());
    }

    [Fact]
    public async Task MsgAttachmentData_IsReadableAfterParseReturned()
    {
        using var parsed = await MailAssert.ParseSampleAsync("HtmlSampleEmailWithAttachment.msg");

        var attachment = Assert.IsType<MsgReader.Outlook.Storage.Attachment>(Assert.Single(parsed.MsgMessage!.Attachments));
        Assert.Equal("SampleAttachment.pdf", attachment.FileName);
        Assert.Equal(169480, attachment.Data.Length);
    }

    [Fact]
    public async Task Dispose_CalledTwice_DoesNotThrow()
    {
        var parsed = await MailAssert.ParseSampleAsync("body-rtf-only.msg");

        parsed.Dispose();
        parsed.Dispose();
    }

    [Fact]
    public async Task ParsedMsg_DoesNotKeepSourceFileOpen()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.DirectoryPath, "copy.msg");
        File.Copy(SampleFiles.GetPath("body-rtf-only.msg"), path);

        using var parsed = await EmailParser.ParseFileAsync(path);

        // 文档还开着时，用户依然可以删除原文件（Windows 上被占用的文件删不掉）
        File.Delete(path);
        Assert.False(File.Exists(path));
        Assert.Equal("RTF-only body sample", parsed.Document.Subject);
    }

    [Fact]
    public void CodePageRegistration_EnsureRegistered_MakesLegacyEncodingsAvailable()
    {
        CodePageRegistration.EnsureRegistered();

        Assert.Equal(936, Encoding.GetEncoding("gbk").CodePage);
        Assert.Equal(950, Encoding.GetEncoding("big5").CodePage);
        Assert.Equal(932, Encoding.GetEncoding("shift_jis").CodePage);
    }
}
