using EmailFileViewer.Core.Parsing;
using MimeKit;
using MsgReader.Outlook;
using MsgStorageMessage = MsgReader.Outlook.Storage.Message;

namespace EmailFileViewer.Core;

/// <summary>
/// 邮件文件的解析入口。
/// </summary>
public static class EmailParser
{
    /// <summary>
    /// 解析一个 EML 或 MSG 文件。格式按内容判断，不看扩展名。
    /// </summary>
    /// <remarks>
    /// 文件头是 OLE 复合文档签名的按 MSG 处理，否则按 MIME 处理；MIME 在解析前后各做一次合理性检查，
    /// 避免把图片、空文件等当成邮件。文件读完后就不再占用，MSG 的内容保存在内存里。
    /// </remarks>
    /// <param name="filePath">邮件文件路径。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>解析结果，调用方用完后要释放。</returns>
    /// <exception cref="EmailParseException">
    /// 文件不是邮件（<see cref="EmailParseErrorKind.UnsupportedFormat"/>）、内容已损坏（<see cref="EmailParseErrorKind.Corrupted"/>）
    /// 或无法读取（<see cref="EmailParseErrorKind.Unreadable"/>）。
    /// </exception>
    /// <exception cref="OperationCanceledException">已取消。</exception>
    public static async Task<ParsedEmail> ParseFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        CodePageRegistration.EnsureRegistered();

        FileStream stream;
        try
        {
            stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);
        }
        catch (Exception ex) when (IsFileAccessFailure(ex))
        {
            throw CreateUnreadable(filePath, ex);
        }

        await using (stream)
        {
            return await ParseStreamAsync(stream, filePath, cancellationToken);
        }
    }

    /// <summary>
    /// 按内容解析一个可定位的流：文件、或者从附件里写出来的内存流。
    /// </summary>
    /// <param name="stream">要解析的流，从头读起。</param>
    /// <param name="sourceName">来源名称（路径或附件文件名），只用于错误信息。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    internal static async Task<ParsedEmail> ParseStreamAsync(Stream stream, string sourceName, CancellationToken cancellationToken)
    {
        CodePageRegistration.EnsureRegistered();

        var header = new byte[EmailFormatDetector.SniffLength];
        int headerLength;
        try
        {
            stream.Position = 0;
            headerLength = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);
        }
        catch (Exception ex) when (IsFileAccessFailure(ex))
        {
            throw CreateUnreadable(sourceName, ex);
        }

        var headerBytes = header.AsMemory(0, headerLength);
        if (EmailFormatDetector.IsOleCompoundFile(headerBytes.Span))
        {
            stream.Position = 0;
            return await ParseMsgAsync(sourceName, stream, cancellationToken);
        }

        EmailFormatDetector.EnsureLooksLikeMime(headerBytes.Span);
        var startOffset = EmailFormatDetector.GetMimeStartOffset(headerBytes.Span);
        stream.Position = startOffset;
        return await ParseMimeAsync(sourceName, stream, startOffset, cancellationToken);
    }

    private static async Task<ParsedEmail> ParseMsgAsync(string sourceName, Stream stream, CancellationToken cancellationToken)
    {
        // 先整个读进内存：磁盘错误在这一步变成“无法读取”，之后 MsgReader 抛出的异常都是内容问题，归为“已损坏”；
        // 也让文档打开期间不占用原文件，Windows 上用户可以删除或移动它
        var buffer = new MemoryStream();
        MsgStorageMessage? message = null;
        var succeeded = false;
        try
        {
            try
            {
                await stream.CopyToAsync(buffer, cancellationToken);
            }
            catch (Exception ex) when (IsFileAccessFailure(ex))
            {
                throw CreateUnreadable(sourceName, ex);
            }

            // S/MIME 的 MSG 要先把消息类改成普通邮件，免得 MsgReader 去验证证书链（可能联网）
            var smime = MsgSmime.NeutralizeInPlace(buffer);
            message = new MsgStorageMessage(buffer, FileAccess.Read, leaveStreamOpen: true);

            // 其他 OLE 文档（Word、Excel 等）也有同样的文件头，MsgReader 不会报错，只是得到一个类型未知的空消息
            if (message.Type == MessageType.Unknown)
            {
                throw new EmailParseException(EmailParseErrorKind.UnsupportedFormat, "文件是 OLE 复合文档，但不是 Outlook 邮件。");
            }

            var parsed = new ParsedEmail(MsgEmailMapper.Map(message, smime), mimeMessage: null, message, buffer);
            succeeded = true;
            return parsed;
        }
        catch (Exception ex) when (ex is not (EmailParseException or OperationCanceledException or OutOfMemoryException))
        {
            throw new EmailParseException(EmailParseErrorKind.Corrupted, "MSG 文件的结构已损坏，无法解析。", ex);
        }
        finally
        {
            if (!succeeded)
            {
                message?.Dispose();
                await buffer.DisposeAsync();
            }
        }
    }

    private static async Task<ParsedEmail> ParseMimeAsync(string sourceName, Stream stream, long startOffset, CancellationToken cancellationToken)
    {
        MimeMessage? message = null;
        var succeeded = false;
        try
        {
            message = await MimeMessage.LoadAsync(MimeParserOptions.Create(), stream, cancellationToken);
            EmailFormatDetector.EnsureHasCommonHeader(message);

            // 邮件头里直接写了 8 位字节（没有 RFC 2047 编码）而且不是 UTF-8 时，用正文声明的字符集或识别出的字符集重新解析
            var headerEncoding = MimeHeaderEncodingResolver.Resolve(message);
            if (headerEncoding is not null)
            {
                message.Dispose();
                message = null;
                stream.Position = startOffset;

                message = await MimeMessage.LoadAsync(MimeParserOptions.Create(headerEncoding), stream, cancellationToken);
            }

            var parsed = new ParsedEmail(MimeEmailMapper.Map(message), message, msgMessage: null, msgStream: null);
            succeeded = true;
            return parsed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw CreateUnreadable(sourceName, ex);
        }
        catch (Exception ex) when (ex is not (EmailParseException or OperationCanceledException or OutOfMemoryException))
        {
            throw new EmailParseException(EmailParseErrorKind.Corrupted, "邮件内容无法按 MIME 格式解析。", ex);
        }
        finally
        {
            if (!succeeded)
            {
                message?.Dispose();
            }
        }
    }

    // 打开或读取文件时可能出现的异常：不存在、没权限、被占用、路径非法、磁盘错误等
    private static bool IsFileAccessFailure(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException
            or NotSupportedException
            or ArgumentException;
    }

    private static EmailParseException CreateUnreadable(string filePath, Exception inner)
    {
        return new EmailParseException(EmailParseErrorKind.Unreadable, $"无法读取文件 {Path.GetFileName(filePath)}：{inner.Message}", inner);
    }
}
