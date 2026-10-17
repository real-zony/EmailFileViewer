using EmailFileViewer.Core.Parsing;

namespace EmailFileViewer.Core.Tests;

/// <summary>
/// 把几份 HTML 拼成一份时，取出每份文档 body 内的内容。
/// </summary>
public class HtmlBodyExtractorTests
{
    [Fact]
    public void FullDocument_ReturnsTheBodyContentAndHeadStyles()
    {
        var parts = HtmlBodyExtractor.Extract("<!DOCTYPE html><html><head><title>T</title><style>p{color:red}</style></head><body class=\"x\"><p>Hi</p></body></html>");

        Assert.Equal("<p>Hi</p>", parts.BodyContent);
        Assert.Equal("<style>p{color:red}</style>", parts.HeadStyles);
    }

    [Fact]
    public void Fragment_IsReturnedAsIs()
    {
        var parts = HtmlBodyExtractor.Extract("<div>just a fragment</div>");

        Assert.Equal("<div>just a fragment</div>", parts.BodyContent);
        Assert.Equal(string.Empty, parts.HeadStyles);
    }

    [Fact]
    public void TagNamesAreCaseInsensitive()
    {
        var parts = HtmlBodyExtractor.Extract("<HTML><HEAD><STYLE>a{}</STYLE></HEAD><BODY BGCOLOR=\"#fff\">Upper</BODY></HTML>");

        Assert.Equal("Upper", parts.BodyContent);
        Assert.Equal("<STYLE>a{}</STYLE>", parts.HeadStyles);
    }

    [Fact]
    public void GreaterThanInsideAQuotedAttribute_DoesNotEndTheBodyTag()
    {
        var parts = HtmlBodyExtractor.Extract("<html><body data-x=\"a>b\" title='c>d'>CONTENT</body></html>");

        Assert.Equal("CONTENT", parts.BodyContent);
    }

    [Fact]
    public void BodyTagInsideAComment_IsIgnored()
    {
        var parts = HtmlBodyExtractor.Extract("<html><head><!-- <body> not this --></head><body>REAL</body></html>");

        Assert.Equal("REAL", parts.BodyContent);
    }

    [Fact]
    public void EndBodyTextInsideAScript_IsNotTheEnd()
    {
        var parts = HtmlBodyExtractor.Extract("<html><body><p>A</p><script>var s = '</body>';</script><p>B</p></body></html>");

        Assert.Equal("<p>A</p><script>var s = '</body>';</script><p>B</p>", parts.BodyContent);
    }

    [Fact]
    public void MissingEndBody_TakesEverythingToTheEnd()
    {
        var parts = HtmlBodyExtractor.Extract("<html><body><p>open</p>");

        Assert.Equal("<p>open</p>", parts.BodyContent);
    }

    [Fact]
    public void ConditionalCommentsInTheHead_AreNotMistakenForContent()
    {
        var parts = HtmlBodyExtractor.Extract("<html><head><!--[if mso]><style>.m{}</style><![endif]--><style>.k{}</style></head><body>X</body></html>");

        Assert.Equal("X", parts.BodyContent);
        Assert.Equal("<style>.k{}</style>", parts.HeadStyles);
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain words, no tags at all")]
    [InlineData("a < b and c > d")]
    [InlineData("<unterminated")]
    [InlineData("<body")]
    public void OddInputs_DoNotThrowAndKeepTheText(string html)
    {
        var parts = HtmlBodyExtractor.Extract(html);

        Assert.Equal(html, parts.BodyContent);
    }
}
