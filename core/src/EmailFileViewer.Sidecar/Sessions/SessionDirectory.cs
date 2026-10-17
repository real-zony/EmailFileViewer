namespace EmailFileViewer.Sidecar.Sessions;

/// <summary>
/// 本次会话的临时目录：<c>&lt;每个用户自己的目录&gt;/EmailFileViewer/sessions/&lt;会话 Id&gt;/</c>，存放提取出来供系统程序打开的附件。
/// </summary>
/// <remarks>
/// <para>位置只用当前用户自己的目录，不用多个用户共享的 <c>/tmp</c>：Linux 依次用 <c>$XDG_CACHE_HOME</c>、<c>~/.cache</c>，最后才用 <c>$XDG_RUNTIME_DIR</c>（它通常是容量很小的 tmpfs，大附件可能写满）；
/// macOS 用 <c>$TMPDIR</c>（本来就是每个用户一个），没有就用 <c>~/Library/Caches</c>；Windows 用 <c>%LOCALAPPDATA%\Temp</c>。
/// 共享目录里别人可以抢先创建同名目录或符号链接，把我们的清理和写入引到别处。</para>
/// <para><c>EmailFileViewer</c>、<c>sessions</c> 这两级目录和会话目录本身都要检查：不能是符号链接（或联接点），
/// Unix 上不能让组和其他用户有任何权限（能改权限的只有属主，所以改成功也同时证明了它是当前用户的）。检查不过就换下一个位置，都不行就不创建会话目录。</para>
/// <para>目录里有一个一直被独占打开的 <c>.lock</c> 文件，进程退出（包括被强行结束）时由系统释放。
/// 启动时清理其他会话留下的目录，只删除名字是 32 位十六进制的、真正的目录（不跟随符号链接），并且加不上这把锁的——锁被占着说明那个会话还活着，不能删它的文件。</para>
/// <para>清理都是尽力而为：文件仍被外部程序占用（Windows 上常见）等原因删不掉时直接忽略，下次启动再清理。</para>
/// </remarks>
internal sealed class SessionDirectory : IDisposable
{
    private const string LockFileName = ".lock";
    private const string AppDirectoryName = "EmailFileViewer";
    private const string SessionsDirectoryName = "sessions";

    private const UnixFileMode GroupAndOtherPermissions =
        UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
        | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

    private const UnixFileMode UserOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private const int SessionIdLength = 32;

    private readonly IReadOnlyList<string> _rootCandidates;
    private readonly bool _usesDefaultLayout;
    private FileStream? _lock;
    private bool _created;

    /// <summary>
    /// 创建会话目录对象，还没有创建目录，调用 <see cref="Initialize"/> 才创建。
    /// </summary>
    /// <param name="root">所有会话目录的父目录，必须是当前用户私有的；<c>null</c> 时按平台选每个用户自己的目录（见类说明）。</param>
    /// <param name="getEnvironmentVariable">读环境变量的函数；<c>null</c> 时读真实的环境变量（测试里用来模拟各种环境）。</param>
    internal SessionDirectory(string? root = null, Func<string, string?>? getEnvironmentVariable = null)
    {
        SessionId = Guid.NewGuid().ToString("N");
        _usesDefaultLayout = root is null;
        _rootCandidates = root is null ? GetDefaultRoots(getEnvironmentVariable ?? Environment.GetEnvironmentVariable) : [root];
        Root = _rootCandidates.Count > 0 ? _rootCandidates[0] : string.Empty;
        Path = Root.Length > 0 ? System.IO.Path.Combine(Root, SessionId) : string.Empty;
    }

    /// <summary>
    /// 会话根目录。
    /// </summary>
    internal string Root { get; private set; }

    /// <summary>
    /// 会话 Id：32 位十六进制（小写，无连字符）。
    /// </summary>
    internal string SessionId { get; }

    /// <summary>
    /// 本次会话的目录。
    /// </summary>
    internal string Path { get; private set; }

