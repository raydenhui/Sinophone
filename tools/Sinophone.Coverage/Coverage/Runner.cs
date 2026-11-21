using System.Text;
using System.Text.Json;
using Sinophone.Data;
using Sinophone.Handlers;

namespace Sinophone.Coverage;

/// <summary>What one case turned into.</summary>
internal enum Outcome
{
    Resolved,
    NoEnglish,
    Unresolved,
}

internal sealed record ScriptCandidate(string Key, string Chinese);

internal sealed record Miss(
    string Component,
    string Source,
    Outcome Outcome,
    string Tags,
    string English,
    string Reason = "");

internal sealed class Tally
{
    public int Total;
    public int Resolved;
    public int ResolvedById;
    public int Unresolved;
    public int NoChinese;
    public int Skipped;
    public readonly Dictionary<string, int> Reasons = new(StringComparer.Ordinal);
    public readonly Dictionary<string, int> ByTag = new(StringComparer.Ordinal);
    public readonly Dictionary<string, int> ResolvedByTag = new(StringComparer.Ordinal);
    public readonly List<Miss> Misses = [];
}

/// <summary>
/// Enumerates every English string a component can show and asks that component to translate it.
/// </summary>
internal sealed class Runner
{
    private readonly TranslationArtifact _artifact;
    private readonly Probes _probes;
    private readonly Dictionary<string, EnCsv> _csvs = new(StringComparer.Ordinal);
    private readonly string _enDirectory;

    public Runner(TranslationArtifact artifact, Probes probes, string enDirectory)
    {
        _artifact = artifact;
        _probes = probes;
        _enDirectory = enDirectory;
    }

    /// <summary>Per component and per sheet: total, resolved, and the misses behind the number.</summary>
    public Dictionary<string, Tally> Run()
    {
        var result = new Dictionary<string, Tally>(StringComparer.Ordinal);

        foreach (var component in Sources.All.Where(source => source.Component.Length > 0)
                     .GroupBy(source => source.Component))
        {
            var tally = new Tally();
            foreach (var source in component)
                RunSource(source, tally);

            result[component.Key] = tally;
        }

        RunScripts(result);

        // Script lines have no row id, so the id column equals the text column for them.
        var scripts = result["Talk (scripts)"];
        scripts.ResolvedById = scripts.Resolved;

        return result;
    }

    private void RunSource(Source source, Tally tally)
    {
        if (!_artifact.Sheets.TryGetValue(source.Sheet, out var rows))
            return;

        var csv = LoadCsv(source);
        if (csv is null)
            return;

        foreach (var (rowKey, fields) in rows)
        {
            if (!uint.TryParse(rowKey, out var rowId))
                continue;

            if (!fields.TryGetValue(source.Field, out var chineseSlots) || chineseSlots.Length == 0)
                continue;

            if (!csv.TryGetSlots(rowId, out var englishSlots))
                continue;

            var slots = Math.Min(chineseSlots.Length, englishSlots.Length);

            for (var slot = 0; slot < slots; slot++)
            {
                var english = englishSlots[slot];
                if (!HasWords(english))
                    continue;

                foreach (var variant in Variants(english))
                {
                    if (string.IsNullOrWhiteSpace(variant))
                        continue;

                    Record(tally, source, rowId, slot, variant, !string.IsNullOrWhiteSpace(chineseSlots[slot]));
                }
            }
        }
    }

    /// <summary>
    /// A label with no letters cannot carry meaning to translate: numbers, punctuation, and empty
    /// rows are skipped so the percentage stays about text.
    /// </summary>
    private static bool HasWords(string text)
    {
        foreach (var ch in text)
        {
            if (char.IsLetter(ch))
                return true;
        }

        return false;
    }

    /// <summary>One case per conditional branch the game can show.</summary>
    private static IEnumerable<string> Variants(string english)
    {
        if (!ConditionalText.HasConditional(english))
            return [english];

        var first = ConditionalText.Resolve(english, _ => true);
        var second = ConditionalText.Resolve(english, _ => false);
        return string.Equals(first, second, StringComparison.Ordinal) ? [first] : [first, second];
    }

