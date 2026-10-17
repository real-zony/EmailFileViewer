using System.Runtime.Versioning;
using EmailFileViewer.Core.Models;
using EmailFileViewer.Core.Tests;
using EmailFileViewer.Sidecar.Methods;

namespace EmailFileViewer.Sidecar.Tests;

/// <summary>
/// 审查复评：Unix 上 <c>File.Move(overwrite: false)</c> 是先检查再 rename，两步之间同名文件被抢先创建会被覆盖。
/// 改用 renameat2(RENAME_NOREPLACE) / renamex_np(RENAME_EXCL)，不支持时退回 link()+unlink()，再不行才退回旧做法并记一条警告。
/// </summary>
[UnsupportedOSPlatform("windows")]
public class NoReplaceMoverTests
{
    [UnixOnlyFact]
    [Trait("Task", "4.3")]
    public void RenameExclusive_WhenTheTargetDoesNotExist_MovesTheFile()
    {
        using var temp = new TempDirectory();
        var (source, target) = Paths(temp);
        File.WriteAllText(source, "payload");

        var outcome = NoReplaceMover.RenameExclusive(source, target);

        Assert.Equal(NoReplaceOutcome.Moved, outcome);
        Assert.False(File.Exists(source));
        Assert.Equal("payload", File.ReadAllText(target));
    }

    [UnixOnlyFact]
    [Trait("Task", "4.3")]
    public void RenameExclusive_WhenTheTargetExists_DoesNotOverwriteIt()
    {
        using var temp = new TempDirectory();
        var (source, target) = Paths(temp);
        File.WriteAllText(source, "new content");
        File.WriteAllText(target, "ORIGINAL");

        var outcome = NoReplaceMover.RenameExclusive(source, target);

        Assert.Equal(NoReplaceOutcome.TargetExists, outcome);
        Assert.Equal("ORIGINAL", File.ReadAllText(target));
        Assert.Equal("new content", File.ReadAllText(source));
    }

    [UnixOnlyFact]
    [Trait("Task", "4.3")]
    public void RenameExclusive_WhenTheTargetIsADirectory_ReportsItAsExisting()
    {
        using var temp = new TempDirectory();
        var (source, target) = Paths(temp);
        File.WriteAllText(source, "x");
        Directory.CreateDirectory(target);

        Assert.Equal(NoReplaceOutcome.TargetExists, NoReplaceMover.RenameExclusive(source, target));
        Assert.True(File.Exists(source));
    }

    [UnixOnlyFact]
    [Trait("Task", "4.3")]
    public void LinkThenUnlink_MovesTheFile_AndDoesNotOverwrite()
    {
        using var temp = new TempDirectory();
        var (source, target) = Paths(temp);
        File.WriteAllText(source, "payload");

        Assert.Equal(NoReplaceOutcome.Moved, NoReplaceMover.LinkThenUnlink(source, target));
        Assert.False(File.Exists(source));
        Assert.Equal("payload", File.ReadAllText(target));

        var second = Path.Combine(temp.DirectoryPath, "second.tmp");
        File.WriteAllText(second, "other");
        Assert.Equal(NoReplaceOutcome.TargetExists, NoReplaceMover.LinkThenUnlink(second, target));
        Assert.Equal("payload", File.ReadAllText(target));
        Assert.Equal("other", File.ReadAllText(second));
    }

    [UnixOnlyFact]
    [Trait("Task", "4.3")]
    public void WhenTheSourceIsMissing_BothStepsReportAnOrdinaryFailure()
    {
        using var temp = new TempDirectory();
        var (source, target) = Paths(temp);

        Assert.Equal(NoReplaceOutcome.Failed, NoReplaceMover.RenameExclusive(source, target));
        Assert.Equal(NoReplaceOutcome.Failed, NoReplaceMover.LinkThenUnlink(source, target));
        Assert.False(File.Exists(target));
    }

    [Fact]
    [Trait("Task", "4.3")]
    public void Move_FallsBackToLink_WhenTheKernelRenameIsNotSupported()
    {
        var calls = new List<string>();
        var mover = new NoReplaceMover(
            (_, _) => { calls.Add("rename"); return NoReplaceOutcome.Unsupported; },
            (_, _) => { calls.Add("link"); return NoReplaceOutcome.Moved; });

        Assert.Equal(NoReplaceOutcome.Moved, mover.Move("a", "b"));
        Assert.Equal(["rename", "link"], calls);
    }

    [Fact]
    [Trait("Task", "4.3")]
    public void Move_DoesNotTryLink_WhenTheRenameAnswersDefinitively()
    {
        foreach (var answer in new[] { NoReplaceOutcome.Moved, NoReplaceOutcome.TargetExists, NoReplaceOutcome.Failed })
        {
            var linkCalled = false;
            var mover = new NoReplaceMover((_, _) => answer, (_, _) => { linkCalled = true; return NoReplaceOutcome.Moved; });

            Assert.Equal(answer, mover.Move("a", "b"));
            Assert.False(linkCalled);
        }
    }

    [Fact]
    [Trait("Task", "4.3")]
    public void Move_ReportsUnsupported_WhenNeitherStepWorks()
    {
        var mover = new NoReplaceMover((_, _) => NoReplaceOutcome.Unsupported, (_, _) => NoReplaceOutcome.Unsupported);

        Assert.Equal(NoReplaceOutcome.Unsupported, mover.Move("a", "b"));
    }

