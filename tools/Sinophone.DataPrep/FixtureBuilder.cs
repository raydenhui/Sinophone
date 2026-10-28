using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Sinophone.DataPrep;

/// <summary>
/// Builds the small test fixture checked into <c>tests/fixtures</c>.
/// </summary>
/// <remarks>
/// The fixture is a handful of real rows per sheet, in both Chinese variants, plus
/// the English text the plugin keys on. It exists so tests are fast and offline.
/// This lives in the tool rather than in a script because the datamining dumps have
/// duplicate column names that PowerShell's Import-Csv rejects.
/// </remarks>
internal static class FixtureBuilder
{
    /// <summary>
    /// Sheets to copy into the fixture. <see cref="EnglishColumn"/> is the column in
    /// the English dump; <see cref="LocalizedIndex"/> and <see cref="UseLastColumn"/>
    /// pick the CN/TC column for dumps that name none.
    /// </summary>
    private static readonly FixtureSheet[] FixtureSheets =
    [
        new("DefaultTalk", "Text", "Text[0]"),
        new("ENpcResident", "Singular", "Singular"),
        new("BNpcName", "Singular", "Singular"),
        new("WKSText", "Text", "Text", LocalizedIndex: 1),
        new("GimmickTalk", "Message", "Message", UseLastColumn: true),
    ];

    /// <summary>Script folders the fixture covers. The full build indexes more.</summary>
    private static readonly string[] FixtureScriptDirectories = ["custom", "cut_scene", "quest"];

    private const int FixtureScriptsPerDirectory = 3;

    public static int Run(string cnSource, string tcSource, string enSource, string outputDirectory, int rowCount)
    {
        foreach (var path in new[] { cnSource, tcSource, enSource })
        {
            if (!Directory.Exists(path))
            {
                Console.Error.WriteLine($"Source directory not found: {path}");
                return 2;
            }
        }

        if (rowCount < 1)
        {
            Console.Error.WriteLine("--rows must be at least 1.");
            return 2;
        }

        var cnRows = new Dictionary<string, Dictionary<string, Dictionary<string, string[]>>>(StringComparer.Ordinal);
        var tcRows = new Dictionary<string, Dictionary<string, Dictionary<string, string[]>>>(StringComparer.Ordinal);
        var paired = new List<PairedRow>();

        var cnScripts = new Dictionary<string, ScriptEntry>(StringComparer.Ordinal);
        var tcScripts = new Dictionary<string, ScriptEntry>(StringComparer.Ordinal);
        var pairedScripts = new List<PairedScript>();

        foreach (var spec in FixtureSheets)
        {
            var english = LoadEnglish(Path.Combine(enSource, spec.Sheet + ".csv"), spec.EnglishColumn);
            var cn = LoadLocalized(Path.Combine(cnSource, spec.Sheet + ".csv"), spec.EnglishColumn, spec.LocalizedIndex, spec.UseLastColumn);
            var tc = LoadLocalized(Path.Combine(tcSource, spec.Sheet + ".csv"), spec.EnglishColumn, spec.LocalizedIndex, spec.UseLastColumn);

            if (english.Count == 0 || cn.Count == 0 || tc.Count == 0)
            {
                Console.Error.WriteLine(
                    $"  ! {spec.Sheet} skipped (en={english.Count} cn={cn.Count} tc={tc.Count} usable rows)");
                continue;
            }

            var cnSheet = new Dictionary<string, Dictionary<string, string[]>>(StringComparer.Ordinal);
            var tcSheet = new Dictionary<string, Dictionary<string, string[]>>(StringComparer.Ordinal);

            var kept = 0;
            foreach (var (rowId, englishText) in english)
            {
                if (kept >= rowCount)
                    break;

                if (!cn.TryGetValue(rowId, out var cnText) || !tc.TryGetValue(rowId, out var tcText))
                    continue;

                var key = rowId.ToString(CultureInfo.InvariantCulture);
                cnSheet[key] = new Dictionary<string, string[]>(StringComparer.Ordinal) { [spec.Field] = [cnText] };
                tcSheet[key] = new Dictionary<string, string[]>(StringComparer.Ordinal) { [spec.Field] = [tcText] };

                paired.Add(new PairedRow(spec.Sheet, rowId, englishText, cnText, tcText));
                kept++;
            }

            Console.WriteLine($"{spec.Sheet,-16} {kept,4} row(s)");
            cnRows[spec.Sheet] = cnSheet;
            tcRows[spec.Sheet] = tcSheet;
        }

        foreach (var directory in FixtureScriptDirectories)
        {
            var cn = ScriptDump.TakePaired(cnSource, enSource, directory, FixtureScriptsPerDirectory);
            var tc = ScriptDump.TakePaired(tcSource, enSource, directory, FixtureScriptsPerDirectory)
                .ToDictionary(entry => entry.Key, StringComparer.Ordinal);

            var kept = 0;
            foreach (var entry in cn)
            {
                if (!tc.TryGetValue(entry.Key, out var translated))
                    continue;

                cnScripts[entry.Key] = new ScriptEntry { English = entry.English, Text = entry.Text };
                tcScripts[entry.Key] = new ScriptEntry { English = entry.English, Text = translated.Text };
                pairedScripts.Add(new PairedScript(entry.Key, entry.English, entry.Text, translated.Text));
                kept++;
            }

            Console.WriteLine($"script/{directory,-9} {kept,4} line(s)");
        }

        Directory.CreateDirectory(outputDirectory);

        WriteArtifact(Path.Combine(outputDirectory, "Sinophone.cn.json.gz"), "CN", cnRows, cnScripts);
        WriteArtifact(Path.Combine(outputDirectory, "Sinophone.tc.json.gz"), "TC", tcRows, tcScripts);
        WriteRows(Path.Combine(outputDirectory, "rows.json"), paired, pairedScripts);

        Console.WriteLine();
        Console.WriteLine(
            $"Fixture written to {outputDirectory} ({paired.Count} paired rows, {pairedScripts.Count} script lines).");
        return 0;
    }

