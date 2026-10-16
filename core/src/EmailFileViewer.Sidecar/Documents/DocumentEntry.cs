using EmailFileViewer.Core;
using EmailFileViewer.Sidecar.Protocol;

namespace EmailFileViewer.Sidecar.Documents;

/// <summary>
/// 一个已打开的文档：解析结果，加上同一文档操作的串行队列。
/// </summary>
/// <remarks>
/// <see cref="ParsedEmail"/> 不保证线程安全，所以针对同一个文档的操作都通过 <see cref="Enqueue"/> 排队，一次只执行一个，
/// 并且按进队的先后顺序（也就是请求到达的先后顺序）执行；不同文档的操作互不影响，可以并发。
/// </remarks>
internal sealed class DocumentEntry
{
    private readonly object _queueLock = new();
    private Task _tail = Task.CompletedTask;

    internal DocumentEntry(string docId, ParsedEmail parsed)
    {
        DocId = docId;
        Parsed = parsed;
    }

    /// <summary>
    /// 文档 Id。
    /// </summary>
    internal string DocId { get; }

    /// <summary>
    /// 解析结果。只能在 <see cref="Enqueue"/> 排的队里访问。
    /// </summary>
    internal ParsedEmail Parsed { get; }

    /// <summary>
    /// 已经提取到临时目录的附件：附件 Id 到文件路径。只能在队列里访问。
    /// </summary>
    internal Dictionary<string, string> ExtractedFiles { get; } = [];

    /// <summary>
    /// 文档已经关闭。排在关闭请求后面的操作看到它为 <c>true</c>，要按“文档不存在”处理。
    /// </summary>
    internal bool IsClosed { get; private set; }

    /// <summary>
    /// 把一个操作排进这个文档的队列。
    /// </summary>
    /// <param name="work">要执行的操作，自己负责处理异常，不应抛出。</param>
    /// <returns>操作完成时结束的任务。</returns>
    internal Task Enqueue(Func<Task> work)
    {
        lock (_queueLock)
        {
            _tail = RunAfterAsync(_tail, work);
            return _tail;
        }
    }

    /// <summary>
    /// 确认文档还没有关闭，已经关闭则抛出“文档不存在”。
    /// </summary>
    /// <exception cref="ProtocolException">文档已经关闭。</exception>
    internal void EnsureOpen()
    {
        if (IsClosed)
        {
            throw DocumentNotFound(DocId);
        }
    }

    /// <summary>
    /// 关闭文档并释放解析结果。只能在队列里调用。
    /// </summary>
    internal void Close()
    {
        if (IsClosed)
        {
            return;
        }

        IsClosed = true;
        Parsed.Dispose();
    }

    /// <summary>
    /// 构造“文档不存在”错误。
    /// </summary>
    /// <param name="docId">请求里的文档 Id。</param>
    /// <returns>异常，调用方抛出。</returns>
    internal static ProtocolException DocumentNotFound(string docId)
    {
        return new ProtocolException(ErrorCodes.DocumentNotFound, $"文档 \"{docId}\" 不存在，可能没有打开过或者已经关闭。");
    }

    private static async Task RunAfterAsync(Task previous, Func<Task> work)
    {
        try
        {
            await previous;
        }
        catch (Exception)
        {
            // 前一个操作的失败已经由它自己回复，不影响后面的操作
        }

        await work();
    }
}
