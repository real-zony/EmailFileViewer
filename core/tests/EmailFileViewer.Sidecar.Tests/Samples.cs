using System.Text.Json.Nodes;
using EmailFileViewer.Core;

namespace EmailFileViewer.Sidecar.Tests;

/// <summary>
/// 测试里反复用到的操作：打开样本、找附件、取 Core 直接解析出的附件字节作为对照。
/// </summary>
internal static class Samples
{
    /// <summary>
    /// 用 <c>email.open</c> 打开一个样本。
    /// </summary>
    public static async Task<(string DocId, JsonObject Document)> OpenAsync(SidecarSession session, string sampleName)
    {
        var result = SidecarSession.Result(await session.CallAsync("email.open", new JsonObject { ["path"] = EmailFileViewer.Core.Tests.SampleFiles.GetPath(sampleName) }));
        return ((string)result["docId"]!, result["document"]!.AsObject());
    }

    /// <summary>
    /// 在文档的附件列表里按文件名找附件的 Id。
    /// </summary>
    public static string AttachmentId(JsonObject document, string fileName)
    {
        var attachment = document["attachments"]!.AsArray().Single(a => (string?)a!["fileName"] == fileName)!;
        return (string)attachment["id"]!;
    }

    /// <summary>
    /// 目录下所有文件和子目录的名字，按名字排序。
    /// </summary>
    public static string[] EntryNames(string directory, params string[] except)
    {
        return Directory
            .EnumerateFileSystemEntries(directory)
            .Select(path => Path.GetFileName(path)!)
            .Where(name => !except.Contains(name))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// 不经过 sidecar，直接用 Core 读出一个附件的字节。
    /// </summary>
    public static async Task<byte[]> ReadAttachmentWithCoreAsync(string sampleName, string attachmentId)
    {
        using var parsed = await EmailParser.ParseFileAsync(EmailFileViewer.Core.Tests.SampleFiles.GetPath(sampleName));
        using var buffer = new MemoryStream();
        await parsed.WriteAttachmentAsync(attachmentId, buffer);
        return buffer.ToArray();
    }
}
