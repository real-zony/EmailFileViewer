using System.Runtime.InteropServices;
using System.Text;
using EmailFileViewer.Sidecar;

Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

// 被结束进程的信号终止时也走正常的退出流程，才能清理临时目录
using var terminate = TryRegisterTerminate(cts);

if (args is ["--smoke", var directory])
{
    return await SmokeCommand.RunAsync(directory, Console.Out, Console.Error, cts.Token);
}

if (args is ["--stdio"])
{
    // stdout 只能有协议消息：直接用原始字节流，不经过 Console.Out 的编码和换行转换
    using var stdin = Console.OpenStandardInput();
    using var stdout = Console.OpenStandardOutput();
    return await StdioCommand.RunAsync(stdin, stdout, Console.Error, options: null, cts.Token);
}

await Console.Error.WriteLineAsync("用法:");
await Console.Error.WriteLineAsync("  EmailFileViewer.Sidecar --stdio          作为 JSON 行协议服务端运行（stdin/stdout），详见 core/docs/sidecar-protocol.md");
await Console.Error.WriteLineAsync("  EmailFileViewer.Sidecar --smoke <目录>   解析目录中的全部邮件并输出摘要");
return 2;

// 个别平台不支持注册这个信号：不支持就算了，只是收到信号时不做清理
static PosixSignalRegistration? TryRegisterTerminate(CancellationTokenSource cancellation)
{
    try
    {
        return PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
        {
            context.Cancel = true;
            cancellation.Cancel();
        });
    }
    catch (PlatformNotSupportedException)
    {
        return null;
    }
}
