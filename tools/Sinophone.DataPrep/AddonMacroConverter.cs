using System.Globalization;
using System.Text;

namespace Sinophone.DataPrep;

/// <summary>
/// Converts the datamining notation in Chinese <c>Addon</c> rows into the macro
/// string Lumina parses. The plugin then evaluates the string with the values the
/// game passed, instead of filling them with string replacement.
/// </summary>
/// <remarks>
/// A row with a tag this converter does not know is left unconverted. The plugin
/// keeps the old replacement path for it, so a gap here never breaks a prompt.
/// </remarks>
internal static class AddonMacroConverter
{
    public static bool TryConvert(string text, out string macroString)
    {
        macroString = string.Empty;

        try
        {
            var builder = new StringBuilder(text.Length + 32);
            if (!ConvertRun(text, expression: false, builder))
                return false;

            macroString = builder.ToString();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool ConvertRun(string text, bool expression, StringBuilder output)
    {
        var index = 0;

        while (index < text.Length)
        {
            var open = text.IndexOf('<', index);
            if (open < 0)
            {
                AppendText(output, text[index..], expression);
                return true;
            }

            AppendText(output, text[index..open], expression);

            if (!TryReadTag(text, open, out var tag))
            {
                // A literal angle bracket, such as "<t>" in the dumps.
                var close = text.IndexOf('>', open + 1);
                if (close < 0)
                    return false;

                AppendText(output, text[open..(close + 1)], expression);
                index = close + 1;
                continue;
            }

            if (tag.Closing)
            {
                switch (tag.Name)
                {
                    case "Emphasis":
                        output.Append("<italic(0)>");
                        index = tag.End;
                        continue;

                    case "Emphasis2":
                        output.Append("<bold(0)>");
                        index = tag.End;
                        continue;

                    case "Color":
                        output.Append("<color(stackcolor)>");
                        index = tag.End;
                        continue;

                    default:
                        // If, Switch, Case, and Else are consumed by their opener.
                        return false;
                }
            }

            if (!EmitTag(text, tag, output, out index))
                return false;
        }

        return true;
    }

    private static bool EmitTag(string text, Tag tag, StringBuilder output, out int next)
    {
        next = tag.End;

        switch (tag.Name)
        {
            case "Value":
            case "TwoDigitValue":
            case "Highlight":
            {
                if (!TryReadBody(text, tag, out var body, out next))
                    return false;

                var macro = tag.Name switch
                {
                    "Value" => "num",
                    "TwoDigitValue" => "sec",
                    _ => "string",
                };

                output.Append('<').Append(macro).Append('(');
                if (!ConvertExpression(body, output))
                    return false;
                output.Append(")>");
                return true;
            }

            case "ZeroPaddedValue":
            {
                if (!TryReadBody(text, tag, out var body, out next))
                    return false;

                output.Append("<digit(");
                if (!ConvertExpression(body, output))
                    return false;
                output.Append(',');
                if (!ConvertExpression(tag.Args ?? string.Empty, output))
                    return false;
                output.Append(")>");
                return true;
            }

            case "Time":
                output.Append("<settime(");
                if (!ConvertExpression(tag.Args ?? string.Empty, output))
                    return false;
                output.Append(")>");
                return true;

            case "Format":
            {
                var args = SplitArgs(tag.Args);
                if (args.Count != 2)
                    return false;

                output.Append("<kilo(");
                if (!ConvertExpression(args[0], output))
                    return false;
                output.Append(',');
                if (!ConvertRaw(args[1], output))
                    return false;
                output.Append(")>");
                return true;
            }

            case "Sheet":
            {
                var args = SplitArgs(tag.Args);
                if (args.Count == 0)
                    return false;

                output.Append("<sheet(");
                for (var i = 0; i < args.Count; i++)
                {
                    if (i > 0)
                        output.Append(',');

                    if (!ConvertExpression(args[i], output))
                        return false;
                }

                output.Append(")>");
                return true;
            }

            case "Gui":
            case "CommandIcon":
            case "Clickable":
            case "Color":
            {
                var macro = tag.Name switch
                {
                    "Gui" => "icon",
                    "CommandIcon" => "icon2",
                    "Clickable" => "head",
                    _ => "color",
                };

                output.Append('<').Append(macro).Append('(');
                if (!ConvertExpression(tag.Args ?? string.Empty, output))
                    return false;
                output.Append(")>");
                return true;
            }

            case "Indent":
                output.Append("<nbsp>");
                return true;

            case "Emphasis":
                output.Append("<italic(1)>");
                return true;

            case "Emphasis2":
                output.Append("<bold(1)>");
                return true;

            case "UIForeground":
            case "UIGlow":
            case "Unknown14":
            case "Unknown2D":
            case "Unknown60":
            {
                if (!TryReadBody(text, tag, out var body, out next))
                    return false;

                var macro = tag.Name switch
                {
                    "UIForeground" => "colortype",
                    "UIGlow" => "edgecolortype",
                    "Unknown14" => "edgecolor",
                    "Unknown2D" => "headall",
                    _ => "sound",
                };

                output.Append('<').Append(macro).Append('(');
                if (!ConvertRaw(body, output))
                    return false;
                output.Append(")>");
                return true;
            }

            case "If":
                return EmitIf(text, tag, output, out next);

            case "Switch":
                return EmitSwitch(text, tag, output, out next);

            default:
                // Plain text that happens to use angle brackets.
                if (tag.Name is "t" or "attack1" or "M")
                {
                    AppendText(output, text[tag.Start..tag.End], expression: true);
                    return true;
                }

                return false;
        }
    }

    private static bool EmitIf(string text, Tag tag, StringBuilder output, out int next)
    {
        next = tag.End;

        if (!TryFindClose(text, tag, out var close))
            return false;

        var inner = text[tag.End..close];
        if (!SplitElse(inner, out var whenTrue, out var whenFalse))
            return false;

        output.Append("<if(");
        if (!ConvertExpression(tag.Args ?? string.Empty, output))
            return false;
        output.Append(',');
        if (!ConvertFragment(whenTrue, output))
            return false;
        output.Append(',');
        if (!ConvertFragment(whenFalse, output))
            return false;
        output.Append(")>");

        next = close + tag.Name.Length + 3;
        return true;
    }

    private static bool EmitSwitch(string text, Tag tag, StringBuilder output, out int next)
    {
        next = tag.End;

        if (!TryFindClose(text, tag, out var close))
            return false;

        var inner = text[tag.End..close];
        var cases = new List<string>();
        var index = 0;

        while (index < inner.Length)
        {
            var open = inner.IndexOf("<Case", index, StringComparison.Ordinal);
            if (open < 0)
                break;

            if (!TryReadTag(inner, open, out var caseTag) || caseTag.Closing)
                return false;

            if (!TryReadBody(inner, caseTag, out var body, out var after))
                return false;

            cases.Add(body);
            index = after;
        }

        if (cases.Count == 0)
            return false;

        output.Append("<switch(");
        if (!ConvertExpression(tag.Args ?? string.Empty, output))
            return false;

        foreach (var body in cases)
        {
            output.Append(',');
            if (!ConvertFragment(body, output))
                return false;
        }

        output.Append(")>");

        next = close + tag.Name.Length + 3;
        return true;
    }

    private static bool ConvertFragment(string fragment, StringBuilder output)
        => ConvertRun(fragment, expression: true, output);

    private static bool ConvertExpression(string expression, StringBuilder output)
    {
        var value = expression.Trim();
        if (value.Length == 0)
            return true;

        if (value.StartsWith('<'))
            return ConvertRun(value, expression: true, output);

        var paren = value.IndexOf('(');
        if (paren > 0 && value.EndsWith(')'))
        {
            var name = value[..paren];
            var args = SplitArgs(value[(paren + 1)..^1]);

            switch (name)
            {
                case "IntegerParameter":
                case "StringParameter":
                case "PlayerParameter":
                case "ObjectParameter":
                {
                    if (args.Count != 1 || !IsNumber(args[0]))
                        return false;

                    output.Append(name switch
                    {
                        "IntegerParameter" => "lnum",
                        "StringParameter" => "lstr",
                        "PlayerParameter" => "gnum",
                        _ => "gstr",
                    }).Append(args[0]);
                    return true;
                }

                case "TopLevelParameter":
                {
                    if (args.Count != 1)
                        return false;

                    var placeholder = args[0] switch
                    {
                        "215" => "t_msec",
                        "216" => "t_sec",
                        "217" => "t_min",
                        "218" => "t_hour",
                        "219" => "t_day",
                        "220" => "t_wday",
                        "221" => "t_mon",
                        "222" => "t_year",
                        _ => null,
                    };

                    if (placeholder is null)
                        return false;

                    output.Append(placeholder);
                    return true;
                }

                case "GreaterThan":
                case "GreaterThanOrEqualTo":
                case "LessThan":
                case "LessThanOrEqualTo":
                case "Equal":
                case "NotEqual":
                {
                    if (args.Count != 2)
                        return false;

                    var op = name switch
                    {
                        "GreaterThan" => ">",
                        "GreaterThanOrEqualTo" => ">=",
                        "LessThan" => "<",
                        "LessThanOrEqualTo" => "<=",
                        "Equal" => "==",
                        _ => "!=",
                    };

                    output.Append('[');
                    if (!ConvertExpression(args[0], output))
                        return false;
                    output.Append(op);
                    if (!ConvertExpression(args[1], output))
                        return false;
                    output.Append(']');
                    return true;
                }

                default:
                    return false;
            }
        }

        if (IsNumber(value))
        {
            output.Append(value);
            return true;
        }

        AppendText(output, value, expression: true);
        return true;
    }

    /// <summary>
    /// Decodes the raw argument the dumper prints as hex. UIForeground and UIGlow
    /// hold a UIColor row id as two big-endian bytes; Format holds a string
    /// expression whose text is the last byte, such as a comma.
    /// </summary>
    private static bool ConvertRaw(string raw, StringBuilder output)
    {
        var value = raw.Trim();
        if (value.Length == 0)
            return false;

        if (string.Equals(value, "EC", StringComparison.OrdinalIgnoreCase))
        {
            output.Append("stackcolor");
            return true;
        }

        // "01" resets the colour stack to the default.
        if (string.Equals(value, "01", StringComparison.OrdinalIgnoreCase))
        {
            output.Append('0');
            return true;
        }

        // A short body is a raw parameter expression, such as EA02 for lstr1.
        if (value.Length == 4 && value[0] is 'E' or 'e' && value.All(Uri.IsHexDigit))
        {
            var kind = value[..2].ToUpperInvariant() switch
            {
                "E8" => "lnum",
                "E9" => "gnum",
                "EA" => "lstr",
                "EB" => "gstr",
                _ => null,
            };

            if (kind is not null
                && byte.TryParse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var index))
            {
                output.Append(kind).Append(index.ToString(CultureInfo.InvariantCulture));
                return true;
            }
        }

        // FE plus a four-byte signed value, such as an edge colour.
        if (value.Length == 10
            && value.StartsWith("FE", StringComparison.OrdinalIgnoreCase)
            && value.All(Uri.IsHexDigit))
        {
            var number = unchecked((int)uint.Parse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            output.Append(number.ToString(CultureInfo.InvariantCulture));
            return true;
        }

        if (value.Length != 6 || !value.All(Uri.IsHexDigit))
            return false;

        if (value.StartsWith("F2", StringComparison.OrdinalIgnoreCase))
        {
            var number = ushort.Parse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            output.Append(number.ToString(CultureInfo.InvariantCulture));
            return true;
        }

        if (value.StartsWith("FF", StringComparison.OrdinalIgnoreCase))
        {
            var character = (char)byte.Parse(value[4..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            AppendText(output, character.ToString(), expression: true);
            return true;
        }

        return false;
    }

    private static bool TryReadBody(string text, Tag tag, out string body, out int next)
    {
        body = string.Empty;
        next = tag.End;

        var close = text.IndexOf("</" + tag.Name + ">", tag.End, StringComparison.Ordinal);
        if (close < 0)
            return false;

        body = text[tag.End..close];
        next = close + tag.Name.Length + 3;
        return true;
    }

    private static bool TryFindClose(string text, Tag tag, out int close)
    {
        close = -1;
        var depth = 0;
        var index = tag.End;

        while (index < text.Length)
        {
            var open = text.IndexOf('<', index);
            if (open < 0)
                return false;

            if (!TryReadTag(text, open, out var found))
            {
                index = open + 1;
                continue;
            }

            if (found.Name == tag.Name)
            {
                if (found.Closing)
                {
                    if (depth == 0)
                    {
                        close = found.Start;
                        return true;
                    }

                    depth--;
                }
                else if (!found.SelfClosing)
                {
                    depth++;
                }
            }

            index = found.End;
        }

        return false;
    }

    private static bool SplitElse(string inner, out string whenTrue, out string whenFalse)
    {
        var depth = 0;
        var index = 0;

        while (index < inner.Length)
        {
            var open = inner.IndexOf('<', index);
            if (open < 0)
                break;

            if (!TryReadTag(inner, open, out var tag))
            {
                index = open + 1;
                continue;
            }

            if (tag.Name == "Else" && depth == 0)
            {
                whenTrue = inner[..tag.Start];
                whenFalse = inner[tag.End..];
                return true;
            }

            if (tag.Name is "If" or "Switch")
                depth += tag.Closing ? -1 : tag.SelfClosing ? 0 : 1;

            index = tag.End;
        }

        whenTrue = inner;
        whenFalse = string.Empty;
        return true;
    }

    private static bool TryReadTag(string text, int start, out Tag tag)
    {
        tag = default;

        if (start >= text.Length || text[start] != '<')
            return false;

        // An argument can hold another tag, as Sheet(PlaceName,<Sheet(...)/>,0)
        // does. Find the '>' that closes this tag, not the first one.
        var depth = 0;
        var end = -1;
        for (var i = start + 1; i < text.Length; i++)
        {
            if (text[i] == '<')
            {
                depth++;
            }
            else if (text[i] == '>')
            {
                if (depth == 0)
                {
                    end = i;
                    break;
                }

                depth--;
            }
        }

        if (end < 0)
            return false;

        var inner = text[(start + 1)..end];
        var closing = inner.StartsWith('/');
        if (closing)
            inner = inner[1..];

        var selfClosing = inner.EndsWith('/');
        if (selfClosing)
            inner = inner[..^1];

        var paren = inner.IndexOf('(');
        string name;
        string? args = null;

        if (paren > 0 && inner.EndsWith(')'))
        {
            name = inner[..paren];
            args = inner[(paren + 1)..^1];
        }
        else
        {
            name = inner;
        }

        if (name.Length == 0)
            return false;

        tag = new Tag(name, args, start, end + 1, closing, selfClosing);
        return true;
    }

    private static List<string> SplitArgs(string? args)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(args))
            return result;

        var depth = 0;
        var start = 0;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case '(' or '[':
                    depth++;
                    break;
                case ')' or ']':
                    depth--;
                    break;
                case ',' when depth == 0:
                    result.Add(args[start..i].Trim());
                    start = i + 1;
                    break;
            }
        }

        result.Add(args[start..].Trim());
        return result;
    }

    private static bool IsNumber(string value)
        => value.Length > 0 && value.All(character => char.IsAsciiDigit(character) || character == '-');

    private static void AppendText(StringBuilder output, string text, bool expression)
    {
        foreach (var character in text)
        {
            if (character is '\\' or '<')
                output.Append('\\').Append(character);
            else if (expression && character is ',' or '>' or '(' or ')' or '[' or ']')
                output.Append('\\').Append(character);
            else
                output.Append(character);
        }
    }

    private readonly record struct Tag(
        string Name,
        string? Args,
        int Start,
        int End,
        bool Closing,
        bool SelfClosing);
}
