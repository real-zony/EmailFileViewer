using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EmailFileViewer.Sidecar.Documents;
using EmailFileViewer.Sidecar.Methods;
using EmailFileViewer.Sidecar.Protocol;
using EmailFileViewer.Sidecar.Sessions;

namespace EmailFileViewer.Sidecar.Tests;

/// <summary>
/// 任务 4.4：<c>core/docs/sidecar-protocol.md</c> 里的示例消息能被协议实现原样解析，文档和实现保持一致。
/// </summary>
/// <remarks>
/// 文档里的 JSON 代码块用围栏的说明文字标注种类：<c>json request</c>、<c>json response &lt;方法名&gt;</c>、<c>json error</c>。
/// 紧跟在请求后面的那个块就是它的响应（成功或错误）。
/// </remarks>
public partial class ProtocolDocumentTests
{
    private sealed record Example(string Kind, string? Method, string Json, int Line)
    {
        public JsonObject Parse() => JsonNode.Parse(Json)!.AsObject();
    }

    [GeneratedRegex(@"^```json(?:\s+(?<kind>request|response|error)(?:\s+(?<method>[\w.]+))?)?\s*$")]
    private static partial Regex JsonFence();

    [GeneratedRegex(@"^### `(?<name>[\w.]+)`\s*$")]
    private static partial Regex MethodHeading();

    [GeneratedRegex(@"^\| `(?<code>[a-z_]+)` \|")]
    private static partial Regex ErrorCodeRow();

    [Fact]
    [Trait("Task", "4.4")]
    public void EveryJsonBlock_IsTaggedAndIsValidJson()
    {
        var examples = ReadExamples();

        Assert.NotEmpty(examples);
        foreach (var example in examples)
        {
            var parsed = JsonNode.Parse(example.Json);
            Assert.True(parsed is JsonObject, $"文档第 {example.Line} 行的示例不是 JSON 对象");
        }
    }

    [Fact]
    [Trait("Task", "4.4")]
    public void EveryRequestExample_ParsesAsAProtocolRequest_AndIsFollowedByItsResponse()
    {
        var examples = ReadExamples();
        var table = CreateMethodTable();
        var requests = examples.Where(example => example.Kind == "request").ToList();
        Assert.True(requests.Count >= 10, "文档里应该有每个方法的请求示例");

        foreach (var request in requests)
        {
            Assert.True(RequestParser.TryParse(request.Json, out var parsed, out var error), $"文档第 {request.Line} 行的请求无法解析：{error?.Message}");

            var next = examples[examples.IndexOf(request) + 1];
            Assert.True(next.Kind is "response" or "error", $"文档第 {request.Line} 行的请求后面应该紧跟它的响应");
            Assert.Equal(parsed.Id.GetRawText(), next.Parse()["id"]!.ToJsonString());

            // 响应是 invalid_params / method_not_found 的，请求本来就应该被拒绝；其余的请求必须是有效的
            var expectedCode = next.Kind == "error" ? (string)next.Parse()["error"]!["code"]! : null;
            if (expectedCode == ErrorCodes.MethodNotFound)
            {
                Assert.False(table.TryGet(parsed.Method, out _), $"文档第 {request.Line} 行：方法 {parsed.Method} 不应该存在");
                continue;
            }

            Assert.True(table.TryGet(parsed.Method, out var method), $"文档第 {request.Line} 行：没有方法 {parsed.Method}");
            var parameters = WithAbsolutePathsForThisPlatform(parsed.Params);
            if (expectedCode == ErrorCodes.InvalidParams)
            {
                var rejected = Assert.Throws<ProtocolException>(() => method.Prepare(parameters));
                Assert.Equal(ErrorCodes.InvalidParams, rejected.Code);
            }
            else
            {
                var call = method.Prepare(parameters);
                if (parsed.Method is "attachment.save" or "attachment.saveAll" or "attachment.extractForOpen" or "email.openNested" or "document.close")
                {
                    Assert.False(string.IsNullOrEmpty(call.DocumentId), $"文档第 {request.Line} 行：{parsed.Method} 应该针对某个文档");
                }
            }

            if (next.Kind == "response")
            {
                Assert.Equal(parsed.Method, next.Method);
            }
        }
    }

