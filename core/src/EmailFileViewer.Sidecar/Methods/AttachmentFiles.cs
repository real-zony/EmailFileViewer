using EmailFileViewer.Core.Attachments;
using EmailFileViewer.Core.Models;
using EmailFileViewer.Sidecar.Protocol;

namespace EmailFileViewer.Sidecar.Methods;

/// <summary>
/// 把附件的字节写成文件：单个保存（可覆盖）、全部保存（不覆盖）、提取到临时目录。
/// </summary>
/// <remarks>
/// 文件名的清理、重名编号、路径校验都用 Core 的 <see cref="AttachmentFileNames"/> 和 <see cref="AttachmentPaths"/>。
/// 附件字节不经过 stdio，由这里直接写到磁盘。
/// </remarks>
internal sealed class AttachmentFiles
{
    private const int BufferSize = 81920;

    // 竞争导致重名时重新取名，最多试这么多次
    private const int MaxCreateAttempts = 20;

    // Windows 上刚写完的文件可能被杀毒软件、搜索索引短暂占用，目标文件也可能正被别的程序打开，
    // 这时改名会因为“文件正在使用”或“拒绝访问”失败：等一会儿再试，总共最多等约 0.6 秒
    private const int MaxMoveAttempts = 6;

    private const int FirstMoveRetryDelayMilliseconds = 20;

    private readonly Func<string, bool> _pathExists;
    private readonly NoReplaceMover _mover;
    private readonly TextWriter _diagnostics;
    private int _warnedAboutNonAtomicMove;

    /// <summary>
    /// 创建。
    /// </summary>
    /// <param name="pathExists">判断路径上是否已有文件或目录；默认查文件系统，测试里用来模拟“取名之后别人抢先创建了同名文件”。</param>
    /// <param name="mover">Unix 上原子的“不覆盖改名”；默认用系统调用，测试里用来模拟系统不支持。</param>
    /// <param name="diagnostics">诊断日志的输出（进程里是 stderr）；默认丢弃。</param>
    internal AttachmentFiles(Func<string, bool>? pathExists = null, NoReplaceMover? mover = null, TextWriter? diagnostics = null)
    {
        _pathExists = pathExists ?? (path => File.Exists(path) || Directory.Exists(path));
        _mover = mover ?? new NoReplaceMover();
        _diagnostics = diagnostics ?? TextWriter.Null;
    }

    /// <summary>
    /// 保存到用户选定的路径；已有同名文件时直接覆盖。
    /// </summary>
    /// <remarks>
    /// 先把内容写进同目录下的临时文件，写完再替换目标。中途失败（磁盘满、附件读取失败、被取消）时目标文件保持原样，临时文件也会删掉。
    /// </remarks>
    /// <param name="targetPath">目标文件的完整路径。</param>
    /// <param name="writeAsync">把附件字节写入流的函数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>写出的文件的完整路径。</returns>
    internal async Task<string> SaveAsync(string targetPath, Func<Stream, CancellationToken, Task> writeAsync, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(targetPath);
        var directory = Path.GetDirectoryName(fullPath) ?? throw new IOException($"路径 \"{targetPath}\" 没有所在的文件夹。");
        var tempPath = Path.Combine(directory, TempFileName());

        try
        {
            await WriteNewFileAsync(tempPath, writeAsync, cancellationToken);
            await MoveAsync(tempPath, fullPath, overwrite: true, cancellationToken);
            return fullPath;
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    /// <summary>
    /// 把全部附件保存到文件夹，不覆盖任何已有文件。
    /// </summary>
    /// <remarks>
    /// 每个附件依次：清理文件名、先写进同一文件夹里的临时文件，写完（落盘）再用不覆盖的方式改成最终名字。遇到重名生成 <c>name (1).ext</c>，
    /// 并校验最终路径在文件夹内。取名到改名之间别的进程抢先创建了同名文件时，改名会失败，这时重新取名。
    /// 这样即使进程在写入过程中被强行结束，文件夹里也只会留下一个 <c>.efv-*.tmp</c> 临时文件，不会出现名字正确但内容残缺的附件。
    /// 单个附件失败（例如写入出错）记入失败列表，不影响其他附件，临时文件会删掉；整个请求被取消时直接中止。
    /// </remarks>
    /// <param name="directory">目标文件夹，必须已经存在。</param>
    /// <param name="attachments">要保存的附件。</param>
    /// <param name="writeAsync">把指定 Id 的附件字节写入流的函数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>保存成功和失败的附件。</returns>
    internal async Task<SaveAllResult> SaveAllAsync(
        string directory,
        IReadOnlyList<EmailAttachment> attachments,
        Func<string, Stream, CancellationToken, Task> writeAsync,
        CancellationToken cancellationToken = default)
    {
        var saved = new List<SavedAttachment>();
        var failed = new List<FailedAttachment>();

        foreach (var attachment in attachments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var path = await WriteUniqueFileAsync(
                    directory,
                    AttachmentFileNames.Sanitize(attachment.FileName),
                    (stream, token) => writeAsync(attachment.Id, stream, token),
                    cancellationToken);
                saved.Add(new SavedAttachment(attachment.Id, path));
            }
            catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException))
            {
                var (code, message) = Describe(ex);
                failed.Add(new FailedAttachment(attachment.Id, code, message));
            }
        }

