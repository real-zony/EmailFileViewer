using System.Buffers;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace EmailFileViewer.Sidecar.Protocol;

/// <summary>
/// 往响应里写 <c>result</c> 的函数。
/// </summary>
/// <param name="writer">已经写好 <c>"result":</c> 属性名的写入器。</param>
internal delegate void ResultWriter(Utf8JsonWriter writer);

/// <summary>
/// 把响应拼成一行字节：<c>{"id":…,"result":…}\n</c> 或 <c>{"id":…,"error":{"code":…,"message":…}}\n</c>。
/// </summary>
/// <remarks>
/// 用宽松的转义（<see cref="JavaScriptEncoder.UnsafeRelaxedJsonEscaping"/>），中文原样输出，不会变成 <c>\uXXXX</c>。
/// 协议里的 JSON 不会嵌进 HTML，所以不需要转义 <c>&lt;</c>、<c>&gt;</c>、<c>&amp;</c>；控制字符和换行仍然会被转义，一条消息永远只占一行。
/// </remarks>
internal static class ResponseWriter
{
    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>
    /// 拼成功响应。
    /// </summary>
    /// <param name="id">请求 Id。</param>
    /// <param name="writeResult">写 <c>result</c> 的函数。</param>
    /// <returns>一行响应，含结尾的换行。</returns>
    internal static byte[] Success(JsonElement id, ResultWriter writeResult)
    {
        return Build(id, writer =>
        {
            writer.WritePropertyName("result");
            writeResult(writer);
        });
    }

    /// <summary>
    /// 拼错误响应。
    /// </summary>
    /// <param name="id">请求 Id；确认不了时为 <c>null</c>。</param>
    /// <param name="code">错误码。</param>
    /// <param name="message">说明。</param>
    /// <returns>一行响应，含结尾的换行。</returns>
    internal static byte[] Error(JsonElement? id, string code, string message)
    {
        return Build(id, writer =>
        {
            writer.WriteStartObject("error");
            writer.WriteString("code", code);
            writer.WriteString("message", message);
            writer.WriteEndObject();
        });
    }

    private static byte[] Build(JsonElement? id, Action<Utf8JsonWriter> writeBody)
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("id");
            if (id is { } value)
            {
                value.WriteTo(writer);
            }
            else
            {
                writer.WriteNullValue();
            }

            writeBody(writer);
            writer.WriteEndObject();
        }

        var newLine = buffer.GetSpan(1);
        newLine[0] = (byte)'\n';
        buffer.Advance(1);
        return buffer.WrittenSpan.ToArray();
    }
}
