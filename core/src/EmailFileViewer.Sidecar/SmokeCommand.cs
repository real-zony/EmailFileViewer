using EmailFileViewer.Core;

namespace EmailFileViewer.Sidecar;

/// <summary>
/// <c>--smoke &lt;dir&gt;</c> 命令：逐个解析目录中的邮件文件并输出摘要，用来验证原生发布的可执行文件能正常工作。
/// </summary>
public static class SmokeCommand
{
    private static readonly string[] EmailExtensions = [".eml", ".msg"];

    /// <summary>
    /// 解析目录（不递归）下所有扩展名为 .eml 或 .msg 的文件。
    /// </summary>
    /// <remarks>
    /// 每个成功的文件向 <paramref name="output"/> 写一行 <c>文件名\t主题\t附件数</c>；失败的文件向 <paramref name="error"/> 写错误信息。
    /// </remarks>
    /// <param name="directory">样本目录。</param>
    /// <param name="output">标准输出。</param>
    /// <param name="error">错误输出。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>全部成功返回 0；目录不存在、没有邮件文件或任一文件解析失败返回 1。</returns>
    public static async Task<int> RunAsync(
        string directory,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directory))
        {
            await error.WriteLineAsync($"ERROR\t{directory}\t目录不存在");
            return 1;
        }

        var files = Directory
            .EnumerateFiles(directory)
            .Where(path => EmailExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (files.Length == 0)
        {
            await error.WriteLineAsync($"ERROR\t{directory}\t目录中没有 .eml 或 .msg 文件");
            return 1;
        }

        var failedCount = 0;
        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            try
            {
                var summary = await EmailSummaryReader.ReadAsync(file, cancellationToken);
                await output.WriteLineAsync($"{fileName}\t{ToSingleLine(summary.Subject)}\t{summary.AttachmentCount}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failedCount++;
                await error.WriteLineAsync($"ERROR\t{fileName}\t{ex.GetType().FullName}: {ToSingleLine(ex.Message)}");
            }
        }

        if (failedCount > 0)
        {
            await error.WriteLineAsync($"共 {files.Length} 个文件，{failedCount} 个解析失败");
            return 1;
        }

        return 0;
    }

    // 主题里可能有制表符或换行，会破坏按制表符分隔的输出
    private static string ToSingleLine(string text)
    {
        return text.ReplaceLineEndings(" ").Replace('\t', ' ');
    }
}
