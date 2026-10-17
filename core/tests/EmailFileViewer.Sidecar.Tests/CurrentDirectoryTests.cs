using System.Text.Json.Nodes;

namespace EmailFileViewer.Sidecar.Tests;

/// <summary>
/// 这个集合里的测试会改变整个进程的当前目录，不能和其他测试同时运行。
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CurrentDirectoryCollection
{
    /// <summary>集合名。</summary>
    public const string Name = "ProcessWideCurrentDirectory";
}

/// <summary>
/// 会话目录正被某个程序用作当前目录时（用文件管理器、命令行打开了它），Windows 上这个目录删不掉：退出不能因此失败。
/// </summary>
[Collection(CurrentDirectoryCollection.Name)]
public class CurrentDirectoryTests
{
    [Fact]
    [Trait("Task", "4.3")]
    public async Task Exit_WhenTheDocumentDirectoryIsSomeonesCurrentDirectory_ExitsNormally()
    {
        await using var session = SidecarSession.Start();
        var (docId, document) = await Samples.OpenAsync(session, "attachments-pdf-xlsx-jpg.eml");
        var extracted = SidecarSession.Result(await session.CallAsync("attachment.extractForOpen", new JsonObject
        {
            ["docId"] = docId,
            ["attachmentId"] = Samples.AttachmentId(document, "report.pdf"),
        }));
        var documentDirectory = Path.GetDirectoryName((string)extracted["path"]!)!;

        var previous = Environment.CurrentDirectory;
        Environment.CurrentDirectory = documentDirectory;
        int code;
        try
        {
            code = await session.CompleteAsync();
        }
        finally
        {
            Environment.CurrentDirectory = previous;
        }

        Assert.Equal(0, code);
        Assert.Empty(session.Error.ToString());
    }
}
