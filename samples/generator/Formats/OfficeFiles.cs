using System.Security;
using System.Text;

namespace EmailFileViewer.SampleGenerator.Formats;

/// <summary>
/// 生成能被 Office 打开的最小 docx / xlsx，以及一个最小 PDF。内容全是占位文字。
/// </summary>
internal static class OfficeFiles
{
    public static byte[] CreateDocx(string title, params string[] paragraphs)
    {
        var body = new StringBuilder();
        body.Append("<w:p><w:r><w:rPr><w:b/></w:rPr><w:t>").Append(SecurityElement.Escape(title)).Append("</w:t></w:r></w:p>");
        foreach (var paragraph in paragraphs)
        {
            body.Append("<w:p><w:r><w:t xml:space=\"preserve\">").Append(SecurityElement.Escape(paragraph)).Append("</w:t></w:r></w:p>");
        }

        var zip = new StoredZipWriter();
        zip.Add("[Content_Types].xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>
            """);
        zip.Add("_rels/.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>
            """);
        zip.Add("word/document.xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n"
            + "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>"
            + body
            + "</w:body></w:document>");
        return zip.ToArray();
    }

    public static byte[] CreateXlsx(string[][] rows)
    {
        var sheet = new StringBuilder();
        sheet.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n");
        sheet.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
        for (var r = 0; r < rows.Length; r++)
        {
            sheet.Append("<row r=\"").Append(r + 1).Append("\">");
            for (var c = 0; c < rows[r].Length; c++)
            {
                var reference = $"{(char)('A' + c)}{r + 1}";
                var value = rows[r][c];
                if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))
                {
                    sheet.Append("<c r=\"").Append(reference).Append("\"><v>").Append(value).Append("</v></c>");
                }
                else
                {
                    sheet.Append("<c r=\"").Append(reference).Append("\" t=\"inlineStr\"><is><t>")
                        .Append(SecurityElement.Escape(value)).Append("</t></is></c>");
                }
            }

            sheet.Append("</row>");
        }

        sheet.Append("</sheetData></worksheet>");

        var zip = new StoredZipWriter();
        zip.Add("[Content_Types].xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/></Types>
            """);
        zip.Add("_rels/.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>
            """);
        zip.Add("xl/workbook.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Sheet1" sheetId="1" r:id="rId1"/></sheets></workbook>
            """);
        zip.Add("xl/_rels/workbook.xml.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/></Relationships>
            """);
        zip.Add("xl/worksheets/sheet1.xml", sheet.ToString());
        return zip.ToArray();
    }

    /// <summary>
    /// 生成只有一页文字的 PDF，xref 偏移按实际写入位置计算。<paramref name="text"/> 只能包含 ASCII。
    /// </summary>
    public static byte[] CreatePdf(string text)
    {
        var escaped = text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
        var content = $"BT /F1 14 Tf 20 100 Td ({escaped}) Tj ET";

        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 144] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        ];

        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(pdf.Length);
            pdf.Append(i + 1).Append(" 0 obj\n").Append(objects[i]).Append("\nendobj\n");
        }

        var xrefOffset = pdf.Length;
        pdf.Append("xref\n0 ").Append(objects.Length + 1).Append('\n');
        pdf.Append("0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            pdf.Append(offset.ToString("D10")).Append(" 00000 n \n");
        }

        pdf.Append("trailer\n<< /Size ").Append(objects.Length + 1).Append(" /Root 1 0 R >>\n");
        pdf.Append("startxref\n").Append(xrefOffset).Append("\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }
}

/// <summary>
/// 只存储不压缩的 ZIP 写入器，时间戳固定。.NET 自带的 ZipArchive 会按运行平台写不同的"创建系统"字段，
/// 在 macOS 和 Windows 上生成的字节会不一样，所以这里自己写。
/// </summary>
internal sealed class StoredZipWriter
{
    // 1980-01-01 00:00:00 的 DOS 时间
    private const ushort DosTime = 0;
    private const ushort DosDate = (1 << 5) | 1;

    private readonly List<(string Name, byte[] Data, uint Crc, uint Offset)> _entries = [];
    private readonly MemoryStream _stream = new();

    public void Add(string name, string content) => Add(name, new UTF8Encoding(false).GetBytes(content.ReplaceLineEndings("\n")));

    public void Add(string name, byte[] data)
    {
        var crc = Checksums.Crc32(data);
        var offset = (uint)_stream.Length;
        var nameBytes = Encoding.UTF8.GetBytes(name);
        using var writer = new BinaryWriter(_stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(0x04034B50u);
        writer.Write((ushort)20); // 需要的版本
        writer.Write((ushort)0x0800); // 文件名为 UTF-8
        writer.Write((ushort)0); // 存储
        writer.Write(DosTime);
        writer.Write(DosDate);
        writer.Write(crc);
        writer.Write((uint)data.Length);
        writer.Write((uint)data.Length);
        writer.Write((ushort)nameBytes.Length);
        writer.Write((ushort)0);
        writer.Write(nameBytes);
        writer.Write(data);
        _entries.Add((name, data, crc, offset));
    }

    public byte[] ToArray()
    {
        using var output = new MemoryStream();
        output.Write(_stream.ToArray());
        var centralStart = (uint)output.Length;
        using (var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true))
        {
            foreach (var (name, data, crc, offset) in _entries)
            {
                var nameBytes = Encoding.UTF8.GetBytes(name);
                writer.Write(0x02014B50u);
                writer.Write((ushort)20); // 创建版本，固定为 DOS/FAT，不随平台变化
                writer.Write((ushort)20);
                writer.Write((ushort)0x0800);
                writer.Write((ushort)0);
                writer.Write(DosTime);
                writer.Write(DosDate);
                writer.Write(crc);
                writer.Write((uint)data.Length);
                writer.Write((uint)data.Length);
                writer.Write((ushort)nameBytes.Length);
                writer.Write((ushort)0);
                writer.Write((ushort)0);
                writer.Write((ushort)0);
                writer.Write((ushort)0);
                writer.Write(0u);
                writer.Write(offset);
                writer.Write(nameBytes);
            }

            var centralSize = (uint)output.Length - centralStart;
            writer.Write(0x06054B50u);
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write((ushort)_entries.Count);
            writer.Write((ushort)_entries.Count);
            writer.Write(centralSize);
            writer.Write(centralStart);
            writer.Write((ushort)0);
        }

        return output.ToArray();
    }
}
