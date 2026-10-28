namespace Sinophone.Data;

/// <summary>
/// Root object of a generated <c>Sinophone.&lt;language&gt;.json.gz</c> artifact.
/// </summary>
/// <remarks>
/// Layout: <c>Sheets[sheetName][rowId][fieldName] = string[]</c>.
/// Row keys are row ids as strings. Field names mirror Lumina's row property names.
/// Empty slots are preserved as empty strings so indices stay aligned with the English sheet.
/// <para>
/// <c>Scripts</c> holds dialogue that has no Lumina row, keyed by the dump's text key.
/// The English text travels with the artifact because the game has no sheet to read it from.
/// </para>
/// </remarks>
public sealed class TranslationArtifact
{
    public int FormatVersion { get; set; } = 1;

    /// <summary>Human readable language tag, e.g. <c>CN</c> or <c>TC</c>.</summary>
    public string Language { get; set; } = string.Empty;

    public string GeneratedAt { get; set; } = string.Empty;

    public string GameVersion { get; set; } = string.Empty;

    public Dictionary<string, Dictionary<string, Dictionary<string, string[]>>> Sheets { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Lines from the game's script dumps, keyed by text key, for example
    /// <c>TEXT_REGSEAAETHEGUID_00051_SUNDHIMAL_000_1</c>.
    /// </summary>
    public Dictionary<string, ScriptEntry> Scripts { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>One line from a script dump, in both the game's language and the translated one.</summary>
public sealed class ScriptEntry
{
    /// <summary>The English text the game shows. This is the lookup key.</summary>
    public string English { get; set; } = string.Empty;

    /// <summary>The translated text to draw in the overlay.</summary>
    public string Text { get; set; } = string.Empty;
}