    [Fact]
    [Trait("Task", "4.4")]
    public void EveryResponseExample_RoundTripsThroughTheProtocolDtos_WithoutLosingOrInventingFields()
    {
        var table = CreateMethodTable();
        var responses = ReadExamples().Where(example => example.Kind == "response").ToList();
        Assert.True(responses.Count >= 7, "文档里应该有每个方法的成功响应示例");

        foreach (var example in responses)
        {
            Assert.True(table.TryGet(example.Method!, out var method), $"文档第 {example.Line} 行：没有方法 {example.Method}");
            var response = example.Parse();
            Assert.Null(response["error"]);
            Assert.Contains(response["id"]!.GetValueKind(), new[] { JsonValueKind.Number, JsonValueKind.String });

            var result = response["result"]!;
            var dto = JsonSerializer.Deserialize(result.ToJsonString(), method.ResultTypeInfo);
            Assert.NotNull(dto);

            // 反序列化成 DTO 再序列化回来，要和文档里写的完全一样：文档里多写的字段、少写的字段、写错的字段名和类型，都会在这里暴露
            var roundTrip = JsonNode.Parse(JsonSerializer.Serialize(dto, method.ResultTypeInfo))!;
            Assert.True(JsonNode.DeepEquals(result, roundTrip), $"文档第 {example.Line} 行的 {example.Method} 响应与协议 DTO 不一致。\n文档：{result.ToJsonString()}\nDTO：{roundTrip.ToJsonString()}");
        }
    }

    [Fact]
    [Trait("Task", "4.4")]
    public void EveryErrorExample_UsesAKnownErrorCode_AndAValidId()
    {
        var errors = ReadExamples().Where(example => example.Kind == "error").ToList();
        Assert.NotEmpty(errors);

        foreach (var example in errors)
        {
            var response = example.Parse();
            Assert.Null(response["result"]);
            var id = response["id"];
            Assert.True(id is null || id.GetValueKind() is JsonValueKind.Number or JsonValueKind.String, $"文档第 {example.Line} 行的 id 类型不对");
            Assert.Contains((string)response["error"]!["code"]!, ErrorCodes.All);
            Assert.False(string.IsNullOrWhiteSpace((string)response["error"]!["message"]!));
            Assert.Equal(["code", "message"], response["error"]!.AsObject().Select(property => property.Key).Order().ToArray());
        }
    }

    [Fact]
    [Trait("Task", "4.4")]
    public async Task TheExamplesThatCanBeReplayed_ProduceExactlyTheResponseInTheDocument()
    {
        var examples = ReadExamples();
        await using var session = SidecarSession.Start();

        // 请求 → 响应：ping、方法不存在、参数不对，响应（含 message）和文档逐字段一致
        var replayed = 0;
        foreach (var request in examples.Where(example => example.Kind == "request"))
        {
            var expected = examples[examples.IndexOf(request) + 1];
            var expectedBody = expected.Parse();
            var isReplayable = expected is { Kind: "response", Method: "system.ping" }
                || (expected.Kind == "error" && (string)expectedBody["error"]!["code"]! is ErrorCodes.MethodNotFound or ErrorCodes.InvalidParams);
            if (!isReplayable)
            {
                continue;
            }

            var parsed = request.Parse();
            var actual = await session.CallWithIdAsync(parsed["id"]!.DeepClone(), (string)parsed["method"]!, parsed["params"]?.DeepClone().AsObject());
            Assert.True(JsonNode.DeepEquals(expectedBody, actual), $"文档第 {request.Line} 行的示例与实际响应不一致。\n文档：{expectedBody.ToJsonString()}\n实际：{actual.ToJsonString()}");
            replayed++;
        }

        Assert.Equal(4, replayed);

        // 无法解析的行：文档里的 text 块之后紧跟的错误响应
        var lines = await File.ReadAllLinesAsync(FindDocument());
        var garbageIndex = Array.IndexOf(lines, "this is not json");
        Assert.True(garbageIndex > 0, "文档里应该有一个无法解析的行的示例");
        await session.SendRawAsync(lines[garbageIndex]);
        var nullIdResponse = (await session.WaitForNullIdResponsesAsync(1))[0];
        var documented = examples.First(e => e.Kind == "error" && e.Line > garbageIndex).Parse();
        Assert.True(JsonNode.DeepEquals(documented, nullIdResponse), $"无法解析的行的示例与实际响应不一致。\n文档：{documented.ToJsonString()}\n实际：{nullIdResponse.ToJsonString()}");
    }

    [Fact]
    [Trait("Task", "4.4")]
    public void EveryMethod_IsDocumented_AndEveryDocumentedMethodExists()
    {
        var table = CreateMethodTable();
        var examples = ReadExamples();
        var headings = File.ReadAllLines(FindDocument())
            .Select(line => MethodHeading().Match(line))
            .Where(match => match.Success)
            .Select(match => match.Groups["name"].Value)
            .ToList();

        Assert.Equal(table.All.Select(method => method.Name).Order().ToArray(), headings.Order().ToArray());
        foreach (var method in table.All)
        {
            Assert.Contains(examples, example => example.Kind == "request" && example.Parse()["method"]!.ToString() == method.Name);
            Assert.Contains(examples, example => example.Kind == "response" && example.Method == method.Name);
        }
    }

