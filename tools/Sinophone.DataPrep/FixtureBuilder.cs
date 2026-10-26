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
    private static readonly Program.SheetSpec[] FixtureSheets =
    [
        new("DefaultTalk", "Text", ["Text[0]", "Text[1]", "Text[2]"]),
        new("ENpcResident", "Singular", ["Singular"]),
        new("BNpcName", "Singular", ["Singular"]),
    ];

    /// <summary>Column that holds the English text for each fixture sheet.</summary>
    private static readonly Dictionary<string, string> EnglishColumns = new(StringComparer.Ordinal)
    {
        ["DefaultTalk"] = "Text[0]",
        ["ENpcResident"] = "Singular",
        ["BNpcName"] = "Singular",
    };

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

        foreach (var spec in FixtureSheets)
        {
            var english = LoadEnglish(Path.Combine(enSource, spec.Sheet + ".csv"), EnglishColumns[spec.Sheet]);
            var cn = LoadLocalized(Path.Combine(cnSource, spec.Sheet + ".csv"), EnglishColumns[spec.Sheet]);
            var tc = LoadLocalized(Path.Combine(tcSource, spec.Sheet + ".csv"), EnglishColumns[spec.Sheet]);

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

        Directory.CreateDirectory(outputDirectory);

        WriteArtifact(Path.Combine(outputDirectory, "Sinophone.cn.json.gz"), "CN", cnRows);
        WriteArtifact(Path.Combine(outputDirectory, "Sinophone.tc.json.gz"), "TC", tcRows);
        WriteRows(Path.Combine(outputDirectory, "rows.json"), paired);

        Console.WriteLine();
        Console.WriteLine($"Fixture written to {outputDirectory} ({paired.Count} paired rows).");
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
    private static Dictionary<uint, string> LoadLocalized(string path, string column)
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

        // Skip the "#" row and the type row.
        for (var i = headerIndex + 2; i < records.Count; i++)
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
        Dictionary<string, Dictionary<string, Dictionary<string, string[]>>> sheets)
    {
        var artifact = new Program.Artifact
        {
            FormatVersion = 1,
            Language = language,
            GeneratedAt = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            GameVersion = "fixture",
            Sheets = sheets,
        };

        using var file = File.Create(path);
        using var gzip = new GZipStream(file, CompressionLevel.Optimal);
        JsonSerializer.Serialize(gzip, artifact, JsonOptions);
    }

    private static void WriteRows(string path, List<PairedRow> rows)
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

    private sealed record PairedRow(string Sheet, uint RowId, string English, string Cn, string Tc);
}
