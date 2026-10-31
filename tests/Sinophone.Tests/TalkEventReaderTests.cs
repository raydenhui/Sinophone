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
    [InlineData(9, 590415, 591, "DefaultTalk", 590415u, "Text")]
    [InlineData(6, 393339, 123, "GuildleveAssignmentTalk", 123u, "Talk")]
    [InlineData(14, 917627, 123, "CraftLeveTalk", 123u, "Talk")]
    [InlineData(15, 983163, 123, "GimmickTalk", 123u, "Message")]
    [InlineData(16, 1048699, 123, "GimmickBill", 123u, "Text")]
    [InlineData(17, 1114235, 123, "GimmickTalk", 123u, "Message")]
    [InlineData(41, 2687099, 123, "GoldSaucerTalk", 123u, "Message")]
    public void TryGetRow_MapsTalkContentToSheet_WhenContentIsKnown(
        int contentId,
        uint eventId,
        ushort entryId,
        string sheet,
        uint rowId,
        string field)
    {
        var found = TalkEventSurfaces.TryGetRow(eventId, contentId, entryId, out var row);

        Assert.True(found);
        Assert.Equal(sheet, row.Sheet);
        Assert.Equal(rowId, row.RowId);
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
        Assert.False(TalkEventSurfaces.TryGetRow((uint)(contentId << 16) | 123u, contentId, 123, out _));
    }
}
