using System.Runtime.InteropServices;

namespace EmailFileViewer.Sidecar.Methods;

/// <summary>
/// 不覆盖的改名结果。
/// </summary>
internal enum NoReplaceOutcome
{
    /// <summary>改名成功，源文件已经变成目标文件。</summary>
    Moved,

    /// <summary>目标已经存在，什么都没有改动。</summary>
    TargetExists,

    /// <summary>系统调用或文件系统不支持这种做法，可以换下一种。</summary>
    Unsupported,

    /// <summary>出现了与“是否支持”无关的错误（源文件不存在、没有权限、磁盘只读等），交给 <see cref="File.Move(string, string, bool)"/> 去报出对应的异常。</summary>
    Failed,
}

/// <summary>
/// Unix 上原子的“目标不存在才改名”。
/// </summary>
/// <remarks>
/// <para><c>File.Move(源, 目标, overwrite: false)</c> 在 Unix 上是先检查目标是否存在、再 <c>rename()</c>，两步之间别的进程可以抢先创建同名文件，然后被悄悄覆盖。
/// 这里改用内核提供的原子操作，依次尝试：</para>
/// <list type="number">
/// <item>Linux 用 <c>renameat2(..., RENAME_NOREPLACE)</c>，macOS 用 <c>renamex_np(..., RENAME_EXCL)</c>；目标存在时失败并返回 <c>EEXIST</c>。</item>
/// <item>系统调用或文件系统不支持（<c>EINVAL</c>、<c>ENOSYS</c>、<c>ENOTSUP</c>，或者符号根本不存在）时，改用 <c>link()</c> 加 <c>unlink()</c>：<c>link()</c> 在目标存在时同样原子地失败。</item>
/// <item>两种都不行，返回 <see cref="NoReplaceOutcome.Unsupported"/>，由调用方退回到先检查再改名，并记一条警告。</item>
/// </list>
/// <para>用 <c>LibraryImport</c> 生成调用代码，没有运行时反射，NativeAOT 下可用。找不到库或入口（例如很老的 glibc 没有 <c>renameat2</c>）按“不支持”处理。</para>
/// </remarks>
internal sealed partial class NoReplaceMover
{
    private const uint RenameNoReplaceLinux = 1;
    private const uint RenameExclMacOs = 0x4;
    private const int AtFdCwd = -100;

    private const int Eperm = 1;
    private const int Eexist = 17;
    private const int Exdev = 18;
    private const int Einval = 22;
    private const int Emlink = 31;

    private readonly Func<string, string, NoReplaceOutcome> _renameExclusive;
    private readonly Func<string, string, NoReplaceOutcome> _link;

    /// <summary>
    /// 创建。
    /// </summary>
    /// <param name="renameExclusive">第一步：内核的“不覆盖改名”；<c>null</c> 时用当前系统的调用（测试里用来模拟不支持）。</param>
    /// <param name="link">第二步：<c>link()</c> 加 <c>unlink()</c>；<c>null</c> 时用系统调用（测试里用来模拟不支持）。</param>
    internal NoReplaceMover(Func<string, string, NoReplaceOutcome>? renameExclusive = null, Func<string, string, NoReplaceOutcome>? link = null)
    {
        _renameExclusive = renameExclusive ?? RenameExclusive;
        _link = link ?? LinkThenUnlink;
    }

    /// <summary>
    /// 把源文件改名成目标，目标已经存在时不覆盖。只在 Unix 上使用。
    /// </summary>
    /// <param name="source">源文件。</param>
    /// <param name="destination">目标路径。</param>
    /// <returns>结果，见 <see cref="NoReplaceOutcome"/>。</returns>
    internal NoReplaceOutcome Move(string source, string destination)
    {
        var outcome = _renameExclusive(source, destination);
        return outcome == NoReplaceOutcome.Unsupported ? _link(source, destination) : outcome;
    }

    /// <summary>
    /// 第一步：内核的“不覆盖改名”。
    /// </summary>
    /// <param name="source">源文件。</param>
    /// <param name="destination">目标路径。</param>
    /// <returns>结果。</returns>
    internal static NoReplaceOutcome RenameExclusive(string source, string destination)
    {
        try
        {
            int result;
            if (OperatingSystem.IsMacOS())
            {
                result = RenameXNp(source, destination, RenameExclMacOs);
            }
            else if (OperatingSystem.IsLinux())
            {
                result = RenameAt2(AtFdCwd, source, AtFdCwd, destination, RenameNoReplaceLinux);
            }
            else
            {
                return NoReplaceOutcome.Unsupported;
            }

            return result == 0 ? NoReplaceOutcome.Moved : Classify(Marshal.GetLastPInvokeError());
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return NoReplaceOutcome.Unsupported;
        }
    }

    /// <summary>
    /// 第二步：先 <c>link()</c> 出目标，再删掉源文件。<c>link()</c> 在目标存在时原子地失败。
    /// </summary>
    /// <param name="source">源文件。</param>
    /// <param name="destination">目标路径。</param>
    /// <returns>结果。</returns>
    internal static NoReplaceOutcome LinkThenUnlink(string source, string destination)
    {
        try
        {
            if (Link(source, destination) != 0)
            {
                return Classify(Marshal.GetLastPInvokeError());
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return NoReplaceOutcome.Unsupported;
        }

        try
        {
            File.Delete(source);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 目标已经就位；删不掉源文件只是多留一个临时文件，不影响结果
        }

        return NoReplaceOutcome.Moved;
    }

    // 把 errno 分成：目标已存在、不支持这种做法、其他错误。EINVAL/EPERM 是 FAT 之类不支持硬链接或该标志的文件系统常见的返回值
    private static NoReplaceOutcome Classify(int errno)
    {
        if (errno == Eexist)
        {
            return NoReplaceOutcome.TargetExists;
        }

        return errno is Einval or Eperm or Exdev or Emlink || IsNotSupported(errno)
            ? NoReplaceOutcome.Unsupported
            : NoReplaceOutcome.Failed;
    }

    // ENOSYS 与 ENOTSUP/EOPNOTSUPP 的数值在 Linux 和 macOS 上不同
    private static bool IsNotSupported(int errno)
    {
        return OperatingSystem.IsMacOS()
            ? errno is 78 or 45 or 102
            : errno is 38 or 95;
    }

    [LibraryImport("libc", EntryPoint = "renameat2", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int RenameAt2(int oldDirectoryFd, string oldPath, int newDirectoryFd, string newPath, uint flags);

    [LibraryImport("libc", EntryPoint = "renamex_np", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int RenameXNp(string from, string to, uint flags);

    [LibraryImport("libc", EntryPoint = "link", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int Link(string existingPath, string newPath);
}
