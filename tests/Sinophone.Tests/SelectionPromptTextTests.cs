using System.IO.Compression;
using System.Text;
using Sinophone.Data;
using Sinophone.Handlers;

namespace Sinophone.Tests;

public class SelectionPromptTextTests
{
    [Fact]
    public void TryTranslate_UsesDialogueLookup_WhenTextMatches()
    {
        var store = LoadStore("""
            {"FormatVersion":1,"Language":"TC","GeneratedAt":"test","GameVersion":"test",
             "Sheets":{"Addon":{"3":{"Text":["是"]},"4":{"Text":["否"]}}}}
            """);

        Assert.True(SelectionPromptText.TryTranslate(store, "Yes.", out var chinese));
        Assert.Equal("是", chinese);
    }

    [Fact]
    public void TryTranslate_TriesShortLabelWithPeriod_WhenButtonOmitsIt()
    {
        var store = LoadStore("""
            {"FormatVersion":1,"Language":"TC","GeneratedAt":"test","GameVersion":"test",
             "Sheets":{"Addon":{"3":{"Text":["是"]},"4":{"Text":["否"]}}}}
            """);

        Assert.True(SelectionPromptText.TryTranslate(store, "Yes", out var chinese));
        Assert.Equal("是", chinese);
    }

    [Fact]
    public void TryTranslate_TrimsTrailingPunctuation_WhenStoredTextHasNone()
    {
        var store = LoadStore("""
            {"FormatVersion":1,"Language":"TC","GeneratedAt":"test","GameVersion":"test",
             "Sheets":{"Addon":{"1":{"Text":["確定"]}}}}
            """);

        Assert.True(SelectionPromptText.TryTranslate(store, "OK.", out var chinese));
        Assert.Equal("確定", chinese);
    }

    [Fact]
    public void DialogCutscene_NamesTheScene_FromAnUnambiguousOption()
    {
        // One option is unique to cutscene 6006. The other is shared, so the scene from the
        // first option has to pick the right line for the second.
        var json = """
            {"FormatVersion":1,"Language":"TC","GeneratedAt":"test","GameVersion":"test",
             "Sheets":{},"Scripts":{
               "TEXT_VOICEMAN_06006_A6_000_001_NONE_VOICE":{"English":"Meteion...","Text":"梅蒂恩！"},
               "TEXT_VOICEMAN_06006_A6_000_002_NONE_VOICE":{"English":"It's her...the bringer of despair.","Text":"她就是絕望的載體"},
               "TEXT_VOICEMAN_06004_006510_HERMES":{"English":"Meteion...","Text":"梅蒂恩……我……"},
               "TEXT_AKTKMF102_04440_GRAHATIA_000_049":{"English":"Meteion...","Text":"梅蒂恩……"}}}
            """;

        var store = TranslationStore.Load(
            [], ChineseVariant.Traditional, false, _ => ToGzipStream(json), new FakePluginLog());
        Assert.NotNull(store);

        var scene = SelectionPromptText.DialogCutscene(
            store, ["Meteion...", "It's her...the bringer of despair."]);

        Assert.Equal(6006u, scene);
        Assert.True(SelectionPromptText.TryTranslate(
            store, "Meteion...", new DialogueContext(CutsceneId: scene), out var chinese));
        Assert.Equal("梅蒂恩！", chinese);
    }

    [Fact]
    public void TryTranslate_TrimRetryKeepsTheCutscene()
    {
        // Two scenes share "Meteion". The live option carries a trailing period, so only the trim
        // retry can answer it, and that retry must still see the scene.
        var json = """
            {"FormatVersion":1,"Language":"TC","GeneratedAt":"test","GameVersion":"test",
             "Sheets":{},"Scripts":{
               "TEXT_VOICEMAN_06006_A6_000_001_NONE_VOICE":{"English":"Meteion","Text":"梅蒂恩！"},
               "TEXT_VOICEMAN_06004_006510_HERMES":{"English":"Meteion","Text":"梅蒂恩……我……"}}}
            """;

        var store = TranslationStore.Load(
            [], ChineseVariant.Traditional, false, _ => ToGzipStream(json), new FakePluginLog());
        Assert.NotNull(store);

        Assert.True(SelectionPromptText.TryTranslate(
            store, "Meteion...", new DialogueContext(CutsceneId: 6006), out var chinese));
        Assert.Equal("梅蒂恩！", chinese);

        Assert.False(SelectionPromptText.TryTranslate(store, "Meteion...", out _));
    }

