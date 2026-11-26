using Sinophone.Data;
using Sinophone.Handlers;

using GameNpcYell = FFXIVClientStructs.FFXIV.Client.Game.UI.NpcYell;

namespace Sinophone.IntegrationTests;

/// <summary>
/// Tests the decision ChatBubbleHandler makes for one bubble line, wired to the real
/// fixture store. The hook and the bubble nodes need a live game, so they are covered
/// by docs/native-mode-checklist.md instead.
/// </summary>
public class ChatBubbleHandlerTests
{
    private static readonly Configuration Enabled = new() { ShowInChatBubbles = true };

    private static (string English, string Chinese) FirstDialogue()
    {
        var row = FixtureData.Current.Rows.First(r => r.Sheet == "DefaultTalk");
        var chinese = row.Tc;
        return (row.English, MacroNormalizer.CleanForDisplay(chinese));
    }

    [Fact]
    public void TargetSize_CarriesTheTextGrowth_OverToTheBackground()
    {
        // The background the game drew is the English text plus its margin: 200 = 96 + 104.
        // The Chinese needs 150, so the background needs 150 + 104.
        Assert.Equal(254, ChatBubbleHandler.BubbleLayout.TargetSize(200, 96, 150));
    }

    [Fact]
    public void TargetSize_ShrinksForAShorterLine_SoTheGrowStepKeepsTheGameSize()
    {
        // Only ever grows, so a target below the current size leaves the game's layout alone.
        Assert.Equal(164, ChatBubbleHandler.BubbleLayout.TargetSize(200, 96, 60));
    }

    [Fact]
    public void GrownSize_NeverGoesBelowTheTextPlusTheBalloonBorders()
    {
        // A background smaller than the English text means the game had not laid it out yet; the
        // difference is then too small and the caps would clip the Chinese.
        Assert.Equal(174, ChatBubbleHandler.BubbleLayout.GrownSize(100, 122, 154, 20));

        // A plain background: the carried-over change wins.
        Assert.Equal(181, ChatBubbleHandler.BubbleLayout.GrownSize(149, 122, 154, 20));

        // No offsets to read: the text itself is the floor.
        Assert.Equal(154, ChatBubbleHandler.BubbleLayout.GrownSize(100, 122, 154, 0));
    }

    [Fact]
    public void GrownSize_FollowsAShorterTranslation_InsteadOfKeepingTheEnglishWidth()
    {
        // The background already holds the English line. The change shrinks it, so empty width
        // from the English line does not stay around the shorter Chinese.
        Assert.Equal(111, ChatBubbleHandler.BubbleLayout.GrownSize(151, 124, 84, 34));
        Assert.Equal(36, ChatBubbleHandler.BubbleLayout.GrownSize(36, 21, 21, 34));
    }

    [Fact]
    public void TargetSize_LeavesTheBackgroundAlone_WhenTheEnglishSizeIsUnreadable()
    {
        // A node the game has not laid out reports no English size; guessing a margin from that
        // is what made the text spill out of both sides of the bubble.
        Assert.Equal(200, ChatBubbleHandler.BubbleLayout.TargetSize(200, 0, 150));
    }

    [Fact]
    public void Resolve_ReturnsTranslation_ForAKnownBubbleLine()
    {
        var store = FixtureStore.Load(ChineseVariant.Traditional);
        var (english, chinese) = FirstDialogue();

        var content = ChatBubbleHandler.Resolve(store, Enabled, english);

        Assert.Equal(chinese, content);
    }

    [Fact]
    public void Resolve_ReturnsNull_WhenTheSettingIsOff()
    {
        var store = FixtureStore.Load(ChineseVariant.Traditional);
        var (english, _) = FirstDialogue();

        Assert.Null(ChatBubbleHandler.Resolve(store, new Configuration(), english));
    }

    [Fact]
    public void Resolve_ReturnsNull_WhenThePluginIsDisabled()
    {
        var store = FixtureStore.Load(ChineseVariant.Traditional);
        var (english, _) = FirstDialogue();
        var configuration = new Configuration { ShowInChatBubbles = true, Enabled = false };

        Assert.Null(ChatBubbleHandler.Resolve(store, configuration, english));
    }

    [Fact]
    public void Resolve_ReturnsNull_ForAnUnknownLine()
    {
        var store = FixtureStore.Load(ChineseVariant.Traditional);

        Assert.Null(ChatBubbleHandler.Resolve(store, Enabled, "This bubble is not in the sheets."));
    }

    [Fact]
    public void Resolve_ReturnsNull_ForBlankText()
    {
        var store = FixtureStore.Load(ChineseVariant.Traditional);

        Assert.Null(ChatBubbleHandler.Resolve(store, Enabled, null));
        Assert.Null(ChatBubbleHandler.Resolve(store, Enabled, "   "));
    }

    [Fact]
    public void Resolve_UsesTheRowId_WhenTheGameExposesIt()
    {
        var store = FixtureStore.Load(ChineseVariant.Traditional);
        var row = FixtureData.Current.Rows.First(r => r.Sheet == "DefaultTalk");
        var expected = MacroNormalizer.CleanForDisplay(row.Tc);

        // The row says one line, the text says another. The id wins, which is what the
        // NpcYell row does for a yell whose English text is shared.
        var context = new DialogueContext(new DialogueRowId("DefaultTalk", row.RowId, "Text", 0));
        var content = ChatBubbleHandler.Resolve(store, Enabled, "A different line entirely.", context);

        Assert.Equal(expected, content);
    }

    [Theory]
    [InlineData(2u, true)]
    [InlineData(4u, false)]
    [InlineData(6u, true)]
    [InlineData(0u, false)]
    [InlineData(1u, false)]
    public void ShowsAsBubble_UsesTheShowBalloonFlag(uint outputType, bool expected)
        => Assert.Equal(expected, ChatBubbleHandler.ShowsAsBubble((GameNpcYell.NpcYellOutputFlags)outputType));
}
