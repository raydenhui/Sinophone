using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Sinophone.DataPrep;

/// <summary>
/// Minimal CSV reader for the datamining dumps. Handles quoted fields, escaped
/// quotes, and CRLF or LF line endings.
/// </summary>
public static class Csv
{
    public static List<string[]> Parse(string text)
    {
        var records = new List<string[]>();
        var fields = new List<string>();
        var builder = new StringBuilder();
        var inQuotes = false;
        var fieldStarted = false;

        void EndField()
        {
            fields.Add(builder.ToString());
            builder.Clear();
            fieldStarted = false;
        }

        void EndRecord()
        {
            EndField();

            if (!(fields.Count == 1 && fields[0].Length == 0))
                records.Add(fields.ToArray());

            fields.Clear();
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        builder.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    builder.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    fieldStarted = true;
                    break;
                case ',':
                    EndField();
                    fieldStarted = true;
                    break;
                case '\r':
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                        i++;
                    EndRecord();
                    break;
                case '\n':
                    EndRecord();
                    break;
                default:
                    builder.Append(c);
                    fieldStarted = true;
                    break;
            }
        }

        if (builder.Length > 0 || fields.Count > 0 || fieldStarted)
            EndRecord();

        return records;
    }
}

/// <summary>
/// Converts the ffxiv-datamining-cn / ffxiv-datamining-tc CSV dumps into the gzipped
/// JSON artifacts embedded in the Sinophone plugin.
/// </summary>
internal static class Program
{
    private static readonly SheetSpec[] Sheets =
    [
        new("DefaultTalk", "Text", ["Text[0]", "Text[1]", "Text[2]"]),
        new("Warp", "Question", ["Question"]),
        new("Balloon", "Dialogue", ["Dialogue"]),
        // The CN/TC NpcYell header calls column 12 "Text", but the row's text is
        // column 14. Read it by position so rows do not pick up a nearby flag value.
        new("NpcYell", "Text", []) { Indices = [14] },
        new("InstanceContentTextData", "Text", ["Text"]),
        new("ContentTalk", "Text", ["Text"]),
        new("SnipeTalk", "Text", ["Text"]),
        new("GuildleveAssignmentTalk", "Talk",
            ["Talk[0]", "Talk[1]", "Talk[2]", "Talk[3]", "Talk[4]", "Talk[5]", "Talk[6]", "Talk[7]"]),
        new("CraftLeveTalk", "Talk", ["Talk[0]", "Talk[1]", "Talk[2]", "Talk[3]", "Talk[4]", "Talk[5]"]),
        new("GoldSaucerTextData", "Text", ["Text"]),
        new("CSBonusTextData", "Text", ["Text"]),
        new("RideShootingTextData", "String", ["String"]),
        new("QuickChatTransient", "Text", ["Text{Output}"]),
        new("PublicContentTextData", "TextData", ["TextData"]),
        new("PartyContentTextData", "Data", ["Data"]),

        // The CN and TC dumps name no columns for these sheets, so the tool reads
        // them by position. Column numbers count the row id as 0.
        new("GimmickTalk", "Message", []) { UseLastColumn = true },
        new("GimmickBill", "Text", []) { Indices = [1] },
        new("GimmickYesNo", "Message", []) { Indices = [1] },
        new("GimmickYesNo", "YesButton", []) { Indices = [2] },
        new("GimmickYesNo", "NoButton", []) { Indices = [3] },
        new("GoldSaucerTalk", "Message", []) { Indices = [18] },
        new("GoldSaucerTalk", "ChoicesText", []) { RangeStart = 19 },
        new("MassivePcContentTextData", "Text", []) { Indices = [1] },
        new("WKSMissionText", "Text", []) { Indices = [1] },
        new("WKSText", "Text", []) { Indices = [1] },
        new("WKSEmergencyInfoText", "Text", []) { Indices = [1] },
        new("HWDDevLevelWebText", "Unknown0", []) { Indices = [1] },
        new("HWDDevLevelWebText", "Unknown1", []) { Indices = [2] },
        new("InclusionShopWelcomText", "Unknown0", []) { Indices = [1] },
        new("Addon", "Text", []) { Indices = [1] },

        new("MKDLore", "Name", ["Name"]),
        new("MKDLore", "Description", ["Description"]),
        new("ENpcResident", "Singular", ["Singular"]),
        new("BNpcName", "Singular", ["Singular"]),
        new("EObjName", "Singular", ["Singular"]),
        new("SnipeTalkName", "Name", ["Name"]),
        new("Item", "Name", ["Name"]),
    ];

