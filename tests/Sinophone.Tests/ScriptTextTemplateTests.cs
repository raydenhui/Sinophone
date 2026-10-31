using System.Text;
using Lumina.Text.Payloads;
using Lumina.Text.ReadOnly;
using Sinophone.Data;

namespace Sinophone.Tests;

/// <summary>
/// Covers the limited wildcard used for script values that contain a player name
/// or rank. Only the marked dynamic span may differ from the live line.
/// </summary>
public class ScriptTextTemplateTests
{
    [Fact]
    public void Matches_AcceptsTextFilledIntoAnIfSpan()
    {
        var template = ScriptTextTemplate.Build(BuildGreeting().AsSpan());

        Assert.True(ScriptTextTemplate.Matches(
            template,
            "Greetings, Serpent Captain Sol. If you wish to know aught of the current situation, you've come to the right person."));
    }

    [Fact]
    public void Matches_AcceptsAnEmptyIfSpan()
    {
        var template = ScriptTextTemplate.Build(BuildGreeting().AsSpan());

        Assert.True(ScriptTextTemplate.Matches(
            template,
            "Greetings, . If you wish to know aught of the current situation, you've come to the right person."));
    }

    [Fact]
    public void Matches_RejectsTextThatDiffersOutsideTheWildcard()
    {
        var template = ScriptTextTemplate.Build(BuildGreeting().AsSpan());

        Assert.False(ScriptTextTemplate.Matches(
            template,
            "Greetings, Serpent Captain Sol. If you wish to know aught of the current weather, you've come to the right person."));
    }

    [Fact]
    public void Matches_RequiresAnchoredPrefixAndSuffix()
    {
        var template = ScriptTextTemplate.Build(BuildGreeting().AsSpan());

        Assert.False(ScriptTextTemplate.Matches(
            template,
            "Well met. Greetings, Serpent Captain Sol. If you wish to know aught of the current situation, you've come to the right person."));
    }

    [Fact]
    public void Build_KeepsPlainTextAsAnExactTemplate()
    {
        var value = new ReadOnlySeString(Encoding.UTF8.GetBytes("Plain line."));
        var template = ScriptTextTemplate.Build(value.AsSpan());

        Assert.Equal("Plain line.", template);
        Assert.True(ScriptTextTemplate.Matches(template, "Plain line."));
        Assert.False(ScriptTextTemplate.Matches(template, "Plain other line."));
    }

    /// <summary>
    /// Builds <c>Greetings, &lt;If(rank)&gt;Serpent Captain Sol&lt;/If&gt;. If...</c>.
    /// The If body is irrelevant to the template, but it must be a valid payload.
    /// </summary>
    private static ReadOnlySeString BuildGreeting()
    {
        var body = new List<byte> { 0x01 };
        AppendStringExpression(body, "Serpent Captain Sol");

        var bytes = new List<byte>();
        bytes.AddRange(Encoding.UTF8.GetBytes("Greetings, "));
        bytes.Add(0x02);
        bytes.Add((byte)MacroCode.If);
        bytes.Add((byte)(body.Count + 1));
        bytes.AddRange(body);
        bytes.Add(0x03);
        bytes.AddRange(Encoding.UTF8.GetBytes(
            " . If you wish to know aught of the current situation, you've come to the right person."));

        return new ReadOnlySeString(bytes.ToArray());
    }

    private static void AppendStringExpression(List<byte> target, string text)
    {
        var utf8 = Encoding.UTF8.GetBytes(text);
        target.Add(0xFF);
        target.Add((byte)(utf8.Length + 1));
        target.AddRange(utf8);
    }
}
