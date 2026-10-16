using EmailFileViewer.Core;
using EmailFileViewer.Core.Attachments;
using EmailFileViewer.Core.Models;
using EmailFileViewer.Sidecar.Documents;
using EmailFileViewer.Sidecar.Protocol;
using EmailFileViewer.Sidecar.Sessions;

namespace EmailFileViewer.Sidecar.Methods;

/// <summary>
/// 协议里各个方法的实现。
/// </summary>
/// <remarks>
/// 针对某个文档的方法都由 <see cref="ProtocolHost"/> 放进该文档的队列执行，所以这里可以放心使用 <see cref="ParsedEmail"/>，不需要自己加锁。
/// </remarks>
internal sealed class SidecarMethods
{
    private readonly DocumentRegistry _registry;
    private readonly SessionDirectory _session;
    private readonly AttachmentFiles _files;

    /// <summary>
    /// 创建。
    /// </summary>
    /// <param name="registry">已打开的文档。</param>
    /// <param name="session">本次会话的临时目录。</param>
    /// <param name="files">写附件文件的对象。</param>
    internal SidecarMethods(DocumentRegistry registry, SessionDirectory session, AttachmentFiles files)
    {
        _registry = registry;
        _session = session;
        _files = files;
    }

    /// <summary>
    /// 构造包含全部方法的方法表。
    /// </summary>
    /// <returns>方法表。</returns>
    internal MethodTable CreateTable()
    {
        var json = ProtocolJsonContext.Default;
        var table = new MethodTable();
        table.Add(MethodNames.Ping, json.NoParams, json.PingResult, PingAsync);
        table.Add(MethodNames.EmailOpen, json.OpenParams, json.OpenResult, OpenAsync);
        table.Add(MethodNames.EmailOpenNested, json.OpenNestedParams, json.OpenResult, OpenNestedAsync);
        table.Add(MethodNames.DocumentClose, json.CloseParams, json.EmptyResult, CloseAsync);
        table.Add(MethodNames.AttachmentSave, json.SaveParams, json.PathResult, SaveAsync);
        table.Add(MethodNames.AttachmentSaveAll, json.SaveAllParams, json.SaveAllResult, SaveAllAsync);
        table.Add(MethodNames.AttachmentExtractForOpen, json.ExtractParams, json.PathResult, ExtractForOpenAsync);
        return table;
    }

    private static Task<PingResult> PingAsync(NoParams parameters, CancellationToken cancellationToken)
    {
        return Task.FromResult(new PingResult(ProtocolInfo.Version));
    }

    private async Task<OpenResult> OpenAsync(OpenParams parameters, CancellationToken cancellationToken)
    {
        ParsedEmail parsed;
        try
        {
            parsed = await EmailParser.ParseFileAsync(parameters.Path!, cancellationToken);
        }
        catch (EmailParseException ex)
        {
            throw new ProtocolException(ErrorCodes.From(ex.Kind), ex.Message, ex);
        }

        var entry = _registry.Add(parsed);
        return new OpenResult(entry.DocId, parsed.Document);
    }

    private async Task<OpenResult> OpenNestedAsync(OpenNestedParams parameters, CancellationToken cancellationToken)
    {
        var entry = FindOpenDocument(parameters.DocId!);

        ParsedEmail nested;
        try
        {
            nested = await entry.Parsed.OpenNestedEmailAsync(parameters.AttachmentId!, cancellationToken);
        }
        catch (AttachmentNotFoundException ex)
        {
            throw new ProtocolException(ErrorCodes.AttachmentNotFound, ex.Message, ex);
        }
        catch (AttachmentNotNestedEmailException ex)
        {
            throw new ProtocolException(ErrorCodes.NotNestedEmail, ex.Message, ex);
        }
        catch (EmailParseException ex)
        {
            throw new ProtocolException(ErrorCodes.From(ex.Kind), ex.Message, ex);
        }

        // 嵌套邮件是独立的文档，有自己的 docId，和外层各自关闭
        var nestedEntry = _registry.Add(nested);
        return new OpenResult(nestedEntry.DocId, nested.Document);
    }

