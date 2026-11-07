namespace Sinophone.Data;

/// <summary>
/// Converts Chinese text between simplified and traditional script.
/// </summary>
/// <remarks>
/// The tables come from OpenCC (Apache-2.0) and are built by
/// <c>scripts/New-ConversionData.ps1</c>. Each direction holds characters and words, and
/// the longest match wins, so 头发 becomes 頭髮 rather than 頭發.
/// A table is loaded the first time its direction is used.
/// </remarks>
public static class ChineseConverter
{
    private const string SimplifiedToTraditionalSuffix = "Conversion.s2t.txt.gz";
    private const string TraditionalToSimplifiedSuffix = "Conversion.t2s.txt.gz";

    private static readonly Lazy<Table?> SimplifiedToTraditional =
        new(() => LoadTable(SimplifiedToTraditionalSuffix));

    private static readonly Lazy<Table?> TraditionalToSimplified =
        new(() => LoadTable(TraditionalToSimplifiedSuffix));

    /// <summary>True when the table for <paramref name="conversion"/> is embedded and readable.</summary>
    public static bool IsAvailable(ChineseConversion conversion)
        => conversion switch
        {
            ChineseConversion.SimplifiedToTraditional => SimplifiedToTraditional.Value is not null,
            ChineseConversion.TraditionalToSimplified => TraditionalToSimplified.Value is not null,
            _ => true,
        };

    /// <summary>Returns <paramref name="text"/> in the other script, or unchanged text.</summary>
    public static string Convert(string? text, ChineseConversion conversion)
    {
        if (string.IsNullOrEmpty(text) || conversion == ChineseConversion.None)
            return text ?? string.Empty;

        var (table, other) = conversion == ChineseConversion.SimplifiedToTraditional
            ? (SimplifiedToTraditional.Value, TraditionalToSimplified.Value)
            : (TraditionalToSimplified.Value, SimplifiedToTraditional.Value);

        if (table is null)
            return text;

        // Text that is already in the target script must not be converted. Converting
        // it can only corrupt shared characters: already-traditional text goes through
        // the simplified keys and comes out wrong. The user can select any variant, so
        // the script of the text decides, not the setting.
        return IsMostlySourceScript(text, table, other) ? table.Convert(text) : text;
    }

    /// <summary>
    /// True when more characters need this table than need the reverse table. Text with
    /// no characters from either table (Latin, numbers, punctuation) is left alone.
    /// </summary>
    private static bool IsMostlySourceScript(string text, Table source, Table? target)
    {
        var sourceCount = 0;
        var targetCount = 0;

        foreach (var character in text)
        {
            if (source.ChangesCharacter(character))
                sourceCount++;

            if (target is not null && target.ChangesCharacter(character))
                targetCount++;
        }

        return sourceCount > targetCount;
    }

    private static Table? LoadTable(string suffix)
    {
        var assembly = typeof(ChineseConverter).Assembly;
        var name = assembly.GetManifestResourceNames()
            .FirstOrDefault(resource => resource.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

        if (name is null)
            return null;

        using var stream = assembly.GetManifestResourceStream(name);
        if (stream is null)
            return null;

        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);

        var entries = new Dictionary<string, string>(StringComparer.Ordinal);

        while (reader.ReadLine() is { } line)
        {
            // Lines are "key<TAB>value"; '#' starts the provenance header.
            if (line.Length < 3 || line[0] == '#')
                continue;

            var tab = line.IndexOf('\t');
            if (tab <= 0 || tab == line.Length - 1)
                continue;

            entries[line[..tab]] = line[(tab + 1)..];
        }

        return entries.Count == 0 ? null : new Table(entries);
    }

    private sealed class Table
    {
        private readonly Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> _lookup;

        /// <summary>Single characters this table rewrites, used to tell the scripts apart.</summary>
        private readonly HashSet<char> _changedCharacters = new();

        /// <summary>Longest key that starts with a character, so most positions try one slice.</summary>
        private readonly int[] _maxLengthByFirstChar = new int[char.MaxValue + 1];

        public Table(Dictionary<string, string> entries)
        {
            _lookup = entries.GetAlternateLookup<ReadOnlySpan<char>>();

            foreach (var (key, value) in entries)
            {
                if (key.Length == 1 && !string.Equals(key, value, StringComparison.Ordinal))
                    _changedCharacters.Add(key[0]);

                if (key.Length > _maxLengthByFirstChar[key[0]])
                    _maxLengthByFirstChar[key[0]] = key.Length;
            }
        }

        /// <summary>True when this table rewrites the character on its own.</summary>
        public bool ChangesCharacter(char character) => _changedCharacters.Contains(character);

        public string Convert(string text)
        {
            StringBuilder? output = null;
            var copied = 0;
            var index = 0;

            while (index < text.Length)
            {
                var maxLength = _maxLengthByFirstChar[text[index]];
                if (maxLength == 0)
                {
                    index++;
                    continue;
                }

                var remaining = text.Length - index;
                if (maxLength > remaining)
                    maxLength = remaining;

                string? replacement = null;
                var matched = 0;

                for (var length = maxLength; length >= 1; length--)
                {
                    if (!_lookup.TryGetValue(text.AsSpan(index, length), out var value))
                        continue;

                    replacement = value;
                    matched = length;
                    break;
                }

                if (replacement is null)
                {
                    index++;
                    continue;
                }

                output ??= new StringBuilder(text.Length);
                output.Append(text, copied, index - copied);
                output.Append(replacement);
                index += matched;
                copied = index;
            }

            if (output is null)
                return text;

            output.Append(text, copied, text.Length - copied);
            return output.ToString();
        }
    }
}
