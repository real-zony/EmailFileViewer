using System.Text;
using EmailFileViewer.Core.Attachments;

namespace EmailFileViewer.Core.Tests;

/// <summary>
/// 任务 3.9：附件文件名清理、重名处理、可执行类型识别（规格 email-attachments 的“附件文件名的安全处理”“保存全部附件”“打开可执行附件”）。
/// </summary>
public class AttachmentFileNameTests
{
    [Theory]
    [InlineData("../../.bashrc", ".bashrc")]
    [InlineData("..\\..\\x.txt", "x.txt")]
    [InlineData("..\\..\\Windows\\evil.txt", "evil.txt")]
    [InlineData("/tmp/absolute-path.txt", "absolute-path.txt")]
    [InlineData("C:\\Users\\victim\\Desktop\\a.txt", "a.txt")]
    [InlineData("dir/sub\\mixed/name.pdf", "name.pdf")]
    [InlineData("report.pdf", "report.pdf")]
    [InlineData("年度报告 (终稿).docx", "年度报告 (终稿).docx")]
    [Trait("Scenario", "email-attachments / 附件文件名的安全处理 / 文件名包含路径穿越")]
    public void Sanitize_RemovesPathComponents(string input, string expected)
    {
        Assert.Equal(expected, AttachmentFileNames.Sanitize(input));
    }

    [Theory]
    [InlineData("a:b*c?.txt", "a_b_c_.txt")]
    [InlineData("x<y>z|w.txt", "x_y_z_w.txt")]
    [InlineData("say \"hi\".txt", "say _hi_.txt")]
    [InlineData("stream.txt:evil.exe", "stream.txt_evil.exe")]
    [Trait("Scenario", "email-attachments / 附件文件名的安全处理 / 文件名包含非法字符")]
    public void Sanitize_ReplacesInvalidCharactersWithUnderscore(string input, string expected)
    {
        Assert.Equal(expected, AttachmentFileNames.Sanitize(input));
    }

    [Theory]
    [InlineData("a\u0000b.txt", "ab.txt")]
    [InlineData("a\tb\r\nc.txt", "abc.txt")]
    [InlineData("a\u007Fb.txt", "ab.txt")]
    [InlineData("a\u0085b.txt", "ab.txt")]
    [InlineData("invoice\u202Egpj.exe", "invoicegpj.exe")]
    [InlineData("\u2066hidden\u2069.txt", "hidden.txt")]
    [Trait("Scenario", "email-attachments / 附件文件名的安全处理 / 控制字符和反转显示顺序的控制符")]
    public void Sanitize_RemovesControlAndBidiCharacters(string input, string expected)
    {
        Assert.Equal(expected, AttachmentFileNames.Sanitize(input));
    }

    [Theory]
    [InlineData("name.", "name")]
    [InlineData("name. . .", "name")]
    [InlineData("name ", "name")]
    [InlineData("  name.txt  ", "name.txt")]
    [InlineData("name.txt.", "name.txt")]
    [InlineData("..", "attachment")]
    [InlineData(".", "attachment")]
    [InlineData("...", "attachment")]
    [InlineData("", "attachment")]
    [InlineData("   ", "attachment")]
    [InlineData("a/b/", "attachment")]
    [InlineData("\u0000\u0001", "attachment")]
    [Trait("Scenario", "email-attachments / 附件文件名的安全处理 / 结尾的点和空格、空名字")]
    public void Sanitize_TrimsTrailingDotsAndSpaces_AndFallsBackWhenEmpty(string input, string expected)
    {
        Assert.Equal(expected, AttachmentFileNames.Sanitize(input));
    }

    [Fact]
    [Trait("Scenario", "email-attachments / 附件文件名的安全处理 / 没有文件名")]
    public void Sanitize_Null_FallsBack()
    {
        Assert.Equal("attachment", AttachmentFileNames.Sanitize(null));
    }

