using System.Text;

namespace EmailFileViewer.Core.Tests;

/// <summary>
/// 对应规格 email-viewing 中的“字符集解码”。预期文字来自 <c>samples/README.md</c>，主题、名称和正文都要求完全一致。
/// 测试进程里没有自己注册代码页编码提供程序，能通过就说明 Core 自己完成了注册。
/// </summary>
public class CharsetDecodingTests
{
    private const string GbkBody =
        "您好，\n\n"
        + "这是一封用于测试字符集解码的邮件，正文使用 GBK 编码，并且在 Content-Type 中声明了 charset=gbk。\n\n"
        + "如果您能正常看到这段中文，没有出现乱码，说明解码正确。\n"
        + "常用字：你好世界，邮件查看器，附件，发件人，收件人。\n\n"
        + "此致\n敬礼\n";

    private const string Big5Body =
        "各位同仁大家好：\n\n"
        + "本週五下午三點將於第二會議室舉行專案進度報告，請各部門負責人準時出席，並事先準備好本月的工作成果與下個月的計畫。\n\n"
        + "會議重點包括：一、系統上線時程的調整；二、客戶回饋意見的整理；三、人力資源與預算的分配。\n"
        + "如果因為出差或其他原因無法參加，請在今天下班前回覆郵件，我們會安排代理人員協助說明。\n\n"
        + "謝謝大家的配合，祝工作順利。\n\n"
        + "行政部　敬上\n";

