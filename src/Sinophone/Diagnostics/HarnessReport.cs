using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sinophone.Diagnostics;

/// <summary>What one case did on one run.</summary>
public sealed record HarnessResult(
    string Id,
    HarnessSurface Surface,
    HarnessOutcome Outcome,
    string English,
    string? Drawn = null,
    string? Detail = null)
{
    /// <summary>Findings behind the outcome, in the order the checks ran.</summary>
    public IReadOnlyList<HarnessFinding> Findings { get; init; } = [];

    /// <summary>Full measurement, so a report can be re-read without the game.</summary>
    public HarnessMeasurement? Measurement { get; init; }

    /// <summary>True when a failure happened and the detail says why.</summary>
    public bool Failed => Outcome == HarnessOutcome.Failed;
}

/// <summary>
/// One run: the cases, what each did, and when. Writes to JSON so a run can be kept next to
/// the plugin and compared with a later run.
/// </summary>
public sealed class HarnessReport
{
    /// <summary>Format version, so a reader can refuse a report it does not understand.</summary>
    public const int FormatVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>When the run started, in UTC.</summary>
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>When the run finished.</summary>
    public DateTimeOffset FinishedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Variant the run used, as the settings name it.</summary>
    public string Variant { get; init; } = string.Empty;

    /// <summary>Artifact game version, when the store exposes one.</summary>
    public string? GameVersion { get; init; }

    /// <summary>Results in the order the cases ran.</summary>
    public List<HarnessResult> Results { get; init; } = [];

    /// <summary>Format version this file was written with.</summary>
    public int Version { get; init; } = FormatVersion;

    /// <summary>Cases that passed.</summary>
    [JsonIgnore]
    public int PassedCount => Results.Count(static result => result.Outcome == HarnessOutcome.Passed);

    /// <summary>Cases that failed.</summary>
    [JsonIgnore]
    public int FailedCount => Results.Count(static result => result.Outcome == HarnessOutcome.Failed);

    /// <summary>Cases that were skipped.</summary>
    [JsonIgnore]
    public int SkippedCount => Results.Count(static result => result.Outcome == HarnessOutcome.Skipped);

    /// <summary>True when every case that ran passed.</summary>
    [JsonIgnore]
    public bool AllPassed => FailedCount == 0;

    /// <summary>Adds one result and returns it.</summary>
    public HarnessResult Add(HarnessResult result)
    {
        Results.Add(result);
        return result;
    }

    /// <summary>
    /// A one-line summary for the tab and the log. Counts, not percentages, because a run
    /// with four cases would otherwise read as a 75 % score.
    /// </summary>
    public string Summary()
        => $"{PassedCount} passed, {FailedCount} failed, {SkippedCount} skipped of {Results.Count}.";

    /// <summary>Writes the report as JSON.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>
    /// Reads a report. Returns null when the JSON is not a report this version understands,
    /// so a stale file never crashes the tab.
    /// </summary>
    public static HarnessReport? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            // A stray JSON object is not a report, and deserializing it would leave every
            // property at its default and look like a valid empty run. Require the two keys a
            // reader depends on before trusting the rest.
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty(nameof(Version), out var version)
                || !version.TryGetInt32(out var versionNumber)
                || versionNumber != FormatVersion
                || !root.TryGetProperty(nameof(Results), out var results)
                || results.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            return JsonSerializer.Deserialize<HarnessReport>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The default file name for a run, keyed by the time it started so runs do not
    /// overwrite each other.
    /// </summary>
    public static string FileNameFor(DateTimeOffset startedAt)
        => $"harness-{startedAt:yyyyMMdd-HHmmss}.json";
}
