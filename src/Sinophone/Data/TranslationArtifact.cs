namespace Sinophone.Data;

/// <summary>
/// Root object of a generated <c>Sinophone.&lt;language&gt;.json.gz</c> artifact.
/// </summary>
/// <remarks>
/// Layout: <c>Sheets[sheetName][rowId][fieldName] = string[]</c>.
/// Row keys are row ids as strings. Field names mirror Lumina's row property names.
/// Empty slots are preserved as empty strings so indices stay aligned with the English sheet.
/// </remarks>
public sealed class TranslationArtifact
{
    public int FormatVersion { get; set; } = 1;

    /// <summary>Human readable language tag, e.g. <c>CN</c> or <c>TC</c>.</summary>
    public string Language { get; set; } = string.Empty;

    public string GeneratedAt { get; set; } = string.Empty;

    public string GameVersion { get; set; } = string.Empty;

    public Dictionary<string, Dictionary<string, Dictionary<string, string[]>>> Sheets { get; set; } = new(StringComparer.Ordinal);
}
