using System.Text;
using EmailFileViewer.SampleGenerator.Formats;
using MimeKit;

namespace EmailFileViewer.SampleGenerator;

/// <summary>
/// 用 MimeKit 构造的 EML 样本：邮件头、正文、内嵌图片、营销邮件、恶意 HTML、各类附件、嵌套邮件。
/// </summary>
internal static class MimeSamples
{
    private const string RemoteBase = Assets.RemoteBase;

    public static void WriteAll(SampleOutput output)
    {
        WriteHeaderSamples(output);
        WriteBodySamples(output);
        WriteInlineAndRemoteSamples(output);
        WriteSecuritySamples(output);
        WriteAttachmentSamples(output);
        WriteNestedSamples(output);
    }

    #region 邮件头

    private static void WriteHeaderSamples(SampleOutput output)
    {
        // 普通邮件：发件人、两个收件人、一个抄送
        var normal = NewMessage("headers-normal", "Quarterly planning meeting", Personas.Alice, Personas.BaseDate);
        normal.To.Add(Personas.Bob);
        normal.To.Add(Personas.Carol);
        normal.Cc.Add(Personas.Dave);
        normal.Body = new TextPart("plain")
        {
            Text = "Hi Bob and Carol,\n\nThe quarterly planning meeting is on Friday at 14:00.\nDave is copied for visibility.\n\nAlice\n",
        };
        output.Write("headers-normal.eml", normal);

        // 跨时区日期、有 Bcc 和 Reply-To、没有 Cc
        var timezone = NewMessage(
            "headers-timezone",
            "Cross-timezone date, Bcc and Reply-To, no Cc",
            Personas.Alice,
            new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.FromHours(-7)));
        timezone.To.Add(Personas.Bob);
        timezone.Bcc.Add(Personas.Erin);
        timezone.ReplyTo.Add(new MailboxAddress("Alice Zhang (Support)", "support@example.com"));

