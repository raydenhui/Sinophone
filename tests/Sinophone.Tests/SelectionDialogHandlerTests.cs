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
