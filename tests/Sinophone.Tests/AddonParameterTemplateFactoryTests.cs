using Lumina.Text;
using Lumina.Text.Payloads;
using Sinophone.Data;

namespace Sinophone.Tests;

public class AddonParameterTemplateFactoryTests
{
    [Fact]
    public void TryBuild_AddsWildcardForFormattedValue()
    {
        // The English Addon row keeps the raw macro. The CSV text shows a default
        // "0", but the live sheet carries Digit, which renders the parameter.
        var builder = new SeStringBuilder();
        builder.Append("Entrust or withdraw gil. (Gil entrusted: ");
        builder.BeginMacro(MacroCode.Digit);
        builder.AppendLocalNumberExpression(1);
        builder.AppendIntExpression(0x0C2200FF);
        builder.EndMacro();
        builder.Append(")");

        var text = builder.ToReadOnlySeString();

        Assert.True(AddonParameterTemplateFactory.TryBuild(text.AsSpan(), out var template));
        Assert.Equal(new[] { 1 }, template.ParameterIndices);
        Assert.Contains(ScriptTextTemplate.Wildcard, template.Template);
    }

    [Fact]
    public void TryBuild_AddsWildcardForHeadWrappedValue()
    {
        // Addon row 2390. Head draws the class name the sheet expression returns,
        // so the name is a value of its own, not part of the next literal.
        var builder = new SeStringBuilder();
        builder.Append("Reset retainer class. (");
        builder.BeginMacro(MacroCode.Head);
        builder.AppendLocalNumberExpression(1);
        builder.EndMacro();
        builder.Append(": Lv. ");
        builder.BeginMacro(MacroCode.Num);
        builder.AppendLocalNumberExpression(2);
        builder.EndMacro();
        builder.Append(")");

        var text = builder.ToReadOnlySeString();

        Assert.True(AddonParameterTemplateFactory.TryBuild(text.AsSpan(), out var template));
        Assert.Equal(new[] { 1, 2 }, template.ParameterIndices);
        Assert.True(ScriptTextTemplate.TryMatch(
            template.Template, "Reset retainer class. (Carpenter: Lv. 50)", out var captures));
        Assert.Equal(new[] { "Carpenter", "50" }, captures);
    }
}