    [Fact]
    public void ResolveAddonText_FillsItemNameAndAmountForTheMultipleItemPrompt()
    {
        // Addon row 11194. The English client draws the amount and the item name
        // with separate macros, so the template captures both.
        var descriptors = new ISheetDescriptor[]
        {
            new FakeSheetDescriptor("Item", "Name", true, new Dictionary<uint, string[]>
            {
                [1] = ["MGC"],
            }),
        };

        var json = """
            {"FormatVersion":1,"Language":"CN","GeneratedAt":"test","GameVersion":"test",
             "Sheets":{"Item":{"1":{"Name":["金碟点"]}}}}
            """;

        var store = TranslationStore.Load(
            descriptors,
            ChineseVariant.Simplified,
            false,
            _ => ToGzipStream(json),
            new FakePluginLog());
        Assert.NotNull(store);

        var resolved = SelectionPromptText.ResolveAddonText(
            "确定要用<Sheet(Item,IntegerParameter(1),0)/>×<Format(IntegerParameter(2),FF022C)/>，\r\n换取以下道具吗？",
            "Exchange 1,820 MGC for the following item?",
            ["1,820", "MGC"], store, [2, -1]);

        Assert.Equal("确定要用金碟点×1,820，\n换取以下道具吗？", resolved);
    }

    [Fact]
    public void TryTranslate_LeavesUnknownTextAlone()
    {
        var store = LoadStore("""
            {"FormatVersion":1,"Language":"TC","GeneratedAt":"test","GameVersion":"test",
             "Sheets":{"Addon":{"3":{"Text":["是"]}}}}
            """);

        Assert.False(SelectionPromptText.TryTranslate(store, "A line that is not in the sheet.", out var chinese));
        Assert.Empty(chinese);
    }

    [Fact]
    public void ResolveAddonText_FillsThePriceOfAPaymentPrompt()
    {
        // A script line found by its text has no captures, so the price comes from the English.
        const string chinese = "確定要支付<Format(IntegerParameter(1),FF022C)/>Gil嗎？";

        var resolved = SelectionPromptText.ResolveAddonText(chinese, "Pay 2,000 gil?");

        Assert.Equal("確定要支付2,000Gil嗎？", resolved);
    }

    [Fact]
    public void ResolveAddonText_LeavesThePromptEnglish_WhenThePriceIsMissing()
    {
        const string chinese = "確定要支付<Format(IntegerParameter(1),FF022C)/>Gil嗎？";

        Assert.Empty(SelectionPromptText.ResolveAddonText(chinese, "Pay gil?"));
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

        var resolved = SelectionPromptText.ResolveAddonText(chinese, live);

        Assert.Contains("Inform the Cold?", resolved);
        Assert.Contains("等级：90", resolved);
        Assert.Contains("品级：665", resolved);
        Assert.DoesNotContain("StringParameter", resolved);
        Assert.DoesNotContain("IntegerParameter", resolved);
    }

    [Fact]
    public void ResolveAddonText_ReturnsEmpty_WhenParameterIsStillMissing()
    {
        const string chinese = "「」×IntegerParameter(2)需要提交軍票。";

        Assert.Empty(SelectionPromptText.ResolveAddonText(chinese, "Exchange 430 MGC for the following item?"));
    }

    [Fact]
    public void ResolveAddonText_FillsItemNamesAcrossLineBreaks()
    {
        const string chinese =
            "确定要将<Sheet(Item,IntegerParameter(2),0)/>种植在<Sheet(Item,IntegerParameter(1),0)/>中吗？";
        const string live =
            "Prepare the bed with a bag of grade 3 Thanalan '+n+'topsoil and a bag of voidrake seeds?";

        var resolved = SelectionPromptText.ResolveAddonText(chinese, live);

        Assert.Contains("bag of voidrake seeds", resolved);
        Assert.Contains("bag of grade 3 Thanalan", resolved);
        Assert.Contains("topsoil", resolved);
        Assert.DoesNotContain("Sheet(Item", resolved);
        Assert.DoesNotContain("a bag", resolved);
        Assert.DoesNotContain("\n", resolved);
    }

