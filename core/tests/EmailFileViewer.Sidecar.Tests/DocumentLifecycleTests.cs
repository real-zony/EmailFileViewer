using System.Text.Json.Nodes;
using EmailFileViewer.Sidecar.Protocol;

namespace EmailFileViewer.Sidecar.Tests;

/// <summary>
/// 任务 4.2：<c>email.open</c>、<c>email.openNested</c>、<c>document.close</c>。
/// </summary>
public class DocumentLifecycleTests
{
    [Fact]
    [Trait("Task", "4.2")]
    public async Task Open_ReturnsADocIdAndTheDocument()
    {
        await using var session = SidecarSession.Start();

        var (docId, document) = await Samples.OpenAsync(session, "attachments-pdf-xlsx-jpg.eml");

        Assert.False(string.IsNullOrWhiteSpace(docId));
        Assert.Equal("Attachments: pdf, xlsx and jpg", (string)document["subject"]!);
        Assert.Equal(["report.pdf", "budget.xlsx", "photo.jpg"], document["attachments"]!.AsArray().Select(a => (string)a!["fileName"]!).ToArray());
        Assert.Equal("PlainText", (string)document["body"]!["sourceFormat"]!);
        Assert.False((bool)document["isSigned"]!);
        Assert.EndsWith("Z", (string)document["sentAtUtc"]!);
    }

    [Fact]
    [Trait("Task", "4.2")]
    public async Task Open_TheSameFileTwice_GivesTwoIndependentDocuments()
    {
        await using var session = SidecarSession.Start();
        var first = await Samples.OpenAsync(session, "attachments-pdf-xlsx-jpg.eml");
        var second = await Samples.OpenAsync(session, "attachments-pdf-xlsx-jpg.eml");

        Assert.NotEqual(first.DocId, second.DocId);

        SidecarSession.Result(await session.CallAsync("document.close", new JsonObject { ["docId"] = first.DocId }));
        var save = await session.CallAsync("attachment.extractForOpen", new JsonObject
        {
            ["docId"] = second.DocId,
            ["attachmentId"] = Samples.AttachmentId(second.Document, "photo.jpg"),
        });
        SidecarSession.Result(save);
    }

    [Theory]
    [InlineData("invalid/not-an-email.png", ErrorCodes.UnsupportedFormat)]
    [InlineData("invalid/png-disguised-as-eml.eml", ErrorCodes.UnsupportedFormat)]
    [InlineData("invalid/empty.eml", ErrorCodes.UnsupportedFormat)]
    [InlineData("invalid/truncated.msg", ErrorCodes.Corrupted)]
    [Trait("Task", "4.2")]
    public async Task Open_FailuresMapToTheThreeParseErrorKinds(string sampleName, string expectedCode)
    {
        await using var session = SidecarSession.Start();

        var response = await session.CallAsync("email.open", new JsonObject { ["path"] = EmailFileViewer.Core.Tests.SampleFiles.GetPath(sampleName) });

        Assert.Equal(expectedCode, SidecarSession.ErrorCode(response));
        Assert.False(string.IsNullOrWhiteSpace((string)response["error"]!["message"]!));
    }

    [Fact]
    [Trait("Task", "4.2")]
    public async Task Open_MissingFileOrDirectory_IsUnreadable()
    {
        await using var session = SidecarSession.Start();

        var missing = await session.CallAsync("email.open", new JsonObject { ["path"] = Path.Combine(session.Temp.DirectoryPath, "missing.eml") });
        var directory = await session.CallAsync("email.open", new JsonObject { ["path"] = session.Temp.DirectoryPath });

        Assert.Equal(ErrorCodes.Unreadable, SidecarSession.ErrorCode(missing));
        Assert.Equal(ErrorCodes.Unreadable, SidecarSession.ErrorCode(directory));
    }

    [Fact]
    [Trait("Task", "4.2")]
    public async Task Open_AFailedOpenDoesNotLeaveADocumentBehind()
    {
        await using var session = SidecarSession.Start();

        await session.CallAsync("email.open", new JsonObject { ["path"] = EmailFileViewer.Core.Tests.SampleFiles.GetPath("invalid/truncated.msg") });
        var (docId, _) = await Samples.OpenAsync(session, "headers-normal.eml");

        Assert.Equal("d1", docId);
    }

