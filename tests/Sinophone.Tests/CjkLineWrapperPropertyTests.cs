using Sinophone.Data;

namespace Sinophone.Tests;

/// <summary>
/// <see cref="CjkLineWrapperTests"/> checks one hand-picked line per rule. These tests hold
/// the rules over many generated lines instead, so a rule that only works on the examples
/// fails here. Inputs come from a fixed seed, so a failure is reproducible.
/// </summary>
public class CjkLineWrapperPropertyTests
{
    private static readonly Func<string, int> Characters = static text => text.Length;

    /// <summary>Closing marks, copied from <c>CjkLineWrapper.NoLineStart</c>.</summary>
    private const string ClosingMarks = "。，、．；：？！）］｝」』】〉》”’…";

    /// <summary>Opening marks, copied from <c>CjkLineWrapper.NoLineEnd</c>.</summary>
    private const string OpeningMarks = "（［｛「『【〈《“‘";

    private const string Letters = "一二三四五六七八九十天地玄黃宇宙洪荒";

    [Fact]
    public void Wrap_KeepsEveryCharacter_ForEveryGeneratedLine()
    {
        foreach (var (text, width) in Generate())
        {
            var wrapped = CjkLineWrapper.Wrap(text, width, Characters);

            // A break is the only character the wrapper adds. Removing the added breaks
            // must give the input back, so nothing is lost, duplicated, or reordered.
            Assert.Equal(text, wrapped.Replace("\n", string.Empty));
        }
    }

    [Fact]
    public void Wrap_ReturnsTheTextUnchanged_WhenItFits()
    {
        foreach (var (text, width) in Generate())
        {
            if (text.Length > width)
                continue;

            Assert.Equal(text, CjkLineWrapper.Wrap(text, width, Characters));
        }
    }

    [Fact]
    public void Wrap_KeepsEveryParagraph_ForEveryGeneratedLine()
    {
        foreach (var (text, width) in Generate(withParagraphs: true))
        {
            var wrapped = CjkLineWrapper.Wrap(text, width, Characters);

            // Each paragraph is wrapped on its own, so the characters survive and no
            // paragraph break is lost or invented.
            Assert.Equal(
                text.Replace("\n", string.Empty),
                wrapped.Replace("\n", string.Empty));

            Assert.Equal(
                text.Split('\n').Length - 1,
                wrapped.Split('\n').Length - 1 - CountWrapBreaks(text, width));
        }
    }

    [Fact]
    public void Wrap_NeverEndsALineWithAnOpeningMark()
    {
        foreach (var (text, width) in Generate())
        {
            var lines = CjkLineWrapper.Wrap(text, width, Characters).Split('\n');

            for (var i = 0; i < lines.Length - 1; i++)
            {
                if (!OpeningMarks.Contains(lines[i][^1]))
                    continue;

                // The break moves forward over the run of opening marks, so a line can
                // end with one only when the rest of the text is all opening marks and
                // there is nowhere to move the break to.
                Assert.True(
                    string.Concat(lines[(i + 1)..]).All(c => OpeningMarks.Contains(c)),
                    $"'{text}' wrapped at {width} ends line {i} with '{lines[i][^1]}': " +
                    string.Join(" | ", lines));
            }
        }
    }

    [Fact]
    public void Wrap_KeepsAClosingMarkOffALineStart_WhenItCan()
    {
        foreach (var (text, width) in Generate())
        {
            var lines = CjkLineWrapper.Wrap(text, width, Characters).Split('\n');

            for (var i = 1; i < lines.Length; i++)
            {
                if (!ClosingMarks.Contains(lines[i][0]))
                    continue;

                // The break moves back over the closing run. It stops at the previous
                // line's first character, so a line can start with a mark only when
                // everything after that character is a closing mark.
                var movedBack = lines[i - 1][1..];
                Assert.True(
                    movedBack.Length > 0 && movedBack.All(c => ClosingMarks.Contains(c)),
                    $"'{text}' wrapped at {width} starts line {i} with '{lines[i][0]}' " +
                    $"but line {i - 1} is '{lines[i - 1]}': " +
                    string.Join(" | ", lines));
            }
        }
    }

    /// <summary>
    /// How many breaks the wrapper added, found by wrapping each paragraph on its own.
    /// A paragraph is what the text already split into at a line break.
    /// </summary>
    private static int CountWrapBreaks(string text, int width)
        => text.Split('\n')
               .Sum(paragraph => CjkLineWrapper.Wrap(paragraph, width, Characters).Count(c => c == '\n'));

    /// <summary>
    /// Realistic Chinese text: body characters, a closing mark after its character, and an
    /// opening mark before its character. Wrapped at every width that can split a line,
    /// this reaches every rule without a game. A paragraph break is added to some input.
    /// </summary>
    private static IEnumerable<(string Text, int Width)> Generate(bool withParagraphs = false)
    {
        var random = new Random(withParagraphs ? 20261126 : 20261125);
        var runs = withParagraphs ? 2_000 : 5_000;

        for (var run = 0; run < runs; run++)
        {
            var pieces = random.Next(2, 15);
            var text = new System.Text.StringBuilder();

            for (var i = 0; i < pieces; i++)
            {
                if (withParagraphs && i > 0 && random.Next(8) == 0)
                    text.Append('\n');

                var kind = random.Next(10);

                if (kind < 7)
                    text.Append(Letters[random.Next(Letters.Length)]);
                else if (kind < 9)
                    text.Append(Letters[random.Next(Letters.Length)])
                        .Append(ClosingMarks[random.Next(ClosingMarks.Length)]);
                else
                    text.Append(OpeningMarks[random.Next(OpeningMarks.Length)])
                        .Append(Letters[random.Next(Letters.Length)]);
            }

            yield return (text.ToString(), random.Next(2, 11));
        }
    }
}
