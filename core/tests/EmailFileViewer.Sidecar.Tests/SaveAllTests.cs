using System.Text.Json.Nodes;
using EmailFileViewer.Core.Models;
using EmailFileViewer.Sidecar.Methods;
using EmailFileViewer.Sidecar.Protocol;

namespace EmailFileViewer.Sidecar.Tests;

/// <summary>
/// 任务 4.3：<c>attachment.saveAll</c>——文件名清理、重名编号、不覆盖任何已有文件、单个失败不影响其他。
/// </summary>
public class SaveAllTests
{
    [Fact]
    [Trait("Task", "4.3")]
    public async Task SaveAll_WritesEveryAttachmentWithItsOwnName()
    {
        await using var session = SidecarSession.Start();
        var (docId, document) = await Samples.OpenAsync(session, "attachments-pdf-xlsx-jpg.eml");
        var directory = NewDirectory(session, "out");

        var result = SidecarSession.Result(await session.CallAsync("attachment.saveAll", new JsonObject { ["docId"] = docId, ["targetDir"] = directory }));

        Assert.Empty(result["failed"]!.AsArray());
        Assert.Equal(["budget.xlsx", "photo.jpg", "report.pdf"], Samples.EntryNames(directory));
        foreach (var saved in result["saved"]!.AsArray())
        {
            var attachmentId = (string)saved!["attachmentId"]!;
            var path = (string)saved["path"]!;
            Assert.Equal(directory, Path.GetDirectoryName(path));
            Assert.Equal(await Samples.ReadAttachmentWithCoreAsync("attachments-pdf-xlsx-jpg.eml", attachmentId), await File.ReadAllBytesAsync(path));
        }

        Assert.Equal(
            document["attachments"]!.AsArray().Select(a => (string)a!["id"]!).Order().ToArray(),
            result["saved"]!.AsArray().Select(s => (string)s!["attachmentId"]!).Order().ToArray());
    }

    [Fact]
    [Trait("Task", "4.3")]
    public async Task SaveAll_TwoAttachmentsWithTheSameName_GetNumberedNamesAndKeepTheirOwnContent()
    {
        await using var session = SidecarSession.Start();
        var (docId, document) = await Samples.OpenAsync(session, "attachments-duplicate-names.eml");
        var directory = NewDirectory(session, "out");

        var result = SidecarSession.Result(await session.CallAsync("attachment.saveAll", new JsonObject { ["docId"] = docId, ["targetDir"] = directory }));

        Assert.Equal(["report (1).pdf", "report.pdf"], Samples.EntryNames(directory));
        var ids = document["attachments"]!.AsArray().Select(a => (string)a!["id"]!).ToArray();
        var first = await Samples.ReadAttachmentWithCoreAsync("attachments-duplicate-names.eml", ids[0]);
        var second = await Samples.ReadAttachmentWithCoreAsync("attachments-duplicate-names.eml", ids[1]);
        Assert.NotEqual(first, second);
        Assert.Equal(first, await File.ReadAllBytesAsync(Path.Combine(directory, "report.pdf")));
        Assert.Equal(second, await File.ReadAllBytesAsync(Path.Combine(directory, "report (1).pdf")));
        Assert.Equal(2, result["saved"]!.AsArray().Count);
    }

    [Fact]
    [Trait("Task", "4.3")]
    public async Task SaveAll_NeverOverwritesExistingFiles()
    {
        await using var session = SidecarSession.Start();
        var (docId, _) = await Samples.OpenAsync(session, "attachments-duplicate-names.eml");
        var directory = NewDirectory(session, "out");
        await File.WriteAllTextAsync(Path.Combine(directory, "report.pdf"), "KEEP ME");
        await File.WriteAllTextAsync(Path.Combine(directory, "report (1).pdf"), "KEEP ME TOO");

        var result = SidecarSession.Result(await session.CallAsync("attachment.saveAll", new JsonObject { ["docId"] = docId, ["targetDir"] = directory }));

        Assert.Equal("KEEP ME", await File.ReadAllTextAsync(Path.Combine(directory, "report.pdf")));
        Assert.Equal("KEEP ME TOO", await File.ReadAllTextAsync(Path.Combine(directory, "report (1).pdf")));
        Assert.Equal(["report (1).pdf", "report (2).pdf", "report (3).pdf", "report.pdf"], Samples.EntryNames(directory));
        Assert.Empty(result["failed"]!.AsArray());
    }

