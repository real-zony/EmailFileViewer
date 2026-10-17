using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace EmailFileViewer.Sidecar.Protocol;

/// <summary>
/// 一个已经检查过参数、可以执行的调用。
/// </summary>
/// <param name="DocumentId">调用针对的文档 Id；不针对具体文档（例如 <c>email.open</c>）时为 <c>null</c>。同一个文档的调用按到达顺序串行执行。</param>
/// <param name="RunAsync">执行调用，返回写 <c>result</c> 的函数。可以抛出 <see cref="ProtocolException"/> 表示可预料的失败。</param>
internal sealed record PreparedCall(string? DocumentId, Func<CancellationToken, Task<ResultWriter>> RunAsync);

/// <summary>
/// 一个协议方法：名称、参数和结果的类型，以及处理函数。
/// </summary>
internal abstract class MethodDescriptor
{
    /// <summary>
    /// 方法名。
    /// </summary>
    internal abstract string Name { get; }

    /// <summary>
    /// 参数的类型信息。
    /// </summary>
    internal abstract JsonTypeInfo ParamsTypeInfo { get; }

    /// <summary>
    /// 结果的类型信息。
    /// </summary>
    internal abstract JsonTypeInfo ResultTypeInfo { get; }

    /// <summary>
    /// 解析并检查 <c>params</c>。
    /// </summary>
    /// <param name="parameters"><c>params</c>；没有时为 <c>null</c>。</param>
    /// <returns>可以执行的调用。</returns>
    /// <exception cref="ProtocolException"><c>params</c> 不合法（<see cref="ErrorCodes.InvalidParams"/>）。</exception>
    internal abstract PreparedCall Prepare(JsonElement? parameters);
}

/// <summary>
/// 方法表：按名称查方法。
/// </summary>
internal sealed class MethodTable
{
    private readonly Dictionary<string, MethodDescriptor> _methods = new(StringComparer.Ordinal);

    /// <summary>
    /// 所有方法。
    /// </summary>
    internal IReadOnlyCollection<MethodDescriptor> All => _methods.Values;

    /// <summary>
    /// 注册一个方法。
    /// </summary>
    /// <typeparam name="TParams">参数类型。</typeparam>
    /// <typeparam name="TResult">结果类型。</typeparam>
    /// <param name="name">方法名。</param>
    /// <param name="paramsTypeInfo">参数的类型信息。</param>
    /// <param name="resultTypeInfo">结果的类型信息。</param>
    /// <param name="handler">处理函数。</param>
    internal void Add<TParams, TResult>(
        string name,
        JsonTypeInfo<TParams> paramsTypeInfo,
        JsonTypeInfo<TResult> resultTypeInfo,
        Func<TParams, CancellationToken, Task<TResult>> handler)
        where TParams : class, IMethodParams
    {
        _methods.Add(name, new Descriptor<TParams, TResult>(name, paramsTypeInfo, resultTypeInfo, handler));
    }

    /// <summary>
    /// 按名称查方法。
    /// </summary>
    /// <param name="name">方法名。</param>
    /// <param name="method">找到的方法。</param>
    /// <returns>找到返回 <c>true</c>。</returns>
    internal bool TryGet(string name, out MethodDescriptor method)
    {
        return _methods.TryGetValue(name, out method!);
    }

    private sealed class Descriptor<TParams, TResult> : MethodDescriptor
        where TParams : class, IMethodParams
    {
        private readonly JsonTypeInfo<TParams> _paramsTypeInfo;
        private readonly JsonTypeInfo<TResult> _resultTypeInfo;
        private readonly Func<TParams, CancellationToken, Task<TResult>> _handler;

        internal Descriptor(
            string name,
            JsonTypeInfo<TParams> paramsTypeInfo,
            JsonTypeInfo<TResult> resultTypeInfo,
            Func<TParams, CancellationToken, Task<TResult>> handler)
        {
            Name = name;
            _paramsTypeInfo = paramsTypeInfo;
            _resultTypeInfo = resultTypeInfo;
            _handler = handler;
        }

        internal override string Name { get; }

        internal override JsonTypeInfo ParamsTypeInfo => _paramsTypeInfo;

        internal override JsonTypeInfo ResultTypeInfo => _resultTypeInfo;

        internal override PreparedCall Prepare(JsonElement? parameters)
        {
            var typed = Deserialize(parameters);
            typed.Validate();

            return new PreparedCall(
                (typed as IDocumentParams)?.DocId,
                async cancellationToken =>
                {
                    var result = await _handler(typed, cancellationToken);
                    return writer => JsonSerializer.Serialize(writer, result, _resultTypeInfo);
                });
        }

        private TParams Deserialize(JsonElement? parameters)
        {
            // 没有 params 等同于空对象，让没有必填字段的方法可以省略它
            if (parameters is null)
            {
                return JsonSerializer.Deserialize("{}"u8, _paramsTypeInfo)!;
            }

            if (parameters.Value.ValueKind != JsonValueKind.Object)
            {
                throw new ProtocolException(ErrorCodes.InvalidParams, "params 必须是对象。");
            }

            try
            {
                return JsonSerializer.Deserialize(parameters.Value, _paramsTypeInfo)
                    ?? throw new ProtocolException(ErrorCodes.InvalidParams, "params 不能是 null。");
            }
            catch (JsonException ex)
            {
                throw new ProtocolException(ErrorCodes.InvalidParams, $"params 的字段类型不对：{ex.Message}", ex);
            }
        }
    }
}
