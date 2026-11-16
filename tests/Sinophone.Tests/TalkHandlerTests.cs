using System.Text;
using Sinophone.Handlers;

namespace Sinophone.Tests;

/// <summary>
/// Covers reading the Talk text node. The node holds a SeString, and a line with
/// bold or italics carries a formatting macro inside it.
/// </summary>
public class TalkHandlerTests
{
    [Fact]
    public void ExtractPlainText_DropsFormattingMacros_WhenTextNodeHasEmphasis()
    {
        // What the game puts in the node for "You may use your workshop's
        // **fabrication station** to begin...", read as raw UTF-8.
        var text = TalkHandler.ExtractPlainText(BuildEmphasizedText());

        Assert.Equal(
            "You may use your workshop's fabrication station to begin.",
            text);
    }

    [Fact]
    public void ExtractPlainText_ReturnsText_WhenThereAreNoMacros()
    {
        var text = TalkHandler.ExtractPlainText(Encoding.UTF8.GetBytes("Plain line."));

        Assert.Equal("Plain line.", text);
    }

    [Theory]
    [InlineData("阿莉塞", "Alisaie", "阿莉塞 (Alisaie)")]
    [InlineData("", "Alisaie", "Alisaie")]
    [InlineData("阿莉塞", "", "阿莉塞")]
    [InlineData("阿莉塞", null, "阿莉塞")]
    [InlineData(null, "Alisaie", "Alisaie")]
    [InlineData("阿莉塞", "阿莉塞", "阿莉塞")]
    [InlineData("???", "Mysterious Voice", "???")]
    [InlineData("？？？", "Mysterious Voice", "？？？")]
    public void FormatSpeakerName_ShowsBothNames(string? chinese, string? english, string expected)
        => Assert.Equal(expected, TalkHandler.FormatSpeakerName(chinese, english));

    [Theory]
    [InlineData("我們按照你的要求進行了調查。", "我們按照你的要求進行了調查。")]
    [InlineData("第一行\n第二行", "第一行")]
    [InlineData("\n第二行", "(blank line first)")]
    [InlineData("", "(empty)")]
    [InlineData(null, "(empty)")]
    public void FirstLineOf_ReturnsTheFirstLine_ForLogs(string? text, string expected)
        => Assert.Equal(expected, TalkHandler.FirstLineOf(text));

    [Fact]
    public void FirstLineOf_ShortensALongFirstLine()
    {
        var line = new string('あ', 40);

        var first = TalkHandler.FirstLineOf($"{line}\nsecond");

        Assert.EndsWith("...", first);
        Assert.Equal(27, first.Length);
    }

    private static byte[] BuildEmphasizedText()
    {
        var bytes = new List<byte>();
        bytes.AddRange(Encoding.UTF8.GetBytes("You may use your workshop's "));
        AppendFormattingMacro(bytes, 0x48);
        bytes.AddRange(Encoding.UTF8.GetBytes("fabrication station"));
        AppendFormattingMacro(bytes, 0x49);
        bytes.AddRange(Encoding.UTF8.GetBytes(" to begin."));
        return bytes.ToArray();
    }

    private static void AppendFormattingMacro(List<byte> target, byte code)
    {
        target.Add(0x02); // payload start
        target.Add(code);
        target.Add(3);    // payload length 2, as a SeString integer
        target.Add(0x01);
        target.Add(0x01);
        target.Add(0x03); // payload end
    }
}
