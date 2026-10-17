namespace EmailFileViewer.Core.Tests;

/// <summary>
/// 定位仓库根目录下 <c>samples/</c> 中的样本邮件。
/// </summary>
internal static class SampleFiles
{
    /// <summary>
    /// 返回样本文件的完整路径，文件不存在时抛出异常。
    /// </summary>
    /// <param name="fileName">样本文件名。</param>
    /// <returns>样本文件的完整路径。</returns>
    public static string GetPath(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine(directory.FullName, "samples", fileName);
            if (File.Exists(path))
            {
                return path;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"在上级目录中找不到样本文件 samples/{fileName}");
    }
}
