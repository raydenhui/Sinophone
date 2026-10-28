using Lumina.Text;
using Lumina.Text.Expressions;
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
        var value = new SeStringBuilder().Append("Hello.").ToReadOnlySeString();

        var variants = SeStringVariants.Extract(value);

        Assert.Single(variants);
        Assert.Equal("Hello.", variants[0]);
    }

    private static ReadOnlySeString BuildConditional()
        => new SeStringBuilder()
            .BeginMacro(MacroCode.If)
            .AppendNullaryExpression(ExpressionType.Day)
            .AppendStringExpression("Sir")
            .AppendStringExpression("Madam")
            .EndMacro()
            .Append(", hello.")
            .ToReadOnlySeString();
}
