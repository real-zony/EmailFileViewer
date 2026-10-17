namespace EmailFileViewer.Core.Tests;

/// <summary>
/// 只在 Unix 系统且不是特权用户时运行的测试。去掉文件读权限在 Windows 上做法不同，特权用户则不受权限限制。
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class UnixOnlyFactAttribute : FactAttribute
{
    public UnixOnlyFactAttribute()
    {
        if (OperatingSystem.IsWindows())
        {
            Skip = "文件权限用例只在 Unix 系统上运行。";
        }
        else if (Environment.IsPrivilegedProcess)
        {
            Skip = "特权用户不受文件读权限限制。";
        }
    }
}
