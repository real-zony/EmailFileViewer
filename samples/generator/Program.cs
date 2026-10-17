using System.Text;
using EmailFileViewer.SampleGenerator;

// 注册后才能使用 GBK、Big5、Shift_JIS、ISO-2022-JP 等代码页编码
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

if (args.Length != 1)
{
    Console.Error.WriteLine("用法: dotnet run --project samples/generator -- <输出目录>");
    return 2;
}

var output = new SampleOutput(Path.GetFullPath(args[0]));
MimeSamples.WriteAll(output);
CharsetSamples.WriteAll(output);
SpecialSamples.WriteAll(output);
MsgSamples.WriteAll(output);
SmimeMsgSamples.WriteAll(output);
DerivedSamples.WriteAll(output);

Verification.VerifyCharsetAndHeaderSamples(output.Root);
Verification.VerifyMimeSamples(output.Root);
Verification.VerifyMsgSamples(output.Root);
SmimeMsgSamples.Verify(output.Root);
Verification.VerifyFormatMismatchAndInvalidSamples(output.Root);
Console.WriteLine($"已生成 {output.Written.Count} 个文件到 {output.Root}");
return 0;
