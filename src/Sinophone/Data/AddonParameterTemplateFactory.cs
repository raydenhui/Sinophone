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
                // Every macro that renders a value. Format and ZeroPaddedValue in the
                // Chinese dumps parse as Digit. Head and its siblings draw the
                // expression they wrap, such as a class name. Time draws nothing but
                // sets the date values, so its wildcard matches an empty span.
                case MacroCode.Num:
                case MacroCode.Hex:
                case MacroCode.Kilo:
                case MacroCode.Byte:
                case MacroCode.Sec:
                case MacroCode.Time:
                case MacroCode.Float:
                case MacroCode.Digit:
                case MacroCode.Ordinal:
                case MacroCode.String:
                case MacroCode.Head:
                case MacroCode.HeadAll:
                case MacroCode.Caps:
                case MacroCode.Lower:
                case MacroCode.LowerHead:
                    builder.Append(ScriptTextTemplate.Wildcard);
                    indices.Add(TryGetParameterIndex(payload, out var index) ? index : -1);
                    break;

                // EnNoun draws a name from another sheet, as Sheet does for the
                // Chinese client. Addon row 11194 writes an item as
                // "Exchange <Kilo> <EnNoun Item> for the following item?".
                case MacroCode.If:
                case MacroCode.Switch:
                case MacroCode.Sheet:
                case MacroCode.SheetSub:
                case MacroCode.EnNoun:
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
