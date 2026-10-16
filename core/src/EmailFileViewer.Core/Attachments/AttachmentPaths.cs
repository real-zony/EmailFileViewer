namespace EmailFileViewer.Core.Attachments;

/// <summary>
/// 保存附件时的路径校验：最终路径必须落在目标目录之内。
/// </summary>
/// <remarks>
/// 只做路径文本的规范化和比较，不解析符号链接。调用方创建文件时要用 <c>FileMode.CreateNew</c>：
/// 目标目录里已有的同名文件（包括符号链接）会让创建失败，而不是被覆盖或跟随。
/// </remarks>
public static class AttachmentPaths
{
    /// <summary>
    /// 判断路径是否位于目录之内（不含目录本身）。
    /// </summary>
    /// <remarks>
    /// 比较的是规范化后的完整路径，所以 <c>dir/../other</c> 这类写法会被识别出来；目录路径补上结尾分隔符再比较，
    /// <c>/tmp/save</c> 不会把 <c>/tmp/save-other/x</c> 当成它里面的文件。Windows 和 macOS 的文件系统默认不区分大小写，比较时也不区分。
    /// </remarks>
    /// <param name="directory">目标目录。</param>
    /// <param name="path">要写入的文件路径。</param>
    /// <returns>在目录之内返回 <c>true</c>。</returns>
    public static bool IsInsideDirectory(string directory, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var directoryFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;
        var pathFull = Path.GetFullPath(path);
        return pathFull.Length > directoryFull.Length && pathFull.StartsWith(directoryFull, GetPathComparison());
    }

    /// <summary>
    /// 把清理过的文件名放到目标目录下，并确认结果仍在目录之内。
    /// </summary>
    /// <param name="directory">目标目录。</param>
    /// <param name="fileName">已经用 <see cref="AttachmentFileNames.Sanitize"/> 清理过的文件名，不能含路径分隔符。</param>
    /// <returns>规范化后的完整路径。</returns>
    /// <exception cref="ArgumentException">文件名含路径分隔符，或者最终路径不在目标目录之内。</exception>
    public static string CombineInsideDirectory(string directory, string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrEmpty(fileName);

        if (fileName.AsSpan().IndexOfAny('/', '\\') >= 0)
        {
            throw new ArgumentException($"文件名 \"{fileName}\" 含有路径分隔符。", nameof(fileName));
        }

        var path = Path.GetFullPath(Path.Combine(directory, fileName));
        if (!IsInsideDirectory(directory, path))
        {
            throw new ArgumentException($"文件 \"{fileName}\" 的最终路径不在目标目录之内。", nameof(fileName));
        }

        return path;
    }

    private static StringComparison GetPathComparison()
    {
        return OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }
}