    [Theory]
    [InlineData("CON", "_CON")]
    [InlineData("con", "_con")]
    [InlineData("CON.txt", "_CON.txt")]
    [InlineData("PRN.log", "_PRN.log")]
    [InlineData("aux.tar.gz", "_aux.tar.gz")]
    [InlineData("NUL", "_NUL")]
    [InlineData("nul.", "_nul")]
    [InlineData("COM1", "_COM1")]
    [InlineData("com1.log", "_com1.log")]
    [InlineData("COM9.txt", "_COM9.txt")]
    [InlineData("LPT1.txt", "_LPT1.txt")]
    [InlineData("lpt9", "_lpt9")]
    [InlineData("CON .txt", "_CON .txt")]
    [InlineData("COM\u00B9.txt", "_COM\u00B9.txt")]
    [InlineData("CONIN$", "_CONIN$")]
    [Trait("Scenario", "email-attachments / 附件文件名的安全处理 / 文件名是 Windows 保留名")]
    public void Sanitize_PrefixesWindowsReservedNames(string input, string expected)
    {
        Assert.Equal(expected, AttachmentFileNames.Sanitize(input));
    }

    [Theory]
    [InlineData("CONSOLE.txt")]
    [InlineData("COM10.txt")]
    [InlineData("COM.txt")]
    [InlineData("LPT.txt")]
    [InlineData("nullable.txt")]
    [InlineData("my.CON.txt")]
    [InlineData("a.con")]
    [Trait("Scenario", "email-attachments / 附件文件名的安全处理 / 只是名字里含有保留名的不改动")]
    public void Sanitize_LeavesNamesThatOnlyResembleReservedNames(string input)
    {
        Assert.Equal(input, AttachmentFileNames.Sanitize(input));
    }

    [Fact]
    [Trait("Scenario", "email-attachments / 附件文件名的安全处理 / 超长文件名")]
    public void Sanitize_LongAsciiName_IsTruncatedToLimitKeepingExtension()
    {
        var name = Sanitized(new string('a', 400) + ".pdf");

        Assert.Equal(255, Encoding.UTF8.GetByteCount(name));
        Assert.EndsWith(".pdf", name);
        Assert.StartsWith("aaaa", name);
    }

