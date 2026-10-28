using Sinophone.Fonts;

namespace Sinophone.Tests;

public class FontModDetectorTests
{
    private const string GlyphTable = "common/font/AXIS_18.fdt";
    private const string Atlas = "common/font/font1.tex";

    [Fact]
    public void Evaluate_FindsReplacement_WhenPathResolvesElsewhere()
    {
        var answers = new List<(string, string)>
        {
            (GlyphTable, @"C:\mods\ChnAXIS\AXIS_18.fdt"),
            (Atlas, Atlas),
        };

        Assert.Equal(FontModState.ReplacedByPenumbra, FontModDetector.Evaluate(answers));
    }

    [Fact]
    public void Evaluate_FindsNoMod_WhenEveryPathResolvesToItself()
    {
        var answers = new List<(string, string)> { (GlyphTable, GlyphTable), (Atlas, Atlas) };

        Assert.Equal(FontModState.PenumbraNoFontMod, FontModDetector.Evaluate(answers));
    }

    /// <summary>
    /// Penumbra may return the path with different casing, which still means it did
    /// not redirect it. Case must not be read as a replacement.
    /// </summary>
    [Fact]
    public void Evaluate_TreatsDifferentCaseAsNoMod()
    {
        var answers = new List<(string, string)> { (GlyphTable, "common/font/axis_18.FDT") };

        Assert.Equal(FontModState.PenumbraNoFontMod, FontModDetector.Evaluate(answers));
    }

    [Fact]
    public void Evaluate_ReportsMissing_WhenThereIsNoAnswer()
        => Assert.Equal(FontModState.PenumbraMissing, FontModDetector.Evaluate([]));

    [Fact]
    public void Describe_MentionsTheTool_ForEachState()
    {
        Assert.Contains("Penumbra mod replaces", FontModDetector.Describe(FontModState.ReplacedByPenumbra));
        Assert.Contains("no mod replaces", FontModDetector.Describe(FontModState.PenumbraNoFontMod));
        Assert.Contains("TexTools", FontModDetector.Describe(FontModState.PenumbraMissing));
    }

    [Fact]
    public void ProbePaths_CoverBothTheGlyphTableAndTheAtlas()
    {
        Assert.Contains(GlyphTable, FontModDetector.ProbePaths);
        Assert.Contains(Atlas, FontModDetector.ProbePaths);
    }
}
