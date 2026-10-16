using System.Buffers.Binary;
using OpenMcdf;

namespace EmailFileViewer.SampleGenerator;

/// <summary>
/// 把 MSG 里"按值"保存的附件改写成真正的嵌入邮件（ATTACH_EMBEDDED_MSG）。
/// </summary>
/// <remarks>
/// MsgKit 只支持把文件按值放进附件（PR_ATTACH_DATA_BIN），没有嵌入邮件的 API，
/// 所以先用 MsgKit 把内层 MSG 当作普通文件放进外层，再改写复合文档：
/// 1. 附件的 PR_ATTACH_METHOD 从 1 改成 5；
/// 2. PR_ATTACH_DATA_BIN 流换成 PR_ATTACH_DATA_OBJ 存储（__substg1.0_3701000D），里面放内层邮件的全部内容；
/// 3. 内层邮件的 __properties_version1.0 头部是 32 字节（顶层邮件），作为嵌入邮件要改成 24 字节，并去掉 __nameid_version1.0。
/// 格式依据 MS-OXMSG，并对照了 Outlook 生成的样本 EmailWithMsgAttachment.msg 的属性布局。
/// </remarks>
internal static class MsgNesting
{
    private const string PropertiesStream = "__properties_version1.0";
    private const string NameIdStorage = "__nameid_version1.0";
    private const string AttachDataBinStream = "__substg1.0_37010102";
    private const string AttachDataObjStorage = "__substg1.0_3701000D";

    private const uint PrAttachMethod = 0x37050003;
    private const uint PrAttachDataBin = 0x37010102;
    private const uint PrAttachDataObj = 0x3701000D;
    private const int AttachEmbeddedMessage = 5;

    /// <summary>
    /// 把 <paramref name="outerMsg"/> 的第 <paramref name="attachmentIndex"/> 个附件（内容是 <paramref name="innerMsg"/>）改写成嵌入邮件。
    /// </summary>
    public static byte[] EmbedAttachment(byte[] outerMsg, int attachmentIndex, byte[] innerMsg)
    {
        var attachmentName = $"__attach_version1.0_#{attachmentIndex:X8}";

        using var outerStream = new MemoryStream(outerMsg);
        using var innerStream = new MemoryStream(innerMsg);
        using var outerRoot = RootStorage.Open(outerStream, StorageModeFlags.LeaveOpen);
        using var innerRoot = RootStorage.Open(innerStream, StorageModeFlags.LeaveOpen);

        var output = new MemoryStream();
        using (var newRoot = RootStorage.Create(output, OpenMcdf.Version.V3, StorageModeFlags.LeaveOpen))
        {
            foreach (var entry in outerRoot.EnumerateEntries())
            {
                if (entry.Type == EntryType.Storage)
                {
                    var source = outerRoot.OpenStorage(entry.Name);
                    var target = newRoot.CreateStorage(entry.Name);
                    if (entry.Name == attachmentName)
                    {
                        RewriteAttachment(source, target, innerRoot);
                    }
                    else
                    {
                        CopyStorage(source, target, _ => false);
                    }
                }
                else
                {
                    CopyStream(outerRoot, newRoot, entry.Name);
                }
            }
        }

        return output.ToArray();
    }

    private static void RewriteAttachment(Storage source, Storage target, Storage innerRoot)
    {
        foreach (var entry in source.EnumerateEntries())
        {
            if (entry.Type == EntryType.Storage)
            {
                CopyStorage(source.OpenStorage(entry.Name), target.CreateStorage(entry.Name), _ => false);
            }
            else if (entry.Name == AttachDataBinStream)
            {
                // 被 __substg1.0_3701000D 存储取代
            }
            else if (entry.Name == PropertiesStream)
            {
                WriteStream(target, entry.Name, ConvertAttachmentProperties(ReadStream(source, entry.Name)));
            }
            else
            {
                CopyStream(source, target, entry.Name);
            }
        }

        var embedded = target.CreateStorage(AttachDataObjStorage);
        foreach (var entry in innerRoot.EnumerateEntries())
        {
            if (entry.Type == EntryType.Storage)
            {
                if (entry.Name != NameIdStorage)
                {
                    CopyStorage(innerRoot.OpenStorage(entry.Name), embedded.CreateStorage(entry.Name), _ => false);
                }
            }
            else if (entry.Name == PropertiesStream)
            {
                WriteStream(embedded, entry.Name, ConvertTopLevelPropertiesToEmbedded(ReadStream(innerRoot, entry.Name)));
            }
            else
            {
                CopyStream(innerRoot, embedded, entry.Name);
            }
        }
    }

    // 附件属性流：8 字节头，然后每个属性 16 字节（标签 4、标志 4、值 8）
    private static byte[] ConvertAttachmentProperties(byte[] properties)
    {
        var result = properties.ToArray();
        for (var offset = 8; offset + 16 <= result.Length; offset += 16)
        {
            var tag = BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan(offset, 4));
            if (tag == PrAttachMethod)
            {
                BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(offset + 8, 4), AttachEmbeddedMessage);
            }
            else if (tag == PrAttachDataBin)
            {
                // Outlook 生成的嵌入邮件里，这一项是 PR_ATTACH_DATA_OBJ，大小字段 0xFFFFFFFF，保留字段 1
                BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(offset, 4), PrAttachDataObj);
                BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(offset + 8, 4), 0xFFFFFFFF);
                BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(offset + 12, 4), 1);
            }
        }

        return result;
    }

    // 顶层邮件的属性流头部是 32 字节（8 保留、4 个计数、8 保留），嵌入邮件是 24 字节（没有末尾的 8 字节保留）
    private static byte[] ConvertTopLevelPropertiesToEmbedded(byte[] properties)
    {
        return [.. properties.AsSpan(0, 24), .. properties.AsSpan(32)];
    }

    private static void CopyStorage(Storage source, Storage target, Func<string, bool> skip)
    {
        foreach (var entry in source.EnumerateEntries())
        {
            if (skip(entry.Name))
            {
                continue;
            }

            if (entry.Type == EntryType.Storage)
            {
                CopyStorage(source.OpenStorage(entry.Name), target.CreateStorage(entry.Name), skip);
            }
            else
            {
                CopyStream(source, target, entry.Name);
            }
        }
    }

    private static void CopyStream(Storage source, Storage target, string name) => WriteStream(target, name, ReadStream(source, name));

    private static byte[] ReadStream(Storage storage, string name)
    {
        using var stream = storage.OpenStream(name);
        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private static void WriteStream(Storage storage, string name, byte[] bytes)
    {
        using var stream = storage.CreateStream(name);
        stream.Write(bytes);
    }
}
