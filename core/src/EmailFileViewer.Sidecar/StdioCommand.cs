using EmailFileViewer.Sidecar.Documents;
using EmailFileViewer.Sidecar.Methods;
using EmailFileViewer.Sidecar.Protocol;
using EmailFileViewer.Sidecar.Sessions;

namespace EmailFileViewer.Sidecar;

/// <summary>
/// <see cref="StdioCommand"/> 的可调参数，主要给测试用。
/// </summary>
internal sealed record StdioOptions
{
    /// <summary>会话目录的父目录；<c>null</c> 时用系统临时目录。</summary>
    internal string? SessionRoot { get; init; }

    /// <summary>注册额外的方法（测试里用来加一个会抛异常的方法）。</summary>
    internal Action<MethodTable>? ConfigureMethods { get; init; }

    /// <summary>同时处理的请求个数上限；<c>null</c> 时按处理器个数。</summary>
    internal int? MaxConcurrency { get; init; }

    /// <summary>判断路径上是否已有文件或目录；<c>null</c> 时查文件系统（测试里用来模拟取名之后被抢先创建同名文件）。</summary>
    internal Func<string, bool>? PathExists { get; init; }

    /// <summary>输入结束后给进行中的请求的宽限时间；<c>null</c> 时是 2 秒（测试里用来缩短等待）。</summary>
    internal TimeSpan? InputClosedGrace { get; init; }
}

/// <summary>
/// <c>--stdio</c> 命令：作为 JSON 行协议的服务端运行，直到 stdin 结束。
/// </summary>
/// <remarks>
/// 启动时创建本次会话的临时目录并清理其他会话留下的目录；stdin 读到结尾后，给已收到的请求一小段时间处理完，
/// 仍没完成的请求被取消，然后关闭所有文档、删除本次会话的临时目录，正常退出。
/// </remarks>
internal static class StdioCommand
{
    /// <summary>
    /// 运行协议服务。
    /// </summary>
    /// <param name="input">请求输入（进程里是 stdin）。</param>
    /// <param name="output">响应输出（进程里是 stdout），只写协议消息。</param>
    /// <param name="error">诊断日志（进程里是 stderr）。</param>
    /// <param name="options">可调参数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>退出码，见 <see cref="ProtocolHost.RunAsync"/>。</returns>
    internal static async Task<int> RunAsync(
        Stream input,
        Stream output,
        TextWriter error,
        StdioOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        using var session = new SessionDirectory(options?.SessionRoot);
        session.Initialize();

        var registry = new DocumentRegistry();
        try
        {
            var methods = new SidecarMethods(registry, session, new AttachmentFiles(options?.PathExists)).CreateTable();
            options?.ConfigureMethods?.Invoke(methods);

            var host = new ProtocolHost(methods, registry.Find, TextWriter.Synchronized(error), options?.MaxConcurrency, options?.InputClosedGrace);
            return await host.RunAsync(input, output, cancellationToken);
        }
        finally
        {
            registry.CloseAll();
        }
    }
}
