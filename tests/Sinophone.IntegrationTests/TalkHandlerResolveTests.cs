using Sinophone.Data;
using Sinophone.Handlers;

namespace Sinophone.IntegrationTests;

/// <summary>
/// Tests the decision TalkHandler makes for one dialogue line, wired to the real
/// fixture store. The addon event plumbing itself is covered by the in-game
/// checklist in AGENTS.md, because Dalamud does not expose its args constructors.
/// </summary>
public class TalkHandlerResolveTests
{
    private static readonly Configuration Defaults = new();

    private static (string English, string Chinese) FirstDialogue()
    {
        var row = FixtureData.Current.Rows.First(r => r.Sheet == "DefaultTalk");
        var chinese = Defaults.Variant == ChineseVariant.Traditional ? row.Tc : row.Cn;
        return (row.English, MacroNormalizer.CleanForDisplay(chinese));
    }

    [Fact]
    public void Resolve_ReturnsTranslation_ForKnownLine()
    {
        var store = FixtureStore.Load(ChineseVariant.Traditional);
        var (english, chinese) = FirstDialogue();

        var content = TalkHandler.Resolve(store, Defaults, english, null);

        Assert.NotNull(content);
        Assert.Equal(chinese, content!.Value.ChineseText);
    }

    [Fact]
    public void Resolve_ReturnsNull_ForBlankText()
    {
        var store = FixtureStore.Load(ChineseVariant.Traditional);

        Assert.Null(TalkHandler.Resolve(store, Defaults, "   ", null));
        Assert.Null(TalkHandler.Resolve(store, Defaults, null, null));
    }

    [Fact]
    public void Resolve_ReturnsNull_ForUnknownLine_WhenHidingMissingTranslations()
    {
        var store = FixtureStore.Load(ChineseVariant.Traditional);
        var config = new Configuration { HideWhenNoTranslation = true };

        var content = TalkHandler.Resolve(store, config, "This line is not in the sheets.", null);

        Assert.Null(content);
    }

    [Fact]
    public void Resolve_ReturnsEmptyTranslation_ForUnknownLine_WhenNotHiding()
    {
        var store = FixtureStore.Load(ChineseVariant.Traditional);
        var config = new Configuration { HideWhenNoTranslation = false };

        var content = TalkHandler.Resolve(store, config, "This line is not in the sheets.", null);

        Assert.NotNull(content);
        Assert.Equal(string.Empty, content!.Value.ChineseText);
    }

    [Fact]
    public void Resolve_TranslatesSpeakerName()
    {
        var store = FixtureStore.Load(ChineseVariant.Traditional);
        var row = FixtureData.Current.Rows.First(r => r.Sheet == "ENpcResident");
        var (english, _) = FirstDialogue();

        var content = TalkHandler.Resolve(store, Defaults, english, row.English);

        Assert.NotNull(content);
        Assert.Equal(MacroNormalizer.CleanForDisplay(row.Tc), content!.Value.ChineseName);
    }

    [Fact]
    public void Resolve_LeavesNameEmpty_WhenSpeakerNameIsDisabled()
    {
        var store = FixtureStore.Load(ChineseVariant.Traditional);
        var row = FixtureData.Current.Rows.First(r => r.Sheet == "ENpcResident");
        var (english, _) = FirstDialogue();
        var config = new Configuration { ShowSpeakerName = false };

        var content = TalkHandler.Resolve(store, config, english, row.English);

        Assert.NotNull(content);
        Assert.Equal(string.Empty, content!.Value.ChineseName);
    }

    [Fact]
    public void Resolve_LeavesNameEmpty_WhenNameIsUnknown()
    {
        var store = FixtureStore.Load(ChineseVariant.Traditional);
        var (english, _) = FirstDialogue();

        var content = TalkHandler.Resolve(store, Defaults, english, "Nobody in the sheets");

        Assert.NotNull(content);
        Assert.Equal(string.Empty, content!.Value.ChineseName);
    }

    [Fact]
    public void Resolve_StillReturnsTranslation_WhenNameIsUnknown()
    {
        var store = FixtureStore.Load(ChineseVariant.Traditional);
        var (english, chinese) = FirstDialogue();

        var content = TalkHandler.Resolve(store, Defaults, english, "Nobody in the sheets");

        Assert.NotNull(content);
        Assert.Equal(chinese, content!.Value.ChineseText);
    }

    [Fact]
    public void Resolve_MatchesLineWithMacros_AgainstSheetText()
    {
        var store = FixtureStore.Load(ChineseVariant.Traditional);
        var (english, chinese) = FirstDialogue();

        // The game inserts macros and line breaks the sheet does not have.
        var fromGame = english.Replace(", ", ",<br>", StringComparison.Ordinal);

        var content = TalkHandler.Resolve(store, Defaults, fromGame, null);

        Assert.NotNull(content);
        Assert.Equal(chinese, content!.Value.ChineseText);
    }

    [Fact]
    public void Resolve_WorksAcrossBothVariants()
    {
        var row = FixtureData.Current.Rows.First(r => r.Sheet == "DefaultTalk");

        foreach (var (variant, expected) in new[]
                 {
                     (ChineseVariant.Traditional, row.Tc),
                     (ChineseVariant.Simplified, row.Cn),
                 })
        {
            var store = FixtureStore.Load(variant);
            var config = new Configuration { Variant = variant };

            var content = TalkHandler.Resolve(store, config, row.English, null);

            Assert.NotNull(content);
            Assert.Equal(MacroNormalizer.CleanForDisplay(expected), content!.Value.ChineseText);
        }
    }
}
