using MimeKit;

namespace EmailFileViewer.SampleGenerator;

/// <summary>
/// 由前面生成的文件派生出来的样本：改扩展名、截断、作为附件、远程图片素材。
/// </summary>
internal static class DerivedSamples
{
    public static void WriteAll(SampleOutput output)
    {
        WriteAssets(output);
        WriteFormatMismatches(output);
        WriteMsgAttachment(output);
        WriteInvalid(output);
    }

    // samples/assets：营销邮件里远程图片、样式表和追踪像素指向的文件，以及 photo.jpg
    private static void WriteAssets(SampleOutput output)
    {
        output.Write("assets/logo.png", Assets.Logo());
        output.Write("assets/hero.png", Assets.Hero());
        for (var i = 1; i <= 3; i++)
        {
            output.Write($"assets/product-{i}.png", Assets.Product(i));
        }

        output.Write("assets/social-1.png", Assets.Social(1));
        output.Write("assets/social-2.png", Assets.Social(2));
        output.Write("assets/pixel.gif", Assets.Pixel);
        output.Write("assets/photo.jpg", Assets.Photo());
        output.Write("assets/marketing.css", Assets.MarketingCss());
    }

    // 扩展名和内容不一致：.eml 里是 MSG，.msg 里是 EML。应用按内容判断格式
    private static void WriteFormatMismatches(SampleOutput output)
    {
        output.Write("format-msg-disguised-as-eml.eml", Read(output, MsgSamples.RtfOnlyFileName));
        output.Write("format-eml-disguised-as-msg.msg", Read(output, "body-plain-only.eml"));
    }

    // EML 里作为附件的 .msg 文件
    private static void WriteMsgAttachment(SampleOutput output)
    {
        var message = MimeSamples.NewMessage("attachment-msg-file", "Forwarded as attachment: a .msg file", Personas.Bob, Personas.BaseDate.AddHours(2));
        message.To.Add(Personas.Alice);
        message.Body = new Multipart("mixed")
        {
            new TextPart("plain") { Text = "The attachment forwarded-message.msg is an Outlook MSG file (an RTF-only message).\n" },
            new MimePart("application", "vnd.ms-outlook")
            {
                Content = new MimeContent(new MemoryStream(Read(output, MsgSamples.RtfOnlyFileName))),
                ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
                ContentTransferEncoding = ContentEncoding.Base64,
                FileName = "forwarded-message.msg",
            },
        };
        output.Write("attachment-msg-file.eml", message);
    }

    // samples/invalid：打不开的文件。smoke 不递归，不会读到这里
    private static void WriteInvalid(SampleOutput output)
    {
        // 截断的 MSG：复合文档的目录和扇区表被截掉，OLE 签名还在
        var msg = Read(output, MsgSamples.RtfOnlyFileName);
        output.Write("invalid/truncated.msg", msg[..(msg.Length / 2)]);

        // 截断成一半的 EML：断在第二个附件的 Base64 数据中间，没有结束边界
        var eml = Read(output, "attachments-pdf-xlsx-jpg.eml");
        output.Write("invalid/truncated-half.eml", eml[..(eml.Length / 2)]);

        // 一张 PNG，以及伪装成 .eml 的 PNG
        output.Write("invalid/not-an-email.png", Assets.Logo());
        output.Write("invalid/png-disguised-as-eml.eml", Assets.Logo());

        // 空文件
        output.Write("invalid/empty.eml", []);
    }

    private static byte[] Read(SampleOutput output, string relativePath)
    {
        return File.ReadAllBytes(Path.Combine(output.Root, relativePath));
    }
}