    private static Dictionary<uint, string> LoadEnglish(string path, string column)
    {
        var result = new Dictionary<uint, string>();
        if (!File.Exists(path))
            return result;

        var (records, headerIndex) = ReadCsv(path);
        if (headerIndex < 0)
            return result;

        var index = Array.IndexOf(records[headerIndex], column);
        if (index < 0)
            return result;

        for (var i = headerIndex + 1; i < records.Count; i++)
        {
            var record = records[i];
            if (record.Length <= index)
                continue;

            if (!uint.TryParse(record[0].Trim(), out var rowId))
                continue;

            var value = record[index];
            if (!IsUsable(value))
                continue;

            result[rowId] = value;
        }

        return result;
    }

    /// <summary>
    /// Reads a CN/TC dump, which has three header rows: positional names, then the
    /// "#" names, then the column types.
    /// </summary>
    private static Dictionary<uint, string> LoadLocalized(string path, string column, int? index, bool useLastColumn)
    {
        var result = new Dictionary<uint, string>();
        if (!File.Exists(path))
            return result;

        var (records, headerIndex) = ReadCsv(path);
        if (headerIndex < 0)
            return result;

        var resolved = ResolveColumnIndex(records[headerIndex], column, index, useLastColumn);
        if (resolved < 0)
            return result;

        // Skip the "#" row and the type row.
        for (var i = headerIndex + 2; i < records.Count; i++)
        {
            var record = records[i];
            if (record.Length <= resolved)
                continue;

            if (!uint.TryParse(record[0].Trim(), out var rowId))
                continue;

            var value = record[resolved];
            if (!IsUsable(value))
                continue;

            result[rowId] = value;
        }

        return result;
    }

    /// <summary>
    /// Picks the CN/TC column for a sheet: a fixed index, the last column of the
    /// header row, or the named column when the dump has names.
    /// </summary>
    private static int ResolveColumnIndex(string[] header, string column, int? index, bool useLastColumn)
    {
        if (useLastColumn)
            return header.Length - 1;

        if (index is int fixedIndex)
            return fixedIndex;

        return Array.IndexOf(header, column);
    }

    private static bool IsUsable(string value)
        => !string.IsNullOrWhiteSpace(value) && value.Trim() != "0";

    private static (List<string[]> Records, int HeaderIndex) ReadCsv(string path)
    {
        var text = File.ReadAllText(path, Encoding.UTF8);
        if (text.Length > 0 && text[0] == '﻿')
            text = text[1..];

        var records = Csv.Parse(text);

        for (var i = 0; i < records.Count; i++)
        {
            if (records[i].Length > 0 && records[i][0].Trim() == "#")
                return (records, i);
        }

        return (records, -1);
    }

    private static void WriteArtifact(
        string path,
        string language,
        Dictionary<string, Dictionary<string, Dictionary<string, string[]>>> sheets,
        Dictionary<string, ScriptEntry> scripts)
    {
        var artifact = new Program.Artifact
        {
            FormatVersion = 1,
            Language = language,
            GeneratedAt = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            GameVersion = "fixture",
            Sheets = sheets,
            Scripts = scripts,
        };

        using var file = File.Create(path);
        using var gzip = new GZipStream(file, CompressionLevel.Optimal);
        JsonSerializer.Serialize(gzip, artifact, JsonOptions);
    }

    private static void WriteRows(string path, List<PairedRow> rows, List<PairedScript> scripts)
    {
        var payload = new
        {
            note = "Real rows from the CN/TC datamining dumps, paired with the English text the game shows. " +
                   "Generated once and checked in so tests are fast and offline. Do not hand-edit; " +
                   "regenerate with scripts/Update-TestFixture.ps1.",
            generatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            sources = new
            {
                en = "https://github.com/xivapi/ffxiv-datamining (csv/en)",
                cn = "https://github.com/thewakingsands/ffxiv-datamining-cn",
                tc = "https://github.com/thewakingsands/ffxiv-datamining-tc",
            },
            rows,
            scripts,
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

        File.WriteAllText(path, json + Environment.NewLine, new UTF8Encoding(false));
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    private sealed record FixtureSheet(
        string Sheet,
        string Field,
        string EnglishColumn,
        int? LocalizedIndex = null,
        bool UseLastColumn = false);

    private sealed record PairedRow(string Sheet, uint RowId, string English, string Cn, string Tc);

    private sealed record PairedScript(string Key, string English, string Cn, string Tc);
}
