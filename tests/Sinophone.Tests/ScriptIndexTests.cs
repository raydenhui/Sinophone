using System.Text;
using Sinophone.Data;

namespace Sinophone.Tests;

public class ScriptIndexTests
{
    private static readonly PluginLog Log =
        new(new FakePluginLog(), new Configuration(), LogCategory.Dialogue);

    [Fact]
    public void AddKey_StoresTheTextKeyRowAndSheetText()
    {
        var index = new ScriptIndex();
        index.AddKey("TEXT_TEST_00001_1", "你好。", "Hello.", "cut_scene/022/VoiceMan_02200", 42);

        Assert.True(index.TryGetText("TEXT_TEST_00001_1", out var text));
        Assert.Equal("你好。", text);
        Assert.True(index.TryGetPairedEnglish("TEXT_TEST_00001_1", out var english));
        Assert.Equal("Hello.", english);
        Assert.True(index.TryGetRowId("TEXT_TEST_00001_1", out var rowId));
        Assert.Equal(42u, rowId);
        Assert.True(index.TryGetRowText("cut_scene/022/VoiceMan_02200", 42, out var rowText));
        Assert.Equal("你好。", rowText);
    }

    [Fact]
    public void AddKey_WithoutASheet_KeepsOnlyTheKey()
    {
        var index = new ScriptIndex();
        index.AddKey("TEXT_TEST_00001_1", "你好。", "Hello.", string.Empty, 0);

        Assert.True(index.TryGetText("TEXT_TEST_00001_1", out _));
        Assert.False(index.TryGetRowId("TEXT_TEST_00001_1", out _));
        Assert.False(index.TryGetRowText(string.Empty, 0, out _));
    }

    [Fact]
    public void AddKey_KeepsTheFirstText_WhenTheOtherVariantRepeatsTheKey()
    {
        var index = new ScriptIndex();
        index.AddKey("TEXT_TEST_00001_1", "你好。", "Hello.", string.Empty, 0);
        index.AddKey("TEXT_TEST_00001_1", "哈囉。", "Hello.", string.Empty, 0);

        Assert.True(index.TryGetText("TEXT_TEST_00001_1", out var text));
        Assert.Equal("你好。", text);
    }

    [Fact]
    public void TryGetTemplate_MatchesABlankGapTemplate()
    {
        var index = new ScriptIndex();
        index.AddTemplate(ScriptTextTemplate.BuildBlankGapTemplate("Give me  the sword."), "給我劍。");

        Assert.True(index.TryGetTemplate("Give me the sword.", out var chinese));
        Assert.Equal("給我劍。", chinese);
        Assert.False(index.TryGetTemplate("Sell me the sword.", out _));
    }

    [Fact]
    public void AmbiguousLookup_PicksTheCandidateTheSpeakerNames()
    {
        var index = AmbiguousPair();

        Assert.True(index.TryLookupAmbiguous(
            "You bastard!", "Jullus", default, false, Log, null, out var chinese));
        Assert.Equal("你這個混蛋！", chinese);
    }

    [Fact]
    public void AmbiguousLookup_StaysEnglish_WhenNoHintPicksALine()
    {
        var index = new ScriptIndex();
        index.AddCandidate("You bastard!", "TEXT_TEST_00001_JULLUS", "你這個混蛋！");
        index.AddCandidate("You bastard!", "TEXT_TEST_00002_THANCRED", "你這個混帳！");

        Assert.False(index.TryLookupAmbiguous(
            "You bastard!", null, default, false, Log, null, out _));
    }

    [Fact]
    public void AmbiguousLookup_PicksTheCutsceneThatIsPlaying()
    {
        var index = new ScriptIndex();
        index.AddCandidate("You bastard!", "TEXT_VOICEMAN_06005_000910_JULLUS", "你這個混蛋！");
        index.AddCandidate("You bastard!", "TEXT_VOICEMAN_06006_000910_JULLUS", "你這個混帳！");

        Assert.True(index.TryLookupAmbiguous(
            "You bastard!",
            null,
            new DialogueContext(CutsceneId: 6006),
            false,
            Log,
            null,
            out var chinese));
        Assert.Equal("你這個混帳！", chinese);
    }

