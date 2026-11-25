namespace Sinophone.Diagnostics;

/// <summary>
/// Decides whether a gamertag a nameplate shows is worth driving in a harness run.
/// </summary>
/// <remarks>
/// Kept apart from the engine code so the rule is tested. A nameplate needs a real character
/// and a name the store knows, so a run has to filter the object list before it starts.
/// </remarks>
public static class HarnessNamePlateSelection
{
    /// <summary>
    /// True when the name is long enough to be worth a lookup and does not look like a masked
    /// name. A masked name such as <c>???</c> stays masked on purpose, so it is not a case.
    /// </summary>
    public static bool IsWorthTesting(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length < 2)
            return false;

        var letters = 0;
        foreach (var ch in trimmed)
        {
            if (char.IsLetter(ch))
                letters++;
        }

        return letters >= 2;
    }

    /// <summary>
    /// The first candidate from a list that passes <see cref="IsWorthTesting"/>, or the fallback.
    /// </summary>
    public static string? FirstWorthTesting(IEnumerable<string?> names, string? fallback = null)
    {
        foreach (var name in names)
        {
            if (IsWorthTesting(name))
                return name;
        }

        return fallback;
    }
}