    [Fact]
    [Trait("Task", "4.3")]
    public async Task SaveAll_RunTwice_ProducesAnotherSetOfNumberedFiles()
    {
        await using var session = SidecarSession.Start();
        var (docId, _) = await Samples.OpenAsync(session, "attachments-pdf-xlsx-jpg.eml");
        var directory = NewDirectory(session, "out");

        SidecarSession.Result(await session.CallAsync("attachment.saveAll", new JsonObject { ["docId"] = docId, ["targetDir"] = directory }));
        SidecarSession.Result(await session.CallAsync("attachment.saveAll", new JsonObject { ["docId"] = docId, ["targetDir"] = directory }));

        Assert.Equal(
            ["budget (1).xlsx", "budget.xlsx", "photo (1).jpg", "photo.jpg", "report (1).pdf", "report.pdf"],
            Samples.EntryNames(directory));
    }

    [Fact]
    [Trait("Task", "4.3")]
    public async Task SaveAll_HostileFileNames_StayInsideTheTargetFolder()
    {
        await using var session = SidecarSession.Start();
        var (docId, _) = await Samples.OpenAsync(session, "attachments-unsafe-names.eml");
        var directory = NewDirectory(session, "out");

        var result = SidecarSession.Result(await session.CallAsync("attachment.saveAll", new JsonObject { ["docId"] = docId, ["targetDir"] = directory }));

        Assert.Empty(result["failed"]!.AsArray());
        Assert.Equal(7, result["saved"]!.AsArray().Count);
        Assert.Equal(
            [".bashrc", "_CON.txt", "_NUL", "a_b_c_.txt", "absolute-path.txt", "evil.txt", "x_y_z_w.txt"],
            Samples.EntryNames(directory));
        foreach (var saved in result["saved"]!.AsArray())
        {
            Assert.Equal(directory, Path.GetDirectoryName((string)saved!["path"]!));
        }

        // 目标文件夹的上一级除了 out 和会话目录之外什么都没有多出来
        Assert.Equal(["out"], Samples.EntryNames(session.Temp.DirectoryPath, "sessions"));
    }

    [Fact]
    [Trait("Task", "4.3")]
    public async Task SaveAll_ExecutableAttachmentsAreSavedLikeAnyOther()
    {
        await using var session = SidecarSession.Start();
        var (docId, _) = await Samples.OpenAsync(session, "attachments-executables.eml");
        var directory = NewDirectory(session, "out");

        var result = SidecarSession.Result(await session.CallAsync("attachment.saveAll", new JsonObject { ["docId"] = docId, ["targetDir"] = directory }));

        Assert.Empty(result["failed"]!.AsArray());
        Assert.Equal(
            ["Tool.app", "install.sh", "installer.exe", "launcher.command", "run-me.bat", "setup.ps1"],
            Samples.EntryNames(directory));
    }

    [Fact]
    [Trait("Task", "4.3")]
    public async Task SaveAll_ADocumentWithoutAttachments_ReturnsEmptyLists()
    {
        await using var session = SidecarSession.Start();
        var (docId, _) = await Samples.OpenAsync(session, "headers-normal.eml");
        var directory = NewDirectory(session, "out");

        var result = SidecarSession.Result(await session.CallAsync("attachment.saveAll", new JsonObject { ["docId"] = docId, ["targetDir"] = directory }));

        Assert.Empty(result["saved"]!.AsArray());
        Assert.Empty(result["failed"]!.AsArray());
        Assert.Empty(Samples.EntryNames(directory));
    }

    [Fact]
    [Trait("Task", "4.3")]
    public async Task SaveAll_ToAFolderThatDoesNotExist_IsWriteFailed()
    {
        await using var session = SidecarSession.Start();
        var (docId, _) = await Samples.OpenAsync(session, "attachments-pdf-xlsx-jpg.eml");
        var missing = Path.Combine(session.Temp.DirectoryPath, "no-such-folder");

        var response = await session.CallAsync("attachment.saveAll", new JsonObject { ["docId"] = docId, ["targetDir"] = missing });

        Assert.Equal(ErrorCodes.WriteFailed, SidecarSession.ErrorCode(response));
        Assert.False(Directory.Exists(missing));
    }

    // ---- 下面直接测试写文件的类：模拟“取名之后被抢先创建”和单个附件写入失败 ----