    [Fact]
    public void AmbiguousLookup_AnswersWhenEveryLineIsFromThePlayingCutscene()
    {
        var index = new ScriptIndex();
        index.AddCandidate("You bastard!", "TEXT_VOICEMAN_06005_000910_JULLUS", "你這個混蛋！");
        index.AddCandidate("You bastard!", "TEXT_VOICEMAN_06005_000911_JULLUS", "你這個混帳！");

        Assert.True(index.TryLookupAmbiguous(
            "You bastard!", null, default, false, Log, null, out var chinese));
        Assert.Equal("你這個混蛋！", chinese);
    }

    [Fact]
    public void AmbiguousLookup_AnswersWhenTheLinesDifferOnlyInPunctuation()
    {
        var index = new ScriptIndex();
        index.AddCandidate("You bastard!", "TEXT_VOICEMAN_06005_000910_JULLUS", "你這個混蛋！");
        index.AddCandidate("You bastard!", "TEXT_VOICEMAN_06006_000910_THANCRED", "你這個混蛋。");

        Assert.True(index.TryLookupAmbiguous(
            "You bastard!", null, default, false, Log, null, out var chinese));
        Assert.Equal("你這個混蛋！", chinese);
    }

    [Fact]
    public void AmbiguousLookup_AnswersAsoundOnlyLine()
    {
        var index = new ScriptIndex();
        index.AddCandidate("<squeal> <squeal>", "TEXT_TEST_00001_A", "尖叫！");
        index.AddCandidate("<squeal> <squeal>", "TEXT_TEST_00002_B", "尖叫。");

        Assert.True(index.TryLookupAmbiguous(
            "<squeal> <squeal>", null, default, false, Log, null, out var chinese));
        Assert.Equal("尖叫！", chinese);
    }

    [Theory]
    [InlineData(false, "解鎖")]
    [InlineData(true, "普通")]
    public void AmbiguousLookup_PicksTheChocoboGreetingForTheStandState(bool unlocked, string expected)
    {
        var index = new ScriptIndex();
        index.AddCandidate(
            "Welcome to the chocobo stand.",
            "TEXT_CHOCOBOTAXISTAND_00000_CHOCOBOTAXI_UNLOCK_01",
            "解鎖");
        index.AddCandidate(
            "Welcome to the chocobo stand.",
            "TEXT_CHOCOBOTAXISTAND_00001_CHOCOBOTAXI_01",
            "普通");

        Assert.True(index.TryLookupAmbiguous(
            "Welcome to the chocobo stand.",
            null,
            new DialogueContext(ChocoboTaxiStandUnlocked: unlocked),
            false,
            Log,
            null,
            out var chinese));
        Assert.Equal(expected, chinese);
    }

    [Fact]
    public void HasChocoboTaxiStandVariant_IsTrueOnlyForAStandGreeting()
    {
        var index = new ScriptIndex();
        index.AddCandidate(
            "Welcome to the chocobo stand.",
            "TEXT_CHOCOBOTAXISTAND_00000_CHOCOBOTAXI_UNLOCK_01",
            "解鎖");
        index.AddCandidate(
            "Welcome to the chocobo stand.",
            "TEXT_CHOCOBOTAXISTAND_00001_CHOCOBOTAXI_01",
            "普通");
        index.AddCandidate("We must hurry.", "TEXT_A_00001_ALDIS_000_010", "走。");
        index.AddCandidate("We must hurry.", "TEXT_B_00001_ALDIS_000_010", "快走。");

        Assert.True(index.HasChocoboTaxiStandVariant("Welcome to the chocobo stand."));
        Assert.False(index.HasChocoboTaxiStandVariant("We must hurry."));
        Assert.False(index.HasChocoboTaxiStandVariant("Not in the data."));
    }

    [Fact]
    public void AmbiguousLookup_PicksTheEventScriptFamilyTheDialogNames()
    {
        var index = new ScriptIndex();
        index.AddCandidate("We must hurry.", "TEXT_AKTKMG113_04462_ALDIS_000_010", "我們得走了。");
        index.AddCandidate("We must hurry.", "TEXT_OTHER_00001_ALDIS_000_010", "動作快。");

        Assert.True(index.TryLookupAmbiguous(
            "We must hurry.",
            null,
            new DialogueContext(ScriptKeyHints: ["AKTKMG113_04462"]),
            false,
            Log,
            null,
            out var chinese));
        Assert.Equal("我們得走了。", chinese);
    }