    [Fact]
    [Trait("Scenario", "email-attachments / 附件文件名的安全处理 / 超长的中文文件名按 UTF-8 字节数截断，不拆开字符")]
    public void Sanitize_LongChineseName_IsTruncatedByBytesWithoutSplittingCharacters()
    {
        var name = Sanitized(string.Concat(Enumerable.Repeat("报告", 200)) + ".docx");

        Assert.True(Encoding.UTF8.GetByteCount(name) <= 255);
        Assert.EndsWith(".docx", name);

        // 能无损地往返，说明没有留下被截断的字符
        Assert.Equal(name, Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(name)));
        Assert.DoesNotContain('\uFFFD', name);
    }

    [Fact]
    [Trait("Scenario", "email-attachments / 附件文件名的安全处理 / 超长名字里的代理对不被截断")]
    public void Sanitize_LongNameWithSurrogatePairs_KeepsPairsIntact()
    {
        var name = Sanitized(string.Concat(Enumerable.Repeat("𠮷", 200)) + ".txt");

        Assert.True(Encoding.UTF8.GetByteCount(name) <= 255);
        Assert.EndsWith(".txt", name);
        Assert.Equal(name, Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(name)));
        Assert.DoesNotContain('\uFFFD', name);
    }

    [Fact]
    [Trait("Scenario", "email-attachments / 附件文件名的安全处理 / 超长扩展名不再当扩展名保留，整体截断")]
    public void Sanitize_LongExtension_IsTruncatedAsAWhole()
    {
        var name = Sanitized("a." + new string('b', 400));

        Assert.True(Encoding.UTF8.GetByteCount(name) <= 255);
        Assert.StartsWith("a.bbb", name);
    }

    [Fact]
    [Trait("Scenario", "email-attachments / 附件文件名的安全处理 / 刚好 255 字节的名字不改动")]
    public void Sanitize_NameOfExactlyLimit_IsKept()
    {
        var input = new string('a', 251) + ".txt";

        Assert.Equal(input, AttachmentFileNames.Sanitize(input));
    }

    [Fact]
    [Trait("Scenario", "email-attachments / 附件文件名的安全处理 / 样本库里的每个不安全文件名清理后都能安全写入")]
    public async Task Sanitize_EveryUnsafeSampleName_ProducesAFileInsideTheTargetDirectory()
    {
        using var parsed = await MailAssert.ParseSampleAsync("attachments-unsafe-names.eml");
        using var temp = new TempDirectory();

        foreach (var attachment in parsed.Document.Attachments)
        {
            var name = AttachmentFileNames.Sanitize(attachment.FileName);
            var path = AttachmentPaths.CombineInsideDirectory(temp.DirectoryPath, name);

            Assert.True(AttachmentPaths.IsInsideDirectory(temp.DirectoryPath, path), attachment.FileName);
            Assert.Equal(Path.GetFullPath(temp.DirectoryPath), Path.GetDirectoryName(path));
        }

        Assert.Equal(
            [".bashrc", "evil.txt", "absolute-path.txt", "_CON.txt", "_NUL", "a_b_c_.txt", "x_y_z_w.txt"],
            parsed.Document.Attachments.Select(a => AttachmentFileNames.Sanitize(a.FileName)));
    }

    [Fact]
    [Trait("Scenario", "email-attachments / 保存全部附件 / 目标文件夹已有同名文件")]
    public void GetUniqueName_NoConflict_ReturnsTheSameName()
    {
        Assert.Equal("report.pdf", AttachmentFileNames.GetUniqueName("report.pdf", _ => false));
    }

    [Fact]
    [Trait("Scenario", "email-attachments / 保存全部附件 / 目标文件夹已有同名文件")]
    public void GetUniqueName_Conflicts_NumbersTheName()
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "report.pdf", "Report (1).pdf" };

        var first = AttachmentFileNames.GetUniqueName("report.pdf", used.Contains);
        used.Add(first);
        var second = AttachmentFileNames.GetUniqueName("report.pdf", used.Contains);
        used.Add(second);
        var third = AttachmentFileNames.GetUniqueName("report.pdf", used.Contains);

        Assert.Equal("report (2).pdf", first);
        Assert.Equal("report (3).pdf", second);
        Assert.Equal("report (4).pdf", third);
    }

    [Theory]
    [InlineData("archive.tar.gz", "archive.tar (1).gz")]
    [InlineData("noextension", "noextension (1)")]
    [InlineData(".bashrc", ".bashrc (1)")]
    [InlineData("a.b.c.txt", "a.b.c (1).txt")]
    [Trait("Scenario", "email-attachments / 保存全部附件 / 编号插在最后一个扩展名之前")]
    public void GetUniqueName_PutsTheNumberBeforeTheLastExtension(string name, string expected)
    {
        Assert.Equal(expected, AttachmentFileNames.GetUniqueName(name, existing => existing == name));
    }

    [Fact]
    [Trait("Scenario", "email-attachments / 保存全部附件 / 编号后仍不超过长度上限")]
    public void GetUniqueName_NearTheLengthLimit_ShortensTheStem()
    {
        var name = new string('a', 251) + ".txt";

        var unique = AttachmentFileNames.GetUniqueName(name, existing => existing == name);

        Assert.True(Encoding.UTF8.GetByteCount(unique) <= 255);
        Assert.EndsWith(" (1).txt", unique);
    }

    [Fact]
    [Trait("Scenario", "email-attachments / 保存全部附件 / 两个同名的 report.pdf 不互相覆盖")]
    public async Task DuplicateAttachmentsFromSample_GetDistinctNames()
    {
        using var parsed = await MailAssert.ParseSampleAsync("attachments-duplicate-names.eml");
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var names = parsed.Document.Attachments
            .Select(attachment => AttachmentFileNames.GetUniqueName(AttachmentFileNames.Sanitize(attachment.FileName), used.Contains))
            .Select(name =>
            {
                used.Add(name);
                return name;
            })
            .ToList();

        Assert.Equal(["report.pdf", "report (1).pdf"], names);
    }

    [Fact]
    [Trait("Scenario", "email-attachments / 保存全部附件 / 编号次数用尽")]
    public void GetUniqueName_EverythingTaken_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => AttachmentFileNames.GetUniqueName("a.txt", _ => true));
    }

    [Theory]
    [InlineData("setup.exe")]
    [InlineData("run.bat")]
    [InlineData("run.cmd")]
    [InlineData("game.com")]
    [InlineData("installer.msi")]
    [InlineData("script.ps1")]
    [InlineData("script.vbs")]
    [InlineData("script.js")]
    [InlineData("script.jse")]
    [InlineData("job.wsf")]
    [InlineData("screen.scr")]
    [InlineData("shortcut.lnk")]
    [InlineData("page.hta")]
    [InlineData("panel.cpl")]
    [Trait("Scenario", "email-attachments / 用系统默认程序打开附件 / 打开可执行附件（Windows）")]
    public void IsExecutable_WindowsExtensions(string fileName)
    {
        Assert.True(AttachmentFileNames.IsExecutable(fileName));
    }

    [Theory]
    [InlineData("Tool.app")]
    [InlineData("launcher.command")]
    [InlineData("installer.pkg")]
    [Trait("Scenario", "email-attachments / 用系统默认程序打开附件 / 打开可执行附件（macOS）")]
    public void IsExecutable_MacOsExtensions(string fileName)
    {
        Assert.True(AttachmentFileNames.IsExecutable(fileName));
    }

    [Theory]
    [InlineData("install.sh")]
    [InlineData("app.desktop")]
    [InlineData("Tool.AppImage")]
    [InlineData("installer.run")]
    [Trait("Scenario", "email-attachments / 用系统默认程序打开附件 / 打开可执行附件（Linux）")]
    public void IsExecutable_LinuxExtensions(string fileName)
    {
        Assert.True(AttachmentFileNames.IsExecutable(fileName));
    }

    [Theory]
    [InlineData("tool.jar")]
    [InlineData("script.vbe")]
    [InlineData("script.ws")]
    [InlineData("script.wsh")]
    [InlineData("module.psm1")]
    [InlineData("manifest.psd1")]
    [InlineData("format.ps1xml")]
    [InlineData("settings.reg")]
    [InlineData("console.msc")]
    [Trait("Scenario", "email-attachments / 用系统默认程序打开附件 / 打开可执行附件（其他脚本和配置）")]
    public void IsExecutable_OtherScriptExtensions(string fileName)
    {
        Assert.True(AttachmentFileNames.IsExecutable(fileName));
    }

    [Theory]
    [InlineData("folder.scf")]
    [InlineData("site.url")]
    [InlineData("program.pif")]
    [Trait("Scenario", "email-attachments / 用系统默认程序打开附件 / 打开可执行附件（快捷方式）")]
    public void IsExecutable_ShortcutExtensions(string fileName)
    {
        Assert.True(AttachmentFileNames.IsExecutable(fileName));
    }

    [Theory]
    [InlineData("app.msix")]
    [InlineData("app.msixbundle")]
    [InlineData("app.appx")]
    [InlineData("app.appxbundle")]
    [InlineData("app.appinstaller")]
    [InlineData("app.application")]
    [Trait("Scenario", "email-attachments / 用系统默认程序打开附件 / 打开可执行附件（安装包）")]
    public void IsExecutable_PackageExtensions(string fileName)
    {
        Assert.True(AttachmentFileNames.IsExecutable(fileName));
    }

    [Theory]
    [InlineData("addin.xll")]
    [InlineData("disc.iso")]
    [InlineData("disk.img")]
    [InlineData("disk.vhd")]
    [InlineData("disk.vhdx")]
    [Trait("Scenario", "email-attachments / 用系统默认程序打开附件 / 打开可执行附件（Excel 加载项和可挂载的磁盘映像）")]
    public void IsExecutable_AddInAndDiskImageExtensions(string fileName)
    {
        Assert.True(AttachmentFileNames.IsExecutable(fileName));
    }

    [Theory]
    [InlineData("SETUP.EXE")]
    [InlineData("Setup.Exe")]
    [InlineData("INSTALL.SH")]
    [InlineData("tool.APP")]
    [InlineData("TOOL.JAR")]
    [InlineData("Disc.Iso")]
    [InlineData("App.MsixBundle")]
    [Trait("Scenario", "email-attachments / 用系统默认程序打开附件 / 扩展名不区分大小写")]
    public void IsExecutable_IsCaseInsensitive(string fileName)
    {
        Assert.True(AttachmentFileNames.IsExecutable(fileName));
    }

    [Theory]
    [InlineData("invoice.pdf.exe")]
    [InlineData("photo.jpg.scr")]
    [InlineData("archive.tar.sh")]
    [InlineData("report.docx.js")]
    [InlineData("holiday.jpg.url")]
    [InlineData("invoice.pdf.iso")]
    [InlineData("evil.exe.")]
    [InlineData("evil.exe ")]
    [InlineData("..\\..\\evil.exe")]
    [InlineData("/tmp/run.sh")]
    [InlineData(".exe")]
    [InlineData("invoice\u202Egpj.exe")]
    [Trait("Scenario", "email-attachments / 用系统默认程序打开附件 / 双扩展名、结尾的点和空格、带路径、反转控制符")]
    public void IsExecutable_LooksAtTheLastExtensionOfTheCleanedName(string fileName)
    {
        Assert.True(AttachmentFileNames.IsExecutable(fileName));
    }

    [Theory]
    [InlineData("report.pdf")]
    [InlineData("photo.jpg")]
    [InlineData("notes.txt")]
    [InlineData("archive.zip")]
    [InlineData("setup.exe.txt")]
    [InlineData("evil.exe.pdf")]
    [InlineData("exe")]
    [InlineData("run")]
    [InlineData("sh")]
    [InlineData("noextension")]
    [InlineData("bash.shx")]
    [InlineData("x.jsx")]
    [InlineData("tool.jar.txt")]
    [InlineData("disc.iso.pdf")]
    [InlineData("settings.regx")]
    [InlineData("notes.urls")]
    [InlineData("app.msixx")]
    [InlineData("iso")]
    [InlineData("")]
    [Trait("Scenario", "email-attachments / 用系统默认程序打开附件 / 打开图片附件和其他普通文件")]
    public void IsExecutable_OrdinaryFiles_AreNot(string fileName)
    {
        Assert.False(AttachmentFileNames.IsExecutable(fileName));
    }

    [Fact]
    [Trait("Scenario", "email-attachments / 用系统默认程序打开附件 / null 名字不是可执行文件")]
    public void IsExecutable_Null_IsNot()
    {
        Assert.False(AttachmentFileNames.IsExecutable(null));
    }

    [Fact]
    [Trait("Scenario", "email-attachments / 用系统默认程序打开附件 / 样本库里的可执行附件全部被识别，普通附件不被误判")]
    public async Task IsExecutable_Samples()
    {
        using var executables = await MailAssert.ParseSampleAsync("attachments-executables.eml");
        Assert.All(executables.Document.Attachments, attachment => Assert.True(AttachmentFileNames.IsExecutable(attachment.FileName), attachment.FileName));

        using var ordinary = await MailAssert.ParseSampleAsync("attachments-pdf-xlsx-jpg.eml");
        Assert.All(ordinary.Document.Attachments, attachment => Assert.False(AttachmentFileNames.IsExecutable(attachment.FileName), attachment.FileName));
    }

    private static string Sanitized(string input)
    {
        var name = AttachmentFileNames.Sanitize(input);
        Assert.NotEmpty(name);
        return name;
    }
}
