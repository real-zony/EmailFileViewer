using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EmailFileViewer.Sidecar.Protocol;

namespace EmailFileViewer.Sidecar.Tests;

/// <summary>
/// 任务 4.1：stdio 上的 JSON 行协议——消息格式、分发、错误处理。
/// </summary>
public class ProtocolBasicsTests
{
    [Fact]
    [Trait("Task", "4.1")]
    public async Task Ping_ReturnsTheProtocolVersion()
    {
        await using var session = SidecarSession.Start();

        var response = await session.CallAsync("system.ping");

        Assert.Equal(1, (int)SidecarSession.Result(response)["protocolVersion"]!);
        Assert.Equal(1, (int)response["id"]!);
    }

    [Fact]
    [Trait("Task", "4.1")]
    public async Task RequestId_IsEchoedWithItsOwnType()
    {
        await using var session = SidecarSession.Start();

        var integerId = await session.CallWithIdAsync(JsonValue.Create(42), "system.ping");
        var stringId = await session.CallWithIdAsync(JsonValue.Create("req-7"), "system.ping");

        Assert.Equal(JsonValueKind.Number, integerId["id"]!.GetValueKind());
        Assert.Equal(42, (int)integerId["id"]!);
        Assert.Equal(JsonValueKind.String, stringId["id"]!.GetValueKind());
        Assert.Equal("req-7", (string)stringId["id"]!);
    }