    private static int Main(string[] args)
    {
        Options options;
        try
        {
            options = Options.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            PrintUsage();
            return 2;
        }

        if (options.ListSheets)
        {
            // The data workflow reads this list instead of keeping its own copy.
            foreach (var spec in Sheets)
                Console.WriteLine($"sheet:{spec.Sheet}");

            return 0;
        }

        if (options.ListScripts)
        {
            foreach (var scriptDirectory in ScriptDump.Directories)
                Console.WriteLine($"script:{scriptDirectory}");

            return 0;
        }

        if (options.FixtureOutputDirectory is not null)
        {
            return FixtureBuilder.Run(
                options.SourceDirectory,
                options.FixtureTcSource!,
                options.EnSourceDirectory!,
                options.FixtureOutputDirectory,
                options.FixtureRowCount);
        }

        if (!Directory.Exists(options.SourceDirectory))
        {
            Console.Error.WriteLine($"Source directory not found: {options.SourceDirectory}");
            return 2;
        }

        var artifact = new Artifact
        {
            FormatVersion = 1,
            Language = options.Language,
            GeneratedAt = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
            GameVersion = options.GameVersion,
        };

        var totalRows = 0;
        var totalSlots = 0;

        if (options.ScriptSourceDirectory is not null)
        {
            if (!Directory.Exists(options.ScriptSourceDirectory))
            {
                Console.Error.WriteLine($"Script source directory not found: {options.ScriptSourceDirectory}");
                return 2;
            }

            if (!Directory.Exists(options.EnSourceDirectory!))
            {
                Console.Error.WriteLine($"English source directory not found: {options.EnSourceDirectory}");
                return 2;
            }

            Console.WriteLine();
            Console.WriteLine("Script dumps");
            artifact.Scripts = ScriptDump.Build(options.ScriptSourceDirectory, options.EnSourceDirectory!);
        }

        foreach (var spec in Sheets)
        {
            var rows = ProcessSheet(spec, options.SourceDirectory);
            if (rows is null || rows.Count == 0)
            {
                Console.WriteLine($"{spec.Sheet,-26} skipped");
                continue;
            }

            // A sheet can have more than one spec, one per text field. Merge the
            // fields into the same row instead of replacing the sheet.
            if (!artifact.Sheets.TryGetValue(spec.Sheet, out var sheetRows))
            {
                sheetRows = new Dictionary<string, Dictionary<string, string[]>>(StringComparer.Ordinal);
                artifact.Sheets[spec.Sheet] = sheetRows;
            }

            var addedRows = 0;
            foreach (var (rowId, fields) in rows)
            {
                if (!sheetRows.TryGetValue(rowId, out var row))
                {
                    row = new Dictionary<string, string[]>(StringComparer.Ordinal);
                    sheetRows[rowId] = row;
                    addedRows++;
                }

                foreach (var (field, values) in fields)
                    row[field] = values;
            }

            totalRows += addedRows;

            var slots = rows.Values.Sum(fields => fields[spec.Field].Length);
            totalSlots += slots;

            Console.WriteLine($"{spec.Sheet,-26} {rows.Count,7:N0} rows  {slots,8:N0} slots");
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(options.OutputPath));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var jsonOptions = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = false,
        };

        using (var file = File.Create(options.OutputPath))
        using (var gzip = new GZipStream(file, CompressionLevel.Optimal))
        {
            JsonSerializer.Serialize(gzip, artifact, jsonOptions);
        }

