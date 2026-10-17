using System.Diagnostics;
using System.Text;
using EmailFileViewer.Core.Models;
using EmailFileViewer.Core.Parsing;
using OpenMcdf;

namespace EmailFileViewer.Core.Tests;

/// <summary>
/// 整分支审查 I6：层层嵌套的 message/rfc822 会让递归序列化栈溢出，整个进程直接退出。
/// 病态输入在测试里现场生成；如果回归了，栈溢出会让测试进程崩溃，而不是单个用例失败。
/// </summary>
public class DeepNestingTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);

    // depth 层 message/rfc822，最里面是一封纯文本邮件
    private static string NestedRfc822(int depth)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < depth; i++)
        {
            builder.Append($"From: a@b.c\r\nSubject: s{i}\r\nMIME-Version: 1.0\r\nContent-Type: message/rfc822\r\n\r\n");
        }

        builder.Append("From: a@b.c\r\nSubject: last\r\nContent-Type: text/plain\r\n\r\nhello\r\n");
        return builder.ToString();
    }

    [Fact]
    public void ParserOptions_LimitTheMimeDepth()
    {
        Assert.Equal(MimeParserOptions.MaxMimeDepth, MimeParserOptions.Create().MaxMimeDepth);
        Assert.Equal(64, MimeParserOptions.MaxMimeDepth);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(1_000)]
    [InlineData(20_000)]
    [Trait("Scenario", "email-opening / 无法识别或损坏的文件 / 层层嵌套的 message/rfc822 不会让进程崩溃")]
    public async Task DeeplyNestedRfc822_IsParsedWithoutCrashing(int depth)
    {
        using var temp = new TempDirectory();
        var path = temp.WriteMail("deep.eml", NestedRfc822(depth));
        var stopwatch = Stopwatch.StartNew();

        using var parsed = await EmailParser.ParseFileAsync(path);

        Assert.True(stopwatch.Elapsed < Limit, $"耗时 {stopwatch.Elapsed.TotalSeconds:F1} 秒");
        var attachment = Assert.Single(parsed.Document.Attachments);
        Assert.True(attachment.IsNestedEmail);
        Assert.Equal("message/rfc822", attachment.MimeType);
        Assert.True(attachment.Size > 0);

        // 能写出来，字节数与列表里的大小一致
        var bytes = await MailAssert.ReadAttachmentAsync(parsed, attachment.Id);
        Assert.Equal(attachment.Size, bytes.Length);
    }

    [Fact]
    [Trait("Scenario", "email-attachments / 打开嵌套邮件 / 超过解析层数上限的嵌套邮件逐层打开仍然可以走到最里面")]
    public async Task NestingBeyondTheParserLimit_CanStillBeOpenedLevelByLevel()
    {
        const int depth = MimeParserOptions.MaxMimeDepth * 2 + 20;
        using var temp = new TempDirectory();
        var path = temp.WriteMail("deep.eml", NestedRfc822(depth));

        var current = await EmailParser.ParseFileAsync(path);
        try
        {
            for (var level = 1; level < depth; level++)
            {
                var attachment = Assert.Single(current.Document.Attachments);
                Assert.True(attachment.IsNestedEmail, $"第 {level} 层应该是嵌套邮件");
                var next = await current.OpenNestedEmailAsync(attachment.Id);
                current.Dispose();
                current = next;
                Assert.Equal($"s{level}", current.Document.Subject);
            }

            Assert.Equal("last", (await current.OpenNestedEmailAsync(Assert.Single(current.Document.Attachments).Id)).Document.Subject);
        }
        finally
        {
            current.Dispose();
        }
    }

    [Theory]
    [InlineData(2_000)]
    [InlineData(20_000)]
    [Trait("Scenario", "email-opening / 无法识别或损坏的文件 / 层层嵌套的 multipart 不会让进程崩溃")]
    public async Task DeeplyNestedMultipart_IsParsedWithoutCrashing(int depth)
    {
        var builder = new StringBuilder("From: a@b.c\r\nSubject: s\r\nMIME-Version: 1.0\r\n");
        for (var i = 0; i < depth; i++)
        {
            builder.Append($"Content-Type: multipart/mixed; boundary=b{i}\r\n\r\n--b{i}\r\n");
        }

        builder.Append("Content-Type: text/plain\r\n\r\nhello\r\n");
        for (var i = depth - 1; i >= 0; i--)
        {
            builder.Append($"--b{i}--\r\n");
        }

        using var temp = new TempDirectory();
        var path = temp.WriteMail("deep-multipart.eml", builder.ToString());
        var stopwatch = Stopwatch.StartNew();

        using var parsed = await EmailParser.ParseFileAsync(path);

        Assert.True(stopwatch.Elapsed < Limit, $"耗时 {stopwatch.Elapsed.TotalSeconds:F1} 秒");
        Assert.Equal("s", parsed.Document.Subject);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 加密与签名邮件提示 / 签名里层层嵌套的邮件同样受层数限制")]
    public async Task DeeplyNestedRfc822_InsideAnOpaqueSignature_IsParsedWithoutCrashing()
    {
        // 被签名的内容是一封直接包着 5000 层嵌套的 multipart/mixed 邮件
        var entity = "Content-Type: multipart/mixed; boundary=outer\r\n\r\n--outer\r\nContent-Type: text/plain\r\n\r\nSIGNED-BODY\r\n--outer\r\n"
            + "Content-Type: message/rfc822\r\n\r\n" + NestedRfc822(5_000) + "\r\n--outer--\r\n";
        using var temp = new TempDirectory();
        var path = TestCms.WriteSignedMail(temp, TestCms.SignEntity(entity));
        var stopwatch = Stopwatch.StartNew();

        using var parsed = await EmailParser.ParseFileAsync(path);

        Assert.True(stopwatch.Elapsed < Limit, $"耗时 {stopwatch.Elapsed.TotalSeconds:F1} 秒");
        Assert.True(parsed.Document.IsSigned);
        Assert.Contains("SIGNED-BODY", MailAssert.PlainTextOf(parsed.Document));
        var attachment = Assert.Single(parsed.Document.Attachments);
        Assert.True(attachment.IsNestedEmail);
        Assert.Equal(attachment.Size, (await MailAssert.ReadAttachmentAsync(parsed, attachment.Id)).Length);
    }

    [Fact]
    [Trait("Scenario", "email-opening / 无法识别或损坏的文件 / 层层嵌套的 MSG 报告已损坏，不会让进程崩溃")]
    public async Task DeeplyNestedMsg_IsRejectedAsCorrupted_WithoutCrashing()
    {
        // MsgReader 自己限制了嵌入邮件的层数（10 层）；这里确认超过限制时得到的是“已损坏”，而不是崩溃
        using var temp = new TempDirectory();
        var path = temp.WriteBytes("deep.msg", BuildNestedMsg(File.ReadAllBytes(SampleFiles.GetPath("nested-msg-two-levels.msg")), depth: 40));

        await MailAssert.ParseFailsAsync(path, EmailParseErrorKind.Corrupted);
    }

    [Fact]
    [Trait("Scenario", "email-attachments / 打开嵌套邮件 / MSG 嵌入的 MSG 在限制层数以内可以正常打开")]
    public async Task NestedMsg_WithinTheLimit_StillOpens()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteBytes("nested.msg", BuildNestedMsg(File.ReadAllBytes(SampleFiles.GetPath("nested-msg-two-levels.msg")), depth: 4));

        using var parsed = await EmailParser.ParseFileAsync(path);

        var attachment = Assert.Single(parsed.Document.Attachments);
        Assert.True(attachment.IsNestedEmail);
        Assert.Equal(attachment.Size, (await MailAssert.ReadAttachmentAsync(parsed, attachment.Id)).Length);
    }

    // 以样本 MSG 的结构为模板，造一个嵌入了 depth 层 MSG 的 MSG
    private static byte[] BuildNestedMsg(byte[] sample, int depth)
    {
        const string attachName = "__attach_version1.0_#00000000";
        const string embeddedName = "__substg1.0_3701000D";

        using var source = new MemoryStream(sample);
        using var sourceRoot = RootStorage.Open(source);
        var attachTemplate = sourceRoot.OpenStorage(attachName);
        var embeddedTemplate = attachTemplate.OpenStorage(embeddedName);

        var output = new MemoryStream();
        using (var root = RootStorage.Create(output, OpenMcdf.Version.V3, StorageModeFlags.LeaveOpen))
        {
            Fill(root, sourceRoot, depth);
        }

        return output.ToArray();

        void Fill(Storage destination, Storage messageTemplate, int remaining)
        {
            foreach (var entry in messageTemplate.EnumerateEntries().Where(entry => entry.Name != attachName))
            {
                Copy(destination, messageTemplate, entry);
            }

            if (remaining <= 0)
            {
                return;
            }

            var attach = destination.CreateStorage(attachName);
            foreach (var entry in attachTemplate.EnumerateEntries().Where(entry => entry.Name != embeddedName))
            {
                Copy(attach, attachTemplate, entry);
            }

            Fill(attach.CreateStorage(embeddedName), embeddedTemplate, remaining - 1);
        }

        static void Copy(Storage destination, Storage from, EntryInfo entry)
        {
            if (entry.Type == EntryType.Storage)
            {
                var copy = destination.CreateStorage(entry.Name);
                var original = from.OpenStorage(entry.Name);
                foreach (var child in original.EnumerateEntries())
                {
                    Copy(copy, original, child);
                }
            }
            else
            {
                using var read = from.OpenStream(entry.Name);
                using var write = destination.CreateStream(entry.Name);
                read.CopyTo(write);
            }
        }
    }
}
