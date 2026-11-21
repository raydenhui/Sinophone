namespace Sinophone.Coverage;

/// <summary>One runtime-built line, with the runtime value the harness chose for it.</summary>
/// <param name="MustTranslate">
/// True when the plugin is expected to translate the line. False marks a label the Chinese client
/// builds in its own UI, such as a clock or a level, where staying English is correct.
/// </param>
internal sealed record RuntimeCase(
    string Component,
    string English,
    string Category,
    bool MustTranslate,
    string Note = "");

/// <summary>
/// The text the game assembles at run time, with the runtime values chosen here so it can be
/// measured offline.
/// </summary>
/// <remarks>
/// Two kinds. The first is built from real rows whose English carries a placeholder: the player's
/// name, or a gap the game splices a value into. The value is chosen here and put into the row's
/// own wording, so the case is stable and reviewable, and the lookup then runs exactly as it does
/// in game. The second is the labels the client formats itself (clock, date, level); those are
/// expected to stay English, so the cases record that rather than pretending to translate them.
/// </remarks>
internal static class RuntimeCases
{
    /// <summary>The full character name, for the dump's "<c>Player Player</c>" placeholder.</summary>
    public const string PlayerName = "Y'shtola Rhul";

    /// <summary>The given name, for a dump that writes the placeholder as "<c>Player</c>".</summary>
    public const string PlayerGivenName = "Y'shtola";

    /// <summary>The value spliced into a double-space gap.</summary>
    public const string GapValue = "Copper Ore";

    /// <summary>Labels the client formats itself. English is the expected answer for each.</summary>
    public static readonly RuntimeCase[] Labels =
    [
        new("Selection", "8:00 a.m.", "client-label", false, "the client formats its own clock"),
        new("Selection", "8/27/2013 8:00 a.m.", "client-label", false, "the client formats its own date"),
        new("Selection", "27/8/2013 8:00", "client-label", false, "the client formats its own date"),
        new("Selection", "Lv. 90", "client-label", false, "the client formats its own level"),
        new("Selection", "UNKNOWN/UNKNOWN", "client-label", false, "the row holds only placeholders"),
    ];

    /// <summary>A line with the player's name filled in, from a row that writes the placeholder.</summary>
    public static string WithPlayerName(string english)
        => english.Replace("Player Player", PlayerName, StringComparison.Ordinal)
            .Replace("Player", PlayerGivenName, StringComparison.Ordinal);

    /// <summary>A line with the first double-space gap filled the way the game fills it.</summary>
    public static string WithGapValue(string english)
    {
        var gap = english.IndexOf("  ", StringComparison.Ordinal);
        return gap < 0 ? english : english[..gap] + " " + GapValue + " " + english[(gap + 2)..];
    }
}
