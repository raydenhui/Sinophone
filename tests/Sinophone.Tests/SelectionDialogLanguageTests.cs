using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Sinophone.Data;
using Sinophone.Handlers;

namespace Sinophone.Tests;

/// <summary>
/// Selection dialogs are built from Addon rows the game fills at runtime. The
/// values must be read from whatever language the client draws, not only English.
/// </summary>
public class SelectionDialogLanguageTests
{
    private const string DateTemplate =
        "<Time(IntegerParameter(1))/>" +
        "<Value>TopLevelParameter(222)</Value>/" +
        "<Value>TopLevelParameter(221)</Value>/" +
        "<Value>TopLevelParameter(219)</Value> " +
        "<Value>TopLevelParameter(218)</Value>:" +
        "<TwoDigitValue>TopLevelParameter(217)</TwoDigitValue>";

    private static readonly DateTime Now = new(2026, 11, 25);

    [Fact]
    public void TryTranslate_ResolvesAJapaneseOnlyLabel()
    {
        // Addon row 2: the Japanese client draws キャンセル, the CN row says 取消.
        var store = AddonStore((2, "キャンセル", "取消"));

        Assert.True(SelectionDialogHandler.TryTranslate(store, "キャンセル", out var chinese));
        Assert.Equal("取消", chinese);
    }

    [Fact]
    public void TryTranslate_ResolvesAnEnglishLabel()
    {
        var store = AddonStore((2, "Cancel", "取消"));

        Assert.True(SelectionDialogHandler.TryTranslate(store, "Cancel", out var chinese));
        Assert.Equal("取消", chinese);
    }

    [Fact]
    public void ResolveAddonText_FillsAJapaneseDate()
    {
        var resolved = SelectionDialogHandler.ResolveAddonText(
            DateTemplate, "2013年8月27日 8:00", now: Now);

        Assert.Equal("2013/8/27 8:00", resolved);
    }

    [Fact]
    public void ResolveAddonText_FillsAJapaneseClock()
    {
        var resolved = SelectionDialogHandler.ResolveAddonText(
            "<Time(IntegerParameter(1))/>" +
            "<Value>TopLevelParameter(218)</Value>:" +
            "<TwoDigitValue>TopLevelParameter(217)</TwoDigitValue>",
            "午後8時00分",
            now: Now);

        Assert.Equal("20:00", resolved);
    }

    [Fact]
    public void ResolveAddonText_FillsAMorningClock()
    {
        var resolved = SelectionDialogHandler.ResolveAddonText(
            "<Time(IntegerParameter(1))/>" +
            "<Value>TopLevelParameter(218)</Value>:" +
            "<TwoDigitValue>TopLevelParameter(217)</TwoDigitValue>",
            "午前8時30分",
            now: Now);

        Assert.Equal("8:30", resolved);
    }

    [Fact]
    public void ResolveAddonText_FillsAJapaneseLevel()
    {
        var resolved = SelectionDialogHandler.ResolveAddonText(
            "等级<Value>IntegerParameter(1)</Value>", "レベル90", now: Now);

        Assert.Equal("等级90", resolved);
    }

    [Fact]
    public void ResolveAddonText_FillsAJapaneseQuestName()
    {
        var resolved = SelectionDialogHandler.ResolveAddonText(
            "接受「<Highlight>StringParameter(1)</Highlight>」", "「新生の日々」を受注しますか？", now: Now);

        Assert.Equal("接受「新生の日々」", resolved);
    }

    [Fact]
    public void ResolveAddonText_FillsAnEnglishDate()
    {
        var resolved = SelectionDialogHandler.ResolveAddonText(
            DateTemplate, "8/27/2026 8:00 p.m.", now: Now);

        Assert.Equal("2026/8/27 20:00", resolved);
    }

    [Fact]
    public void ResolveAddonText_FillsADayFirstDate()
    {
        var resolved = SelectionDialogHandler.ResolveAddonText(
            DateTemplate, "27/8/2013 8:00", now: Now);

        Assert.Equal("2013/8/27 8:00", resolved);
    }

    [Fact]
    public void ResolveAddonText_FillsAnEnglishLevel()
    {
        var resolved = SelectionDialogHandler.ResolveAddonText(
            "等级<Value>IntegerParameter(1)</Value>", "Level: 90", now: Now);

        Assert.Equal("等级90", resolved);
    }

    [Fact]
    public void ResolveAddonText_FillsAnEnglishQuestName()
    {
        var resolved = SelectionDialogHandler.ResolveAddonText(
            "接受「<Highlight>StringParameter(1)</Highlight>」", "Accept “A New Day”?", now: Now);

        Assert.Equal("接受「A New Day」", resolved);
    }

    [Fact]
    public void ResolveAddonText_ReturnsEmpty_WhenADateCannotBeRead()
    {
        var resolved = SelectionDialogHandler.ResolveAddonText(
            DateTemplate, "tomorrow", now: Now);

        Assert.Equal(string.Empty, resolved);
    }

    private static TranslationStore AddonStore(params (uint RowId, string Live, string Chinese)[] rows)
    {
        var descriptor = new FakeSheetDescriptor(
            "Addon",
            "Text",
            false,
            rows.ToDictionary(row => row.RowId, row => new[] { row.Live }));

        var artifact = JsonSerializer.Serialize(new
        {
            FormatVersion = 1,
            Language = "CN",
            GeneratedAt = "test",
            GameVersion = "test",
            Sheets = new Dictionary<string, Dictionary<string, Dictionary<string, string[]>>>
            {
                ["Addon"] = rows.ToDictionary(
                    row => row.RowId.ToString(),
                    row => new Dictionary<string, string[]> { ["Text"] = [row.Chinese] }),
            },
            Scripts = new Dictionary<string, object>(),
        });

        var store = TranslationStore.Load(
            [descriptor],
            ChineseVariant.Simplified,
            false,
            _ => ToGzipStream(artifact),
            new FakePluginLog());

        Assert.NotNull(store);
        return store!;
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
