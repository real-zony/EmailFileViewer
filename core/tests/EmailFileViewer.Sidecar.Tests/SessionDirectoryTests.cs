using System.Runtime.Versioning;
using System.Text.Json.Nodes;
using EmailFileViewer.Core.Tests;
using EmailFileViewer.Sidecar.Sessions;

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
            Seed(root, NewSessionName(), withLock: true, "doc1/report.pdf", "doc1/photo.jpg"),
            Seed(root, NewSessionName(), withLock: false, "doc1/report.pdf"),
            Seed(root, NewSessionName()),
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
        var live = Seed(root, NewSessionName(), withLock: false, "doc1/open-in-viewer.pdf");

        // 另一个还活着的会话：一直占着它的锁文件
        using var heldLock = new FileStream(Path.Combine(live, ".lock"), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        var stale = Seed(root, NewSessionName(), withLock: true, "doc1/old.pdf");

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

    // ---- 审查 I1：会话目录的位置与符号链接 ----

    [Fact]
    [Trait("Task", "4.3")]
    public async Task Startup_OnlyDeletesDirectoriesWhoseNameLooksLikeASessionId()
    {
        using var temp = new TempDirectory();
        var root = Path.Combine(temp.DirectoryPath, "sessions");
        var stale = Seed(root, NewSessionName(), withLock: false, "doc1/old.pdf");

        // 名字不是 32 位小写十六进制的目录不是我们建的，没有锁文件也不能动
        var notOurs = new[]
        {
            Seed(root, "my-photos", withLock: false, "a.jpg"),
            Seed(root, new string('a', 31), withLock: false, "b.txt"),
            Seed(root, new string('a', 33), withLock: false, "c.txt"),
            Seed(root, new string('A', 32), withLock: false, "d.txt"),
            Seed(root, new string('g', 32), withLock: false, "e.txt"),
        };

        await using var session = SidecarSession.Start(new StdioOptions { SessionRoot = root });
        await session.CallAsync("system.ping");

        Eventually.DirectoryIsGone(stale);
        Assert.All(notOurs, directory => Assert.True(Directory.Exists(directory), $"{directory} 不应该被删除"));
    }

    [UnixOnlyFact]
    [UnsupportedOSPlatform("windows")]
    [Trait("Task", "4.3")]
    public async Task Startup_WhenTheRootIsASymbolicLink_DoesNotTouchTheLinkTarget_AndOnlyExtractingFails()
    {
        // 复现审查里的攻击：根目录事先被放成指向别人目录的符号链接，里面有一个看起来像“已结束的会话”的目录
        using var temp = new TempDirectory();
        var victim = Path.Combine(temp.DirectoryPath, "victim-projects");
        var victimSession = Seed(victim, NewSessionName(), withLock: false, "important.txt");
        var important = Path.Combine(victimSession, "important.txt");
        var root = Path.Combine(temp.DirectoryPath, "sessions");
        Directory.CreateSymbolicLink(root, victim);

        await using var session = SidecarSession.Start(new StdioOptions { SessionRoot = root });
        var (docId, document) = await Samples.OpenAsync(session, "attachments-pdf-xlsx-jpg.eml");
        var extract = await session.CallAsync("attachment.extractForOpen", new JsonObject { ["docId"] = docId, ["attachmentId"] = Samples.AttachmentId(document, "report.pdf") });
        var code = await session.CompleteAsync();

        Assert.Equal(Protocol.ErrorCodes.WriteFailed, SidecarSession.ErrorCode(extract));
        Assert.Equal(0, code);
        Assert.True(File.Exists(important), "符号链接指向的目录里的内容不能被删除");
        Assert.Equal(["important.txt"], Samples.EntryNames(victimSession));
        Assert.Equal([Path.GetFileName(victimSession)], Samples.EntryNames(victim));
    }

    [UnixOnlyFact]
    [UnsupportedOSPlatform("windows")]
    [Trait("Task", "4.3")]
    public async Task Startup_NeverFollowsSymbolicLinks_WhenCleaningUp()
    {
        using var temp = new TempDirectory();
        var outside = Path.Combine(temp.DirectoryPath, "outside");
        Directory.CreateDirectory(Path.Combine(outside, "project"));
        File.WriteAllText(Path.Combine(outside, "project", "code.txt"), "keep");

        var root = Path.Combine(temp.DirectoryPath, "sessions");

        // 名字像会话目录的符号链接：整个跳过
        Directory.CreateDirectory(root);
        var linkedSession = Path.Combine(root, NewSessionName());
        Directory.CreateSymbolicLink(linkedSession, outside);

        // 真正的残留会话目录里有指向外面的符号链接：只删链接，不进去
        var stale = Seed(root, NewSessionName(), withLock: false, "doc1/old.pdf");
        Directory.CreateSymbolicLink(Path.Combine(stale, "doc1", "link-to-outside"), outside);

        await using var session = SidecarSession.Start(new StdioOptions { SessionRoot = root });
        await session.CallAsync("system.ping");

        Eventually.DirectoryIsGone(stale);
        Assert.True(File.Exists(Path.Combine(outside, "project", "code.txt")));
        Assert.True(Directory.Exists(linkedSession), "符号链接本身也不动");
    }

    [UnixOnlyFact]
    [UnsupportedOSPlatform("windows")]
    [Trait("Task", "4.3")]
    public async Task Startup_TightensARootThatOtherUsersCouldAccess_ToOwnerOnly()
    {
        using var temp = new TempDirectory();
        var root = Path.Combine(temp.DirectoryPath, "sessions");
        Directory.CreateDirectory(root);
        File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        await using var session = SidecarSession.Start(new StdioOptions { SessionRoot = root });
        var (docId, document) = await Samples.OpenAsync(session, "attachments-pdf-xlsx-jpg.eml");
        var extract = SidecarSession.Result(await session.CallAsync("attachment.extractForOpen", new JsonObject { ["docId"] = docId, ["attachmentId"] = Samples.AttachmentId(document, "report.pdf") }));

        var ownerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        Assert.Equal(ownerOnly, File.GetUnixFileMode(root));
        Assert.Equal(ownerOnly, File.GetUnixFileMode(Path.GetDirectoryName(Path.GetDirectoryName((string)extract["path"]!)!)!));
        Assert.Equal(ownerOnly, File.GetUnixFileMode(Path.GetDirectoryName((string)extract["path"]!)!));
    }

    [Fact]
    [Trait("Task", "4.3")]
    public void SessionId_IsThirtyTwoLowercaseHexDigits()
    {
        using var temp = new TempDirectory();
        using var directory = new SessionDirectory(Path.Combine(temp.DirectoryPath, "sessions"));

        Assert.Matches("^[0-9a-f]{32}$", directory.SessionId);
    }

    [Fact]
    [Trait("Task", "4.3")]
    public void DefaultRoot_IsInsideAPerUserLocation_NeverTheSharedTempFolder()
    {
        using var temp = new TempDirectory();
        var environment = CreateEnvironment(temp);

        var roots = SessionDirectory.GetDefaultRoots(environment.Get);

        Assert.NotEmpty(roots);
        Assert.All(roots, root =>
        {
            Assert.EndsWith(Path.Combine("EmailFileViewer", "sessions"), root);
            Assert.False(root.StartsWith(environment.SharedTemp, StringComparison.Ordinal), $"{root} 不应该在共享的临时目录下");
        });

        // 平台首选的位置排在前面
        var preferred = OperatingSystem.IsWindows()
            ? Path.Combine(environment.LocalAppData, "Temp")
            : OperatingSystem.IsMacOS() ? environment.PerUserTemp : environment.CacheHome;
        Assert.StartsWith(preferred, roots[0], StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Task", "4.3")]
    public void DefaultRoot_TriesTheLocationsInTheDocumentedOrder()
    {
        using var temp = new TempDirectory();
        var environment = CreateEnvironment(temp);

        var roots = SessionDirectory.GetDefaultRoots(environment.Get);

        // Linux：$XDG_RUNTIME_DIR 通常是容量很小的 tmpfs，大附件可能写满，所以排在最后
        string[] expectedBases = OperatingSystem.IsWindows()
            ? [Path.Combine(environment.LocalAppData, "Temp"), environment.PerUserTemp]
            : OperatingSystem.IsMacOS()
                ? [environment.PerUserTemp, Path.Combine(environment.Home, "Library", "Caches")]
                : [environment.CacheHome, Path.Combine(environment.Home, ".cache"), environment.RuntimeDir];
        Assert.Equal(expectedBases.Select(b => Path.Combine(b, "EmailFileViewer", "sessions")), roots);
    }

    [UnixOnlyFact]
    [UnsupportedOSPlatform("windows")]
    [Trait("Task", "4.3")]
    public void DefaultRoot_OnLinuxWithoutCacheVariables_UsesTheCacheFolderInHome_BeforeTheRuntimeDirectory()
    {
        if (OperatingSystem.IsMacOS())
        {
            return;
        }

        using var temp = new TempDirectory();
        var environment = CreateEnvironment(temp);

        var roots = SessionDirectory.GetDefaultRoots(name => name == "XDG_CACHE_HOME" ? null : environment.Get(name));

        Assert.Equal(
            [Path.Combine(environment.Home, ".cache", "EmailFileViewer", "sessions"), Path.Combine(environment.RuntimeDir, "EmailFileViewer", "sessions")],
            roots);
    }

    [UnixOnlyFact]
    [UnsupportedOSPlatform("windows")]
    [Trait("Task", "4.3")]
    public void DefaultRoot_SkipsABaseDirectoryThatOtherUsersCanWriteTo()
    {
        using var temp = new TempDirectory();
        var environment = CreateEnvironment(temp);

        // TMPDIR、XDG_RUNTIME_DIR 被设成 /tmp 这类谁都能写的目录：不能用
        var roots = SessionDirectory.GetDefaultRoots(name => name is "TMPDIR" or "XDG_RUNTIME_DIR" or "XDG_CACHE_HOME" ? environment.SharedTemp : environment.Get(name));

        Assert.NotEmpty(roots);
        Assert.All(roots, root => Assert.False(root.StartsWith(environment.SharedTemp, StringComparison.Ordinal)));
    }

    [UnixOnlyFact]
    [UnsupportedOSPlatform("windows")]
    [Trait("Task", "4.3")]
    public void DefaultRoot_WithoutAnyUsableLocation_HasNoRoot_AndNothingIsCreated()
    {
        using var temp = new TempDirectory();
        var environment = CreateEnvironment(temp);

        // 没有 HOME，其他环境变量也指向谁都能写的目录
        var directory = new SessionDirectory(root: null, name => name is "TMPDIR" or "XDG_RUNTIME_DIR" ? environment.SharedTemp : null);
        directory.Initialize();

        Assert.Throws<IOException>(() => directory.GetDocumentDirectory("d1"));
        Assert.Empty(Directory.GetFileSystemEntries(environment.SharedTemp));
        directory.Dispose();
    }

    [UnixOnlyFact]
    [UnsupportedOSPlatform("windows")]
    [Trait("Task", "4.3")]
    public void DefaultRoot_CreatesOwnerOnlyDirectoriesAtEveryLevel()
    {
        using var temp = new TempDirectory();
        var environment = CreateEnvironment(temp);

        using var directory = new SessionDirectory(root: null, environment.Get);
        directory.Initialize();

        var ownerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        Assert.True(Directory.Exists(directory.Path));
        Assert.Equal(ownerOnly, File.GetUnixFileMode(directory.Path));
        Assert.Equal(ownerOnly, File.GetUnixFileMode(directory.Root));
        Assert.Equal(ownerOnly, File.GetUnixFileMode(Path.GetDirectoryName(directory.Root)!));
    }

    [UnixOnlyFact]
    [UnsupportedOSPlatform("windows")]
    [Trait("Task", "4.3")]
    public void DefaultRoot_WhenTheApplicationFolderIsASymbolicLink_IsNotUsed_AndTheNextLocationIsTried()
    {
        using var temp = new TempDirectory();
        var environment = CreateEnvironment(temp);
        var firstRoot = SessionDirectory.GetDefaultRoots(environment.Get)[0];
        var appFolder = Path.GetDirectoryName(firstRoot)!;

        // 攻击者在首选位置里预先放了一个指向别人目录的 EmailFileViewer
        var victim = Directory.CreateDirectory(Path.Combine(temp.DirectoryPath, "victim")).FullName;
        Directory.CreateSymbolicLink(appFolder, victim);

        using var directory = new SessionDirectory(root: null, environment.Get);
        directory.Initialize();

        Assert.Empty(Directory.GetFileSystemEntries(victim));
        Assert.NotEqual(firstRoot, directory.Root);
        Assert.True(Directory.Exists(directory.Path), "应该改用下一个位置");
    }

    [UnixOnlyFact]
    [UnsupportedOSPlatform("windows")]
    [Trait("Task", "4.3")]
    public void DefaultRoot_WhenSessionsFolderIsASymbolicLink_IsNotUsed()
    {
        using var temp = new TempDirectory();
        var environment = CreateEnvironment(temp);
        var roots = SessionDirectory.GetDefaultRoots(environment.Get);

        // 每个候选位置里的 sessions 都被放成指向别人目录的符号链接
        var victim = Directory.CreateDirectory(Path.Combine(temp.DirectoryPath, "victim")).FullName;
        var stale = Seed(victim, NewSessionName(), withLock: false, "important.txt");
        foreach (var root in roots)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(root)!);
            Directory.CreateSymbolicLink(root, victim);
        }

        using var directory = new SessionDirectory(root: null, environment.Get);
        directory.Initialize();

        Assert.Throws<IOException>(() => directory.GetDocumentDirectory("d1"));
        Assert.True(File.Exists(Path.Combine(stale, "important.txt")), "指向的目录里的内容不能被删除");
    }

    private static string NewSessionName() => Guid.NewGuid().ToString("N");

    // 模拟一个用户环境：各种“每个用户自己的目录”都存在，另外有一个谁都能写的共享临时目录
    private static FakeEnvironment CreateEnvironment(TempDirectory temp)
    {
        var shared = Directory.CreateDirectory(Path.Combine(temp.DirectoryPath, "shared-tmp")).FullName;
        var home = Directory.CreateDirectory(Path.Combine(temp.DirectoryPath, "home")).FullName;
        var runtimeDir = Directory.CreateDirectory(Path.Combine(temp.DirectoryPath, "run-user")).FullName;
        var cacheHome = Directory.CreateDirectory(Path.Combine(temp.DirectoryPath, "xdg-cache")).FullName;
        var perUserTemp = Directory.CreateDirectory(Path.Combine(temp.DirectoryPath, "per-user-tmp")).FullName;
        var localAppData = Directory.CreateDirectory(Path.Combine(temp.DirectoryPath, "local-app-data")).FullName;
        Directory.CreateDirectory(Path.Combine(localAppData, "Temp"));
        Directory.CreateDirectory(Path.Combine(home, ".cache"));
        Directory.CreateDirectory(Path.Combine(home, "Library", "Caches"));

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(shared, (UnixFileMode)0b1_111_111_111);
            File.SetUnixFileMode(runtimeDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(cacheHome, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(perUserTemp, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return new FakeEnvironment(shared, home, runtimeDir, cacheHome, perUserTemp, localAppData);
    }

    private sealed record FakeEnvironment(string SharedTemp, string Home, string RuntimeDir, string CacheHome, string PerUserTemp, string LocalAppData)
    {
        public string? Get(string name) => name switch
        {
            "HOME" or "USERPROFILE" => Home,
            "XDG_RUNTIME_DIR" => RuntimeDir,
            "XDG_CACHE_HOME" => CacheHome,
            "TMPDIR" => PerUserTemp,
            "LOCALAPPDATA" => LocalAppData,
            "TEMP" => PerUserTemp,
            _ => null,
        };
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