    [Fact]
    [Trait("Scenario", "email-viewing / 字符集解码 / GBK 编码的中文邮件")]
    public async Task Gbk_SubjectSenderAndBodyAreExact()
    {
        using var parsed = await MailAssert.ParseSampleAsync("charset-gbk.eml");
        var document = parsed.Document;

        Assert.Equal("测试邮件：GBK 编码的中文主题", document.Subject);
        MailAssert.AddressesEqual(document.From, ("张三", "zhang.san@example.com"));
        Assert.Equal(GbkBody, MailAssert.PlainTextOf(document));
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 字符集解码 / GB18030 编码的邮件（含四字节字符）")]
    public async Task Gb18030_FourByteCharactersAreDecoded()
    {
        using var parsed = await MailAssert.ParseSampleAsync("charset-gb18030.eml");

        Assert.Equal("GB18030 样本：包含四字节字符", parsed.Document.Subject);
        Assert.Equal(
            "GB18030 编码的正文，其中包含需要四字节表示的字符：𠮷（U+20BB7）和 𠀀（U+20000）。\n"
            + "同时也包含欧元符号 € 和普通汉字。\n",
            MailAssert.PlainTextOf(parsed.Document));
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 字符集解码 / 未声明字符集")]
    public async Task Big5WithoutDeclaredCharset_IsDetectedAndDecoded()
    {
        using var parsed = await MailAssert.ParseSampleAsync("charset-big5-undeclared.eml");

        Assert.Equal("公司內部公告：專案進度報告", parsed.Document.Subject);
        Assert.Equal(Big5Body, MailAssert.PlainTextOf(parsed.Document));
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 字符集解码 / ISO-2022-JP 编码的邮件")]
    public async Task Iso2022Jp_SubjectSenderAndBodyAreExact()
    {
        using var parsed = await MailAssert.ParseSampleAsync("charset-iso-2022-jp.eml");
        var document = parsed.Document;

        Assert.Equal("文字コードのテスト：ISO-2022-JP", document.Subject);
        MailAssert.AddressesEqual(document.From, ("山田太郎", "yamada.taro@example.org"));
        Assert.Equal(
            "お世話になっております。\n\n"
            + "これは文字コードの確認用メールです。本文は ISO-2022-JP で符号化されています。\n"
            + "日本語が正しく表示されていれば、デコードは成功です。\n\n"
            + "よろしくお願いいたします。\n",
            MailAssert.PlainTextOf(document));
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 字符集解码 / Shift_JIS 编码的邮件")]
    public async Task ShiftJis_SubjectAndBodyAreExact()
    {
        using var parsed = await MailAssert.ParseSampleAsync("charset-shift-jis.eml");

        Assert.Equal("文字コードのテスト：Shift_JIS", parsed.Document.Subject);
        Assert.Equal(
            "お疲れ様です。\n\n"
            + "このメールの本文は Shift_JIS で符号化されています。\n"
            + "漢字、ひらがな、カタカナ（テスト）が正しく表示されることを確認してください。\n\n"
            + "以上、よろしくお願いします。\n",
            MailAssert.PlainTextOf(parsed.Document));
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 字符集解码 / Thunderbird 发出的真实 ISO-2022-JP 邮件（主题拆成两个编码字）")]
    public async Task Iso2022JpFromThunderbird_SubjectAndBodyAreExact()
    {
        using var parsed = await MailAssert.ParseSampleAsync("charset-iso-2022-jp-thunderbird.eml");

        Assert.Equal("日本語メールテスト (testing Japanese emails)", parsed.Document.Subject);
        Assert.Equal(
            "Let's see if both subject and body works fine...\n\n日本語が\n正常に\n送れているか\nテスト.\n",
            MailAssert.PlainTextOf(parsed.Document));
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 字符集解码 / RFC 2047 编码的邮件头")]
    public async Task Rfc2047Headers_SubjectSenderAndRecipientAreExact()
    {
        using var parsed = await MailAssert.ParseSampleAsync("headers-rfc2047.eml");
        var document = parsed.Document;

        Assert.Equal("月度报告 Report （含附件 — résumé）", document.Subject);
        MailAssert.AddressesEqual(
            document.From,
            ("李 Li 明 (Ming)", "li.ming@example.com"));
        MailAssert.AddressesEqual(
            document.To,
            ("Müller 先生", "mueller@example.org"),
            ("Carol Wang", "carol.wang@example.net"));
        Assert.Equal(
            "Expected subject: 月度报告 Report （含附件 — résumé）\n"
            + "Expected sender name: 李 Li 明 (Ming)\n"
            + "Expected first recipient name: Müller 先生\n",
            MailAssert.PlainTextOf(document));
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 字符集解码 / 字符集声明为 us-ascii 但内容是 GBK")]
    public async Task DeclaredUsAsciiButGbkContent_IsDetected()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteBytes("mislabeled-ascii.eml", RelabelGbkSample("charset=\"gbk\"", "charset=\"us-ascii\""));

        using var parsed = await EmailParser.ParseFileAsync(path);

        Assert.Equal(GbkBody, MailAssert.PlainTextOf(parsed.Document));
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 字符集解码 / 字符集声明为 utf-8 但内容是 GBK")]
    public async Task DeclaredUtf8ButGbkContent_IsDetected()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteBytes("mislabeled-utf8.eml", RelabelGbkSample("charset=\"gbk\"", "charset=\"utf-8\""));

        using var parsed = await EmailParser.ParseFileAsync(path);

        Assert.Equal(GbkBody, MailAssert.PlainTextOf(parsed.Document));
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 字符集解码 / 字符集声明为 iso-8859-1 但内容是 UTF-8")]
    public async Task DeclaredLatin1ButUtf8Content_IsDecodedAsUtf8()
    {
        var bytes = File.ReadAllBytes(SampleFiles.GetPath("headers-rfc2047.eml"));
        var relabeled = Replace(bytes, "charset=utf-8", "charset=iso-8859-1");
        using var temp = new TempDirectory();
        var path = temp.WriteBytes("mislabeled-latin1.eml", relabeled);

        using var parsed = await EmailParser.ParseFileAsync(path);

        Assert.Contains("月度报告 Report （含附件 — résumé）", MailAssert.PlainTextOf(parsed.Document));
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 字符集解码 / 字符集声明为 iso-8859-1 且内容确实是 Latin-1 时不改动")]
    public async Task DeclaredLatin1WithLatin1Content_IsKept()
    {
        // é 在 Latin-1 里是单个字节 0xE9，不是合法的 UTF-8 序列
        byte[] body = [.. "Caf"u8.ToArray(), 0xE9, .. " au lait"u8.ToArray()];
        var header = Encoding.ASCII.GetBytes("From: a@example.com\r\nContent-Type: text/plain; charset=iso-8859-1\r\nContent-Transfer-Encoding: 8bit\r\n\r\n");
        using var temp = new TempDirectory();
        var path = temp.WriteBytes("latin1.eml", [.. header, .. body]);

        using var parsed = await EmailParser.ParseFileAsync(path);

        Assert.Equal("Café au lait", MailAssert.PlainTextOf(parsed.Document));
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 字符集解码 / 未声明字符集的 UTF-8 邮件")]
    public async Task Utf8WithoutDeclaredCharset_IsDecodedAsUtf8()
    {
        var header = Encoding.ASCII.GetBytes("From: a@example.com\r\nContent-Type: text/plain\r\nContent-Transfer-Encoding: 8bit\r\n\r\n");
        using var temp = new TempDirectory();
        var path = temp.WriteBytes("utf8-undeclared.eml", [.. header, .. Encoding.UTF8.GetBytes("中文 résumé")]);

        using var parsed = await EmailParser.ParseFileAsync(path);

        Assert.Equal("中文 résumé", MailAssert.PlainTextOf(parsed.Document));
    }

    // 把 GBK 样本头部的 charset 声明改掉，正文字节不变。用 Latin-1 做字节和字符的一一对应，不依赖代码页编码
    private static byte[] RelabelGbkSample(string oldText, string newText)
    {
        var bytes = File.ReadAllBytes(SampleFiles.GetPath("charset-gbk.eml"));
        return Replace(bytes, oldText, newText);
    }

    private static byte[] Replace(byte[] bytes, string oldText, string newText)
    {
        var text = Encoding.Latin1.GetString(bytes);
        Assert.Contains(oldText, text);
        return Encoding.Latin1.GetBytes(text.Replace(oldText, newText, StringComparison.Ordinal));
    }
}