        // 日期头逐字写成 `Mon, 5 Oct 2026 09:00:00 -0700`，MimeKit 默认会把日补成两位
        timezone.Headers.Replace(HeaderId.Date, "Mon, 5 Oct 2026 09:00:00 -0700");
        timezone.Body = new TextPart("plain")
        {
            Text = "Sent at Mon, 5 Oct 2026 09:00:00 -0700.\n"
                + "In a UTC+8 time zone this is 2026-10-06 00:00.\n"
                + "This message has Bcc and Reply-To headers but no Cc header.\n",
        };
        output.Write("headers-timezone-bcc-replyto-no-cc.eml", timezone);
    }

    #endregion

    #region 正文

    private static void WriteBodySamples(SampleOutput output)
    {
        // 同时有 HTML 和纯文本：两个版本的文字不一样，方便看出显示的是哪一个
        var both = NewMessage("body-html-and-plain", "HTML and plain-text alternatives", Personas.Carol, Personas.BaseDate.AddHours(1));
        both.To.Add(Personas.Alice);
        both.Body = new BodyBuilder
        {
            TextBody = "PLAIN-TEXT-VERSION\n\nThis is the plain-text alternative. If you can read this in the viewer, the HTML alternative was not selected.\n",
            HtmlBody = """
                <html><head><meta charset="utf-8"><title>HTML version</title></head>
                <body>
                <h1>HTML-VERSION</h1>
                <p>This is the <b>HTML</b> alternative with <i>basic formatting</i> and a
                <a href="https://example.com/">link</a>.</p>
                <ul><li>First item</li><li>Second item</li></ul>
                </body></html>
                """,
        }.ToMessageBody();
        output.Write("body-html-and-plain.eml", both);

        // 只有纯文本：多段、连续空行、缩进、制表符、引用行、需要转义的字符
        var plain = NewMessage("body-plain-only", "Plain text only: paragraphs and blank lines", Personas.Bob, Personas.BaseDate.AddHours(2));
        plain.To.Add(Personas.Alice);
        plain.Body = new TextPart("plain")
        {
            Text = string.Join(
                "\n",
                "Hello team,",
                "",
                "This is a plain-text-only message used to check paragraph handling.",
                "",
                "First paragraph: a few sentences. The second sentence is here.",
                "Line two of the same paragraph (a hard line break inside the paragraph).",
                "",
                "",
                "Third paragraph comes after TWO blank lines.",
                "    Indented line with four spaces.",
                "\tIndented line with a tab.",
                "",
                "- list item one",
                "- list item two",
                "",
                "> quoted line one",
                "> quoted line two",
                "",
                "Characters that must be escaped when shown as HTML: <b>not bold</b> & \"quotes\" 'single'.",
                "A URL on its own line: https://example.com/path?a=1&b=2",
                "Non-ASCII text: 中文段落，用来确认 UTF-8 纯文本也能正确显示。",
                "",
                "Regards,",
                "Bob",
                "-- ",
                "Bob Li",
                "Sample Org",
                ""),
        };
        output.Write("body-plain-only.eml", plain);

        WriteSegmentedBodySample(output);
    }

    // Apple Mail 在正文里插入图片时的结构：multipart/alternative 的 HTML 备选是一个 multipart/mixed，
    // 里面交替放着多个 text/html 和内嵌图片（Content-Disposition: inline，带 Content-ID 但 HTML 里没有引用），最后是真正的附件
    private static void WriteSegmentedBodySample(SampleOutput output)
    {
        var message = NewMessage("body-multipart-mixed-inline-images", "Segmented body with inline images (Apple Mail style)", Personas.Dave, Personas.BaseDate.AddHours(2).AddMinutes(30));
        message.To.Add(Personas.Alice);

        var mixed = new Multipart("mixed")
        {
            SegmentHtml("<div>SEGMENT-ONE: text before the first picture.</div>"),
            InlineImage("photo.jpg", "image/jpeg", Assets.Photo(), "apple-photo@samples.efv.invalid"),
            SegmentHtml("<div>SEGMENT-TWO: text between the two pictures. 中文：两张图片之间的文字。</div>"),
            InlineImage("logo.png", "image/png", Assets.Logo(), "apple-logo@samples.efv.invalid"),
            SegmentHtml("<div>SEGMENT-THREE: text after the second picture.</div>"),
            BinaryAttachment("itinerary.pdf", "application/pdf", OfficeFiles.CreatePdf("Itinerary attached to the segmented body")),
        };

        message.Body = new Multipart("alternative")
        {
            new TextPart("plain") { Text = "PLAIN-ALTERNATIVE: the plain-text alternative of the segmented body. The viewer shows the HTML parts instead.\n" },
            mixed,
        };
        output.Write("body-multipart-mixed-inline-images.eml", message);
    }

    private static TextPart SegmentHtml(string content)
    {
        return new TextPart("html")
        {
            Text = "<html><head><meta http-equiv=\"content-type\" content=\"text/html; charset=utf-8\"></head>"
                + "<body style=\"overflow-wrap: break-word; -webkit-nbsp-mode: space; line-break: after-white-space;\">"
                + content
                + "</body></html>",
        };
    }

    private static MimePart InlineImage(string fileName, string contentType, byte[] data, string contentId)
    {
        return new MimePart(ContentType.Parse(contentType))
        {
            Content = new MimeContent(new MemoryStream(data)),
            ContentDisposition = new ContentDisposition(ContentDisposition.Inline) { FileName = fileName },
            ContentTransferEncoding = ContentEncoding.Base64,
            ContentId = contentId,
        };
    }

    #endregion

    #region cid 内嵌图片、营销邮件

    private static void WriteInlineAndRemoteSamples(SampleOutput output)
    {
        // HTML 通过 cid 引用 logo 和签名图，另有一个普通附件 docx
        var cid = NewMessage("cid-inline-image", "Inline images via cid, plus one attachment", Personas.Alice, Personas.BaseDate.AddHours(3));
        cid.To.Add(Personas.Bob);

        var builder = new BodyBuilder
        {
            TextBody = "Please see the attached report. The company logo is embedded in the HTML version.\n",
            HtmlBody = """
                <html><body>
                <p>Hello Bob,</p>
                <p>Please see the attached report.</p>
                <p><img src="cid:logo@samples.efv.invalid" width="120" height="40" alt="Company logo"></p>
                <p>公司标志在上面 (logo above). Signature image:</p>
                <p><img src="cid:signature@samples.efv.invalid" width="24" height="24" alt="Signature mark"></p>
                <p>Alice Zhang</p>
                </body></html>
                """,
        };

        // logo 带 Content-Disposition: inline；签名图只有 Content-ID，没有 Content-Disposition。两种写法都很常见
        var logo = (MimePart)builder.LinkedResources.Add("logo.png", Assets.Logo(), ContentType.Parse("image/png"));
        logo.ContentId = "logo@samples.efv.invalid";
        logo.ContentDisposition = new ContentDisposition(ContentDisposition.Inline) { FileName = "logo.png" };

        var signature = (MimePart)builder.LinkedResources.Add("signature.png", Assets.Social(1), ContentType.Parse("image/png"));
        signature.ContentId = "signature@samples.efv.invalid";
        signature.ContentDisposition = null;

        builder.Attachments.Add(
            "quarterly-report.docx",
            OfficeFiles.CreateDocx("Quarterly report", "This is a placeholder document attached to the cid sample."),
            ContentType.Parse("application/vnd.openxmlformats-officedocument.wordprocessingml.document"));
        cid.Body = builder.ToMessageBody();
        output.Write("cid-inline-image.eml", cid);

        // 营销邮件：600px 表格布局，多张远程图片、远程样式表、远程字体、追踪像素
        var marketing = NewMessage("marketing-remote-content", "Autumn Sale: up to 40% off", new MailboxAddress("Sample Shop", "news@shop.example.com"), Personas.BaseDate.AddHours(4));
        marketing.To.Add(Personas.Alice);
        marketing.Headers.Add("List-Unsubscribe", "<https://example.com/unsubscribe?u=0001>");
        marketing.Body = new BodyBuilder
        {
            TextBody = "Autumn Sale: up to 40% off\n\nThis is the plain-text version of a marketing email.\nShop now: https://example.com/shop?utm_campaign=autumn\n",
            HtmlBody = MarketingHtml(),
        }.ToMessageBody();
        output.Write("marketing-remote-content.eml", marketing);
    }

    private static string MarketingHtml()
    {
        return $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Autumn Sale</title>
            <!-- 远程样式表 -->
            <link rel="stylesheet" href="{{RemoteBase}}marketing.css">
            <!-- 远程字体：样式表和 @font-face / @import -->
            <link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Roboto:wght@400;700&display=swap">
            <style type="text/css">
              @import url("https://fonts.googleapis.com/css2?family=Open+Sans&display=swap");
              @font-face { font-family: "EfvSampleFont"; src: url("https://fonts.gstatic.com/s/efvsample/v1/efv-sample.woff2") format("woff2"); }
              body { margin: 0; padding: 0; background-color: #f4f4f4; font-family: "EfvSampleFont", Roboto, Arial, sans-serif; }
              .efv-headline { color: #202124; }
              .efv-cta { background-color: #34a853; }
              .hero-bg { background-image: url("{{RemoteBase}}hero.png"); }
            </style>
            </head>
            <body style="margin:0;padding:0;background-color:#f4f4f4;" background="{{RemoteBase}}hero.png">
            <div style="display:none;max-height:0;overflow:hidden;">Up to 40% off this week only.</div>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#f4f4f4">
              <tr><td align="center">
                <table role="presentation" width="600" cellpadding="0" cellspacing="0" border="0" bgcolor="#ffffff" style="width:600px;max-width:600px;">
                  <tr><td bgcolor="#1f3a5f" style="padding:12px 20px;">
                    <a href="https://example.com/?utm_source=newsletter"><img src="{{RemoteBase}}logo.png" width="120" height="40" alt="Sample Shop" style="display:block;border:0;"></a>
                  </td></tr>
                  <tr><td class="hero-bg">
                    <img src="{{RemoteBase}}hero.png" width="600" height="200" alt="Autumn sale banner" style="display:block;border:0;width:600px;height:200px;">
                  </td></tr>
                  <tr><td style="padding:24px 20px 8px 20px;">
                    <h1 class="efv-headline" style="margin:0;font-size:28px;line-height:34px;">Autumn Sale: up to 40% off</h1>
                  </td></tr>
                  <tr><td style="padding:0 20px 16px 20px;font-size:16px;line-height:24px;color:#3c4043;">
                    Fresh picks for the new season. Prices drop this week only, while stock lasts.
                  </td></tr>
                  <tr><td style="padding:0 10px;">
                    <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
                      <tr>
                        <td width="33%" align="center" style="padding:10px;font-size:14px;"><img src="{{RemoteBase}}product-1.png" width="170" height="170" alt="Product one" style="display:block;border:0;"><br>Product one<br><b>$19</b></td>
                        <td width="33%" align="center" style="padding:10px;font-size:14px;"><img src="{{RemoteBase}}product-2.png" width="170" height="170" alt="Product two" style="display:block;border:0;"><br>Product two<br><b>$29</b></td>
                        <td width="33%" align="center" style="padding:10px;font-size:14px;"><img src="{{RemoteBase}}product-3.png" width="170" height="170" alt="Product three" style="display:block;border:0;"><br>Product three<br><b>$39</b></td>
                      </tr>
                    </table>
                  </td></tr>
                  <tr><td align="center" style="padding:24px;">
                    <a class="efv-cta" href="https://example.com/shop?utm_campaign=autumn&amp;utm_medium=email" style="background-color:#34a853;color:#ffffff;padding:12px 28px;text-decoration:none;border-radius:4px;display:inline-block;font-weight:bold;">Shop now</a>
                  </td></tr>
                  <tr><td align="center" style="padding:16px;font-size:12px;line-height:18px;color:#80868b;">
                    <a href="https://example.com/social/one"><img src="{{RemoteBase}}social-1.png" width="24" height="24" alt="Social one" style="border:0;"></a>
                    <a href="https://example.com/social/two"><img src="{{RemoteBase}}social-2.png" width="24" height="24" alt="Social two" style="border:0;"></a>
                    <br>You are receiving this email because you subscribed on example.com.<br>
                    <a href="https://example.com/unsubscribe?u=0001" style="color:#80868b;">Unsubscribe</a>
                  </td></tr>
                </table>
              </td></tr>
            </table>
            <!-- 追踪像素 -->
            <img src="{{RemoteBase}}pixel.gif?campaign=efv-sample&amp;uid=0001" width="1" height="1" alt="" style="display:block;border:0;width:1px;height:1px;">
            </body>
            </html>
            """;
    }

    #endregion

    #region 安全相关：恶意 HTML、链接文字与地址不符

    private static void WriteSecuritySamples(SampleOutput output)
    {
        var malicious = NewMessage("html-malicious", "Malicious HTML probe (harmless)", new MailboxAddress("Probe Sender", "probe@example.com"), Personas.BaseDate.AddHours(5));
        malicious.To.Add(Personas.Alice);
        malicious.Body = new BodyBuilder
        {
            TextBody = "This message carries a deliberately hostile HTML body for sandbox testing.\nAll probes only call the non-existent command efv_probe_should_fail and never do anything destructive.\n",
            HtmlBody = MaliciousHtml(),
        }.ToMessageBody();
        output.Write("html-malicious.eml", malicious);

        var mismatch = NewMessage("link-text-mismatch", "Verify your account (link text does not match target)", new MailboxAddress("Account Notice", "notice@example.com"), Personas.BaseDate.AddHours(6));
        mismatch.To.Add(Personas.Alice);
        mismatch.Body = new BodyBuilder
        {
            TextBody = "Link text paypal.com points to https://example.com/not-paypal\n",
            HtmlBody = """
                <html><body>
                <p>Dear customer, please verify your account.</p>
                <p>1. Domain-only text: <a href="https://example.com/not-paypal">paypal.com</a></p>
                <p>2. URL text with a different host: <a href="https://example.com/not-paypal">https://www.paypal.com/signin</a></p>
                <p>3. Spoofed title attribute: <a href="https://example.com/not-paypal" title="https://www.paypal.com/">Hover me</a></p>
                <p>4. Control, text equals target: <a href="https://example.com/">https://example.com/</a></p>
                </body></html>
                """,
        }.ToMessageBody();
        output.Write("link-text-mismatch.eml", mismatch);

        var links = NewMessage("links-anchors-and-schemes", "Links: anchors and schemes", new MailboxAddress("Link Tester", "links@example.com"), Personas.BaseDate.AddHours(15));
        links.To.Add(Personas.Alice);
        links.Body = new BodyBuilder
        {
            TextBody = "Open this message in the viewer and click every link in the HTML version.\nExpected behavior is listed in samples/README.md.\n",
            HtmlBody = LinksHtml(),
        }.ToMessageBody();
        output.Write("links-anchors-and-schemes.eml", links);
    }

    // 链接验收样本：页内锚点要能看出滚动效果，所以顶部是目录，下面是 5 个很长的段落（每段至少 420px 高），最后一段的 id 是 bottom
    private static string LinksHtml()
    {
        // 1x1 的 PNG，放大成 160x48 当图像映射的底图
        const string pixel = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

        static string Paragraph(string id, string marker)
        {
            var filler = string.Concat(Enumerable.Repeat("This sentence only exists to make the paragraph long enough that scrolling to it is clearly visible. ", 20));
            return $"<p id=\"{id}\" style=\"min-height:420px;border-left:4px solid #888;padding-left:12px;margin:0 0 32px 0;\"><b>{marker}</b> {filler}</p>";
        }

        return $$"""
            <html>
            <head><meta charset="utf-8"><title>Links: anchors and schemes</title></head>
            <body>
            <h1>Links: anchors and schemes</h1>
            <h2>Table of contents</h2>
            <ol>
            <li><a href="#section-1">Section 1</a></li>
            <li><a href="#section-2">Section 2</a></li>
            <li><a href="#section-3">Section 3</a></li>
            <li><a href="#section-4">Section 4</a></li>
            <li><a href="#bottom" target="_blank">Bottom (this anchor carries target="_blank")</a></li>
            </ol>
            <h2>Link kinds</h2>
            <ul>
            <li><a href="https://example.com/page">https link</a></li>
            <li><a href="http://example.com/plain">http link</a></li>
            <li><a href="mailto:someone@example.com">mailto link</a></li>
            <li><a href="tel:+10000000000">tel link</a></li>
            <li><a href="file:///etc/hosts">file link</a></li>
            <li><a href="page.html">relative link: page.html</a></li>
            <li><a href="?x=1">relative link: ?x=1</a></li>
            <li><a href="/root">relative link: /root</a></li>
            <li><a href="">empty link: href=""</a></li>
            </ul>
            <h2>SVG links</h2>
            <svg width="360" height="70" xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink">
            <a xlink:href="#section-2"><text x="10" y="25" fill="#06c">SVG link to #section-2 (xlink:href)</text></a>
            <a href="https://example.com/svg"><text x="10" y="55" fill="#06c">SVG link to https://example.com/svg (href)</text></a>
            </svg>
            <h2>Image map</h2>
            <p>The image below has one clickable area.</p>
            <map name="efv-map"><area shape="rect" coords="0,0,160,48" href="https://example.com/area" alt="area link"></map>
            <img src="data:image/png;base64,{{pixel}}" width="160" height="48" usemap="#efv-map" alt="image map">
            <h2>Sections</h2>
            {{Paragraph("section-1", "SECTION-1-MARKER")}}
            {{Paragraph("section-2", "SECTION-2-MARKER")}}
            {{Paragraph("section-3", "SECTION-3-MARKER")}}
            {{Paragraph("section-4", "SECTION-4-MARKER")}}
            <p id="bottom" style="min-height:420px;border-left:4px solid #888;padding-left:12px;margin:0;"><b>BOTTOM-MARKER</b> This is the last paragraph. <a href="#">Back to the top (href="#")</a></p>
            </body>
            </html>
            """;
    }

    private static string MaliciousHtml()
    {
        // 所有探测都只调用不存在的命令 efv_probe_should_fail；目标地址都用 .invalid 域名，永远不会解析到真实主机
        return """
            <!DOCTYPE html>
            <html>
            <head>
            <meta charset="utf-8">
            <title>Malicious HTML probe</title>
            <meta http-equiv="refresh" content="0;url=https://efv-probe.invalid/meta-refresh">
            <base href="https://efv-probe.invalid/base/">
            <link rel="stylesheet" href="https://efv-probe.invalid/probe.css">
            </head>
            <body onload="efvProbe('body onload')">
            <h1>Sandbox probe</h1>
            <p id="efv-marker-start">MARKER-START: the rest of the body must still be shown.</p>
            <p id="efv-script-result">NO-SCRIPT-RAN</p>

            <script>
              function efvProbe(source) {
                var target = document.getElementById('efv-script-result');
                if (target) { target.textContent = 'SCRIPT-RAN: ' + source; }
                try { window.__TAURI_INTERNALS__.invoke('efv_probe_should_fail', { source: source }); } catch (e) {}
                try { window.__TAURI__.core.invoke('efv_probe_should_fail', { source: source }); } catch (e) {}
                try { window.parent.postMessage({ command: 'efv_probe_should_fail', source: source }, '*'); } catch (e) {}
              }
              efvProbe('inline script');
            </script>

            <p><img src="x" onerror="efvProbe('img onerror')" alt="broken image"></p>
            <svg onload="efvProbe('svg onload')" width="10" height="10"><rect width="10" height="10"/></svg>
            <div onclick="efvProbe('div onclick')" onmouseover="efvProbe('div onmouseover')">Event attributes on a div (onclick, onmouseover)</div>
            <input autofocus onfocus="efvProbe('input onfocus')" value="autofocus input">
            <p><a href="javascript:efvProbe('javascript: link')">javascript: link</a></p>
            <p><a href="JaVaScRiPt:efvProbe('mixed-case javascript: link')">mixed-case javascript: link</a></p>
            <p><a href="https://example.com/ok">A normal https link that should keep working</a></p>

            <form action="https://efv-probe.invalid/form" method="post">
              <input type="text" name="q" value="form field">
              <input type="submit" value="Submit form">
            </form>

            <iframe src="https://efv-probe.invalid/iframe" width="300" height="60"></iframe>
            <iframe srcdoc="&lt;script&gt;try{window.parent.__TAURI_INTERNALS__.invoke('efv_probe_should_fail')}catch(e){}try{window.top.postMessage({command:'efv_probe_should_fail'},'*')}catch(e){}&lt;/script&gt;iframe-srcdoc" width="300" height="60"></iframe>
            <object data="https://efv-probe.invalid/object.swf" type="application/x-shockwave-flash" width="100" height="40"></object>
            <embed src="https://efv-probe.invalid/embed.swf" width="100" height="40">

            <p id="efv-marker-end">MARKER-END: reached the end of the body.</p>
            </body>
            </html>
            """;
    }

    #endregion

    #region 附件

    private static void WriteAttachmentSamples(SampleOutput output)
    {
        // 附件列表：pdf、xlsx、jpg
        var listing = NewMessage("attachments-pdf-xlsx-jpg", "Attachments: pdf, xlsx and jpg", Personas.Bob, Personas.BaseDate.AddHours(7));
        listing.To.Add(Personas.Alice);
        var listingBuilder = new BodyBuilder { TextBody = "Three attachments: a PDF, an xlsx workbook and a JPEG photo.\n" };
        listingBuilder.Attachments.Add("report.pdf", OfficeFiles.CreatePdf("Placeholder PDF report"), ContentType.Parse("application/pdf"));
        listingBuilder.Attachments.Add(
            "budget.xlsx",
            OfficeFiles.CreateXlsx([["Item", "Amount"], ["Coffee", "12.5"], ["Tea", "8"]]),
            ContentType.Parse("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"));
        listingBuilder.Attachments.Add("photo.jpg", Assets.Photo(), ContentType.Parse("image/jpeg"));
        listing.Body = listingBuilder.ToMessageBody();
        output.Write("attachments-pdf-xlsx-jpg.eml", listing);

        // 文件名里有路径成分、Windows 保留名和非法字符
        var unsafeNames = NewMessage("attachments-unsafe-names", "Attachments with unsafe file names", Personas.Carol, Personas.BaseDate.AddHours(8));
        unsafeNames.To.Add(Personas.Alice);
        var unsafeBody = new Multipart("mixed")
        {
            new TextPart("plain") { Text = "Every attachment has harmless placeholder text. Only the file names are hostile.\n" },
        };

        foreach (var name in new[]
                 {
                     "../../.bashrc",
                     "..\\..\\Windows\\evil.txt",
                     "/tmp/absolute-path.txt",
                     "CON.txt",
                     "NUL",
                     "a:b*c?.txt",
                     "x<y>z|w.txt",
                 })
        {
            unsafeBody.Add(TextAttachment(name, $"Harmless placeholder for the file name {name}\n"));
        }

        unsafeNames.Body = unsafeBody;
        output.Write("attachments-unsafe-names.eml", unsafeNames);

        // 两个同名附件，内容不同
        var duplicates = NewMessage("attachments-duplicate-names", "Two attachments named report.pdf", Personas.Dave, Personas.BaseDate.AddHours(9));
        duplicates.To.Add(Personas.Alice);
        var duplicateBody = new Multipart("mixed")
        {
            new TextPart("plain") { Text = "Two different attachments share the same file name report.pdf.\n" },
            BinaryAttachment("report.pdf", "application/pdf", OfficeFiles.CreatePdf("Report A (first)")),
            BinaryAttachment("report.pdf", "application/pdf", OfficeFiles.CreatePdf("Report B (second, different content)")),
        };
        duplicates.Body = duplicateBody;
        output.Write("attachments-duplicate-names.eml", duplicates);

        // 可执行和脚本类附件，内容只是纯文本占位
        var executables = NewMessage("attachments-executables", "Executable and script attachments (placeholders)", Personas.Erin, Personas.BaseDate.AddHours(10));
        executables.To.Add(Personas.Alice);
        var executableBody = new Multipart("mixed")
        {
            new TextPart("plain") { Text = "Each attachment is plain text. None of them is a real executable or script.\n" },
        };

        foreach (var (name, mediaType) in new[]
                 {
                     ("installer.exe", "application/x-msdownload"),
                     ("run-me.bat", "application/x-bat"),
                     ("setup.ps1", "text/plain"),
                     ("install.sh", "application/x-sh"),
                     ("launcher.command", "application/octet-stream"),
                     ("Tool.app", "application/octet-stream"),
                 })
        {
            var parts = mediaType.Split('/');
            var part = new MimePart(parts[0], parts[1])
            {
                Content = new MimeContent(new MemoryStream(Encoding.ASCII.GetBytes($"PLACEHOLDER for {name}: plain text, not executable code.\n"))),
                ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
                ContentTransferEncoding = ContentEncoding.Base64,
                FileName = name,
            };
            executableBody.Add(part);
        }

        executables.Body = executableBody;
        output.Write("attachments-executables.eml", executables);
    }

    internal static MimePart TextAttachment(string fileName, string text)
    {
        return new MimePart("text", "plain")
        {
            Content = new MimeContent(new MemoryStream(new UTF8Encoding(false).GetBytes(text))),
            ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
            ContentTransferEncoding = ContentEncoding.SevenBit,
            FileName = fileName,
        };
    }

    private static MimePart BinaryAttachment(string fileName, string contentType, byte[] data)
    {
        var type = ContentType.Parse(contentType);
        return new MimePart(type)
        {
            Content = new MimeContent(new MemoryStream(data)),
            ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
            ContentTransferEncoding = ContentEncoding.Base64,
            FileName = fileName,
        };
    }

    #endregion

    #region 嵌套邮件

    private static void WriteNestedSamples(SampleOutput output)
    {
        // 两层 message/rfc822：外层 -> 中层 -> 内层，每层的主题和正文都不同
        var inner = NewMessage("nested-inner", "Level 3 (innermost) message", Personas.Dave, Personas.BaseDate.AddHours(-2));
        inner.To.Add(Personas.Carol);
        var innerBuilder = new BodyBuilder { TextBody = "This is the innermost message (level 3). It has its own attachment.\n" };
        innerBuilder.Attachments.Add("level3-note.txt", Encoding.UTF8.GetBytes("Attachment inside the innermost message.\n"), ContentType.Parse("text/plain"));
        inner.Body = innerBuilder.ToMessageBody();

        var middle = NewMessage("nested-middle", "Level 2 (middle) message", Personas.Carol, Personas.BaseDate.AddHours(-1));
        middle.To.Add(Personas.Bob);
        middle.Body = new Multipart("mixed")
        {
            new TextPart("plain") { Text = "This is the middle message (level 2). It carries the level 3 message as a message/rfc822 part.\n" },
            new MessagePart("rfc822") { Message = inner },
        };

        var outer = NewMessage("nested-outer", "Level 1 (outer) message with two nested levels", Personas.Bob, Personas.BaseDate);
        outer.To.Add(Personas.Alice);
        outer.Body = new Multipart("mixed")
        {
            new TextPart("plain") { Text = "This is the outer message (level 1). It carries the level 2 message as a message/rfc822 part.\n" },
            new MessagePart("rfc822") { Message = middle },
        };
        output.Write("nested-rfc822-two-levels.eml", outer);

        // 作为附件的 .eml 文件：一个是 application/octet-stream 的普通文件，一个是 Content-Disposition: attachment 的 message/rfc822
        var forwarded = NewMessage("forwarded-original", "Original message that was forwarded as an attachment", Personas.Erin, Personas.BaseDate.AddDays(-1));
        forwarded.To.Add(Personas.Bob);
        forwarded.Body = new TextPart("plain") { Text = "This is the original message that was attached as a .eml file.\n" };
        var forwardedBytes = output.Serialize(forwarded);

        var withEml = NewMessage("attachment-eml-file", "Forwarded as attachment: a .eml file", Personas.Bob, Personas.BaseDate.AddHours(1));
        withEml.To.Add(Personas.Alice);
        var asFile = new MimePart("application", "octet-stream")
        {
            Content = new MimeContent(new MemoryStream(forwardedBytes)),
            ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
            ContentTransferEncoding = ContentEncoding.Base64,
            FileName = "forwarded-as-file.eml",
        };
        var asRfc822 = new MessagePart("rfc822")
        {
            Message = forwarded,
            ContentDisposition = new ContentDisposition(ContentDisposition.Attachment) { FileName = "forwarded-as-rfc822.eml" },
        };
        withEml.Body = new Multipart("mixed")
        {
            new TextPart("plain") { Text = "Two attachments: the same original message, once as an opaque .eml file and once as message/rfc822.\n" },
            asFile,
            asRfc822,
        };
        output.Write("attachment-eml-file.eml", withEml);
    }

    #endregion

    internal static MimeMessage NewMessage(string idName, string subject, MailboxAddress from, DateTimeOffset date)
    {
        var message = new MimeMessage();
        message.From.Add(from);
        message.Subject = subject;
        message.Date = date;
        message.MessageId = Personas.MessageId(idName);
        return message;
    }
}
