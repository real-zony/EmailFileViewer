namespace EmailFileViewer.Sidecar.Tests;

/// <summary>
/// 只在 Windows 上运行的测试。其他平台上没有对应的行为（共享冲突、盘符路径、<c>\\?\</c> 前缀等）。
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class WindowsOnlyFactAttribute : FactAttribute
{
    public WindowsOnlyFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "只在 Windows 上运行。";
        }
    }
}

/// <summary>
/// 只在 Windows 上运行的数据驱动测试。
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class WindowsOnlyTheoryAttribute : TheoryAttribute
{
    public WindowsOnlyTheoryAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "只在 Windows 上运行。";
        }
    }
}

/// <summary>
/// 只在 Unix 系统（macOS、Linux）上运行的数据驱动测试。
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class UnixOnlyTheoryAttribute : TheoryAttribute
{
    public UnixOnlyTheoryAttribute()
    {
        if (OperatingSystem.IsWindows())
        {
            Skip = "只在 Unix 系统上运行。";
        }
    }
}
