using System.Text.Json.Nodes;
using EmailFileViewer.Sidecar.Protocol;

namespace EmailFileViewer.Sidecar.Tests;

/// <summary>
/// 任务 4.3：<c>attachment.save</c>——保存到用户选的完整路径，字节与原附件一致，覆盖已有文件。
/// </summary>
public class AttachmentSaveTests
{
    [Theory]
    [InlineData("report.pdf")]
    [InlineData("budget.xlsx")]
    [InlineData("photo.jpg")]
    [Trait("Task", "4.3")]
    public async Task Save_WritesTheAttachmentByteForByte(string fileName)
    {
        await using var session = SidecarSession.Start();
        var (docId, document) = await Samples.OpenAsync(session, "attachments-pdf-xlsx-jpg.eml");
        var attachmentId = Samples.AttachmentId(document, fileName);
        var target = Path.Combine(session.Temp.DirectoryPath, "saved-" + fileName);

        var result = SidecarSession.Result(await session.CallAsync("attachment.save", new JsonObject
        {
            ["docId"] = docId,
            ["attachmentId"] = attachmentId,
            ["targetPath"] = target,
        }));

        Assert.Equal(Path.GetFullPath(target), (string)result["path"]!);
        var expected = await Samples.ReadAttachmentWithCoreAsync("attachments-pdf-xlsx-jpg.eml", attachmentId);
        Assert.Equal(expected, await File.ReadAllBytesAsync(target));
        Assert.Equal((long)document["attachments"]!.AsArray().Single(a => (string)a!["id"]! == attachmentId)!["size"]!, expected.Length);
    }

    [Fact]
    [Trait("Task", "4.3")]
    public async Task Save_TheJpegIsIdenticalToTheOriginalImageInTheSampleLibrary()
    {
        await using var session = SidecarSession.Start();
        var (docId, document) = await Samples.OpenAsync(session, "attachments-pdf-xlsx-jpg.eml");
        var target = Path.Combine(session.Temp.DirectoryPath, "photo.jpg");

        SidecarSession.Result(await session.CallAsync("attachment.save", new JsonObject
        {
            ["docId"] = docId,
            ["attachmentId"] = Samples.AttachmentId(document, "photo.jpg"),
            ["targetPath"] = target,
        }));

        var original = await File.ReadAllBytesAsync(EmailFileViewer.Core.Tests.SampleFiles.GetPath("assets/photo.jpg"));
        Assert.Equal(original, await File.ReadAllBytesAsync(target));
    }

    [Fact]
    [Trait("Task", "4.3")]
    public async Task Save_UsesTheUsersChosenFileName_WithoutSanitizingIt()
    {
        await using var session = SidecarSession.Start();
        var (docId, document) = await Samples.OpenAsync(session, "attachments-pdf-xlsx-jpg.eml");
        var target = Path.Combine(session.Temp.DirectoryPath, "我的 报告 (最终版).bin");

        var result = SidecarSession.Result(await session.CallAsync("attachment.save", new JsonObject
        {
            ["docId"] = docId,
            ["attachmentId"] = Samples.AttachmentId(document, "report.pdf"),
            ["targetPath"] = target,
        }));

        Assert.Equal(target, (string)result["path"]!);
        Assert.True(File.Exists(target));
    }

    [Fact]
    [Trait("Task", "4.3")]
    public async Task Save_OverwritesAnExistingFile_BecauseTheSystemDialogAlreadyConfirmed()
    {
        await using var session = SidecarSession.Start();
        var (docId, document) = await Samples.OpenAsync(session, "attachments-pdf-xlsx-jpg.eml");
        var attachmentId = Samples.AttachmentId(document, "report.pdf");
        var target = Path.Combine(session.Temp.DirectoryPath, "report.pdf");
        await File.WriteAllTextAsync(target, "OLD CONTENT THAT IS LONGER THAN NOTHING");

        SidecarSession.Result(await session.CallAsync("attachment.save", new JsonObject { ["docId"] = docId, ["attachmentId"] = attachmentId, ["targetPath"] = target }));

        Assert.Equal(await Samples.ReadAttachmentWithCoreAsync("attachments-pdf-xlsx-jpg.eml", attachmentId), await File.ReadAllBytesAsync(target));
    }

    [Fact]
    [Trait("Task", "4.3")]
    public async Task Save_LeavesNoTemporaryFileBehind()
    {
        await using var session = SidecarSession.Start();
        var (docId, document) = await Samples.OpenAsync(session, "attachments-pdf-xlsx-jpg.eml");
        var directory = Directory.CreateDirectory(Path.Combine(session.Temp.DirectoryPath, "out")).FullName;

        SidecarSession.Result(await session.CallAsync("attachment.save", new JsonObject
        {
            ["docId"] = docId,
            ["attachmentId"] = Samples.AttachmentId(document, "report.pdf"),
            ["targetPath"] = Path.Combine(directory, "report.pdf"),
        }));

        Assert.Equal(["report.pdf"], Samples.EntryNames(directory));
    }

