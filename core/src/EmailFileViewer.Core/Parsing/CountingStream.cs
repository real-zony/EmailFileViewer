namespace EmailFileViewer.Core.Parsing;

/// <summary>
/// 只统计写入字节数、不保存内容的流。用来在不占用内存的情况下得到解码后的附件大小。
/// </summary>
internal sealed class CountingStream : Stream
{
    private long _count;

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => _count;

    public override long Position
    {
        get => _count;
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => _count += count;

    public override void Write(ReadOnlySpan<byte> buffer) => _count += buffer.Length;

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        _count += buffer.Length;
        return ValueTask.CompletedTask;
    }
}
