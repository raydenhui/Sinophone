using System.IO.Compression;
using System.Text;

using Sinophone.Data;
using Sinophone.Handlers;

using GameNpcYell = FFXIVClientStructs.FFXIV.Client.Game.UI.NpcYell;

namespace Sinophone.IntegrationTests;

/// <summary>
/// Tests the decision BattleTalkHandler makes for one line, wired to the real fixture store. The
/// hooks and the game's own box need a live game, so they are covered by
/// docs/native-mode-checklist.md instead.
/// </summary>
public class BattleTalkHandlerTests
{
    private static readonly Configuration Enabled = new() { ShowInBattleTalk = true };

    private static FixtureData.FixtureRow Row(string sheet)
        => FixtureData.Current.Rows.First(r => r.Sheet == sheet);

    private static FixtureData.FixtureRow Row(string sheet, uint rowId)
        => FixtureData.Current.Rows.First(r => r.Sheet == sheet && r.RowId == rowId);

    private static string Tc(FixtureData.FixtureRow row)
        => MacroNormalizer.CleanForDisplay(row.Tc);

    [Fact]
    public void Resolve_AnswersAnNpcYellByItsRow()
    {
        var store = FixtureStore.Load(ChineseVariant.Traditional);
        var row = Row("NpcYell", 474);
        var context = new DialogueContext(new DialogueRowId("NpcYell", row.RowId, "Text"));

        var content = BattleTalkHandler.Resolve(store, Enabled, row.English, "Chocobokeep", context);

        Assert.NotNull(content);
        Assert.Equal(Tc(row), content.Value.ChineseText);
    }

    [Fact]
    public void Resolve_AnswersDungeonNarrationByItsText()
    {
        // Dungeon narration has no yell row, so the text is all there is.
        var store = FixtureStore.Load(ChineseVariant.Traditional);
        var row = FixtureData.Current.Rows.First(r => r.Sheet == "InstanceContentTextData" && r.RowId == 2);

        var content = BattleTalkHandler.Resolve(store, Enabled, row.English, null);

        Assert.NotNull(content);
        Assert.Equal(Tc(row), content.Value.ChineseText);
    }

    [Fact]
    public void Resolve_FallsBackToTheText_WhenTheRowDoesNotMatch()
    {
        var store = FixtureStore.Load(ChineseVariant.Traditional);
        var row = Row("NpcYell");
        var context = new DialogueContext(new DialogueRowId("NpcYell", 999999, "Text"));

        var content = BattleTalkHandler.Resolve(store, Enabled, row.English, null, context);

        Assert.NotNull(content);
        Assert.Equal(Tc(row), content.Value.ChineseText);
    }

    [Fact]
    public void Resolve_TranslatesTheSpeakerAliasFromAScriptLine()
    {
        // Battle talk can pass the dump's alias with the line. The script line also names the
        // speaker, so the game's English name is translated from the same alias.
        var json = """
            {"FormatVersion":1,"Language":"TC","GeneratedAt":"test","GameVersion":"test",
             "Sheets":{},"Scripts":{
               "TEXT_AKTKMG114_04463_PASSER_000_090":{
                 "English":"(-Voice from the Past-)If you need a push, I'll be right there behind you.",
                 "Text":"(-劃過耳畔的聲音-)如果你決定前進，就由我來支援你。"}}}
            """;
        var store = TranslationStore.Load(
            [], ChineseVariant.Traditional, false, _ => ToGzipStream(json), new FakePluginLog());

        Assert.NotNull(store);
        // Battle talk can put the player's name in the name field; the dump alias wins.
        var content = BattleTalkHandler.Resolve(
            store,
            Enabled,
            "(-Voice from the Past-)If you need a push, I'll be right there behind you.",
            "Nedyar Sol");

        Assert.NotNull(content);
        Assert.Equal("如果你決定前進，就由我來支援你。", content.Value.ChineseText);
        Assert.Equal("劃過耳畔的聲音 (Voice from the Past)", content.Value.ChineseName);
    }

