using System.Text.Json;
using System.Text.Json.Nodes;
using EmailFileViewer.Sidecar.Protocol;

namespace EmailFileViewer.Sidecar.Tests;

/// <summary>
/// 任务 4.1：并发——不同请求并发处理，同一文档的操作串行，每条响应完整地占一行。
/// </summary>
public class ConcurrencyTests
{
    [Fact]
    [Trait("Task", "4.1")]
    public async Task ManyConcurrentOpens_AreAllAnsweredWithCompleteDistinctDocuments()
    {
        await using var session = SidecarSession.Start();
        string[] samples = ["marketing-remote-content.eml", "cid-inline-image.eml", "body-multipart-mixed-inline-images.eml", "nested-msg-two-levels.msg", "charset-gbk.eml"];

        var calls = Enumerable.Range(0, 60)
            .Select(i => session.CallAsync("email.open", new JsonObject { ["path"] = EmailFileViewer.Core.Tests.SampleFiles.GetPath(samples[i % samples.Length]) }))
            .Concat(Enumerable.Range(0, 20).Select(_ => session.CallAsync("system.ping")))
            .ToList();
        var responses = await Task.WhenAll(calls);
        await session.CompleteAsync();

        var opens = responses.Where(response => response["result"]?["docId"] is not null).ToList();
        Assert.Equal(60, opens.Count);
        Assert.Equal(60, opens.Select(response => (string)response["result"]!["docId"]!).Distinct().Count());
        Assert.Equal(80, session.Lines.Count);
        Assert.All(opens, response => Assert.False(string.IsNullOrEmpty((string?)response["result"]!["document"]!["subject"])));
    }

    [Fact]
    [Trait("Task", "4.1")]
    public async Task ConcurrentLargeResponses_NeverInterleave_EachLineIsOneCompleteJsonObject()
    {
        using var temp = new EmailFileViewer.Core.Tests.TempDirectory();
        var big = WriteMailWithLargeInlineImage(temp, megabytes: 2);
        await using var session = SidecarSession.Start();

        var calls = Enumerable.Range(0, 12)
            .Select(_ => session.CallAsync("email.open", new JsonObject { ["path"] = big }))
            .Concat(Enumerable.Range(0, 40).Select(_ => session.CallAsync("system.ping")))
            .ToList();
        var responses = await Task.WhenAll(calls);
        await session.CompleteAsync();

        // 每一行都能解析成完整的 JSON 对象；大响应的长度只差 docId 和 id 的位数（d1 与 d12 差一位）
        Assert.Equal(52, session.Lines.Count);
        var sizes = session.Lines.Where(line => line.Length > 1_000_000).Select(line => line.Length).ToList();
        Assert.Equal(12, sizes.Count);
        Assert.True(sizes.Max() - sizes.Min() <= 4, $"大响应的长度差得太多：{sizes.Min()} 到 {sizes.Max()}");
        Assert.All(session.Lines, line => Assert.Equal(JsonValueKind.Object, JsonNode.Parse(line)!.GetValueKind()));
        Assert.Equal(52, responses.Select(response => (int)response["id"]!).Distinct().Count());
    }

    [Fact]
    [Trait("Task", "4.1")]
    public async Task RequestsForDifferentDocuments_RunConcurrently()
    {
        var methods = new TestMethods();
        await using var session = SidecarSession.Start(new StdioOptions { ConfigureMethods = methods.Register });
        var first = await Samples.OpenAsync(session, "headers-normal.eml");
        var second = await Samples.OpenAsync(session, "headers-normal.eml");

        // 两个调用必须同时在执行才都能结束；如果不同文档的请求被串行了，会一直等到超时
        var responses = await Task.WhenAll(
            session.CallAsync("test.rendezvous.document", new JsonObject { ["docId"] = first.DocId }),
            session.CallAsync("test.rendezvous.document", new JsonObject { ["docId"] = second.DocId }));

        Assert.All(responses, response => SidecarSession.Result(response));
    }

    [Fact]
    [Trait("Task", "4.1")]
    public async Task RequestsNotTiedToADocument_RunConcurrently()
    {
        var methods = new TestMethods();
        await using var session = SidecarSession.Start(new StdioOptions { ConfigureMethods = methods.Register });

        var responses = await Task.WhenAll(
            session.CallAsync("test.rendezvous"),
            session.CallAsync("test.rendezvous"));

        Assert.All(responses, response => SidecarSession.Result(response));
    }

    [Fact]
    [Trait("Task", "4.1")]
    public async Task RequestsForTheSameDocument_RunOneAtATime_InArrivalOrder()
    {
        var methods = new TestMethods();
        await using var session = SidecarSession.Start(new StdioOptions { ConfigureMethods = methods.Register });
        var (docId, _) = await Samples.OpenAsync(session, "headers-normal.eml");

        // 第一个最慢：如果没有按文档串行，后面的会和它重叠，或者抢在它前面执行
        var delays = new[] { 80, 5, 5, 5, 5, 5 };
        var calls = delays.Select(delay => session.CallAsync("test.exclusive", new JsonObject { ["docId"] = docId, ["delayMilliseconds"] = delay })).ToList();
        var responses = await Task.WhenAll(calls);

        Assert.All(responses, response => SidecarSession.Result(response));
        Assert.Equal(1, methods.MaxConcurrentExclusive);
        var expected = delays.SelectMany(delay => new[] { $"start {delay}", $"end {delay}" }).ToArray();
        Assert.Equal(expected, methods.Events);
    }

