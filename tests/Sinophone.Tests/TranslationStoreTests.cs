using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Dalamud.Plugin.Services;
using Sinophone.Data;

namespace Sinophone.Tests;

public class TranslationStoreTests
{
    private const string SheetName = "DefaultTalk";
    private const string NameSheetName = "ENpcResident";
    private const string FieldName = "Text[0]";
    private const string NameFieldName = "Singular";

    [Fact]
    public void Load_FallsBackToEmbedded_WhenPrimaryArtifactIsBroken()
    {
        var descriptor = TalkSheet((1, "Hello there."));
        var embedded = Artifact("TC", (SheetName, FieldName, 1, "你好。"));

        var store = TranslationStore.Load(
            [descriptor],
            ChineseVariant.Traditional,
            false,
            _ => new MemoryStream(new byte[] { 1, 2, 3, 4 }),
            new FakePluginLog(),
            _ => ToGzipStream(embedded));

        Assert.NotNull(store);
        Assert.True(store.TryGetDialogue("Hello there.", out var chinese));
        Assert.Equal("你好。", chinese);
    }

    [Fact]
    public void Load_UsesPrimary_WhenItIsValid()
    {
        var descriptor = TalkSheet((1, "Hello there."));
        var downloaded = Artifact("TC", (SheetName, FieldName, 1, "下載"));
        var embedded = Artifact("TC", (SheetName, FieldName, 1, "內建"));

        var store = TranslationStore.Load(
            [descriptor],
            ChineseVariant.Traditional,
            false,
            _ => ToGzipStream(downloaded),
            new FakePluginLog(),
            _ => ToGzipStream(embedded));

        Assert.NotNull(store);
        Assert.True(store.TryGetDialogue("Hello there.", out var chinese));
        Assert.Equal("下載", chinese);
    }

    private static FakeSheetDescriptor TalkSheet(params (uint RowId, string Text)[] rows)
        => new(SheetName, FieldName, false,
            rows.ToDictionary(r => r.RowId, r => new[] { r.Text }));

    private static FakeSheetDescriptor NameSheet(params (uint RowId, string Text)[] rows)
        => new(NameSheetName, NameFieldName, true,
            rows.ToDictionary(r => r.RowId, r => new[] { r.Text }));

    private static Func<ChineseVariant, Stream?> Artifacts(string? cnJson, string? tcJson)
        => variant => variant switch
        {
            ChineseVariant.Simplified when cnJson is not null => ToGzipStream(cnJson),
            ChineseVariant.Traditional when tcJson is not null => ToGzipStream(tcJson),
            _ => null,
        };

    private static Stream ToGzipStream(string json)
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

    private static string Artifact(string language, params (string Sheet, string Field, uint RowId, string Value)[] rows)
        => Artifact(language, null, rows);

    private static string Artifact(
        string language,
        Dictionary<string, ScriptEntry>? scripts,
        params (string Sheet, string Field, uint RowId, string Value)[] rows)
    {
        var sheets = rows
            .GroupBy(r => r.Sheet)
            .ToDictionary(
                g => g.Key,
                g => g.ToDictionary(
                    r => r.RowId.ToString(),
                    r => new Dictionary<string, string[]> { [r.Field] = [r.Value] }));

        return JsonSerializer.Serialize(new
        {
            FormatVersion = 1,
            Language = language,
            GeneratedAt = "test",
            GameVersion = "test",
            Sheets = sheets,
            Scripts = scripts ?? new Dictionary<string, ScriptEntry>(),
        });
    }

    private static Dictionary<string, ScriptEntry> Scripts(params (string English, string Text)[] entries)
    {
        var result = new Dictionary<string, ScriptEntry>(StringComparer.Ordinal);

        for (var i = 0; i < entries.Length; i++)
            result[$"TEXT_TEST_{i:000}_1"] = new ScriptEntry { English = entries[i].English, Text = entries[i].Text };

        return result;
    }

