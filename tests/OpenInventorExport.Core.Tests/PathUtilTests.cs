using OpenInventorExport.Core;
using Xunit;

namespace OpenInventorExport.Core.Tests;

public class PathUtilTests
{
    [Theory]
    [InlineData(@"C:\x\Motor.ipt<No Cover>", @"C:\x\Motor.ipt")]
    [InlineData(@"C:\x\Plain.ipt", @"C:\x\Plain.ipt")]
    public void StripMemberSuffix_RemovesOnlyTheSuffix(string input, string expected) =>
        Assert.Equal(expected, PathUtil.StripMemberSuffix(input));

    [Fact]
    public void DrawingBaseName_IgnoresFolderExtensionAndSuffix() =>
        Assert.Equal("Motor", PathUtil.DrawingBaseName(@"C:\x\Motor.ipt<No Cover>"));
}