        return new SaveAllResult(saved, failed);
    }

    /// <summary>
    /// 提取到一个目录下，供系统默认程序打开。
    /// </summary>
    /// <remarks>
    /// 文件名是清理后的附件名，目录下已有同名文件时生成 <c>name (1).ext</c>。先写临时文件再改名，打开程序不会读到写了一半的文件。
    /// </remarks>
    /// <param name="directory">目标目录，不存在会创建。</param>
    /// <param name="fileName">附件名。</param>
    /// <param name="writeAsync">把附件字节写入流的函数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>提取出的文件的完整路径。</returns>
    internal async Task<string> ExtractAsync(string directory, string? fileName, Func<Stream, CancellationToken, Task> writeAsync, CancellationToken cancellationToken = default)
    {
        return await WriteUniqueFileAsync(directory, AttachmentFileNames.Sanitize(fileName), writeAsync, cancellationToken);
    }

    /// <summary>
    /// 把异常换成错误码和说明，用于 <c>saveAll</c> 的失败列表。
    /// </summary>
    /// <param name="exception">写文件时发生的异常。</param>
    /// <returns>错误码和说明。</returns>
    internal static (string Code, string Message) Describe(Exception exception)
    {
        return IsFileSystemFailure(exception)
            ? (ErrorCodes.WriteFailed, $"无法写入文件：{exception.Message}")
            : (ErrorCodes.InternalError, $"保存附件时发生未预期的错误：{exception.GetType().Name}");
    }

    /// <summary>
    /// 判断异常是不是写文件时文件系统方面的失败：没有权限、文件夹不存在、磁盘满、路径不合法等。
    /// </summary>
    /// <param name="exception">异常。</param>
    /// <returns>是返回 <c>true</c>。</returns>
    internal static bool IsFileSystemFailure(Exception exception)
    {
        return exception is IOException or UnauthorizedAccessException or NotSupportedException or System.Security.SecurityException or ArgumentException;
    }

    // 先写临时文件，再以不覆盖的方式改成最终名字；目标名字已被占用就重新取名。任何失败都会删掉临时文件
    private async Task<string> WriteUniqueFileAsync(
        string directory,
        string name,
        Func<Stream, CancellationToken, Task> writeAsync,
        CancellationToken cancellationToken)
    {
        var tempPath = Path.Combine(directory, TempFileName());

        try
        {
            await WriteNewFileAsync(tempPath, writeAsync, cancellationToken);

            for (var attempt = 0; attempt < MaxCreateAttempts; attempt++)
            {
                var path = ChooseUniquePath(directory, name);
                try
                {
                    await MoveAsync(tempPath, path, overwrite: false, cancellationToken);
                    return path;
                }
                catch (IOException) when (_pathExists(path))
                {
                    // 取名之后同名文件被别人抢先创建了：重新取名，绝不覆盖
                }
            }

            throw new IOException($"无法为 \"{name}\" 找到没有被占用的名字。");
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    private string ChooseUniquePath(string directory, string name)
    {
        var uniqueName = AttachmentFileNames.GetUniqueName(name, candidate => _pathExists(AttachmentPaths.CombineInsideDirectory(directory, candidate)));
        return AttachmentPaths.CombineInsideDirectory(directory, uniqueName);
    }

    // 改名。Windows 上遇到可能是暂时的失败（文件正在使用、拒绝访问）时短暂等待后重试；其他平台和明确不会好转的失败直接抛出。
    // Unix 上“不覆盖”的改名用内核的原子操作（见 NoReplaceMover）：File.Move 在 Unix 上是先检查再 rename，两步之间同名文件可能被抢先创建，然后被覆盖
    private async Task MoveAsync(string source, string destination, bool overwrite, CancellationToken cancellationToken)
    {
        if (!overwrite && !OperatingSystem.IsWindows())
        {
            switch (_mover.Move(source, destination))
            {
                case NoReplaceOutcome.Moved:
                    return;
                case NoReplaceOutcome.TargetExists:
                    throw new IOException($"目标文件 \"{destination}\" 已经存在。");
                case NoReplaceOutcome.Unsupported:
                    WarnNonAtomicMove(destination);
                    break;
            }
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(source, destination, overwrite);
                return;
            }
            catch (Exception ex) when (attempt < MaxMoveAttempts && OperatingSystem.IsWindows() && MayBeTransient(ex, destination, overwrite))
            {
                await Task.Delay(FirstMoveRetryDelayMilliseconds << (attempt - 1), cancellationToken);
            }
        }
    }

    // 系统和文件系统都不支持原子的“不覆盖改名”，退回到先检查再改名：极小的概率下，同时创建的同名文件会被覆盖。
    // 降级但仍能工作，进程里只记一次，免得每个附件一条
    private void WarnNonAtomicMove(string destination)
    {
        if (Interlocked.Exchange(ref _warnedAboutNonAtomicMove, 1) != 0)
        {
            return;
        }

        try
        {
            _diagnostics.WriteLine($"[sidecar] 警告：{Path.GetDirectoryName(destination)} 所在的文件系统不支持原子的“不覆盖”改名（renameat2、renamex_np、link 都不可用），已退回到先检查再改名。");
        }
        catch (IOException)
        {
            // stderr 写不出去也不能影响保存
        }
    }

    // 不重试的情况：文件夹不存在、源文件不见了、目标是一个文件夹，以及不允许覆盖时目标已经有了（那是重名，由调用方换名字）
    private static bool MayBeTransient(Exception exception, string destination, bool overwrite)
    {
        if (exception is not (IOException or UnauthorizedAccessException) || exception is DirectoryNotFoundException or FileNotFoundException)
        {
            return false;
        }

        return !Directory.Exists(destination) && (overwrite || !File.Exists(destination));
    }

    private static async Task WriteNewFileAsync(string path, Func<Stream, CancellationToken, Task> writeAsync, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
        await writeAsync(stream, cancellationToken);
        await stream.FlushAsync(cancellationToken);

        // 确保内容真的落盘再改名，断电后不会得到一个空文件
        stream.Flush(flushToDisk: true);
    }

    private static string TempFileName() => $".efv-{Guid.NewGuid():N}.tmp";

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 残留的临时文件不影响结果
        }
    }
}
