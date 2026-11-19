using Sinophone.Handlers;

namespace Sinophone.Tests;

/// <summary>
/// The history behind the chat log restore: a message the game builds from a translation is matched
/// back to the English line the plugin laid out.
/// </summary>
public class ChatLogHistoryTests
{
    [Fact]
    public void TryGetEnglish_ReturnsTheEnglishLine_ForARememberedTranslation()
    {
        var history = new ChatLogHistory();
        history.Remember("來吧，讓我們開始。", "Then let us begin.", 4);

        Assert.True(history.TryGetEnglish("來吧，讓我們開始。", out var english));
        Assert.Equal("Then let us begin.", english);
    }

    [Fact]
    public void TryGetEnglish_TrimsTheMessage()
    {
        var history = new ChatLogHistory();
        history.Remember("你好。", "Hello.", 4);

        Assert.True(history.TryGetEnglish("  你好。 ", out var english));
        Assert.Equal("Hello.", english);
    }

    [Fact]
    public void TryGetEnglish_DoesNotMatchAnUnknownLine()
    {
        var history = new ChatLogHistory();
        history.Remember("你好。", "Hello.", 4);

        Assert.False(history.TryGetEnglish("再見。", out _));
    }

    [Fact]
    public void TryGetEnglish_DoesNotMatchAnEmptyMessage()
    {
        var history = new ChatLogHistory();
        history.Remember("你好。", "Hello.", 4);

        Assert.False(history.TryGetEnglish(null, out _));
        Assert.False(history.TryGetEnglish("   ", out _));
    }

    [Fact]
    public void Remember_IgnoresPairsThatWouldNotChangeAnything()
    {
        var history = new ChatLogHistory();
        history.Remember("", "Hello.", 4);
        history.Remember("你好。", "", 4);
        history.Remember("Same.", "Same.", 4);

        Assert.Equal(0, history.Count);
    }

    [Fact]
    public void Remember_KeepsOnlyTheMostRecentLines()
    {
        var history = new ChatLogHistory();
        history.Remember("第一句。", "First.", 2);
        history.Remember("第二句。", "Second.", 2);
        history.Remember("第三句。", "Third.", 2);

        Assert.Equal(2, history.Count);
        Assert.False(history.TryGetEnglish("第一句。", out _));
        Assert.True(history.TryGetEnglish("第三句。", out var english));
        Assert.Equal("Third.", english);
    }

    [Fact]
    public void Remember_KeepsOneSlot_ForARepeatedLine()
    {
        var history = new ChatLogHistory();
        history.Remember("你好。", "Hello.", 4);

        for (var i = 0; i < 6; i++)
            history.Remember("你好。", "Hello.", 4);

        Assert.Equal(1, history.Count);
        Assert.True(history.TryGetEnglish("你好。", out var english));
        Assert.Equal("Hello.", english);
    }

    [Fact]
    public void Remember_KeepsTheSpeakerName_WhenManyLinesAreRemembered()
    {
        // A conversation writes its whole dialogue to the Story tab at once, so the window has to
        // hold a dialogue, not just the last line.
        var history = new ChatLogHistory();
        history.Remember("歌鳴威 (Singingway)", "Singingway", 64);

        for (var i = 0; i < 40; i++)
            history.Remember($"第{i}句。", $"Line {i}.", 64);

        Assert.True(history.TryGetEnglish("歌鳴威 (Singingway)", out var name));
        Assert.Equal("Singingway", name);
        Assert.True(history.TryGetEnglish("第39句。", out _));
    }

    [Fact]
    public void FindReplacements_RestoresBothTheLineAndTheSpeakerName()
    {
        var history = new ChatLogHistory();
        history.Remember("這棵「樹」看起來真不錯！", "That looks like the “trees”!", 16);
        history.Remember("歌鳴威 (Singingway)", "Singingway", 16);

        history.FindReplacements(
            "這棵「樹」看起來真不錯！",
            "歌鳴威 (Singingway)",
            out var message,
            out var sender);

        Assert.Equal("That looks like the “trees”!", message);
        Assert.Equal("Singingway", sender);
    }

    [Fact]
    public void FindReplacements_FindsTheName_WhenOnlyTheNameWasTranslated()
    {
        var history = new ChatLogHistory();
        history.Remember("歌鳴威 (Singingway)", "Singingway", 16);

        history.FindReplacements("An English line.", "歌鳴威 (Singingway)", out var message, out var sender);

        Assert.Null(message);
        Assert.Equal("Singingway", sender);
    }

    [Fact]
    public void FindReplacements_FindsTheLine_WhenOnlyTheLineWasTranslated()
    {
        var history = new ChatLogHistory();
        history.Remember("你好。", "Hello.", 16);

        history.FindReplacements("你好。", "Singingway", out var message, out var sender);

        Assert.Equal("Hello.", message);
        Assert.Null(sender);
    }

    [Fact]
    public void FindReplacements_FindsNothing_ForAnUntranslatedMessage()
    {
        var history = new ChatLogHistory();
        history.Remember("你好。", "Hello.", 16);

        history.FindReplacements("Goodbye.", "Singingway", out var message, out var sender);

        Assert.Null(message);
        Assert.Null(sender);
    }

    [Fact]
    public void TryGetEnglish_UsesTheMostRecentPair_WhenTheChineseRepeats()
    {
        var history = new ChatLogHistory();
        history.Remember("你好。", "Hello.", 4);
        history.Remember("你好。", "Hi there.", 4);

        Assert.True(history.TryGetEnglish("你好。", out var english));
        Assert.Equal("Hi there.", english);
    }
}
