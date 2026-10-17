namespace EmailFileViewer.Sidecar.Protocol;

/// <summary>
/// 协议方法名。
/// </summary>
internal static class MethodNames
{
    /// <summary>确认 sidecar 已就绪，返回协议版本。</summary>
    internal const string Ping = "system.ping";

    /// <summary>打开一个邮件文件。</summary>
    internal const string EmailOpen = "email.open";

    /// <summary>打开一个嵌套邮件附件。</summary>
    internal const string EmailOpenNested = "email.openNested";

    /// <summary>关闭文档。</summary>
    internal const string DocumentClose = "document.close";

    /// <summary>把一个附件保存到指定路径。</summary>
    internal const string AttachmentSave = "attachment.save";

    /// <summary>把全部附件保存到指定文件夹。</summary>
    internal const string AttachmentSaveAll = "attachment.saveAll";

    /// <summary>把一个附件提取到临时目录，供系统默认程序打开。</summary>
    internal const string AttachmentExtractForOpen = "attachment.extractForOpen";
}
