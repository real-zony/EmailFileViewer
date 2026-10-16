using System.Text;

namespace EmailFileViewer.Core.Tests;

/// <summary>
/// 测试用的临时目录，释放时整个删除。用来构造样本库里没有的邮件文件。
/// </summary>
internal sealed class TempDirectory : IDisposable
{
    /// <summary>
    /// 创建一个新的空目录。
    /// </summary>
    public TempDirectory()
    {
        // GitHub 的 Windows runner 上 TEMP 是 8.3 短名（C:\Users\RUNNER~1\...），而 Path.GetFullPath 会把它展开成长名（runneradmin）。
        // 这里先规范化一次，测试里拼出来的路径才能和被测代码返回的（经过 GetFullPath 的）路径逐字符比较
        DirectoryPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "efv-tests-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(DirectoryPath);
    }

    /// <summary>
    /// 目录的完整路径。
    /// </summary>
    public string DirectoryPath { get; }

    /// <summary>
    /// 写入一个二进制文件。
    /// </summary>
    /// <param name="fileName">文件名。</param>
    /// <param name="content">文件内容。</param>
    /// <returns>文件的完整路径。</returns>
    public string WriteBytes(string fileName, byte[] content)
    {
        var path = Path.Combine(DirectoryPath, fileName);
        File.WriteAllBytes(path, content);
        return path;
    }

    /// <summary>
    /// 写入一个 UTF-8（无 BOM）文本文件，换行统一成 CRLF。
    /// </summary>
    /// <param name="fileName">文件名。</param>
    /// <param name="text">文件内容。</param>
    /// <returns>文件的完整路径。</returns>
    public string WriteMail(string fileName, string text)
    {
        return WriteBytes(fileName, new UTF8Encoding(false).GetBytes(text.ReplaceLineEndings("\r\n")));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Directory.Delete(DirectoryPath, recursive: true);
        }
        catch (IOException)
        {
            // 清理失败不应该让测试失败
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
