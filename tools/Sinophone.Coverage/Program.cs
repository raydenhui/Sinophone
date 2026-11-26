using System.Net.Http;
using Sinophone;
using Sinophone.Coverage;
using Sinophone.Data;
using Sinophone.Handlers;

var options = Options.Parse(args);

if (options.ShowHelp)
{
    Options.PrintHelp();
    return 0;
}

if (options.ListSheets)
{
    foreach (var sheet in Sources.SheetsInUse())
        Console.WriteLine(sheet);

    return 0;
}

var dataDirectory = options.DataDirectory;
var enDirectory = options.EnDirectory ?? Path.Combine(dataDirectory, $"{options.Language}-csv");
var outputDirectory = options.OutputDirectory ?? Path.Combine(dataDirectory, "coverage");
var artifactPath = Path.Combine(dataDirectory, options.Variant == "cn" ? "Sinophone.cn.json.gz" : "Sinophone.tc.json.gz");

if (!File.Exists(artifactPath))
{
    Console.Error.WriteLine($"No artifact at {artifactPath}. Pass --data with the folder that holds the .json.gz files.");
    return 2;
}

Directory.CreateDirectory(enDirectory);

var needed = Sources.SheetsInUse().ToList();
var missing = needed
    .Select(sheet => Path.Combine(enDirectory, sheet + ".csv"))
    .Where(path => !File.Exists(path))
    .ToList();

if (missing.Count > 0)
{
    if (options.DryRun)
    {
        foreach (var path in missing)
            Console.WriteLine($"would download {Path.GetFileName(path)}");

        return 0;
    }

    using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    foreach (var path in missing)
    {
        var name = Path.GetFileName(path);
        var url = $"{options.DumpBaseUrl}/csv/{options.Language}/{Uri.EscapeDataString(name)}";
        Console.WriteLine($"downloading {name} …");

        try
        {
            var bytes = await http.GetByteArrayAsync(url);
            await File.WriteAllBytesAsync(path, bytes);
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine($"could not download {url}: {ex.Message}");
            return 2;
        }
    }
}

var log = new ConsoleLog();
var artifact = Runner.ReadArtifact(artifactPath);

var descriptors = new List<ISheetDescriptor>();
foreach (var (sheet, field, enColumn, isName) in Sources.Descriptors())
{
    var path = Path.Combine(enDirectory, sheet + ".csv");
    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"!! no {options.Language} dump for {sheet}");
        continue;
    }

    try
    {
        descriptors.Add(new EnCsvSheetDescriptor(sheet, field, isName, EnCsv.Load(path, enColumn)));
    }
    catch (InvalidDataException ex)
    {
        Console.Error.WriteLine($"!! {ex.Message}");
    }
}

var store = TranslationStore.Load(
    descriptors,
    options.Variant == "cn" ? ChineseVariant.Simplified : ChineseVariant.Traditional,
    useFallback: false,
    _ => File.OpenRead(artifactPath),
    log,
    fallbackResource: null,
    displayResolver: null,
    playerName: () => RuntimeCases.PlayerName);

if (store is null)
{
    Console.Error.WriteLine("The artifact could not be read.");
    return 2;
}

var probes = new Probes(store, Probes.Configuration);
var runner = new Runner(artifact, probes, enDirectory, options.Language);

Console.WriteLine($"artifact {Path.GetFileName(artifactPath)}, game version {artifact.GameVersion}, {store.TextEntryCount:N0} text entries");
Console.WriteLine();

var results = runner.Run();
Report.Write(
    outputDirectory,
    options.Variant.ToUpperInvariant(),
    artifact.GameVersion,
    options.Language,
    results,
    runner.Runtime);

Console.WriteLine($"{"component",-16} {"cases",10} {"text",10} {"text %",8} {"id",10} {"id %",8}");
foreach (var (name, tally) in results.OrderBy(entry => entry.Key, StringComparer.Ordinal))
{
    var text = tally.Total == 0 ? 0 : (double)tally.Resolved / tally.Total;
    var byId = tally.Total == 0 ? 0 : (double)tally.ResolvedById / tally.Total;
    Console.WriteLine(
        $"{name,-16} {tally.Total,10:N0} {tally.Resolved,10:N0} {text,8:P1} {tally.ResolvedById,10:N0} {byId,8:P1}");
}

