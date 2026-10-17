using System.Text;

namespace EmailFileViewer.SampleGenerator;

/// <summary>
/// 字符集样本。这些邮件需要精确控制字节：声明的字符集、RFC 2047 编码字、是否声明字符集，
/// MimeKit 的构造 API 会自动选择编码，所以这里逐行手写 MIME 报文。
/// </summary>
internal static class CharsetSamples
{
    // 每个样本的预期解码结果。生成器会在写文件后重新解析，和这里的文字逐字比较
    public const string GbkSubject = "测试邮件：GBK 编码的中文主题";

    public const string GbkFromName = "张三";

    public const string GbkBody =
        "您好，\n\n"
        + "这是一封用于测试字符集解码的邮件，正文使用 GBK 编码，并且在 Content-Type 中声明了 charset=gbk。\n\n"
        + "如果您能正常看到这段中文，没有出现乱码，说明解码正确。\n"
        + "常用字：你好世界，邮件查看器，附件，发件人，收件人。\n\n"
        + "此致\n敬礼\n";

    public const string Gb18030Subject = "GB18030 样本：包含四字节字符";

    public const string Gb18030Body =
        "GB18030 编码的正文，其中包含需要四字节表示的字符：𠮷（U+20BB7）和 𠀀（U+20000）。\n"
        + "同时也包含欧元符号 € 和普通汉字。\n";

    public const string Big5Subject = "公司內部公告：專案進度報告";

    public const string Big5Body =
        "各位同仁大家好：\n\n"
        + "本週五下午三點將於第二會議室舉行專案進度報告，請各部門負責人準時出席，並事先準備好本月的工作成果與下個月的計畫。\n\n"
        + "會議重點包括：一、系統上線時程的調整；二、客戶回饋意見的整理；三、人力資源與預算的分配。\n"
        + "如果因為出差或其他原因無法參加，請在今天下班前回覆郵件，我們會安排代理人員協助說明。\n\n"
        + "謝謝大家的配合，祝工作順利。\n\n"
        + "行政部　敬上\n";

    public const string IsoJpSubject = "文字コードのテスト：ISO-2022-JP";

    public const string IsoJpFromName = "山田太郎";

    public const string IsoJpBody =
        "お世話になっております。\n\n"
        + "これは文字コードの確認用メールです。本文は ISO-2022-JP で符号化されています。\n"
        + "日本語が正しく表示されていれば、デコードは成功です。\n\n"
        + "よろしくお願いいたします。\n";

    public const string ShiftJisSubject = "文字コードのテスト：Shift_JIS";

    public const string ShiftJisBody =
        "お疲れ様です。\n\n"
        + "このメールの本文は Shift_JIS で符号化されています。\n"
        + "漢字、ひらがな、カタカナ（テスト）が正しく表示されることを確認してください。\n\n"
        + "以上、よろしくお願いします。\n";

    // RFC 2047 样本：主题和发件人名称里各有一个 GB2312(B) 和一个 UTF-8(Q) 编码字，中间夹着没有编码的 ASCII 文字
    public const string Rfc2047SubjectGbPart = "月度报告";

    public const string Rfc2047SubjectUtf8Part = "（含附件 — résumé）";

    public const string Rfc2047FromGbPart = "李";

    public const string Rfc2047FromUtf8Part = "明 (Ming)";

    public const string Rfc2047ToName = "Müller 先生";

    public static string Rfc2047ExpectedSubject => $"{Rfc2047SubjectGbPart} Report {Rfc2047SubjectUtf8Part}";

    public static string Rfc2047ExpectedFromName => $"{Rfc2047FromGbPart} Li {Rfc2047FromUtf8Part}";

    public const string Rfc2047Body =
        "Expected subject: 月度报告 Report （含附件 — résumé）\n"
        + "Expected sender name: 李 Li 明 (Ming)\n"
        + "Expected first recipient name: Müller 先生\n";

    // 8 位原始邮件头样本：主题和发件人名称直接写 GBK 字节，没有 RFC 2047 编码字；正文声明 gb2312
    public const string GbkRawHeaderSubject = "老邮件：邮件头直接写 GBK 字节";

    public const string GbkRawHeaderFromName = "李四";

