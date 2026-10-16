using EmailFileViewer.Core.Tests;

namespace EmailFileViewer.Sidecar.Tests;

/// <summary>
/// <c>--smoke</c> 命令在新增 <c>--stdio</c> 之后保持可用。
/// </summary>
public class SmokeCommandTests
{
    [Fact]
    [Trait("Task", "4.1")]
    public async Task Smoke_ParsesEverySampleAndSucceeds()
    {
        var samples = Path.GetDirectoryName(SampleFiles.GetPath("headers-normal.eml"))!;
        using var output = new StringWriter();
        using var error = new StringWriter();

        var code = await SmokeCommand.RunAsync(samples, output, error);

        Assert.Equal(0, code);
        Assert.Empty(error.ToString());
        Assert.Contains("smime-signed-opaque.eml\t", output.ToString());
        Assert.Contains("body-multipart-mixed-inline-images.eml\t", output.ToString());
    }

    [Fact]
    [Trait("Task", "4.1")]
    public async Task Smoke_ADirectoryThatDoesNotExist_Fails()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var code = await SmokeCommand.RunAsync(Path.Combine(Path.GetTempPath(), "efv-no-such-directory-" + Guid.NewGuid().ToString("N")), output, error);

        Assert.Equal(1, code);
    }
}
