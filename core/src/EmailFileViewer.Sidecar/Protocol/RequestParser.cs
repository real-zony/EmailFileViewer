using System.Text.Json;

namespace EmailFileViewer.Sidecar.Protocol;

/// <summary>
/// 解析好的请求。
/// </summary>
/// <param name="Id">请求 Id（整数或字符串），原样写进响应。</param>
/// <param name="Method">方法名。</param>
/// <param name="Params"><c>params</c>；没有或为 <c>null</c> 时为 <c>null</c>。</param>
internal sealed record ProtocolRequest(JsonElement Id, string Method, JsonElement? Params);

/// <summary>
/// 解析请求行失败的结果。
/// </summary>
/// <param name="Id">能确认的请求 Id；确认不了时为 <c>null</c>，响应里写 <c>"id": null</c>。</param>
/// <param name="Message">说明。</param>
internal sealed record RequestParseError(JsonElement? Id, string Message);

/// <summary>
/// 把一行 JSON 解析成请求。
/// </summary>
internal static class RequestParser
{
    /// <summary>
    /// 解析一行。
    /// </summary>
    /// <remarks>
    /// 只有 <c>id</c> 是整数或字符串、<c>method</c> 是非空字符串时才算有效请求；<c>params</c> 的内容留给具体的方法检查。
    /// 行不是 JSON、不是对象、<c>id</c> 缺失或类型不对时，错误响应里的 <c>id</c> 是 <c>null</c>；
    /// <c>id</c> 有效但 <c>method</c> 有问题时，错误响应带上这个 <c>id</c>。
    /// </remarks>
    /// <param name="line">一行文本。</param>
    /// <param name="request">解析成功时的请求。</param>
    /// <param name="error">解析失败时的错误。</param>
    /// <returns>有效返回 <c>true</c>。</returns>
    internal static bool TryParse(string line, out ProtocolRequest request, out RequestParseError error)
    {
        request = null!;
        error = null!;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            error = new RequestParseError(null, "这一行不是合法的 JSON。");
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = new RequestParseError(null, "请求必须是 JSON 对象。");
                return false;
            }

            if (!root.TryGetProperty("id", out var id) || !IsValidId(id))
            {
                error = new RequestParseError(null, "id 缺失，或者不是整数或字符串。");
                return false;
            }

            var clonedId = id.Clone();
            if (!root.TryGetProperty("method", out var method) || method.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(method.GetString()))
            {
                error = new RequestParseError(clonedId, "method 缺失，或者不是非空字符串。");
                return false;
            }

            JsonElement? parameters = null;
            if (root.TryGetProperty("params", out var rawParams) && rawParams.ValueKind != JsonValueKind.Null)
            {
                parameters = rawParams.Clone();
            }

            request = new ProtocolRequest(clonedId, method.GetString()!, parameters);
            return true;
        }
    }

    private static bool IsValidId(JsonElement id)
    {
        return id.ValueKind switch
        {
            JsonValueKind.String => true,
            JsonValueKind.Number => id.TryGetInt64(out _),
            _ => false,
        };
    }
}
