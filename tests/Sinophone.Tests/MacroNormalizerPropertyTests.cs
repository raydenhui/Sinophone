using Sinophone.Data;

namespace Sinophone.Tests;

/// <summary>
/// <see cref="MacroNormalizerTests"/> checks one hand-picked line per rule. These tests hold
/// the rules over many generated lines instead. A line read from the game and the same line
/// read from the dump must normalize to the same key, so the normalizer has to be a function
/// that settles: running it twice must give what running it once gave.
/// </summary>
public class MacroNormalizerPropertyTests
{
    /// <summary>
    /// The pieces a line can be made of: plain words, client macros, emphasis, a speaker
    /// alias, and whitespace the game shows but the dump does not. A speaker alias is only
    /// ever the start of a line, so it is generated apart from the body pieces below.
    /// </summary>
    private static readonly string[] Fragments =
    [
        "Hello", "there.", "the", " ", "  ", "\t", "\n", "World",
        "<br>", "<Sheet(ENpcResident,1,1)/>", "<Highlight>", "</Highlight>",
        "<UIForeground>F20223</UIForeground>", "<Unknown60>0262</Unknown60>",
        "<squeal>", "*", "**", "Player Player", "78th", "a.m.", "（", "）", "……",
    ];

    /// <summary>
    /// A dump line can start with one speaker alias. Two in a row is not a shape the dumps
    /// write, so the generator never builds one.
    /// </summary>
    private static readonly string[] SpeakerPrefixes =
    [
        "", "", "", "(-Retainer-)", "(-???-)", "(-Voice from the Past-)",
    ];

    [Fact]
    public void NormalizeKey_IsIdempotent_ForEveryGeneratedLine()
    {
        foreach (var text in Generate())
        {
            var once = MacroNormalizer.NormalizeKey(text);
            var twice = MacroNormalizer.NormalizeKey(once);

            Assert.Equal(once, twice);
        }
    }

    [Fact]
    public void NormalizeKey_NeverThrows_ForEveryGeneratedLine()
    {
        // The normalizer runs on every lookup, including text the dump never showed it.
        // A throw would reach the handler's lookup path, so it must accept anything.
        foreach (var text in Generate())
            _ = MacroNormalizer.NormalizeKey(text);
    }

    [Fact]
    public void NormalizeKey_HasNoLeadingOrTrailingSpace_ForEveryGeneratedLine()
    {
        foreach (var text in Generate())
        {
            var key = MacroNormalizer.NormalizeKey(text);

            if (key.Length == 0)
                continue;

            Assert.False(char.IsWhiteSpace(key[0]), $"leading space in '{key}' from '{text}'");
            Assert.False(char.IsWhiteSpace(key[^1]), $"trailing space in '{key}' from '{text}'");
        }
    }

    [Fact]
    public void NormalizeKey_HasNoRunOfSpaces_ForEveryGeneratedLine()
    {
        foreach (var text in Generate())
        {
            var key = MacroNormalizer.NormalizeKey(text);

            Assert.DoesNotContain("  ", key);
        }
    }

    private static IEnumerable<string> Generate()
    {
        var random = new Random(20261127);

        for (var run = 0; run < 3_000; run++)
        {
            var pieces = random.Next(1, 9);
            var builder = new System.Text.StringBuilder();

            // The alias, when there is one, is the start of the line.
            builder.Append(SpeakerPrefixes[random.Next(SpeakerPrefixes.Length)]);

            for (var i = 0; i < pieces; i++)
                builder.Append(Fragments[random.Next(Fragments.Length)]);

            yield return builder.ToString();
        }
    }
}