    [Fact]
    public void AddCandidate_IgnoresTheSameDumpKeyTwice()
    {
        var index = new ScriptIndex();
        index.AddCandidate("You bastard!", "TEXT_VOICEMAN_06005_000910_JULLUS", "你這個混蛋！");
        index.AddCandidate("You bastard!", "TEXT_VOICEMAN_06005_000910_JULLUS", "別的翻譯");
        index.AddCandidate("You bastard!", "TEXT_VOICEMAN_06006_000910_THANCRED", "你這個混帳！");

        Assert.True(index.TryLookupAmbiguous(
            "You bastard!", "Jullus", default, false, Log, null, out var chinese));
        Assert.Equal("你這個混蛋！", chinese);
    }

    [Fact]
    public void PromoteToAmbiguous_KeepsTheFirstLineBesideTheNewOne()
    {
        var index = new ScriptIndex();
        var first = ScriptIndex.CreateCandidate("甲", "TEXT_VOICEMAN_06005_000910_JULLUS");
        index.PromoteToAmbiguous("You bastard!", first, "TEXT_VOICEMAN_06006_000910_THANCRED", "乙");

        Assert.True(index.HasAmbiguous("You bastard!"));
        Assert.Equal(1, index.AmbiguousKeyCount);
        Assert.True(index.TryLookupAmbiguous(
            "You bastard!", "Thancred", default, false, Log, null, out var chinese));
        Assert.Equal("乙", chinese);
    }

    [Fact]
    public void NoteScene_KeepsASceneEveryKeyAgreesOn()
    {
        var scenes = new Dictionary<string, uint>(StringComparer.Ordinal);
        var conflicts = new HashSet<string>(StringComparer.Ordinal);
        ScriptIndex.NoteScene(scenes, conflicts, "Line", "TEXT_VOICEMAN_06005_000910_JULLUS");
        ScriptIndex.NoteScene(scenes, conflicts, "Line", "TEXT_VOICEMAN_06005_000911_THANCRED");

        var index = new ScriptIndex();
        index.MergeScenes(scenes, conflicts);

        Assert.True(index.TryGetCutsceneId("Line", out var scene));
        Assert.Equal(6005u, scene);
    }

    [Fact]
    public void NoteScene_DropsALineTwoCutscenesHold()
    {
        var scenes = new Dictionary<string, uint>(StringComparer.Ordinal);
        var conflicts = new HashSet<string>(StringComparer.Ordinal);
        ScriptIndex.NoteScene(scenes, conflicts, "Line", "TEXT_VOICEMAN_06005_000910_JULLUS");
        ScriptIndex.NoteScene(scenes, conflicts, "Line", "TEXT_VOICEMAN_06006_000910_JULLUS");

        Assert.Contains("Line", conflicts);

        var index = new ScriptIndex();
        index.MergeScenes(scenes, conflicts);
        Assert.False(index.TryGetCutsceneId("Line", out _));
    }

    [Fact]
    public void MergeScenes_DropsALineAKeyOutsideACutsceneAlsoHolds()
    {
        var scenes = new Dictionary<string, uint>(StringComparer.Ordinal)
        {
            ["Line"] = 0,
        };

        var index = new ScriptIndex();
        index.MergeScenes(scenes, new HashSet<string>(StringComparer.Ordinal));

        Assert.False(index.TryGetCutsceneId("Line", out _));
    }

    [Fact]
    public void MergeScenes_DropsASceneTheOtherArtifactDisagreesWith()
    {
        var index = new ScriptIndex();
        index.MergeScenes(
            new Dictionary<string, uint>(StringComparer.Ordinal) { ["Line"] = 6005 },
            new HashSet<string>(StringComparer.Ordinal));

        index.MergeScenes(
            new Dictionary<string, uint>(StringComparer.Ordinal) { ["Line"] = 6006 },
            new HashSet<string>(StringComparer.Ordinal));

        Assert.False(index.TryGetCutsceneId("Line", out _));
    }

