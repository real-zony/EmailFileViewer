using System.Text;
using MimeKit;

namespace EmailFileViewer.Core.Parsing;

/// <summary>
/// 解析 MIME 时统一使用的解析选项。
/// </summary>
/// <remarks>
/// MimeKit 默认允许 1024 层嵌套，而序列化嵌套的 <c>message/rfc822</c>（算附件大小、保存附件）是递归的，
/// 层数多了会栈溢出，整个进程直接退出。所以所有加载 MIME 的地方都用这里的选项，把层数压到 <see cref="MaxMimeDepth"/>；
/// 超过这个层数的部分当作普通的叶子内容，不再往里解析。
/// </remarks>
internal static class MimeParserOptions
{
    /// <summary>
    /// MIME 最大嵌套层数。真实邮件很少超过 10 层（multipart 套 multipart、转发邮件套转发邮件），64 层留足余量，
    /// 同时递归深度小到在任何线程的栈上都不会有风险。
    /// </summary>
    internal const int MaxMimeDepth = 64;

    /// <summary>
    /// 创建解析选项。
    /// </summary>
    /// <param name="charsetEncoding">邮件头里直接写了 8 位字节时使用的编码；<c>null</c> 保持 MimeKit 的默认值。</param>
    /// <returns>新的解析选项，调用方可以随意修改。</returns>
    internal static ParserOptions Create(Encoding? charsetEncoding = null)
    {
        var options = ParserOptions.Default.Clone();
        options.MaxMimeDepth = MaxMimeDepth;
        if (charsetEncoding is not null)
        {
            options.CharsetEncoding = charsetEncoding;
        }

        return options;
    }
}
