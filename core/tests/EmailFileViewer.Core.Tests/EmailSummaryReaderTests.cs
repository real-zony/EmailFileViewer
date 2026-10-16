namespace EmailFileViewer.Core.Tests;

public class EmailSummaryReaderTests
{
    [Fact]
    public void IsOleCompoundFile_OleSignature_ReturnsTrue()
    {
        byte[] header = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0x00];

        Assert.True(EmailSummaryReader.IsOleCompoundFile(header));
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0xD0, 0xCF, 0x11, 0xE0 })]
    [InlineData(new byte[] { 0x46, 0x72, 0x6F, 0x6D, 0x3A, 0x20, 0x61, 0x40 })]
    public void IsOleCompoundFile_OtherBytes_ReturnsFalse(byte[] header)
    {
        Assert.False(EmailSummaryReader.IsOleCompoundFile(header));
    }

    [Theory]
    [InlineData("simple-multipart.eml", "this is a simple multipart message", 1)]
    [InlineData("EmailWithMultilineSubject.eml", "One Two Three Four Five Six Seven Eight Nine Ten Eleven Twelve Thirteen Fourteen Fifteen", 0)]
    public async Task ReadAsync_Eml_ReturnsSubjectAndAttachmentCount(string fileName, string expectedSubject, int expectedAttachmentCount)
    {
        var summary = await EmailSummaryReader.ReadAsync(SampleFiles.GetPath(fileName));

        Assert.Equal(expectedSubject, summary.Subject);
        Assert.Equal(expectedAttachmentCount, summary.AttachmentCount);
    }

    [Theory]
    [InlineData("HtmlSampleEmailWithAttachment.msg", "HtmlSampleEmailWithAttachment", 1)]
    [InlineData("EmailWithMsgAttachment.msg", "FW: Msg Attachment Test", 1)]
    [InlineData("RtfSampleEmail.msg", "RtfSampleEmail", 0)]
    public async Task ReadAsync_Msg_ReturnsSubjectAndAttachmentCount(string fileName, string expectedSubject, int expectedAttachmentCount)
    {
        var summary = await EmailSummaryReader.ReadAsync(SampleFiles.GetPath(fileName));

        Assert.Equal(expectedSubject, summary.Subject);
        Assert.Equal(expectedAttachmentCount, summary.AttachmentCount);
    }
}
