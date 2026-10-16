using MimeKit;

namespace EmailFileViewer.SampleGenerator;

/// <summary>
/// 样本邮件里用的虚构人物和固定值。地址全部在 example.com / example.org / example.net 下。
/// </summary>
internal static class Personas
{
    public static MailboxAddress Alice => new("Alice Zhang", "alice.zhang@example.com");

    public static MailboxAddress Bob => new("Bob Li", "bob.li@example.org");

    public static MailboxAddress Carol => new("Carol Wang", "carol.wang@example.net");

    public static MailboxAddress Dave => new("Dave Chen", "dave.chen@example.com");

    public static MailboxAddress Erin => new("Erin Xu", "erin.xu@example.org");

    /// <summary>
    /// 所有样本共用的基准日期（UTC+8），各样本在它的基础上改动。
    /// </summary>
    public static DateTimeOffset BaseDate { get; } = new(2026, 10, 1, 10, 0, 0, TimeSpan.FromHours(8));

    public static string MessageId(string name) => $"{name}@samples.efv.invalid";
}