    private void Record(Tally tally, Source source, uint rowId, int slot, string english, bool hasChinese)
    {
        if (Dynamic.IsRuntimeLabel(english))
        {
            tally.Skipped++;
            return;
        }

        tally.Total++;

        var tags = Dynamic.Tags(english);
        var tagKey = tags.Length == 0 ? "plain" : string.Join("+", tags);
        tally.ByTag[tagKey] = tally.ByTag.GetValueOrDefault(tagKey) + 1;

        if (!hasChinese)
        {
            // The artifact has no Chinese for this row: data lag, not a matcher miss.
            tally.NoChinese++;
            AddMiss(tally, new Miss(source.Component, source.ToString(), Outcome.NoEnglish, tagKey, english));
            return;
        }

        // Text alone is the worst case; the game often also names the row, which is the id path.
        var chinese = SafeResolve(source, rowId, slot, english, useId: false);
        var chineseById = HasId(source) ? SafeResolve(source, rowId, slot, english, useId: true) : chinese;

        if (!string.IsNullOrWhiteSpace(chineseById))
            tally.ResolvedById++;

        if (!string.IsNullOrWhiteSpace(chinese))
        {
            tally.Resolved++;
            tally.ResolvedByTag[tagKey] = tally.ResolvedByTag.GetValueOrDefault(tagKey) + 1;
            return;
        }

        tally.Unresolved++;
        AddMiss(tally, new Miss(source.Component, source.ToString(), Outcome.Unresolved, tagKey, english));
    }

    /// <summary>True for a kind the game can reach with a row id, not only with the text.</summary>
    private static bool HasId(Source source)
        => source.Kind is SourceKind.TalkRow or SourceKind.Yell or SourceKind.BattleTalkRow or SourceKind.Item;

