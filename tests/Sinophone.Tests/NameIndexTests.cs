using System.Text;
using Sinophone.Data;

namespace Sinophone.Tests;

public class NameIndexTests
{
    [Fact]
    public void Match_IgnoresCase()
    {
        var index = new NameIndex();
        index.AddText("skywatcher", "眺望者");

        Assert.Equal(NameMatch.Exact, index.Match("Skywatcher", out var name, out _));
        Assert.Equal("眺望者", name);
    }

    [Fact]
    public void Match_TrimsTrailingPunctuation()
    {
        var index = new NameIndex();
        index.AddText("Pattin", "帕汀");

        Assert.Equal(NameMatch.Trimmed, index.Match("Pattin?", out var name, out var matchedKey));
        Assert.Equal("帕汀", name);
        Assert.Equal("Pattin", matchedKey);
    }

    [Fact]
    public void Match_DoesNotTrimWhenTheTrimmedKeyIsEmpty()
    {
        var index = new NameIndex();
        index.AddText("Pattin", "帕汀");

        Assert.Equal(NameMatch.None, index.Match("?!", out _, out _));
    }

    [Fact]
    public void Match_ReportsTheKeyItUsed()
    {
        var index = new NameIndex();
        index.AddText("Pattin", "帕汀");

        Assert.Equal(NameMatch.Exact, index.Match("Pattin", out _, out var matchedKey));
        Assert.Equal("Pattin", matchedKey);
    }

    [Fact]
    public void AddIfMissing_KeepsTheExistingName()
    {
        var index = new NameIndex();
        index.AddText("All", "所有人");
        index.AddIfMissing("All", "大家");

        Assert.Equal(NameMatch.Exact, index.Match("All", out var name, out _));
        Assert.Equal("所有人", name);
    }

    [Fact]
    public void AddText_PrefersChineseOverJapanese()
    {
        var index = new NameIndex();
        index.AddText("Stella", "ステラ");
        index.AddText("Stella", "斯特拉");

        Assert.Equal(NameMatch.Exact, index.Match("Stella", out var name, out _));
        Assert.Equal("斯特拉", name);
    }

    [Fact]
    public void Count_CountsNamesOnlyOnce()
    {
        var index = new NameIndex();
        index.AddText("Stella", "斯特拉");
        index.AddText("Stella", "ステラ");
        index.AddRow("BNpcName", 1, "斯特拉");

        Assert.Equal(1, index.Count);
    }

    [Fact]
    public void TryGetRow_AnswersTheSheetRow()
    {
        var index = new NameIndex();
        index.AddRow("BNpcName", 10416, "斯特拉");

        Assert.True(index.TryGetRow("BNpcName", 10416, out var name));
        Assert.Equal("斯特拉", name);
        Assert.False(index.TryGetRow("BNpcName", 10417, out _));
    }

    [Fact]
    public void CacheRoundTrip_RestoresBothLookups()
    {
        var index = new NameIndex();
        index.AddText("Skywatcher", "眺望者");
        index.AddIfMissing("All", "所有人");
        index.AddRow("BNpcName", 10416, "斯特拉");

        var copy = RoundTrip(index);

        Assert.Equal(index.Count, copy.Count);
        Assert.Equal(NameMatch.Exact, copy.Match("Skywatcher", out var name, out _));
        Assert.Equal("眺望者", name);
        Assert.True(copy.TryGetRow("BNpcName", 10416, out var rowName));
        Assert.Equal("斯特拉", rowName);
    }

    private static NameIndex RoundTrip(NameIndex index)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            index.WriteTo(writer);

        stream.Position = 0;

        var copy = new NameIndex();
        using (var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true))
            copy.ReadFrom(reader);

        return copy;
    }
}
