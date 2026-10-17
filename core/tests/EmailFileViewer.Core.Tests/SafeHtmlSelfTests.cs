namespace EmailFileViewer.Core.Tests;

/// <summary>
/// 确认 <see cref="SafeHtml"/> 这个检查工具本身不会放过不安全的 HTML，免得清理器的测试因为工具太松而空转。
/// </summary>
public class SafeHtmlSelfTests
{
    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<p onclick=x()>a</p>")]
    [InlineData("<a href=\"javascript:alert(1)\">a</a>")]
    [InlineData("<img src=\"data:text/html,x\">")]
    [InlineData("<iframe></iframe>")]
    [InlineData("<object></object>")]
    [InlineData("<base href=\"https://a.example\">")]
    [InlineData("<meta http-equiv=\"refresh\" content=\"0\">")]
    [InlineData("<div style=\"background:url(javascript:x)\"></div>")]
    [InlineData("<style>a { width: expression(x) }</style>")]
    [InlineData("<p title=a title=b>x</p>")]
    [InlineData("<!-- comment --><p>x</p>")]
    public void AssertSafe_RejectsUnsafeHtml(string html)
    {
        Assert.ThrowsAny<Exception>(() => SafeHtml.AssertSafe(html));
    }

    [Fact]
    public void AssertSafe_AcceptsOrdinaryHtml()
    {
        SafeHtml.AssertSafe("<html><head><style>a { color: red }</style></head><body><p class=\"x\">hi <a href=\"https://a.example\">link</a></p><img src=\"data:image/png;base64,AAAA\"></body></html>");
    }
}
