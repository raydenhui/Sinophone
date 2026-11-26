using Sinophone.Diagnostics;

namespace Sinophone.Data;

/// <summary>
/// Addon-sheet prompts: the short label lookup, the templates that match a prompt with values the
/// game fills at runtime, and the order to try them in.
/// </summary>
internal sealed class AddonIndex
{
    private readonly Dictionary<string, string> _labels = new(StringComparer.Ordinal);
    private readonly List<AddonTemplate> _templates = [];
    private readonly HashSet<string> _templateKeys = new(StringComparer.Ordinal);

    public int TemplateCount => _templates.Count;

    public void AddLabel(string key, string display)
        => KanaText.TryAddPreferred(_labels, key, display);

    public bool TryGetLabel(string key, out string stored)
        => _labels.TryGetValue(key, out stored!);

    public bool TryGetText(
        string key,
        FailureTrace? trace,
        out string chineseText,
        out string[] captures,
        out int[]? parameterIndices,
        out string? macroTemplate)
    {
        chineseText = string.Empty;
        captures = [];
        parameterIndices = null;
        macroTemplate = null;

        if (key.Length == 0)
        {
            trace?.Step("addon.template", "empty");
            return false;
        }

        if (_templates.Count == 0)
        {
            trace?.Step("addon.template", "no-templates");
            return false;
        }

        foreach (var template in _templates)
        {
            if (!ScriptTextTemplate.TryMatch(template.Template, key, out captures))
                continue;

            trace?.Step("addon.template", "hit", template.Template);
            chineseText = template.Chinese;
            parameterIndices = template.ParameterIndices;
            macroTemplate = template.MacroTemplate;
            return true;
        }

        trace?.Step("addon.template", "miss");
        return false;
    }

    /// <summary>
    /// True when an Addon row could hold this prompt: some template's literal prefix matches the
    /// start of the text. False means the dumps have no row for it at all, which is what a live
    /// client newer than the data looks like.
    /// </summary>
    public bool HasPromptLike(string key, int prefixLength)
    {
        if (key.Length < prefixLength)
            return false;

        var prefix = key[..prefixLength];

        foreach (var template in _templates)
        {
            if (template.Template.Length == 0 || template.Template[0] == ScriptTextTemplate.Wildcard)
                continue;

            if (template.Template.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Adds the template built from the row's raw SeString, which keeps the parameter indices the
    /// game fills. Short templates are dropped because "Enter ?" would match unrelated prompts.
    /// </summary>
    public void AddParameterized(AddonParameterTemplate template, string chinese, string? macro)
    {
        if (ScriptTextTemplate.LiteralLength(template.Template) < 10)
            return;

        AddTemplate(template.Template, chinese, template.ParameterIndices, macro);
    }

    /// <summary>
    /// Adds templates for an English Addon line whose values are blanks rather than macros.
    /// </summary>
    public void AddPromptVariants(string english, string chinese, string? macro)
    {
        if (english.Contains("UNKNOWN", StringComparison.Ordinal))
        {
            var template = ScriptTextTemplate.BuildFromPlainText(english);
            if (ScriptTextTemplate.LiteralLength(template) >= 4)
                AddTemplate(template, chinese, null, macro);

            var loose = ScriptTextTemplate.BuildLooseFromPlainText(english);
            if (ScriptTextTemplate.LiteralLength(loose) >= 4
                && !string.Equals(loose, template, StringComparison.Ordinal))
            {
                AddTemplate(loose, chinese, null, macro);
            }

            // A prompt with several lines can change which detail lines the game shows. Match
            // the first line and let the trailing wildcard cover the rest.
            var firstLineEnd = english.IndexOf('\n');
            if (firstLineEnd > 0 && firstLineEnd < english.Length - 1)
            {
                var firstLine = english[..firstLineEnd].Trim();
                var prefix = ScriptTextTemplate.BuildLooseFromPlainText(firstLine)
                             + ScriptTextTemplate.Wildcard;
                if (ScriptTextTemplate.LiteralLength(prefix) >= 4)
                    AddTemplate(prefix, chinese, null, macro);
            }
        }

        // Blank placeholders in the English Addon row become double spaces. Treat each gap
        // between words as dynamic.
        if (english.Contains("  ", StringComparison.Ordinal))
        {
            var wordTemplate = ScriptTextTemplate.BuildWordGapTemplate(english);
            if (ScriptTextTemplate.LiteralLength(wordTemplate) >= 4)
                AddTemplate(wordTemplate, chinese, null, macro);
        }
    }

    /// <summary>
    /// Orders templates from the most literal text to the least. A template with fewer wildcards
    /// wins a tie, because it allows less text to vary.
    /// </summary>
    public void SortTemplates() => _templates.Sort(CompareTemplates);

    private void AddTemplate(string template, string chinese, int[]? indices, string? macro)
    {
        if (_templateKeys.Add(template))
            _templates.Add(new AddonTemplate(template, chinese, indices, macro));
    }

    private static int CompareTemplates(AddonTemplate left, AddonTemplate right)
    {
        var literal = ScriptTextTemplate.LiteralLength(right.Template)
            .CompareTo(ScriptTextTemplate.LiteralLength(left.Template));
        if (literal != 0)
            return literal;

        var wildcards = CountWildcards(left.Template).CompareTo(CountWildcards(right.Template));
        if (wildcards != 0)
            return wildcards;

        return string.CompareOrdinal(left.Template, right.Template);
    }

    private static int CountWildcards(string template)
    {
        var count = 0;
        foreach (var character in template)
        {
            if (character == ScriptTextTemplate.Wildcard)
                count++;
        }

        return count;
    }

    public void WriteTo(BinaryWriter writer)
    {
        IndexCacheFormat.WriteStrings(writer, _labels);

        writer.Write(_templates.Count);
        foreach (var template in _templates)
        {
            writer.Write(template.Template);
            writer.Write(template.Chinese);
            writer.Write(template.MacroTemplate is not null);
            if (template.MacroTemplate is not null)
                writer.Write(template.MacroTemplate);

            if (template.ParameterIndices is null)
            {
                writer.Write(-1);
                continue;
            }

            writer.Write(template.ParameterIndices.Length);
            foreach (var index in template.ParameterIndices)
                writer.Write(index);
        }

        IndexCacheFormat.WriteStringSet(writer, _templateKeys);
    }

    public void ReadFrom(BinaryReader reader)
    {
        IndexCacheFormat.ReadStrings(reader, _labels);

        var count = reader.ReadInt32();
        for (var i = 0; i < count; i++)
        {
            var template = reader.ReadString();
            var chinese = reader.ReadString();
            var macro = reader.ReadBoolean() ? reader.ReadString() : null;
            var length = reader.ReadInt32();

            int[]? indices = null;
            if (length >= 0)
            {
                indices = new int[length];
                for (var j = 0; j < length; j++)
                    indices[j] = reader.ReadInt32();
            }

            _templates.Add(new AddonTemplate(template, chinese, indices, macro));
        }

        IndexCacheFormat.ReadStringSet(reader, _templateKeys);
    }

    private readonly record struct AddonTemplate(
        string Template,
        string Chinese,
        int[]? ParameterIndices,
        string? MacroTemplate);
}
