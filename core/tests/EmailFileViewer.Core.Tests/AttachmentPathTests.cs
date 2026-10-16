using EmailFileViewer.Core.Attachments;

namespace EmailFileViewer.Core.Tests;

/// <summary>
/// 任务 3.9：写入前校验最终路径在目标目录之内（规格 email-attachments 的“附件文件名的安全处理”）。
/// </summary>
public class AttachmentPathTests
{
    private static string Root => Path.Combine(Path.GetTempPath(), "efv-path-tests", "save");

    [Fact]
    [Trait("Scenario", "email-attachments / 附件文件名的安全处理 / 最终路径在目标目录之内")]
    public void CombineInsideDirectory_OrdinaryName_ReturnsPathInsideTheDirectory()
    {
        var path = AttachmentPaths.CombineInsideDirectory(Root, "report.pdf");

        Assert.Equal(Path.GetFullPath(Path.Combine(Root, "report.pdf")), path);
        Assert.True(AttachmentPaths.IsInsideDirectory(Root, path));
    }

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("..\\evil.txt")]
    [InlineData("sub/evil.txt")]
    [InlineData("sub\\evil.txt")]
    [InlineData("/etc/passwd")]
    [InlineData("\\Windows\\evil.txt")]
    [Trait("Scenario", "email-attachments / 附件文件名的安全处理 / 文件名含路径分隔符被拒绝")]
    public void CombineInsideDirectory_NameWithSeparators_Throws(string fileName)
    {
        Assert.Throws<ArgumentException>(() => AttachmentPaths.CombineInsideDirectory(Root, fileName));
    }

    [Theory]
    [InlineData("..")]
    [InlineData(".")]
    [Trait("Scenario", "email-attachments / 附件文件名的安全处理 / 名字是 .. 或 . 时最终路径不是目录里的文件")]
    public void CombineInsideDirectory_DotNames_Throw(string fileName)
    {
        Assert.Throws<ArgumentException>(() => AttachmentPaths.CombineInsideDirectory(Root, fileName));
    }

    [Fact]
    [Trait("Scenario", "email-attachments / 附件文件名的安全处理 / 经过清理的名字总能通过校验")]
    public void SanitizedNames_AlwaysPassTheCheck()
    {
        foreach (var raw in new[] { "../../.bashrc", "..\\..\\x.txt", "..", ".", "CON.txt", "a:b*c?.txt", "/abs/path.txt", "", "...", "C:\\x\\y.txt" })
        {
            var name = AttachmentFileNames.Sanitize(raw);

            var path = AttachmentPaths.CombineInsideDirectory(Root, name);

            Assert.True(AttachmentPaths.IsInsideDirectory(Root, path), raw);
        }
    }

    [Fact]
    [Trait("Scenario", "email-attachments / 附件文件名的安全处理 / 用规范化后的路径比较")]
    public void IsInsideDirectory_UsesNormalizedFullPaths()
    {
        Assert.True(AttachmentPaths.IsInsideDirectory(Root, Path.Combine(Root, "a", "..", "b.txt")));
        Assert.True(AttachmentPaths.IsInsideDirectory(Root, Path.Combine(Root, ".", "b.txt")));
        Assert.True(AttachmentPaths.IsInsideDirectory(Root, Path.Combine(Root, "sub", "b.txt")));
        Assert.False(AttachmentPaths.IsInsideDirectory(Root, Path.Combine(Root, "..", "b.txt")));
        Assert.False(AttachmentPaths.IsInsideDirectory(Root, Path.Combine(Root, "sub", "..", "..", "b.txt")));
        Assert.False(AttachmentPaths.IsInsideDirectory(Root, Path.Combine(Path.GetTempPath(), "other", "b.txt")));
    }

    [Fact]
    [Trait("Scenario", "email-attachments / 附件文件名的安全处理 / 目录本身不算目录里的文件")]
    public void IsInsideDirectory_TheDirectoryItself_IsNotInside()
    {
        Assert.False(AttachmentPaths.IsInsideDirectory(Root, Root));
        Assert.False(AttachmentPaths.IsInsideDirectory(Root, Root + Path.DirectorySeparatorChar));
        Assert.False(AttachmentPaths.IsInsideDirectory(Root, Path.Combine(Root, "sub", "..")));
    }

    [Fact]
    [Trait("Scenario", "email-attachments / 附件文件名的安全处理 / 目录路径补结尾分隔符再比较，名字相同前缀的兄弟目录不算在内")]
    public void IsInsideDirectory_SiblingDirectoryWithSamePrefix_IsNotInside()
    {
        Assert.False(AttachmentPaths.IsInsideDirectory(Root, Root + "-other" + Path.DirectorySeparatorChar + "file.txt"));
        Assert.False(AttachmentPaths.IsInsideDirectory(Root, Root + "x"));
    }

    [Fact]
    [Trait("Scenario", "email-attachments / 附件文件名的安全处理 / 目录路径带不带结尾分隔符结果一样")]
    public void IsInsideDirectory_DirectoryWithOrWithoutTrailingSeparator_GivesTheSameResult()
    {
        var file = Path.Combine(Root, "a.txt");

        Assert.True(AttachmentPaths.IsInsideDirectory(Root, file));
        Assert.True(AttachmentPaths.IsInsideDirectory(Root + Path.DirectorySeparatorChar, file));
    }

    [Fact]
    [Trait("Scenario", "email-attachments / 附件文件名的安全处理 / 比较是否区分大小写取决于平台")]
    public void IsInsideDirectory_CaseSensitivityFollowsThePlatform()
    {
        var upperCaseDirectory = Root.ToUpperInvariant();
        var file = Path.Combine(Root, "a.txt");

        var expected = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();
        Assert.Equal(expected, AttachmentPaths.IsInsideDirectory(upperCaseDirectory, file));
    }

    [Fact]
    [Trait("Scenario", "email-attachments / 附件文件名的安全处理 / 目标目录是相对路径")]
    public void IsInsideDirectory_RelativeDirectory_IsResolvedAgainstTheCurrentDirectory()
    {
        var path = AttachmentPaths.CombineInsideDirectory("relative-save-dir", "a.txt");

        Assert.True(Path.IsPathRooted(path));
        Assert.True(AttachmentPaths.IsInsideDirectory("relative-save-dir", path));
    }
}
