using EmailFileViewer.Core.Parsing;

namespace EmailFileViewer.Core.Tests;

/// <summary>
/// 测试用的附件来源：内容是内存里的字节。
/// </summary>
internal sealed class FakeAttachment : AttachmentSource
{
    private readonly byte[] _bytes;

    public FakeAttachment(string id, string? fileName, string? mimeType, byte[] bytes)
        : base(id, fileName, mimeType, bytes.Length, isNestedEmail: false)
    {
        _bytes = bytes;
    }

    internal override async Task WriteToAsync(Stream destination, CancellationToken cancellationToken)
    {
        await destination.WriteAsync(_bytes, cancellationToken);
    }
}
