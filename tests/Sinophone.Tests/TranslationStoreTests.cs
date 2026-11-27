using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Dalamud.Plugin.Services;
using Sinophone.Data;
using Sinophone.Diagnostics;
using Sinophone.Handlers;

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

    [Fact]
    public void Lookup_Logs_WhenTheDialogueGroupIsOn()
    {
        var (store, log) = LoadWithLog(new Configuration());

        Assert.True(store!.TryGetDialogue("Hello there.", Context(), out _));
        Assert.Contains(log.Entries, e => e.Message.Contains("Dialogue matched", StringComparison.Ordinal));
    }

    [Fact]
    public void Lookup_AnswersALineThatWritesBothPlayerNamePlaceholders()
    {
        // The dump writes "Player Player" for the full name and "Player" for the given name, and
        // one line can hold both.
        var scripts = ScriptsWithKeys(
            ("TEXT_TEST_000_1", "Player Player is Player of the Scions.", "Player Player是Player的拂曉血盟成員。"));
        var artifacts = Artifacts(null, Artifact("TC", scripts));

        var store = TranslationStore.Load(
            [],
            ChineseVariant.Traditional,
            false,
            artifacts,
            new FakePluginLog(),
            playerName: () => "Y'shtola Rhul");

        Assert.NotNull(store);
        Assert.True(store.TryGetDialogue(
            "Y'shtola Rhul is Y'shtola of the Scions.", out var chinese));
        Assert.Equal("Player Player是Player的拂曉血盟成員。", chinese);
    }

    [Fact]
    public void Lookup_DoesNotRepeatACandidate_WhenTheFallbackVariantOwnsTheSameKey()
    {
        // The fallback artifact carries the same dump keys with its own Chinese. They are the same
        // lines, not a second scene, so the candidate list must not grow a phantom entry: with one,
        // the speaker step sees two lines and drops a line that only one scene has.
        var tc = Artifact("TC", ScriptsWithKeys(
            ("TEXT_VOICEMAN_06005_004360_ALISAIE", "Mother!?", "母親！？"),
            ("TEXT_BANANA001_03036_ALPA_000_067", "Mother!?", "媽媽！？")));
        var cn = Artifact("CN", ScriptsWithKeys(
            ("TEXT_VOICEMAN_06005_004360_ALISAIE", "Mother!?", "母亲！？"),
            ("TEXT_BANANA001_03036_ALPA_000_067", "Mother!?", "妈妈！？")));

        var store = TranslationStore.Load(
            [], ChineseVariant.Traditional, true, Artifacts(cn, tc), new FakePluginLog());

        Assert.NotNull(store);
        Assert.False(store.TryGetDialogue("Mother!?", out _));
        Assert.True(store.TryGetDialogue("Mother!?", default, "Alisaie", out var chinese));
        Assert.Equal("母親！？", chinese);
    }

    [Fact]
    public void Lookup_NamesTheCandidateKeys_WhenAnAmbiguousLineIsNotAnswered()
    {
        var artifacts = Artifacts(null, Artifact("TC", DuplicateScripts()));
        var log = new FakePluginLog();
        var store = TranslationStore.Load([], ChineseVariant.Traditional, false, artifacts, log);

        Assert.False(store!.TryGetDialogue("You bastard!", default, "Y'shtola", out _));

        var message = Assert.Single(log.Entries, e => e.Message.Contains("none was chosen", StringComparison.Ordinal)).Message;
        Assert.Contains("TEXT_VOICEMAN_05004_004230_THANCRED", message);
        Assert.Contains("TEXT_JOBPLD650_02573_ALDIS_000_052", message);
    }

    [Fact]
    public void Lookup_DoesNotLog_WhenTheDialogueGroupIsOff()
    {
        var (store, log) = LoadWithLog(new Configuration { LogDialogue = false });

        Assert.True(store!.TryGetDialogue("Hello there.", Context(), out _));
        Assert.DoesNotContain(log.Entries, e => e.Message.Contains("Dialogue matched", StringComparison.Ordinal));
    }

    private static DialogueContext Context()
        => new(new DialogueRowId(SheetName, 1, FieldName));

    private static (TranslationStore? Store, FakePluginLog Log) LoadWithLog(Configuration configuration)
    {
        var descriptor = TalkSheet((1, "Hello there."));
        var artifact = Artifact("TC", (SheetName, FieldName, 1, "你好。"));
        var log = new FakePluginLog();

        var store = TranslationStore.Load(
            [descriptor],
            ChineseVariant.Traditional,
            false,
            _ => ToGzipStream(artifact),
            log,
            configuration: configuration);

        return (store, log);
    }

    /// <summary>
    /// Where <c>ReadCacheBody</c> starts: the 8-byte magic, a 4-byte format version, the
    /// key as a 7-bit-encoded length and its bytes, then the variant, the fallback flag,
    /// and the script entry count. Tests corrupt a saved cache from this offset, so it
    /// has to match the writer.
    /// </summary>
    private static int CacheBodyOffset(byte[] file)
    {
        const int magicAndVersion = 12;

        var length = 0;
        var shift = 0;
        var i = magicAndVersion;

        for (; ; i++)
        {
            length |= (file[i] & 0x7F) << shift;

            if ((file[i] & 0x80) == 0)
                break;

            shift += 7;
        }

        var afterKey = i + 1 + length;

        // primaryVariant (int32), usedFallback (bool), scriptEntryCount (int32).
        return afterKey + 4 + 1 + 4;
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

    private static string ArtifactWithSlots(
        string language,
        params (string Sheet, string Field, uint RowId, string[] Values)[] rows)
    {
        var sheets = rows
            .GroupBy(r => r.Sheet)
            .ToDictionary(
                g => g.Key,
                g => g.ToDictionary(
                    r => r.RowId.ToString(),
                    r => new Dictionary<string, string[]> { [r.Field] = r.Values }));

        return JsonSerializer.Serialize(new
        {
            FormatVersion = 1,
            Language = language,
            GeneratedAt = "test",
            GameVersion = "test",
            Sheets = sheets,
            Scripts = new Dictionary<string, ScriptEntry>(),
        });
    }

    private static Dictionary<string, ScriptEntry> Scripts(params (string English, string Text)[] entries)
    {
        var result = new Dictionary<string, ScriptEntry>(StringComparer.Ordinal);

        for (var i = 0; i < entries.Length; i++)
            result[$"TEXT_TEST_{i:000}_1"] = new ScriptEntry { English = entries[i].English, Text = entries[i].Text };

        return result;
    }

    private static Dictionary<string, ScriptEntry> ScriptsWithKeys(
        params (string Key, string English, string Text)[] entries)
    {
        var result = new Dictionary<string, ScriptEntry>(StringComparer.Ordinal);

        foreach (var (key, english, text) in entries)
            result[key] = new ScriptEntry { English = english, Text = text };

        return result;
    }

    private static Dictionary<string, ScriptEntry> ScriptsWithRows(
        params (string Key, string Sheet, uint RowId, string English, string Text)[] entries)
    {
        var result = new Dictionary<string, ScriptEntry>(StringComparer.Ordinal);

        foreach (var (key, sheet, rowId, english, text) in entries)
            result[key] = new ScriptEntry { Sheet = sheet, RowId = rowId, English = english, Text = text };

        return result;
    }

    /// <summary>
    /// The real line from <c>custom/000/CmnBhtEnterLv020St0003_00013.csv</c> row 28.
    /// The key and row id are the same in the English, Japanese, and Chinese dumps.
    /// </summary>
    private const string GuildhestKey = "TEXT_CMNBHTENTERLV020ST0003_00013_BEHEST_TALK_00";
    private const string GuildhestSheet = "custom/000/CmnBhtEnterLv020St0003_00013";
    private const string GuildhestEnglish =
        "Guildhests are similar to guildleves in that clients in need of assistance petition the Adventurers' Guild.";
    private const string GuildhestJapanese =
        "「ギルドオーダー」とは、冒険者ギルドが発行する\nパーティ向けの特殊任務のことです。";
    private const string GuildhestChinese = "“行会令”指的是冒险者行会发行的\n面向小队的特殊任务。";

    private static Dictionary<string, ScriptEntry> GuildhestScripts()
        => ScriptsWithRows((GuildhestKey, GuildhestSheet, 28, GuildhestEnglish, GuildhestChinese));

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
    public void ParseThenBuild_MatchesLoad_WhenBuiltFromTheSameArtifacts()
    {
        var descriptor = TalkSheet((1, "Hello there."));
        var artifacts = Artifacts(
            Artifact("CN", (SheetName, FieldName, 1, "你好。")),
            Artifact("TC", (SheetName, FieldName, 1, "妳好。")));
        var configuration = new Configuration { Variant = ChineseVariant.Traditional };

        var parsed = TranslationStore.Parse(configuration, new FakePluginLog(), artifacts);

        Assert.NotNull(parsed);
        var store = TranslationStore.Build(parsed!, [descriptor], ChineseVariant.Traditional, new FakePluginLog());

        Assert.NotNull(store);
        Assert.True(store.TryGetDialogue("Hello there.", out var chinese));
        Assert.Equal("妳好。", chinese);
    }

    [Fact]
    public void Build_FromTimeSlicedExtraction_MatchesTheSyncBuild()
    {
        // The plugin reads the sheets a few milliseconds at a time, then indexes on
        // a worker task. The result must match the one-shot build.
        var descriptor = TalkSheet((1, "Hello there."), (2, "Another line."));
        var artifacts = Artifacts(null, Artifact("TC",
            (SheetName, FieldName, 1, "你好。"),
            (SheetName, FieldName, 2, "另一行。")));
        var parsed = TranslationStore.Parse(
            new Configuration { Variant = ChineseVariant.Traditional },
            new FakePluginLog(),
            artifacts);

        Assert.NotNull(parsed);

        var extraction = new SheetExtraction(parsed!, [descriptor]);
        Assert.False(extraction.IsComplete);

        while (!extraction.IsComplete)
            extraction.Extract(TimeSpan.Zero);

        Assert.Equal(2, extraction.RowCount);

        var store = TranslationStore.Build(
            parsed!,
            [descriptor],
            extraction,
            ChineseVariant.Traditional,
            new FakePluginLog());

        Assert.NotNull(store);
        Assert.True(store.TryGetDialogue("Hello there.", out var first));
        Assert.Equal("你好。", first);
        Assert.True(store.TryGetDialogue("Another line.", out var second));
        Assert.Equal("另一行。", second);
    }

    [Fact]
    public void Build_UsesTheIndexResolverAtLoad_AndTheDisplayResolverAtLookup()
    {
        // The index build runs on a worker task and gets a copy of the player
        // state. Text that depends on the player is still resolved with the live
        // resolver when it is looked up.
        var descriptor = TalkSheet((1, "Hello there."), (2, "Hello again."));
        var artifacts = Artifacts(null, Artifact("TC",
            (SheetName, FieldName, 1, "你好。"),
            (SheetName, FieldName, 2, "<Highlight>ObjectParameter(1)</Highlight> 再見。")));
        var parsed = TranslationStore.Parse(
            new Configuration { Variant = ChineseVariant.Traditional },
            new FakePluginLog(),
            artifacts);

        Assert.NotNull(parsed);
        var store = TranslationStore.Build(
            parsed!,
            [descriptor],
            ChineseVariant.Traditional,
            new FakePluginLog(),
            displayResolver: text => "live:" + text,
            indexResolver: text => "load:" + text);

        Assert.NotNull(store);
        Assert.True(store.TryGetDialogue("Hello there.", out var first));
        Assert.Equal("load:你好。", first);
        Assert.True(store.TryGetDialogue("Hello again.", out var second));
        Assert.Equal("live:<Highlight>ObjectParameter(1)</Highlight> 再見。", second);
    }

    [Fact]
    public void Parse_ReturnsNull_WhenNoArtifactsExist()
    {
        var configuration = new Configuration { Variant = ChineseVariant.Traditional };

        Assert.Null(TranslationStore.Parse(configuration, new FakePluginLog(), Artifacts(null, null)));
    }

    [Fact]
    public void Parse_DoesNotReadTheOtherVariant_WhenFallbackIsOff()
    {
        // The fallback artifact is broken on purpose. With the fallback off it must
        // never be opened, so parsing still succeeds.
        var configuration = new Configuration
        {
            Variant = ChineseVariant.Traditional,
            UseFallbackVariant = false,
        };

        var opened = new List<ChineseVariant>();
        var artifacts = (ChineseVariant variant) =>
        {
            opened.Add(variant);
            return variant == ChineseVariant.Traditional
                ? ToGzipStream(Artifact("TC", (SheetName, FieldName, 1, "妳好。")))
                : ToGzipStream("not json");
        };

        var parsed = TranslationStore.Parse(configuration, new FakePluginLog(), artifacts);

        Assert.NotNull(parsed);
        Assert.Equal([ChineseVariant.Traditional], opened);
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
        Assert.Equal(0, store.TextEntryCount);
        Assert.Equal(1, store.NameEntryCount);
    }

    [Fact]
    public void TryGetName_PrefersTheChineseRow_OverAJapaneseDuplicate()
    {
        // The CN and TC dumps leave rows 10416-10418 in Japanese. Row 10426 has the same
        // English name and the localised one, so the kana row must not win by row order.
        var descriptor = NameSheet((10416, "stellar dragon"), (10426, "stellar dragon"));
        var artifacts = Artifacts(null, Artifact("TC",
            (NameSheetName, NameFieldName, 10416, "ステラ・ドラゴン"),
            (NameSheetName, NameFieldName, 10426, "星宇巨龍")));

        var store = Load(descriptor, ChineseVariant.Traditional, false, artifacts);

        Assert.NotNull(store);
        Assert.True(store.TryGetName("Stellar Dragon", out var name));
        Assert.Equal("星宇巨龍", name);
    }

    [Theory]
    [InlineData("skywatcher", "Skywatcher")]
    [InlineData("Skywatcher", "skywatcher")]
    public void TryGetName_MatchesName_WhenOnlyTheCaseDiffers(string sheetName, string gameName)
    {
        // ENpcResident stores generic NPC names in lower case; the Talk window
        // capitalises them.
        var descriptor = NameSheet((100, sheetName));
        var artifacts = Artifacts(null, Artifact("TC", (NameSheetName, NameFieldName, 100, "天气预报员")));

        var store = Load(descriptor, ChineseVariant.Traditional, false, artifacts);

        Assert.NotNull(store);
        Assert.True(store.TryGetName(gameName, out var name));
        Assert.Equal("天气预报员", name);
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
    public void Lookup_AnswersTheRealStudiumLine_WithTheLivePlayerName()
    {
        const string sheet = "Ah, Player! I have come to this Studium in order to study pudding.";
        const string live = "Ah, Nedyar! I have come to this Studium in order to study pudding.";

        var descriptor = new FakeSheetDescriptor("DefaultTalk", "Text", false,
            new Dictionary<uint, string[]>
            {
                [598522] = [sheet, "0", "0"],
            });

        var store = TranslationStore.Load(
            [descriptor],
            ChineseVariant.Traditional,
            false,
            _ => ToGzipStream(Artifact("TC", ("DefaultTalk", "Text", 598522, "PUDDING-CHINESE"))),
            new FakePluginLog(),
            playerName: () => "Nedyar");

        Assert.NotNull(store);

        var context = new DialogueContext(new DialogueRowId("DefaultTalk", 598522, "Text"));
        Assert.True(store.TryGetDialogue(live, in context, "Puddingway", out var byId), "row id path");
        Assert.Equal("PUDDING-CHINESE", byId);

        Assert.True(store.TryGetDialogue(live, out var byText), "text path");
        Assert.Equal("PUDDING-CHINESE", byText);
    }

    [Fact]
    public void Lookup_AnswersAnAmbiguousLine_WhenThePlayerNameIsTheOnlyDifference()
    {
        // "Are you ready, Player?" is shared by several speakers. The live line has the real
        // name, and the placeholder step has to reach the ambiguous script step after restoring it.
        var scripts = ScriptsWithKeys(
            ("TEXT_AKTKMG115_04464_ALPHINAUD_000_010",
             "Are you ready, Player?",
             "<Highlight>ObjectParameter(1)</Highlight>……你準備好了嗎？"),
            ("TEXT_STMBDR301_03154_CID_000_050",
             "Are you ready, Player?",
             "那麼……怎麼樣，<Highlight>ObjectParameter(1)</Highlight>，\n準備好了嗎？"));

        var artifacts = Artifacts(null, Artifact("TC", scripts));
        var store = TranslationStore.Load(
            [],
            ChineseVariant.Traditional,
            false,
            artifacts,
            new FakePluginLog(),
            playerName: () => "Nedyar Sol");

        Assert.NotNull(store);
        var context = new DialogueContext(CutsceneId: 6006);
        Assert.True(store.TryGetDialogue("Are you ready, Nedyar?", in context, "Alphinaud", out var chinese));
        Assert.Equal("……你準備好了嗎？", chinese);
    }

    [Fact]
    public void Lookup_AnswersASheetRow_WhoseEnglishNamesTheOtherGender()
    {
        // DefaultTalk 598565: the dump holds "Ah, Mistress Player" and the game shows
        // "Ah, Master Nedyar" to a male character, so only one branch's wording is indexed.
        const string sheet = "Ah, Mistress Player. I have been made aware of your coming.";
        const string live = "Ah, Master Nedyar. I have been made aware of your coming.";

        var descriptor = new FakeSheetDescriptor("DefaultTalk", "Text", false,
            new Dictionary<uint, string[]>
            {
                [598565] = [sheet, "0", "0"],
            });

        var store = TranslationStore.Load(
            [descriptor],
            ChineseVariant.Traditional,
            false,
            _ => ToGzipStream(Artifact("TC", ("DefaultTalk", "Text", 598565,
                "<Highlight>ObjectParameter(1)</Highlight>大人。事情我都聽說了。"))),
            new FakePluginLog(),
            playerName: () => "Nedyar");

        Assert.NotNull(store);
        Assert.True(store.TryGetDialogue(live, out var chinese));
        Assert.Contains("大人", chinese);
    }

    [Fact]
    public void Lookup_AnswersASharedKey_WhenTheLinesDifferOnlyInPunctuation()
    {
        // The real pair from cutscene VoiceMan 06006: same English, same words, one closing mark.
        var scripts = ScriptsWithKeys(
            ("TEXT_VOICEMAN_06006_001230_KRILE", "You must triumph.", "一定要凱旋。"),
            ("TEXT_VOICEMAN_06006_006180_KRILE", "You must triumph.", "一定要凱旋"));

        var store = TranslationStore.Load(
            [],
            ChineseVariant.Traditional,
            false,
            _ => ToGzipStream(Artifact("TC", scripts)),
            new FakePluginLog());

        Assert.NotNull(store);
        Assert.True(store.TryGetDialogue("You must triumph.", default, "Krile", out var chinese));
        Assert.Equal("一定要凱旋。", chinese);
    }

    [Fact]
    public void Lookup_StillRefusesASharedKey_WhenTheLinesDiffer()
    {
        var scripts = ScriptsWithKeys(
            ("TEXT_TEST_000_001_KRILE", "You must triumph.", "一定要凱旋。"),
            ("TEXT_TEST_000_002_KRILE", "You must triumph.", "你一定要贏。"));

        var store = TranslationStore.Load(
            [],
            ChineseVariant.Traditional,
            false,
            _ => ToGzipStream(Artifact("TC", scripts)),
            new FakePluginLog());

        Assert.NotNull(store);
        Assert.False(store.TryGetDialogue("You must triumph.", default, "Krile", out _));
    }

    [Fact]
    public void Lookup_UsesTheCutsceneScene_ToPickASharedOptionLine()
    {
        // The real keys from a cutscene choice: the same English option sits in several scenes.
        var scripts = ScriptsWithKeys(
            ("TEXT_VOICEMAN_06006_A2_000_001_NONE_VOICE", "It's a secret.", "保密！"),
            ("TEXT_VOICEMAN_06005_A2_000_001_NONE_VOICE", "It's a secret.", "這是秘密。"),
            ("TEXT_VOICEMAN_06006_A2_000_003_NONE_VOICE", "Nothing in particular.", "也沒什麼事"),
            ("TEXT_VOICEMAN_06005_A2_000_003_NONE_VOICE", "Nothing in particular.", "什麼都不做。"),
            ("TEXT_VOICEMAN_07003_000520_ESTINIEN", "Nothing in particular.", "我只是到處遊山玩水。"));

        var store = TranslationStore.Load(
            [],
            ChineseVariant.Traditional,
            false,
            _ => ToGzipStream(Artifact("TC", scripts)),
            new FakePluginLog());

        Assert.NotNull(store);

        var scene = new DialogueContext(CutsceneId: 6006);
        Assert.True(store.TryGetDialogue("Nothing in particular.", in scene, null, out var chinese));
        Assert.Equal("也沒什麼事", chinese);

        // Without the scene the store cannot tell the scenes apart, so the line stays English.
        Assert.False(store.TryGetDialogue("Nothing in particular.", out _));
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
    public void Lookup_DoesNotAnswer_WhenSeveralScriptLinesShareOneEnglishKey()
    {
        // Three real lines from the dumps: the same English, a different Chinese line
        // for each speaker.
        var artifacts = Artifacts(null, Artifact("TC", DuplicateScripts()));

        var store = TranslationStore.Load([], ChineseVariant.Traditional, false, artifacts, new FakePluginLog());

        Assert.NotNull(store);
        Assert.False(store.TryGetDialogue("You bastard!", out _));
        Assert.Equal(1, store.AmbiguousScriptKeyCount);
    }

    [Fact]
    public void Lookup_PicksTheScriptLineSpokenByTheNamedSpeaker()
    {
        var artifacts = Artifacts(null, Artifact("TC", DuplicateScripts()));
        var store = TranslationStore.Load([], ChineseVariant.Traditional, false, artifacts, new FakePluginLog());

        Assert.NotNull(store);
        Assert.True(store.TryGetDialogue("You bastard!", default, "Jullus", out var chinese));
        Assert.Equal("芝諾斯……你這個混蛋！！", chinese);
    }

    [Fact]
    public void Lookup_StaysUnanswered_WhenTheNameDoesNotMatchAScriptLine()
    {
        var artifacts = Artifacts(null, Artifact("TC", DuplicateScripts()));
        var store = TranslationStore.Load([], ChineseVariant.Traditional, false, artifacts, new FakePluginLog());

        Assert.NotNull(store);
        Assert.False(store.TryGetDialogue("You bastard!", default, "Y'shtola", out _));
    }

    [Fact]
    public void Lookup_StaysUnanswered_WhenTwoScriptLinesNameTheSameSpeaker()
    {
        var scripts = ScriptsWithKeys(
            ("TEXT_VOICEMAN_00001_1_JULLUS", "You bastard!", "第一句"),
            ("TEXT_VOICEMAN_00002_2_JULLUS", "You bastard!", "第二句"));
        var artifacts = Artifacts(null, Artifact("TC", scripts));
        var store = TranslationStore.Load([], ChineseVariant.Traditional, false, artifacts, new FakePluginLog());

        Assert.NotNull(store);
        Assert.False(store.TryGetDialogue("You bastard!", default, "Jullus", out _));
    }

    [Fact]
    public void Lookup_AcceptsAScriptKey_WhenOnlyAPlayerWordDiffers()
    {
        // The dump resolved the deliverer's race to Hyur; the live line says Viera. The rest of
        // the line is identical, so the key is still the same line.
        var scripts = ScriptsWithKeys(
            ("TEXT_CMNDEFMOGLETTER_00002_LETTERMOOGLE_000_001",
             "Well, if it isn't our favorite delivery Hyur! No new letters for you today, kupo!",
             "現在還沒有給你這個後輩的新郵件咕波。"));

        var artifacts = Artifacts(null, Artifact("TC", scripts));
        var store = TranslationStore.Load([], ChineseVariant.Traditional, false, artifacts, new FakePluginLog());

        Assert.NotNull(store);
        var context = new DialogueContext(
            ScriptKeys: ["TEXT_CMNDEFMOGLETTER_00002_LETTERMOOGLE_000_001"]);

        Assert.True(store.TryGetDialogue(
            "Well, if it isn't our favorite delivery Viera! No new letters for you today, kupo!",
            in context,
            out var chinese));
        Assert.Equal("現在還沒有給你這個後輩的新郵件咕波。", chinese);
    }

    [Fact]
    public void Lookup_IgnoresAScriptKey_WhenTheDumpPairedItWithAnotherLine()
    {
        // The live Lua map can reuse a menu key for another line. The dump paired this key with
        // "Madam?", not with the discard prompt, so the key must not answer for it.
        var scripts = ScriptsWithKeys(
            ("TEXT_CMNDEFRETAINERBELL_00544_ENTER2_ROEGADYN_FEMALE_TYPE3",
             "Madam?",
             "哼，你難道打算再派給我一些工作嗎？"));

        var artifacts = Artifacts(null, Artifact("TC", scripts));
        var store = TranslationStore.Load([], ChineseVariant.Traditional, false, artifacts, new FakePluginLog());

        Assert.NotNull(store);
        var context = new DialogueContext(
            ScriptKeys: ["TEXT_CMNDEFRETAINERBELL_00544_ENTER2_ROEGADYN_FEMALE_TYPE3"]);

        Assert.False(store.TryGetDialogue("Discard the pot of metallic orange dye?", in context, out _));
    }

    [Fact]
    public void Lookup_AnswersByScriptKey_WhenTheEnglishKeyIsAmbiguous()
    {
        var artifacts = Artifacts(null, Artifact("TC", DuplicateScripts()));
        var store = TranslationStore.Load([], ChineseVariant.Traditional, false, artifacts, new FakePluginLog());
        var context = new DialogueContext(
            null,
            ["TEXT_VOICEMAN_06005_000910_JULLUS"]);

        Assert.NotNull(store);
        Assert.True(store.TryGetDialogue("You bastard!", in context, out var chinese));
        Assert.Equal("芝諾斯……你這個混蛋！！", chinese);
    }

    [Fact]
    public void Lookup_StillAnswers_WhenTwoScriptLinesShareOneEnglishKeyAndTheSameChinese()
    {
        var scripts = ScriptsWithKeys(
            ("TEXT_ONE_0001", "See you later.", "再見。"),
            ("TEXT_TWO_0002", "See you later.", "再見。"));
        var artifacts = Artifacts(null, Artifact("TC", scripts));
        var store = TranslationStore.Load([], ChineseVariant.Traditional, false, artifacts, new FakePluginLog());

        Assert.NotNull(store);
        Assert.True(store.TryGetDialogue("See you later.", out var chinese));
        Assert.Equal("再見。", chinese);
        Assert.Equal(0, store.AmbiguousScriptKeyCount);
    }

    [Fact]
    public void Lookup_PicksTheLineForATwoPartName_WhenTheKeyJoinsIt()
    {
        // The three real "Meteion..." lines. The key writes G'raha Tia as one token.
        var scripts = ScriptsWithKeys(
            ("TEXT_VOICEMAN_06004_006510_HERMES", "Meteion...", "梅蒂恩……我……"),
            ("TEXT_VOICEMAN_06006_A6_000_001_NONE_VOICE", "Meteion...", "梅蒂恩！"),
            ("TEXT_AKTKMF102_04440_GRAHATIA_000_049", "Meteion...", "梅蒂恩……"));

        var artifacts = Artifacts(null, Artifact("TC", scripts));
        var store = TranslationStore.Load([], ChineseVariant.Traditional, false, artifacts, new FakePluginLog());

        Assert.NotNull(store);
        Assert.True(store.TryGetDialogue("Meteion...", default, "G'raha Tia", out var chinese));
        Assert.Equal("梅蒂恩……", chinese);
    }

    [Fact]
    public void TryGetCutsceneId_NamesOneScene_OnlyWhenEveryKeyAgrees()
    {
        var scripts = ScriptsWithKeys(
            ("TEXT_VOICEMAN_06006_A6_000_001_NONE_VOICE", "Meteion...", "梅蒂恩！"),
            ("TEXT_VOICEMAN_06006_A6_000_002_NONE_VOICE", "It's her...the bringer of despair.", "她就是絕望的載體"),
            ("TEXT_VOICEMAN_06004_006510_HERMES", "Meteion...", "梅蒂恩……我……"),
            ("TEXT_AKTKMF102_04440_GRAHATIA_000_049", "Meteion...", "梅蒂恩……"));

        var artifacts = Artifacts(null, Artifact("TC", scripts));
        var store = TranslationStore.Load([], ChineseVariant.Traditional, false, artifacts, new FakePluginLog());

        Assert.NotNull(store);
        Assert.True(store.TryGetCutsceneId("It's her...the bringer of despair.", out var scene));
        Assert.Equal(6006u, scene);

        // One line sits in two cutscenes and a quest, so its text cannot name a scene.
        Assert.False(store.TryGetCutsceneId("Meteion...", out _));
    }

    [Fact]
    public void TryGetCutsceneId_DropsALineThatAlsoSitsOutsideACutscene()
    {
        var scripts = ScriptsWithKeys(
            ("TEXT_VOICEMAN_06006_000010_THANCRED", "Hold the line.", "守住！"),
            ("TEXT_AKTKMF102_000010_THANCRED", "Hold the line.", "守住！"));

        var artifacts = Artifacts(null, Artifact("TC", scripts));
        var store = TranslationStore.Load([], ChineseVariant.Traditional, false, artifacts, new FakePluginLog());

        Assert.NotNull(store);
        Assert.False(store.TryGetCutsceneId("Hold the line.", out _));
    }

    [Theory]
    [InlineData("REGUNI5BASEOMICRON_00760")]
    [InlineData("TEXT_REGUNI5BASEOMICRON_00760_Q1_000_000")]
    public void Lookup_UsesAScriptKeyHint_ToPickOneOfSeveralSharedLines(string hint)
    {
        // The Base Omicron options are the ones in the current dialog. The other two lines come
        // from a different registration script and a guide, so only the hint can choose.
        var scripts = ScriptsWithKeys(
            ("TEXT_REGUNI5BASEOMICRON_00760_A1_000_001", "What do you do here?", "你是做什麼的？"),
            ("TEXT_REGSEAREDISTRIBUTION_00087_A1_000_1", "What do you do here?", "交談"),
            ("TEXT_FESPDY2017SHARE_00377_A1_100_001", "What do you do here?", "你在這裡做什麼？"));

        var artifacts = Artifacts(null, Artifact("TC", scripts));
        var store = TranslationStore.Load([], ChineseVariant.Traditional, false, artifacts, new FakePluginLog());

        Assert.NotNull(store);
        Assert.False(store.TryGetDialogue("What do you do here?", out _));

        var context = new DialogueContext(ScriptKeyHints: [hint]);
        Assert.True(store.TryGetDialogue("What do you do here?", in context, out var chinese));
        Assert.Equal("你是做什麼的？", chinese);
    }

    [Fact]
    public void Lookup_PrefersTheScriptLine_WhenASheetRowSharesItsEnglish()
    {
        // NpcYell row 13169 has the same English text but a different, unrelated Chinese line.
        // The sheet text stays as the text fallback, while the speaker picks the script line.
        var descriptor = new FakeSheetDescriptor("NpcYell", "Text", false,
            new Dictionary<uint, string[]>
            {
                [13169] = ["Did you find anything?"],
            });

        var scripts = ScriptsWithKeys(
            ("TEXT_VOICEMAN_06006_005320_ALPHINAUD", "Did you find anything?", "啊，是你們。\n有什麼發現嗎？"),
            ("TEXT_HEAVNA702_01664_ESTINIEN_000_092", "Did you find anything?", "有什麼發現嗎？"));

        var json = Artifact("TC", scripts,
            ("NpcYell", "Text", 13169, "艾佈雷爾諾……\n有什麼收穫嗎？"));
        var store = TranslationStore.Load(
            [descriptor],
            ChineseVariant.Traditional,
            false,
            _ => ToGzipStream(json),
            new FakePluginLog());

        Assert.NotNull(store);

        // Without a speaker the sheet text is the old fallback.
        Assert.True(store.TryGetDialogue("Did you find anything?", out var fallback));
        Assert.Equal("艾佈雷爾諾……\n有什麼收穫嗎？", fallback);

        var context = new DialogueContext(CutsceneId: 6006);
        Assert.True(store.TryGetDialogue("Did you find anything?", in context, "Alphinaud", out var chinese));
        Assert.Equal("啊，是你們。\n有什麼發現嗎？", chinese);
    }

    [Fact]
    public void Lookup_PicksTheFirstBranch_WhenBothLinesAreInOneCutscene()
    {
        // Both lines are in scene 6006 and spoken by G'raha. The game picks one by player
        // progress, and the addon exposes no flag for it. Both are valid for this scene, so the
        // default (first) branch answers instead of leaving the line English.
        var scripts = ScriptsWithKeys(
            ("TEXT_VOICEMAN_06006_004300_GRAHATIA", "The significance of these details.",
                "可能現在的你還有很多資訊沒有接觸到，\n不過，這裡大概就是那個歐米茄的母星。"),
            ("TEXT_VOICEMAN_06006_004320_GRAHATIA", "The significance of these details.",
                "如果你奇怪我為什麼憑藉這些資訊來進行判斷，\n那我只能說，這是在「可能存在的未來」中獲知的情報了。"));

        var artifacts = Artifacts(null, Artifact("TC", scripts));
        var store = TranslationStore.Load([], ChineseVariant.Traditional, false, artifacts, new FakePluginLog());

        Assert.NotNull(store);
        var context = new DialogueContext(CutsceneId: 6006);
        Assert.True(store.TryGetDialogue("The significance of these details.", in context, out var chinese));
        Assert.Equal("可能現在的你還有很多資訊沒有接觸到，\n不過，這裡大概就是那個歐米茄的母星。", chinese);
    }

    /// <summary>The real Endwalker pair: one English line used by two cutscenes.</summary>
    private const string SharedVoiceLine = "That's why...it's too soon for this to end.";

    private static Dictionary<string, ScriptEntry> SharedCutsceneScripts()
        => ScriptsWithKeys(
            ("TEXT_VOICEMAN_06005_003840_THANCRED", SharedVoiceLine, "所以這顆星球……還不能就這樣終結。"),
            ("TEXT_VOICEMAN_06006_006170_THANCRED", SharedVoiceLine, "這顆星球……還不能就這樣終結"));

    [Fact]
    public void Lookup_PicksTheLineForTheCutsceneThatIsPlaying()
    {
        var artifacts = Artifacts(null, Artifact("TC", SharedCutsceneScripts()));
        var store = TranslationStore.Load([], ChineseVariant.Traditional, false, artifacts, new FakePluginLog());

        Assert.NotNull(store);
        Assert.False(store.TryGetDialogue(SharedVoiceLine, out _));

        var context = new DialogueContext(null, null, CutsceneId: 6005);
        Assert.True(store.TryGetDialogue(SharedVoiceLine, in context, out var chinese));
        Assert.Equal("所以這顆星球……還不能就這樣終結。", chinese);
    }

    [Fact]
    public void Lookup_PicksTheCutsceneLine_WhenTheSpeakerMatchesBothLines()
    {
        // Thancred speaks both lines, so only the cutscene can tell them apart.
        var artifacts = Artifacts(null, Artifact("TC", SharedCutsceneScripts()));
        var store = TranslationStore.Load([], ChineseVariant.Traditional, false, artifacts, new FakePluginLog());

        Assert.NotNull(store);
        Assert.False(store.TryGetDialogue(SharedVoiceLine, default, "Thancred", out _));

        var context = new DialogueContext(null, null, CutsceneId: 6006);
        Assert.True(store.TryGetDialogue(SharedVoiceLine, in context, "Thancred", out var chinese));
        Assert.Equal("這顆星球……還不能就這樣終結", chinese);
    }

    [Fact]
    public void Lookup_StaysUnanswered_WhenTheCutsceneHoldsBothLines()
    {
        // A cutscene that is not the one either line belongs to must not choose.
        var artifacts = Artifacts(null, Artifact("TC", SharedCutsceneScripts()));
        var store = TranslationStore.Load([], ChineseVariant.Traditional, false, artifacts, new FakePluginLog());

        Assert.NotNull(store);
        var context = new DialogueContext(null, null, CutsceneId: 9999);
        Assert.False(store.TryGetDialogue(SharedVoiceLine, in context, out _));
    }

    /// <summary>The real porter pair: the question, and the announcement for a locked stand.</summary>
    private static Dictionary<string, ScriptEntry> TaxiGreetingScripts()
        => ScriptsWithKeys(
            ("TEXT_CHOCOBOTAXISTAND_00000_CHOCOBOTAXI_01", "Well met, traveler!", "歡迎光臨。您要使用「陸行鳥運輸」嗎？"),
            ("TEXT_CHOCOBOTAXISTAND_00000_CHOCOBOTAXI_UNLOCK_01", "Well met, traveler!", "歡迎光臨。此處可以為您提供「陸行鳥運輸」的服務。"));

    [Fact]
    public void Lookup_PicksTheTaxiGreetingForAnUnlockedStand()
    {
        var artifacts = Artifacts(null, Artifact("TC", TaxiGreetingScripts()));
        var store = TranslationStore.Load([], ChineseVariant.Traditional, false, artifacts, new FakePluginLog());

        Assert.NotNull(store);
        var context = new DialogueContext(ChocoboTaxiStandUnlocked: true);

        Assert.True(store.TryGetDialogue("Well met, traveler!", in context, out var chinese));
        Assert.Equal("歡迎光臨。您要使用「陸行鳥運輸」嗎？", chinese);
    }

    [Fact]
    public void Lookup_PicksTheTaxiAnnouncementForAStandThatIsStillLocked()
    {
        var artifacts = Artifacts(null, Artifact("TC", TaxiGreetingScripts()));
        var store = TranslationStore.Load([], ChineseVariant.Traditional, false, artifacts, new FakePluginLog());

        Assert.NotNull(store);
        var context = new DialogueContext(ChocoboTaxiStandUnlocked: false);

        Assert.True(store.TryGetDialogue("Well met, traveler!", in context, out var chinese));
        Assert.Equal("歡迎光臨。此處可以為您提供「陸行鳥運輸」的服務。", chinese);
    }

    [Fact]
    public void Lookup_StaysUnanswered_WhenTheTaxiStandStateIsUnknown()
    {
        var artifacts = Artifacts(null, Artifact("TC", TaxiGreetingScripts()));
        var store = TranslationStore.Load([], ChineseVariant.Traditional, false, artifacts, new FakePluginLog());

        Assert.NotNull(store);
        Assert.False(store.TryGetDialogue("Well met, traveler!", out _));
    }

    /// <summary>The real dump lines: one English squeal, several ways to write it in Chinese.</summary>
    private static Dictionary<string, ScriptEntry> SoundOnlyScripts()
        => ScriptsWithKeys(
            ("TEXT_LUCKZC012_03395_BEAVERA03395_000_023", "<squeal> <squeal>", "嗷嗷嗷！"),
            ("TEXT_LUCKZC013_03396_BEAVERA03396_000_020", "<squeal> <squeal>", "嗷嗷，嗷嗷。"),
            ("TEXT_AKTKZC004_04213_GIANTBEAVER04213_000_044", "<squeal> <squeal>", "嗷嗷！"));

    [Fact]
    public void Lookup_AnswersASoundOnlyLineThatSeveralScriptLinesShare()
    {
        // A balloon that holds only a sound has no speaker name and no id, so the candidates cannot
        // be told apart. Any of them says the same thing, so one is answered instead of none.
        var artifacts = Artifacts(null, Artifact("TC", SoundOnlyScripts()));
        var store = TranslationStore.Load([], ChineseVariant.Traditional, false, artifacts, new FakePluginLog());

        Assert.NotNull(store);
        Assert.True(store.TryGetDialogue("<squeal> <squeal>", out var chinese));
        Assert.Contains(chinese, new[] { "嗷嗷嗷！", "嗷嗷，嗷嗷。", "嗷嗷！" });
    }

    [Fact]
    public void Lookup_KeepsASoundMarkerInAMixedLine()
    {
        var scripts = ScriptsWithKeys(
            ("TEXT_AKTKZA004_04183_TINYMIQOTE_000_009", "<squeal> You found him!", "嗷嗷！你找到他了！"));
        var artifacts = Artifacts(null, Artifact("TC", scripts));
        var store = TranslationStore.Load([], ChineseVariant.Traditional, false, artifacts, new FakePluginLog());

        Assert.NotNull(store);
        Assert.True(store.TryGetDialogue("<squeal> You found him!", out var chinese));
        Assert.Equal("嗷嗷！你找到他了！", chinese);
    }

    [Fact]
    public void ScriptLookup_UsesTheLiveRowId_WithoutComparingEnglish()
    {
        var artifacts = Artifacts(Artifact("CN", GuildhestScripts()), null);
        var store = TranslationStore.Load([], ChineseVariant.Simplified, false, artifacts, new FakePluginLog());
        Assert.NotNull(store);

        var rows = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase) { [GuildhestKey] = 28 };
        var context = new DialogueContext(ScriptKeys: [GuildhestKey], ScriptRowIds: rows);

        Assert.True(store.TryGetDialogue(GuildhestJapanese, in context, out var chinese));
        Assert.Equal(GuildhestChinese, chinese);
    }

    [Fact]
    public void ScriptLookup_RejectsAKeyWhoseLiveRowIdDiffers()
    {
        var artifacts = Artifacts(Artifact("CN", GuildhestScripts()), null);
        var store = TranslationStore.Load([], ChineseVariant.Simplified, false, artifacts, new FakePluginLog());
        Assert.NotNull(store);

        var rows = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase) { [GuildhestKey] = 29 };
        var context = new DialogueContext(ScriptKeys: [GuildhestKey], ScriptRowIds: rows);

        Assert.False(store.TryGetDialogue(GuildhestJapanese, in context, out _));
    }

    [Fact]
    public void ScriptLookup_FallsBackToEnglish_WhenTheLiveRowIdIsMissing()
    {
        var artifacts = Artifacts(Artifact("CN", GuildhestScripts()), null);
        var store = TranslationStore.Load([], ChineseVariant.Simplified, false, artifacts, new FakePluginLog());
        Assert.NotNull(store);

        var context = new DialogueContext(ScriptKeys: [GuildhestKey]);

        Assert.True(store.TryGetDialogue(GuildhestEnglish, in context, out var chinese));
        Assert.Equal(GuildhestChinese, chinese);
        Assert.False(store.TryGetDialogue(GuildhestJapanese, in context, out _));
    }

    [Fact]
    public void ScriptLookup_ResolvesBySheetRow()
    {
        var artifacts = Artifacts(Artifact("CN", GuildhestScripts()), null);
        var store = TranslationStore.Load([], ChineseVariant.Simplified, false, artifacts, new FakePluginLog());
        Assert.NotNull(store);

        var context = new DialogueContext(
            ScriptRow: new DialogueRowId(GuildhestSheet, 28, string.Empty));

        Assert.True(store.TryGetDialogue(GuildhestJapanese, in context, out var chinese));
        Assert.Equal(GuildhestChinese, chinese);
    }

    [Fact]
    public void RowLookup_ResolvesDutyTextById()
    {
        var descriptor = new FakeSheetDescriptor(
            "InstanceContentTextData", "Text", false,
            new Dictionary<uint, string[]> { [100] = ["The enemy approaches."] });
        var artifacts = Artifacts(
            Artifact("CN", ("InstanceContentTextData", "Text", 100, "敌人正在接近。")),
            null);

        var store = TranslationStore.Load(
            [descriptor], ChineseVariant.Simplified, false, artifacts, new FakePluginLog());
        Assert.NotNull(store);

        // The clip names the row and the sheet has one text slot, so the slot is known.
        var context = new DialogueContext(
            new DialogueRowId("InstanceContentTextData", 100, "Text", 0));

        Assert.True(store.TryGetDialogue("敵が近づいている。", in context, out var chinese));
        Assert.Equal("敌人正在接近。", chinese);
    }

    [Fact]
    public void ScriptLookup_ResolvesARealCutsceneRow()
    {
        var scripts = ScriptsWithRows(
            ("TEXT_VOICEMAN_02200_000010_THANCRED", "cut_scene/022/VoiceMan_02200", 0,
                "Now then...", "接下来……"));
        var artifacts = Artifacts(Artifact("CN", scripts), null);
        var store = TranslationStore.Load([], ChineseVariant.Simplified, false, artifacts, new FakePluginLog());
        Assert.NotNull(store);

        var context = new DialogueContext(
            ScriptRow: new DialogueRowId("cut_scene/022/VoiceMan_02200", 0, string.Empty));

        Assert.True(store.TryGetDialogue("さてと……。", in context, out var chinese));
        Assert.Equal("接下来……", chinese);
    }

    [Fact]
    public void Cache_KeepsTheScriptRowIds()
    {
        var artifacts = Artifacts(Artifact("CN", GuildhestScripts()), null);
        var store = TranslationStore.Load([], ChineseVariant.Simplified, false, artifacts, new FakePluginLog());
        Assert.NotNull(store);

        var path = Path.Combine(Path.GetTempPath(), $"sinophone-index-{Guid.NewGuid():N}.bin");
        var log = new PluginLog(new FakePluginLog(), new Configuration(), LogCategory.Dialogue);

        try
        {
            TranslationStore.SaveCache(path, "inputs", store, log);

            Assert.True(TranslationStore.TryLoadCache(
                path, "inputs", [], log, key => key, null, out var loaded));
            Assert.NotNull(loaded);

            var rows = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase) { [GuildhestKey] = 28 };
            var context = new DialogueContext(ScriptKeys: [GuildhestKey], ScriptRowIds: rows);

            Assert.True(loaded.TryGetDialogue(GuildhestJapanese, in context, out var chinese));
            Assert.Equal(GuildhestChinese, chinese);

            var wrong = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase) { [GuildhestKey] = 29 };
            Assert.False(loaded.TryGetDialogue(
                GuildhestJapanese,
                new DialogueContext(ScriptKeys: [GuildhestKey], ScriptRowIds: wrong),
                out _));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Cache_KeepsTheScriptLinesThatShareOneEnglishKey()
    {
        var artifacts = Artifacts(null, Artifact("TC", DuplicateScripts()));
        var store = TranslationStore.Load([], ChineseVariant.Traditional, false, artifacts, new FakePluginLog());
        Assert.NotNull(store);

        var path = Path.Combine(Path.GetTempPath(), $"sinophone-index-{Guid.NewGuid():N}.bin");
        var log = new FakePluginLog();

        try
        {
            TranslationStore.SaveCache(path, "inputs", store, new PluginLog(log, new Configuration(), LogCategory.Dialogue));

            Assert.True(TranslationStore.TryLoadCache(
                path, "inputs", [], new PluginLog(log, new Configuration(), LogCategory.Dialogue),
                key => key, null, out var loaded));

            Assert.NotNull(loaded);
            Assert.Equal(1, loaded.AmbiguousScriptKeyCount);
            Assert.False(loaded.TryGetDialogue("You bastard!", out _));
            Assert.True(loaded.TryGetDialogue("You bastard!", default, "Jullus", out var chinese));
            Assert.Equal("芝諾斯……你這個混蛋！！", chinese);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Cache_KeepsTheSceneOfAScriptLine()
    {
        var scripts = ScriptsWithKeys(
            ("TEXT_VOICEMAN_06006_A6_000_002_NONE_VOICE", "It's her...the bringer of despair.", "她就是絕望的載體"));
        var artifacts = Artifacts(null, Artifact("TC", scripts));
        var store = TranslationStore.Load([], ChineseVariant.Traditional, false, artifacts, new FakePluginLog());
        Assert.NotNull(store);

        var path = Path.Combine(Path.GetTempPath(), $"sinophone-index-{Guid.NewGuid():N}.bin");
        var log = new PluginLog(new FakePluginLog(), new Configuration(), LogCategory.Dialogue);

        try
        {
            TranslationStore.SaveCache(path, "inputs", store, log);

            Assert.True(TranslationStore.TryLoadCache(
                path, "inputs", [], log, key => key, null, out var loaded));

            Assert.NotNull(loaded);
            Assert.True(loaded.TryGetCutsceneId("It's her...the bringer of despair.", out var scene));
            Assert.Equal(6006u, scene);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static Dictionary<string, ScriptEntry> DuplicateScripts()
        => ScriptsWithKeys(
            ("TEXT_VOICEMAN_05004_004230_THANCRED", "You bastard!", "愛梅特賽爾克……你！"),
            ("TEXT_VOICEMAN_06005_000910_JULLUS", "You bastard!", "芝諾斯……你這個混蛋！！"),
            ("TEXT_JOBPLD650_02573_ALDIS_000_052", "You bastard!", "你說什麼！"));

    [Fact]
    public void TryGetAddonText_MatchesUnknownPlaceholder()
    {
        var descriptor = new FakeSheetDescriptor("Addon", "Text", false,
            new Dictionary<uint, string[]>
            {
                [102421] = ["Duty calls. Commence battle for\n“UNKNOWN”?\n\nLevel: 90\nItem level: 665"],
            });

        var artifacts = Artifacts(
            Artifact("CN", ("Addon", "Text", 102421, "要开始“StringParameter(1)”的战斗吗？")),
            null);

        var store = TranslationStore.Load(
            [descriptor],
            ChineseVariant.Simplified,
            false,
            artifacts,
            new FakePluginLog());

        Assert.NotNull(store);
        Assert.Equal(3, store.AddonTemplateCount);
        Assert.True(store.TryGetAddonText(
            "Duty calls. Commence battle for\n“Inform the Cold?”\n\nLevel: 90\nItem level: 665",
            out var chinese));
        Assert.Contains("StringParameter(1)", chinese);

        // The game can omit the item-level line. The first-line template still matches.
        Assert.True(store.TryGetAddonText(
            "Duty calls. Commence battle for\n“In from the Cold”?\nLevel sync is in effect.\nLevel: 83",
            out _));
    }

    [Fact]
    public void TryGetAddonText_MatchesAddonRowWithBlankPlaceholders()
    {
        var descriptor = new FakeSheetDescriptor("Addon", "Text", false,
            new Dictionary<uint, string[]>
            {
                [6413] = ["Prepare the bed with  and ?"],
            });

        var artifacts = Artifacts(
            Artifact("CN", ("Addon", "Text", 6413,
                "确定要将<Sheet(Item,IntegerParameter(2),0)/>种植在<Sheet(Item,IntegerParameter(1),0)/>中吗？")),
            null);

        var store = TranslationStore.Load(
            [descriptor],
            ChineseVariant.Simplified,
            false,
            artifacts,
            new FakePluginLog());

        Assert.NotNull(store);
        Assert.Equal(1, store.AddonTemplateCount);
        Assert.True(store.TryGetAddonText(
            "Prepare the bed with a bag of grade 3 Thanalan topsoil and a bag of voidrake seeds?",
            out var chinese));
        Assert.Contains("Sheet(Item", chinese);
    }

    [Fact]
    public void TryGetAddonText_PrefersTheTemplateWithMoreLiteralText()
    {
        // Addon row 3290 fills the seal/seals word with a conditional macro, so
        // its template matches row 11194's prompt too. The row that names the
        // following item has more literal text and has to win.
        var descriptor = new FakeSheetDescriptor("Addon", "Text", false,
            new Dictionary<uint, string[]>
            {
                [3290] = ["Exchange UNKNOWN UNKNOWN for UNKNOWN?"],
                [11194] = ["Exchange UNKNOWN UNKNOWN for the following item?"],
            });

        var artifacts = Artifacts(
            Artifact(
                "CN",
                ("Addon", "Text", 3290, "「<Sheet(Item,IntegerParameter(1),0)/>」×IntegerParameter(2)需要提交军票。要交换吗？"),
                ("Addon", "Text", 11194, "确定要用<Sheet(Item,IntegerParameter(1),0)/>×IntegerParameter(2)，换取以下道具吗？")),
            null);

        var store = TranslationStore.Load(
            [descriptor],
            ChineseVariant.Simplified,
            false,
            artifacts,
            new FakePluginLog());

        Assert.NotNull(store);
        Assert.True(store.TryGetAddonText("Exchange 1,820 MGC for the following item?", out var chinese));
        Assert.Contains("换取以下道具吗", chinese, StringComparison.Ordinal);
        Assert.DoesNotContain("军票", chinese, StringComparison.Ordinal);
    }

    [Fact]
    public void TryGetAddonText_IgnoresTemplateWithNoLiteralText()
    {
        var descriptor = new FakeSheetDescriptor("Addon", "Text", false,
            new Dictionary<uint, string[]>
            {
                [1] = ["UNKNOWN"],
            });

        var artifacts = Artifacts(
            Artifact("CN", ("Addon", "Text", 1, "取消")),
            null);

        var store = TranslationStore.Load(
            [descriptor],
            ChineseVariant.Simplified,
            false,
            artifacts,
            new FakePluginLog());

        Assert.NotNull(store);
        Assert.Equal(0, store.AddonTemplateCount);
        Assert.False(store.TryGetAddonText("Settle down for a restful supper?", out _));
    }

    [Fact]
    public void TryGetAddonLabel_PrefersAddonRowOverDialogueRowWithSameText()
    {
        var npcYell = new FakeSheetDescriptor("NpcYell", "Text", false,
            new Dictionary<uint, string[]> { [730] = ["No."] });
        var addon = new FakeSheetDescriptor("Addon", "Text", false,
            new Dictionary<uint, string[]> { [4] = ["No."] });

        var artifacts = Artifacts(
            Artifact("CN",
                ("NpcYell", "Text", 730, "希望只是我想多了吧……"),
                ("Addon", "Text", 4, "否")),
            null);

        var store = TranslationStore.Load(
            [npcYell, addon],
            ChineseVariant.Simplified,
            false,
            artifacts,
            new FakePluginLog());

        Assert.NotNull(store);
        Assert.True(store.TryGetAddonLabel("No.", out var label));
        Assert.Equal("否", label);
        Assert.True(store.TryGetDialogue("No.", out var dialogue));
        Assert.Equal("希望只是我想多了吧……", dialogue);
    }

    [Fact]
    public void Load_RemembersTextStyleFromScriptLine()
    {
        var artifacts = Artifacts(
            null,
            Artifact("TC", Scripts(
                ("(-Ancient Spirit-)I... I...",
                 "(-古代人-)<UIForeground>F20223</UIForeground>我，我是……<UIForeground>01</UIForeground>"))));

        var store = TranslationStore.Load([], ChineseVariant.Traditional, false, artifacts, new FakePluginLog());

        Assert.NotNull(store);
        Assert.True(store.TryGetDialogue("I... I...", out var line));
        Assert.Equal("我，我是……", line);
        Assert.True(store.TryGetStyle(line, out var style));
        Assert.Equal((ushort)0x0223, style.ForegroundKey);
    }

    [Fact]
    public void Load_LearnsSpeakerNameFromScriptAlias()
    {
        var artifacts = Artifacts(
            null,
            Artifact("TC", Scripts(
                ("(-Distant Echo-)**You... With me...",
                 "(-不知從何處傳來的聲音-)……之人……來這裡……"))));

        var store = TranslationStore.Load([], ChineseVariant.Traditional, false, artifacts, new FakePluginLog());

        Assert.NotNull(store);
        Assert.True(store.TryGetName("Distant Echo", out var name));
        Assert.Equal("不知從何處傳來的聲音", name);
    }

    [Fact]
    public void Load_ResolvesSheetMacroFromNameRow()
    {
        var names = new FakeSheetDescriptor("EObjName", "Singular", true,
            new Dictionary<uint, string[]> { [2012178] = ["保存食物的货箱"] });

        var artifacts = Artifacts(
            Artifact("CN",
                Scripts(
                    ("You open the storage unit and find a rainbow-colored assortment of carrots.",
                     "<Sheet(EObj,2012178,0)/>中\r\n装有各种颜色的胡萝卜……")),
                ("EObjName", "Singular", 2012178, "保存食物的货箱")),
            null);

        var store = TranslationStore.Load(
            [names],
            ChineseVariant.Simplified,
            false,
            artifacts,
            new FakePluginLog());

        Assert.NotNull(store);
        Assert.True(store.TryGetDialogue(
            "You open the storage unit and find a rainbow-colored assortment of carrots.",
            out var chinese));
        Assert.Contains("保存食物的货箱", chinese);
    }

    [Fact]
    public void Load_StripsSpeakerPrefixFromScriptEnglish()
    {
        var artifacts = Artifacts(
            null,
            Artifact("TC", Scripts(
                ("(-Radio-)We...have gained the power...take back...rightfully ours.",
                 "(-收音機-)朕乃……加雷馬帝國……皇帝……"))));

        var store = TranslationStore.Load([], ChineseVariant.Traditional, false, artifacts, new FakePluginLog());

        Assert.NotNull(store);
        Assert.True(store.TryGetDialogue(
            "We...have gained the power...take back...rightfully ours.", out var chinese));
        Assert.Equal("朕乃……加雷馬帝國……皇帝……", chinese);

        // Battle talk can pass the live line with the dump's alias still attached.
        Assert.True(store.TryGetDialogue(
            "(-Radio-)We...have gained the power...take back...rightfully ours.", out var prefixed));
        Assert.Equal("朕乃……加雷馬帝國……皇帝……", prefixed);
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
    public void TryGetDialogue_MatchesLine_WhenPlayerNameReplacesPlaceholder()
    {
        var artifacts = Artifacts(null, Artifact("TC", Scripts(("Hello, Player Player.", "你好。"))));

        var store = TranslationStore.Load(
            [],
            ChineseVariant.Traditional,
            false,
            artifacts,
            new FakePluginLog(),
            playerName: () => "Alice Rivers");

        Assert.NotNull(store);
        Assert.True(store.TryGetDialogue("Hello, Alice Rivers.", out var chinese));
        Assert.Equal("你好。", chinese);
    }

    [Fact]
    public void TryGetDialogue_DoesNotMatchPlayerName_WhenNoPlayerNameIsSupplied()
    {
        var artifacts = Artifacts(null, Artifact("TC", Scripts(("Hello, Player Player.", "你好。"))));

        var store = TranslationStore.Load([], ChineseVariant.Traditional, false, artifacts, new FakePluginLog());

        Assert.NotNull(store);
        Assert.False(store.TryGetDialogue("Hello, Alice Rivers.", out _));
    }

    [Fact]
    public void TryGetDialogue_ResolvesPlayerNameInDisplay_OnEachLookup()
    {
        var playerName = "Alice Rivers";
        var artifacts = Artifacts(
            null,
            Artifact("TC", Scripts(("Hello, Player Player.", "你好，<Highlight>ObjectParameter(1)</Highlight>。"))));

        var store = TranslationStore.Load(
            [],
            ChineseVariant.Traditional,
            false,
            artifacts,
            new FakePluginLog(),
            displayResolver: text => MacroNormalizer.CleanForDisplay(text, playerName: playerName),
            playerName: () => playerName);

        Assert.NotNull(store);

        Assert.True(store.TryGetDialogue("Hello, Alice Rivers.", out var first));
        Assert.Equal("你好，Alice Rivers。", first);

        playerName = "Bob";
        Assert.True(store.TryGetDialogue("Hello, Bob.", out var second));
        Assert.Equal("你好，Bob。", second);
    }

    [Fact]
    public void TryGetDialogue_MatchesOtherGender_WhenScriptUsesOnePronoun()
    {
        var artifacts = Artifacts(null, Artifact("TC", Scripts(
            ("One could reduce her body to aether without losing the grip on her spirit.",
                "而“返回”或“传送”这类魔法，"))));

        var store = TranslationStore.Load([], ChineseVariant.Traditional, false, artifacts, new FakePluginLog());

        Assert.NotNull(store);
        Assert.True(store.TryGetDialogue(
            "One could reduce his body to aether without losing the grip on his spirit.", out var chinese));
        Assert.Equal("而“返回”或“传送”这类魔法，", chinese);
    }

    [Fact]
    public void TryGetDialogue_MatchesMaleWording_WhenDumpUsesFemaleWording()
    {
        var artifacts = Artifacts(null, Artifact("TC", Scripts(
            ("He says the aetheryte is closed.", "传唤水晶已经关闭。"))));

        var store = TranslationStore.Load([], ChineseVariant.Traditional, false, artifacts, new FakePluginLog());

        Assert.NotNull(store);
        Assert.True(store.TryGetDialogue("She says the aetheryte is closed.", out var chinese));
        Assert.Equal("传唤水晶已经关闭。", chinese);
    }

    [Fact]
    public void TryGetDialogue_PrefersExactGender_WhenBothFormsAreIndexed()
    {
        var artifacts = Artifacts(null, Artifact("TC", Scripts(
            ("He says the aetheryte is closed.", "他說。"),
            ("She says the aetheryte is closed.", "她說。"))));

        var store = TranslationStore.Load([], ChineseVariant.Traditional, false, artifacts, new FakePluginLog());

        Assert.NotNull(store);
        Assert.True(store.TryGetDialogue("She says the aetheryte is closed.", out var chinese));
        Assert.Equal("她說。", chinese);
    }

    [Fact]
    public void TryGetDialogue_DoesNotMatchGender_WhenTextIsDifferent()
    {
        var artifacts = Artifacts(null, Artifact("TC", Scripts(
            ("She says the aetheryte is closed.", "传唤水晶已经关闭。"))));

        var store = TranslationStore.Load([], ChineseVariant.Traditional, false, artifacts, new FakePluginLog());

        Assert.NotNull(store);
        Assert.False(store.TryGetDialogue("She says the aetheryte is open.", out _));
    }

    [Fact]
    public void TryGetDialogue_MatchesOtherGenderNoun_WhenDumpUsesTheOtherWording()
    {
        var artifacts = Artifacts(null, Artifact("TC", Scripts(
            ("Greetin's, lass. What can I do for ye this fine evenin'?", "喲，這裡是後桅旅店接待處。"))));

        var store = TranslationStore.Load([], ChineseVariant.Traditional, false, artifacts, new FakePluginLog());

        Assert.NotNull(store);
        Assert.True(store.TryGetDialogue(
            "Greetin's, lad. What can I do for ye this fine evenin'?", out var chinese));
        Assert.Equal("喲，這裡是後桅旅店接待處。", chinese);
    }

    [Fact]
    public void TryGetDialogue_MatchesThePlayerNoun_WhenTheDumpNamesTheOtherGender()
    {
        // The real Yezahn line. The dump holds the female wording; a male player sees
        // "him"/"man". The pronoun list covers "her"/"him", and the noun list now covers
        // "woman"/"man", so the branch-neutral key still matches after the name is restored.
        const string key = "TEXT_AKTKZF101_04493_YEZAHN_000_001";
        var artifacts = Artifacts(Artifact("CN", ScriptsWithKeys(
            (key,
                "Wait, you're her! The very woman who saved Mehvan's child. Player, was it?",
                "咦，你是不是那个救了麦梵孩子的？"))), null);

        var store = TranslationStore.Load(
            [],
            ChineseVariant.Simplified,
            false,
            artifacts,
            new FakePluginLog(),
            playerName: () => "Nedyar");

        Assert.NotNull(store);
        Assert.True(store.TryGetDialogue(
            "Wait, you're him! The very man who saved Mehvan's child. Player, was it?", out _));
        Assert.True(store.TryGetDialogue(
            "Wait, you're him! The very man who saved Mehvan's child. Nedyar, was it?",
            out var chinese));
        Assert.Equal("咦，你是不是那个救了麦梵孩子的？", chinese);
    }

    [Fact]
    public void TryGetDialogue_UsesTheLiveTemplate_WhenTheDumpHasAnotherBranch()
    {
        // "baron"/"baroness" is not in a branch list, but the game's own Lua value marks
        // the word as a span that varies, so the dump line is still this key's other branch.
        const string key = "TEXT_TEST_000_1";
        var artifacts = Artifacts(null, Artifact("TC", ScriptsWithKeys(
            (key, "The baron is here.", "男爵在此。"))));

        var store = TranslationStore.Load(
            [],
            ChineseVariant.Traditional,
            false,
            artifacts,
            new FakePluginLog());

        Assert.NotNull(store);

        var template = $"The {ScriptTextTemplate.Wildcard} is here.";
        var context = new DialogueContext(
            ScriptKeys: [key],
            ScriptTemplates: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [key] = template,
            });

        Assert.True(store.TryGetDialogue("The baroness is here.", in context, out var chinese));
        Assert.Equal("男爵在此。", chinese);

        // Without the template the line stays English, which is the old behavior.
        var plain = new DialogueContext(ScriptKeys: [key]);
        Assert.False(store.TryGetDialogue("The baroness is here.", in plain, out _));

        // A reused key named by another line does not match outside the template's spans.
        Assert.False(store.TryGetDialogue("The knight is away.", in context, out _));
    }

    [Fact]
    public void TryGetDialogue_MatchesOtherTimeOfDay_WhenDumpUsesTheOtherWording()
    {
        var artifacts = Artifacts(null, Artifact("TC", Scripts(
            ("Greetin's, lass. What can I do for ye this fine evenin'?", "喲，這裡是後桅旅店接待處。"))));

        var store = TranslationStore.Load([], ChineseVariant.Traditional, false, artifacts, new FakePluginLog());

        Assert.NotNull(store);
        Assert.True(store.TryGetDialogue(
            "Greetin's, lad. What can I do for ye this fine day?", out var chinese));
        Assert.Equal("喲，這裡是後桅旅店接待處。", chinese);
    }

    [Fact]
    public void TryGetDialogue_MatchesCollapsedLine_WhenBothVariantsHoldTheScript()
    {
        // The fallback variant holds the same English under a different Chinese, so
        // the fallback pass must not turn the primary's collapsed key ambiguous.
        var artifacts = Artifacts(
            Artifact("CN", Scripts(
                ("Greetin's, lass. What can I do for ye this fine evenin'?", "喲，这里是后桅旅店接待处。"))),
            Artifact("TC", Scripts(
                ("Greetin's, lass. What can I do for ye this fine evenin'?", "喲，這裡是後桅旅店接待處。"))));

        var store = TranslationStore.Load([], ChineseVariant.Traditional, true, artifacts, new FakePluginLog());

        Assert.NotNull(store);
        Assert.True(store.TryGetDialogue(
            "Greetin's, lad. What can I do for ye this fine day?", out var chinese));
        Assert.Equal("喲，這裡是後桅旅店接待處。", chinese);
    }

    [Fact]
    public void TryGetDialogue_DoesNotUseACollapsedKey_WhenItMatchesTwoLines()
    {
        var artifacts = Artifacts(null, Artifact("TC", Scripts(
            ("She is the lass.", "她是少女。"),
            ("She is the lad.", "她是少年。"))));

        var store = TranslationStore.Load([], ChineseVariant.Traditional, false, artifacts, new FakePluginLog());

        // Both lines collapse to the same key with different Chinese, so the key
        // cannot answer for either one. The exact lines still match.
        Assert.NotNull(store);
        Assert.False(store.TryGetDialogue("He is the lass.", out _));
        Assert.True(store.TryGetDialogue("She is the lass.", out var chinese));
        Assert.Equal("她是少女。", chinese);
    }

    [Fact]
    public void TryGetDialogue_PrefersRowId_WhenEnglishTextAppearsInSeveralRows()
    {
        var descriptor = TalkSheet((1, "Same line."), (2, "Same line."));
        var artifacts = Artifacts(null, Artifact("TC",
            (SheetName, FieldName, 1, "第一行"),
            (SheetName, FieldName, 2, "第二行")));

        var store = Load(descriptor, ChineseVariant.Traditional, false, artifacts);
        Assert.NotNull(store);

        var context = new DialogueContext(new DialogueRowId(SheetName, 2, FieldName));
        Assert.True(store.TryGetDialogue("Same line.", in context, out var byId));
        Assert.Equal("第二行", byId);

        Assert.True(store.TryGetDialogue("Same line.", out var byText));
        Assert.Equal("第一行", byText);
    }

    [Fact]
    public void TryGetDialogue_UsesRowSlot_WhenSlotIsKnown()
    {
        var rows = new Dictionary<uint, IReadOnlyList<IReadOnlyList<string>>>
        {
            [1] = new IReadOnlyList<string>[] { new[] { "First line." }, new[] { "Second line." } },
        };
        var descriptor = new FakeSheetDescriptor(SheetName, FieldName, false, rows);
        var artifacts = Artifacts(null, ArtifactWithSlots("TC",
            (SheetName, FieldName, 1, ["第一行", "第二行"])));

        var store = TranslationStore.Load([descriptor], ChineseVariant.Traditional, false, artifacts, new FakePluginLog());
        Assert.NotNull(store);

        var context = new DialogueContext(new DialogueRowId(SheetName, 1, FieldName, 1));
        Assert.True(store.TryGetDialogue("Second line.", in context, out var chinese));
        Assert.Equal("第二行", chinese);
    }

    [Fact]
    public void TryGetDialogue_SelectsRowSlotByText_WhenSlotIsUnknown()
    {
        var rows = new Dictionary<uint, IReadOnlyList<IReadOnlyList<string>>>
        {
            [1] = new IReadOnlyList<string>[] { new[] { "First line." }, new[] { "Second line." } },
        };
        var descriptor = new FakeSheetDescriptor(SheetName, FieldName, false, rows);
        var artifacts = Artifacts(null, ArtifactWithSlots("TC",
            (SheetName, FieldName, 1, ["第一行", "第二行"])));

        var store = TranslationStore.Load([descriptor], ChineseVariant.Traditional, false, artifacts, new FakePluginLog());
        Assert.NotNull(store);

        var context = new DialogueContext(new DialogueRowId(SheetName, 1, FieldName));
        Assert.True(store.TryGetDialogue("Second line.", in context, out var chinese));
        Assert.Equal("第二行", chinese);
    }

    [Fact]
    public void TryGetDialogue_FailsOpen_WhenRowTextMatchesNoSlot()
    {
        // The store used to guess the only translated slot here, so a second page
        // that the translation did not cover showed the first page's Chinese again.
        var rows = new Dictionary<uint, IReadOnlyList<IReadOnlyList<string>>>
        {
            [1] = new IReadOnlyList<string>[] { new[] { "First line." }, new[] { "Second line." } },
        };
        var descriptor = new FakeSheetDescriptor(SheetName, FieldName, false, rows);
        var artifacts = Artifacts(null, ArtifactWithSlots("TC",
            (SheetName, FieldName, 1, ["第一行", "", ""])));

        var store = TranslationStore.Load([descriptor], ChineseVariant.Traditional, false, artifacts, new FakePluginLog());
        Assert.NotNull(store);

        var context = new DialogueContext(new DialogueRowId(SheetName, 1, FieldName));
        Assert.False(store.TryGetDialogue("Second line.", in context, out var chinese));
        Assert.Equal(string.Empty, chinese);
    }

    [Fact]
    public void TryGetDialogue_UsesTheOnlySlot_WhenThereIsNoLineToCompare()
    {
        // A surface without the English line still gets the one translated slot.
        var rows = new Dictionary<uint, IReadOnlyList<IReadOnlyList<string>>>
        {
            [1] = new IReadOnlyList<string>[] { new[] { "First line." }, new[] { "Second line." } },
        };
        var descriptor = new FakeSheetDescriptor(SheetName, FieldName, false, rows);
        var artifacts = Artifacts(null, ArtifactWithSlots("TC",
            (SheetName, FieldName, 1, ["第一行", "", ""])));

        var store = TranslationStore.Load([descriptor], ChineseVariant.Traditional, false, artifacts, new FakePluginLog());
        Assert.NotNull(store);

        var context = new DialogueContext(new DialogueRowId(SheetName, 1, FieldName));
        Assert.True(store.TryGetDialogue(null, in context, out var chinese));
        Assert.Equal("第一行", chinese);
    }

    [Fact]
    public void TryGetDialogue_FallsBackToText_WhenRowIdIsNotInTheArtifact()
    {
        var descriptor = TalkSheet((1, "Hello there."));
        var artifacts = Artifacts(null, Artifact("TC", (SheetName, FieldName, 1, "你好。")));

        var store = Load(descriptor, ChineseVariant.Traditional, false, artifacts);
        Assert.NotNull(store);

        var context = new DialogueContext(new DialogueRowId(SheetName, 999, FieldName));
        Assert.True(store.TryGetDialogue("Hello there.", in context, out var chinese));
        Assert.Equal("你好。", chinese);
    }

    [Fact]
    public void TryGetDialogue_UsesScriptKey_WhenEnglishTextAppearsInSeveralScripts()
    {
        var scripts = new Dictionary<string, ScriptEntry>(StringComparer.Ordinal)
        {
            ["TEXT_TEST_A"] = new() { English = "Same script line.", Text = "甲" },
            ["TEXT_TEST_B"] = new() { English = "Same script line.", Text = "乙" },
        };
        var artifacts = Artifacts(null, Artifact("TC", scripts));

        var store = TranslationStore.Load([], ChineseVariant.Traditional, false, artifacts, new FakePluginLog());
        Assert.NotNull(store);

        var context = new DialogueContext(ScriptKeys: ["TEXT_TEST_B"]);
        Assert.True(store.TryGetDialogue("Same script line.", in context, out var byKey));
        Assert.Equal("乙", byKey);

        // The English alone cannot tell the two lines apart, so it must not answer.
        Assert.False(store.TryGetDialogue("Same script line.", out _));
    }

    [Fact]
    public void TryGetDialogue_FallsBackToText_WhenScriptKeyIsMissing()
    {
        var artifacts = Artifacts(null, Artifact("TC", Scripts(("Hello from a script.", "腳本你好。"))));

        var store = TranslationStore.Load([], ChineseVariant.Traditional, false, artifacts, new FakePluginLog());
        Assert.NotNull(store);

        var context = new DialogueContext(ScriptKeys: ["TEXT_NOT_IN_ARTIFACT"]);
        Assert.True(store.TryGetDialogue("Hello from a script.", in context, out var chinese));
        Assert.Equal("腳本你好。", chinese);
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

    [Fact]
    public void TryGetDialogue_SkipsScriptEntry_WhenEnglishIsUnknownPlaceholder()
    {
        var scripts = new Dictionary<string, ScriptEntry>(StringComparer.Ordinal)
        {
            ["TEXT_BAD"] = new() { English = "\r\nUNKNOWN ", Text = "錯誤" },
        };

        var artifacts = Artifacts(null, Artifact("TC", scripts));
        var store = TranslationStore.Load([], ChineseVariant.Traditional, false, artifacts, new FakePluginLog());

        Assert.NotNull(store);
        Assert.Equal(0, store.ScriptEntryCount);

        var context = new DialogueContext(ScriptKeys: ["TEXT_BAD"]);
        Assert.False(store.TryGetDialogue("Hide-and-seek? What's that?", in context, out _));
    }

    [Fact]
    public void TryGetDialogue_RestoresSinglePlayerPlaceholder_WhenGameHasTheName()
    {
        var descriptor = TalkSheet((1, "Player...and Jullus."));
        var artifacts = Artifacts(Artifact("CN", (SheetName, FieldName, 1, "啊……Nedyar、尤卢斯……")), null);

        var store = TranslationStore.Load(
            [descriptor],
            ChineseVariant.Simplified,
            false,
            artifacts,
            new FakePluginLog(),
            playerName: () => "Nedyar");

        Assert.NotNull(store);
        Assert.True(store.TryGetDialogue("Nedyar...and Jullus.", out var chinese));
        Assert.Equal("啊……Nedyar、尤卢斯……", chinese);
    }

    [Fact]
    public void TryGetDialogue_RestoresPlaceholder_WhenGameUsesOnlyTheFirstName()
    {
        var descriptor = TalkSheet((1, "Ah, Player. I was informed of Quintus's suicide."));
        var artifacts = Artifacts(Artifact("CN", (SheetName, FieldName, 1, "您回来了！")), null);

        var store = TranslationStore.Load(
            [descriptor],
            ChineseVariant.Simplified,
            false,
            artifacts,
            new FakePluginLog(),
            playerName: () => "Nedyar Testname");

        Assert.NotNull(store);
        Assert.True(store.TryGetDialogue(
            "Ah, Nedyar. I was informed of Quintus's suicide.", out var chinese));
        Assert.Equal("您回来了！", chinese);
    }

    [Fact]
    public void TryGetName_TrimsTrailingPunctuation_WhenExactNameIsMissing()
    {
        var descriptor = NameSheet((100, "Pattin"));
        var artifacts = Artifacts(null, Artifact("TC", (NameSheetName, NameFieldName, 100, "帕汀")));

        var store = Load(descriptor, ChineseVariant.Traditional, false, artifacts);

        Assert.NotNull(store);
        Assert.True(store.TryGetName("Pattin?", out var name));
        Assert.Equal("帕汀", name);
    }

    [Fact]
    public void Cache_RoundTrip_RestoresTheLookups()
    {
        var talk = TalkSheet((1, "Hello there."));
        var names = NameSheet((1, "Mother Miounne"));
        var addon = new FakeSheetDescriptor("Addon", "Text", false,
            new Dictionary<uint, string[]>
            {
                [102421] = ["Duty calls. Commence battle for\n“UNKNOWN”?\n\nLevel: 90\nItem level: 665"],
            });

        var artifacts = Artifacts(
            null,
            Artifact("TC",
                Scripts(("(-Ancient Spirit-)I... I...", "(-古代人-)<UIForeground>F20223</UIForeground>我，我是……<UIForeground>01</UIForeground>")),
                (SheetName, FieldName, 1, "你好。"),
                (NameSheetName, NameFieldName, 1, "穆恩"),
                ("Addon", "Text", 102421, "要开始“StringParameter(1)”的战斗吗？")));

        var store = TranslationStore.Load(
            [talk, names, addon],
            ChineseVariant.Traditional,
            false,
            artifacts,
            new FakePluginLog());

        Assert.NotNull(store);

        var path = Path.Combine(Path.GetTempPath(), $"sinophone-cache-{Guid.NewGuid():N}.bin");
        var log = new PluginLog(new FakePluginLog(), new Configuration(), LogCategory.Data);

        try
        {
            TranslationStore.SaveCache(path, "key", store, log);

            Assert.True(TranslationStore.TryLoadCache(
                path, "key", [talk, names, addon], log, s => s, null, out var cached));
            Assert.NotNull(cached);

            Assert.Equal(store.TextEntryCount, cached.TextEntryCount);
            Assert.Equal(store.NameEntryCount, cached.NameEntryCount);
            Assert.Equal(store.ScriptEntryCount, cached.ScriptEntryCount);

            Assert.True(cached.TryGetDialogue("Hello there.", out var dialogue));
            Assert.Equal("你好。", dialogue);

            Assert.True(cached.TryGetName("Mother Miounne", out var name));
            Assert.Equal("穆恩", name);

            Assert.True(cached.TryGetDialogue("I... I...", out var script));
            Assert.Equal("我，我是……", script);
            Assert.True(cached.TryGetStyle(script, out var style));
            Assert.Equal((ushort)0x0223, style.ForegroundKey);

            Assert.True(cached.TryGetAddonText(
                "Duty calls. Commence battle for\n“Inform the Cold?”\n\nLevel: 90\nItem level: 665",
                out var addonText));
            Assert.Contains("StringParameter(1)", addonText);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Cache_RoundTrip_KeepsTheSheetSampleAndTheCutsceneScene()
    {
        // The testing tab samples SheetTextEntries, and a selection dialog asks the store
        // for the scene. A cache hit must still answer both.
        var talk = TalkSheet((1, "Hello there."));
        var scripts = ScriptsWithKeys(
            ("TEXT_VOICEMAN_06005_003840_THANCRED", "A line.", "台詞。"));

        var artifacts = Artifacts(
            null,
            Artifact("TC", scripts, (SheetName, FieldName, 1, "你好。")));

        var store = TranslationStore.Load(
            [talk],
            ChineseVariant.Traditional,
            false,
            artifacts,
            new FakePluginLog());

        Assert.NotNull(store);

        var path = Path.Combine(Path.GetTempPath(), $"sinophone-cache-{Guid.NewGuid():N}.bin");
        var log = new PluginLog(new FakePluginLog(), new Configuration(), LogCategory.Data);

        try
        {
            TranslationStore.SaveCache(path, "key", store, log);

            Assert.True(TranslationStore.TryLoadCache(
                path, "key", [talk], log, s => s, null, out var cached));
            Assert.NotNull(cached);

            Assert.Equal([("Hello there.", "你好。")], cached.SheetTextEntries);

            Assert.True(cached.TryGetCutsceneId("A line.", out var scene));
            Assert.Equal(6005u, scene);

            Assert.True(cached.TryGetDialogue("A line.", out var chinese));
            Assert.Equal("台詞。", chinese);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Cache_IsIgnored_WhenTheKeyDiffers()
    {
        var descriptor = TalkSheet((1, "Hello there."));
        var artifacts = Artifacts(null, Artifact("TC", (SheetName, FieldName, 1, "你好。")));
        var store = Load(descriptor, ChineseVariant.Traditional, false, artifacts);
        Assert.NotNull(store);

        var path = Path.Combine(Path.GetTempPath(), $"sinophone-cache-{Guid.NewGuid():N}.bin");
        var log = new PluginLog(new FakePluginLog(), new Configuration(), LogCategory.Data);

        try
        {
            TranslationStore.SaveCache(path, "old", store, log);

            Assert.False(TranslationStore.TryLoadCache(
                path, "new", [descriptor], log, s => s, null, out var cached));
            Assert.Null(cached);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Cache_IsIgnored_WhenTheFileIsNotAnIndex()
    {
        var descriptor = TalkSheet((1, "Hello there."));
        var path = Path.Combine(Path.GetTempPath(), $"sinophone-cache-{Guid.NewGuid():N}.bin");
        var log = new PluginLog(new FakePluginLog(), new Configuration(), LogCategory.Data);

        try
        {
            File.WriteAllBytes(path, [1, 2, 3, 4]);

            Assert.False(TranslationStore.TryLoadCache(
                path, "key", [descriptor], log, s => s, null, out var cached));
            Assert.Null(cached);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Cache_IsIgnored_WhenTheHeaderIsIntactButTheBodyIsCutShort()
    {
        // A crash during the save leaves the magic and the key, then nothing. The file
        // passes the header check, so the body read is what has to fail safely.
        var descriptor = TalkSheet((1, "Hello there."));
        var artifacts = Artifacts(null, Artifact("TC", (SheetName, FieldName, 1, "你好。")));
        var store = Load(descriptor, ChineseVariant.Traditional, false, artifacts);
        Assert.NotNull(store);

        var path = Path.Combine(Path.GetTempPath(), $"sinophone-cache-{Guid.NewGuid():N}.bin");
        var log = new PluginLog(new FakePluginLog(), new Configuration(), LogCategory.Data);

        try
        {
            TranslationStore.SaveCache(path, "key", store, log);

            var full = File.ReadAllBytes(path);

            // The header is the 8-byte magic, a 4-byte version, then the key written as
            // a 7-bit-encoded length followed by its bytes. Cut right after the key so
            // the header check passes and the body read is what fails.
            var header = CacheBodyOffset(full);
            Assert.True(full.Length > header, "the saved cache has no body to cut");

            File.WriteAllBytes(path, full[..header]);

            Assert.False(TranslationStore.TryLoadCache(
                path, "key", [descriptor], log, s => s, null, out var cached));
            Assert.Null(cached);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Cache_IsIgnored_WhenTheBodyBytesAreOverwritten()
    {
        // Corruption after the header: the key still matches, but the body holds
        // nonsense. The store must rebuild instead of trusting a broken index.
        var descriptor = TalkSheet((1, "Hello there."));
        var artifacts = Artifacts(null, Artifact("TC", (SheetName, FieldName, 1, "你好。")));
        var store = Load(descriptor, ChineseVariant.Traditional, false, artifacts);
        Assert.NotNull(store);

        var path = Path.Combine(Path.GetTempPath(), $"sinophone-cache-{Guid.NewGuid():N}.bin");
        var log = new PluginLog(new FakePluginLog(), new Configuration(), LogCategory.Data);

        try
        {
            TranslationStore.SaveCache(path, "key", store, log);

            var bytes = File.ReadAllBytes(path);

            // Start at the body, after the magic, the version, and the key.
            var header = CacheBodyOffset(bytes);

            // The body starts with the text-lookup count as a little-endian int32. Claim
            // a count the file cannot possibly hold, so the body read runs past the end
            // and has to fail. Writing zeros instead would read as an empty index and
            // quietly load, which is exactly the kind of silent corruption this guards.
            BitConverter.GetBytes(int.MaxValue).CopyTo(bytes, header);
            File.WriteAllBytes(path, bytes);

            Assert.False(TranslationStore.TryLoadCache(
                path, "key", [descriptor], log, s => s, null, out var cached));
            Assert.Null(cached);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Cache_IsIgnored_WhenACountIsNegative()
    {
        // The reader does not check a count for a negative value. The loop simply does not
        // run, the stream falls out of step, and the next read fails on the wrong bytes.
        // The cache is refused either way, but only by that knock-on failure, not by a
        // check on the count itself. Keep this bounded behaviour if the reader changes.
        var descriptor = TalkSheet((1, "Hello there."));
        var artifacts = Artifacts(null, Artifact("TC", (SheetName, FieldName, 1, "你好。")));
        var store = Load(descriptor, ChineseVariant.Traditional, false, artifacts);
        Assert.NotNull(store);

        var path = Path.Combine(Path.GetTempPath(), $"sinophone-cache-{Guid.NewGuid():N}.bin");
        var log = new PluginLog(new FakePluginLog(), new Configuration(), LogCategory.Data);

        try
        {
            TranslationStore.SaveCache(path, "key", store, log);

            var bytes = File.ReadAllBytes(path);
            BitConverter.GetBytes(-1).CopyTo(bytes, CacheBodyOffset(bytes));
            File.WriteAllBytes(path, bytes);

            Assert.False(TranslationStore.TryLoadCache(
                path, "key", [descriptor], log, s => s, null, out var cached));
            Assert.Null(cached);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Cache_IsIgnored_WhenTheFormatVersionIsNewer()
    {
        // An old build reading a cache written by a newer build must rebuild. Bumping
        // CacheFormatVersion is what makes that happen, so this checks the gate works.
        var descriptor = TalkSheet((1, "Hello there."));
        var artifacts = Artifacts(null, Artifact("TC", (SheetName, FieldName, 1, "你好。")));
        var store = Load(descriptor, ChineseVariant.Traditional, false, artifacts);
        Assert.NotNull(store);

        var path = Path.Combine(Path.GetTempPath(), $"sinophone-cache-{Guid.NewGuid():N}.bin");
        var log = new PluginLog(new FakePluginLog(), new Configuration(), LogCategory.Data);

        try
        {
            TranslationStore.SaveCache(path, "key", store, log);

            var bytes = File.ReadAllBytes(path);

            // The magic is 8 bytes, then a little-endian int32 version. Writing a
            // version no build knows makes the file look stale to every reader.
            var versionOffset = "SNPHIDX1".Length;
            BitConverter.GetBytes(999).CopyTo(bytes, versionOffset);
            File.WriteAllBytes(path, bytes);

            Assert.False(TranslationStore.TryLoadCache(
                path, "key", [descriptor], log, s => s, null, out var cached));
            Assert.Null(cached);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Trace_RecordsTheLayerOrder_WhenTheLineIsMissing()
    {
        var store = Load(
            TalkSheet((1, "Hello there.")),
            ChineseVariant.Traditional,
            false,
            Artifacts(null, Artifact("TC", (SheetName, FieldName, 1, "你好。"))));
        Assert.NotNull(store);

        using var recorder = NewRecorder(out var root);
        try
        {
            var trace = recorder.Begin("talk", nameof(TranslationStoreTests), "Not in the data.");
            Assert.NotNull(trace);

            Assert.False(store.TryGetDialogue("Not in the data.", default, null, out _, trace));
            trace!.Finish(FailureOutcome.Miss, "no-translation");

            Assert.Equal(FailureOutcome.Miss, trace.Case.Outcome);
            Assert.Contains(trace.Case.Steps, step => step is { Stage: "lookup", Result: "enter" });
            Assert.Contains(trace.Case.Steps, step => step is { Stage: "text.exact", Result: "miss" });
            Assert.Contains(trace.Case.Steps, step => step is { Stage: "text.branchNeutral", Result: "miss" });
        }
        finally
        {
            recorder.Dispose();
            DeleteFolder(root);
        }
    }

    [Fact]
    public void Trace_RecordsAnExactHit()
    {
        var store = Load(
            TalkSheet((1, "Hello there.")),
            ChineseVariant.Traditional,
            false,
            Artifacts(null, Artifact("TC", (SheetName, FieldName, 1, "你好。"))));
        Assert.NotNull(store);

        using var recorder = NewRecorder(out var root);
        try
        {
            var trace = recorder.Begin("talk", nameof(TranslationStoreTests), "Hello there.");
            Assert.NotNull(trace);

            Assert.True(store.TryGetDialogue("Hello there.", default, null, out var chinese, trace));
            trace!.Finish(FailureOutcome.Hit);

            Assert.Equal("你好。", chinese);
            Assert.Equal(FailureOutcome.Hit, trace.Case.Outcome);
            Assert.Contains(trace.Case.Steps, step => step is { Stage: "text.exact", Result: "hit" });
        }
        finally
        {
            recorder.Dispose();
            DeleteFolder(root);
        }
    }

    [Fact]
    public void Trace_RecordsARejectedScriptKey()
    {
        var scripts = ScriptsWithKeys(
            ("TEXT_CMNDEFRETAINERBELL_00544_ENTER2_ROEGADYN_FEMALE_TYPE3",
             "Madam?",
             "哼，你難道打算再派給我一些工作嗎？"));

        var artifacts = Artifacts(null, Artifact("TC", scripts));
        var store = TranslationStore.Load([], ChineseVariant.Traditional, false, artifacts, new FakePluginLog());
        Assert.NotNull(store);

        using var recorder = NewRecorder(out var root);
        try
        {
            var context = new DialogueContext(
                ScriptKeys: ["TEXT_CMNDEFRETAINERBELL_00544_ENTER2_ROEGADYN_FEMALE_TYPE3"]);
            var trace = recorder.Begin("talk", nameof(TranslationStoreTests), "Discard the pot of metallic orange dye?");
            Assert.NotNull(trace);

            Assert.False(store.TryGetDialogue(
                "Discard the pot of metallic orange dye?", in context, null, out _, trace));
            trace!.Finish(FailureOutcome.Miss, "no-translation");

            var rejected = Assert.Single(
                trace.Case.Steps,
                step => step.Stage == "id.scriptKey" && step.Result == "rejected");
            Assert.Contains("Madam?", rejected.Detail);
        }
        finally
        {
            recorder.Dispose();
            DeleteFolder(root);
        }
    }

    [Fact]
    public void Trace_RecordsANameMiss()
    {
        var store = Load(
            NameSheet((1, "Skywatcher")),
            ChineseVariant.Traditional,
            false,
            Artifacts(null, Artifact("TC", (NameSheetName, NameFieldName, 1, "觀星者"))));
        Assert.NotNull(store);

        using var recorder = NewRecorder(out var root);
        try
        {
            var trace = recorder.Begin("name", nameof(TranslationStoreTests), "Unknown Name");
            Assert.NotNull(trace);

            Assert.False(store.TryGetName("Unknown Name", out _, trace));
            trace!.Finish(FailureOutcome.Miss, "no-translation");

            Assert.Contains(trace.Case.Steps, step => step is { Stage: "name.exact", Result: "miss" });
        }
        finally
        {
            recorder.Dispose();
            DeleteFolder(root);
        }
    }

    [Fact]
    public void TalkResolve_RecordsTheTextMiss()
    {
        var store = Load(
            TalkSheet((1, "Hello there.")),
            ChineseVariant.Traditional,
            false,
            Artifacts(null, Artifact("TC", (SheetName, FieldName, 1, "你好。"))));
        Assert.NotNull(store);

        using var recorder = NewRecorder(out var root);
        try
        {
            var trace = recorder.Begin("talk", nameof(TalkHandler), "Not in the data.");
            Assert.NotNull(trace);

            var content = TalkHandler.Resolve(
                store,
                new Configuration { HideWhenNoTranslation = true },
                "Not in the data.",
                "Someone",
                trace: trace);

            Assert.Null(content);
            trace!.Finish(FailureOutcome.Miss, "no-translation");
            Assert.Contains(trace.Case.Steps, step => step is { Stage: "talk.text", Result: "miss" });
        }
        finally
        {
            recorder.Dispose();
            DeleteFolder(root);
        }
    }

    [Fact]
    public void ItemResolve_RecordsTheNameMiss()
    {
        var store = Load(
            NameSheet((1, "Fire Shard")),
            ChineseVariant.Traditional,
            false,
            Artifacts(null, Artifact("TC", (NameSheetName, NameFieldName, 1, "火之碎晶"))));
        Assert.NotNull(store);

        using var recorder = NewRecorder(out var root);
        try
        {
            var trace = recorder.Begin("item", nameof(ItemNameHandler), "Unknown item");
            Assert.NotNull(trace);

            Assert.Null(ItemNameHandler.Resolve(store, 1, "Unknown item", trace));
            trace!.Finish(FailureOutcome.Miss, "no-translation");
            Assert.Contains(trace.Case.Steps, step => step is { Stage: "item.name", Result: "miss" });
        }
        finally
        {
            recorder.Dispose();
            DeleteFolder(root);
        }
    }

    private static FailureRecorder NewRecorder(out string root)
    {
        root = Path.Combine(Path.GetTempPath(), "sinophone-trace-" + Guid.NewGuid().ToString("N"));
        return new FailureRecorder(
            root,
            new FailureBuildInfo("test", "tc", "7.4", "en", TranslationStore.IndexFormatVersion));
    }

    private static void DeleteFolder(string root)
    {
        try
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
