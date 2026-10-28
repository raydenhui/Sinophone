using System.Text;

namespace Sinophone.DataPrep;

/// <summary>
/// Reads the game's script dumps. These are CSV files under folders such as
/// <c>custom/</c> and <c>cut_scene/</c>, keyed by a text key instead of a row id.
/// The CN and TC dumps start with three header rows; the English dump has none.
/// </summary>
/// <remarks>
/// <c>quest/</c> is deliberately left out. It holds quest journal text, which the
/// Talk overlay never shows, and it is about ten times the size of everything else.
/// See issue #34.
/// </remarks>
internal static class ScriptDump
{
    /// <summary>Script folders to index, in the order they are reported.</summary>
    public static readonly string[] Directories =
    [
        "content",
        "custom",
        "cut_scene",
        "dungeon",
        "guild_order",
        "leve",
        "opening",
        "raid",
        "shop",
        "story",
        "system",
        "transport",
        "warp",
    ];

    /// <summary>
    /// Pairs every localized line with its English text. Keys are unique across all
    /// folders, so the result is one flat map.
    /// </summary>
    public static Dictionary<string, ScriptEntry> Build(string localizedRoot, string englishRoot)
    {
        var result = new Dictionary<string, ScriptEntry>(StringComparer.Ordinal);

        foreach (var directory in Directories)
        {
            var localized = LoadDirectory(Path.Combine(localizedRoot, directory));
            var english = LoadDirectory(Path.Combine(englishRoot, directory));

            var paired = 0;
            foreach (var (key, text) in localized)
            {
                if (!english.TryGetValue(key, out var englishText) || string.IsNullOrWhiteSpace(englishText))
                    continue;

                if (string.IsNullOrWhiteSpace(text))
                    continue;

                if (result.TryAdd(key, new ScriptEntry { English = englishText, Text = text }))
                    paired++;
            }

            Console.WriteLine($"  script/{directory,-11} {paired,7:N0} lines");
        }

        return result;
    }

    /// <summary>
    /// Reads a few paired lines from one folder. Used for the test fixture, where
    /// loading the whole folder would be wasted work.
    /// </summary>
    public static List<PairedScript> TakePaired(
        string localizedRoot,
        string englishRoot,
        string directory,
        int count)
    {
        var result = new List<PairedScript>(count);
        var localizedDirectory = Path.Combine(localizedRoot, directory);
        var englishDirectory = Path.Combine(englishRoot, directory);

        if (!Directory.Exists(localizedDirectory) || !Directory.Exists(englishDirectory))
            return result;

        foreach (var path in SortedCsvFiles(localizedDirectory))
        {
            if (result.Count >= count)
                break;

            var relative = Path.GetRelativePath(localizedDirectory, path);
            var englishPath = Path.Combine(englishDirectory, relative);
            if (!File.Exists(englishPath))
                continue;

            var localized = LoadFile(path);
            var english = LoadFile(englishPath);

            foreach (var key in localized.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                if (result.Count >= count)
                    break;

                if (!english.TryGetValue(key, out var englishText) || string.IsNullOrWhiteSpace(englishText))
                    continue;

                if (string.IsNullOrWhiteSpace(localized[key]))
                    continue;

                result.Add(new PairedScript(key, englishText, localized[key]));
            }
        }

        return result;
    }

    internal static Dictionary<string, string> LoadDirectory(string directory)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        if (!Directory.Exists(directory))
        {
            Console.Error.WriteLine($"  ! script folder not found: {directory}");
            return result;
        }

        foreach (var path in SortedCsvFiles(directory))
        {
            foreach (var (key, text) in LoadFile(path))
                result.TryAdd(key, text);
        }

        return result;
    }

    /// <summary>Reads one dump file. Returns text key to text.</summary>
    internal static Dictionary<string, string> LoadFile(string path)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        var text = File.ReadAllText(path, Encoding.UTF8);
        if (text.Length > 0 && text[0] == '﻿')
            text = text[1..];

        var records = Csv.Parse(text);
        var start = FindDataStart(records);

        for (var i = start; i < records.Count; i++)
        {
            var record = records[i];
            if (record.Length < 3)
                continue;

            var key = record[1].Trim();
            if (key.Length == 0)
                continue;

            result.TryAdd(key, record[2]);
        }

        return result;
    }

    private static IEnumerable<string> SortedCsvFiles(string directory)
        => Directory
            .EnumerateFiles(directory, "*.csv", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal);

    /// <summary>
    /// Finds the first data row. The CN and TC dumps have <c>key,0,1</c>, then
    /// <c>#,,</c>, then <c>int32,str,str</c>. The English dump starts at row 0.
    /// </summary>
    private static int FindDataStart(List<string[]> records)
    {
        if (records.Count == 0 || records[0].Length == 0 ||
            !string.Equals(records[0][0].Trim(), "key", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        for (var i = 0; i < records.Count; i++)
        {
            if (records[i].Length > 0 && records[i][0].Trim() == "#")
                return Math.Min(i + 2, records.Count);
        }

        return records.Count;
    }

    internal sealed record PairedScript(string Key, string English, string Text);
}

/// <summary>One script line in the artifact. Mirrors the plugin's shape.</summary>
internal sealed class ScriptEntry
{
    public string English { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;
}
