using System.Text;
using Sinophone.Fonts;
using FileMode = FFXIVClientStructs.FFXIV.Client.System.File.FileMode;

namespace Sinophone.Tests;

public class FontReplacementTests
{
    [Theory]
    [InlineData("common/font/axis_12.fdt", "C:/mods/axis_12.fdt", true)]
    [InlineData("common/font/axis_12.fdt", "COMMON/FONT/AXIS_12.FDT", false)]
    [InlineData("common/font/axis_12.fdt", "", false)]
    [InlineData("common/font/axis_12.fdt", null, false)]
    public void IsReplacedPath_OnlyCountsADifferentFile(
        string gamePath, string? resolved, bool expected)
        => Assert.Equal(expected, FontReplacementService.IsReplacedPath(gamePath, resolved));

    [Fact]
    public unsafe void WriteUnpackedPath_WritesDescriptorAndCallerInterfaceBuffer()
    {
        const string localPath = @"C:\fonts\axis_12.fdt";
        var descriptor = new FontReplacementService.SeFileDescriptor
        {
            FileMode = FileMode.LoadSqPackResource,
        };
        var utf16 = Encoding.Unicode.GetBytes(localPath);
        var interfaceSize = 0x20 + utf16.Length + 0x16;
        var interfaceBuffer = stackalloc byte[interfaceSize];

        FontReplacementService.WriteUnpackedPath(&descriptor, localPath, utf16, interfaceBuffer, interfaceSize);

        Assert.Equal(FileMode.LoadUnpackedResource, descriptor.FileMode);
        Assert.Equal((nint)interfaceBuffer, (nint)descriptor.FileInterface);
        Assert.Equal(localPath, new string(descriptor.FilePath, 0, localPath.Length));
        Assert.Equal('\0', descriptor.FilePath[localPath.Length]);

        var writtenInterfacePath = Encoding.Unicode.GetString(
            new ReadOnlySpan<byte>(interfaceBuffer + 0x21, utf16.Length));
        Assert.Equal(localPath, writtenInterfacePath);
        Assert.Equal(0, interfaceBuffer[0x21 + utf16.Length]);
        Assert.Equal(0, interfaceBuffer[0x21 + utf16.Length + 1]);
    }
}