    public const string GbkRawHeaderBody =
        "您好，\n\n"
        + "这封邮件的主题和发件人名称没有经过 RFC 2047 编码，而是直接写入了 GBK 字节，"
        + "很多十几年前的中文邮件客户端就是这样发信的。\n\n"
        + "正文声明了 charset=gb2312，邮件头需要参考它来解码。\n";

    public static void WriteAll(SampleOutput output)
    {
        output.Write("charset-gbk.eml", BuildGbk());
        output.Write("charset-gbk-raw-header.eml", BuildGbkRawHeader());
        output.Write("charset-gb18030.eml", BuildGb18030());
        output.Write("charset-big5-undeclared.eml", BuildBig5Undeclared());
        output.Write("charset-iso-2022-jp.eml", BuildIsoJp());
        output.Write("charset-shift-jis.eml", BuildShiftJis());
        output.Write("headers-rfc2047.eml", BuildRfc2047());
    }

    // GBK 正文，Content-Type 里声明了 charset，Base64 传输；主题和发件人用 GBK 的 RFC 2047 编码字
    private static byte[] BuildGbk()
    {
        var mail = new RawMail()
            .Header("From", $"{Rfc2047.EncodeB("GBK", GbkFromName)} <zhang.san@example.com>")
            .Header("To", "Alice Zhang <alice.zhang@example.com>")
            .Header("Subject", Rfc2047.EncodeB("GBK", GbkSubject))
            .Header("Date", "Thu, 1 Oct 2026 10:00:00 +0800")
            .Header("Message-Id", "<charset-gbk@samples.efv.invalid>")
            .Header("MIME-Version", "1.0")
            .Header("Content-Type", "text/plain; charset=\"gbk\"")
            .Header("Content-Transfer-Encoding", "base64")
            .EndHeaders();
        mail.Base64Body(Encode("gbk", GbkBody));
        return mail.ToArray();
    }

