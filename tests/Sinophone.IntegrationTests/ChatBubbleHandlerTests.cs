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
    public void SelectPadding_UsesTheBalloonBorders_WhenTheyAreSet()
    {
        var (padding, source) = ChatBubbleHandler.BubbleLayout.SelectPadding(76, 15, 116, 63);

        Assert.Equal((76, 15), padding);
        Assert.Contains("borders", source);
    }

    [Fact]
    public void SelectPadding_KeepsTheMeasuredMargin_WhenTheBordersAreLarger()
    {
        // The real balloon: the game's margin is 76 by 15, while the texture borders are 76 by 46.
        // Using the borders alone added 31px of height and pushed the tail inside the bubble.
        var (padding, source) = ChatBubbleHandler.BubbleLayout.SelectPadding(76, 46, 76, 15);

        Assert.Equal((76, 15), padding);
        Assert.Contains("measured", source);
    }

    [Fact]
    public void SelectPadding_UsesTheBorders_WhenTheBackgroundHasNotBeenLaidOut()
    {
        // A stale background reports no margin at all; the borders are the only bound left.
        var (padding, _) = ChatBubbleHandler.BubbleLayout.SelectPadding(76, 46, 0, 0);

        Assert.Equal((76, 46), padding);
    }

    [Fact]
    public void SelectPadding_FallsBackToTheMeasuredBackground_WhenTheBordersAreUnset()
    {
        var (padding, source) = ChatBubbleHandler.BubbleLayout.SelectPadding(0, 0, 116, 63);

        Assert.Equal((116, 63), padding);
        Assert.Contains("measured", source);
    }

    [Fact]
    public void SelectPadding_NeverReturnsANegativePadding()
    {
        var (padding, _) = ChatBubbleHandler.BubbleLayout.SelectPadding(0, 0, -40, -2);

        Assert.Equal((0, 0), padding);
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