    [Fact]
    public void ResolveAddonText_FillsFormattedValueFromCapture()
    {
        // Addon row 2379. The game draws the gil amount inside the Format macro,
        // so the value has to replace the whole tag, not just its expression.
        const string chinese = "金币管理　[现保管金币：<Format(IntegerParameter(1),FF022C)/>]";
        const string live = "Entrust or withdraw gil. (Gil entrusted: 1,234)";

        var resolved = SelectionPromptText.ResolveAddonText(
            chinese, live, ["1,234"], parameterIndices: [1]);

        Assert.Equal("金币管理　[现保管金币：1,234]", resolved);
    }

    [Fact]
    public void ResolveAddonText_FillsFormattedValueFromNumbers_WhenNoCapture()
    {
        var resolved = SelectionPromptText.ResolveAddonText(
            "所持Gil：<Format(IntegerParameter(1),FF022C)/>", "Current Gil: 42");

        Assert.Equal("所持Gil：42", resolved);
    }

    [Fact]
    public void ResolveAddonText_ReturnsEmpty_WhenFormattedValueIsMissing()
    {
        Assert.Empty(SelectionPromptText.ResolveAddonText(
            "<Format(IntegerParameter(2),FF022C)/>", "No values here."));
    }

    [Fact]
    public void ResolveAddonText_FillsDateAndClockFromLiveLine()
    {
        // Addon row 2384. The SetTime macro fills the date and clock values but
        // draws nothing, so the values have to come from the English line.
        var resolved = SelectionPromptText.ResolveAddonText(
            VenturePrompt, "View venture report. (Complete on 8/27 8:00)",
            now: new DateTime(2026, 8, 20));

        Assert.Equal("查看雇员探险情况　[～2026/8/27 8:00]", resolved);
    }

    [Fact]
    public void ResolveAddonText_ReadsDayFirstDate()
    {
        var resolved = SelectionPromptText.ResolveAddonText(
            VenturePrompt, "View venture report. (Complete on 27/8 8:00)",
            now: new DateTime(2026, 8, 20));

        Assert.Equal("查看雇员探险情况　[～2026/8/27 8:00]", resolved);
    }

    [Fact]
    public void ResolveAddonText_UsesNextYear_WhenTheDateHasPassed()
    {
        var resolved = SelectionPromptText.ResolveAddonText(
            VenturePrompt, "View venture report. (Complete on 8/27 8:00)",
            now: new DateTime(2026, 11, 7));

        Assert.Equal("查看雇员探险情况　[～2027/8/27 8:00]", resolved);
    }

    [Fact]
    public void ResolveAddonText_ReadsAfternoonClock()
    {
        const string chinese =
            "<Time(IntegerParameter(1))/><Value>TopLevelParameter(218)</Value>:" +
            "<TwoDigitValue>TopLevelParameter(217)</TwoDigitValue>";

        Assert.Equal("20:00", SelectionPromptText.ResolveAddonText(chinese, "8:00 p.m."));
    }

    [Fact]
    public void ResolveAddonText_ReturnsEmpty_WhenTheDateIsMissing()
    {
        Assert.Empty(SelectionPromptText.ResolveAddonText(VenturePrompt, "View venture report."));
    }

    [Fact]
    public void ResolveAddonText_FillsSheetNameFromTheEnglishLine()
    {
        // Addon row 2390. The row names the retainer's class through the ClassJob
        // sheet, and the English line shows the name the game drew.
        var store = LoadStore("""
            {"FormatVersion":1,"Language":"TC","GeneratedAt":"test","GameVersion":"test",
             "Sheets":{"Addon":{"5":{"Text":["刻木匠"]}}}}
            """);

        var resolved = SelectionPromptText.ResolveAddonText(
            "设置雇员职业　[<Value>IntegerParameter(2)</Value>级<Sheet(ClassJob,IntegerParameter(1),0)/>]",
            "Reset retainer class. (Carpenter: Lv. 50)",
            ["Carpenter", "50"], store, [-1, 2]);

        Assert.Equal("设置雇员职业　[50级刻木匠]", resolved);
    }

