using Sinophone.Handlers;

namespace Sinophone.Tests;

/// <summary>
/// Covers the mapping from the game's event content id to the sheet row the Talk
/// window is showing. The unsafe reader itself is checked in game; this is the pure
/// decision it uses.
/// </summary>
public class TalkEventReaderTests
{
    [Theory]
    [InlineData(9, "DefaultTalk", "Text")]
    [InlineData(6, "GuildleveAssignmentTalk", "Talk")]
    [InlineData(14, "CraftLeveTalk", "Talk")]
    [InlineData(15, "GimmickTalk", "Message")]
    [InlineData(16, "GimmickBill", "Text")]
    [InlineData(17, "GimmickTalk", "Message")]
    [InlineData(41, "GoldSaucerTalk", "Message")]
    public void TryGetRow_MapsTalkContentToSheet_WhenContentIsKnown(int contentId, string sheet, string field)
    {
        var found = TalkEventSurfaces.TryGetRow(contentId, 123, out var row);

        Assert.True(found);
        Assert.Equal(sheet, row.Sheet);
        Assert.Equal(123u, row.RowId);
        Assert.Equal(field, row.Field);
        Assert.Equal(-1, row.Slot);
    }

    [Theory]
    [InlineData(1)]   // Quest: dialogue comes from script text keys, not a sheet row.
    [InlineData(11)]  // CustomTalk: dialogue comes from script text keys.
    [InlineData(25)]  // ContentNpc: the row holds ContentTalk links, not the line.
    [InlineData(999)]
    public void TryGetRow_ReturnsFalse_WhenContentHasNoDirectRow(int contentId)
    {
        Assert.False(TalkEventSurfaces.TryGetRow(contentId, 123, out _));
    }
}
