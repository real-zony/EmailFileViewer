using EmailFileViewer.SampleGenerator.Formats;

namespace EmailFileViewer.SampleGenerator;

/// <summary>
/// <c>samples/assets/</c> 里的小图片，以及各个样本邮件内嵌用的二进制素材。
/// </summary>
internal static class Assets
{
    /// <summary>营销邮件里远程资源的统一前缀，文件本身在 <c>samples/assets/</c>。</summary>
    public const string RemoteBase = "https://raw.githubusercontent.com/real-zony/EmailFileViewer/main/samples/assets/";

    // 1x1 透明 GIF，追踪像素用
    public static readonly byte[] Pixel =
    [
        0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x01, 0x00, 0x01, 0x00, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00,
        0xFF, 0xFF, 0xFF, 0x21, 0xF9, 0x04, 0x01, 0x00, 0x00, 0x00, 0x00, 0x2C, 0x00, 0x00, 0x00, 0x00,
        0x01, 0x00, 0x01, 0x00, 0x00, 0x02, 0x02, 0x44, 0x01, 0x00, 0x3B,
    ];

    public static byte[] Logo() => CreateLogo();

    public static byte[] Hero() => CreateHero();

    public static byte[] Product(int number) => CreateProduct(number);

    public static byte[] Social(int number) => CreateSocial(number);

    /// <summary>
    /// 一张 180x60 的 JPEG，用来测试"打开 .jpg 附件"和远程图片。
    /// </summary>
    /// <remarks>
    /// 生成器不带 JPEG 编码器，这个字节串是用 macOS 的 <c>sips -z 60 180 -s format jpeg</c> 把 <see cref="Hero"/> 缩小转换得到的，
    /// 一次性转成 Base64 放在这里，所以每次生成的字节都相同。
    /// </remarks>
    public static byte[] Photo() => Convert.FromBase64String(PhotoJpegBase64.ReplaceLineEndings(string.Empty).Replace(" ", string.Empty));

    public static byte[] MarketingCss() => new System.Text.UTF8Encoding(false).GetBytes(
        "/* 营销邮件样本引用的远程样式表。远程内容被允许加载后，标题会变成红色。 */\n"
        + ".efv-headline { color: #d93025 !important; letter-spacing: 1px; }\n"
        + ".efv-cta { background-color: #1a73e8 !important; }\n");

    private static byte[] CreateLogo()
    {
        // 0 底色 1 白 2 橙
        var canvas = new PngCanvas(120, 40, (31, 58, 95), (255, 255, 255), (245, 166, 35));
        canvas.FillAll(0);
        canvas.FillCircle(22, 20, 15, 1);
        canvas.FillCircle(22, 20, 9, 0);
        canvas.FillCircle(22, 20, 4, 2);
        canvas.FillRect(48, 9, 60, 6, 1);
        canvas.FillRect(48, 19, 42, 6, 1);
        canvas.FillRect(48, 29, 60, 6, 1);
        return canvas.ToPng();
    }

    private static byte[] CreateHero()
    {
        // 0 天空 1 草地 2 太阳 3 云 4 深色边框
        var canvas = new PngCanvas(300, 100, (66, 133, 244), (52, 168, 83), (251, 188, 5), (255, 255, 255), (32, 33, 36));
        canvas.FillAll(0);
        canvas.FillCircle(240, 24, 16, 2);
        canvas.FillCircle(60, 26, 10, 3);
        canvas.FillCircle(74, 22, 13, 3);
        canvas.FillCircle(90, 27, 9, 3);
        canvas.FillRect(50, 27, 50, 9, 3);
        canvas.FillCircle(60, 120, 60, 1);
        canvas.FillCircle(190, 130, 70, 1);
        canvas.FillCircle(300, 115, 55, 1);
        canvas.StrokeRect(0, 0, 300, 100, 2, 4);
        return canvas.ToPng();
    }

