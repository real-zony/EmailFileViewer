namespace EmailFileViewer.Core.Tests;

/// <summary>
/// 对应规格 email-viewing 中的“显示邮件头”和“日期按本地时区显示”（EML 部分）。
/// </summary>
public class EmlHeaderTests
{
    [Fact]
    [Trait("Scenario", "email-viewing / 显示邮件头 / 普通邮件")]
    public async Task NormalMail_AllHeadersWithNameAndAddress()
    {
        using var parsed = await MailAssert.ParseSampleAsync("headers-normal.eml");
        var document = parsed.Document;

        MailAssert.AddressesEqual(document.From, ("Alice Zhang", "alice.zhang@example.com"));
        MailAssert.AddressesEqual(document.To, ("Bob Li", "bob.li@example.org"), ("Carol Wang", "carol.wang@example.net"));
        MailAssert.AddressesEqual(document.Cc, ("Dave Chen", "dave.chen@example.com"));
        Assert.Equal("Quarterly planning meeting", document.Subject);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 2, 0, 0, TimeSpan.Zero), document.SentAtUtc);
        Assert.Equal("Thu, 01 Oct 2026 10:00:00 +0800", document.DateHeaderRaw);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 显示邮件头 / 普通邮件（密送和回复地址）")]
    public async Task BccAndReplyTo_AreMapped()
    {
        using var parsed = await MailAssert.ParseSampleAsync("headers-timezone-bcc-replyto-no-cc.eml");
        var document = parsed.Document;

        MailAssert.AddressesEqual(document.Bcc, ("Erin Xu", "erin.xu@example.org"));
        MailAssert.AddressesEqual(document.ReplyTo, ("Alice Zhang (Support)", "support@example.com"));
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 缺少某个邮件头 / 邮件没有抄送字段")]
    public async Task MailWithoutCc_CcIsEmptyList()
    {
        using var parsed = await MailAssert.ParseSampleAsync("headers-timezone-bcc-replyto-no-cc.eml");

        Assert.NotNull(parsed.Document.Cc);
        Assert.Empty(parsed.Document.Cc);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 缺少某个邮件头 / 缺少多个字段时都是空列表或 null，不用空字符串占位")]
    public async Task MailWithOnlySubject_MissingFieldsAreEmptyOrNull()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteMail("subject-only.eml", "Subject: only a subject\n\nbody\n");

        using var parsed = await EmailParser.ParseFileAsync(path);
        var document = parsed.Document;

        Assert.Empty(document.From);
        Assert.Empty(document.To);
        Assert.Empty(document.Cc);
        Assert.Empty(document.Bcc);
        Assert.Empty(document.ReplyTo);
        Assert.Null(document.SentAtUtc);
        Assert.Null(document.DateHeaderRaw);
        Assert.Equal("only a subject", document.Subject);
    }

    [Theory]
    [InlineData("Subject:\n")]
    [InlineData("Subject:   \n")]
    [Trait("Scenario", "email-viewing / 缺少某个邮件头 / 主题为空时是 null，不是空字符串")]
    public async Task EmptySubject_IsNull(string subjectLine)
    {
        using var temp = new TempDirectory();
        var path = temp.WriteMail("empty-subject.eml", "From: a@example.com\n" + subjectLine + "\nbody\n");

        using var parsed = await EmailParser.ParseFileAsync(path);

        Assert.Null(parsed.Document.Subject);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 显示邮件头 / 地址只有邮箱没有名称")]
    public async Task AddressWithoutName_NameIsNull()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteMail("bare-address.eml", "From: bare@example.com\nTo: <angle@example.com>, \"Named\" <named@example.com>\n\nbody\n");

        using var parsed = await EmailParser.ParseFileAsync(path);

        MailAssert.AddressesEqual(parsed.Document.From, (null, "bare@example.com"));
        MailAssert.AddressesEqual(parsed.Document.To, (null, "angle@example.com"), ("Named", "named@example.com"));
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 显示邮件头 / 收件人是地址组")]
    public async Task GroupAddress_MembersAreFlattened_EmptyGroupIsIgnored()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteMail(
            "groups.eml",
            "From: a@example.com\nTo: Team: x@example.com, Y <y@example.com>;\nCc: undisclosed-recipients:;\n\nbody\n");

        using var parsed = await EmailParser.ParseFileAsync(path);

        MailAssert.AddressesEqual(parsed.Document.To, (null, "x@example.com"), ("Y", "y@example.com"));
        Assert.Empty(parsed.Document.Cc);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 显示邮件头 / 主题折行")]
    public async Task FoldedSubject_IsJoinedIntoOneLine()
    {
        using var parsed = await MailAssert.ParseSampleAsync("EmailWithMultilineSubject.eml");

        Assert.Equal(
            "One Two Three Four Five Six Seven Eight Nine Ten Eleven Twelve Thirteen Fourteen Fifteen",
            parsed.Document.Subject);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 显示邮件头 / 发件人、收件人、抄送是同一个人，名称带 Windows-1252 编码字")]
    public async Task EncodedWordNames_AreDecoded()
    {
        using var parsed = await MailAssert.ParseSampleAsync("TestWithAttachmentsAndInlines.eml");
        var document = parsed.Document;

        MailAssert.AddressesEqual(document.From, ("Müller Peter", "Peter.Mueller@domain.ch"));
        MailAssert.AddressesEqual(document.To, ("Müller Peter", "Peter.Mueller@domain.ch"));
        MailAssert.AddressesEqual(document.Cc, ("Müller Peter", "Peter.Mueller@domain.ch"));
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 日期按本地时区显示 / 跨时区邮件（模型里统一为 UTC）")]
    public async Task CrossTimezoneDate_IsConvertedToUtc()
    {
        using var parsed = await MailAssert.ParseSampleAsync("headers-timezone-bcc-replyto-no-cc.eml");
        var document = parsed.Document;

        // Date: Mon, 5 Oct 2026 09:00:00 -0700，和本机时区无关
        var sentAtUtc = Assert.IsType<DateTimeOffset>(document.SentAtUtc);
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 16, 0, 0, TimeSpan.Zero), sentAtUtc);
        Assert.Equal(TimeSpan.Zero, sentAtUtc.Offset);
        Assert.Equal("Mon, 5 Oct 2026 09:00:00 -0700", document.DateHeaderRaw);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 日期按本地时区显示 / UTC+8 的用户看到的时间")]
    public async Task CrossTimezoneDate_ConvertedToUtcPlus8ShowsNextDayMidnight()
    {
        using var parsed = await MailAssert.ParseSampleAsync("headers-timezone-bcc-replyto-no-cc.eml");
        var utcPlus8 = TimeZoneInfo.CreateCustomTimeZone("UTC+8", TimeSpan.FromHours(8), "UTC+8", "UTC+8");

        var local = TimeZoneInfo.ConvertTime(parsed.Document.SentAtUtc!.Value, utcPlus8);

        Assert.Equal(new DateTime(2026, 10, 6, 0, 0, 0), local.DateTime);
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 显示邮件头 / Date 头无法解析时保留原文")]
    public async Task UnparseableDate_SentAtUtcIsNullAndRawIsKept()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteMail("bad-date.eml", "From: a@example.com\nDate: sometime last week\nSubject: x\n\nbody\n");

        using var parsed = await EmailParser.ParseFileAsync(path);

        Assert.Null(parsed.Document.SentAtUtc);
        Assert.Equal("sometime last week", parsed.Document.DateHeaderRaw);
    }
}
