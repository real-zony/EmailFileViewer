using System.Text.Json;
using EmailFileViewer.Core.Json;
using EmailFileViewer.Core.Models;

namespace EmailFileViewer.Core.Tests;

/// <summary>
/// <see cref="EmailDocument"/> 的 JSON 输出：时间带 <c>Z</c>，缺失字段是空数组或 <c>null</c>。
/// </summary>
public class EmailJsonTests
{
    [Fact]
    [Trait("Scenario", "email-viewing / 日期按本地时区显示 / JSON 中的发送时间是带 Z 的 UTC ISO 8601")]
    public async Task SentAtUtc_IsWrittenAsIso8601WithZ()
    {
        using var parsed = await MailAssert.ParseSampleAsync("headers-timezone-bcc-replyto-no-cc.eml");

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(parsed.Document, EmailJsonContext.Default.EmailDocument));
        var root = json.RootElement;

        Assert.Equal("2026-10-05T16:00:00Z", root.GetProperty("sentAtUtc").GetString());
        Assert.Equal("Mon, 5 Oct 2026 09:00:00 -0700", root.GetProperty("dateHeaderRaw").GetString());
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 缺少某个邮件头 / JSON 中缺失字段是空数组或 null")]
    public async Task MissingFields_AreEmptyArraysOrNullInJson()
    {
        using var parsed = await MailAssert.ParseSampleAsync("headers-timezone-bcc-replyto-no-cc.eml");

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(parsed.Document, EmailJsonContext.Default.EmailDocument));
        var root = json.RootElement;

        Assert.Equal(JsonValueKind.Array, root.GetProperty("cc").ValueKind);
        Assert.Equal(0, root.GetProperty("cc").GetArrayLength());
        Assert.Equal(0, root.GetProperty("attachments").GetArrayLength());
        Assert.Equal("Bob Li", root.GetProperty("to")[0].GetProperty("name").GetString());
        Assert.Equal("bob.li@example.org", root.GetProperty("to")[0].GetProperty("address").GetString());
        Assert.Equal("PlainText", root.GetProperty("body").GetProperty("sourceFormat").GetString());
        Assert.False(root.GetProperty("isSigned").GetBoolean());
        Assert.False(root.GetProperty("isEncrypted").GetBoolean());
        Assert.False(root.GetProperty("hasRemoteContent").GetBoolean());
    }

    [Fact]
    [Trait("Scenario", "email-viewing / 缺少某个邮件头 / 没有日期和主题时 JSON 里是 null")]
    public async Task MissingDateAndSubject_AreNullInJson()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteMail("from-only.eml", "From: a@example.com\n\nbody\n");
        using var parsed = await EmailParser.ParseFileAsync(path);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(parsed.Document, EmailJsonContext.Default.EmailDocument));
        var root = json.RootElement;

        Assert.Equal(JsonValueKind.Null, root.GetProperty("sentAtUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("dateHeaderRaw").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("subject").ValueKind);
    }

    [Fact]
    public async Task Document_RoundTripsThroughJson()
    {
        using var parsed = await MailAssert.ParseSampleAsync("headers-timezone-bcc-replyto-no-cc.eml");
        var original = parsed.Document;

        var json = JsonSerializer.Serialize(original, EmailJsonContext.Default.EmailDocument);
        var restored = JsonSerializer.Deserialize(json, EmailJsonContext.Default.EmailDocument);

        Assert.NotNull(restored);
        Assert.Equal(original.SentAtUtc, restored.SentAtUtc);
        Assert.Equal(original.Subject, restored.Subject);
        Assert.Equal(original.Body, restored.Body);
        MailAssert.AddressesEqual(restored.ReplyTo, ("Alice Zhang (Support)", "support@example.com"));
    }

    [Fact]
    public void SentAtUtc_WithNonUtcOffset_IsWrittenAsUtc()
    {
        var document = new EmailDocument { SentAtUtc = new DateTimeOffset(2026, 10, 6, 0, 0, 0, 123, TimeSpan.FromHours(8)) };

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(document, EmailJsonContext.Default.EmailDocument));

        Assert.Equal("2026-10-05T16:00:00.123Z", json.RootElement.GetProperty("sentAtUtc").GetString());
    }
}
