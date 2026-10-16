namespace EmailFileViewer.Sidecar.Sessions;

/// <summary>
/// 本次会话的临时目录：<c>&lt;系统临时目录&gt;/EmailFileViewer/sessions/&lt;会话 Id&gt;/</c>，存放提取出来供系统程序打开的附件。
/// </summary>
/// <remarks>
/// <para>目录里有一个一直被独占打开的 <c>.lock</c> 文件，进程退出（包括被强行结束）时由系统释放。
/// 启动时清理其他会话留下的目录，只删除加不上这把锁的——锁被占着说明那个会话还活着，不能删它的文件。</para>
/// <para>清理都是尽力而为：文件仍被外部程序占用（Windows 上常见）等原因删不掉时直接忽略，下次启动再清理。</para>
/// </remarks>
internal sealed class SessionDirectory : IDisposable
{
    private const string LockFileName = ".lock";

    private readonly bool _usesDefaultRoot;
    private FileStream? _lock;

    /// <summary>
    /// 创建会话目录对象，还没有创建目录，调用 <see cref="Initialize"/> 才创建。
    /// </summary>
    /// <param name="root">所有会话目录的父目录；<c>null</c> 时用系统临时目录下的 <c>EmailFileViewer/sessions</c>。</param>
    internal SessionDirectory(string? root = null)
    {
        _usesDefaultRoot = root is null;
        SessionId = Guid.NewGuid().ToString("N");
        Root = root ?? DefaultRoot;
        Path = System.IO.Path.Combine(Root, SessionId);
    }

    /// <summary>
    /// 默认的会话根目录。
    /// </summary>
    internal static string DefaultRoot => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "EmailFileViewer", "sessions");

    /// <summary>
    /// 会话根目录。
    /// </summary>
    internal string Root { get; private set; }

    /// <summary>
    /// 会话 Id。
    /// </summary>
    internal string SessionId { get; }

    /// <summary>
    /// 本次会话的目录。
    /// </summary>
    internal string Path { get; private set; }

    /// <summary>
    /// 创建本次会话的目录并加锁，然后清理其他会话留下的目录。创建失败（例如临时目录不可写）不抛出，之后提取附件时会报“无法写入”。
    /// </summary>
    /// <remarks>
    /// 默认的根目录创建不了时（多用户的 Linux 上，<c>/tmp/EmailFileViewer</c> 可能已经被另一个用户创建，并且只有他能访问），
    /// 改用带用户名的 <c>/tmp/EmailFileViewer-&lt;用户名&gt;/sessions</c>。
    /// </remarks>
    internal void Initialize()
    {
        if (!TryCreate() && _usesDefaultRoot)
        {
            Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "EmailFileViewer-" + Environment.UserName, "sessions");
            Path = System.IO.Path.Combine(Root, SessionId);
            TryCreate();
        }

        // 没有会话目录也能解析邮件、保存附件，只是提取不了
        DeleteStaleSessions();
    }

    private bool TryCreate()
    {
        try
        {
            CreateDirectory(Path);
            _lock = new FileStream(System.IO.Path.Combine(Path, LockFileName), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// 取某个文档的提取目录，不存在就创建。
    /// </summary>
    /// <param name="docId">文档 Id（由 sidecar 自己分配，不含路径分隔符）。</param>
    /// <returns>目录的完整路径。</returns>
    /// <exception cref="IOException">无法创建目录。</exception>
    internal string GetDocumentDirectory(string docId)
    {
        var directory = System.IO.Path.Combine(Path, docId);
        CreateDirectory(directory);
        return directory;
    }

    /// <summary>
    /// 释放锁并删除本次会话的目录，删不掉（例如文件仍被外部程序打开）就忽略。
    /// </summary>
    public void Dispose()
    {
        _lock?.Dispose();
        _lock = null;
        TryDeleteDirectory(Path);
    }

    private void DeleteStaleSessions()
    {
        IEnumerable<string> directories;
        try
        {
            directories = Directory.EnumerateDirectories(Root).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var directory in directories)
        {
            if (string.Equals(directory, Path, StringComparison.Ordinal) || !IsAbandoned(directory))
            {
                continue;
            }

            TryDeleteDirectory(directory);
        }
    }

    // 能把锁文件独占打开，说明没有进程占着它：会话已经结束。没有锁文件的目录（残留的空目录）也算
    private static bool IsAbandoned(string directory)
    {
        try
        {
            using var probe = new FileStream(System.IO.Path.Combine(directory, LockFileName), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (FileNotFoundException)
        {
            return true;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void CreateDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);
        }
        else
        {
            // 只有当前用户可以访问，免得同一台机器上的其他用户读到邮件附件
            Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 删不掉就留着，下次启动时再清理
        }
    }
}
