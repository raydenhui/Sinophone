using System.Text;
using Sinophone.Data;

namespace Sinophone.Tests;

public class AddonIndexTests
{
    private static readonly char Wildcard = ScriptTextTemplate.Wildcard;

    [Fact]
    public void TryGetText_ReturnsCapturesIndicesAndMacro()
    {
        var index = new AddonIndex();
        index.AddParameterized(
            new AddonParameterTemplate($"Give me {Wildcard} now.", [1]),
            "現在給我。",
            "在<Format(IntegerParameter(1),4)/>前。");

        Assert.True(index.TryGetText(
            "Give me the sword now.",
            null,
            out var chinese,
            out var captures,
            out var indices,
            out var macro));

        Assert.Equal("現在給我。", chinese);
        Assert.Equal(["the sword"], captures);
        Assert.NotNull(indices);
        Assert.Equal([1], indices);
        Assert.Equal("在<Format(IntegerParameter(1),4)/>前。", macro);
    }

    [Fact]
    public void TryGetText_MissesWhenNoTemplateMatches()
    {
        var index = new AddonIndex();
        index.AddParameterized(new AddonParameterTemplate($"Give me {Wildcard} now.", [1]), "現在給我。", null);

        Assert.False(index.TryGetText("Sell me the sword later.", null, out _, out _, out _, out _));
    }

    [Fact]
    public void TryGetText_MissesOnAnEmptyKey()
    {
        var index = new AddonIndex();
        index.AddParameterized(new AddonParameterTemplate($"Give me {Wildcard} now.", [1]), "現在給我。", null);

        Assert.False(index.TryGetText(string.Empty, null, out _, out _, out _, out _));
    }

    [Fact]
    public void AddParameterized_DropsShortTemplates()
    {
        var index = new AddonIndex();

        // "Enter ?" would match unrelated prompts such as "Enter the estate hall?".
        index.AddParameterized(new AddonParameterTemplate($"Enter {Wildcard}", [1]), "進入。", null);

        Assert.Equal(0, index.TemplateCount);
        Assert.False(index.TryGetText("Enter the estate hall", null, out _, out _, out _, out _));
    }

    [Fact]
    public void SortTemplates_PutsTheMostLiteralTemplateFirst()
    {
        var index = new AddonIndex();

        // Neither template is literal enough to be dropped, and both match the line.
        index.AddParameterized(
            new AddonParameterTemplate($"Enter the {Wildcard} hall.", [1]),
            "寬鬆",
            null);
        index.AddParameterized(
            new AddonParameterTemplate("Enter the estate hall.", [1]),
            "精確",
            null);

        index.SortTemplates();

        Assert.True(index.TryGetText("Enter the estate hall.", null, out var chinese, out _, out _, out _));
        Assert.Equal("精確", chinese);
    }

    [Fact]
    public void AddPromptVariants_MatchesTheValueTheGameFills()
    {
        var index = new AddonIndex();
        index.AddPromptVariants(
            "Duty calls. Commence battle for\n“UNKNOWN”?",
            "開始戰鬥。",
            null);

        var live = MacroNormalizer.NormalizeKey(
            "Duty calls. Commence battle for\n“Inform the Cold?”");

        Assert.True(index.TryGetText(live, null, out var chinese, out _, out _, out _));
        Assert.Equal("開始戰鬥。", chinese);
    }

    [Fact]
    public void AddPromptVariants_MatchesFirstLineOfAMultiLinePrompt()
    {
        var index = new AddonIndex();
        index.AddPromptVariants(
            "Duty calls.\nCommence battle for\n“UNKNOWN”?",
            "開始戰鬥。",
            null);

        var live = MacroNormalizer.NormalizeKey(
            "Duty calls.\nCommence battle for\n“Inform the Cold?”\n\nLevel: 90");

        Assert.True(index.TryGetText(live, null, out var chinese, out _, out _, out _));
        Assert.Equal("開始戰鬥。", chinese);
    }

    [Fact]
    public void AddPromptVariants_TreatsBlankGapsAsDynamic()
    {
        var index = new AddonIndex();
        index.AddPromptVariants("Give me  the sword.", "給我劍。", null);

        Assert.True(index.TryGetText("Give me the bright sword.", null, out var chinese, out _, out _, out _));
        Assert.Equal("給我劍。", chinese);
    }

    [Fact]
    public void HasPromptLike_AnswersWhetherTheDumpsCouldHoldThePrompt()
    {
        var index = new AddonIndex();
        index.AddParameterized(
            new AddonParameterTemplate("Commence battle for the estate.", [1]),
            "開始戰鬥。",
            null);

        Assert.True(index.HasPromptLike("Commence battle for something else", 8));
        Assert.False(index.HasPromptLike("Commen", 8));
        Assert.False(index.HasPromptLike("Return to the estate", 8));
    }

    [Fact]
    public void AddLabel_StoresTheShortLabel()
    {
        var index = new AddonIndex();
        index.AddLabel("Yes.", "是。");

        Assert.True(index.TryGetLabel("Yes.", out var chinese));
        Assert.Equal("是。", chinese);
        Assert.False(index.TryGetLabel("No.", out _));
    }

    [Fact]
    public void AddLabel_PrefersChineseOverJapanese()
    {
        var index = new AddonIndex();
        index.AddLabel("Yes.", "はい。");
        index.AddLabel("Yes.", "是。");

        Assert.True(index.TryGetLabel("Yes.", out var chinese));
        Assert.Equal("是。", chinese);
    }

    [Fact]
    public void CacheRoundTrip_RestoresLabelsAndTemplates()
    {
        var index = new AddonIndex();
        index.AddLabel("Yes.", "是。");
        index.AddParameterized(
            new AddonParameterTemplate($"Give me {Wildcard} now.", [1]),
            "現在給我。",
            "在<Format(IntegerParameter(1),4)/>前。");
        index.SortTemplates();

        var copy = RoundTrip(index);

        Assert.Equal(index.TemplateCount, copy.TemplateCount);
        Assert.True(copy.TryGetLabel("Yes.", out var label));
        Assert.Equal("是。", label);

        Assert.True(copy.TryGetText(
            "Give me the sword now.",
            null,
            out var chinese,
            out _,
            out var indices,
            out var macro));
        Assert.Equal("現在給我。", chinese);
        Assert.NotNull(indices);
        Assert.Equal([1], indices);
        Assert.Equal("在<Format(IntegerParameter(1),4)/>前。", macro);
    }

    private static AddonIndex RoundTrip(AddonIndex index)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            index.WriteTo(writer);

        stream.Position = 0;

        var copy = new AddonIndex();
        using (var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true))
            copy.ReadFrom(reader);

        return copy;
    }
}