    [Theory]
    [InlineData("TEXT_VOICEMAN_06005_003840_THANCRED", 6005u)]
    [InlineData("text_voiceman_06005_003840_thancred", 6005u)]
    [InlineData("TEXT_VOICEMAN_6005_X", 6005u)]
    [InlineData("TEXT_JOBPLD650_02573_ALDIS_000_052", 0u)]
    [InlineData("TEXT_VOICEMAN_", 0u)]
    [InlineData("TEXT_VOICEMAN_NOT_A_NUMBER_X", 0u)]
    [InlineData("TEXT_VOICEMAN_06005", 0u)]
    [InlineData(null, 0u)]
    public void CutsceneIdOf_ReadsTheNumberFromAVoiceManKey(string? key, uint expected)
        => Assert.Equal(expected, ScriptIndex.CutsceneIdOf(key));

    [Theory]
    [InlineData("TEXT_REGUNI5BASEOMICRON_00760_A1_000_001", "REGUNI5BASEOMICRON_00760")]
    [InlineData("REGUNI5BASEOMICRON_00760", "REGUNI5BASEOMICRON_00760")]
    [InlineData("cut_scene/022/VoiceMan_02200", "VoiceMan_02200")]
    [InlineData("TEXT_X.2", "X")]
    [InlineData("TEXT_AKTKMG113_04462_ALDIS_000_010", "AKTKMG113_04462")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void ScriptFamilyOf_DropsTheLineSuffix(string? key, string expected)
        => Assert.Equal(expected, ScriptIndex.ScriptFamilyOf(key));

    [Fact]
    public void CacheRoundTrip_RestoresEveryCollection()
    {
        var index = new ScriptIndex();
        index.AddKey("TEXT_VOICEMAN_06005_000910_JULLUS", "你這個混蛋！", "You bastard!",
            "cut_scene/060/VoiceMan_06005", 910);
        index.AddTemplate(ScriptTextTemplate.BuildBlankGapTemplate("Give me  the sword."), "給我劍。");
        index.AddCandidate("You bastard!", "TEXT_VOICEMAN_06005_000910_JULLUS", "你這個混蛋！");
        index.AddCandidate("You bastard!", "TEXT_VOICEMAN_06006_000910_JULLUS", "你這個混帳！");
        index.MergeScenes(
            new Dictionary<string, uint>(StringComparer.Ordinal) { ["You bastard!"] = 6005 },
            new HashSet<string>(StringComparer.Ordinal));

        var copy = RoundTrip(index);

        Assert.Equal(1, copy.AmbiguousKeyCount);
        Assert.True(copy.TryGetAmbiguous("You bastard!", out var candidates));
        Assert.Equal(2, candidates.Count);
        Assert.True(copy.TryGetText("TEXT_VOICEMAN_06005_000910_JULLUS", out var text));
        Assert.Equal("你這個混蛋！", text);
        Assert.True(copy.TryGetRowId("TEXT_VOICEMAN_06005_000910_JULLUS", out var rowId));
        Assert.Equal(910u, rowId);
        Assert.True(copy.TryGetRowText("cut_scene/060/VoiceMan_06005", 910, out var rowText));
        Assert.Equal("你這個混蛋！", rowText);
        Assert.True(copy.TryGetCutsceneId("You bastard!", out var scene));
        Assert.Equal(6005u, scene);
        Assert.True(copy.TryGetTemplate("Give me the sword.", out var template));
        Assert.Equal("給我劍。", template);

        Assert.True(copy.TryLookupAmbiguous(
            "You bastard!",
            "Jullus",
            new DialogueContext(CutsceneId: 6005),
            false,
            Log,
            null,
            out var chinese));
        Assert.Equal("你這個混蛋！", chinese);
    }

    private static ScriptIndex AmbiguousPair()
    {
        var index = new ScriptIndex();
        index.AddCandidate("You bastard!", "TEXT_VOICEMAN_06005_000910_JULLUS", "你這個混蛋！");
        index.AddCandidate("You bastard!", "TEXT_VOICEMAN_06005_000911_THANCRED", "你這個混帳！");
        return index;
    }

    private static ScriptIndex RoundTrip(ScriptIndex index)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            index.WriteTo(writer);

        stream.Position = 0;

        var copy = new ScriptIndex();
        using (var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true))
            copy.ReadFrom(reader);

        return copy;
    }
}