    private Task<EmptyResult> CloseAsync(CloseParams parameters, CancellationToken cancellationToken)
    {
        var entry = FindOpenDocument(parameters.DocId!);
        entry.Close();
        _registry.Remove(entry.DocId);
        return Task.FromResult(new EmptyResult());
    }

    private async Task<PathResult> SaveAsync(SaveParams parameters, CancellationToken cancellationToken)
    {
        var entry = FindOpenDocument(parameters.DocId!);
        var attachment = FindAttachment(entry, parameters.AttachmentId!);

        try
        {
            var path = await _files.SaveAsync(
                parameters.TargetPath!,
                (stream, token) => entry.Parsed.WriteAttachmentAsync(attachment.Id, stream, token),
                cancellationToken);
            return new PathResult(path);
        }
        catch (Exception ex) when (AttachmentFiles.IsFileSystemFailure(ex))
        {
            throw new ProtocolException(ErrorCodes.WriteFailed, $"无法保存到 \"{parameters.TargetPath}\"：{ex.Message}", ex);
        }
    }

    private async Task<SaveAllResult> SaveAllAsync(SaveAllParams parameters, CancellationToken cancellationToken)
    {
        var entry = FindOpenDocument(parameters.DocId!);

        var directory = Path.GetFullPath(parameters.TargetDir!);
        if (!Directory.Exists(directory))
        {
            throw new ProtocolException(ErrorCodes.WriteFailed, $"目标文件夹 \"{directory}\" 不存在。");
        }

        return await _files.SaveAllAsync(
            directory,
            entry.Parsed.Document.Attachments,
            (attachmentId, stream, token) => entry.Parsed.WriteAttachmentAsync(attachmentId, stream, token),
            cancellationToken);
    }

    private async Task<PathResult> ExtractForOpenAsync(ExtractParams parameters, CancellationToken cancellationToken)
    {
        var entry = FindOpenDocument(parameters.DocId!);
        var attachment = FindAttachment(entry, parameters.AttachmentId!);

        if (AttachmentFileNames.IsExecutable(attachment.FileName))
        {
            throw new ProtocolException(ErrorCodes.ExecutableRefused, $"附件 \"{attachment.FileName}\" 是可执行文件或脚本，只能保存，不能直接打开。");
        }

        // 同一个附件再次打开：直接用上次提取出来的文件
        if (entry.ExtractedFiles.TryGetValue(attachment.Id, out var existing) && File.Exists(existing))
        {
            return new PathResult(existing);
        }

        try
        {
            var directory = _session.GetDocumentDirectory(entry.DocId);
            var path = await _files.ExtractAsync(
                directory,
                attachment.FileName,
                (stream, token) => entry.Parsed.WriteAttachmentAsync(attachment.Id, stream, token),
                cancellationToken);
            entry.ExtractedFiles[attachment.Id] = path;
            return new PathResult(path);
        }
        catch (Exception ex) when (AttachmentFiles.IsFileSystemFailure(ex))
        {
            throw new ProtocolException(ErrorCodes.WriteFailed, $"无法把附件提取到临时目录：{ex.Message}", ex);
        }
    }

    // 排在关闭请求后面的操作，到这里文档已经从登记表里移除或标记为关闭
    private DocumentEntry FindOpenDocument(string docId)
    {
        var entry = _registry.Find(docId) ?? throw DocumentEntry.DocumentNotFound(docId);
        entry.EnsureOpen();
        return entry;
    }

    private static EmailAttachment FindAttachment(DocumentEntry entry, string attachmentId)
    {
        return entry.Parsed.Document.Attachments.FirstOrDefault(attachment => attachment.Id == attachmentId)
            ?? throw new ProtocolException(ErrorCodes.AttachmentNotFound, $"文档里没有 Id 为 \"{attachmentId}\" 的附件。");
    }
}
