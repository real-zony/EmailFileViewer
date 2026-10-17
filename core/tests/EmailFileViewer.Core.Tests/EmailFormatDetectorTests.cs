using System.Text;
using EmailFileViewer.Core.Parsing;

namespace EmailFileViewer.Core.Tests;

public class EmailFormatDetectorTests
{
    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

    [Fact]
    public void IsOleCompoundFile_OleSignature_ReturnsTrue()
    {
        byte[] header = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0x00];

        Assert.True(EmailFormatDetector.IsOleCompoundFile(header));
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0xD0, 0xCF, 0x11, 0xE0 })]
    [InlineData(new byte[] { 0x46, 0x72, 0x6F, 0x6D, 0x3A, 0x20, 0x61, 0x40 })]
    public void IsOleCompoundFile_OtherBytes_ReturnsFalse(byte[] header)
    {
        Assert.False(EmailFormatDetector.IsOleCompoundFile(header));
    }

    [Theory]
    [InlineData("From: a@example.com\r\n\r\nbody")]
    [InlineData("Subject:\r\n")]
    [InlineData("X-Custom-Header: value")]
    [InlineData("Received: from a\r\n by b")]
    [InlineData("\r\n\r\n  \r\n\t\r\nFrom: a@example.com")]
    [InlineData("From : a@example.com")]
    [InlineData("From - Wed Jul 22 01:02:29 2015\r\nX-Mozilla-Status: 0001\r\nFrom: a@example.com")]
    [InlineData("From MAILER-DAEMON Wed Jul 22 01:02:29 2015\nSubject: mbox style")]
    public void EnsureLooksLikeMime_HeaderFieldFirst_Accepts(string text)
    {
        EmailFormatDetector.EnsureLooksLikeMime(Ascii(text));
    }

    [Fact]
    public void EnsureLooksLikeMime_Utf8BomBeforeHeader_Accepts()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. Ascii("From: a@example.com")];

        EmailFormatDetector.EnsureLooksLikeMime(bytes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \r\n\t\r\n  ")]
    [InlineData("Hello world, this is just a text file.")]
    [InlineData("  From: a@example.com")]
    [InlineData("From alice@example.com Mon Oct  5 09:00:00 2026")]
    [InlineData("From alice@example.com Mon Oct  5 09:00:00 2026\r\nthis is not a header")]
    [InlineData(": value")]
    [InlineData("Fr om: x")]
    public void EnsureLooksLikeMime_NotAHeaderField_ThrowsUnsupportedFormat(string text)
    {
        var exception = Assert.Throws<EmailParseException>(() => EmailFormatDetector.EnsureLooksLikeMime(Ascii(text)));

        Assert.Equal(EmailParseErrorKind.UnsupportedFormat, exception.Kind);
    }

    [Fact]
    public void EnsureLooksLikeMime_NonAsciiFieldName_ThrowsUnsupportedFormat()
    {
        var bytes = Encoding.UTF8.GetBytes("发件人: a@example.com");

        var exception = Assert.Throws<EmailParseException>(() => EmailFormatDetector.EnsureLooksLikeMime(bytes));

        Assert.Equal(EmailParseErrorKind.UnsupportedFormat, exception.Kind);
    }

    [Fact]
    public void EnsureLooksLikeMime_NulWithinFirstKilobyte_ThrowsUnsupportedFormat()
    {
        var bytes = new byte[2048];
        Ascii("From: a@example.com\r\n").CopyTo(bytes, 0);
        Array.Fill(bytes, (byte)'x', 21, 2000);
        bytes[1000] = 0;

        var exception = Assert.Throws<EmailParseException>(() => EmailFormatDetector.EnsureLooksLikeMime(bytes));

        Assert.Equal(EmailParseErrorKind.UnsupportedFormat, exception.Kind);
    }

    [Fact]
    public void EnsureLooksLikeMime_NulAfterFirstKilobyte_Accepts()
    {
        var bytes = new byte[2048];
        Ascii("From: a@example.com\r\n").CopyTo(bytes, 0);
        Array.Fill(bytes, (byte)'x', 21, 2000);
        bytes[1500] = 0;

        EmailFormatDetector.EnsureLooksLikeMime(bytes);
    }
}
