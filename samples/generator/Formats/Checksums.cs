namespace EmailFileViewer.SampleGenerator.Formats;

/// <summary>
/// 手写的 CRC32 和 Adler32，用来拼 PNG 和 ZIP，避免引入额外的包。
/// </summary>
internal static class Checksums
{
    private static readonly uint[] Crc32Table = BuildCrc32Table();

    public static uint Crc32(ReadOnlySpan<byte> data, uint seed = 0)
    {
        var crc = ~seed;
        foreach (var b in data)
        {
            crc = Crc32Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return ~crc;
    }

    public static uint Adler32(ReadOnlySpan<byte> data)
    {
        uint a = 1, b = 0;
        foreach (var value in data)
        {
            a = (a + value) % 65521;
            b = (b + a) % 65521;
        }

        return (b << 16) | a;
    }

    private static uint[] BuildCrc32Table()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
