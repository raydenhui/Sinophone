using System.Text.Json.Serialization;

namespace Sinophone.Diagnostics;

/// <summary>How a lookup ended. A dev build writes the interesting ones to disk.</summary>
public enum FailureOutcome
{
    Hit,
    Fallback,
    Miss,
    Partial,
    Error,
}

/// <summary>Which outcomes a recorder keeps. Set with <c>SINOPHONE_FAILURE_LEVEL</c>.</summary>
public enum FailureLevel
{
    /// <summary>Only misses and errors.</summary>
    Miss,

    /// <summary>Misses, errors, partial results, and hits that used a loose path.</summary>
    Fallback,

    /// <summary>Every lookup, including exact hits.</summary>
    All,
}

internal static class FailureLevels
{
    public static FailureLevel Parse(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            "miss" or "misses" => FailureLevel.Miss,
            "all" => FailureLevel.All,
            _ => FailureLevel.Fallback,
        };
}

/// <summary>One step a lookup took, such as a rejected script key.</summary>
internal sealed record FailureStep(string Stage, string Result, string? Detail = null);

/// <summary>The build and data a case was recorded against, so a stale case can be spotted.</summary>
internal sealed record FailureBuildInfo(
    string Plugin,
    string Artifact,
    string GameVersion,
    string ClientLanguage,
    int IndexFormat);

/// <summary>One recorded lookup. Serialized as a single JSON file.</summary>
internal sealed class FailureCase
{
    public int Schema { get; init; } = 1;

    public required string Id { get; init; }

    public DateTimeOffset Time { get; init; } = DateTimeOffset.Now;

    public required string Surface { get; init; }

    public required string Component { get; init; }

    public FailureOutcome Outcome { get; set; }

    public string? Reason { get; set; }

    public int Repeat { get; set; } = 1;

    /// <summary>True when a hit used a loose path, so the case is kept as a fallback.</summary>
    public bool Approximate { get; set; }

    public FailureBuildInfo? Build { get; init; }

    public Dictionary<string, string> Input { get; init; } = new(StringComparer.Ordinal);

    public Dictionary<string, string> Data { get; init; } = new(StringComparer.Ordinal);

    public List<FailureStep> Steps { get; init; } = [];

    /// <summary>The dedupe key. Not written, because it repeats the surface and the input.</summary>
    [JsonIgnore]
    public string SubjectKey { get; init; } = string.Empty;
}

internal static class FailureText
{
    public const int MaxLength = 400;

    public static string Fit(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value.Length <= MaxLength ? value : string.Concat(value.AsSpan(0, MaxLength), "…");
    }
}
