using System.IO.Pipelines;
using System.Text;
using System.Text.Json.Nodes;
using EmailFileViewer.Core.Tests;

namespace EmailFileViewer.Sidecar.Tests;

/// <summary>
/// 在内存管道上运行一个 sidecar 协议服务，像 Rust 端那样发请求、按 id 等响应。
/// </summary>
/// <remarks>
/// 不起进程：直接把两条内存管道接到 <see cref="StdioCommand"/> 上。会话目录放在测试自己的临时目录里，不碰真实的系统临时目录。
/// </remarks>
internal sealed class SidecarSession : IAsyncDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly Pipe _requests = new();
    private readonly Pipe _responses = new();
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task<int> _run;
    private readonly Task _readResponses;
    private readonly object _sync = new();
    private readonly Dictionary<string, TaskCompletionSource<JsonObject>> _waiting = [];
    private readonly List<string> _lines = [];
    private int _nextId;
    private bool _completed;

    private SidecarSession(StdioOptions? options)
    {
        Temp = new TempDirectory();
        SessionRoot = options?.SessionRoot ?? Path.Combine(Temp.DirectoryPath, "sessions");
        var effective = (options ?? new StdioOptions()) with { SessionRoot = SessionRoot };

        _run = Task.Run(async () =>
        {
            try
            {
                return await StdioCommand.RunAsync(_requests.Reader.AsStream(), _responses.Writer.AsStream(), TextWriter.Synchronized(Error), effective, _cancellation.Token);
            }
            finally
            {
                await _responses.Writer.CompleteAsync();
            }
        });
        _readResponses = Task.Run(ReadResponsesAsync);
    }

    /// <summary>测试用的临时目录，会话目录和保存的文件都放在里面。</summary>
    public TempDirectory Temp { get; }

    /// <summary>会话目录的父目录。</summary>
    public string SessionRoot { get; }

    /// <summary>sidecar 写到 stderr 的内容。</summary>
    public StringWriter Error { get; } = new();

    /// <summary>已经收到的全部输出行（不含换行）。</summary>
    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (_sync)
            {
                return [.. _lines];
            }
        }
    }

    /// <summary>启动一个会话。</summary>
    public static SidecarSession Start(StdioOptions? options = null) => new(options);

    /// <summary>发一个请求并等它的响应。</summary>
    public async Task<JsonObject> CallAsync(string method, JsonObject? parameters = null)
    {
        var id = Interlocked.Increment(ref _nextId);
        return await CallWithIdAsync(JsonValue.Create(id), method, parameters);
    }

    /// <summary>用指定的 id 发一个请求并等它的响应。</summary>
    public async Task<JsonObject> CallWithIdAsync(JsonNode id, string method, JsonObject? parameters = null)
    {
        var request = new JsonObject { ["id"] = id.DeepClone(), ["method"] = method };
        if (parameters is not null)
        {
            request["params"] = parameters;
        }

        var key = id.ToJsonString();
        var waiting = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_sync)
        {
            _waiting.Add(key, waiting);
        }

        await SendRawAsync(request.ToJsonString());
        return await waiting.Task.WaitAsync(Timeout);
    }

    /// <summary>发送一行原始文本（不一定是合法的请求）。</summary>
    public async Task SendRawAsync(string line)
    {
        await _requests.Writer.WriteAsync(Encoding.UTF8.GetBytes(line + "\n"));
    }

    /// <summary>等到收到至少 <paramref name="count"/> 行没有对应请求的响应（<c>id</c> 为 null）并返回它们。</summary>
    public async Task<IReadOnlyList<JsonObject>> WaitForNullIdResponsesAsync(int count)
    {
        var deadlineUtc = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadlineUtc)
        {
            var found = Lines.Select(line => JsonNode.Parse(line)!.AsObject()).Where(response => response["id"] is null).ToList();
            if (found.Count >= count)
            {
                return found;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("没有收到预期数量的 id 为 null 的响应。");
    }

    /// <summary>关闭输入（相当于 stdin 到了结尾），等服务退出，返回退出码。</summary>
    public async Task<int> CompleteAsync()
    {
        _completed = true;
        await _requests.Writer.CompleteAsync();
        var code = await _run.WaitAsync(Timeout);
        await _readResponses.WaitAsync(Timeout);
        return code;
    }

    /// <summary>取消服务（相当于收到 Ctrl+C 或结束信号），返回退出码。</summary>
    public async Task<int> CancelAsync()
    {
        _completed = true;
        await _cancellation.CancelAsync();
        var code = await _run.WaitAsync(Timeout);
        await _readResponses.WaitAsync(Timeout);
        return code;
    }

    /// <summary>取响应里的 <c>result</c>，响应是错误时让测试失败。</summary>
    public static JsonObject Result(JsonObject response)
    {
        Assert.True(response["error"] is null, $"预期成功，实际是错误：{response.ToJsonString()}");
        return response["result"]!.AsObject();
    }

    /// <summary>取错误响应的错误码，响应是成功时让测试失败。</summary>
    public static string ErrorCode(JsonObject response)
    {
        Assert.True(response["result"] is null, $"预期错误，实际成功：{response.ToJsonString()}");
        return (string)response["error"]!["code"]!;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (!_completed)
        {
            try
            {
                await CancelAsync();
            }
            catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
            {
                // 测试已经失败，不要再掩盖原来的错误
            }
        }

        Temp.Dispose();
    }

    private async Task ReadResponsesAsync()
    {
        using var reader = new StreamReader(_responses.Reader.AsStream(), new UTF8Encoding(false));
        while (await reader.ReadLineAsync() is { } line)
        {
            lock (_sync)
            {
                _lines.Add(line);
            }

            JsonObject response;
            try
            {
                response = JsonNode.Parse(line)!.AsObject();
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException)
            {
                // 输出里出现了不是 JSON 对象的行：让所有在等响应的测试立刻失败，而不是等到超时
                lock (_sync)
                {
                    foreach (var pending in _waiting.Values)
                    {
                        pending.TrySetException(new InvalidOperationException($"stdout 上有不是 JSON 对象的行：{line}", ex));
                    }
                }

                throw;
            }

            var key = response["id"]?.ToJsonString() ?? "null";

            TaskCompletionSource<JsonObject>? waiting;
            lock (_sync)
            {
                _waiting.Remove(key, out waiting);
            }

            waiting?.TrySetResult(response);
        }
    }
}
