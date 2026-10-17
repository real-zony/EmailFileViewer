using System.Text;

namespace EmailFileViewer.Core.Tests;

/// <summary>
/// 对应规格 email-attachments 中的“打开嵌套邮件”：message/rfc822、MSG 嵌入的 MSG、作为附件的 .eml / .msg 文件，以及多层嵌套。
/// </summary>
public class NestedEmailTests
{
    [Fact]
    [Trait("Scenario", "email-attachments / 打开嵌套邮件 / EML 中附带另一封邮件（message/rfc822）")]
    public async Task Rfc822Part_IsMarkedNestedAndOpens()
    {
        using var parsed = await MailAssert.ParseSampleAsync("simple-embedded-message.eml");

        var attachment = Assert.Single(parsed.Document.Attachments);
        Assert.True(attachment.IsNestedEmail);
        Assert.Equal("message/rfc822", attachment.MimeType);

        using var nested = await parsed.OpenNestedEmailAsync(attachment.Id);
        Assert.Equal("embedded message", nested.Document.Subject);
    }

    [Fact]
    [Trait("Scenario", "email-attachments / 打开嵌套邮件 / 多层嵌套（message/rfc822 两层）")]
    public async Task Rfc822TwoLevels_CanBeOpenedLevelByLevel()
    {
        using var outer = await MailAssert.ParseSampleAsync("nested-rfc822-two-levels.eml");
        Assert.Equal("Level 1 (outer) message with two nested levels", outer.Document.Subject);

        using var middle = await outer.OpenNestedEmailAsync(Assert.Single(outer.Document.Attachments).Id);
        Assert.Equal("Level 2 (middle) message", middle.Document.Subject);

        using var inner = await middle.OpenNestedEmailAsync(Assert.Single(middle.Document.Attachments).Id);
        Assert.Equal("Level 3 (innermost) message", inner.Document.Subject);

        // 最内层带一个普通附件，不是嵌套邮件
        var note = Assert.Single(inner.Document.Attachments);
        Assert.Equal("level3-note.txt", note.FileName);
        Assert.False(note.IsNestedEmail);
        Assert.Equal(note.Size, (await MailAssert.ReadAttachmentAsync(inner, note.Id)).Length);
    }

    [Fact]
    [Trait("Scenario", "email-attachments / 打开嵌套邮件 / 嵌套邮件是独立的，外层释放后仍可使用")]
    public async Task NestedEmail_StaysUsableAfterTheOuterEmailIsDisposed()
    {
        var outer = await MailAssert.ParseSampleAsync("nested-rfc822-two-levels.eml");
        using var middle = await outer.OpenNestedEmailAsync(Assert.Single(outer.Document.Attachments).Id);
        outer.Dispose();

        using var inner = await middle.OpenNestedEmailAsync(Assert.Single(middle.Document.Attachments).Id);

        Assert.Equal("Level 3 (innermost) message", inner.Document.Subject);
    }

    [Fact]
    [Trait("Scenario", "email-attachments / 打开嵌套邮件 / 写出的嵌套邮件字节数等于列表里的大小，并且能重新解析")]
    public async Task WrittenNestedEmail_HasListedSizeAndParsesAgain()
    {
        using var parsed = await MailAssert.ParseSampleAsync("nested-rfc822-two-levels.eml");
        var attachment = Assert.Single(parsed.Document.Attachments);

        var bytes = await MailAssert.ReadAttachmentAsync(parsed, attachment.Id);

        Assert.Equal(attachment.Size, bytes.Length);
        Assert.Contains("\r\n", Encoding.UTF8.GetString(bytes));
        using var temp = new TempDirectory();
        using var reparsed = await EmailParser.ParseFileAsync(temp.WriteBytes("nested.eml", bytes));
        Assert.Equal("Level 2 (middle) message", reparsed.Document.Subject);
    }

    [Fact]
    [Trait("Scenario", "email-attachments / 打开嵌套邮件 / 作为附件的 .eml 文件")]
    public async Task EmlFilesAttached_AreNestedWhetherOrNotTheTypeSaysRfc822()
    {
        using var parsed = await MailAssert.ParseSampleAsync("attachment-eml-file.eml");

        Assert.Equal(["forwarded-as-file.eml", "forwarded-as-rfc822.eml"], parsed.Document.Attachments.Select(a => a.FileName));
        Assert.All(parsed.Document.Attachments, attachment => Assert.True(attachment.IsNestedEmail));

        foreach (var attachment in parsed.Document.Attachments)
        {
            using var nested = await parsed.OpenNestedEmailAsync(attachment.Id);
            Assert.Equal("Original message that was forwarded as an attachment", nested.Document.Subject);
        }
    }

    [Fact]
    [Trait("Scenario", "email-attachments / 打开嵌套邮件 / 作为附件的 .msg 文件（EML 里附带 MSG）")]
    public async Task MsgFileAttachedToEml_IsNestedAndOpensAsMsg()
    {
        using var parsed = await MailAssert.ParseSampleAsync("attachment-msg-file.eml");

        var attachment = Assert.Single(parsed.Document.Attachments);
        Assert.Equal("forwarded-message.msg", attachment.FileName);
        Assert.True(attachment.IsNestedEmail);

        using var nested = await parsed.OpenNestedEmailAsync(attachment.Id);
        Assert.NotNull(nested.MsgMessage);
        Assert.Equal("RTF-only body sample", nested.Document.Subject);
    }