    [Fact]
    public void ResolveAddonText_ReturnsEmpty_WhenTheSheetNameIsUnknown()
    {
        var store = LoadStore("""
            {"FormatVersion":1,"Language":"TC","GeneratedAt":"test","GameVersion":"test",
             "Sheets":{"Addon":{"3":{"Text":["是"]}}}}
            """);

        Assert.Empty(SelectionPromptText.ResolveAddonText(
            "设置雇员职业　[<Value>IntegerParameter(2)</Value>级<Sheet(ClassJob,IntegerParameter(1),0)/>]",
            "Reset retainer class. (Carpenter: Lv. 50)",
            ["Carpenter", "50"], store, [-1, 2]));
    }

    [Fact]
    public void FitPromptLines_JoinsTheLines_WhenTheJoinedLineFitsTheEnglishBox()
    {
        // Addon row 11194. The English prompt is one line, so the dialog is laid out
        // for one line and the Chinese break would push the second line over the icon.
        var fitted = SelectionPromptText.FitPromptLines(
            "Exchange 1,820 MGC for the following item?",
            "確定要用金碟點×1,820，\n換取以下道具嗎？",
            344,
            18,
            _ => ((ushort)330, (ushort)18));

        Assert.Equal("確定要用金碟點×1,820，換取以下道具嗎？", fitted);
    }

    [Fact]
    public void FitPromptLines_JoinsTheLines_WhenTheTextUsesACarriageReturn()
    {
        var fitted = SelectionPromptText.FitPromptLines(
            "Exchange 1,820 MGC for the following item?",
            "確定要用金碟點×1,820，\r\n換取以下道具嗎？",
            344,
            18,
            _ => ((ushort)330, (ushort)18));

        Assert.Equal("確定要用金碟點×1,820，換取以下道具嗎？", fitted);
    }

    [Fact]
    public void FitPromptLines_KeepsTheLines_WhenTheJoinedLineIsWiderThanTheBox()
    {
        var chinese = "確定要用金碟點×1,820，\n換取以下道具嗎？";

        var fitted = SelectionPromptText.FitPromptLines(
            "Exchange 1,820 MGC for the following item?",
            chinese,
            344,
            18,
            _ => ((ushort)400, (ushort)18));

        Assert.Equal(chinese, fitted);
    }

    [Fact]
    public void FitPromptLines_KeepsTheLines_WhenTheJoinedLineWouldWrap()
    {
        var chinese = "確定要用金碟點×1,820，\n換取以下道具嗎？";

        var fitted = SelectionPromptText.FitPromptLines(
            "Exchange 1,820 MGC for the following item?",
            chinese,
            344,
            18,
            _ => ((ushort)340, (ushort)36));

        Assert.Equal(chinese, fitted);
    }

    [Fact]
    public void FitPromptLines_KeepsTheLines_WhenTheEnglishHasAsManyLines()
    {
        var chinese = "第一行\n第二行";

        var fitted = SelectionPromptText.FitPromptLines(
            "First line\nSecond line",
            chinese,
            344,
            36,
            _ => ((ushort)200, (ushort)36));

        Assert.Equal(chinese, fitted);
    }

    private const string VenturePrompt =
        "查看雇员探险情况　[～<Time(IntegerParameter(1))/>" +
        "<Value>TopLevelParameter(222)</Value>/<Value>TopLevelParameter(221)</Value>/" +
        "<Value>TopLevelParameter(219)</Value> <Value>TopLevelParameter(218)</Value>:" +
        "<TwoDigitValue>TopLevelParameter(217)</TwoDigitValue>]";

    private static TranslationStore LoadStore(string json)
    {
        var descriptor = new FakeSheetDescriptor("Addon", "Text", false,
            new Dictionary<uint, string[]>
            {
                [1] = ["OK"],
                [3] = ["Yes."],
                [4] = ["No."],
                [5] = ["Carpenter"],
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
