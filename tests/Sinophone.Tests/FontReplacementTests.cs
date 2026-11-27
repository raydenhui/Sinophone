using Sinophone.Fonts;

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
}