    private static byte[] CreateProduct(int number)
    {
        (byte R, byte G, byte B) background = number switch
        {
            1 => (234, 67, 53),
            2 => (52, 168, 83),
            _ => (103, 58, 183),
        };

        // 0 底色 1 白 2 深色边框
        var canvas = new PngCanvas(90, 90, background, (255, 255, 255), (32, 33, 36));
        canvas.FillAll(0);
        switch (number)
        {
            case 1:
                canvas.FillCircle(45, 45, 24, 1);
                break;
            case 2:
                canvas.FillTriangle(45, 18, 20, 70, 70, 70, 1);
                break;
            default:
                canvas.FillRect(22, 22, 46, 46, 1);
                break;
        }

        canvas.StrokeRect(0, 0, 90, 90, 2, 2);
        return canvas.ToPng();
    }

    private static byte[] CreateSocial(int number)
    {
        var canvas = new PngCanvas(24, 24, (255, 255, 255), number == 1 ? ((byte)29, (byte)161, (byte)242) : ((byte)51, (byte)51, (byte)51), (255, 255, 255));
        canvas.FillAll(0);
        canvas.FillCircle(12, 12, 11, 1);
        if (number == 1)
        {
            canvas.FillTriangle(6, 14, 18, 8, 18, 16, 2);
        }
        else
        {
            canvas.FillRect(8, 8, 8, 8, 2);
        }

        return canvas.ToPng();
    }

    private const string PhotoJpegBase64 =
        """
        /9j/4AAQSkZJRgABAQAASABIAAD/4QBMRXhpZgAATU0AKgAAAAgAAYdpAAQAAAABAAAAGgAAAAAAA6ABAAMAAAABAAEAAKACAAQA
        AAABAAAAtKADAAQAAAABAAAAPAAAAAD/7QA4UGhvdG9zaG9wIDMuMAA4QklNBAQAAAAAAAA4QklNBCUAAAAAABDUHYzZjwCyBOmA
        CZjs+EJ+/8AAEQgAPAC0AwEiAAIRAQMRAf/EAB8AAAEFAQEBAQEBAAAAAAAAAAABAgMEBQYHCAkKC//EALUQAAIBAwMCBAMFBQQE
        AAABfQECAwAEEQUSITFBBhNRYQcicRQygZGhCCNCscEVUtHwJDNicoIJChYXGBkaJSYnKCkqNDU2Nzg5OkNERUZHSElKU1RVVldY
        WVpjZGVmZ2hpanN0dXZ3eHl6g4SFhoeIiYqSk5SVlpeYmZqio6Slpqeoqaqys7S1tre4ubrCw8TFxsfIycrS09TV1tfY2drh4uPk
        5ebn6Onq8fLz9PX29/j5+v/EAB8BAAMBAQEBAQEBAQEAAAAAAAABAgMEBQYHCAkKC//EALURAAIBAgQEAwQHBQQEAAECdwABAgMR
        BAUhMQYSQVEHYXETIjKBCBRCkaGxwQkjM1LwFWJy0QoWJDThJfEXGBkaJicoKSo1Njc4OTpDREVGR0hJSlNUVVZXWFlaY2RlZmdo
        aWpzdHV2d3h5eoKDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uLj5OXm5+jp6vLz
        9PX29/j5+v/bAEMABAQEBAQEBgQEBgkGBgYJDAkJCQkMDwwMDAwMDxIPDw8PDw8SEhISEhISEhUVFRUVFRkZGRkZHBwcHBwcHBwc
        HP/bAEMBBAUFBwcHDAcHDB0UEBQdHR0dHR0dHR0dHR0dHR0dHR0dHR0dHR0dHR0dHR0dHR0dHR0dHR0dHR0dHR0dHR0dHf/dAAQA
        DP/aAAwDAQACEQMRAD8A4Dw34b0C60CwubmwhklkhVmZlyST3Nbf/CJ+Gv8AoGwf980eFP8AkWtN/wCuC10FfuOEwmHeHpt01sui
        7eh+b4jEVVVmlN7vq+5z/wDwifhr/oGwf980f8In4a/6BsH/AHzXQVo6RpsusapaaVCwV7uVYgx5A3Hk/gOa6JYXCxi5SpxsvJf5
        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
        """;
}