    [Fact]
    [Trait("Task", "4.2")]
    public async Task Close_ReturnsAnEmptyObject_AndTheDocumentIsGone()
    {
        await using var session = SidecarSession.Start();
        var (docId, document) = await Samples.OpenAsync(session, "attachments-pdf-xlsx-jpg.eml");
        var attachmentId = Samples.AttachmentId(document, "report.pdf");
        var target = Path.Combine(session.Temp.DirectoryPath, "report.pdf");

        var closed = SidecarSession.Result(await session.CallAsync("document.close", new JsonObject { ["docId"] = docId }));

        Assert.Empty(closed);

        var docParams = () => new JsonObject { ["docId"] = docId };
        Assert.Equal(ErrorCodes.DocumentNotFound, SidecarSession.ErrorCode(await session.CallAsync("document.close", docParams())));
        Assert.Equal(
            ErrorCodes.DocumentNotFound,
            SidecarSession.ErrorCode(await session.CallAsync("email.openNested", new JsonObject { ["docId"] = docId, ["attachmentId"] = attachmentId })));
        Assert.Equal(
            ErrorCodes.DocumentNotFound,
            SidecarSession.ErrorCode(await session.CallAsync("attachment.save", new JsonObject { ["docId"] = docId, ["attachmentId"] = attachmentId, ["targetPath"] = target })));
        Assert.Equal(
            ErrorCodes.DocumentNotFound,
            SidecarSession.ErrorCode(await session.CallAsync("attachment.saveAll", new JsonObject { ["docId"] = docId, ["targetDir"] = session.Temp.DirectoryPath })));
        Assert.Equal(
            ErrorCodes.DocumentNotFound,
            SidecarSession.ErrorCode(await session.CallAsync("attachment.extractForOpen", new JsonObject { ["docId"] = docId, ["attachmentId"] = attachmentId })));
        Assert.False(File.Exists(target));
    }

    [Fact]
    [Trait("Task", "4.2")]
    public async Task UnknownDocId_IsDocumentNotFound()
    {
        await using var session = SidecarSession.Start();

        var response = await session.CallAsync("document.close", new JsonObject { ["docId"] = "d999" });

        Assert.Equal(ErrorCodes.DocumentNotFound, SidecarSession.ErrorCode(response));
    }

    [Fact]
    [Trait("Task", "4.2")]
    public async Task OpenNested_Eml_TwoLevelsDeep_EachLevelIsItsOwnDocument()
    {
        await using var session = SidecarSession.Start();
        var outer = await Samples.OpenAsync(session, "nested-rfc822-two-levels.eml");
        Assert.Equal("Level 1 (outer) message with two nested levels", (string)outer.Document["subject"]!);

        var middle = await OpenNestedAsync(session, outer);
        Assert.Equal("Level 2 (middle) message", (string)middle.Document["subject"]!);

        var inner = await OpenNestedAsync(session, middle);
        Assert.Equal("Level 3 (innermost) message", (string)inner.Document["subject"]!);
        Assert.Equal(["level3-note.txt"], inner.Document["attachments"]!.AsArray().Select(a => (string)a!["fileName"]!).ToArray());

        Assert.Equal(3, new[] { outer.DocId, middle.DocId, inner.DocId }.Distinct().Count());
    }

    [Fact]
    [Trait("Task", "4.2")]
    public async Task OpenNested_Msg_TwoLevelsDeep()
    {
        await using var session = SidecarSession.Start();
        var outer = await Samples.OpenAsync(session, "nested-msg-two-levels.msg");

        var middle = await OpenNestedAsync(session, outer);
        var inner = await OpenNestedAsync(session, middle);

        Assert.Equal("Level 2 (middle) MSG", (string)middle.Document["subject"]!);
        Assert.Equal("Level 3 (innermost) MSG", (string)inner.Document["subject"]!);
        Assert.Equal("level3-note.txt", (string)inner.Document["attachments"]![0]!["fileName"]!);
    }