    [Fact]
    [Trait("Task", "4.1")]
    public async Task ParamsMayBeOmittedOrNull_ForMethodsWithoutRequiredFields()
    {
        await using var session = SidecarSession.Start();

        await session.SendRawAsync("""{"id":1,"method":"system.ping"}""");
        await session.SendRawAsync("""{"id":2,"method":"system.ping","params":null}""");
        await session.SendRawAsync("""{"id":3,"method":"system.ping","params":{}}""");
        await session.CompleteAsync();

        Assert.Equal(3, session.Lines.Count(line => line.Contains("protocolVersion", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("this is not json")]
    [InlineData("{\"id\":1,\"method\":")]
    [InlineData("[1,2,3]")]
    [InlineData("42")]
    [InlineData("\"text\"")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("""{"method":"system.ping"}""")]
    [InlineData("""{"id":null,"method":"system.ping"}""")]
    [InlineData("""{"id":true,"method":"system.ping"}""")]
    [InlineData("""{"id":1.5,"method":"system.ping"}""")]
    [InlineData("""{"id":{"a":1},"method":"system.ping"}""")]
    [Trait("Task", "4.1")]
    public async Task UnparsableOrIdlessLines_GetNullIdAndInvalidRequest_AndTheNextRequestStillWorks(string line)
    {
        await using var session = SidecarSession.Start();

        await session.SendRawAsync(line);
        var errors = await session.WaitForNullIdResponsesAsync(1);
        var next = await session.CallAsync("system.ping");

        var error = Assert.Single(errors);
        Assert.Equal(ErrorCodes.InvalidRequest, (string)error["error"]!["code"]!);
        Assert.False(string.IsNullOrWhiteSpace((string)error["error"]!["message"]!));
        Assert.Null(error["id"]);
        Assert.Null(error["result"]);
        SidecarSession.Result(next);
    }

    [Theory]
    [InlineData("""{"id":5}""")]
    [InlineData("""{"id":5,"method":""}""")]
    [InlineData("""{"id":5,"method":7}""")]
    [Trait("Task", "4.1")]
    public async Task MissingOrBadMethod_KeepsTheValidId(string line)
    {
        await using var session = SidecarSession.Start();

        await session.SendRawAsync(line);
        await session.CompleteAsync();

        var response = JsonNode.Parse(Assert.Single(session.Lines))!.AsObject();
        Assert.Equal(5, (int)response["id"]!);
        Assert.Equal(ErrorCodes.InvalidRequest, SidecarSession.ErrorCode(response));
    }

    [Fact]
    [Trait("Task", "4.1")]
    public async Task BlankLines_AreIgnored()
    {
        await using var session = SidecarSession.Start();

        await session.SendRawAsync("");
        await session.SendRawAsync("   ");
        var response = await session.CallAsync("system.ping");
        await session.CompleteAsync();

        SidecarSession.Result(response);
        Assert.Single(session.Lines);
    }

    [Fact]
    [Trait("Task", "4.1")]
    public async Task UnknownMethod_ReturnsMethodNotFound()
    {
        await using var session = SidecarSession.Start();

        var response = await session.CallAsync("no.such.method", new JsonObject());

        Assert.Equal(ErrorCodes.MethodNotFound, SidecarSession.ErrorCode(response));
        Assert.Contains("no.such.method", (string)response["error"]!["message"]!);
    }

    [Theory]
    [InlineData("email.open", """{}""")]
    [InlineData("email.open", """{"path":""}""")]
    [InlineData("email.open", """{"path":123}""")]
    [InlineData("email.open", """[1]""")]
    [InlineData("email.open", "\"text\"")]
    [InlineData("email.openNested", """{"docId":"d1"}""")]
    [InlineData("email.openNested", """{"attachmentId":"2"}""")]
    [InlineData("document.close", """{}""")]
    [InlineData("document.close", """{"docId":null}""")]
    [InlineData("attachment.save", """{"docId":"d1","attachmentId":"2"}""")]
    [InlineData("attachment.save", """{"docId":"d1","attachmentId":"2","targetPath":"relative/path.txt"}""")]
    [InlineData("attachment.saveAll", """{"docId":"d1"}""")]
    [InlineData("attachment.saveAll", """{"docId":"d1","targetDir":"relative"}""")]
    [InlineData("attachment.extractForOpen", """{"docId":"d1"}""")]
    [Trait("Task", "4.1")]
    public async Task BadParams_ReturnInvalidParams_WithoutTouchingAnyDocument(string method, string parametersJson)
    {
        await using var session = SidecarSession.Start();

        await session.SendRawAsync($$"""{"id":1,"method":"{{method}}","params":{{parametersJson}}}""");
        await session.CompleteAsync();

        var response = JsonNode.Parse(Assert.Single(session.Lines))!.AsObject();
        Assert.Equal(ErrorCodes.InvalidParams, SidecarSession.ErrorCode(response));
    }

    [Fact]
    [Trait("Task", "4.1")]
    public async Task ARequestThatThrows_ReturnsInternalError_AndLaterRequestsAreStillHandled()
    {
        await using var session = SidecarSession.Start(new StdioOptions { ConfigureMethods = TestMethods.AddThrowing });

        var before = await session.CallAsync("system.ping");
        var failed = await session.CallAsync("test.throw");
        var after = await session.CallAsync("system.ping");
        var opened = await Samples.OpenAsync(session, "headers-normal.eml");
        var code = await session.CompleteAsync();

        SidecarSession.Result(before);
        Assert.Equal(ErrorCodes.InternalError, SidecarSession.ErrorCode(failed));
        SidecarSession.Result(after);
        Assert.StartsWith("d", opened.DocId);
        Assert.Equal(0, code);

        // 错误响应里不带异常细节，细节在 stderr
        Assert.DoesNotContain("deliberate", failed.ToJsonString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("deliberate failure", session.Error.ToString());
        Assert.Contains("method=test.throw", session.Error.ToString());
    }

    [Fact]
    [Trait("Task", "4.1")]
    public async Task EveryStdoutLine_IsOneCompleteJsonObject_AndDiagnosticsStayOffStdout()
    {
        await using var session = SidecarSession.Start(new StdioOptions { ConfigureMethods = TestMethods.AddThrowing });

        await session.SendRawAsync("garbage");
        await session.CallAsync("test.throw");
        await session.CallAsync("no.such.method");
        await Samples.OpenAsync(session, "charset-gbk.eml");
        await session.CompleteAsync();

        Assert.Equal(4, session.Lines.Count);
        foreach (var line in session.Lines)
        {
            var parsed = JsonNode.Parse(line)!.AsObject();
            Assert.True(parsed.ContainsKey("id"));
            Assert.True(parsed.ContainsKey("result") ^ parsed.ContainsKey("error"), line);
            Assert.DoesNotContain('\n', line);
        }

        Assert.DoesNotContain("deliberate", string.Concat(session.Lines), StringComparison.Ordinal);
        Assert.NotEmpty(session.Error.ToString());
    }

    [Fact]
    [Trait("Task", "4.1")]
    public async Task ChineseText_IsWrittenAsIs_NotAsUnicodeEscapes()
    {
        await using var session = SidecarSession.Start();

        var (_, document) = await Samples.OpenAsync(session, "charset-gbk.eml");
        await session.CompleteAsync();

        var openLine = session.Lines.Single(line => line.Contains("docId", StringComparison.Ordinal));
        Assert.Contains("测试邮件：GBK 编码的中文主题", openLine);
        Assert.Contains("张三", openLine);
        Assert.DoesNotContain("\\u", openLine);
        Assert.Equal("测试邮件：GBK 编码的中文主题", (string)document["subject"]!);
    }

    [Fact]
    [Trait("Task", "4.1")]
    public async Task HtmlInTheDocument_IsNotEscapedEither_ButStaysOnOneLine()
    {
        await using var session = SidecarSession.Start();

        var (_, document) = await Samples.OpenAsync(session, "body-html-and-plain.eml");
        await session.CompleteAsync();

        var openLine = session.Lines.Single(line => line.Contains("docId", StringComparison.Ordinal));
        Assert.Contains("<h1>HTML-VERSION</h1>", openLine);
        Assert.Contains("HTML-VERSION", (string)document["body"]!["html"]!);
        Assert.DoesNotContain('\n', openLine);
    }

    [Fact]
    [Trait("Task", "4.1")]
    public async Task EndOfInput_FinishesPendingRequestsThenExitsWithZero()
    {
        await using var session = SidecarSession.Start();

        // 请求刚发出就关闭输入：已经收到的请求仍然要处理完
        var request = new JsonObject
        {
            ["id"] = 1,
            ["method"] = "email.open",
            ["params"] = new JsonObject { ["path"] = EmailFileViewer.Core.Tests.SampleFiles.GetPath("marketing-remote-content.eml") },
        };
        await session.SendRawAsync(request.ToJsonString());
        var code = await session.CompleteAsync();

        Assert.Equal(0, code);
        var response = JsonNode.Parse(Assert.Single(session.Lines))!.AsObject();
        Assert.Equal("Autumn Sale: up to 40% off", (string)SidecarSession.Result(response)["document"]!["subject"]!);
    }

    [Fact]
    [Trait("Task", "4.1")]
    public async Task Cancellation_StopsTheServiceWith130()
    {
        await using var session = SidecarSession.Start();
        await session.CallAsync("system.ping");

        var code = await session.CancelAsync();

        Assert.Equal(130, code);
    }

    [Fact]
    [Trait("Task", "4.1")]
    public async Task WhenTheOutputPipeIsClosedByThePeer_TheServiceStopsAndExitsWithOne()
    {
        using var temp = new EmailFileViewer.Core.Tests.TempDirectory();
        var input = new MemoryStream(Encoding.UTF8.GetBytes("""{"id":1,"method":"system.ping"}""" + "\n"));

        var code = await StdioCommand.RunAsync(
            input,
            new BrokenPipeStream(),
            TextWriter.Null,
            new StdioOptions { SessionRoot = Path.Combine(temp.DirectoryPath, "sessions") });

        Assert.Equal(1, code);
        Eventually.HasNoSubdirectories(Path.Combine(temp.DirectoryPath, "sessions"));
    }

    [Fact]
    [Trait("Task", "4.1")]
    public async Task ARequestWithoutAnyInput_JustExitsCleanly()
    {
        await using var session = SidecarSession.Start();

        var code = await session.CompleteAsync();

        Assert.Equal(0, code);
        Assert.Empty(session.Lines);
        Assert.Empty(session.Error.ToString());
    }
}

// 写入时总是失败的输出流，模拟对方（Rust 端）已经关闭了管道
file sealed class BrokenPipeStream : Stream
{
    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new IOException("Broken pipe");

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => throw new IOException("Broken pipe");
}