    [Fact]
    public void Resolve_UsesTheLineAlias_WhenTheAliasIsShared()
    {
        // "All" is used by several groups. The line's own alias must win over the global name.
        var json = """
            {"FormatVersion":1,"Language":"TC","GeneratedAt":"test","GameVersion":"test",
             "Sheets":{},"Scripts":{
               "TEXT_FESEST103_00559_MOTTE_000_352":{
                 "English":"(-All-)Happy birthday!!","Text":"(-宴會的來賓們-)生日快樂！！"},
               "TEXT_AKTKMG115_04464_SCIONS_000_022":{
                 "English":"(-All-)Aye!","Text":"(-拂曉的同伴們-)好！"}}}
            """;
        var store = TranslationStore.Load(
            [], ChineseVariant.Traditional, false, _ => ToGzipStream(json), new FakePluginLog());

        Assert.NotNull(store);
        var content = BattleTalkHandler.Resolve(store, Enabled, "(-All-)Aye!", "All");

        Assert.NotNull(content);
        Assert.Equal("好！", content.Value.ChineseText);
        Assert.Equal("拂曉的同伴們 (All)", content.Value.ChineseName);
    }


    [Fact]
    public void Resolve_ReturnsNull_WhenTheSettingIsOff()
    {
        var store = FixtureStore.Load(ChineseVariant.Traditional);
        var row = Row("NpcYell");

        Assert.Null(BattleTalkHandler.Resolve(store, new Configuration(), row.English, null));
    }

    [Fact]
    public void Resolve_ReturnsNull_ForALineWithNoTranslation()
    {
        var store = FixtureStore.Load(ChineseVariant.Traditional);

        Assert.Null(BattleTalkHandler.Resolve(
            store, Enabled, "A line that exists in no dump at all.", null));
    }

    [Fact]
    public void Resolve_SubstitutesTheYellParameters()
    {
        // The row reads 打倒<Sheet(BNpcName,IntegerParameter(1),0)/>, and the game passes the
        // target's row id in the first message parameter.
        var store = FixtureStore.Load(ChineseVariant.Traditional);
        var row = Row("NpcYell", 2405);

        var content = BattleTalkHandler.Resolve(store, Enabled, row.English, null, default, [77]);

        Assert.NotNull(content);
        Assert.Contains("77", content.Value.ChineseText);
    }

    [Fact]
    public void Resolve_ShowsTheChineseNameWithTheEnglishInBrackets()
    {
        var store = FixtureStore.Load(ChineseVariant.Traditional);
        var name = Row("ENpcResident");

        var content = BattleTalkHandler.Resolve(store, Enabled, Row("NpcYell").English, name.English);

        Assert.NotNull(content);
        Assert.Contains(name.Tc, content.Value.ChineseName);
        Assert.Contains(name.English, content.Value.ChineseName);
    }

    [Fact]
    public void Resolve_LeavesTheNameOff_WhenTheSpeakerNameSettingIsOff()
    {
        var store = FixtureStore.Load(ChineseVariant.Traditional);
        var configuration = new Configuration { ShowInBattleTalk = true, ShowSpeakerName = false };

        var content = BattleTalkHandler.Resolve(store, configuration, Row("NpcYell").English, "Chocobokeep");

        Assert.NotNull(content);
        Assert.Equal(string.Empty, content.Value.ChineseName);
    }

    [Theory]
    [InlineData(GameNpcYell.NpcYellOutputFlags.ShowBattleTalk, true)]
    [InlineData(GameNpcYell.NpcYellOutputFlags.PrintToLog | GameNpcYell.NpcYellOutputFlags.ShowBattleTalk, true)]
    [InlineData(GameNpcYell.NpcYellOutputFlags.ShowBalloon, false)]
    [InlineData(GameNpcYell.NpcYellOutputFlags.PrintToLog, false)]
    [InlineData(GameNpcYell.NpcYellOutputFlags.None, false)]
    public void ShowsAsBattleTalk_FollowsTheOutputFlags(GameNpcYell.NpcYellOutputFlags flags, bool expected)
        => Assert.Equal(expected, BattleTalkHandler.ShowsAsBattleTalk(flags));

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
}
