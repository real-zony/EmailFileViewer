using System.Text;

namespace EmailFileViewer.Core.Parsing;

/// <summary>
/// 注册代码页编码提供程序，让 .NET 认识 GBK、Big5、Shift_JIS 等非 Unicode 字符集。
/// </summary>
/// <remarks>
/// MsgReader 解析 MSG 和 MimeKit 解析 TNEF 都需要它，没注册会抛 <see cref="TypeInitializationException"/>。
/// 注册放在静态构造函数里，由 CLR 保证只执行一次，并且并发调用时后到的线程会等它完成。
/// </remarks>
internal static class CodePageRegistration
{
    static CodePageRegistration()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// 确保代码页编码提供程序已注册。每个解析入口在使用 MimeKit 或 MsgReader 之前都要调用。
    /// </summary>
    internal static void EnsureRegistered()
    {
        // 调用带有显式静态构造函数的类型的静态方法，会先触发静态构造函数，方法体不需要内容
    }
}