    private string? SafeResolve(Source source, uint rowId, int slot, string english, bool useId)
    {
        try
        {
            return _probes.Resolve(source, rowId, slot, english, useId: useId);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"!! {source} row {rowId}: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static void AddMiss(Tally tally, Miss miss)
    {
        if (tally.Misses.Count >= 200)
            return;

        tally.Misses.Add(miss);
    }

    /// <summary>
    /// Script-dump lines: one case per key, with the speaker the key names, which is what the game
    /// shows the handler when it has no id.
    /// </summary>
    private void RunScripts(Dictionary<string, Tally> result)
    {
        var component = GetTally(result, "Talk (scripts)");
        var groups = ScriptGroups();

        foreach (var (key, entry) in _artifact.Scripts)
        {
            // The dump writes the speaker alias into the English line, and the game draws it in
            // the name node instead, so the alias is the speaker and not part of the text.
            var alias = MacroNormalizer.ExtractSpeakerPrefix(entry.English);
            var english = MacroNormalizer.StripSpeakerPrefix(entry.English);
            if (string.IsNullOrWhiteSpace(english))
                continue;

            foreach (var variant in Variants(english))
            {
                if (string.IsNullOrWhiteSpace(variant) || Dynamic.IsRuntimeLabel(variant))
                {
                    if (!string.IsNullOrWhiteSpace(variant))
                        component.Skipped++;

                    continue;
                }

                component.Total++;
                var tags = Dynamic.Tags(variant);
                var tagKey = tags.Length == 0 ? "plain" : string.Join("+", tags);
                component.ByTag[tagKey] = component.ByTag.GetValueOrDefault(tagKey) + 1;

                var speaker = alias.Length > 0 ? alias : SpeakerOf(key);
                var chinese = TalkHandler.Resolve(
                    _probes.Store, Probes.Configuration, variant, speaker)?.ChineseText;

                // The game also names the cutscene a VOICEMAN line belongs to, so the id column
                // measures that narrowing too instead of copying the text column.
                var cutsceneId = CutsceneOf(key);
                var chineseById = cutsceneId == 0
                    ? chinese
                    : TalkHandler.Resolve(
                        _probes.Store,
                        Probes.Configuration,
                        variant,
                        speaker,
                        new DialogueContext(null, null, cutsceneId))?.ChineseText;

                if (!string.IsNullOrWhiteSpace(chineseById))
                    component.ResolvedById++;

                if (!string.IsNullOrWhiteSpace(chinese))
                {
                    component.Resolved++;
                    component.ResolvedByTag[tagKey] = component.ResolvedByTag.GetValueOrDefault(tagKey) + 1;
                    continue;
                }

                component.Unresolved++;
                var reason = Classify(groups, variant);
                component.Reasons[reason] = component.Reasons.GetValueOrDefault(reason) + 1;
                AddMiss(component, new Miss("Talk (scripts)", key, Outcome.Unresolved, tagKey, variant, reason));
            }
        }
    }

    /// <summary>
    /// Why a shared script line was not answered, from the artifact's own candidates. It tells the
    /// reader which of these the game can still resolve at run time: a speaker it names, or the
    /// cutscene the line belongs to.
    /// </summary>
    private static string Classify(Dictionary<string, List<ScriptCandidate>> groups, string english)
    {
        if (!groups.TryGetValue(MacroNormalizer.NormalizeKey(english), out var candidates))
            return "not indexed";

        if (candidates.Select(candidate => candidate.Chinese).Distinct(StringComparer.Ordinal).Count() == 1)
            return "not indexed";

        if (candidates.All(candidate => CutsceneOf(candidate.Key) != 0))
            return "ambiguous: cutscene";

        var speakers = candidates
            .Select(candidate => LastToken(candidate.Key))
            .Where(token => token.Length >= 4 && !token.All(char.IsDigit))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return speakers.Count switch
        {
            0 => "ambiguous: no speaker",
            1 => "ambiguous: one speaker",
            _ => "ambiguous: several speakers",
        };
    }

    private Dictionary<string, List<ScriptCandidate>>? _scriptGroups;

    private Dictionary<string, List<ScriptCandidate>> ScriptGroups()
    {
        if (_scriptGroups is not null)
            return _scriptGroups;

        _scriptGroups = new Dictionary<string, List<ScriptCandidate>>(StringComparer.Ordinal);
        foreach (var (key, entry) in _artifact.Scripts)
        {
            var english = entry.English;
            if (string.IsNullOrWhiteSpace(english))
                continue;

            var normalized = MacroNormalizer.NormalizeKey(MacroNormalizer.StripSpeakerPrefix(english));
            if (normalized.Length == 0)
                continue;

            if (!_scriptGroups.TryGetValue(normalized, out var list))
            {
                list = [];
                _scriptGroups[normalized] = list;
            }

            list.Add(new ScriptCandidate(key, entry.Text));
        }

        return _scriptGroups;
    }

    private static string LastToken(string key)
    {
        var index = key.LastIndexOf('_');
        return index < 0 ? string.Empty : key[(index + 1)..];
    }

    /// <summary>The cutscene number in a VOICEMAN key, or 0.</summary>
    private static uint CutsceneOf(string key)
        => TranslationStore.CutsceneIdOf(key);

    private static Tally GetTally(Dictionary<string, Tally> result, string component)
    {
        if (!result.TryGetValue(component, out var tally))
        {
            tally = new Tally();
            result[component] = tally;
        }

        return tally;
    }

    /// <summary>The speaker a dump key names, such as "Thancred" from <c>..._THANCRED</c>.</summary>
    internal static string? SpeakerOf(string key)
    {
        var parts = key.Split('_');
        if (parts.Length == 0)
            return null;

        var last = parts[^1];
        if (last.Length < 4 || last.All(char.IsDigit) || last is "SYSTEM" or "VOICE" or "NONE")
            return null;

        var words = last.Split(['-'], StringSplitOptions.RemoveEmptyEntries);
        var builder = new StringBuilder(last.Length);
        foreach (var word in words)
            builder.Append(char.ToUpperInvariant(word[0])).Append(word[1..].ToLowerInvariant());

        return builder.ToString();
    }

    private EnCsv? LoadCsv(Source source)
    {
        if (_csvs.TryGetValue(source.ToString(), out var ready))
            return ready;

        var path = Path.Combine(_enDirectory, source.Sheet + ".csv");
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"!! missing English dump: {path}");
            return null;
        }

        try
        {
            var csv = EnCsv.Load(path, source.EnColumn);
            _csvs[source.ToString()] = csv;
            return csv;
        }
        catch (InvalidDataException ex)
        {
            Console.Error.WriteLine($"!! {ex.Message}");
            return null;
        }
    }

    /// <summary>Reads the artifact the plugin itself would download.</summary>
    public static TranslationArtifact ReadArtifact(string path)
    {
        using var file = File.OpenRead(path);
        using var gzip = new System.IO.Compression.GZipStream(file, System.IO.Compression.CompressionMode.Decompress);
        return JsonSerializer.Deserialize<TranslationArtifact>(gzip)
               ?? throw new InvalidDataException($"{path} is not a translation artifact.");
    }
}