        var size = new FileInfo(options.OutputPath).Length;
        Console.WriteLine();
        Console.WriteLine($"Language      : {options.Language}");
        Console.WriteLine($"Sheets        : {artifact.Sheets.Count:N0}");
        Console.WriteLine($"Rows          : {totalRows:N0}");
        Console.WriteLine($"Slots         : {totalSlots:N0}");
        Console.WriteLine($"Scripts       : {artifact.Scripts.Count:N0}");
        Console.WriteLine($"Output        : {options.OutputPath} ({size / 1024.0 / 1024.0:F2} MiB)");
        return 0;
    }

    internal static Dictionary<string, Dictionary<string, string[]>>? ProcessSheet(SheetSpec spec, string sourceDirectory)
    {
        var path = Path.Combine(sourceDirectory, spec.Sheet + ".csv");
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"  ! {spec.Sheet}.csv not found");
            return null;
        }

        var text = File.ReadAllText(path, Encoding.UTF8);
        if (text.Length > 0 && text[0] == '\uFEFF')
            text = text[1..];

        var records = Csv.Parse(text);

        var headerIndex = -1;
        for (var i = 0; i < records.Count; i++)
        {
            if (records[i].Length > 0 && records[i][0].Trim() == "#")
            {
                headerIndex = i;
                break;
            }
        }

        if (headerIndex < 0)
        {
            Console.Error.WriteLine($"  ! {spec.Sheet}.csv has no '#' header row");
            return null;
        }

        var columnMap = new Dictionary<string, int>(StringComparer.Ordinal);
        var header = records[headerIndex];

        for (var i = 0; i < header.Length; i++)
        {
            var name = header[i].Trim();
            if (name.Length > 0)
                columnMap[name] = i;
        }

        var namedIndices = new List<int>(spec.Columns.Length);
        foreach (var column in spec.Columns)
        {
            if (columnMap.TryGetValue(column, out var index))
                namedIndices.Add(index);
        }

        if (!spec.IsPositional && namedIndices.Count == 0)
        {
            Console.Error.WriteLine($"  ! {spec.Sheet}.csv has none of the expected columns ({string.Join(", ", spec.Columns)})");
            return null;
        }

        var rows = new Dictionary<string, Dictionary<string, string[]>>(StringComparer.Ordinal);

        var indices = ResolveIndices(spec, header.Length, namedIndices);
        if (indices.Count == 0)
            return rows;

        for (var i = headerIndex + 1; i < records.Count; i++)
        {
            var record = records[i];
            if (record.Length == 0)
                continue;

            var key = record[0].Trim();
            if (key.Length == 0 || !uint.TryParse(key, out _))
                continue;

            var slots = new string[indices.Count];
            var any = false;

            for (var slot = 0; slot < indices.Count; slot++)
            {
                var index = indices[slot];
                var raw = index < record.Length ? record[index] : string.Empty;
                var value = NormalizeValue(raw);
                slots[slot] = value;

                if (value.Length > 0)
                    any = true;
            }

            if (!any)
                continue;

            rows.TryAdd(key, new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [spec.Field] = slots,
            });
        }

        return rows;
    }

    /// <summary>
    /// Picks the columns to read from one sheet. Named specs use the "#" header row;
    /// positional specs use fixed indices, the last column, or a range to the end.
    /// </summary>
    private static IReadOnlyList<int> ResolveIndices(SheetSpec spec, int columnCount, IReadOnlyList<int> namedIndices)
    {
        if (spec.UseLastColumn)
            return columnCount > 1 ? [columnCount - 1] : [];

        if (spec.RangeStart is int start)
        {
            if (columnCount <= start)
                return [];

            var range = new int[columnCount - start];
            for (var i = 0; i < range.Length; i++)
                range[i] = start + i;
            return range;
        }

        if (spec.Indices is not null)
            return spec.Indices;

        return namedIndices;
    }

    private static string NormalizeValue(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return string.Empty;

        var trimmed = raw.Trim();
        if (trimmed.Length == 0 || trimmed == "0")
            return string.Empty;

        return raw;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  Sinophone.DataPrep --source <csv-directory> --language <CN|TC> --output <file.json.gz> [--game-version <version>]");
        Console.WriteLine("      [--script-source <dump-root> --en-source <en-dump-root>]");
        Console.WriteLine("  Sinophone.DataPrep --fixture --source <cn-csv> --tc-source <tc-csv> --en-source <en-csv> --fixture-output <dir> [--rows <n>]");
        Console.WriteLine("  Sinophone.DataPrep --list-sheets");
        Console.WriteLine("  Sinophone.DataPrep --list-scripts");
        Console.WriteLine();
        Console.WriteLine("Example:");
        Console.WriteLine(@"  Sinophone.DataPrep --source C:\ffxiv-datamining-cn --language CN --output ..\src\Sinophone\Resources\Sinophone.cn.json.gz --game-version 7.4 --script-source C:\ffxiv-datamining-cn --en-source C:\ffxiv-datamining\csv\en");
        Console.WriteLine(@"  Sinophone.DataPrep --fixture --source C:\ffxiv-datamining-cn --tc-source C:\ffxiv-datamining-tc --en-source C:\ffxiv-datamining\csv\en --fixture-output ..\tests\fixtures");
    }

    internal sealed record SheetSpec(string Sheet, string Field, string[] Columns)
    {
        /// <summary>Fixed zero-based column indices for dumps that name no columns.</summary>
        public int[]? Indices { get; init; }

        /// <summary>Read the last column of the row, whatever its position.</summary>
        public bool UseLastColumn { get; init; }

        /// <summary>Read from this zero-based column to the last column of the row.</summary>
        public int? RangeStart { get; init; }

        public bool IsPositional => Indices is not null || UseLastColumn || RangeStart is not null;
    }

    internal sealed class Artifact
    {
        public int FormatVersion { get; set; } = 1;
        public string Language { get; set; } = string.Empty;
        public string GeneratedAt { get; set; } = string.Empty;
        public string GameVersion { get; set; } = string.Empty;
        public Dictionary<string, Dictionary<string, Dictionary<string, string[]>>> Sheets { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, ScriptEntry> Scripts { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class Options
    {
        public string SourceDirectory { get; private init; } = string.Empty;
        public string Language { get; private init; } = "CN";
        public string OutputPath { get; private init; } = string.Empty;
        public string GameVersion { get; private init; } = "unknown";

        /// <summary>Print the sheet names the tool expects and exit.</summary>
        public bool ListSheets { get; private init; }

        /// <summary>Print the script folder names the tool expects and exit.</summary>
        public bool ListScripts { get; private init; }

        /// <summary>Root of the localized script dumps, for example the CN repo checkout.</summary>
        public string? ScriptSourceDirectory { get; private init; }

        /// <summary>Root of the English dump, for example <c>ffxiv-datamining/csv/en</c>.</summary>
        public string? EnSourceDirectory { get; private init; }

        /// <summary>Set when building the test fixture instead of a full artifact.</summary>
        public string? FixtureOutputDirectory { get; private init; }
        public string? FixtureTcSource { get; private init; }
        public int FixtureRowCount { get; private init; } = 8;

        public static Options Parse(string[] args)
        {
            string? source = null;
            string? language = null;
            string? output = null;
            string? fixtureOutput = null;
            string? tcSource = null;
            string? enSource = null;
            string? scriptSource = null;
            var gameVersion = "unknown";
            var rowCount = 8;
            var fixture = false;
            var listSheets = false;
            var listScripts = false;

            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                string? value = i + 1 < args.Length ? args[i + 1] : null;

                switch (arg)
                {
                    case "--source":
                        source = value ?? throw new ArgumentException("--source requires a directory.");
                        i++;
                        break;
                    case "--language":
                        language = value ?? throw new ArgumentException("--language requires CN or TC.");
                        i++;
                        break;
                    case "--output":
                        output = value ?? throw new ArgumentException("--output requires a file path.");
                        i++;
                        break;
                    case "--game-version":
                        gameVersion = value ?? throw new ArgumentException("--game-version requires a value.");
                        i++;
                        break;
                    case "--fixture":
                        fixture = true;
                        break;
                    case "--list-sheets":
                        listSheets = true;
                        break;
                    case "--list-scripts":
                        listScripts = true;
                        break;
                    case "--script-source":
                        scriptSource = value ?? throw new ArgumentException("--script-source requires a directory.");
                        i++;
                        break;
                    case "--fixture-output":
                        fixtureOutput = value ?? throw new ArgumentException("--fixture-output requires a directory.");
                        i++;
                        break;
                    case "--tc-source":
                        tcSource = value ?? throw new ArgumentException("--tc-source requires a directory.");
                        i++;
                        break;
                    case "--en-source":
                        enSource = value ?? throw new ArgumentException("--en-source requires a directory.");
                        i++;
                        break;
                    case "--rows":
                        if (value is null || !int.TryParse(value, out rowCount) || rowCount < 1)
                            throw new ArgumentException("--rows requires a positive number.");
                        i++;
                        break;
                    case "-h":
                    case "--help":
                        throw new ArgumentException("Help requested.");
                    default:
                        throw new ArgumentException($"Unknown argument: {arg}");
                }
            }

            if (listSheets)
                return new Options { ListSheets = true };

            if (listScripts)
                return new Options { ListScripts = true };

            if (string.IsNullOrWhiteSpace(source))
                throw new ArgumentException("--source is required.");

            if (fixture)
            {
                if (string.IsNullOrWhiteSpace(tcSource))
                    throw new ArgumentException("--tc-source is required with --fixture.");

                if (string.IsNullOrWhiteSpace(enSource))
                    throw new ArgumentException("--en-source is required with --fixture.");

                if (string.IsNullOrWhiteSpace(fixtureOutput))
                    throw new ArgumentException("--fixture-output is required with --fixture.");

                return new Options
                {
                    SourceDirectory = source,
                    FixtureOutputDirectory = fixtureOutput,
                    FixtureTcSource = tcSource,
                    EnSourceDirectory = enSource,
                    FixtureRowCount = rowCount,
                };
            }

            if (!string.IsNullOrWhiteSpace(scriptSource) && string.IsNullOrWhiteSpace(enSource))
                throw new ArgumentException("--en-source is required with --script-source.");

            if (string.IsNullOrWhiteSpace(language))
                throw new ArgumentException("--language is required.");

            language = language.Trim().ToUpperInvariant();
            if (language is not ("CN" or "TC"))
                throw new ArgumentException("--language must be CN or TC.");

            if (string.IsNullOrWhiteSpace(output))
                throw new ArgumentException("--output is required.");

            return new Options
            {
                SourceDirectory = source,
                Language = language,
                OutputPath = output,
                GameVersion = gameVersion,
                ScriptSourceDirectory = scriptSource,
                EnSourceDirectory = enSource,
            };
        }
    }
}