    private static TranslationStore? Load(
        FakeSheetDescriptor descriptor,
        ChineseVariant variant,
        bool useFallback,
        Func<ChineseVariant, Stream?> artifacts,
        IPluginLog? log = null)
        => TranslationStore.Load([descriptor], variant, useFallback, artifacts, log ?? new FakePluginLog());

    [Fact]
    public void Load_ReturnsNull_WhenNoArtifactsExist()
    {
        var store = Load(TalkSheet(), ChineseVariant.Traditional, true, Artifacts(null, null));
        Assert.Null(store);
    }

    [Fact]
    public void Load_LogsError_WhenNoArtifactsExist()
    {
        var log = new FakePluginLog();
        Load(TalkSheet(), ChineseVariant.Traditional, true, Artifacts(null, null), log);

        Assert.True(log.Contains(Serilog.Events.LogEventLevel.Error, "No translation artifacts"));
    }

    [Fact]
    public void Load_IndexesRows_AndResolvesDialogue()
    {
        var descriptor = TalkSheet((1, "Hello there."));
        var artifacts = Artifacts(
            Artifact("CN", (SheetName, FieldName, 1, "你好。")),
            Artifact("TC", (SheetName, FieldName, 1, "你好。")));

        var store = Load(descriptor, ChineseVariant.Traditional, false, artifacts);

        Assert.NotNull(store);
        Assert.True(store.TryGetDialogue("Hello there.", out var chinese));
        Assert.Equal("你好。", chinese);
    }

    [Fact]
    public void Load_ReturnsFalse_ForUnknownText()
    {
        var descriptor = TalkSheet((1, "Hello there."));
        var artifacts = Artifacts(null, Artifact("TC", (SheetName, FieldName, 1, "你好。")));

        var store = Load(descriptor, ChineseVariant.Traditional, false, artifacts);

        Assert.NotNull(store);
        Assert.False(store.TryGetDialogue("Not in the sheet.", out _));
    }

    [Fact]
    public void Load_ReturnsFalse_ForEmptyKey()
    {
        var descriptor = TalkSheet((1, "Hello there."));
        var artifacts = Artifacts(null, Artifact("TC", (SheetName, FieldName, 1, "你好。")));

        var store = Load(descriptor, ChineseVariant.Traditional, false, artifacts);

        Assert.NotNull(store);
        Assert.False(store.TryGetDialogue("   ", out _));
    }

    [Fact]
    public void Load_MatchesText_AfterNormalization()
    {
        // The sheet has a line break; the game sends the same text with a macro.
        var descriptor = TalkSheet((1, "First line\r\nSecond line"));
        var artifacts = Artifacts(null, Artifact("TC", (SheetName, FieldName, 1, "第一行\n第二行")));

        var store = Load(descriptor, ChineseVariant.Traditional, false, artifacts);

        Assert.NotNull(store);
        Assert.True(store.TryGetDialogue("First line<br>Second line", out var chinese));
        Assert.Equal("第一行\n第二行", chinese);
    }

    [Fact]
    public void Load_IndexesNameSheet_IntoNamesLookup()
    {
        var descriptor = NameSheet((100, "Mother Miounne"));
        var artifacts = Artifacts(null, Artifact("TC", (NameSheetName, NameFieldName, 100, "繆恩")));

        var store = Load(descriptor, ChineseVariant.Traditional, false, artifacts);

        Assert.NotNull(store);
        Assert.True(store.TryGetName("Mother Miounne", out var name));
        Assert.Equal("繆恩", name);
        Assert.False(store.TryGetDialogue("Mother Miounne", out _));
    }

    [Fact]
    public void Load_DoesNotIndexNames_IntoDialogueLookup()
    {
        var descriptor = NameSheet((100, "Mother Miounne"));
        var artifacts = Artifacts(null, Artifact("TC", (NameSheetName, NameFieldName, 100, "繆恩")));

        var store = Load(descriptor, ChineseVariant.Traditional, false, artifacts);

        Assert.NotNull(store);
        Assert.Equal(0, store.TextEntryCount);
        Assert.Equal(1, store.NameEntryCount);
    }

