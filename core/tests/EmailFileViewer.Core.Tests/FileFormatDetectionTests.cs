using System.Runtime.Versioning;
using OpenMcdf;

namespace EmailFileViewer.Core.Tests;

/// <summary>
/// 对应规格 email-opening 中“按内容识别文件格式”和“无法识别或损坏的文件”，以及“一次打开多个文件”。
/// </summary>
public class FileFormatDetectionTests
{
    [Fact]
    [Trait("Scenario", "email-opening / 按内容识别文件格式 / 扩展名与内容不符")]
    public async Task MsgContentInEmlFile_ParsedAsMsg()
    {
        using var parsed = await MailAssert.ParseSampleAsync("format-msg-disguised-as-eml.eml");

        Assert.NotNull(parsed.MsgMessage);
        Assert.Null(parsed.MimeMessage);
        Assert.Equal("RTF-only body sample", parsed.Document.Subject);
    }

    [Fact]
    [Trait("Scenario", "email-opening / 按内容识别文件格式 / 扩展名与内容不符")]
    public async Task EmlContentInMsgFile_ParsedAsMime()
    {
        using var parsed = await MailAssert.ParseSampleAsync("format-eml-disguised-as-msg.msg");

        Assert.NotNull(parsed.MimeMessage);
        Assert.Null(parsed.MsgMessage);
        Assert.Equal("Plain text only: paragraphs and blank lines", parsed.Document.Subject);
    }

    [Theory]
    [InlineData("invalid/not-an-email.png")]
    [InlineData("invalid/png-disguised-as-eml.eml")]
    [Trait("Scenario", "email-opening / 无法识别或损坏的文件 / 打开一张图片")]
    public async Task PngImage_ReportsUnsupportedFormat(string sampleName)
    {
        await MailAssert.ParseFailsAsync(SampleFiles.GetPath(sampleName), EmailParseErrorKind.UnsupportedFormat);
    }

    [Fact]
    [Trait("Scenario", "email-opening / 无法识别或损坏的文件 / 打开一张图片（空文件）")]
    public async Task EmptyFile_ReportsUnsupportedFormat()
    {
        await MailAssert.ParseFailsAsync(SampleFiles.GetPath("invalid/empty.eml"), EmailParseErrorKind.UnsupportedFormat);
    }

    [Fact]
    [Trait("Scenario", "email-opening / 无法识别或损坏的文件 / 打开一张图片（只有空白）")]
    public async Task WhitespaceOnlyFile_ReportsUnsupportedFormat()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteMail("blank.eml", "  \n\n\t \n");

