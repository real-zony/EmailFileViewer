using System.Runtime.CompilerServices;
using System.Text;

namespace EmailFileViewer.Core.Tests;

/// <summary>
/// 测试进程启动时的一次性初始化。
/// </summary>
internal static class TestEnvironment
{
    /// <summary>
    /// 注册代码页编码提供程序，和 sidecar 启动时的行为保持一致。MsgReader 解析 MSG 时需要它。
    /// </summary>
    [ModuleInitializer]
    public static void RegisterCodePages()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }
}
