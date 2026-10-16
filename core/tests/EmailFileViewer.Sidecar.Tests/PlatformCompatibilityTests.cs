using System.Text;
using System.Text.Json.Nodes;
using EmailFileViewer.Core.Tests;
using EmailFileViewer.Sidecar.Methods;
using EmailFileViewer.Sidecar.Protocol;
using EmailFileViewer.Sidecar.Sessions;

namespace EmailFileViewer.Sidecar.Tests;

/// <summary>
/// Windows 上容易出问题的地方：换行、路径形式、大小写、被占用的文件和目录。
/// 能在所有平台上表达的，写成不依赖平台的断言（这样在 macOS、Linux 上也能发现同类问题）；只有 Windows 才有的行为，用 Windows 专用的测试。
/// </summary>
public class PlatformCompatibilityTests
{
    // ---- 换行与编码 ----

    [Fact]
    [Trait("Task", "4.1")]
    public async Task Output_EveryMessageEndsWithASingleLf_AndNoCarriageReturnOrBomAppears()
    {
        await using var session = SidecarSession.Start(new StdioOptions { ConfigureMethods = TestMethods.AddThrowing });

        await session.CallAsync("system.ping");
        await Samples.OpenAsync(session, "charset-gbk.eml");
        await session.CallAsync("test.throw");
        await session.CallAsync("no.such.method");
        await session.SendRawAsync("not json");
        await session.WaitForNullIdResponsesAsync(1);
        await session.CompleteAsync();

        var bytes = session.RawOutput;
        Assert.NotEmpty(bytes);
        Assert.Equal((byte)'\n', bytes[^1]);
        Assert.DoesNotContain((byte)'\r', bytes);
        Assert.Equal(session.Lines.Count, bytes.Count(b => b == (byte)'\n'));
        Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()), "输出不能以 BOM 开头");
        Assert.DoesNotContain("\r\n", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    [Trait("Task", "4.1")]
    public async Task Input_AcceptsLfAndCrLfLineEndings_AndALastLineWithoutAnyLineEnding()
    {
        await using var session = SidecarSession.Start();

        await session.SendTextAsync("{\"id\":1,\"method\":\"system.ping\"}\r\n");
        await session.SendTextAsync("{\"id\":2,\"method\":\"system.ping\"}\n");
        await session.SendTextAsync("{\"id\":3,\"method\":\"system.ping\"}");
        await session.CompleteAsync();

        var ids = session.Lines.Select(line => (int)JsonNode.Parse(line)!["id"]!).Order().ToArray();
        Assert.Equal([1, 2, 3], ids);
    }

    // ---- 路径形式 ----

    [UnixOnlyTheory]
    [InlineData("/home/alice/x.pdf", true)]
    [InlineData("/", true)]
    [InlineData("x.pdf", false)]
    [InlineData("./x.pdf", false)]
    [InlineData("../x.pdf", false)]
    [InlineData("~/x.pdf", false)]
    [InlineData("C:\\Users\\alice\\x.pdf", false)]
    [Trait("Task", "4.3")]
    public void AbsolutePath_OnUnix(string path, bool expectedValid)
    {
        AssertAbsolutePath(path, expectedValid);
    }

    [WindowsOnlyTheory]
    [InlineData("C:\\Users\\alice\\x.pdf", true)]
    [InlineData("C:/Users/alice/x.pdf", true)]
    [InlineData("c:\\users\\alice\\x.pdf", true)]
    [InlineData("\\\\server\\share\\x.pdf", true)]
    [InlineData("\\\\?\\C:\\Users\\alice\\x.pdf", true)]
    [InlineData("\\\\?\\UNC\\server\\share\\x.pdf", true)]
    [InlineData("x.pdf", false)]
    [InlineData(".\\x.pdf", false)]
    [InlineData("..\\x.pdf", false)]
    [InlineData("C:x.pdf", false)]
    [InlineData("\\Users\\alice\\x.pdf", false)]
    [InlineData("/Users/alice/x.pdf", false)]
    [Trait("Task", "4.3")]
    public void AbsolutePath_OnWindows(string path, bool expectedValid)
    {
        AssertAbsolutePath(path, expectedValid);
    }

    [Fact]
    [Trait("Task", "4.3")]
    public async Task Save_AcceptsAlternateSeparatorsAndDotSegments_AndReturnsTheNormalizedPath()
    {
        await using var session = SidecarSession.Start();
        var (docId, document) = await Samples.OpenAsync(session, "attachments-pdf-xlsx-jpg.eml");
        var directory = Directory.CreateDirectory(Path.Combine(session.Temp.DirectoryPath, "out")).FullName;
        var sep = Path.DirectorySeparatorChar;

        // 例如 C:\...\out\sub\..\x.pdf，把分隔符换成另一种写法：Windows 上是 C:/.../out/sub/../x.pdf，Unix 上两种写法相同
        var written = directory + sep + "sub" + sep + ".." + sep + "x.pdf";
        var target = written.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var result = SidecarSession.Result(await session.CallAsync("attachment.save", new JsonObject
        {
            ["docId"] = docId,
            ["attachmentId"] = Samples.AttachmentId(document, "report.pdf"),
            ["targetPath"] = target,
        }));

        Assert.Equal(Path.Combine(directory, "x.pdf"), (string)result["path"]!);
        Assert.Equal(["x.pdf"], Samples.EntryNames(directory));
    }

    [Fact]
    [Trait("Task", "4.3")]
    public async Task SaveAll_AcceptsATargetFolderWithATrailingSeparator()
    {
        await using var session = SidecarSession.Start();
        var (docId, _) = await Samples.OpenAsync(session, "attachments-pdf-xlsx-jpg.eml");
        var directory = Directory.CreateDirectory(Path.Combine(session.Temp.DirectoryPath, "out")).FullName;

        var result = SidecarSession.Result(await session.CallAsync("attachment.saveAll", new JsonObject
        {
            ["docId"] = docId,
            ["targetDir"] = directory + Path.DirectorySeparatorChar,
        }));

        Assert.Empty(result["failed"]!.AsArray());
        Assert.Equal(3, result["saved"]!.AsArray().Count);
        Assert.All(result["saved"]!.AsArray(), saved => Assert.Equal(directory, Path.GetDirectoryName((string)saved!["path"]!)));
        Assert.Equal(["budget.xlsx", "photo.jpg", "report.pdf"], Samples.EntryNames(directory));
    }

    [Fact]
    [Trait("Task", "4.3")]
    public async Task Save_ReturnsTheNormalizedFullPath_EvenWhenTheRequestUsesTheRawTemporaryDirectory()
    {
        // 故意不经过 TempDirectory 的规范化：GitHub 的 Windows runner 上 Path.GetTempPath() 是 8.3 短名，
        // 返回的路径是 GetFullPath 之后的形式（短名会被展开），Rust 端要用返回的路径，而不是自己拼的
        await using var session = SidecarSession.Start();
        var (docId, document) = await Samples.OpenAsync(session, "attachments-pdf-xlsx-jpg.eml");
        var target = Path.Combine(Path.GetTempPath(), "efv-raw-temp-" + Guid.NewGuid().ToString("N") + ".pdf");
        try
        {
            var result = SidecarSession.Result(await session.CallAsync("attachment.save", new JsonObject
            {
                ["docId"] = docId,
                ["attachmentId"] = Samples.AttachmentId(document, "report.pdf"),
                ["targetPath"] = target,
            }));

            Assert.Equal(Path.GetFullPath(target), (string)result["path"]!);
            Assert.True(File.Exists(target));
        }
        finally
        {
            File.Delete(target);
        }
    }

    [WindowsOnlyFact]
    [Trait("Task", "4.3")]
    public async Task Save_ToAPathWithTheExtendedLengthPrefix_Works()
    {
        // Rust 的 std::fs::canonicalize 在 Windows 上返回 \\?\C:\... 形式的路径
        await using var session = SidecarSession.Start();
        var (docId, document) = await Samples.OpenAsync(session, "attachments-pdf-xlsx-jpg.eml");
        var directory = Directory.CreateDirectory(Path.Combine(session.Temp.DirectoryPath, "out")).FullName;
        var target = @"\\?\" + Path.Combine(directory, "report.pdf");

        var result = SidecarSession.Result(await session.CallAsync("attachment.save", new JsonObject
        {
            ["docId"] = docId,
            ["attachmentId"] = Samples.AttachmentId(document, "report.pdf"),
            ["targetPath"] = target,
        }));

        Assert.Equal(target, (string)result["path"]!);
        Assert.Equal(["report.pdf"], Samples.EntryNames(directory));
    }

    [WindowsOnlyFact]
    [Trait("Task", "4.3")]
    public async Task SaveAll_ToAFolderWithTheExtendedLengthPrefix_Works()
    {
        await using var session = SidecarSession.Start();
        var (docId, _) = await Samples.OpenAsync(session, "attachments-duplicate-names.eml");
        var directory = Directory.CreateDirectory(Path.Combine(session.Temp.DirectoryPath, "out")).FullName;

        var result = SidecarSession.Result(await session.CallAsync("attachment.saveAll", new JsonObject
        {
            ["docId"] = docId,
            ["targetDir"] = @"\\?\" + directory,
        }));

        Assert.Empty(result["failed"]!.AsArray());
        Assert.Equal(["report (1).pdf", "report.pdf"], Samples.EntryNames(directory));
    }

    // ---- 大小写 ----

    [Fact]
    [Trait("Task", "4.3")]
    public async Task SaveAll_NeverOverwritesAFileThatDiffersOnlyInCase_OnAnyFileSystem()
    {
        // Windows 和 macOS 的文件系统不区分大小写（Report.PDF 和 report.pdf 是同一个文件），Linux 区分；
        // 不管是哪一种，已有的 Report.PDF 都不能被覆盖，两个附件的内容都要完整保存下来
        await using var session = SidecarSession.Start();
        var (docId, document) = await Samples.OpenAsync(session, "attachments-duplicate-names.eml");
        var directory = Directory.CreateDirectory(Path.Combine(session.Temp.DirectoryPath, "out")).FullName;
        await File.WriteAllTextAsync(Path.Combine(directory, "Report.PDF"), "KEEP ME");

        SidecarSession.Result(await session.CallAsync("attachment.saveAll", new JsonObject { ["docId"] = docId, ["targetDir"] = directory }));

        var names = Samples.EntryNames(directory);
        Assert.Equal(3, names.Length);
        Assert.Equal("KEEP ME", await File.ReadAllTextAsync(Path.Combine(directory, "Report.PDF")));

        var ids = document["attachments"]!.AsArray().Select(a => (string)a!["id"]!).ToArray();
        var expected = new List<string>();
        foreach (var id in ids)
        {
            expected.Add(Convert.ToBase64String(await Samples.ReadAttachmentWithCoreAsync("attachments-duplicate-names.eml", id)));
        }

        var actual = new List<string>();
        foreach (var name in names.Where(name => name != "Report.PDF"))
        {
            actual.Add(Convert.ToBase64String(await File.ReadAllBytesAsync(Path.Combine(directory, name))));
        }

        Assert.Equal(expected.Order(StringComparer.Ordinal).ToArray(), actual.Order(StringComparer.Ordinal).ToArray());
    }

    // ---- 被占用的文件和目录、锁文件 ----

    [Fact]
    [Trait("Task", "4.3")]
    public async Task Exit_WhenAnExtractedFileIsStillOpenInAnotherProgram_ExitsNormally_AndTheNextStartupCleansUp()
    {
        await using var first = SidecarSession.Start();
        var (docId, document) = await Samples.OpenAsync(first, "attachments-pdf-xlsx-jpg.eml");
        var extracted = SidecarSession.Result(await first.CallAsync("attachment.extractForOpen", new JsonObject
        {
            ["docId"] = docId,
            ["attachmentId"] = Samples.AttachmentId(document, "report.pdf"),
        }));

        // 模拟 PDF 阅读器还开着这个文件：Windows 上它会让文件（以及它所在的目录）删不掉
        var viewer = new FileStream((string)extracted["path"]!, FileMode.Open, FileAccess.Read, FileShare.Read);
        int code;
        try
        {
            code = await first.CompleteAsync();
        }
        finally
        {
            await viewer.DisposeAsync();
        }

        Assert.Equal(0, code);
        Assert.Empty(first.Error.ToString());

        // 阅读器关掉之后，下一次启动把残留的目录清理掉
        await using var second = SidecarSession.Start(new StdioOptions { SessionRoot = first.SessionRoot });
        await second.CallAsync("system.ping");
        Eventually.IsTrue(() => Directory.GetDirectories(first.SessionRoot).Length == 1, "残留的会话目录应该被下一次启动清理掉，只剩第二个会话自己的目录");
        var left = Assert.Single(Directory.GetDirectories(first.SessionRoot));
        Assert.True(File.Exists(Path.Combine(left, ".lock")));
        Assert.False(Directory.Exists(Path.Combine(left, docId)));
    }

    [Fact]
    [Trait("Task", "4.3")]
    public void LockFile_CannotBeOpenedExclusivelyWhileTheSessionIsAlive_AndTwoLiveSessionsDoNotDeleteEachOther()
    {
        using var temp = new TempDirectory();
        var root = Path.Combine(temp.DirectoryPath, "sessions");
        var first = new SessionDirectory(root);
        first.Initialize();
        var second = new SessionDirectory(root);
        second.Initialize();
        try
        {
            // 第二个会话启动时会清理“其他会话”的目录：第一个还活着，它的目录不能被删
            Assert.True(Directory.Exists(first.Path));
            Assert.True(Directory.Exists(second.Path));

            // 锁文件被独占占用：别人以独占方式打不开（Windows 是共享冲突，Unix 是 flock）
            Assert.Throws<IOException>(() => new FileStream(Path.Combine(first.Path, ".lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None));

            first.Dispose();
            Eventually.DirectoryIsGone(first.Path);

            var third = new SessionDirectory(root);
            third.Initialize();
            try
            {
                // 第一个已经结束并清理了自己；第二个还活着，不能被第三个清理掉
                Assert.True(Directory.Exists(second.Path));
                Assert.True(Directory.Exists(third.Path));
            }
            finally
            {
                third.Dispose();
            }
        }
        finally
        {
            first.Dispose();
            second.Dispose();
        }

        Eventually.HasNoSubdirectories(root);
    }

    [Fact]
    [Trait("Task", "4.3")]
    public void Dispose_WhenAFileInsideIsOpen_DoesNotThrow_AndTheLockIsAlreadyReleased()
    {
        using var temp = new TempDirectory();
        var root = Path.Combine(temp.DirectoryPath, "sessions");
        var session = new SessionDirectory(root);
        session.Initialize();
        var documentDirectory = session.GetDocumentDirectory("d1");
        var file = Path.Combine(documentDirectory, "open.pdf");
        File.WriteAllText(file, "x");

        using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            session.Dispose();
        }

        // 锁文件已经释放：不管目录有没有删干净，下一次启动都能把它认作“已经结束的会话”
        var next = new SessionDirectory(root);
        next.Initialize();
        try
        {
            Eventually.IsTrue(() => Samples.EntryNames(root).SequenceEqual([next.SessionId]), "残留的目录应该被清理掉，只剩新会话自己的目录");
        }
        finally
        {
            next.Dispose();
        }
    }

    // ---- 被占用的文件：Windows 上改名会失败，要重试 ----

    [WindowsOnlyFact]
    [Trait("Task", "4.3")]
    public async Task Save_WhenTheTargetIsBrieflyOpenInAnotherProgram_RetriesUntilItIsReleased()
    {
        using var temp = new TempDirectory();
        var target = Path.Combine(temp.DirectoryPath, "report.pdf");
        await File.WriteAllTextAsync(target, "OLD");
        var files = new AttachmentFiles();

        // 另一个程序（例如 PDF 阅读器）打开着目标文件，一会儿就关
        var other = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read);
        var release = Task.Run(async () =>
        {
            await Task.Delay(100);
            await other.DisposeAsync();
        });

        await files.SaveAsync(target, (stream, token) => stream.WriteAsync("NEW"u8.ToArray(), token).AsTask());
        await release;

        Assert.Equal("NEW", await File.ReadAllTextAsync(target));
        Assert.Equal(["report.pdf"], Samples.EntryNames(temp.DirectoryPath));
    }

    [WindowsOnlyFact]
    [Trait("Task", "4.3")]
    public async Task Save_WhenTheTargetStaysOpenInAnotherProgram_FailsWithWriteFailed_KeepsTheOldFileAndLeavesNoTemporaryFile()
    {
        await using var session = SidecarSession.Start();
        var (docId, document) = await Samples.OpenAsync(session, "attachments-pdf-xlsx-jpg.eml");
        var directory = Directory.CreateDirectory(Path.Combine(session.Temp.DirectoryPath, "out")).FullName;
        var target = Path.Combine(directory, "report.pdf");
        await File.WriteAllTextAsync(target, "OLD");

        string code;
        using (new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var response = await session.CallAsync("attachment.save", new JsonObject
            {
                ["docId"] = docId,
                ["attachmentId"] = Samples.AttachmentId(document, "report.pdf"),
                ["targetPath"] = target,
            });
            code = SidecarSession.ErrorCode(response);
        }

        Assert.Equal(ErrorCodes.WriteFailed, code);
        Assert.Equal("OLD", await File.ReadAllTextAsync(target));
        Assert.Equal(["report.pdf"], Samples.EntryNames(directory));
    }

    private static void AssertAbsolutePath(string path, bool expectedValid)
    {
        if (expectedValid)
        {
            Assert.Equal(path, ParamsCheck.RequireAbsolutePath(path, "targetPath"));
            return;
        }

        var exception = Assert.Throws<ProtocolException>(() => ParamsCheck.RequireAbsolutePath(path, "targetPath"));
        Assert.Equal(ErrorCodes.InvalidParams, exception.Code);
    }
}