    [Fact]
    public void Load_PrefersPrimaryVariant_WhenBothDefineTheSameKey()
    {
        var descriptor = TalkSheet((1, "Hello there."));
        var artifacts = Artifacts(
            Artifact("CN", (SheetName, FieldName, 1, "简体")),
            Artifact("TC", (SheetName, FieldName, 1, "繁體")));

        var store = Load(descriptor, ChineseVariant.Traditional, true, artifacts);

        Assert.NotNull(store);
        Assert.True(store.TryGetDialogue("Hello there.", out var chinese));
        Assert.Equal("繁體", chinese);
    }

    [Fact]
    public void Load_FallsBackToOtherVariant_ForMissingKeys()
    {
        var descriptor = TalkSheet((1, "Hello there."), (2, "Only in CN."));
        var artifacts = Artifacts(
            Artifact("CN", (SheetName, FieldName, 1, "简体一"), (SheetName, FieldName, 2, "简体二")),
            Artifact("TC", (SheetName, FieldName, 1, "繁體一")));

        var store = Load(descriptor, ChineseVariant.Traditional, true, artifacts);

        Assert.NotNull(store);
        Assert.True(store.TryGetDialogue("Only in CN.", out var chinese));
        Assert.Equal("简体二", chinese);
    }

    [Fact]
    public void Load_DoesNotFallBack_WhenDisabled()
    {
        var descriptor = TalkSheet((1, "Hello there."), (2, "Only in CN."));
        var artifacts = Artifacts(
            Artifact("CN", (SheetName, FieldName, 1, "简体一"), (SheetName, FieldName, 2, "简体二")),
            Artifact("TC", (SheetName, FieldName, 1, "繁體一")));

        var store = Load(descriptor, ChineseVariant.Traditional, false, artifacts);

        Assert.NotNull(store);
        Assert.False(store.TryGetDialogue("Only in CN.", out _));
        Assert.False(store.UsedFallback);
    }

    [Fact]
    public void Load_ReportsUsedFallback()
    {
        var descriptor = TalkSheet((1, "Hello there."));
        var artifacts = Artifacts(
            Artifact("CN", (SheetName, FieldName, 1, "简体")),
            Artifact("TC", (SheetName, FieldName, 1, "繁體")));

        var store = Load(descriptor, ChineseVariant.Traditional, true, artifacts);

        Assert.NotNull(store);
        Assert.True(store.UsedFallback);
        Assert.Equal(ChineseVariant.Traditional, store.PrimaryVariant);
    }

    [Fact]
    public void Load_WorksWithoutPrimary_WhenFallbackExists()
    {
        var descriptor = TalkSheet((1, "Hello there."));
        var artifacts = Artifacts(null, Artifact("TC", (SheetName, FieldName, 1, "繁體")));

        var store = Load(descriptor, ChineseVariant.Simplified, true, artifacts);

        Assert.NotNull(store);
        Assert.True(store.TryGetDialogue("Hello there.", out var chinese));
        Assert.Equal("繁體", chinese);
    }

    [Fact]
    public void Load_SkipsRows_ThatTheSheetDoesNotHave()
    {
        // Artifact has row 2, the live sheet does not. Nothing should be indexed.
        var descriptor = TalkSheet((1, "Hello there."));
        var artifacts = Artifacts(null, Artifact("TC",
            (SheetName, FieldName, 1, "一"),
            (SheetName, FieldName, 2, "二")));

        var store = Load(descriptor, ChineseVariant.Traditional, false, artifacts);

        Assert.NotNull(store);
        Assert.Equal(1, store.TextEntryCount);
    }