        await MailAssert.ParseFailsAsync(path, EmailParseErrorKind.UnsupportedFormat);
    }

    [Fact]
    [Trait("Scenario", "email-opening / 无法识别或损坏的文件 / 打开一张图片（普通文本文件）")]
    public async Task PlainTextFile_ReportsUnsupportedFormat()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteMail("notes.eml", "Shopping list\n- milk\n- eggs\n");

        await MailAssert.ParseFailsAsync(path, EmailParseErrorKind.UnsupportedFormat);
    }

    [Fact]
    [Trait("Scenario", "email-opening / 无法识别或损坏的文件 / 打开一张图片（像邮件头但没有任何常见邮件头）")]
    public async Task HeaderLikeTextWithoutCommonHeader_ReportsUnsupportedFormat()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteMail("config.eml", "X-Setting: on\nX-Other: off\n\nbody\n");

        await MailAssert.ParseFailsAsync(path, EmailParseErrorKind.UnsupportedFormat);
    }

    [Fact]
    [Trait("Scenario", "email-opening / 无法识别或损坏的文件 / 打开一张图片（其他 OLE 文档）")]
    public async Task OleDocumentThatIsNotOutlookMail_ReportsUnsupportedFormat()
    {
        // Word、Excel 等老格式也是 OLE 复合文档，文件头和 MSG 一样
        using var stream = new MemoryStream();
        using (var root = RootStorage.Create(stream, OpenMcdf.Version.V3, StorageModeFlags.LeaveOpen))
        {
            using var wordStream = root.CreateStream("WordDocument");
            wordStream.Write(new byte[100]);
        }

        using var temp = new TempDirectory();
        var path = temp.WriteBytes("document.msg", stream.ToArray());

        await MailAssert.ParseFailsAsync(path, EmailParseErrorKind.UnsupportedFormat);
    }

    [Fact]
    [Trait("Scenario", "email-opening / 无法识别或损坏的文件 / 解析过程中崩溃")]
    public async Task TruncatedMsg_ReportsCorrupted()
    {
        var exception = await MailAssert.ParseFailsAsync(SampleFiles.GetPath("invalid/truncated.msg"), EmailParseErrorKind.Corrupted);

        Assert.NotNull(exception.InnerException);
    }

    [Fact]
    [Trait("Scenario", "email-opening / 无法识别或损坏的文件 / 解析过程中崩溃（MimeKit 能容忍的截断）")]
    public async Task TruncatedEml_NeverCrashesWithUnexpectedException()
    {
        // MimeKit 能解出被截断邮件的前半部分，这是可以接受的；要求只是不抛出 EmailParseException 以外的异常
        try
        {
            using var parsed = await MailAssert.ParseSampleAsync("invalid/truncated-half.eml");
            Assert.Equal("Attachments: pdf, xlsx and jpg", parsed.Document.Subject);
        }
        catch (EmailParseException)
        {
        }
    }

    [Fact]
    [Trait("Scenario", "email-opening / 无法识别或损坏的文件 / 文件无法读取（不存在）")]
    public async Task MissingFile_ReportsUnreadable()
    {
        using var temp = new TempDirectory();

        await MailAssert.ParseFailsAsync(Path.Combine(temp.DirectoryPath, "missing.eml"), EmailParseErrorKind.Unreadable);
    }

    [Fact]
    [Trait("Scenario", "email-opening / 无法识别或损坏的文件 / 文件无法读取（路径是目录）")]
    public async Task Directory_ReportsUnreadable()
    {
        using var temp = new TempDirectory();

        await MailAssert.ParseFailsAsync(temp.DirectoryPath, EmailParseErrorKind.Unreadable);
    }

    [UnixOnlyFact]
    [UnsupportedOSPlatform("windows")]
    [Trait("Scenario", "email-opening / 无法识别或损坏的文件 / 文件无法读取（无读权限）")]
    public async Task FileWithoutReadPermission_ReportsUnreadable()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteMail("locked.eml", "From: a@example.com\nSubject: locked\n\nbody\n");
        File.SetUnixFileMode(path, UnixFileMode.None);

        var exception = await MailAssert.ParseFailsAsync(path, EmailParseErrorKind.Unreadable);

        Assert.IsType<UnauthorizedAccessException>(exception.InnerException);
    }

    [Fact]
    [Trait("Scenario", "email-opening / 无法识别或损坏的文件 / 取消解析")]
    public async Task CanceledToken_ThrowsOperationCanceled()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => EmailParser.ParseFileAsync(SampleFiles.GetPath("headers-normal.eml"), cancellation.Token));
    }

    [Fact]
    [Trait("Scenario", "email-opening / 一次打开多个文件 / 多个文件中有一个损坏")]
    public async Task OneBadFileAmongSeveral_OtherFilesStillParse()
    {
        string[] samples = ["headers-normal.eml", "invalid/truncated.msg", "body-rtf-only.msg", "invalid/not-an-email.png", "simple-multipart.eml"];

        var subjects = new List<string?>();
        var failures = new List<EmailParseErrorKind>();
        foreach (var sample in samples)
        {
            try
            {
                using var parsed = await MailAssert.ParseSampleAsync(sample);
                subjects.Add(parsed.Document.Subject);
            }
            catch (EmailParseException ex)
            {
                failures.Add(ex.Kind);
            }
        }

        Assert.Equal(["Quarterly planning meeting", "RTF-only body sample", "this is a simple multipart message"], subjects);
        Assert.Equal([EmailParseErrorKind.Corrupted, EmailParseErrorKind.UnsupportedFormat], failures);
    }

    [Fact]
    [Trait("Scenario", "格式识别 / 只有一个常见邮件头的最小邮件仍然可以打开")]
    public async Task MinimalMailWithSingleCommonHeader_Parses()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteMail("minimal.eml", "Subject: just a subject\n\nbody\n");

        using var parsed = await EmailParser.ParseFileAsync(path);

        Assert.Equal("just a subject", parsed.Document.Subject);
    }

    [Fact]
    [Trait("Scenario", "格式识别 / Thunderbird 另存的 EML 开头有 mbox 起始行，仍然可以打开")]
    public async Task EmlWithMboxEnvelopeLine_Parses()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteMail(
            "thunderbird.eml",
            "From - Wed Jul 22 01:02:29 2015\nX-Mozilla-Status: 0001\nFrom: Alice <a@example.com>\nSubject: hello thunderbird\n\nbody\n");

        using var parsed = await EmailParser.ParseFileAsync(path);

        Assert.Equal("hello thunderbird", parsed.Document.Subject);
        MailAssert.AddressesEqual(parsed.Document.From, ("Alice", "a@example.com"));
    }
}
