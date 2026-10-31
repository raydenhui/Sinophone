using System.IO.Compression;
using System.Text;
using Sinophone.Data;
using Sinophone.Handlers;

namespace Sinophone.Tests;

public class SelectionDialogHandlerTests
{
    [Fact]
    public void TryTranslate_UsesDialogueLookup_WhenTextMatches()
    {
        var store = LoadStore("""
            {"FormatVersion":1,"Language":"TC","GeneratedAt":"test","GameVersion":"test",
             "Sheets":{"Addon":{"3":{"Text":["是"]},"4":{"Text":["否"]}}}}
            """);

        Assert.True(SelectionDialogHandler.TryTranslate(store, "Yes.", out var chinese));
        Assert.Equal("是", chinese);
    }

    [Fact]
    public void TryTranslate_TriesShortLabelWithPeriod_WhenButtonOmitsIt()
    {
        var store = LoadStore("""
            {"FormatVersion":1,"Language":"TC","GeneratedAt":"test","GameVersion":"test",
             "Sheets":{"Addon":{"3":{"Text":["是"]},"4":{"Text":["否"]}}}}
            """);

        Assert.True(SelectionDialogHandler.TryTranslate(store, "Yes", out var chinese));
        Assert.Equal("是", chinese);
    }

    [Fact]
    public void TryTranslate_TrimsTrailingPunctuation_WhenStoredTextHasNone()
    {
        var store = LoadStore("""
            {"FormatVersion":1,"Language":"TC","GeneratedAt":"test","GameVersion":"test",
             "Sheets":{"Addon":{"1":{"Text":["確定"]}}}}
            """);

        Assert.True(SelectionDialogHandler.TryTranslate(store, "OK.", out var chinese));
        Assert.Equal("確定", chinese);
    }

    [Fact]
    public void TryTranslate_LeavesUnknownTextAlone()
    {
        var store = LoadStore("""
            {"FormatVersion":1,"Language":"TC","GeneratedAt":"test","GameVersion":"test",
             "Sheets":{"Addon":{"3":{"Text":["是"]}}}}
            """);

        Assert.False(SelectionDialogHandler.TryTranslate(store, "A line that is not in the sheet.", out var chinese));
        Assert.Empty(chinese);
    }

    [Fact]
    public void ResolveAddonText_FillsQuestNameAndLevels()
    {
        const string chinese =
            "要开始“<Highlight>StringParameter(1)</Highlight>”的战斗吗？\n\n" +
            "※此任务是等级同步任务，\n\u3000超过上限时会自动调整为：\n" +
            "\u3000\uE0BC等级：<Sheet(QuestBattle,IntegerParameter(2),3)/>" +
            "<If(GreaterThan(IntegerParameter(3),0))>\n" +
            "\u3000\uE0BD品级：<Value>IntegerParameter(3)</Value><Else/></If>";
        const string live =
            "Duty calls. Commence battle for\n“Inform the Cold?”\n\n" +
            "**※Level sync is in effect.** If you are above the maximum allowed level, " +
            "it will be synced as follows:\n\n\uE0BC Level: 90\n\uE0BD Item level: 665**";

        var resolved = SelectionDialogHandler.ResolveAddonText(chinese, live);

        Assert.Contains("Inform the Cold?", resolved);
        Assert.Contains("等级：90", resolved);
        Assert.Contains("品级：665", resolved);
        Assert.DoesNotContain("StringParameter", resolved);
        Assert.DoesNotContain("IntegerParameter", resolved);
    }

    [Fact]
    public void ResolveAddonText_FillsItemNamesAcrossLineBreaks()
    {
        const string chinese =
            "确定要将<Sheet(Item,IntegerParameter(2),0)/>种植在<Sheet(Item,IntegerParameter(1),0)/>中吗？";
        const string live =
            "Prepare the bed with a bag of grade 3 Thanalan '+n+'topsoil and a bag of voidrake seeds?";

        var resolved = SelectionDialogHandler.ResolveAddonText(chinese, live);

        Assert.Contains("bag of voidrake seeds", resolved);
        Assert.Contains("bag of grade 3 Thanalan", resolved);
        Assert.Contains("topsoil", resolved);
        Assert.DoesNotContain("Sheet(Item", resolved);
        Assert.DoesNotContain("a bag", resolved);
        Assert.DoesNotContain("\n", resolved);
    }

    private static TranslationStore LoadStore(string json)
    {
        var descriptor = new FakeSheetDescriptor("Addon", "Text", false,
            new Dictionary<uint, string[]>
            {
                [1] = ["OK"],
                [3] = ["Yes."],
                [4] = ["No."],
            });

        var store = TranslationStore.Load(
            [descriptor],
            ChineseVariant.Traditional,
            false,
            _ => ToGzipStream(json),
            new FakePluginLog());

        Assert.NotNull(store);
        return store;
    }

    private static MemoryStream ToGzipStream(string json)
    {
        var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionMode.Compress, leaveOpen: true))
        using (var writer = new StreamWriter(gzip, new UTF8Encoding(false)))
        {
            writer.Write(json);
        }

        output.Position = 0;
        return output;
    }
}
