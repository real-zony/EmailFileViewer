using System.Text;
using System.Text.Json;
using EmailFileViewer.Sidecar.Documents;

namespace EmailFileViewer.Sidecar.Protocol;

/// <summary>
/// stdio 上的 JSON 行协议宿主：逐行读请求，分发给方法，把响应一行一行写回去。
/// </summary>
/// <remarks>
/// <para>输出流只写协议消息；诊断信息写到 <c>diagnostics</c>（进程里就是 stderr）。</para>
/// <para>并发：不同请求可以同时处理，响应的顺序不一定和请求一致，调用方按 <c>id</c> 对应。
/// 针对同一个文档的请求放进该文档的队列，一次只执行一个，并且按到达的顺序执行（见 <see cref="DocumentEntry.Enqueue"/>）。
/// 写输出时加锁，保证每条消息完整地占一行。</para>
/// <para>单个请求的失败（含未处理的异常）只变成这个请求的错误响应，进程继续运行。</para>
/// </remarks>
internal sealed class ProtocolHost
{
    private const int ReadBufferSize = 64 * 1024;

    private readonly MethodTable _methods;
    private readonly Func<string, DocumentEntry?> _findDocument;
    private readonly TextWriter _diagnostics;
    private readonly SemaphoreSlim _concurrency;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly object _pendingLock = new();
    private readonly HashSet<Task> _pending = [];

    private Stream _output = Stream.Null;
    private CancellationTokenSource _stop = new();
    private bool _outputBroken;

    /// <summary>
    /// 创建宿主。
    /// </summary>
    /// <param name="methods">方法表。</param>
    /// <param name="findDocument">按 <c>docId</c> 找文档，用来把针对同一个文档的请求排进它的队列。</param>
    /// <param name="diagnostics">诊断日志的输出，只在发生未预期的异常时写入。</param>
    /// <param name="maxConcurrency">同时处理的请求个数上限；<c>null</c> 时按处理器个数（2 到 8）。</param>
    internal ProtocolHost(MethodTable methods, Func<string, DocumentEntry?> findDocument, TextWriter diagnostics, int? maxConcurrency = null)
    {
        _methods = methods;
        _findDocument = findDocument;
        _diagnostics = diagnostics;
        _concurrency = new SemaphoreSlim(maxConcurrency ?? Math.Clamp(Environment.ProcessorCount, 2, 8));
    }

    /// <summary>
    /// 处理请求，直到输入结束或被取消。
    /// </summary>
    /// <remarks>
    /// 输入读到结尾（EOF）后，先等已经收到的请求全部处理完并写出响应，再返回。被取消时，进行中的请求也会被取消。
    /// </remarks>
    /// <param name="input">请求输入，每行一个 JSON，UTF-8。</param>
    /// <param name="output">响应输出，每行一个 JSON，UTF-8，不带 BOM。</param>
    /// <param name="cancellationToken">取消令牌（Ctrl+C、结束信号）。</param>
    /// <returns>退出码：输入正常结束为 0；被取消为 130；输出管道被对方关闭为 1。</returns>
    internal async Task<int> RunAsync(Stream input, Stream output, CancellationToken cancellationToken = default)
    {
        _output = output;
        _stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var canceled = false;

        try
        {
            using var reader = new StreamReader(input, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), detectEncodingFromByteOrderMarks: true, ReadBufferSize, leaveOpen: true);
            while (true)
            {
                var line = await ReadLineAsync(reader, _stop.Token);
                if (line is null)
                {
                    break;
                }

                if (!string.IsNullOrWhiteSpace(line))
                {
                    Dispatch(line);
                }
            }
        }
        catch (OperationCanceledException)
        {
            canceled = true;
        }
        catch (IOException)
        {
            // 输入管道出错，按输入结束处理
        }

        await DrainAsync();
        _stop.Dispose();

        if (_outputBroken)
        {
            return 1;
        }