    [Fact]
    [Trait("Task", "4.4")]
    public void TheErrorCodeTable_ListsExactlyTheErrorCodesOfTheImplementation()
    {
        // 只看“错误码”这一节的表格（EmailDocument 一节的表格里也有用反引号括起来的字段名）
        var section = File.ReadAllLines(FindDocument())
            .SkipWhile(line => !line.StartsWith("## 3.", StringComparison.Ordinal))
            .Skip(1)
            .TakeWhile(line => !line.StartsWith("## ", StringComparison.Ordinal));
        var documented = section
            .Select(line => ErrorCodeRow().Match(line))
            .Where(match => match.Success)
            .Select(match => match.Groups["code"].Value)
            .ToList();

        Assert.Equal(ErrorCodes.All.Order().ToArray(), documented.Order().ToArray());
        Assert.Equal(documented.Count, documented.Distinct().Count());
        Assert.True(documented.Count >= 10);
    }

    [Fact]
    [Trait("Task", "4.4")]
    public void TheDocument_StatesTheCurrentProtocolVersion()
    {
        var text = File.ReadAllText(FindDocument());

        Assert.Contains($"当前协议版本：**{ProtocolInfo.Version}**", text);
    }

    [Fact]
    [Trait("Task", "4.4")]
    public void TheExamples_AreReadTheSameFromACrLfCopyOfTheDocument()
    {
        // Windows 的 CI 上 git 会把文本文件检出成 CRLF：文档里的示例和说明不能因为换行不同而读成别的内容
        using var temp = new EmailFileViewer.Core.Tests.TempDirectory();
        var crlfCopy = Path.Combine(temp.DirectoryPath, "sidecar-protocol.md");
        File.WriteAllText(crlfCopy, File.ReadAllText(FindDocument()).ReplaceLineEndings("\r\n"));
        Assert.Contains("\r\n", File.ReadAllText(crlfCopy));

        var original = ReadExamples(FindDocument());
        var fromCrLf = ReadExamples(crlfCopy);

        Assert.Equal(original, fromCrLf);
    }

    [Fact]
    [Trait("Task", "4.4")]
    public void TheDocument_SaysThatEveryMessageEndsWithLfOnEveryPlatform()
    {
        var text = File.ReadAllText(FindDocument());

        Assert.Contains("行尾固定是 `\\n`", text);
        Assert.Contains("Windows", text);
    }

    // 文档里的示例路径是 /Users/...：在 Windows 上这不是完整路径（没有盘符），先换成当前平台的绝对路径再检查参数
    private static JsonElement? WithAbsolutePathsForThisPlatform(JsonElement? parameters)
    {
        if (parameters is null)
        {
            return null;
        }

        var node = JsonNode.Parse(parameters.Value.GetRawText())!.AsObject();
        foreach (var property in node.ToList())
        {
            if (property.Value is JsonValue value
                && value.TryGetValue<string>(out var text)
                && text.StartsWith('/')
                && !Path.IsPathFullyQualified(text))
            {
                node[property.Key] = Path.GetFullPath(text);
            }
        }

        using var document = JsonDocument.Parse(node.ToJsonString());
        return document.RootElement.Clone();
    }

    private static MethodTable CreateMethodTable()
    {
        return new SidecarMethods(new DocumentRegistry(), new SessionDirectory(Path.Combine(Path.GetTempPath(), "efv-doc-test-never-created")), new AttachmentFiles()).CreateTable();
    }

    private static string FindDocument()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine(directory.FullName, "docs", "sidecar-protocol.md");
            if (File.Exists(path))
            {
                return path;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("在上级目录中找不到 docs/sidecar-protocol.md");
    }

    private static List<Example> ReadExamples() => ReadExamples(FindDocument());

    private static List<Example> ReadExamples(string path)
    {
        var lines = File.ReadAllLines(path);
        var examples = new List<Example>();

        for (var i = 0; i < lines.Length; i++)
        {
            var fence = lines[i].StartsWith("```json", StringComparison.Ordinal) ? JsonFence().Match(lines[i]) : null;
            if (fence is null)
            {
                continue;
            }

            Assert.True(fence.Success && fence.Groups["kind"].Success, $"文档第 {i + 1} 行：json 代码块必须标注种类（request / response <方法名> / error）：{lines[i]}");
            var start = i + 1;
            var end = Array.IndexOf(lines, "```", start);
            Assert.True(end > start, $"文档第 {i + 1} 行的代码块没有结束");

            var kind = fence.Groups["kind"].Value;
            var method = fence.Groups["method"].Success ? fence.Groups["method"].Value : null;
            Assert.True(kind != "response" || method is not null, $"文档第 {i + 1} 行：response 示例要写明是哪个方法的响应");
            examples.Add(new Example(kind, method, string.Join('\n', lines[start..end]), i + 1));
            i = end;
        }

        return examples;
    }
}