    [Fact]
    [Trait("Scenario", "email-attachments / 打开嵌套邮件 / MSG 中附带另一封 MSG")]
    public async Task MsgEmbeddedInMsg_IsNestedAndOpens()
    {
        using var parsed = await MailAssert.ParseSampleAsync("EmailWithMsgAttachment.msg");

        var attachment = Assert.Single(parsed.Document.Attachments);
        Assert.Equal("Msg Attachment Test.msg", attachment.FileName);
        Assert.True(attachment.IsNestedEmail);

        using var nested = await parsed.OpenNestedEmailAsync(attachment.Id);
        Assert.Equal("Msg Attachment Test", nested.Document.Subject);
    }

    [Fact]
    [Trait("Scenario", "email-attachments / 打开嵌套邮件 / MSG 外层有普通附件，内层邮件也有附件")]
    public async Task OuterAndInnerMsg_EachListTheirOwnAttachments()
    {
        using var outer = await MailAssert.ParseSampleAsync("EmailWithInnerMailAndAttachments.msg");

        Assert.Equal(["OUTER 1.pdf", "OUTER 2.pdf", "Inner mail.msg"], outer.Document.Attachments.Select(a => a.FileName));
        Assert.Equal([false, false, true], outer.Document.Attachments.Select(a => a.IsNestedEmail));

        using var inner = await outer.OpenNestedEmailAsync(MailAssert.AttachmentNamed(outer.Document, "Inner mail.msg").Id);
        Assert.Equal("Inner mail", inner.Document.Subject);
        Assert.Equal(["INNER 1.pdf", "INNER 2.pdf"], inner.Document.Attachments.Select(a => a.FileName));

        var pdf = await MailAssert.ReadAttachmentAsync(inner, inner.Document.Attachments[0].Id);
        Assert.Equal(inner.Document.Attachments[0].Size, pdf.Length);
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(pdf, 0, 5));
    }

    [Fact]
    [Trait("Scenario", "email-attachments / 打开嵌套邮件 / 多层嵌套（MSG 两层）")]
    public async Task MsgTwoLevels_CanBeOpenedLevelByLevel()
    {
        using var level1 = await MailAssert.ParseSampleAsync("nested-msg-two-levels.msg");
        using var level2 = await level1.OpenNestedEmailAsync(Assert.Single(level1.Document.Attachments).Id);
        Assert.Equal("Level 2 (middle) MSG", level2.Document.Subject);

        using var level3 = await level2.OpenNestedEmailAsync(Assert.Single(level2.Document.Attachments).Id);
        Assert.Equal("Level 3 (innermost) MSG", level3.Document.Subject);

        var note = Assert.Single(level3.Document.Attachments);
        Assert.Equal("level3-note.txt", note.FileName);
        Assert.Equal(note.Size, (await MailAssert.ReadAttachmentAsync(level3, note.Id)).Length);
    }

    [Fact]
    [Trait("Scenario", "email-attachments / 打开嵌套邮件 / 声明为邮件但内容不是邮件时报不支持的格式")]
    public async Task Rfc822PartWithGarbageContent_OpenReportsUnsupportedFormat()
    {
        using var parsed = await MailAssert.ParseSampleAsync("TestWithAttachmentsAndInlines.eml");
        var attachment = parsed.Document.Attachments.First(a => a.IsNestedEmail);

        var exception = await Assert.ThrowsAsync<EmailParseException>(() => parsed.OpenNestedEmailAsync(attachment.Id));

        Assert.Equal(EmailParseErrorKind.UnsupportedFormat, exception.Kind);
    }

    [Fact]
    [Trait("Scenario", "email-attachments / 打开嵌套邮件 / 附件不是邮件")]
    public async Task OpeningANonEmailAttachment_Throws()
    {
        using var parsed = await MailAssert.ParseSampleAsync("attachments-pdf-xlsx-jpg.eml");
        var pdf = MailAssert.AttachmentNamed(parsed.Document, "report.pdf");

        var exception = await Assert.ThrowsAsync<AttachmentNotNestedEmailException>(() => parsed.OpenNestedEmailAsync(pdf.Id));

        Assert.Equal(pdf.Id, exception.AttachmentId);
    }

    [Fact]
    [Trait("Scenario", "email-attachments / 打开嵌套邮件 / 附件 Id 不存在")]
    public async Task OpeningAnUnknownId_Throws()
    {
        using var parsed = await MailAssert.ParseSampleAsync("nested-rfc822-two-levels.eml");

        await Assert.ThrowsAsync<AttachmentNotFoundException>(() => parsed.OpenNestedEmailAsync("99"));
    }

    [Fact]
    [Trait("Scenario", "email-attachments / 打开嵌套邮件 / 邮件已释放")]
    public async Task OpeningAfterDispose_Throws()
    {
        var parsed = await MailAssert.ParseSampleAsync("nested-rfc822-two-levels.eml");
        var id = Assert.Single(parsed.Document.Attachments).Id;
        parsed.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => parsed.OpenNestedEmailAsync(id));
    }

    [Fact]
    [Trait("Scenario", "email-attachments / 打开嵌套邮件 / 取消")]
    public async Task OpeningWithCanceledToken_Throws()
    {
        using var parsed = await MailAssert.ParseSampleAsync("nested-rfc822-two-levels.eml");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => parsed.OpenNestedEmailAsync(Assert.Single(parsed.Document.Attachments).Id, cancellation.Token));
    }
}