    [Fact]
    [Trait("Task", "4.1")]
    public async Task ACloseSentRightAfterASlowOperation_WaitsForIt_AndLaterOperationsSeeTheDocumentGone()
    {
        var methods = new TestMethods();
        await using var session = SidecarSession.Start(new StdioOptions { ConfigureMethods = methods.Register });
        var (docId, document) = await Samples.OpenAsync(session, "attachments-pdf-xlsx-jpg.eml");
        var target = Path.Combine(session.Temp.DirectoryPath, "late.pdf");

        var slow = session.CallAsync("test.exclusive", new JsonObject { ["docId"] = docId, ["delayMilliseconds"] = 150 });
        var close = session.CallAsync("document.close", new JsonObject { ["docId"] = docId });
        var after = session.CallAsync("attachment.save", new JsonObject { ["docId"] = docId, ["attachmentId"] = Samples.AttachmentId(document, "report.pdf"), ["targetPath"] = target });

        SidecarSession.Result(await close);

        // 关闭请求排在慢操作后面：它的响应到达时，慢操作已经结束
        Assert.Contains("end 150", methods.Events);
        SidecarSession.Result(await slow);
        Assert.Equal(ErrorCodes.DocumentNotFound, SidecarSession.ErrorCode(await after));
        Assert.False(File.Exists(target));
    }

    [Fact]
    [Trait("Task", "4.1")]
    public async Task ASaveSentRightBeforeClose_StillSucceeds()
    {
        await using var session = SidecarSession.Start();
        var (docId, document) = await Samples.OpenAsync(session, "attachments-pdf-xlsx-jpg.eml");
        var target = Path.Combine(session.Temp.DirectoryPath, "report.pdf");

        var save = session.CallAsync("attachment.save", new JsonObject { ["docId"] = docId, ["attachmentId"] = Samples.AttachmentId(document, "report.pdf"), ["targetPath"] = target });
        var close = session.CallAsync("document.close", new JsonObject { ["docId"] = docId });

        SidecarSession.Result(await save);
        SidecarSession.Result(await close);
        Assert.True(File.Exists(target));
    }

    [Fact]
    [Trait("Task", "4.1")]
    public async Task ConcurrentSavesFromTheSameDocument_AllWriteCorrectContent()
    {
        await using var session = SidecarSession.Start();
        var (docId, document) = await Samples.OpenAsync(session, "attachments-pdf-xlsx-jpg.eml");
        var directory = Directory.CreateDirectory(Path.Combine(session.Temp.DirectoryPath, "out")).FullName;
        var names = new[] { "report.pdf", "budget.xlsx", "photo.jpg" };

        var calls = Enumerable.Range(0, 30)
            .Select(i => session.CallAsync("attachment.save", new JsonObject
            {
                ["docId"] = docId,
                ["attachmentId"] = Samples.AttachmentId(document, names[i % 3]),
                ["targetPath"] = Path.Combine(directory, $"copy-{i}-{names[i % 3]}"),
            }))
            .ToList();
        var responses = await Task.WhenAll(calls);

        Assert.All(responses, response => SidecarSession.Result(response));
        foreach (var name in names)
        {
            var expected = await Samples.ReadAttachmentWithCoreAsync("attachments-pdf-xlsx-jpg.eml", Samples.AttachmentId(document, name));
            foreach (var file in Directory.GetFiles(directory, $"copy-*-{name}"))
            {
                Assert.Equal(expected, await File.ReadAllBytesAsync(file));
            }
        }

        Assert.Equal(30, Directory.GetFiles(directory).Length);
    }

    // 一封带一张很大的内嵌图片的邮件：解析后的 HTML 里有一个几 MB 的 data: URI
    private static string WriteMailWithLargeInlineImage(EmailFileViewer.Core.Tests.TempDirectory temp, int megabytes)
    {
        var bytes = new byte[megabytes * 1024 * 1024];
        new Random(7).NextBytes(bytes);
        var base64 = Convert.ToBase64String(bytes, Base64FormattingOptions.InsertLineBreaks);
        return temp.WriteMail("big.eml", $"""
            From: a@example.com
            Subject: big inline image
            MIME-Version: 1.0
            Content-Type: multipart/related; boundary="r"

            --r
            Content-Type: text/html; charset=utf-8

            <p>big</p><img src="cid:big@example.com">
            --r
            Content-Type: image/png
            Content-Transfer-Encoding: base64
            Content-ID: <big@example.com>

            {base64}
            --r--
            """);
    }
}
