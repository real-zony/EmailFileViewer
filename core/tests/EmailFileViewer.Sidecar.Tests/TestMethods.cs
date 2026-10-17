using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using EmailFileViewer.Sidecar.Protocol;

namespace EmailFileViewer.Sidecar.Tests;

/// <summary>针对某个文档的测试方法的参数。</summary>
internal sealed record TestDocumentParams : IDocumentParams
{
    public string? DocId { get; init; }

    public int DelayMilliseconds { get; init; }

    public void Validate() => ParamsCheck.Require(DocId, "docId");
}

/// <summary>不针对文档的测试方法的参数。</summary>
internal sealed record TestPlainParams : IMethodParams
{
    public void Validate()
    {
    }
}

/// <summary>测试方法的结果。</summary>
internal sealed record TestResult(int Value);

/// <summary>
/// 测试里额外注册的协议方法：故意抛异常的方法，以及用来检查并发与串行的方法。
/// </summary>
internal sealed class TestMethods
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    private int _running;
    private int _maxRunningPerCall;
    private int _rendezvous;
    private readonly TaskCompletionSource _allArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _eventsLock = new();
    private readonly List<string> _events = [];

    /// <summary>同一个文档上同时在执行的 <c>test.exclusive</c> 个数的最大值。</summary>
    public int MaxConcurrentExclusive => _maxRunningPerCall;

    /// <summary>按发生顺序记录的事件。</summary>
    public IReadOnlyList<string> Events
    {
        get
        {
            lock (_eventsLock)
            {
                return [.. _events];
            }
        }
    }

    /// <summary>只注册一个会抛异常的方法。</summary>
    public static void AddThrowing(MethodTable table)
    {
        table.Add<TestPlainParams, TestResult>(
            "test.throw",
            (JsonTypeInfo<TestPlainParams>)Options.GetTypeInfo(typeof(TestPlainParams)),
            (JsonTypeInfo<TestResult>)Options.GetTypeInfo(typeof(TestResult)),
            (_, _) => throw new InvalidOperationException("deliberate failure for the test"));
    }

    /// <summary>注册全部测试方法。</summary>
    public void Register(MethodTable table)
    {
        AddThrowing(table);

        var documentParams = (JsonTypeInfo<TestDocumentParams>)Options.GetTypeInfo(typeof(TestDocumentParams));
        var plainParams = (JsonTypeInfo<TestPlainParams>)Options.GetTypeInfo(typeof(TestPlainParams));
        var result = (JsonTypeInfo<TestResult>)Options.GetTypeInfo(typeof(TestResult));

        // 同一个文档上的调用必须一个接一个执行：记录同时执行的最大个数
        table.Add("test.exclusive", documentParams, result, async (parameters, cancellationToken) =>
        {
            var running = Interlocked.Increment(ref _running);
            InterlockedMax(ref _maxRunningPerCall, running);
            Record($"start {parameters.DelayMilliseconds}");
            await Task.Delay(parameters.DelayMilliseconds, cancellationToken);
            Record($"end {parameters.DelayMilliseconds}");
            Interlocked.Decrement(ref _running);
            return new TestResult(parameters.DelayMilliseconds);
        });

        // 两个调用必须同时在执行才都能结束：如果实现把请求全部串行了，这里会一直等下去
        table.Add("test.rendezvous", plainParams, result, async (_, cancellationToken) =>
        {
            if (Interlocked.Increment(ref _rendezvous) == 2)
            {
                _allArrived.TrySetResult();
            }

            await _allArrived.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            return new TestResult(2);
        });

        // 一直等到被取消（最多 30 秒）：用来检查输入结束后进行中的请求会被取消
        table.Add("test.wait", plainParams, result, async (_, cancellationToken) =>
        {
            Record("wait started");
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Record("wait canceled");
                throw;
            }

            return new TestResult(30);
        });

        // 不理会取消，一直占着（模拟无法中断的解析）：最多 15 秒
        table.Add("test.block", plainParams, result, (_, _) =>
        {
            Record("block started");
            return Task.Run(() =>
            {
                Thread.Sleep(TimeSpan.FromSeconds(15));
                return new TestResult(15);
            });
        });

        table.Add("test.rendezvous.document", documentParams, result, async (_, cancellationToken) =>
        {
            if (Interlocked.Increment(ref _rendezvous) == 2)
            {
                _allArrived.TrySetResult();
            }

            await _allArrived.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            return new TestResult(2);
        });
    }

    private void Record(string text)
    {
        lock (_eventsLock)
        {
            _events.Add(text);
        }
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        do
        {
            current = Volatile.Read(ref target);
            if (value <= current)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref target, value, current) != current);
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver() };
        options.MakeReadOnly();
        return options;
    }
}