    /// <summary>
    /// 按平台列出可以放会话根目录（<c>.../EmailFileViewer/sessions</c>）的位置，靠前的优先。只列出存在、而且别的用户不能往里写的目录。
    /// </summary>
    /// <param name="getEnvironmentVariable">读环境变量的函数。</param>
    /// <returns>候选的会话根目录；一个都没有时为空。</returns>
    internal static IReadOnlyList<string> GetDefaultRoots(Func<string, string?> getEnvironmentVariable)
    {
        var bases = new List<string?>();
        var home = getEnvironmentVariable(OperatingSystem.IsWindows() ? "USERPROFILE" : "HOME");

        if (OperatingSystem.IsWindows())
        {
            var localAppData = getEnvironmentVariable("LOCALAPPDATA");
            bases.Add(string.IsNullOrWhiteSpace(localAppData) ? null : System.IO.Path.Combine(localAppData, "Temp"));
            bases.Add(getEnvironmentVariable("TEMP"));
        }
        else if (OperatingSystem.IsMacOS())
        {
            bases.Add(getEnvironmentVariable("TMPDIR"));
            bases.Add(string.IsNullOrWhiteSpace(home) ? null : System.IO.Path.Combine(home, "Library", "Caches"));
        }
        else
        {
            // $XDG_RUNTIME_DIR 通常是容量很小的 tmpfs，提取大附件可能写满，只作最后的退路；缓存目录同样只属于当前用户，而且在磁盘上
            bases.Add(getEnvironmentVariable("XDG_CACHE_HOME"));
            bases.Add(string.IsNullOrWhiteSpace(home) ? null : System.IO.Path.Combine(home, ".cache"));
            bases.Add(getEnvironmentVariable("XDG_RUNTIME_DIR"));
        }

        var roots = new List<string>();
        foreach (var candidate in bases)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && System.IO.Path.IsPathRooted(candidate) && IsUsableBase(candidate))
            {
                roots.Add(System.IO.Path.Combine(candidate, AppDirectoryName, SessionsDirectoryName));
            }
        }

        return roots;
    }

    /// <summary>
    /// 创建本次会话的目录并加锁，然后清理其他会话留下的目录。创建失败（例如临时目录不可写，或位置不安全）不抛出，之后提取附件时会报“无法写入”。
    /// </summary>
    /// <remarks>
    /// 候选位置依次尝试，第一个能建出来的作为会话根目录。
    /// </remarks>
    internal void Initialize()
    {
        foreach (var root in _rootCandidates)
        {
            Root = root;
            Path = System.IO.Path.Combine(root, SessionId);
            if (TryCreate())
            {
                _created = true;

                // 没有会话目录也能解析邮件、保存附件，只是提取不了
                DeleteStaleSessions();
                return;
            }
        }
    }

    private bool TryCreate()
    {
        try
        {
            // 从 EmailFileViewer 到 sessions 逐级检查，每一级都必须是自己的、不是符号链接。直接指定根目录时（测试）只检查根目录本身
            if (_usesDefaultLayout)
            {
                var appDirectory = System.IO.Path.GetDirectoryName(Root);
                if (appDirectory is null || !TryEnsurePrivateDirectory(appDirectory))
                {
                    return false;
                }
            }

            if (!TryEnsurePrivateDirectory(Root))
            {
                return false;
            }

            CreateDirectory(Path);
            _lock = new FileStream(System.IO.Path.Combine(Path, LockFileName), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _lock?.Dispose();
            _lock = null;
            return false;
        }
    }

    /// <summary>
    /// 取某个文档的提取目录，不存在就创建。
    /// </summary>
    /// <param name="docId">文档 Id（由 sidecar 自己分配，不含路径分隔符）。</param>
    /// <returns>目录的完整路径。</returns>
    /// <exception cref="IOException">会话目录不可用，或无法创建目录。</exception>
    internal string GetDocumentDirectory(string docId)
    {
        if (!_created)
        {
            throw new IOException("本次会话的临时目录不可用：没有找到只属于当前用户、可以安全使用的位置。");
        }

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

        if (_created)
        {
            _created = false;
            TryDeleteDirectory(Path);
        }
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
            if (string.Equals(directory, Path, StringComparison.Ordinal) || !IsSessionDirectory(directory) || !IsAbandoned(directory))
            {
                continue;
            }

            TryDeleteDirectory(directory);
        }
    }

    // 只清理“名字是 32 位十六进制、本身是真正的目录”的：别的东西不是我们建的，符号链接更不能碰
    private static bool IsSessionDirectory(string directory)
    {
        var name = System.IO.Path.GetFileName(directory);
        if (name.Length != SessionIdLength || !name.All(char.IsAsciiHexDigitLower))
        {
            return false;
        }

        try
        {
            return !IsLink(new DirectoryInfo(directory));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
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

    // 基础目录：必须存在，Unix 上别的用户不能往里写（TMPDIR 被设成 /tmp 这类共享目录时不用它）。基础目录本身是符号链接没关系，那是用户自己的配置
    private static bool IsUsableBase(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return false;
            }

            return OperatingSystem.IsWindows() || (File.GetUnixFileMode(path) & UnixFileMode.OtherWrite) == 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // 确保目录存在、是真正的目录（不是符号链接或联接点），并且别的用户没有权限。
    // 目录已经存在而且权限太宽时，尝试收紧：只有属主能改权限，改成功就说明它是当前用户的；改不了就不能信任
    private static bool TryEnsurePrivateDirectory(string path)
    {
        try
        {
            var info = new DirectoryInfo(path);
            if (IsLink(info))
            {
                return false;
            }

            if (!info.Exists)
            {
                CreateDirectory(path);
                if (IsLink(info) || !info.Exists)
                {
                    return false;
                }
            }

            if (!OperatingSystem.IsWindows())
            {
                var mode = File.GetUnixFileMode(path);
                if ((mode & GroupAndOtherPermissions) != 0)
                {
                    File.SetUnixFileMode(path, mode & UserOnly);
                }
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // 符号链接（Windows 上还有联接点）。路径不存在时 Attributes 是 -1，不算链接
    private static bool IsLink(FileSystemInfo info)
    {
        info.Refresh();
        var attributes = info.Attributes;
        return (int)attributes != -1 && attributes.HasFlag(FileAttributes.ReparsePoint);
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
            Directory.CreateDirectory(path, UserOnly);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            // Directory.Delete 递归删除时遇到符号链接只删链接本身，不会进到链接指向的目录里
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 删不掉就留着，下次启动时再清理
        }
    }
}
