using System.Text;
using Lumina.Text.Payloads;
using Lumina.Text.ReadOnly;

namespace Sinophone.Data;

/// <summary>
/// Builds an Addon match template from the raw SeString. Unlike CSV text, the raw
/// value keeps the parameter expression indices used by IntegerParameter and
/// StringParameter macros.
/// </summary>
internal static class AddonParameterTemplateFactory
{
    public static bool TryBuild(ReadOnlySeStringSpan span, out AddonParameterTemplate template)
    {
        template = null!;

        var builder = new StringBuilder();
        var indices = new List<int>();

        foreach (var payload in span)
        {
            if (payload.Type == ReadOnlySePayloadType.Text)
            {
                builder.Append(Encoding.UTF8.GetString(payload.Body));
                continue;
            }

            if (payload.Type != ReadOnlySePayloadType.Macro)
                continue;

            switch (payload.MacroCode)
            {
                case MacroCode.Num:
                case MacroCode.Ordinal:
                case MacroCode.String:
                    if (TryGetParameterIndex(payload, out var index))
                    {
                        builder.Append(ScriptTextTemplate.Wildcard);
                        indices.Add(index);
                    }
                    else
                    {
                        builder.Append(ScriptTextTemplate.Wildcard);
                        indices.Add(-1);
                    }

                    break;

                case MacroCode.If:
                case MacroCode.Switch:
                case MacroCode.Sheet:
                case MacroCode.SheetSub:
                    builder.Append(ScriptTextTemplate.Wildcard);
                    indices.Add(-1);
                    break;

                case MacroCode.NewLine:
                case MacroCode.NonBreakingSpace:
                    builder.Append(' ');
                    break;

                case MacroCode.Hyphen:
                    builder.Append('-');
                    break;
            }
        }

        var result = MacroNormalizer.NormalizeKey(builder.ToString());
        if (result.Length == 0 || indices.Count == 0)
            return false;

        template = new AddonParameterTemplate(result, indices.ToArray());
        return true;
    }

    private static bool TryGetParameterIndex(ReadOnlySePayloadSpan payload, out int index)
    {
        index = -1;

        if (!payload.TryGetExpression(out var expression)
            || !expression.TryGetParameterExpression(out _, out var operand)
            || !operand.TryGetUInt(out var value)
            || value == 0
            || value > int.MaxValue)
        {
            return false;
        }

        index = (int)value;
        return true;
    }
}