    // ---- 在 AttachmentFiles 里的行为 ----

    [UnixOnlyFact]
    [Trait("Task", "4.3")]
    public async Task Extract_ManyConcurrentWritersToTheSameName_NeverOverwriteEachOther()
    {
        using var temp = new TempDirectory();
        var files = new AttachmentFiles();

        // 同一个目录、同一个文件名，同时提取 24 次：取名和改名之间互相抢，每个人都必须得到自己的文件
        var paths = await Task.WhenAll(Enumerable.Range(0, 24).Select(i => Task.Run(() => files.ExtractAsync(
            temp.DirectoryPath,
            "report.pdf",
            (stream, token) => stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes($"content-{i}"), token).AsTask()))));

        Assert.Equal(24, paths.Distinct().Count());
        Assert.Equal(24, Directory.GetFiles(temp.DirectoryPath).Length);
        Assert.Equal(
            Enumerable.Range(0, 24).Select(i => $"content-{i}").Order(StringComparer.Ordinal),
            paths.Select(File.ReadAllText).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(Directory.GetFiles(temp.DirectoryPath), name => Path.GetFileName(name).StartsWith(".efv-"));
    }

    [UnixOnlyFact]
    [Trait("Task", "4.3")]
    public async Task SaveAll_WhenTheNameIsTakenWhileWriting_NeverOverwritesTheOtherFile()
    {
        using var temp = new TempDirectory();
        var target = Path.Combine(temp.DirectoryPath, "a.txt");
        var files = new AttachmentFiles();

        // 写入期间别的进程在最终名字上创建了文件：改名是原子的“不覆盖”，对方的文件原样保留，我们换名字
        var result = await files.SaveAllAsync(
            temp.DirectoryPath,
            [new EmailAttachment("1", "a.txt", 3, "text/plain", false)],
            async (_, stream, token) =>
            {
                await File.WriteAllTextAsync(target, "SOMEONE ELSE", token);
                await stream.WriteAsync("new"u8.ToArray(), token);
            });

        Assert.Empty(result.Failed);
        Assert.Equal("SOMEONE ELSE", await File.ReadAllTextAsync(target));
        Assert.Equal("new", await File.ReadAllTextAsync(Assert.Single(result.Saved).Path));
    }

    [UnixOnlyFact]
    [Trait("Task", "4.3")]
    public async Task WhenAtomicMoveIsUnsupportedEverywhere_FilesAreStillSaved_AndOneWarningIsWritten()
    {
        using var temp = new TempDirectory();
        var diagnostics = new StringWriter();
        var mover = new NoReplaceMover((_, _) => NoReplaceOutcome.Unsupported, (_, _) => NoReplaceOutcome.Unsupported);
        var files = new AttachmentFiles(mover: mover, diagnostics: diagnostics);
        EmailAttachment[] attachments =
        [
            new("1", "a.txt", 1, "text/plain", false),
            new("2", "b.txt", 1, "text/plain", false),
            new("3", "a.txt", 1, "text/plain", false),
        ];

        var result = await files.SaveAllAsync(temp.DirectoryPath, attachments, (id, stream, token) => stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes($"#{id}"), token).AsTask());

        Assert.Empty(result.Failed);
        Assert.Equal(["a (1).txt", "a.txt", "b.txt"], Samples.EntryNames(temp.DirectoryPath));
        Assert.Equal("#1", await File.ReadAllTextAsync(Path.Combine(temp.DirectoryPath, "a.txt")));
        Assert.Equal("#3", await File.ReadAllTextAsync(Path.Combine(temp.DirectoryPath, "a (1).txt")));

        // 三个附件、三次降级，只记一条
        var warning = Assert.Single(diagnostics.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.Contains("警告", warning);
        Assert.Contains(temp.DirectoryPath, warning);
    }

    [UnixOnlyFact]
    [Trait("Task", "4.3")]
    public async Task WhenTheAtomicMoveWorks_NothingIsLogged()
    {
        using var temp = new TempDirectory();
        var diagnostics = new StringWriter();
        var files = new AttachmentFiles(diagnostics: diagnostics);

        await files.ExtractAsync(temp.DirectoryPath, "a.txt", (stream, token) => stream.WriteAsync("x"u8.ToArray(), token).AsTask());

        Assert.Empty(diagnostics.ToString());
    }

    [UnixOnlyFact]
    [Trait("Task", "4.3")]
    public async Task WhenTheMoveFailsForAnOrdinaryReason_ItIsReportedAsAnIoError_WithoutAWarning()
    {
        using var temp = new TempDirectory();
        var diagnostics = new StringWriter();
        var files = new AttachmentFiles(diagnostics: diagnostics);

        // 写完临时文件之后把整个目录删掉，改名时源文件和目录都不在了
        await Assert.ThrowsAnyAsync<IOException>(() => files.ExtractAsync(
            temp.DirectoryPath,
            "a.txt",
            async (stream, token) =>
            {
                await stream.WriteAsync("x"u8.ToArray(), token);
                File.Delete(Directory.GetFiles(temp.DirectoryPath).Single());
            }));

        Assert.Empty(diagnostics.ToString());
    }

    private static (string Source, string Target) Paths(TempDirectory temp)
    {
        return (Path.Combine(temp.DirectoryPath, "source.tmp"), Path.Combine(temp.DirectoryPath, "target.txt"));
    }
}
