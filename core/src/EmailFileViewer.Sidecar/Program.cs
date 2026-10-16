using System.Text;
using EmailFileViewer.Sidecar;

// 注册后才能识别 GBK、Big5 等非 Unicode 字符集
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

if (args is ["--smoke", var directory])
{
    return await SmokeCommand.RunAsync(directory, Console.Out, Console.Error, cts.Token);
}

await Console.Error.WriteLineAsync("用法: EmailFileViewer.Sidecar --smoke <目录>");
return 2;
