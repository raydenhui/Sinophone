namespace Sinophone.Diagnostics;

/// <summary>
/// Builds the cases the testing tab offers from the loaded store, the same way the coverage
/// tool enumerates every line. Cases are plain data, so a catalog can be built and checked
/// without the game.
/// </summary>
public static class HarnessCatalog
{
    /// <summary>How many of each kind a run takes by default.</summary>
    public const int DefaultSampleSize = 20;

    /// <summary>
    /// A small fixed catalog that needs no store. Used by the Talk and bubble sub-tabs before
    /// a translation is loaded, and by the tests.
    /// </summary>
    public static readonly HarnessCase[] BuiltIn =
    [
        new("talk-short", HarnessSurface.Talk, "Hello there.", Tags: ["short"]),
        new("talk-typical", HarnessSurface.Talk,
            "Welcome to Limsa Lominsa, adventurer. The Admiral asked for you personally.",
            Tags: ["typical"]),
        new("talk-long", HarnessSurface.Talk,
            "The Garlean Empire has not been idle in the years since the Calamity. Their armies " +
            "mass along the border, and their agents walk among us, whispering promises of peace " +
            "while they sharpen their knives for our backs.",
            Tags: ["long"]),
        new("bubble-short", HarnessSurface.ChatBubble, "Squeal!", Tags: ["short"]),
        new("bubble-typical", HarnessSurface.ChatBubble,
            "Adventurer! Over here! I have something for you.", Tags: ["typical"]),
        new("bubble-long", HarnessSurface.ChatBubble,
            "Oh, thank goodness you're here. The crates arrived early and nobody told me, so I " +
            "spent the whole morning counting them twice before I found the manifest had already " +
            "been sent to the guild.",
            Tags: ["long"]),
    ];

    /// <summary>
    /// Builds cases from pairs of English and Chinese lines. Keeps pairs whose English is short
    /// enough for the surface, drops ones with markup, and labels the rest by length so a run
    /// can filter them.
    /// </summary>
    /// <param name="surface">Surface the cases drive.</param>
    /// <param name="pairs">English and its translation.</param>
    /// <param name="limit">How many cases to build.</param>
    /// <param name="maxEnglish">Longest English line to keep, in characters.</param>
    public static List<HarnessCase> FromPairs(
        HarnessSurface surface,
        IEnumerable<(string English, string Chinese)> pairs,
        int limit = DefaultSampleSize,
        int maxEnglish = 400)
    {
        var cases = new List<HarnessCase>(limit);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (english, chinese) in pairs)
        {
            if (cases.Count >= limit)
                break;

            if (string.IsNullOrWhiteSpace(english) || string.IsNullOrWhiteSpace(chinese))
                continue;

            if (english.Length > maxEnglish)
                continue;

            if (HarnessAssertions.HasMacroLeak(english) || HarnessAssertions.HasMacroLeak(chinese))
                continue;

            if (!seen.Add(english))
                continue;

            cases.Add(new HarnessCase(
                $"{surface.ToString().ToLowerInvariant()}-{cases.Count + 1:00}",
                surface,
                english,
                ExpectedChinese: chinese,
                Tags: TagsFor(english)));
        }

        return cases;
    }

    /// <summary>
    /// Builds cases from the store's own sheet text, so a run uses real lines rather than the
    /// fixed ones. Lines are taken every <paramref name="stride"/> entries, which spreads a
    /// sample across the whole dictionary instead of taking the first page.
    /// </summary>
    /// <param name="surface">Surface the cases drive.</param>
    /// <param name="store">Loaded store, or null when no translation is available.</param>
    /// <param name="limit">How many cases to build.</param>
    /// <param name="maxEnglish">Longest English line to keep, in characters.</param>
    public static List<HarnessCase> FromStore(
        HarnessSurface surface,
        TranslationStore? store,
        int limit = DefaultSampleSize,
        int maxEnglish = 400)
    {
        if (store is null || limit <= 0)
            return [];

        var entries = store.SheetTextEntries;
        if (entries.Count == 0)
            return [];

        var stride = Math.Max(1, entries.Count / Math.Max(1, limit * 4));
        var pairs = new List<(string English, string Chinese)>(limit * 4);

        for (var index = 0; index < entries.Count; index += stride)
        {
            var english = entries[index].English;

            // Ask the store for the line rather than using the stored Chinese: the answer is
            // what the plugin draws, with conditionals resolved and the conversion applied,
            // so a case's expectation matches the surface.
            if (store.TryGetDialogue(english, out var chinese) && !string.IsNullOrWhiteSpace(chinese))
                pairs.Add((english, chinese));
        }

        return FromPairs(surface, pairs, limit, maxEnglish);
    }

    /// <summary>
    /// The length label for one line. Used as a filter tag so a run can take only short lines
    /// when a long one is known to be a separate problem.
    /// </summary>
    public static string LengthTag(string english)
        => english.Length switch
        {
            <= 60 => "short",
            <= 160 => "typical",
            _ => "long",
        };

    /// <summary>
    /// The cases an armed Talk run takes. The driver measures the line that is on screen, so a
    /// second case would measure the same line again; an armed run takes one.
    /// </summary>
    public static IReadOnlyList<HarnessCase> SingleLine(IReadOnlyList<HarnessCase> cases)
        => cases.Count <= 1 ? cases : new[] { cases[0] };

    private static IReadOnlyList<string> TagsFor(string english)
        => [LengthTag(english)];
}
