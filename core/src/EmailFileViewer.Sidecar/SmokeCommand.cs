using System.Text.Json;
using EmailFileViewer.Core;
using EmailFileViewer.Core.Json;

namespace EmailFileViewer.Sidecar;

/// <summary>
/// <c>--smoke &lt;dir&gt;</c> 命令：逐个解析目录中的邮件文件并输出摘要，用来验证原生发布的可执行文件能正常工作。
/// </summary>
public static class SmokeCommand
{
    private const int MaxNestedDepth = 5;

    private static readonly string[] EmailExtensions = [".eml", ".msg"];

    /// <summary>
    /// 解析目录（不递归）下所有扩展名为 .eml 或 .msg 的文件。
    /// </summary>
    /// <remarks>
    /// 每个成功的文件向 <paramref name="output"/> 写一行 <c>文件名\t主题\t正文来源格式\t附件数</c>；失败的文件向 <paramref name="error"/> 写错误信息。
    /// 解析成功后还会做两项只为验证原生发布的检查（结果不输出）：把邮件文档序列化成 JSON；
    /// 逐个写出附件并核对字节数与列表里的大小一致，嵌套邮件逐层打开（声明为邮件但内容不是邮件的，打不开不算失败）。
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
                using var parsed = await EmailParser.ParseFileAsync(file, cancellationToken);
                var document = parsed.Document;
                var json = JsonSerializer.Serialize(document, EmailJsonContext.Default.EmailDocument);
                if (json.Length == 0)
                {
                    throw new InvalidOperationException("邮件文档序列化后为空。");
                }

                await VerifyAttachmentsAsync(parsed, depth: 0, cancellationToken);

                await output.WriteLineAsync(
                    $"{fileName}\t{ToSingleLine(document.Subject ?? string.Empty)}\t{document.Body.SourceFormat}\t{document.Attachments.Count}");
            }
            catch (EmailParseException ex)
            {
                failedCount++;
                await error.WriteLineAsync($"ERROR\t{fileName}\t{ex.Kind}: {ToSingleLine(ex.Message)}");
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

    // 写出每个附件，核对大小；嵌套邮件继续往里打开，最多几层，免得恶意文件无限嵌套
    private static async Task VerifyAttachmentsAsync(ParsedEmail parsed, int depth, CancellationToken cancellationToken)
    {
        foreach (var attachment in parsed.Document.Attachments)
        {
            using (var buffer = new MemoryStream())
            {
                await parsed.WriteAttachmentAsync(attachment.Id, buffer, cancellationToken);
                if (buffer.Length != attachment.Size)
                {
                    throw new InvalidOperationException($"附件 {attachment.Id} 写出了 {buffer.Length} 字节，列表里的大小是 {attachment.Size}。");
                }
            }

            if (!attachment.IsNestedEmail || depth >= MaxNestedDepth)
            {
                continue;
            }

            try
            {
                using var nested = await parsed.OpenNestedEmailAsync(attachment.Id, cancellationToken);
                await VerifyAttachmentsAsync(nested, depth + 1, cancellationToken);
            }
            catch (EmailParseException)
            {
                // 声明为邮件但内容不是邮件（样本里有这种附件）：打不开是预期的结果
            }
        }
    }

    // 主题里可能有制表符或换行，会破坏按制表符分隔的输出
    private static string ToSingleLine(string text)
    {
        return text.ReplaceLineEndings(" ").Replace('\t', ' ');
    }
}
