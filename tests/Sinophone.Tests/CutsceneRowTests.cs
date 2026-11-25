using Sinophone.Data;
using Sinophone.Handlers;

namespace Sinophone.Tests;

/// <summary>
/// The cutscene path is also the sheet name, and row ids are the same in every
/// language. These are real rows from <c>cut_scene/022/VoiceMan_02200</c>.
/// </summary>
public class CutsceneRowTests
{
    [Fact]
    public void BuildCutsceneRowIndex_MapsTheJapaneseSubtitleToItsRow()
    {
        var index = TalkEventReader.BuildCutsceneRowIndex(
        [
            (0, "さてと……。"),
            (1, "俺だ、サンクレッドだ。\n伝えておきたいことがあってね。"),
            (2, "……ええ、わかりました。\n何か動きがあれば連絡しましょう。"),
        ]);

        Assert.Equal(0u, index[MacroNormalizer.NormalizeKey("さてと……。")]);
        Assert.Equal(1u, index[MacroNormalizer.NormalizeKey("俺だ、サンクレッドだ。\n伝えておきたいことがあってね。")]);
        Assert.Equal(2u, index[MacroNormalizer.NormalizeKey("……ええ、わかりました。\n何か動きがあれば連絡しましょう。")]);
    }

    [Fact]
    public void BuildCutsceneRowIndex_MarksADuplicateTextAsAmbiguous()
    {
        var index = TalkEventReader.BuildCutsceneRowIndex(
        [
            (0, "Same line."),
            (1, "Same line."),
        ]);

        Assert.Equal(uint.MaxValue, index["Same line."]);
    }

    [Fact]
    public void BuildCutsceneRowIndex_SkipsEmptyRows()
    {
        var index = TalkEventReader.BuildCutsceneRowIndex(
        [
            (0, string.Empty),
            (1, "  "),
        ]);

        Assert.Empty(index);
    }
}
