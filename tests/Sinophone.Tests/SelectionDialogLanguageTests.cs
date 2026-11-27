using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Lumina.Text.ReadOnly;
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

        Assert.True(SelectionPromptText.TryTranslate(store, "キャンセル", out var chinese));
        Assert.Equal("取消", chinese);
    }

    [Fact]
    public void TryTranslate_ResolvesAnEnglishLabel()
    {
        var store = AddonStore((2, "Cancel", "取消"));

        Assert.True(SelectionPromptText.TryTranslate(store, "Cancel", out var chinese));
        Assert.Equal("取消", chinese);
    }

    [Fact]
    public void ResolveAddonText_FillsAJapaneseDate()
    {
        var resolved = SelectionPromptText.ResolveAddonText(
            DateTemplate, "2013年8月27日 8:00", now: Now);

        Assert.Equal("2013/8/27 8:00", resolved);
    }

    [Fact]
    public void ResolveAddonText_FillsAJapaneseClock()
    {
        var resolved = SelectionPromptText.ResolveAddonText(
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
        var resolved = SelectionPromptText.ResolveAddonText(
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
        var resolved = SelectionPromptText.ResolveAddonText(
            "等级<Value>IntegerParameter(1)</Value>", "レベル90", now: Now);

        Assert.Equal("等级90", resolved);
    }

    [Fact]
    public void ResolveAddonText_FillsAJapaneseQuestName()
    {
        var resolved = SelectionPromptText.ResolveAddonText(
            "接受「<Highlight>StringParameter(1)</Highlight>」", "「新生の日々」を受注しますか？", now: Now);

        Assert.Equal("接受「新生の日々」", resolved);
    }

    [Fact]
    public void ResolveAddonText_FillsAnEnglishDate()
    {
        var resolved = SelectionPromptText.ResolveAddonText(
            DateTemplate, "8/27/2026 8:00 p.m.", now: Now);

        Assert.Equal("2026/8/27 20:00", resolved);
    }

    [Fact]
    public void ResolveAddonText_FillsADayFirstDate()
    {
        var resolved = SelectionPromptText.ResolveAddonText(
            DateTemplate, "27/8/2013 8:00", now: Now);

        Assert.Equal("2013/8/27 8:00", resolved);
    }

    [Fact]
    public void ResolveAddonText_FillsAnEnglishLevel()
    {
        var resolved = SelectionPromptText.ResolveAddonText(
            "等级<Value>IntegerParameter(1)</Value>", "Level: 90", now: Now);

        Assert.Equal("等级90", resolved);
    }

    [Fact]
    public void ResolveAddonText_FillsAnEnglishQuestName()
    {
        var resolved = SelectionPromptText.ResolveAddonText(
            "接受「<Highlight>StringParameter(1)</Highlight>」", "Accept “A New Day”?", now: Now);

        Assert.Equal("接受「A New Day」", resolved);
    }

    [Fact]
    public void ResolveAddonText_ReturnsEmpty_WhenADateCannotBeRead()
    {
        var resolved = SelectionPromptText.ResolveAddonText(
            DateTemplate, "tomorrow", now: Now);

        Assert.Equal(string.Empty, resolved);
    }

    [Fact]
    public void BuildAddonLocals_PlacesNumbersAndStringsAtTheirIndex()
    {
        var locals = SelectionPromptText.BuildAddonLocals(["5", "Wood"], [1, 2]);

        Assert.Equal(2, locals.Length);
        Assert.False(locals[0].IsString);
        Assert.Equal(5u, locals[0].UIntValue);
        Assert.True(locals[1].IsString);
        Assert.Equal("Wood", locals[1].StringValue.ExtractText());
    }

    [Fact]
    public void BuildAddonLocals_StripsThousandsSeparators()
    {
        var locals = SelectionPromptText.BuildAddonLocals(["2,000"], [1]);

        Assert.False(locals[0].IsString);
        Assert.Equal(2000u, locals[0].UIntValue);
    }

    [Fact]
    public void EvaluateAddonTemplate_ReturnsNull_WithoutAnEvaluator()
        => Assert.Null(SelectionPromptText.EvaluateAddonTemplate(null, "<num(lnum1)>", ["5"], [1]));

    [Fact]
    public void TryGetAddonText_ReturnsTheConvertedTemplate()
    {
        const uint rowId = 11194;
        var descriptor = new FakeAddonDescriptor(
            new Dictionary<uint, string> { [rowId] = "Exchange 5 for the item" },
            new Dictionary<uint, AddonParameterTemplate>
            {
                [rowId] = new("Exchange " + ScriptTextTemplate.Wildcard + " for the item", [1]),
            });

        var artifact = JsonSerializer.Serialize(new
        {
            FormatVersion = 2,
            Language = "CN",
            GeneratedAt = "test",
            GameVersion = "test",
            Sheets = new Dictionary<string, Dictionary<string, Dictionary<string, string[]>>>
            {
                ["Addon"] = new()
                {
                    [rowId.ToString()] = new() { ["Text"] = ["确定要用<Sheet(Item,IntegerParameter(1),0)/>换取吗？"] },
                },
            },
            Scripts = new Dictionary<string, object>(),
            AddonTemplates = new Dictionary<string, string> { [rowId.ToString()] = "<num(lnum1)>" },
        });

        var store = TranslationStore.Load(
            [descriptor],
            ChineseVariant.Simplified,
            false,
            _ => ToGzipStream(artifact),
            new FakePluginLog());

        Assert.NotNull(store);
        Assert.True(store.TryGetAddonText(
            "Exchange 5 for the item", out _, out var captures, out var indices, out var macro));
        Assert.Equal(["5"], captures);
        Assert.NotNull(indices);
        Assert.Equal([1], indices);
        Assert.Equal("<num(lnum1)>", macro);
    }

    private sealed class FakeAddonDescriptor(
        Dictionary<uint, string> rows,
        Dictionary<uint, AddonParameterTemplate> templates)
        : ISheetDescriptor, IParameterizedTemplateDescriptor
    {
        public string Name => "Addon";

        public string FieldName => "Text";

        public bool IsNameSheet => false;

        public bool TryExtract(uint rowId, out IReadOnlyList<IReadOnlyList<string>> texts)
        {
            if (rows.TryGetValue(rowId, out var text))
            {
                texts = [new[] { text }];
                return true;
            }

            texts = Array.Empty<IReadOnlyList<string>>();
            return false;
        }

        public bool TryBuildTemplate(uint rowId, out AddonParameterTemplate template)
            => templates.TryGetValue(rowId, out template!);
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
