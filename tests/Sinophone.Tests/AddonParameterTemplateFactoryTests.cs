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
}
