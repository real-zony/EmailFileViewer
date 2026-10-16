using System.Runtime.Versioning;
using System.Text.Json.Nodes;
using EmailFileViewer.Core.Tests;

namespace EmailFileViewer.Sidecar.Tests;

/// <summary>
/// 任务 4.3：临时目录——启动时清理其他会话留下的目录，退出时删除本会话的目录，删除失败忽略。
/// </summary>
public class SessionDirectoryTests
{
    [Fact]
    [Trait("Task", "4.3")]
    public async Task Startup_CreatesTheSessionDirectory_UnderTheSessionsRoot()
    {
        await using var session = SidecarSession.Start();

        await session.CallAsync("system.ping");

        var directory = Assert.Single(Directory.GetDirectories(session.SessionRoot));
        Assert.Equal(32, Path.GetFileName(directory).Length);
        Assert.True(File.Exists(Path.Combine(directory, ".lock")));
    }

    [Fact]
    [Trait("Task", "4.3")]
    public async Task Startup_DeletesDirectoriesLeftByOtherSessions()
    {
        using var temp = new TempDirectory();
        var root = Path.Combine(temp.DirectoryPath, "sessions");
        var leftovers = new[]
        {
            Seed(root, "crashed-with-files", withLock: true, "doc1/report.pdf", "doc1/photo.jpg"),
            Seed(root, "crashed-without-lock", withLock: false, "doc1/report.pdf"),
            Seed(root, "empty"),
        };
        var unrelated = Path.Combine(temp.DirectoryPath, "keep.txt");
        await File.WriteAllTextAsync(unrelated, "not part of any session");

        await using var session = SidecarSession.Start(new StdioOptions { SessionRoot = root });
        await session.CallAsync("system.ping");

        Assert.All(leftovers, Eventually.DirectoryIsGone);
        Assert.Single(Directory.GetDirectories(root));
        Assert.True(File.Exists(unrelated));
    }

