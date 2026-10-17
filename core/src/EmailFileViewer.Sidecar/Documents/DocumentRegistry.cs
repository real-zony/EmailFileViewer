using System.Collections.Concurrent;
using EmailFileViewer.Core;

namespace EmailFileViewer.Sidecar.Documents;

/// <summary>
/// 按 <c>docId</c> 保存已打开的文档。
/// </summary>
internal sealed class DocumentRegistry
{
    private readonly ConcurrentDictionary<string, DocumentEntry> _entries = new(StringComparer.Ordinal);
    private long _counter;

    /// <summary>
    /// 当前打开的文档个数。
    /// </summary>
    internal int Count => _entries.Count;

    /// <summary>
    /// 登记一个新打开的文档，分配 <c>docId</c>。
    /// </summary>
    /// <param name="parsed">解析结果，之后由登记表负责释放。</param>
    /// <returns>登记项。</returns>
    internal DocumentEntry Add(ParsedEmail parsed)
    {
        var entry = new DocumentEntry($"d{Interlocked.Increment(ref _counter)}", parsed);
        _entries[entry.DocId] = entry;
        return entry;
    }

    /// <summary>
    /// 按 Id 找文档。
    /// </summary>
    /// <param name="docId">文档 Id。</param>
    /// <returns>登记项；不存在（没打开过或已经关闭）时为 <c>null</c>。</returns>
    internal DocumentEntry? Find(string docId)
    {
        return _entries.TryGetValue(docId, out var entry) ? entry : null;
    }

    /// <summary>
    /// 从登记表里移除。之后再用这个 Id 访问都是“文档不存在”。
    /// </summary>
    /// <param name="docId">文档 Id。</param>
    internal void Remove(string docId)
    {
        _entries.TryRemove(docId, out _);
    }

    /// <summary>
    /// 关闭所有还没关闭的文档。只在所有请求都处理完之后调用。
    /// </summary>
    internal void CloseAll()
    {
        foreach (var entry in _entries.Values)
        {
            entry.Close();
        }

        _entries.Clear();
    }
}
