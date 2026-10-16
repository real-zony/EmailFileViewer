namespace EmailFileViewer.Sidecar.Tests;

/// <summary>
/// 等待一个条件成立。
/// </summary>
/// <remarks>
/// Windows 上删除目录之后，杀毒软件或搜索索引还握着句柄时，目录会短暂地仍然可见（“待删除”状态）。
/// 断言“目录已经被删掉”时轮询几秒，免得这种瞬时状态让测试偶发失败；条件一直不成立才算失败。
/// </remarks>
internal static class Eventually
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 在超时之前条件成立就通过，否则让测试失败。
    /// </summary>
    /// <param name="condition">要等待的条件。</param>
    /// <param name="because">失败时的说明。</param>
    public static void IsTrue(Func<bool> condition, string because)
    {
        var deadlineUtc = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadlineUtc)
            {
                Assert.Fail($"等了 {Timeout.TotalSeconds} 秒条件仍不成立：{because}");
            }

            Thread.Sleep(20);
        }
    }

    /// <summary>
    /// 目录最终不存在。
    /// </summary>
    /// <param name="path">目录路径。</param>
    public static void DirectoryIsGone(string path)
    {
        IsTrue(() => !Directory.Exists(path), $"目录 {path} 应该已经被删除");
    }

    /// <summary>
    /// 目录最终没有任何子目录。
    /// </summary>
    /// <param name="path">父目录。</param>
    public static void HasNoSubdirectories(string path)
    {
        IsTrue(() => Directory.GetDirectories(path).Length == 0, $"目录 {path} 里不应该再有子目录");
    }
}
