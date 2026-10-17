using System.Text.Json.Serialization;
using EmailFileViewer.Core.Models;

namespace EmailFileViewer.Sidecar.Protocol;

/// <summary>
/// 协议 DTO 的 JSON 序列化上下文，用源生成器，不依赖反射，NativeAOT 下可用。
/// </summary>
/// <remarks>
/// 属性名是 camelCase，枚举写成名称字符串。文档 <see cref="EmailDocument"/> 在这里重新生成元数据，
/// 这样响应用同一个写入器（宽松转义，中文不会变成 <c>\uXXXX</c>）一次写完。
/// </remarks>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(OpenParams))]
[JsonSerializable(typeof(OpenNestedParams))]
[JsonSerializable(typeof(CloseParams))]
[JsonSerializable(typeof(SaveParams))]
[JsonSerializable(typeof(SaveAllParams))]
[JsonSerializable(typeof(ExtractParams))]
[JsonSerializable(typeof(NoParams))]
[JsonSerializable(typeof(PingResult))]
[JsonSerializable(typeof(OpenResult))]
[JsonSerializable(typeof(EmptyResult))]
[JsonSerializable(typeof(PathResult))]
[JsonSerializable(typeof(SaveAllResult))]
internal sealed partial class ProtocolJsonContext : JsonSerializerContext;
