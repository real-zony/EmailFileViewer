using System.IO.Compression;

namespace EmailFileViewer.Core.Tests;

/// <summary>
/// 对应规格 email-attachments 中的“展开 winmail.dat”。
/// </summary>
public class TnefTests
{
    [Fact]
    [Trait("Scenario", "email-attachments / 展开 winmail.dat / Outlook 发出的 winmail.dat")]
    public async Task WinmailDat_IsReplacedByTheFilesInsideIt()
    {
        using var parsed = await MailAssert.ParseSampleAsync("tnef-winmail-docx.eml");

        // 只有 docx：winmail.dat 本身和 TNEF 里没有文件名的正文部分都不列出
        var attachment = Assert.Single(parsed.Document.Attachments);
        Assert.Equal("meeting-minutes.docx", attachment.FileName);
        Assert.Equal("application/vnd.openxmlformats-officedocument.wordprocessingml.document", attachment.MimeType);
        Assert.False(attachment.IsNestedEmail);

        var bytes = await MailAssert.ReadAttachmentAsync(parsed, attachment.Id);
        Assert.Equal(attachment.Size, bytes.Length);
        using var zip = new ZipArchive(new MemoryStream(bytes));
        Assert.Contains(zip.Entries, entry => entry.FullName == "word/document.xml");
    }

    [Fact]
    [Trait("Scenario", "email-attachments / 展开 winmail.dat / 解出文件的 Id 稳定")]
    public async Task ExtractedFileId_IsStableAcrossParses()
    {
        using var first = await MailAssert.ParseSampleAsync("tnef-winmail-docx.eml");
        using var second = await MailAssert.ParseSampleAsync("tnef-winmail-docx.eml");

        Assert.Equal(first.Document.Attachments[0].Id, second.Document.Attachments[0].Id);
    }

    [Fact]
    [Trait("Scenario", "email-attachments / 展开 winmail.dat / 解不开的 winmail.dat 当作普通附件列出")]
    public async Task CorruptWinmailDat_IsListedAsOrdinaryAttachment()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteMail(
            "bad-tnef.eml",
            """
            From: a@example.com
            Subject: corrupt tnef
            MIME-Version: 1.0
            Content-Type: multipart/mixed; boundary="b"

            --b
            Content-Type: text/plain

            see attachment
            --b
            Content-Type: application/ms-tnef; name="winmail.dat"
            Content-Transfer-Encoding: base64

            AAECAwQFBgcICQoLDA0ODw==
            --b--
            """);

        using var parsed = await EmailParser.ParseFileAsync(path);

        var attachment = Assert.Single(parsed.Document.Attachments);
        Assert.Equal("winmail.dat", attachment.FileName);
        Assert.Equal(16, attachment.Size);
    }

    [Fact]
    [Trait("Scenario", "email-attachments / 展开 winmail.dat / 里面没有文件时保留 winmail.dat 本身")]
    public async Task WinmailDatWithoutAnyFile_IsKeptAsOrdinaryAttachment()
    {
        // 合法的 TNEF，但里面只有邮件正文，没有任何带文件名的附件：以前 winmail.dat 会整个消失
        var tnef = BuildBodyOnlyTnef();
        using var temp = new TempDirectory();
        var path = temp.WriteMail(
            "tnef-body-only.eml",
            $"""
            From: a@example.com
            Subject: tnef without files
            MIME-Version: 1.0
            Content-Type: multipart/mixed; boundary="b"

            --b
            Content-Type: text/plain

            see attachment
            --b
            Content-Type: application/ms-tnef; name="winmail.dat"
            Content-Disposition: attachment; filename="winmail.dat"
            Content-Transfer-Encoding: base64

            {Convert.ToBase64String(tnef, Base64FormattingOptions.InsertLineBreaks)}
            --b--
            """);

        using var parsed = await EmailParser.ParseFileAsync(path);

        var attachment = Assert.Single(parsed.Document.Attachments);
        Assert.Equal("winmail.dat", attachment.FileName);
        Assert.Equal(tnef.Length, attachment.Size);
        Assert.Equal(tnef, await MailAssert.ReadAttachmentAsync(parsed, attachment.Id));
    }

    // TNEF 签名 + 密钥 + 三条属性（消息类、版本、正文），没有任何附件
    private static byte[] BuildBodyOnlyTnef()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(0x223E9F78u);
        writer.Write((ushort)0x1234);

        void WriteAttribute(uint tag, byte[] data)
        {
            writer.Write((byte)1);
            writer.Write(tag);
            writer.Write((uint)data.Length);
            writer.Write(data);
            ushort checksum = 0;
            foreach (var b in data)
            {
                checksum += b;
            }

            writer.Write(checksum);
        }

        WriteAttribute(0x00089006u, BitConverter.GetBytes(0x00010000u));
        WriteAttribute(0x00078008u, "IPM.Note\0"u8.ToArray());
        WriteAttribute(0x0002800Cu, "hello from tnef body\0"u8.ToArray());
        writer.Flush();
        return stream.ToArray();
    }
}