Console.WriteLine();
Console.WriteLine($"report written to {outputDirectory}");

if (options.BaselinePath is not null)
{
    Report.WriteBaseline(options.BaselinePath, variant: options.Variant.ToUpperInvariant(), results);
    Console.WriteLine($"baseline written to {options.BaselinePath}");
}

if (options.GatePath is not null)
    return Report.Gate(options.GatePath, results);

return 0;

internal sealed class Options
{
    public string DataDirectory { get; private set; } = DefaultDataDirectory();

    public string? EnDirectory { get; private set; }

    public string? OutputDirectory { get; private set; }

    public string Variant { get; private set; } = "tc";

    /// <summary>The client language whose text is measured. Script coverage stays English-only.</summary>
    public string Language { get; private set; } = "en";

    public string? GatePath { get; private set; }

    public string? BaselinePath { get; private set; }

    public string DumpBaseUrl { get; private set; } = "https://raw.githubusercontent.com/xivapi/ffxiv-datamining/master";

    public bool ListSheets { get; private set; }

    public bool DryRun { get; private set; }

    public bool ShowHelp { get; private set; }

    private static string DefaultDataDirectory()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "XIVLauncher",
            "pluginConfigs",
            "Sinophone");

    public static Options Parse(string[] args)
    {
        var options = new Options();

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--data":
                    options.DataDirectory = Next(args, ref i);
                    break;
                case "--en":
                    options.EnDirectory = Next(args, ref i);
                    break;
                case "--out":
                    options.OutputDirectory = Next(args, ref i);
                    break;
                case "--variant":
                    options.Variant = Next(args, ref i).ToLowerInvariant();
                    break;
                case "--language":
                    options.Language = Next(args, ref i).ToLowerInvariant();
                    break;
                case "--gate":
                    options.GatePath = Next(args, ref i);
                    break;
                case "--baseline":
                    options.BaselinePath = Next(args, ref i);
                    break;
                case "--dump":
                    options.DumpBaseUrl = Next(args, ref i);
                    break;
                case "--list-sheets":
                    options.ListSheets = true;
                    break;
                case "--dry-run":
                    options.DryRun = true;
                    break;
                case "--help":
                case "-h":
                    options.ShowHelp = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {args[i]}. Use --help.");
            }
        }

        if (options.Language is not ("en" or "ja" or "de" or "fr"))
            throw new ArgumentException($"--language must be en, ja, de, or fr, not {options.Language}.");

        return options;
    }

    private static string Next(string[] args, ref int index)
    {
        if (index + 1 >= args.Length)
            throw new ArgumentException($"{args[index]} needs a value.");

        return args[++index];
    }

    public static void PrintHelp()
    {
        Console.WriteLine("""
            Sinophone translation coverage.

            Enumerates every string each component can show in one client language, runs it through
            that component's own resolve, and reports how many come back Chinese.

            Script lines and runtime labels are measured with the English dump only.

            --data <dir>       folder with Sinophone.cn.json.gz / Sinophone.tc.json.gz
                               (default: %APPDATA%/XIVLauncher/pluginConfigs/Sinophone)
            --language <code>  client language: en, ja, de, or fr (default: en)
            --en <dir>         folder with the dump CSVs (default: <data>/<language>-csv)
            --out <dir>        report folder (default: <data>/coverage)
            --variant <tc|cn>  artifact to measure (default: tc)
            --gate <file>      compare against a baseline and exit 1 on a drop
            --baseline <file>  write per-component totals only, for a later --gate
            --dump <url>       base URL of the datamining dump (default: xivapi master)
            --list-sheets      print the sheets the map uses, one per line
            --dry-run          print the downloads that are missing, then stop
            --help             this text
            """);
    }
}
