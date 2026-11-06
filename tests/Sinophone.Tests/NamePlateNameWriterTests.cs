using Sinophone.Handlers;

namespace Sinophone.Tests;

public class NamePlateNameWriterTests
{
    [Fact]
    public void FormatNamePlateText_UsesChineseOnly_WhenEnglishIsOff()
    {
        Assert.Equal("雅·修特拉", NamePlateNameWriter.FormatNamePlateText("雅·修特拉", "Y'shtola", false));
    }

    [Fact]
    public void FormatNamePlateText_PutsEnglishInBracketsOnTheSameLine()
    {
        Assert.Equal("雅·修特拉 (Y'shtola)", NamePlateNameWriter.FormatNamePlateText(
            "雅·修特拉", "Y'shtola", true));
    }

    [Fact]
    public void FormatNamePlateText_UsesChineseOnly_WhenEnglishNameIsEmpty()
    {
        Assert.Equal("雅·修特拉", NamePlateNameWriter.FormatNamePlateText("雅·修特拉", string.Empty, true));
    }
}
