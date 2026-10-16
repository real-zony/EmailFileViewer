using MsgKit;

namespace EmailFileViewer.SampleGenerator;

/// <summary>
/// 用 MsgKit 构造的 Outlook MSG 样本。
/// </summary>
/// <remarks>
/// MsgKit 是 MIT 协议（Sicos1977/MsgKit）。它每次保存都会写入当前时间和随机的 EntryId / InstanceKey，
/// 所以重新生成的 MSG 文件会有字节差异，但内容（主题、正文、附件）相同。
/// </remarks>
internal static class MsgSamples
{
    public const string RtfOnlyFileName = "body-rtf-only.msg";

    public const string RtfOnlyMarker = "RTF-ONLY-BODY";

    public const string HtmlInRtfFileName = "body-html-in-rtf.msg";

    public const string HtmlInRtfMarker = "HTML-IN-RTF-BODY";

    public const string NestedFileName = "nested-msg-two-levels.msg";

    public static void WriteAll(SampleOutput output)
    {
        output.Write(RtfOnlyFileName, CreateRtfOnly());
        output.Write(HtmlInRtfFileName, CreateHtmlInRtf());
        output.Write(NestedFileName, CreateNestedTwoLevels());
    }

    /// <summary>
    /// 两层嵌套的 MSG：外层 -> 中层 -> 内层，内层带一个普通附件。
    /// </summary>
    public static byte[] CreateNestedTwoLevels()
    {
        var innermost = CreateTextEmail(
            "Level 3 (innermost) MSG",
            "This is the innermost message (level 3). It has its own attachment.",
            ("dave.chen@example.com", "Dave Chen"),
            ("carol.wang@example.net", "Carol Wang"),
            new DateTime(2026, 9, 30, 8, 0, 0, DateTimeKind.Utc),
            ("level3-note.txt", "Attachment inside the innermost message.\n"u8.ToArray()));

        var middleByValue = CreateTextEmail(
            "Level 2 (middle) MSG",
            "This is the middle message (level 2). It carries the level 3 message as an embedded message.",
            ("carol.wang@example.net", "Carol Wang"),
            ("bob.li@example.org", "Bob Li"),
            new DateTime(2026, 9, 30, 9, 0, 0, DateTimeKind.Utc),
            ("Level 3 (innermost) MSG.msg", innermost));
        var middle = MsgNesting.EmbedAttachment(middleByValue, 0, innermost);

        var outerByValue = CreateTextEmail(
            "Level 1 (outer) MSG with two nested levels",
            "This is the outer message (level 1). It carries the level 2 message as an embedded message.",
            ("bob.li@example.org", "Bob Li"),
            ("alice.zhang@example.com", "Alice Zhang"),
            new DateTime(2026, 9, 30, 10, 0, 0, DateTimeKind.Utc),
            ("Level 2 (middle) MSG.msg", middle));
        return MsgNesting.EmbedAttachment(outerByValue, 0, middle);
    }