    [Fact]
    [Trait("Task", "4.2")]
    public async Task OpenNested_ClosingTheOuterDocumentDoesNotAffectTheNestedOne()
    {
        await using var session = SidecarSession.Start();
        var outer = await Samples.OpenAsync(session, "nested-rfc822-two-levels.eml");
        var middle = await OpenNestedAsync(session, outer);
        var inner = await OpenNestedAsync(session, middle);

        SidecarSession.Result(await session.CallAsync("document.close", new JsonObject { ["docId"] = outer.DocId }));
        SidecarSession.Result(await session.CallAsync("document.close", new JsonObject { ["docId"] = middle.DocId }));

        // 内层仍然可以使用
        var target = Path.Combine(session.Temp.DirectoryPath, "note.txt");
        SidecarSession.Result(await session.CallAsync("attachment.save", new JsonObject
        {
            ["docId"] = inner.DocId,
            ["attachmentId"] = Samples.AttachmentId(inner.Document, "level3-note.txt"),
            ["targetPath"] = target,
        }));
        Assert.Contains("innermost", await File.ReadAllTextAsync(target));

        SidecarSession.Result(await session.CallAsync("document.close", new JsonObject { ["docId"] = inner.DocId }));
        Assert.Equal(ErrorCodes.DocumentNotFound, SidecarSession.ErrorCode(await session.CallAsync("document.close", new JsonObject { ["docId"] = inner.DocId })));
    }

    [Fact]
    [Trait("Task", "4.2")]
    public async Task OpenNested_ClosingTheNestedDocumentDoesNotAffectTheOuterOne()
    {
        await using var session = SidecarSession.Start();
        var outer = await Samples.OpenAsync(session, "nested-rfc822-two-levels.eml");
        var middle = await OpenNestedAsync(session, outer);

        SidecarSession.Result(await session.CallAsync("document.close", new JsonObject { ["docId"] = middle.DocId }));

        // 外层还能再打开一次嵌套邮件，得到一个新的 docId
        var again = await OpenNestedAsync(session, outer);
        Assert.NotEqual(middle.DocId, again.DocId);
    }

    [Fact]
    [Trait("Task", "4.2")]
    public async Task OpenNested_Errors()
    {
        await using var session = SidecarSession.Start();
        var (docId, document) = await Samples.OpenAsync(session, "attachments-pdf-xlsx-jpg.eml");

        var notAttachment = await session.CallAsync("email.openNested", new JsonObject { ["docId"] = docId, ["attachmentId"] = "no-such-id" });
        var notEmail = await session.CallAsync("email.openNested", new JsonObject { ["docId"] = docId, ["attachmentId"] = Samples.AttachmentId(document, "report.pdf") });
        var noDocument = await session.CallAsync("email.openNested", new JsonObject { ["docId"] = "d404", ["attachmentId"] = "2" });

        Assert.Equal(ErrorCodes.AttachmentNotFound, SidecarSession.ErrorCode(notAttachment));
        Assert.Equal(ErrorCodes.NotNestedEmail, SidecarSession.ErrorCode(notEmail));
        Assert.Equal(ErrorCodes.DocumentNotFound, SidecarSession.ErrorCode(noDocument));
    }

    [Fact]
    [Trait("Task", "4.2")]
    public async Task OpenNested_AnAttachmentThatClaimsToBeEmailButIsNot_IsUnsupportedFormat()
    {
        await using var session = SidecarSession.Start();
        var (docId, document) = await Samples.OpenAsync(session, "TestWithAttachmentsAndInlines.eml");
        var claimed = document["attachments"]!.AsArray().First(a => (bool)a!["isNestedEmail"]!)!;

        var response = await session.CallAsync("email.openNested", new JsonObject { ["docId"] = docId, ["attachmentId"] = (string)claimed["id"]! });

        Assert.Equal(ErrorCodes.UnsupportedFormat, SidecarSession.ErrorCode(response));

        // 打开失败不影响外层文档
        SidecarSession.Result(await session.CallAsync("document.close", new JsonObject { ["docId"] = docId }));
    }

    private static async Task<(string DocId, JsonObject Document)> OpenNestedAsync(SidecarSession session, (string DocId, JsonObject Document) parent)
    {
        var nestedId = (string)parent.Document["attachments"]!.AsArray().Single(a => (bool)a!["isNestedEmail"]!)!["id"]!;
        var result = SidecarSession.Result(await session.CallAsync("email.openNested", new JsonObject { ["docId"] = parent.DocId, ["attachmentId"] = nestedId }));
        return ((string)result["docId"]!, result["document"]!.AsObject());
    }
}