    [Fact]
    [Trait("Task", "4.3")]
    public async Task SaveAll_WhenAnotherProcessCreatesTheSameNameAfterTheNameWasChosen_PicksANewNameAndKeepsTheOtherFile()
    {
        using var temp = new EmailFileViewer.Core.Tests.TempDirectory();
        var existing = Path.Combine(temp.DirectoryPath, "a.txt");
        await File.WriteAllTextAsync(existing, "WRITTEN BY SOMEONE ELSE");

        // 第一次问“a.txt 有没有被占用”时说谎（模拟取名时它还不存在）
        var asked = 0;
        var files = new AttachmentFiles(path => Path.GetFileName(path) == "a.txt" && asked++ == 0 ? false : File.Exists(path) || Directory.Exists(path));

        var result = await files.SaveAllAsync(
            temp.DirectoryPath,
            [new EmailAttachment("1", "a.txt", 3, "text/plain", false)],
            (_, stream, token) => stream.WriteAsync("new"u8.ToArray(), token).AsTask());

        Assert.Empty(result.Failed);
        Assert.Equal(Path.Combine(temp.DirectoryPath, "a (1).txt"), Assert.Single(result.Saved).Path);
        Assert.Equal("WRITTEN BY SOMEONE ELSE", await File.ReadAllTextAsync(existing));
        Assert.Equal("new", await File.ReadAllTextAsync(Path.Combine(temp.DirectoryPath, "a (1).txt")));
    }

    [Fact]
    [Trait("Task", "4.3")]
    public async Task SaveAll_OneAttachmentFailing_DoesNotAffectTheOthers_AndLeavesNoPartialFile()
    {
        using var temp = new EmailFileViewer.Core.Tests.TempDirectory();
        var files = new AttachmentFiles();
        EmailAttachment[] attachments =
        [
            new("1", "first.txt", 1, "text/plain", false),
            new("2", "disk-full.txt", 1, "text/plain", false),
            new("3", "broken.txt", 1, "text/plain", false),
            new("4", "last.txt", 1, "text/plain", false),
        ];

        var result = await files.SaveAllAsync(
            temp.DirectoryPath,
            attachments,
            async (id, stream, token) =>
            {
                await stream.WriteAsync("partial"u8.ToArray(), token);
                switch (id)
                {
                    case "2":
                        throw new IOException("There is not enough space on the disk.");
                    case "3":
                        throw new InvalidOperationException("unexpected");
                }
            });

        Assert.Equal(["1", "4"], result.Saved.Select(s => s.AttachmentId).ToArray());
        Assert.Equal(["2", "3"], result.Failed.Select(f => f.AttachmentId).ToArray());
        Assert.Equal(ErrorCodes.WriteFailed, result.Failed[0].Code);
        Assert.Equal(ErrorCodes.InternalError, result.Failed[1].Code);
        Assert.All(result.Failed, failure => Assert.False(string.IsNullOrWhiteSpace(failure.Message)));
        Assert.Equal(["first.txt", "last.txt"], Samples.EntryNames(temp.DirectoryPath));
    }

    [Fact]
    [Trait("Task", "4.3")]
    public async Task SaveAll_WhileAnAttachmentIsBeingWritten_TheFinalNameDoesNotExistYet()
    {
        using var temp = new EmailFileViewer.Core.Tests.TempDirectory();
        var files = new AttachmentFiles();
        string[] namesWhileWriting = [];
        bool finalNameExistedWhileWriting = true;

        // 写到一半时（相当于此刻进程被强行结束）：最终名字的文件不能存在，只能有以 .efv- 开头的临时文件
        var result = await files.SaveAllAsync(
            temp.DirectoryPath,
            [new EmailAttachment("1", "report.pdf", 12, "application/pdf", false)],
            async (_, stream, token) =>
            {
                await stream.WriteAsync("first half, "u8.ToArray(), token);
                await stream.FlushAsync(token);
                namesWhileWriting = Samples.EntryNames(temp.DirectoryPath);
                finalNameExistedWhileWriting = File.Exists(Path.Combine(temp.DirectoryPath, "report.pdf"));
                await stream.WriteAsync("second half"u8.ToArray(), token);
            });

        Assert.False(finalNameExistedWhileWriting);
        var temporary = Assert.Single(namesWhileWriting);
        Assert.StartsWith(".efv-", temporary);
        Assert.EndsWith(".tmp", temporary);

        Assert.Empty(result.Failed);
        Assert.Equal(["report.pdf"], Samples.EntryNames(temp.DirectoryPath));
        Assert.Equal("first half, second half", await File.ReadAllTextAsync(Path.Combine(temp.DirectoryPath, "report.pdf")));
    }

