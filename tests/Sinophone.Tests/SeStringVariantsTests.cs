using System.Text;
using Lumina.Text.Payloads;
using Lumina.Text.ReadOnly;
using Sinophone.Data;

namespace Sinophone.Tests;

/// <summary>
/// Guards the splitter that turns a conditional SeString into one key per branch.
/// Lumina's ExtractText drops conditional bodies, which is why these rows missed.
/// </summary>
public class SeStringVariantsTests
{
    [Fact]
    public void ExtractText_DropsConditionalBranches()
    {
        Assert.Equal(", hello.", BuildConditional().ExtractText());
    }

    [Fact]
    public void Extract_ReturnsOneVariantPerBranch()
    {
        var variants = SeStringVariants.Extract(BuildConditional());

        Assert.Equal(new[] { "Sir, hello.", "Madam, hello." }, variants);
    }

    [Fact]
    public void Extract_ReturnsOneVariant_WhenThereIsNoConditional()
    {
        var value = new ReadOnlySeString(Encoding.UTF8.GetBytes("Hello."));

        var variants = SeStringVariants.Extract(value);

        Assert.Single(variants);
        Assert.Equal("Hello.", variants[0]);
    }

    /// <summary>
    /// Builds <c>&lt;If(day)&gt;Sir&lt;Else/&gt;Madam&lt;/If&gt;, hello.</c> by hand.
    /// SeStringBuilder would be shorter, but it needs Microsoft.Extensions.ObjectPool,
    /// which the test host does not copy next to the test assembly.
    /// </summary>
    private static ReadOnlySeString BuildConditional()
    {
        var body = new List<byte> { 0x01 }; // condition: the integer 0. The splitter ignores it.
        AppendStringExpression(body, "Sir");
        AppendStringExpression(body, "Madam");

        var bytes = new List<byte>
        {
            0x02,                              // payload start
            (byte)MacroCode.If,
            (byte)(body.Count + 1),            // payload length, as a SeString integer
        };
        bytes.AddRange(body);
        bytes.Add(0x03);                       // payload end
        bytes.AddRange(Encoding.UTF8.GetBytes(", hello."));

        return new ReadOnlySeString(bytes.ToArray());
    }

    private static void AppendStringExpression(List<byte> target, string text)
    {
        var utf8 = Encoding.UTF8.GetBytes(text);
        target.Add(0xFF);                      // marks the expression as a string
        target.Add((byte)(utf8.Length + 1));   // length, as a SeString integer
        target.AddRange(utf8);
    }
}