    [Fact]
    [Trait("Task", "4.3")]
    public async Task Startup_KeepsTheDirectoryOfASessionThatIsStillRunning()
    {
        using var temp = new TempDirectory();
        var root = Path.Combine(temp.DirectoryPath, "sessions");
        var live = Seed(root, "live-session", withLock: false, "doc1/open-in-viewer.pdf");

        // 另一个还活着的会话：一直占着它的锁文件
        using var heldLock = new FileStream(Path.Combine(live, ".lock"), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        var stale = Seed(root, "stale-session", withLock: true, "doc1/old.pdf");

        await using var session = SidecarSession.Start(new StdioOptions { SessionRoot = root });
        await session.CallAsync("system.ping");
        var code = await session.CompleteAsync();

        Assert.Equal(0, code);
        Assert.True(File.Exists(Path.Combine(live, "doc1", "open-in-viewer.pdf")));
        Eventually.DirectoryIsGone(stale);
    }

    [Fact]
    [Trait("Task", "4.3")]
    public async Task Exit_DeletesThisSessionsDirectoryWithTheExtractedFiles()
    {
        await using var session = SidecarSession.Start();
        var (docId, document) = await Samples.OpenAsync(session, "attachments-pdf-xlsx-jpg.eml");
        var extracted = SidecarSession.Result(await session.CallAsync("attachment.extractForOpen", new JsonObject { ["docId"] = docId, ["attachmentId"] = Samples.AttachmentId(document, "report.pdf") }));
        var path = (string)extracted["path"]!;
        Assert.True(File.Exists(path));

        var code = await session.CompleteAsync();

        Assert.Equal(0, code);
        Eventually.IsTrue(() => !File.Exists(path), "提取出来的文件应该已经被删除");
        Eventually.HasNoSubdirectories(session.SessionRoot);
    }

    [Fact]
    [Trait("Task", "4.3")]
    public async Task Exit_AfterCancellation_AlsoDeletesTheSessionDirectory()
    {
        await using var session = SidecarSession.Start();
        var (docId, document) = await Samples.OpenAsync(session, "attachments-pdf-xlsx-jpg.eml");
        SidecarSession.Result(await session.CallAsync("attachment.extractForOpen", new JsonObject { ["docId"] = docId, ["attachmentId"] = Samples.AttachmentId(document, "report.pdf") }));

        await session.CancelAsync();

        Eventually.HasNoSubdirectories(session.SessionRoot);
    }

    [Fact]
    [Trait("Task", "4.3")]
    public async Task Exit_ClosingADocumentDoesNotDeleteItsExtractedFilesYet()
    {
        await using var session = SidecarSession.Start();
        var (docId, document) = await Samples.OpenAsync(session, "attachments-pdf-xlsx-jpg.eml");
        var extracted = SidecarSession.Result(await session.CallAsync("attachment.extractForOpen", new JsonObject { ["docId"] = docId, ["attachmentId"] = Samples.AttachmentId(document, "report.pdf") }));

        SidecarSession.Result(await session.CallAsync("document.close", new JsonObject { ["docId"] = docId }));

        // 外部程序可能还在用这个文件：留到退出时再清理
        Assert.True(File.Exists((string)extracted["path"]!));
    }

    [UnixOnlyFact]
    [UnsupportedOSPlatform("windows")]
    [Trait("Task", "4.3")]
    public async Task Exit_WhenTheDirectoryCannotBeDeleted_ItIsIgnoredAndTheExitCodeIsStillZero()
    {
        await using var session = SidecarSession.Start();
        var (docId, document) = await Samples.OpenAsync(session, "attachments-pdf-xlsx-jpg.eml");
        var extracted = SidecarSession.Result(await session.CallAsync("attachment.extractForOpen", new JsonObject { ["docId"] = docId, ["attachmentId"] = Samples.AttachmentId(document, "report.pdf") }));
        var documentDirectory = Path.GetDirectoryName((string)extracted["path"]!)!;

        // 目录没有写权限，里面的文件就删不掉（和 Windows 上文件仍被外部程序打开的效果类似）
        File.SetUnixFileMode(documentDirectory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            var code = await session.CompleteAsync();

            Assert.Equal(0, code);
            Assert.True(File.Exists((string)extracted["path"]!));
            Assert.Empty(session.Error.ToString());
        }
        finally
        {
            File.SetUnixFileMode(documentDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [UnixOnlyFact]
    [UnsupportedOSPlatform("windows")]
    [Trait("Task", "4.3")]
    public async Task Startup_WhenTheTemporaryDirectoryIsNotWritable_OpeningAndSavingStillWork_OnlyExtractFails()
    {
        using var temp = new TempDirectory();
        var root = Path.Combine(temp.DirectoryPath, "sessions");
        Directory.CreateDirectory(root);
        File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            await using var session = SidecarSession.Start(new StdioOptions { SessionRoot = root });
            var (docId, document) = await Samples.OpenAsync(session, "attachments-pdf-xlsx-jpg.eml");
            var attachmentId = Samples.AttachmentId(document, "report.pdf");

            var extract = await session.CallAsync("attachment.extractForOpen", new JsonObject { ["docId"] = docId, ["attachmentId"] = attachmentId });
            var save = await session.CallAsync("attachment.save", new JsonObject { ["docId"] = docId, ["attachmentId"] = attachmentId, ["targetPath"] = Path.Combine(temp.DirectoryPath, "report.pdf") });

            Assert.Equal(Protocol.ErrorCodes.WriteFailed, SidecarSession.ErrorCode(extract));
            SidecarSession.Result(save);
            Assert.Equal(0, await session.CompleteAsync());
        }
        finally
        {
            File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    // 造一个“别的会话留下的”目录
    private static string Seed(string root, string name, bool withLock = false, params string[] files)
    {
        var directory = Directory.CreateDirectory(Path.Combine(root, name)).FullName;
        if (withLock)
        {
            File.WriteAllText(Path.Combine(directory, ".lock"), string.Empty);
        }

        foreach (var file in files)
        {
            var path = Path.Combine(directory, file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "leftover");
        }

        return directory;
    }
}
