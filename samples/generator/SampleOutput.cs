using MimeKit;
using MimeKit.Cryptography;

namespace EmailFileViewer.SampleGenerator;

/// <summary>
/// 把生成的样本写到输出目录，并记录写过哪些文件。
/// </summary>
/// <remarks>
/// 只写生成器自己负责的文件，不会删除目录里的其他文件（例如从上游取来的样本）。
/// </remarks>
internal sealed class SampleOutput
{
    private readonly List<string> _written = [];
    private readonly FormatOptions _formatOptions;

    public SampleOutput(string root)
    {
        Root = root;

        // 固定用 CRLF 换行，不随运行平台变化
        _formatOptions = FormatOptions.Default.Clone();
        _formatOptions.NewLineFormat = NewLineFormat.Dos;
    }

    public string Root { get; }

    public IReadOnlyList<string> Written => _written;

    /// <summary>
    /// 把 <paramref name="message"/> 序列化成 EML 字节，不落盘。
    /// </summary>
    public byte[] Serialize(MimeMessage message)
    {
        // 补全每个部分的传输编码（二进制用 Base64、非 ASCII 文本用 QP 等）
        message.Prepare(EncodingConstraint.SevenBit);
        NormalizeBoundaries(message);

        using var stream = new MemoryStream();
        message.WriteTo(_formatOptions, stream);
        return stream.ToArray();
    }

    public byte[] Write(string relativePath, MimeMessage message)
    {
        var bytes = Serialize(message);
        Write(relativePath, bytes);
        return bytes;
    }

    public void Write(string relativePath, byte[] bytes)
    {
        var path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        _written.Add(relativePath);
    }

    /// <summary>
    /// MimeKit 默认每次随机生成 multipart 边界；这里改成按出现顺序编号的固定值，让重新生成的结果逐字节一致。
    /// </summary>
    /// <remarks>
    /// 已经签名的 multipart/signed 不要重复调用本方法：签名覆盖的是内部部分，外层边界可以改，内层不能改。
    /// 所以 S/MIME 样本需要在签名前自己设置好内部边界，这里对 <see cref="MultipartSigned"/> 的内部部分不再改动。
    /// </remarks>
    public static void NormalizeBoundaries(MimeMessage message)
    {
        var counter = 0;
        if (message.Body is not null)
        {
            Visit(message.Body, ref counter);
        }
    }

    private static void Visit(MimeEntity entity, ref int counter)
    {
        switch (entity)
        {
            case MultipartSigned signed:
                // 外层边界可以改；内部已经被签名，不能动
                signed.Boundary = $"----=_efv_{++counter:D2}";
                break;
            case Multipart multipart:
                multipart.Boundary = $"----=_efv_{++counter:D2}";
                foreach (var child in multipart)
                {
                    Visit(child, ref counter);
                }

                break;
            case MessagePart { Message.Body: { } body }:
                Visit(body, ref counter);
                break;
        }
    }
}
