using System.Text.Json.Serialization;
using EmailFileViewer.Core.Models;

namespace EmailFileViewer.Core.Json;

/// <summary>
/// <see cref="EmailDocument"/> 的 JSON 序列化上下文，使用源生成器，不依赖反射，可用于 NativeAOT。
/// </summary>
/// <remarks>
/// 属性名为 camelCase，枚举写成名称字符串（如 <c>"PlainText"</c>）。
/// </remarks>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(EmailDocument))]
public sealed partial class EmailJsonContext : JsonSerializerContext;
