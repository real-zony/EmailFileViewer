namespace EmailFileViewer.Core.Models;

/// <summary>
/// 邮件地址，由显示名称和邮箱地址组成。
/// </summary>
/// <remarks>
/// 名称和地址至少有一个不为 <c>null</c>；缺少的一项为 <c>null</c>，不用空字符串占位。
/// </remarks>
/// <param name="Name">显示名称，没有时为 <c>null</c>。</param>
/// <param name="Address">邮箱地址，没有时为 <c>null</c>。</param>
public sealed record EmailAddress(string? Name, string? Address)
{
    /// <summary>
    /// 创建地址，空白的名称和地址按缺失处理。
    /// </summary>
    /// <param name="name">显示名称。与邮箱地址相同时视为没有名称。</param>
    /// <param name="address">邮箱地址。</param>
    /// <returns>名称和地址都缺失时返回 <c>null</c>。</returns>
    internal static EmailAddress? Create(string? name, string? address)
    {
        var trimmedName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        var trimmedAddress = string.IsNullOrWhiteSpace(address) ? null : address.Trim();

        if (trimmedName is not null && string.Equals(trimmedName, trimmedAddress, StringComparison.OrdinalIgnoreCase))
        {
            trimmedName = null;
        }

        if (trimmedName is null && trimmedAddress is null)
        {
            return null;
        }

        return new EmailAddress(trimmedName, trimmedAddress);
    }
}
