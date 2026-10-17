using System.Text;

namespace EmailFileViewer.SampleGenerator.Formats;

/// <summary>
/// 最小的 TNEF（winmail.dat）写入器，只写 Outlook 常见的那几个属性。
/// </summary>
/// <remarks>
/// 结构参考 MS-OXTNEF：签名、附件键，然后是一串"级别、属性 ID、长度、数据、校验和"。
/// 每个附件以 attAttachRenddata 开头，随后是文件名、文件内容和附件的 MAPI 属性。
/// </remarks>
internal static class Tnef
{
    private const byte LevelMessage = 1;
    private const byte LevelAttachment = 2;

    private const uint AttSubject = 0x00018004;
    private const uint AttMessageClass = 0x00078008;
    private const uint AttBody = 0x0002800C;
    private const uint AttAttachData = 0x0006800F;
    private const uint AttAttachTitle = 0x00018010;
    private const uint AttAttachRenddata = 0x00069002;
    private const uint AttAttachment = 0x00069005;
    private const uint AttTnefVersion = 0x00089006;
    private const uint AttOemCodepage = 0x00069007;

    private const uint PrAttachMethod = 0x37050003;
    private const uint PrAttachLongFilename = 0x3707001E;
    private const uint PrAttachFilename = 0x3704001E;
    private const uint PrAttachExtension = 0x3703001E;
    private const uint PrAttachMimeTag = 0x370E001E;
    private const uint PrRenderingPosition = 0x370B0003;

    public sealed record TnefFile(string FileName, string MimeType, byte[] Data);

    public static byte[] Create(string subject, string body, params TnefFile[] files)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        writer.Write(0x223E9F78u);
        writer.Write((ushort)0x0001);

        WriteAttribute(writer, LevelMessage, AttTnefVersion, BitConverter.GetBytes(0x00010000u));
        WriteAttribute(writer, LevelMessage, AttOemCodepage, [0xE4, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]);
        WriteAttribute(writer, LevelMessage, AttMessageClass, NullTerminated("IPM.Microsoft Mail.Note"));
        WriteAttribute(writer, LevelMessage, AttSubject, NullTerminated(subject));
        WriteAttribute(writer, LevelMessage, AttBody, NullTerminated(body));

        foreach (var file in files)
        {
            // atyp = 1（文件）、位置 0xFFFFFFFF（不内嵌在正文里）、宽高 0、标志 0
            var renddata = new byte[14];
            BitConverter.GetBytes((ushort)1).CopyTo(renddata, 0);
            BitConverter.GetBytes(0xFFFFFFFFu).CopyTo(renddata, 2);
            WriteAttribute(writer, LevelAttachment, AttAttachRenddata, renddata);
            WriteAttribute(writer, LevelAttachment, AttAttachTitle, NullTerminated(file.FileName));
            WriteAttribute(writer, LevelAttachment, AttAttachData, file.Data);
            WriteAttribute(writer, LevelAttachment, AttAttachment, BuildAttachmentProperties(file));
        }

        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] BuildAttachmentProperties(TnefFile file)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        var extension = Path.GetExtension(file.FileName);
        var shortName = $"{Path.GetFileNameWithoutExtension(file.FileName).ToUpperInvariant()[..Math.Min(6, Path.GetFileNameWithoutExtension(file.FileName).Length)]}~1{extension.ToUpperInvariant()}";

        writer.Write(6u); // 属性个数
        WriteLong(writer, PrAttachMethod, 1);
        WriteLong(writer, PrRenderingPosition, -1);
        WriteString8(writer, PrAttachLongFilename, file.FileName);
        WriteString8(writer, PrAttachFilename, shortName);
        WriteString8(writer, PrAttachExtension, extension);
        WriteString8(writer, PrAttachMimeTag, file.MimeType);

        writer.Flush();
        return stream.ToArray();
    }

    private static void WriteLong(BinaryWriter writer, uint tag, int value)
    {
        writer.Write(tag);
        writer.Write(value);
    }

    private static void WriteString8(BinaryWriter writer, uint tag, string value)
    {
        var bytes = NullTerminated(value);
        writer.Write(tag);
        writer.Write(1u); // 值的个数
        writer.Write((uint)bytes.Length);
        writer.Write(bytes);
        var padding = (4 - (bytes.Length % 4)) % 4;
        writer.Write(new byte[padding]);
    }

    private static void WriteAttribute(BinaryWriter writer, byte level, uint id, byte[] data)
    {
        writer.Write(level);
        writer.Write(id);
        writer.Write((uint)data.Length);
        writer.Write(data);

        ushort checksum = 0;
        foreach (var b in data)
        {
            checksum += b;
        }

        writer.Write(checksum);
    }

    private static byte[] NullTerminated(string text)
    {
        // 只放 ASCII 内容，避免依赖 OEM 代码页
        var bytes = new byte[text.Length + 1];
        Encoding.ASCII.GetBytes(text, bytes);
        return bytes;
    }
}