    private static byte[] CreateTextEmail(
        string subject,
        string bodyText,
        (string Email, string Name) from,
        (string Email, string Name) to,
        DateTime sentUtc,
        params (string FileName, byte[] Data)[] attachments)
    {
        using var email = new Email(new Sender(from.Email, from.Name), subject)
        {
            SentOn = sentUtc,
            BodyText = bodyText,
        };
        email.Recipients.AddTo(to.Email, to.Name);
        foreach (var (fileName, data) in attachments)
        {
            email.Attachments.Add(new MemoryStream(data), fileName);
        }

        using var stream = new MemoryStream();
        email.Save(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// HTML 封装在 RTF 中的 MSG：HTML 正文只存在于 PR_RTF_COMPRESSED 里（带 <c>\fromhtml1</c>），没有 PR_HTML 流，也没有纯文本流。
    /// </summary>
    /// <remarks>
    /// 封装用 MsgKit 内部的 <c>Encapsulator</c>（它只在邮件是草稿时才会被调用，所以这里用反射直接调用）。
    /// 它把整段 HTML 放进一个 <c>\htmltag</c> 组，比 Outlook 的逐标签封装简单，但同样以 <c>\fromhtml1</c> 标识。
    /// 它对 ASCII 以外的字符，中文用 <c>\uN?</c> 能正确还原，Latin-1 字符（如 é）会按 <c>\'E9</c> 写出但声明的代码页是 UTF-8，还原后是乱码，所以样本里不用这类字符。
    /// </remarks>
    public static byte[] CreateHtmlInRtf()
    {
        var encapsulator = typeof(Email).Assembly.GetType("MsgKit.Rtf.Encapsulator")
            ?? throw new InvalidOperationException("找不到 MsgKit.Rtf.Encapsulator，MsgKit 的内部结构变了");
        var encapsulate = encapsulator.GetMethod("Encapsulate", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("找不到 Encapsulator.Encapsulate，MsgKit 的内部结构变了");
        var rtf = (string)encapsulate.Invoke(null, [HtmlBody])!;

        using var email = new Email(new Sender("alice.zhang@example.com", "Alice Zhang"), "HTML-in-RTF body sample")
        {
            SentOn = new DateTime(2026, 10, 1, 3, 0, 0, DateTimeKind.Utc),
            BodyRtf = rtf,
        };
        email.Recipients.AddTo("bob.li@example.org", "Bob Li");

        using var stream = new MemoryStream();
        email.Save(stream);
        return stream.ToArray();
    }

    private const string HtmlBody =
        "<html><head><meta charset=\"utf-8\"><title>HTML in RTF</title></head><body>"
        + "<h1>HTML-IN-RTF-BODY</h1>"
        + "<p>This message stores its HTML body encapsulated in RTF: <b>bold</b>, <i>italic</i>, and a <a href=\"https://example.com/html-in-rtf\">link</a>.</p>"
        + "<p>中文段落，用来确认非 ASCII 文字能随 RTF 的 \\uN? 转义还原。Ampersand: &amp;</p>"
        + "<ul><li>First item</li><li>Second item</li></ul>"
        + "<table border=\"1\"><tr><td>Cell A</td><td>Cell B</td></tr></table>"
        + "<p>END-OF-HTML-IN-RTF-BODY</p>"
        + "</body></html>";

    /// <summary>
    /// 只有 RTF 正文的 MSG：没有 PR_BODY 纯文本，也没有 PR_HTML，RTF 也不是 HTML 封装（没有 \fromhtml）。
    /// </summary>
    public static byte[] CreateRtfOnly()
    {
        using var email = new Email(new Sender("alice.zhang@example.com", "Alice Zhang"), "RTF-only body sample")
        {
            SentOn = new DateTime(2026, 10, 1, 2, 0, 0, DateTimeKind.Utc),
            BodyRtf = RtfBody,
        };
        email.Recipients.AddTo("bob.li@example.org", "Bob Li");
        email.Recipients.AddCc("dave.chen@example.com", "Dave Chen");

        using var stream = new MemoryStream();
        email.Save(stream);
        return stream.ToArray();
    }

    // RTF 里只用 ASCII，中文用 \uN? 转义。每个段落单独一行，行尾是 \par
    private const string RtfBody =
        """
        {\rtf1\ansi\ansicpg1252\deff0\nouicompat{\fonttbl{\f0\fswiss\fcharset0 Arial;}{\f1\fmodern\fcharset0 Courier New;}}{\colortbl ;\red192\green0\blue0;\red0\green112\blue192;}
        \viewkind4\uc1\pard\sa180\sl276\slmult1\f0\fs22\b RTF-ONLY-BODY\b0\par
        This message has only an RTF body: \b bold\b0 , \i italic\i0 , \ul underlined\ulnone  and \cf1 red\cf0  text.\par
        Second paragraph after a paragraph break. Chinese text:  3?▙1?❗3?㎅3?ሩ0?\par
        \pard\fi-360\li720\sa60\bullet\tab First bullet item\par
        \pard\fi-360\li720\sa60\bullet\tab Second bullet item\par
        \pard\sa180\f1 Monospace line in Courier New\f0\par
        \pard\sa180 A hyperlink: {\field{\*\fldinst{HYPERLINK "https://example.com/rtf"}}{\fldrslt{\ul\cf2 https://example.com/rtf}}}\par
        \pard\sa180 END-OF-RTF-BODY\par
        }
        """;
}
