using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EmailFileViewer.Core.Json;

/// <summary>
/// 把时间点统一输出为带 <c>Z</c> 的 UTC ISO 8601 字符串，例如 <c>2026-10-05T16:00:00Z</c>。
/// </summary>
/// <remarks>
/// System.Text.Json 默认把零偏移量写成 <c>+00:00</c>，这里改成 <c>Z</c>；读取时接受任何带偏移量的 ISO 8601。
/// </remarks>
public sealed class UtcDateTimeOffsetJsonConverter : JsonConverter<DateTimeOffset>
{
    private const string UtcFormat = "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'";

    /// <inheritdoc />
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString() ?? throw new JsonException("时间字符串不能为 null。");
        if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value))
        {
            throw new JsonException($"无法把 \"{text}\" 解析为时间。");
        }

        return value.ToUniversalTime();
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.UtcDateTime.ToString(UtcFormat, CultureInfo.InvariantCulture));
    }
}
