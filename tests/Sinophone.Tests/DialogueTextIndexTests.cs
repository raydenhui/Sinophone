using System.Text;
using Sinophone.Data;

namespace Sinophone.Tests;

public class DialogueTextIndexTests
{
    [Fact]
    public void AddSheetText_CountsTheKeyButNoScriptEntry()
    {
        var index = new DialogueTextIndex();

        Assert.Equal(KanaTextAdd.Added, index.AddSheetText("Hello.", "你好。"));

        Assert.Equal(1, index.Count);
        Assert.Equal(0, index.ScriptEntryCount);
        Assert.True(index.TryGetExact("Hello.", out var stored));
        Assert.Equal("你好。", stored);
    }

    [Fact]
    public void AddScriptText_CountsTheScriptEntry()
    {
        var index = new DialogueTextIndex();

        Assert.True(index.AddScriptText("Goodbye.", "再見。", out var sheetOwnsOtherText));

        Assert.False(sheetOwnsOtherText);
        Assert.Equal(1, index.Count);
        Assert.Equal(1, index.ScriptEntryCount);
    }

    [Fact]
    public void AddScriptText_KeepsTheSheetText_WhenTheScriptLineDiffers()
    {
        var index = new DialogueTextIndex();
        index.AddSheetText("Yes.", "是。");

        Assert.False(index.AddScriptText("Yes.", "對。", out var sheetOwnsOtherText));

        Assert.True(sheetOwnsOtherText);
        Assert.True(index.TryGetExact("Yes.", out var stored));
        Assert.Equal("是。", stored);
        Assert.Equal(0, index.ScriptEntryCount);
    }

    [Fact]
    public void AddScriptText_DoesNotClaimTheSheet_WhenBothSayTheSameThing()
    {
        var index = new DialogueTextIndex();
        index.AddSheetText("Yes.", "是。");

        Assert.False(index.AddScriptText("Yes.", "是。", out var sheetOwnsOtherText));

        Assert.False(sheetOwnsOtherText);
    }

    [Fact]
    public void AddScriptText_DoesNotReplaceAKeyTheOtherVariantOwns()
    {
        var index = new DialogueTextIndex();
        index.AddScriptText("Goodbye.", "再見。", out _);

        Assert.False(index.AddScriptText("Goodbye.", "再會。", out var sheetOwnsOtherText));

        Assert.False(sheetOwnsOtherText);
        Assert.Equal(1, index.ScriptEntryCount);
        Assert.True(index.TryGetExact("Goodbye.", out var stored));
        Assert.Equal("再見。", stored);
    }

    [Fact]
    public void RemoveScriptText_DropsTheLineAndItsCount()
    {
        var index = new DialogueTextIndex();
        index.AddScriptText("Goodbye.", "再見。", out _);

        Assert.True(index.RemoveScriptText("Goodbye."));
        Assert.False(index.RemoveScriptText("Goodbye."));
        Assert.Equal(0, index.Count);
        Assert.Equal(0, index.ScriptEntryCount);
    }

    [Fact]
    public void AddBranchNeutral_AnswersTheOtherGenderWording()
    {
        var index = new DialogueTextIndex();
        index.AddScriptText("One could reduce his body to aether.", "甲", out _);

        index.AddBranchNeutral("One could reduce his body to aether.", "甲");

        Assert.True(index.TryGetBranchNeutral("One could reduce her body to aether.", out var stored));
        Assert.Equal("甲", stored);
    }

    [Fact]
    public void AddBranchNeutral_DoesNothing_WhenTheKeyHasNoBranchWord()
    {
        var index = new DialogueTextIndex();
        index.AddScriptText("The moon is bright.", "月亮很亮。", out _);

        index.AddBranchNeutral("The moon is bright.", "月亮很亮。");

        Assert.Equal(0, index.BranchNeutralCount);
        Assert.False(index.TryGetBranchNeutral("The moon is bright.", out _));
    }

    [Fact]
    public void BranchNeutral_IsDropped_WhenTwoLinesCollapseToIt()
    {
        var index = new DialogueTextIndex();
        index.AddScriptText("Greetin's, lad.", "甲", out _);
        index.AddBranchNeutral("Greetin's, lad.", "甲");

        index.AddBranchNeutral("Greetin's, lass.", "乙");

        Assert.Equal(1, index.BranchNeutralCount);
        Assert.Equal(1, index.AmbiguousBranchKeyCount);
        Assert.False(index.TryGetBranchNeutral("Greetin's, lad.", out _));
    }

    [Fact]
    public void BranchNeutral_IsDropped_WhenItsLineBecomesAmbiguous()
    {
        var index = new DialogueTextIndex();
        index.AddScriptText("One could reduce his body to aether.", "甲", out _);
        index.AddBranchNeutral("One could reduce his body to aether.", "甲");

        index.DropBranchNeutral("One could reduce his body to aether.", "甲");

        Assert.False(index.TryGetBranchNeutral("One could reduce her body to aether.", out _));
    }

    [Fact]
    public void Aliases_AreStoredForTheExactLine()
    {
        var index = new DialogueTextIndex();
        index.AddAlias("Everyone, listen!", "All", "所有人");

        Assert.True(index.TryGetAlias("Everyone, listen!", out var alias));
        Assert.Equal("All", alias.English);
        Assert.Equal("所有人", alias.Chinese);
        Assert.False(index.TryGetAlias("Other line", out _));
    }

    [Fact]
    public void SheetEntries_AreSortedAndHoldOnlySheetRows()
    {
        var index = new DialogueTextIndex();
        index.AddSheetText("Beta.", "乙。");
        index.AddSheetText("Alpha.", "甲。");
        index.AddScriptText("Gamma.", "丙。", out _);

        var entries = index.SheetEntries();

        Assert.Equal(["Alpha.", "Beta."], entries.Select(entry => entry.English));
        Assert.Equal("甲。", entries[0].Chinese);
    }

    [Fact]
    public void CacheRoundTrip_RestoresEveryCollection()
    {
        var index = new DialogueTextIndex();
        index.AddSheetText("Hello.", "你好。");
        index.AddScriptText("One could reduce his body to aether.", "甲", out _);
        index.AddBranchNeutral("One could reduce his body to aether.", "甲");
        index.AddScriptText("Goodbye.", "再見。", out _);
        index.RemoveScriptText("Goodbye.");
        index.AddAlias("Everyone, listen!", "All", "所有人");

        var copy = RoundTrip(index);

        Assert.Equal(index.Count, copy.Count);
        Assert.Equal(index.ScriptEntryCount, copy.ScriptEntryCount);

        Assert.True(copy.TryGetExact("Hello.", out var hello));
        Assert.Equal("你好。", hello);
        Assert.True(copy.TryGetBranchNeutral("One could reduce her body to aether.", out var neutral));
        Assert.Equal("甲", neutral);
        Assert.True(copy.TryGetAlias("Everyone, listen!", out var alias));
        Assert.Equal("所有人", alias.Chinese);

        // The sheet rows survive too, so a cached store can still sample them.
        Assert.Equal(["Hello."], copy.SheetEntries().Select(entry => entry.English));
    }

    private static DialogueTextIndex RoundTrip(DialogueTextIndex index)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            index.WriteTo(writer);

        stream.Position = 0;

        var copy = new DialogueTextIndex();
        using (var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true))
            copy.ReadFrom(reader);

        return copy;
    }
}
