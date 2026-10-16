using System.Text;

namespace EmailFileViewer.Core.Attachments;

/// <summary>
/// 附件文件名的安全处理：清理非法内容、重名时编号、识别可执行文件。全部是纯函数，不访问文件系统。
/// </summary>
/// <remarks>
/// 附件名来自不可信的邮件，保存前必须先用 <see cref="Sanitize"/> 清理，再用 <see cref="AttachmentPaths"/> 确认最终路径在目标目录内。
/// </remarks>
public static class AttachmentFileNames
{
    /// <summary>
    /// 清理后名字为空时使用的名字。
    /// </summary>
    public const string FallbackName = "attachment";

    /// <summary>
    /// 文件名按 UTF-8 编码后的最大字节数（主流文件系统的单个名字上限）。
    /// </summary>
    public const int MaxNameBytes = 255;

    // 扩展名超过这个长度就不当扩展名保留了（截断过长的名字时用）
    private const int MaxExtensionBytes = 20;

    private const int MaxUniqueAttempts = 10_000;

    private static readonly char[] InvalidCharacters = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    // Windows 的保留设备名。带扩展名也一样保留（CON.txt、com1.log），上标数字的写法同样被保留
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM¹", "COM²", "COM³",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT¹", "LPT²", "LPT³",
    };

    // 设计文档 D6：不区分平台，只要扩展名在表里，无论附件来自哪个系统都按可执行处理
    private static readonly HashSet<string> ExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Windows
        ".exe", ".bat", ".cmd", ".com", ".msi", ".ps1", ".vbs", ".js", ".jse", ".wsf", ".scr", ".lnk", ".hta", ".cpl",

        // macOS
        ".app", ".command", ".pkg",

        // Linux
        ".sh", ".desktop", ".appimage", ".run",
    };

    /// <summary>
    /// 把邮件里的附件名清理成可以安全写盘的文件名。
    /// </summary>
    /// <remarks>
    /// 依次做：去掉路径成分（<c>/</c> 和 <c>\</c> 都算分隔符，只留最后一段）；去掉控制字符和会反转显示顺序的 Unicode 控制符；
    /// 把 <c>&lt;&gt;:"/\|?*</c> 换成 <c>_</c>；去掉首尾空白和结尾的点；Windows 保留名前面加 <c>_</c>；
    /// 按 UTF-8 不超过 <see cref="MaxNameBytes"/> 字节截断（保留扩展名）；结果为空时用 <see cref="FallbackName"/>。
    /// </remarks>
    /// <param name="fileName">邮件里的附件名，可以为 <c>null</c>。</param>
    /// <returns>可以安全使用的文件名，一定不为空。</returns>
    public static string Sanitize(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return FallbackName;
        }

        var lastSeparator = fileName.AsSpan().LastIndexOfAny('/', '\\');
        var name = lastSeparator >= 0 ? fileName[(lastSeparator + 1)..] : fileName;

        var builder = new StringBuilder(name.Length);
        foreach (var rune in name.EnumerateRunes())
        {
            if (IsRemovedRune(rune))
            {
                continue;
            }

            if (rune.IsBmp && InvalidCharacters.Contains((char)rune.Value))
            {
                builder.Append('_');
            }
            else
            {
                builder.Append(rune.ToString());
            }
        }

        var cleaned = TrimName(builder.ToString());
        if (cleaned.Length == 0)
        {
            return FallbackName;
        }

        if (IsReservedName(cleaned))
        {
            cleaned = "_" + cleaned;
        }

        cleaned = LimitLength(cleaned, MaxNameBytes);
        return cleaned.Length == 0 ? FallbackName : cleaned;
    }

    /// <summary>
    /// 目标位置已有同名文件时，生成 <c>name (1).ext</c>、<c>name (2).ext</c>……形式的新名字。
    /// </summary>
    /// <remarks>
    /// 扩展名取最后一个点之后的部分（<c>a.tar.gz</c> 得到 <c>a.tar (1).gz</c>）；以点开头、没有其他点的名字（<c>.bashrc</c>）整个当作主名。
    /// 编号后总长度仍不超过 <see cref="MaxNameBytes"/> 字节，必要时缩短主名。
    /// </remarks>
    /// <param name="fileName">已经用 <see cref="Sanitize"/> 清理过的文件名。</param>
    /// <param name="exists">判断某个名字是否已被占用。调用方决定比较是否区分大小写。</param>
    /// <returns>没有被占用的名字；原名没被占用时原样返回。</returns>
    /// <exception cref="InvalidOperationException">尝试了上万个编号还是重名。</exception>
    public static string GetUniqueName(string fileName, Func<string, bool> exists)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        ArgumentNullException.ThrowIfNull(exists);

        if (!exists(fileName))
        {
            return fileName;
        }

        var (stem, extension) = SplitExtension(fileName);
        for (var number = 1; number <= MaxUniqueAttempts; number++)
        {
            var suffix = $" ({number})";
            var budget = MaxNameBytes - Encoding.UTF8.GetByteCount(suffix) - Encoding.UTF8.GetByteCount(extension);
            var candidate = TruncateToBytes(stem, budget) + suffix + extension;
            if (!exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException($"无法为 \"{fileName}\" 找到没有被占用的名字。");
    }

    /// <summary>
    /// 判断文件名是否是可执行文件或脚本，这类附件只允许保存，不允许直接打开。
    /// </summary>
    /// <remarks>
    /// 先按 <see cref="Sanitize"/> 的规则整理名字（<c>evil.exe.</c> 和 <c>evil.exe </c> 在 Windows 上就是 <c>evil.exe</c>），再看最后一个扩展名，
    /// 不区分大小写；<c>invoice.pdf.exe</c> 算可执行。扩展名列表见设计文档 D6，三个平台的都算。
    /// </remarks>
    /// <param name="fileName">附件名。</param>
    /// <returns>是可执行类型返回 <c>true</c>。</returns>
    public static bool IsExecutable(string? fileName)
    {
        var name = Sanitize(fileName);
        var lastDot = name.LastIndexOf('.');
        return lastDot >= 0 && ExecutableExtensions.Contains(name[lastDot..]);
    }

    // 控制字符，以及可以让文字显示顺序反转的 Unicode 控制符（"invoice‮gpj.exe" 看起来像 invoice‮gpj.exe）
    private static bool IsRemovedRune(Rune rune)
    {
        if (Rune.IsControl(rune))
        {
            return true;
        }

        var value = rune.Value;
        return value is >= 0x202A and <= 0x202E or >= 0x2066 and <= 0x2069;
    }

    private static string TrimName(string name)
    {
        return name.Trim().TrimEnd('.', ' ');
    }

    // 保留名看第一个点之前的部分，Windows 还会忽略它结尾的空格
    private static bool IsReservedName(string name)
    {
        var dot = name.IndexOf('.');
        var stem = (dot < 0 ? name : name[..dot]).TrimEnd(' ');
        return ReservedNames.Contains(stem);
    }

    private static string LimitLength(string name, int maxBytes)
    {
        if (Encoding.UTF8.GetByteCount(name) <= maxBytes)
        {
            return name;
        }

        var (stem, extension) = SplitExtension(name);
        if (Encoding.UTF8.GetByteCount(extension) > MaxExtensionBytes)
        {
            (stem, extension) = (name, string.Empty);
        }

        var budget = maxBytes - Encoding.UTF8.GetByteCount(extension);
        return TruncateToBytes(stem, budget).TrimEnd('.', ' ') + extension;
    }

    private static (string Stem, string Extension) SplitExtension(string name)
    {
        var lastDot = name.LastIndexOf('.');
        return lastDot > 0 ? (name[..lastDot], name[lastDot..]) : (name, string.Empty);
    }

    // 按字符（不拆开代理对）截断到不超过指定的 UTF-8 字节数
    private static string TruncateToBytes(string text, int maxBytes)
    {
        if (maxBytes <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        var bytes = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            bytes += rune.Utf8SequenceLength;
            if (bytes > maxBytes)
            {
                break;
            }

            builder.Append(rune.ToString());
        }

        return builder.ToString();
    }
}