    [Fact]
    public void Load_SkipsEmptyChineseValues()
    {
        var descriptor = TalkSheet((1, "Hello there."));
        var artifacts = Artifacts(null, Artifact("TC", (SheetName, FieldName, 1, "")));

        var store = Load(descriptor, ChineseVariant.Traditional, false, artifacts);

        Assert.NotNull(store);
        Assert.Equal(0, store.TextEntryCount);
    }

    [Fact]
    public void Load_SkipsEmptyEnglishValues()
    {
        var descriptor = TalkSheet((1, ""));
        var artifacts = Artifacts(null, Artifact("TC", (SheetName, FieldName, 1, "繁體")));

        var store = Load(descriptor, ChineseVariant.Traditional, false, artifacts);

        Assert.NotNull(store);
        Assert.Equal(0, store.TextEntryCount);
    }

    [Fact]
    public void Load_SkipsSheets_ThatTheDescriptorDoesNotCover()
    {
        var descriptor = TalkSheet((1, "Hello there."));
        var artifacts = Artifacts(null, Artifact("TC", ("OtherSheet", "Text", 1, "繁體")));

        var store = Load(descriptor, ChineseVariant.Traditional, false, artifacts);

        Assert.NotNull(store);
        Assert.Equal(0, store.TextEntryCount);
    }

    [Fact]
    public void Load_LogsError_AndReturnsNull_ForCorruptArtifact()
    {
        var descriptor = TalkSheet((1, "Hello there."));
        var log = new FakePluginLog();
        Func<ChineseVariant, Stream?> artifacts = _ => ToGzipStream("not valid json {{{");

        var store = Load(descriptor, ChineseVariant.Traditional, true, artifacts, log);

        Assert.Null(store);
        Assert.True(log.Contains(Serilog.Events.LogEventLevel.Error, "Failed to load"));
    }

    [Fact]
    public void Load_LogsWarning_WhenPrimaryIsMissing()
    {
        var descriptor = TalkSheet((1, "Hello there."));
        var log = new FakePluginLog();
        var artifacts = Artifacts(null, Artifact("TC", (SheetName, FieldName, 1, "繁體")));

        Load(descriptor, ChineseVariant.Simplified, true, artifacts, log);

        Assert.True(log.Contains(Serilog.Events.LogEventLevel.Warning, "Primary artifact"));
    }

    [Fact]
    public void Load_IndexesScriptLines_WhenArtifactHasScripts()
    {
        var artifacts = Artifacts(null, Artifact("TC", Scripts(("Hello from a script.", "腳本你好。"))));

        var store = TranslationStore.Load([], ChineseVariant.Traditional, false, artifacts, new FakePluginLog());

        Assert.NotNull(store);
        Assert.True(store.TryGetDialogue("Hello from a script.", out var chinese));
        Assert.Equal("腳本你好。", chinese);
        Assert.Equal(1, store.ScriptEntryCount);
        Assert.Equal(1, store.TextEntryCount);
    }

    [Fact]
    public void Load_SkipsScriptLines_WhenEnglishOrTextIsEmpty()
    {
        var artifacts = Artifacts(
            null,
            Artifact("TC", Scripts(("", "只有中文。"), ("Only English.", ""))));

        var store = TranslationStore.Load([], ChineseVariant.Traditional, false, artifacts, new FakePluginLog());

        Assert.NotNull(store);
        Assert.False(store.TryGetDialogue("Only English.", out _));
        Assert.Equal(0, store.ScriptEntryCount);
    }

    [Fact]
    public void Load_PrefersPrimaryScript_WhenBothVariantsDefineTheSameLine()
    {
        var artifacts = Artifacts(
            Artifact("CN", Scripts(("Same script line.", "简体"))),
            Artifact("TC", Scripts(("Same script line.", "繁體"))));

        var store = TranslationStore.Load([], ChineseVariant.Traditional, true, artifacts, new FakePluginLog());

        Assert.NotNull(store);
        Assert.True(store.TryGetDialogue("Same script line.", out var chinese));
        Assert.Equal("繁體", chinese);
        Assert.Equal(1, store.ScriptEntryCount);
    }

