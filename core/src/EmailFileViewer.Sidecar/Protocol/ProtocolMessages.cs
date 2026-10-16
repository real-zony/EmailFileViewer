using EmailFileViewer.Core.Models;

namespace EmailFileViewer.Sidecar.Protocol;

/// <summary>
/// 当前协议版本。有不兼容的改动时才增加。
/// </summary>
internal static class ProtocolInfo
{
    /// <summary>
    /// 协议版本，由 <c>system.ping</c> 返回。
    /// </summary>
    internal const int Version = 1;
}

/// <summary>
/// 请求的 <c>params</c>。
/// </summary>
internal interface IMethodParams
{
    /// <summary>
    /// 检查必需的字段。不通过时抛出 <see cref="ProtocolException"/>（<see cref="ErrorCodes.InvalidParams"/>）。
    /// </summary>
    void Validate();
}

/// <summary>
/// 针对某个已打开文档的请求参数。同一个文档的请求按到达顺序串行处理。
/// </summary>
internal interface IDocumentParams : IMethodParams
{
    /// <summary>
    /// 文档 Id。
    /// </summary>
    string? DocId { get; }
}

/// <summary>
/// 参数字段的检查辅助。
/// </summary>
internal static class ParamsCheck
{
    /// <summary>
    /// 要求字符串字段存在且不是空白。
    /// </summary>
    /// <param name="value">字段的值。</param>
    /// <param name="name">字段名，写进错误信息。</param>
    /// <returns>去掉 <c>null</c> 标注的值。</returns>
    internal static string Require(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ProtocolException(ErrorCodes.InvalidParams, $"params.{name} 缺失，或者不是非空字符串。");
        }

        return value;
    }

    /// <summary>
    /// 要求字段是完整的绝对路径。相对路径会被解释成相对于 sidecar 的工作目录，容易写到意料之外的位置。
    /// </summary>
    /// <param name="value">字段的值。</param>
    /// <param name="name">字段名，写进错误信息。</param>
    /// <returns>路径。</returns>
    internal static string RequireAbsolutePath(string? value, string name)
    {
        var path = Require(value, name);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ProtocolException(ErrorCodes.InvalidParams, $"params.{name} 必须是绝对路径。");
        }

        return path;
    }
}

/// <summary><c>email.open</c> 的参数。</summary>
internal sealed record OpenParams : IMethodParams
{
    /// <summary>邮件文件的路径。</summary>
    public string? Path { get; init; }

    /// <inheritdoc />
    public void Validate() => ParamsCheck.Require(Path, "path");
}

/// <summary><c>email.openNested</c> 的参数。</summary>
internal sealed record OpenNestedParams : IDocumentParams
{
    /// <summary>外层文档的 Id。</summary>
    public string? DocId { get; init; }

    /// <summary>嵌套邮件附件的 Id。</summary>
    public string? AttachmentId { get; init; }

    /// <inheritdoc />
    public void Validate()
    {
        ParamsCheck.Require(DocId, "docId");
        ParamsCheck.Require(AttachmentId, "attachmentId");
    }
}

/// <summary><c>document.close</c> 的参数。</summary>
internal sealed record CloseParams : IDocumentParams
{
    /// <summary>要关闭的文档的 Id。</summary>
    public string? DocId { get; init; }

    /// <inheritdoc />
    public void Validate() => ParamsCheck.Require(DocId, "docId");
}

/// <summary><c>attachment.save</c> 的参数。</summary>
internal sealed record SaveParams : IDocumentParams
{
    /// <summary>文档 Id。</summary>
    public string? DocId { get; init; }

    /// <summary>附件 Id。</summary>
    public string? AttachmentId { get; init; }

    /// <summary>用户在保存对话框里选的完整路径（绝对路径）。</summary>
    public string? TargetPath { get; init; }

    /// <inheritdoc />
    public void Validate()
    {
        ParamsCheck.Require(DocId, "docId");
        ParamsCheck.Require(AttachmentId, "attachmentId");
        ParamsCheck.RequireAbsolutePath(TargetPath, "targetPath");
    }
}

/// <summary><c>attachment.saveAll</c> 的参数。</summary>
internal sealed record SaveAllParams : IDocumentParams
{
    /// <summary>文档 Id。</summary>
    public string? DocId { get; init; }

    /// <summary>用户选的目标文件夹（绝对路径）。</summary>
    public string? TargetDir { get; init; }

    /// <inheritdoc />
    public void Validate()
    {
        ParamsCheck.Require(DocId, "docId");
        ParamsCheck.RequireAbsolutePath(TargetDir, "targetDir");
    }
}

/// <summary><c>attachment.extractForOpen</c> 的参数。</summary>
internal sealed record ExtractParams : IDocumentParams
{
    /// <summary>文档 Id。</summary>
    public string? DocId { get; init; }

    /// <summary>附件 Id。</summary>
    public string? AttachmentId { get; init; }

    /// <inheritdoc />
    public void Validate()
    {
        ParamsCheck.Require(DocId, "docId");
        ParamsCheck.Require(AttachmentId, "attachmentId");
    }
}

/// <summary>不需要参数的方法使用的占位。</summary>
internal sealed record NoParams : IMethodParams
{
    /// <inheritdoc />
    public void Validate()
    {
    }
}

/// <summary><c>system.ping</c> 的结果。</summary>
/// <param name="ProtocolVersion">协议版本。</param>
internal sealed record PingResult(int ProtocolVersion);

/// <summary><c>email.open</c> 和 <c>email.openNested</c> 的结果。</summary>
/// <param name="DocId">新文档的 Id。</param>
/// <param name="Document">解析出的邮件文档。</param>
internal sealed record OpenResult(string DocId, EmailDocument Document);

/// <summary><c>document.close</c> 的结果：一个空对象。</summary>
internal sealed record EmptyResult;

/// <summary><c>attachment.save</c> 和 <c>attachment.extractForOpen</c> 的结果。</summary>
/// <param name="Path">写出的文件的完整路径。</param>
internal sealed record PathResult(string Path);

/// <summary><c>attachment.saveAll</c> 里保存成功的一项。</summary>
/// <param name="AttachmentId">附件 Id。</param>
/// <param name="Path">写出的文件的完整路径。</param>
internal sealed record SavedAttachment(string AttachmentId, string Path);

/// <summary><c>attachment.saveAll</c> 里保存失败的一项。</summary>
/// <param name="AttachmentId">附件 Id。</param>
/// <param name="Code">错误码。</param>
/// <param name="Message">给开发者看的说明。</param>
internal sealed record FailedAttachment(string AttachmentId, string Code, string Message);

/// <summary><c>attachment.saveAll</c> 的结果。</summary>
/// <param name="Saved">保存成功的附件。</param>
/// <param name="Failed">保存失败的附件，单个失败不影响其他附件。</param>
internal sealed record SaveAllResult(IReadOnlyList<SavedAttachment> Saved, IReadOnlyList<FailedAttachment> Failed);
