using System.Net;
using System.Text.RegularExpressions;
using EmailFileViewer.Core.Models;

namespace EmailFileViewer.Core.Tests;

/// <summary>
/// 测试里反复用到的解析和断言辅助方法。
/// </summary>
internal static partial class MailAssert
{
    [GeneratedRegex(@"^<pre\b[^>]*>(?<text>.*)</pre>$", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex PreBlock();

    /// <summary>
    /// 解析样本库中的文件。
    /// </summary>
    /// <param name="sampleName">相对 <c>samples/</c> 的文件名。</param>
    /// <returns>解析结果，调用方负责释放。</returns>
    public static Task<ParsedEmail> ParseSampleAsync(string sampleName)
    {
        return EmailParser.ParseFileAsync(SampleFiles.GetPath(sampleName));
    }

    /// <summary>
    /// 断言解析会失败，并返回异常以便继续检查。
    /// </summary>
    /// <param name="path">文件完整路径。</param>
    /// <param name="expectedKind">预期的错误类别。</param>
    /// <returns>抛出的异常。</returns>
    public static async Task<EmailParseException> ParseFailsAsync(string path, EmailParseErrorKind expectedKind)
    {
        var exception = await Assert.ThrowsAsync<EmailParseException>(() => EmailParser.ParseFileAsync(path));
        Assert.Equal(expectedKind, exception.Kind);
        return exception;
    }

    /// <summary>
    /// 断言正文是纯文本，并取出被转义和包装之前的原文。
    /// </summary>
    /// <param name="document">邮件文档。</param>
    /// <returns>纯文本原文。</returns>
    public static string PlainTextOf(EmailDocument document)
    {
        Assert.Equal(BodyFormat.PlainText, document.Body.SourceFormat);
        var html = Assert.IsType<string>(document.Body.Html);
        var match = PreBlock().Match(html);
        Assert.True(match.Success, "纯文本正文应该包在一个 <pre> 容器里");

        return WebUtility.HtmlDecode(match.Groups["text"].Value);
    }

    /// <summary>
    /// 断言地址列表恰好是给定的名称和邮箱，顺序一致。
    /// </summary>
    /// <param name="actual">实际的地址列表。</param>
    /// <param name="expected">预期的（名称，邮箱）。</param>
    public static void AddressesEqual(IReadOnlyList<EmailAddress> actual, params (string? Name, string? Address)[] expected)
    {
        Assert.Equal(expected, actual.Select(address => (address.Name, address.Address)).ToArray());
    }
}