    [Fact]
    [Trait("Task", "4.3")]
    public async Task SaveAll_WhenCanceledHalfway_LeavesNeitherTheFinalFileNorATemporaryFile()
    {
        using var temp = new EmailFileViewer.Core.Tests.TempDirectory();
        using var cancellation = new CancellationTokenSource();
        var files = new AttachmentFiles();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => files.SaveAllAsync(
            temp.DirectoryPath,
            [new EmailAttachment("1", "report.pdf", 12, "application/pdf", false), new EmailAttachment("2", "photo.jpg", 12, "image/jpeg", false)],
            async (_, stream, token) =>
            {
                await stream.WriteAsync("some content"u8.ToArray(), token);
                await cancellation.CancelAsync();
                token.ThrowIfCancellationRequested();
            },
            cancellation.Token));

        Assert.Empty(Samples.EntryNames(temp.DirectoryPath));
    }

    [Fact]
    [Trait("Task", "4.3")]
    public async Task SaveAll_ExistingFilesAreNeverOverwritten_EvenWhenTheNameWasTakenWhileWriting()
    {
        using var temp = new EmailFileViewer.Core.Tests.TempDirectory();
        var files = new AttachmentFiles();
        var target = Path.Combine(temp.DirectoryPath, "a.txt");

        // 写入期间别的程序在最终名字上创建了文件：改名时发现被占用，重新取名，对方的文件原样保留
        var result = await files.SaveAllAsync(
            temp.DirectoryPath,
            [new EmailAttachment("1", "a.txt", 3, "text/plain", false)],
            async (_, stream, token) =>
            {
                await File.WriteAllTextAsync(target, "SOMEONE ELSE", token);
                await stream.WriteAsync("new"u8.ToArray(), token);
            });

        Assert.Empty(result.Failed);
        Assert.Equal(Path.Combine(temp.DirectoryPath, "a (1).txt"), Assert.Single(result.Saved).Path);
        Assert.Equal("SOMEONE ELSE", await File.ReadAllTextAsync(target));
        Assert.Equal("new", await File.ReadAllTextAsync(Path.Combine(temp.DirectoryPath, "a (1).txt")));
        Assert.Equal(["a (1).txt", "a.txt"], Samples.EntryNames(temp.DirectoryPath));
    }

    [Fact]
    [Trait("Task", "4.3")]
    public async Task Save_WhenWritingFailsHalfway_TheExistingTargetIsUntouchedAndNoTemporaryFileRemains()
    {
        using var temp = new EmailFileViewer.Core.Tests.TempDirectory();
        var target = Path.Combine(temp.DirectoryPath, "report.pdf");
        await File.WriteAllTextAsync(target, "PREVIOUS GOOD CONTENT");
        var files = new AttachmentFiles();

        await Assert.ThrowsAsync<IOException>(() => files.SaveAsync(
            target,
            async (stream, token) =>
            {
                await stream.WriteAsync("half of the new content"u8.ToArray(), token);
                throw new IOException("There is not enough space on the disk.");
            }));

        Assert.Equal("PREVIOUS GOOD CONTENT", await File.ReadAllTextAsync(target));
        Assert.Equal(["report.pdf"], Samples.EntryNames(temp.DirectoryPath));
    }

    [Fact]
    [Trait("Task", "4.3")]
    public async Task Save_WhenCanceledHalfway_TheTargetIsNotCreated()
    {
        using var temp = new EmailFileViewer.Core.Tests.TempDirectory();
        var target = Path.Combine(temp.DirectoryPath, "report.pdf");
        using var cancellation = new CancellationTokenSource();
        var files = new AttachmentFiles();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => files.SaveAsync(
            target,
            async (stream, token) =>
            {
                await stream.WriteAsync("some content"u8.ToArray(), token);
                await cancellation.CancelAsync();
                token.ThrowIfCancellationRequested();
            },
            cancellation.Token));

        Assert.Empty(Samples.EntryNames(temp.DirectoryPath));
    }

    private static string NewDirectory(SidecarSession session, string name)
    {
        return Directory.CreateDirectory(Path.Combine(session.Temp.DirectoryPath, name)).FullName;
    }
}