    // 邮件头里直接写 GBK 字节（没有 RFC 2047）：只有 ASCII 以外的 From 名称和 Subject；正文 gb2312 + base64
    private static byte[] BuildGbkRawHeader()
    {
        var gbk = Encoding.GetEncoding("gbk", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        var mail = new RawMail()
            .RawHeader("From", [.. gbk.GetBytes(GbkRawHeaderFromName), .. " <li.si@example.com>"u8])
            .Header("To", "Alice Zhang <alice.zhang@example.com>")
            .RawHeader("Subject", gbk.GetBytes(GbkRawHeaderSubject))
            .Header("Date", "Thu, 1 Oct 2026 10:30:00 +0800")
            .Header("Message-Id", "<charset-gbk-raw-header@samples.efv.invalid>")
            .Header("MIME-Version", "1.0")
            .Header("Content-Type", "text/plain; charset=\"gb2312\"")
            .Header("Content-Transfer-Encoding", "base64")
            .EndHeaders();
        mail.Base64Body(Encode("gb2312", GbkRawHeaderBody));
        return mail.ToArray();
    }

    // GB18030 正文，8bit 直接写原始字节
    private static byte[] BuildGb18030()
    {
        var mail = new RawMail()
            .Header("From", "Zhang San <zhang.san@example.com>")
            .Header("To", "Alice Zhang <alice.zhang@example.com>")
            .Header("Subject", Rfc2047.EncodeB("GB18030", Gb18030Subject))
            .Header("Date", "Thu, 1 Oct 2026 10:05:00 +0800")
            .Header("Message-Id", "<charset-gb18030@samples.efv.invalid>")
            .Header("MIME-Version", "1.0")
            .Header("Content-Type", "text/plain; charset=gb18030")
            .Header("Content-Transfer-Encoding", "8bit")
            .EndHeaders();
        mail.RawBody(Encode("gb18030", Gb18030Body));
        return mail.ToArray();
    }

    // Big5 正文，Content-Type 里没有 charset 参数，也没有任何其他线索；主题是声明了 Big5 的 RFC 2047 编码字
    private static byte[] BuildBig5Undeclared()
    {
        var mail = new RawMail()
            .Header("From", "Admin Office <admin@example.net>")
            .Header("To", "Alice Zhang <alice.zhang@example.com>")
            .Header("Subject", Rfc2047.EncodeB("Big5", Big5Subject))
            .Header("Date", "Thu, 1 Oct 2026 10:10:00 +0800")
            .Header("Message-Id", "<charset-big5-undeclared@samples.efv.invalid>")
            .Header("MIME-Version", "1.0")
            .Header("Content-Type", "text/plain")
            .Header("Content-Transfer-Encoding", "8bit")
            .EndHeaders();
        mail.RawBody(Encode("big5", Big5Body));
        return mail.ToArray();
    }

    // ISO-2022-JP：7bit，带换码序列
    private static byte[] BuildIsoJp()
    {
        var mail = new RawMail()
            .Header("From", $"{Rfc2047.EncodeB("ISO-2022-JP", IsoJpFromName)} <yamada.taro@example.org>")
            .Header("To", "Alice Zhang <alice.zhang@example.com>")
            .Header("Subject", Rfc2047.EncodeB("ISO-2022-JP", IsoJpSubject))
            .Header("Date", "Thu, 1 Oct 2026 10:15:00 +0900")
            .Header("Message-Id", "<charset-iso-2022-jp@samples.efv.invalid>")
            .Header("MIME-Version", "1.0")
            .Header("Content-Type", "text/plain; charset=ISO-2022-JP")
            .Header("Content-Transfer-Encoding", "7bit")
            .EndHeaders();
        mail.RawBody(Encode("iso-2022-jp", IsoJpBody));
        return mail.ToArray();
    }

    // Shift_JIS：Base64 传输
    private static byte[] BuildShiftJis()
    {
        var mail = new RawMail()
            .Header("From", "Yamada Taro <yamada.taro@example.org>")
            .Header("To", "Alice Zhang <alice.zhang@example.com>")
            .Header("Subject", Rfc2047.EncodeB("Shift_JIS", ShiftJisSubject))
            .Header("Date", "Thu, 1 Oct 2026 10:20:00 +0900")
            .Header("Message-Id", "<charset-shift-jis@samples.efv.invalid>")
            .Header("MIME-Version", "1.0")
            .Header("Content-Type", "text/plain; charset=Shift_JIS")
            .Header("Content-Transfer-Encoding", "base64")
            .EndHeaders();
        mail.Base64Body(Encode("shift_jis", ShiftJisBody));
        return mail.ToArray();
    }

    // RFC 2047：主题和发件人名称同时有 =?GB2312?B? 和 =?UTF-8?Q? 两种编码字
    private static byte[] BuildRfc2047()
    {
        var subject = $"{Rfc2047.EncodeB("GB2312", Rfc2047SubjectGbPart)} Report {Rfc2047.EncodeQ("UTF-8", Rfc2047SubjectUtf8Part)}";
        var from = $"{Rfc2047.EncodeB("GB2312", Rfc2047FromGbPart)} Li {Rfc2047.EncodeQ("UTF-8", Rfc2047FromUtf8Part)} <li.ming@example.com>";
        var to = $"{Rfc2047.EncodeQ("UTF-8", Rfc2047ToName)} <mueller@example.org>, Carol Wang <carol.wang@example.net>";

        var mail = new RawMail()
            .Header("From", from)
            .Header("To", to)
            .Header("Subject", subject)
            .Header("Date", "Thu, 1 Oct 2026 10:25:00 +0800")
            .Header("Message-Id", "<headers-rfc2047@samples.efv.invalid>")
            .Header("MIME-Version", "1.0")
            .Header("Content-Type", "text/plain; charset=utf-8")
            .Header("Content-Transfer-Encoding", "base64")
            .EndHeaders();
        mail.Base64Body(Encoding.UTF8.GetBytes(Rfc2047Body));
        return mail.ToArray();
    }

    /// <summary>
    /// 按指定编码把文字转成字节，换行统一成 CRLF。遇到编码里没有的字符直接抛异常，避免悄悄替换成问号。
    /// </summary>
    private static byte[] Encode(string charset, string text)
    {
        var encoding = Encoding.GetEncoding(charset, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        return encoding.GetBytes(text.ReplaceLineEndings("\r\n"));
    }
}