    [Fact]
    public void Load_AddsFallbackScripts_ForLinesMissingFromPrimary()
    {
        var artifacts = Artifacts(
            Artifact("CN", Scripts(("Only in CN.", "简体"))),
            Artifact("TC", Scripts(("Only in TC.", "繁體"))));

        var store = TranslationStore.Load([], ChineseVariant.Traditional, true, artifacts, new FakePluginLog());

        Assert.NotNull(store);
        Assert.True(store.TryGetDialogue("Only in CN.", out var chinese));
        Assert.Equal("简体", chinese);
        Assert.Equal(2, store.ScriptEntryCount);
    }

    [Fact]
    public void Load_MatchesScriptText_AfterNormalization()
    {
        var artifacts = Artifacts(
            null,
            Artifact("TC", Scripts(("First line\r\nSecond line", "第一行\n第二行"))));

        var store = TranslationStore.Load([], ChineseVariant.Traditional, false, artifacts, new FakePluginLog());

        Assert.NotNull(store);
        Assert.True(store.TryGetDialogue("First line<br>Second line", out var chinese));
        Assert.Equal("第一行\n第二行", chinese);
    }

    [Fact]
    public void Load_IndexesEveryConditionalVariant()
    {
        var rows = new Dictionary<uint, IReadOnlyList<IReadOnlyList<string>>>
        {
            [1] = new IReadOnlyList<string>[] { new[] { "Sir, hello.", "Madam, hello." } },
        };

        var descriptor = new FakeSheetDescriptor(SheetName, FieldName, false, rows);
        var artifacts = Artifacts(null, Artifact("TC", (SheetName, FieldName, 1, "你好。")));

        var store = TranslationStore.Load([descriptor], ChineseVariant.Traditional, false, artifacts, new FakePluginLog());

        Assert.NotNull(store);
        Assert.True(store.TryGetDialogue("Sir, hello.", out var sir));
        Assert.True(store.TryGetDialogue("Madam, hello.", out var madam));
        Assert.Equal("你好。", sir);
        Assert.Equal("你好。", madam);
        Assert.Equal(2, store.TextEntryCount);
    }

    [Fact]
    public void Load_ResolvesConditionalDisplay_OnEachLookup()
    {
        var frame = 0;
        var descriptor = TalkSheet((1, "Hello."));
        var artifacts = Artifacts(null, Artifact("TC", (SheetName, FieldName, 1, "<If(X)>甲<Else/>乙</If>")));

        var store = TranslationStore.Load(
            [descriptor],
            ChineseVariant.Traditional,
            false,
            artifacts,
            new FakePluginLog(),
            displayResolver: text => MacroNormalizer.CleanForDisplay(text, _ => frame++ == 0));

        Assert.NotNull(store);
        Assert.True(store.TryGetDialogue("Hello.", out var first));
        Assert.True(store.TryGetDialogue("Hello.", out var second));
        Assert.Equal("甲", first);
        Assert.Equal("乙", second);
    }

    [Fact]
    public void Load_ResolvesPlainDisplay_OnceAtLoad()
    {
        var calls = 0;
        var descriptor = TalkSheet((1, "Hello."));
        var artifacts = Artifacts(null, Artifact("TC", (SheetName, FieldName, 1, "你好。")));

        var store = TranslationStore.Load(
            [descriptor],
            ChineseVariant.Traditional,
            false,
            artifacts,
            new FakePluginLog(),
            displayResolver: text =>
            {
                calls++;
                return MacroNormalizer.CleanForDisplay(text);
            });

        Assert.NotNull(store);
        Assert.Equal(1, calls);

        Assert.True(store.TryGetDialogue("Hello.", out var chinese));
        Assert.Equal("你好。", chinese);
        Assert.Equal(1, calls);
    }
}