        return canceled ? 130 : 0;
    }

    // 控制台输入的读取不能被取消，这里在取消时放弃等待（进程随后就会退出）
    private static async Task<string?> ReadLineAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var read = reader.ReadLineAsync(cancellationToken).AsTask();
        if (read.IsCompleted)
        {
            return await read;
        }

        var canceled = new TaskCompletionSource();
        await using (cancellationToken.Register(() => canceled.TrySetResult()))
        {
            if (await Task.WhenAny(read, canceled.Task) == read)
            {
                return await read;
            }
        }

        throw new OperationCanceledException(cancellationToken);
    }

    private void Dispatch(string line)
    {
        if (!RequestParser.TryParse(line, out var request, out var parseError))
        {
            Reply(ResponseWriter.Error(parseError.Id, ErrorCodes.InvalidRequest, parseError.Message));
            return;
        }

        if (!_methods.TryGet(request.Method, out var method))
        {
            Reply(ResponseWriter.Error(request.Id, ErrorCodes.MethodNotFound, $"没有方法 \"{request.Method}\"。"));
            return;
        }

        PreparedCall call;
        try
        {
            call = method.Prepare(request.Params);
        }
        catch (ProtocolException ex)
        {
            Reply(ResponseWriter.Error(request.Id, ex.Code, ex.Message));
            return;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogUnhandled(method.Name, request.Id, ex);
            Reply(ResponseWriter.Error(request.Id, ErrorCodes.InternalError, InternalErrorMessage));
            return;
        }

        Task Work() => ExecuteAsync(request, method.Name, call, _stop.Token);

        if (call.DocumentId is null)
        {
            Track(Task.Run(Work));
            return;
        }

        var document = _findDocument(call.DocumentId);
        if (document is null)
        {
            Reply(ResponseWriter.Error(request.Id, ErrorCodes.DocumentNotFound, DocumentEntry.DocumentNotFound(call.DocumentId).Message));
            return;
        }

        Track(document.Enqueue(Work));
    }

    private async Task ExecuteAsync(ProtocolRequest request, string methodName, PreparedCall call, CancellationToken cancellationToken)
    {
        byte[] response;

        try
        {
            await _concurrency.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            var writeResult = await call.RunAsync(cancellationToken);
            response = ResponseWriter.Success(request.Id, writeResult);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (ProtocolException ex)
        {
            response = ResponseWriter.Error(request.Id, ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            LogUnhandled(methodName, request.Id, ex);
            response = ResponseWriter.Error(request.Id, ErrorCodes.InternalError, InternalErrorMessage);
        }
        finally
        {
            _concurrency.Release();
        }

        await SendAsync(response, cancellationToken);
    }

    private const string InternalErrorMessage = "处理请求时发生内部错误，详情见 sidecar 的 stderr。";

    // 响应放到线程池上写，免得读请求的循环卡在一个写不出去的管道上
    private void Reply(byte[] response)
    {
        var cancellationToken = _stop.Token;
        Track(Task.Run(() => SendAsync(response, cancellationToken)));
    }

    // 每条消息一次写完，并且加锁：并发的响应不会交错。已经在停止（被取消或管道已断）时不再写
    private async Task SendAsync(byte[] line, CancellationToken cancellationToken)
    {
        try
        {
            await _writeLock.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            await _output.WriteAsync(line, cancellationToken);
            await _output.FlushAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // 正在停止，响应没人要了
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // 对方已经关闭了管道，没人再读响应了：停止接收和处理
            _outputBroken = true;
            await _stop.CancelAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private void LogUnhandled(string methodName, JsonElement id, Exception exception)
    {
        try
        {
            _diagnostics.WriteLine($"[sidecar] 请求处理失败 method={methodName} id={id.GetRawText()}\n{exception}");
        }
        catch (IOException)
        {
            // stderr 写不出去也不能影响协议
        }
    }

    private void Track(Task task)
    {
        lock (_pendingLock)
        {
            _pending.Add(task);
        }

        task.ContinueWith(
            finished =>
            {
                lock (_pendingLock)
                {
                    _pending.Remove(finished);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task DrainAsync()
    {
        while (true)
        {
            Task[] pending;
            lock (_pendingLock)
            {
                _pending.RemoveWhere(task => task.IsCompleted);
                pending = [.. _pending];
            }

            if (pending.Length == 0)
            {
                return;
            }

            await Task.WhenAll(pending);
        }
    }
}