    [Fact]
    [Trait("Task", "4.3")]
    public async Task Save_ToAFolderThatDoesNotExist_IsWriteFailed_AndCreatesNothing()
    {
        await using var session = SidecarSession.Start();
        var (docId, document) = await Samples.OpenAsync(session, "attachments-pdf-xlsx-jpg.eml");
        var missing = Path.Combine(session.Temp.DirectoryPath, "no-such-folder");

        var response = await session.CallAsync("attachment.save", new JsonObject
        {
            ["docId"] = docId,
            ["attachmentId"] = Samples.AttachmentId(document, "report.pdf"),
            ["targetPath"] = Path.Combine(missing, "report.pdf"),
        });

        Assert.Equal(ErrorCodes.WriteFailed, SidecarSession.ErrorCode(response));
        Assert.False(Directory.Exists(missing));
    }

    [Fact]
    [Trait("Task", "4.3")]
    public async Task Save_OntoAnExistingFolder_IsWriteFailed_AndTheFolderIsUntouched()
    {
        await using var session = SidecarSession.Start();
        var (docId, document) = await Samples.OpenAsync(session, "attachments-pdf-xlsx-jpg.eml");
        var folder = Directory.CreateDirectory(Path.Combine(session.Temp.DirectoryPath, "taken")).FullName;
        await File.WriteAllTextAsync(Path.Combine(folder, "keep.txt"), "keep");

        var response = await session.CallAsync("attachment.save", new JsonObject
        {
            ["docId"] = docId,
            ["attachmentId"] = Samples.AttachmentId(document, "report.pdf"),
            ["targetPath"] = folder,
        });

        Assert.Equal(ErrorCodes.WriteFailed, SidecarSession.ErrorCode(response));
        Assert.Equal(["keep.txt"], Samples.EntryNames(folder));
        Assert.Equal(["taken"], Samples.EntryNames(session.Temp.DirectoryPath, "sessions"));
    }

    [Fact]
    [Trait("Task", "4.3")]
    public async Task Save_UnknownAttachment_IsAttachmentNotFound_AndWritesNothing()
    {
        await using var session = SidecarSession.Start();
        var (docId, _) = await Samples.OpenAsync(session, "attachments-pdf-xlsx-jpg.eml");
        var target = Path.Combine(session.Temp.DirectoryPath, "x.bin");

        var response = await session.CallAsync("attachment.save", new JsonObject { ["docId"] = docId, ["attachmentId"] = "999", ["targetPath"] = target });

        Assert.Equal(ErrorCodes.AttachmentNotFound, SidecarSession.ErrorCode(response));
        Assert.Empty(Samples.EntryNames(session.Temp.DirectoryPath, "sessions"));
    }

    [Fact]
    [Trait("Task", "4.3")]
    public async Task Save_AllowsExecutableAttachments_OnlyOpeningThemIsRefused()
    {
        await using var session = SidecarSession.Start();
        var (docId, document) = await Samples.OpenAsync(session, "attachments-executables.eml");
        var target = Path.Combine(session.Temp.DirectoryPath, "installer.exe");

        SidecarSession.Result(await session.CallAsync("attachment.save", new JsonObject
        {
            ["docId"] = docId,
            ["attachmentId"] = Samples.AttachmentId(document, "installer.exe"),
            ["targetPath"] = target,
        }));

        Assert.True(File.Exists(target));
    }

    [Fact]
    [Trait("Task", "4.3")]
    public async Task Save_ANestedEmailAttachment_WritesAFileThatOpensAsEmail()
    {
        await using var session = SidecarSession.Start();
        var (docId, document) = await Samples.OpenAsync(session, "attachment-eml-file.eml");
        var attachmentId = Samples.AttachmentId(document, "forwarded-as-file.eml");
        var target = Path.Combine(session.Temp.DirectoryPath, "forwarded.eml");

        SidecarSession.Result(await session.CallAsync("attachment.save", new JsonObject { ["docId"] = docId, ["attachmentId"] = attachmentId, ["targetPath"] = target }));
        var reopened = SidecarSession.Result(await session.CallAsync("email.open", new JsonObject { ["path"] = target }));

        Assert.Equal("Original message that was forwarded as an attachment", (string)reopened["document"]!["subject"]!);
    }
}
