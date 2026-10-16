using System.Buffers.Binary;

namespace EmailFileViewer.SampleGenerator.Formats;

/// <summary>
/// 最小的索引色 PNG 画布和编码器。
/// </summary>
/// <remarks>
/// IDAT 用 zlib 的"仅存储"块，不做压缩，输出只取决于像素内容，不取决于运行时自带的 zlib 版本，
/// 所以在不同机器上重新生成的结果逐字节一致。图片尺寸都很小，体积可以接受。
/// </remarks>
internal sealed class PngCanvas
{
    private readonly byte[] _pixels;
    private readonly (byte R, byte G, byte B)[] _palette;

    public PngCanvas(int width, int height, params (byte R, byte G, byte B)[] palette)
    {
        Width = width;
        Height = height;
        _palette = palette;
        _pixels = new byte[width * height];
    }

    public int Width { get; }

    public int Height { get; }

    public void FillAll(byte color) => Array.Fill(_pixels, color);

    public void FillRect(int x, int y, int width, int height, byte color)
    {
        for (var yy = Math.Max(0, y); yy < Math.Min(Height, y + height); yy++)
        {
            for (var xx = Math.Max(0, x); xx < Math.Min(Width, x + width); xx++)
            {
                _pixels[yy * Width + xx] = color;
            }
        }
    }

    public void FillCircle(int centerX, int centerY, int radius, byte color)
    {
        for (var y = centerY - radius; y <= centerY + radius; y++)
        {
            for (var x = centerX - radius; x <= centerX + radius; x++)
            {
                if (x >= 0 && y >= 0 && x < Width && y < Height
                    && ((x - centerX) * (x - centerX)) + ((y - centerY) * (y - centerY)) <= radius * radius)
                {
                    _pixels[y * Width + x] = color;
                }
            }
        }
    }

    public void FillTriangle(int x0, int y0, int x1, int y1, int x2, int y2, byte color)
    {
        for (var y = Math.Min(y0, Math.Min(y1, y2)); y <= Math.Max(y0, Math.Max(y1, y2)); y++)
        {
            for (var x = Math.Min(x0, Math.Min(x1, x2)); x <= Math.Max(x0, Math.Max(x1, x2)); x++)
            {
                if (x < 0 || y < 0 || x >= Width || y >= Height)
                {
                    continue;
                }

                var d1 = Sign(x, y, x0, y0, x1, y1);
                var d2 = Sign(x, y, x1, y1, x2, y2);
                var d3 = Sign(x, y, x2, y2, x0, y0);
                var hasNegative = d1 < 0 || d2 < 0 || d3 < 0;
                var hasPositive = d1 > 0 || d2 > 0 || d3 > 0;
                if (!(hasNegative && hasPositive))
                {
                    _pixels[y * Width + x] = color;
                }
            }
        }
    }

    public void StrokeRect(int x, int y, int width, int height, int thickness, byte color)
    {
        FillRect(x, y, width, thickness, color);
        FillRect(x, y + height - thickness, width, thickness, color);
        FillRect(x, y, thickness, height, color);
        FillRect(x + width - thickness, y, thickness, height, color);
    }

    public byte[] ToPng()
    {
        using var output = new MemoryStream();
        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header[0..4], (uint)Width);
        BinaryPrimitives.WriteUInt32BigEndian(header[4..8], (uint)Height);
        header[8] = 8; // 位深
        header[9] = 3; // 索引色
        WriteChunk(output, "IHDR", header);

        var palette = new byte[_palette.Length * 3];
        for (var i = 0; i < _palette.Length; i++)
        {
            palette[i * 3] = _palette[i].R;
            palette[i * 3 + 1] = _palette[i].G;
            palette[i * 3 + 2] = _palette[i].B;
        }

        WriteChunk(output, "PLTE", palette);

        var raw = new byte[(Width + 1) * Height];
        for (var y = 0; y < Height; y++)
        {
            // 每行开头是过滤类型 0（不过滤）
            Array.Copy(_pixels, y * Width, raw, y * (Width + 1) + 1, Width);
        }

        WriteChunk(output, "IDAT", ZlibStored(raw));
        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    private static int Sign(int px, int py, int ax, int ay, int bx, int by)
    {
        return ((px - bx) * (ay - by)) - ((ax - bx) * (py - by));
    }

    private static byte[] ZlibStored(byte[] data)
    {
        using var stream = new MemoryStream();
        stream.WriteByte(0x78);
        stream.WriteByte(0x01);

        const int MaxBlock = 65535;
        var offset = 0;
        do
        {
            var length = Math.Min(MaxBlock, data.Length - offset);
            var isFinal = offset + length >= data.Length;
            stream.WriteByte(isFinal ? (byte)1 : (byte)0);
            stream.WriteByte((byte)(length & 0xFF));
            stream.WriteByte((byte)(length >> 8));
            stream.WriteByte((byte)(~length & 0xFF));
            stream.WriteByte((byte)((~length >> 8) & 0xFF));
            stream.Write(data, offset, length);
            offset += length;
        }
        while (offset < data.Length);

        Span<byte> adler = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(adler, Checksums.Adler32(data));
        stream.Write(adler);
        return stream.ToArray();
    }

    private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(number, (uint)data.Length);
        output.Write(number);

        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);

        var crc = Checksums.Crc32(typeBytes);
        crc = Checksums.Crc32(data, crc);
        BinaryPrimitives.WriteUInt32BigEndian(number, crc);
        output.Write(number);
    }
}
