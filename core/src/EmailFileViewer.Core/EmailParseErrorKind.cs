namespace EmailFileViewer.Core;

/// <summary>
/// 邮件文件打开失败的原因类别，对应界面上的三种提示。
/// </summary>
public enum EmailParseErrorKind
{
    /// <summary>
    /// 不支持的格式：文件不是 EML 也不是 MSG，例如图片、空文件、其他类型的 OLE 文档。
    /// </summary>
    UnsupportedFormat,

    /// <summary>
    /// 文件已损坏：格式看起来是邮件，但内容无法解析，例如被截断的 MSG。
    /// </summary>
    Corrupted,

    /// <summary>
    /// 无法读取：文件不存在、没有读取权限或发生 IO 错误。
    /// </summary>
    Unreadable,
}
