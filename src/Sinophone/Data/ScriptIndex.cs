using Sinophone.Diagnostics;

namespace Sinophone.Data;

/// <summary>
/// Script-dump lines: the game text key, the sheet row it came from, the cutscene it names, and
/// the lines that share one English key but have different Chinese.
/// </summary>
internal sealed class ScriptIndex
{
    private readonly Dictionary<string, string> _byKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _englishByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, uint> _rowByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _sheetByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string Sheet, uint RowId), string> _textByRow = new();
    private readonly Dictionary<string, List<ScriptCandidate>> _ambiguous = new(StringComparer.Ordinal);
    private readonly Dictionary<string, uint> _sceneByEnglish = new(StringComparer.Ordinal);
    private readonly HashSet<string> _sceneConflicts = new(StringComparer.Ordinal);
    private readonly List<(string Template, string Chinese)> _templates = [];

    public int AmbiguousKeyCount => _ambiguous.Count;

    public void AddKey(string scriptKey, string display, string englishKey, string sheet, uint rowId)
    {
        _byKey.TryAdd(scriptKey, display);
        _englishByKey.TryAdd(scriptKey, englishKey);

        if (string.IsNullOrEmpty(sheet))
            return;

        _rowByKey.TryAdd(scriptKey, rowId);
        _sheetByKey.TryAdd(scriptKey, sheet);
        _textByRow.TryAdd((sheet, rowId), display);
    }

    public void AddTemplate(string template, string display)
        => _templates.Add((template, display));

    public bool TryGetText(string scriptKey, out string stored)
        => _byKey.TryGetValue(scriptKey, out stored!);

    /// <summary>The normalized English line the dump paired with a script key.</summary>
    public bool TryGetPairedEnglish(string scriptKey, out string english)
        => _englishByKey.TryGetValue(scriptKey, out english!);

    public bool TryGetRowId(string scriptKey, out uint rowId)
        => _rowByKey.TryGetValue(scriptKey, out rowId);

    /// <summary>Script text keyed by the sheet row the game names, such as a cutscene subtitle.</summary>
    public bool TryGetRowText(string sheet, uint rowId, out string stored)
        => _textByRow.TryGetValue((sheet, rowId), out stored!);

    public bool TryGetCutsceneId(string key, out uint cutsceneId)
        => _sceneByEnglish.TryGetValue(key, out cutsceneId);

    public bool TryGetTemplate(string key, out string stored)
    {
        stored = string.Empty;

        foreach (var (template, chinese) in _templates)
        {
            if (!ScriptTextTemplate.TryMatch(template, key, out _))
                continue;

            stored = chinese;
            return true;
        }

        return false;
    }

    public bool HasAmbiguous(string key) => _ambiguous.ContainsKey(key);

    public bool TryGetAmbiguous(string key, out List<ScriptCandidate> candidates)
        => _ambiguous.TryGetValue(key, out candidates!);

    /// <summary>Adds a candidate for a key that more than one script line shares.</summary>
    public void AddCandidate(string key, string? scriptKey, string display)
    {
        if (!_ambiguous.TryGetValue(key, out var candidates))
        {
            candidates = [];
            _ambiguous[key] = candidates;
        }

        AddCandidate(candidates, scriptKey, display);
    }

    /// <summary>
    /// Turns a key into an ambiguous one: the first line seen is kept and the new line is added
    /// beside it, so the speaker, scene, or script family can still pick one.
    /// </summary>
    public void PromoteToAmbiguous(string key, ScriptCandidate first, string? scriptKey, string display)
    {
        var candidates = new List<ScriptCandidate>(3) { first };
        AddCandidate(candidates, scriptKey, display);
        _ambiguous[key] = candidates;
    }

    /// <summary>
    /// Answers a key that several script lines share. The speaker narrows by the token in each
    /// dump key; a cutscene narrows by its number in a <c>VOICEMAN</c> key. A key with no hint, or
    /// more than one candidate left, stays unanswered rather than guessing.
    /// </summary>
    public bool TryLookupAmbiguous(
        string key,
        string? speakerName,
        in DialogueContext context,
        bool hasTextFallback,
        PluginLog log,
        FailureTrace? trace,
        out string stored)
    {
        stored = string.Empty;

        if (!_ambiguous.TryGetValue(key, out var candidates))
        {
            trace?.Step("text.ambiguous", "none");
            return false;
        }

        var narrowed = candidates;

        var words = SpeakerWords(speakerName);
        if (words.Length > 0)
        {
            var spoken = candidates.FindAll(candidate => CandidateIsSpokenBy(candidate, words));
            if (spoken.Count > 0)
                narrowed = spoken;
        }

        // The event script that produced a selection dialog is a stronger hint than the English
        // text alone. One of its keys, or a key from a sibling option, names the script family
        // the answer must come from.
        if (context.ScriptKeyHints is { Count: > 0 } hints)
        {
            var hinted = narrowed.FindAll(candidate => CandidateMatchesAnyHint(candidate, hints));
            if (hinted.Count > 0)
            {
                if (hinted.Count < narrowed.Count)
                {
                    log.Debug(
                        "Dialogue narrowed {Count} shared script lines to {Matched} by the event script {Hints}.",
                        narrowed.Count, hinted.Count, string.Join(", ", hints));
                }

                narrowed = hinted;
            }
        }

        if (narrowed.Count == 1)
        {
            trace?.Step("text.ambiguous", "hit", "one candidate");
            stored = narrowed[0].Display;
            log.Debug(
                "Dialogue matched one of {Count} script lines that share a key, by speaker {Speaker}: {Keys}.",
                candidates.Count, speakerName ?? string.Empty, Describe(narrowed[0]));
            return true;
        }

        var cutsceneId = context.CutsceneId;
        if (cutsceneId != 0)
        {
            var match = OnlyMatch(narrowed, candidate => HoldsCutscene(candidate, cutsceneId));
            if (match is not null)
            {
                trace?.Step("text.ambiguous", "hit", $"cutscene {cutsceneId}");
                stored = match.Value.Display;
                log.Debug(
                    "Dialogue matched the cutscene {CutsceneId} line out of {Count} script lines " +
                    "that share a key.",
                    cutsceneId, narrowed.Count);
                return true;
            }
        }

        if (MacroNormalizer.IsOnlySoundMarkers(key))
        {
            trace?.Step("text.ambiguous", "hit", "sound only");
            stored = narrowed[0].Display;
            log.Debug(
                "Dialogue picked one of {Count} lines that hold only a sound ({Key}).",
                narrowed.Count, key);
            return true;
        }

        if (context.ChocoboTaxiStandUnlocked is { } standUnlocked)
        {
            var match = OnlyMatch(narrowed, candidate => HoldsUnlockLine(candidate) != standUnlocked);
            if (match is not null)
            {
                trace?.Step("text.ambiguous", "hit", $"chocobo {(standUnlocked ? "unlocked" : "locked")}");
                stored = match.Value.Display;
                log.Debug(
                    "Dialogue picked the {State} chocobo taxi greeting out of {Count} script lines " +
                    "that share a key.",
                    standUnlocked ? "unlocked" : "locked", narrowed.Count);
                return true;
            }
        }

        // Two scenes of one cutscene file can hold the same line twice, and the localisation
        // differs only in a closing mark. Saying either is right, so answer instead of refusing.
        if (AllSayTheSame(narrowed))
        {
            trace?.Step("text.ambiguous", "hit", "same text");
            stored = narrowed[0].Display;
            log.Debug(
                "Dialogue picked one of {Count} script lines that share a key and differ only in " +
                "punctuation ({Keys}).",
                narrowed.Count, Describe(narrowed[0]));
            return true;
        }

        // A cutscene can hold two versions of one line for different player progress, with the
        // same speaker and scene. No id on the addon says which branch is playing, and both are
        // from this scene, so use the first (default) branch instead of leaving the line English.
        if (narrowed.Count > 1 && AllLinesInOneCutscene(narrowed, cutsceneId))
        {
            trace?.Step("text.ambiguous", "hit", "branch line");
            stored = narrowed[0].Display;
            log.Debug(
                "Dialogue picked the first of {Count} branch lines in cutscene {CutsceneId}: {Keys}.",
                narrowed.Count, cutsceneId, Describe(narrowed[0]));
            return true;
        }

        // A sheet row may still answer this key by text after the script candidates fail.
        if (!hasTextFallback)
        {
            // The line stays English. Name the dump keys, because a report of a missing line
            // cannot say which scenes collided from a count alone.
            log.Debug(
                "Dialogue line has {Count} script candidates and none was chosen ({Speaker}, cutscene " +
                "{CutsceneId}): {Keys}.",
                candidates.Count,
                speakerName ?? string.Empty,
                cutsceneId,
                string.Join(", ", candidates.Select(Describe)));

            if (trace is not null)
            {
                trace.Step(
                    "text.ambiguous",
                    "no-choice",
                    string.Join("; ", candidates.Take(6).Select(Describe)));
            }
        }

        return false;
    }

    /// <summary>
    /// Records the scene one script key names, and drops the line when another key disagrees or
    /// sits outside a cutscene. A line is scanned first so the order of its keys does not decide
    /// the answer.
    /// </summary>
    public static void NoteScene(
        Dictionary<string, uint> scenes,
        HashSet<string> conflicts,
        string englishKey,
        string scriptKey)
    {
        if (conflicts.Contains(englishKey))
            return;

        var scene = CutsceneIdOf(scriptKey);
        if (!scenes.TryGetValue(englishKey, out var known))
        {
            scenes[englishKey] = scene;
            return;
        }

        if (known == scene)
            return;

        scenes.Remove(englishKey);
        conflicts.Add(englishKey);
    }

    /// <summary>Adds the scenes this artifact agreed on to the index.</summary>
    public void MergeScenes(Dictionary<string, uint> scenes, HashSet<string> conflicts)
    {
        foreach (var (key, scene) in scenes)
        {
            if (scene == 0 || _sceneConflicts.Contains(key))
                continue;

            if (_sceneByEnglish.TryGetValue(key, out var known))
            {
                if (known == scene)
                    continue;

                _sceneByEnglish.Remove(key);
                _sceneConflicts.Add(key);
                continue;
            }

            _sceneByEnglish[key] = scene;
        }

        foreach (var key in conflicts)
        {
            if (_sceneByEnglish.Remove(key))
                _sceneConflicts.Add(key);
        }
    }

    /// <summary>
    /// The cutscene number in a <c>VOICEMAN</c> dump key: <c>TEXT_VOICEMAN_06005_003840_THANCRED</c>
    /// is cutscene 6005. Zero for any other key.
    /// </summary>
    internal static uint CutsceneIdOf(string? scriptKey)
    {
        const string prefix = "TEXT_VOICEMAN_";

        if (scriptKey is null || !scriptKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return 0;

        var start = prefix.Length;
        var end = scriptKey.IndexOf('_', start);
        if (end <= start)
            return 0;

        return uint.TryParse(scriptKey.AsSpan(start, end - start), out var id) ? id : 0;
    }

    /// <summary>
    /// The script a dump key belongs to, without the line suffix.
    /// <c>TEXT_REGUNI5BASEOMICRON_00760_A1_000_001</c> and <c>REGUNI5BASEOMICRON_00760</c> both
    /// name <c>REGUNI5BASEOMICRON_00760</c>, so a Lua key and a dump key can be compared.
    /// </summary>
    internal static string ScriptFamilyOf(string? key)
    {
        if (string.IsNullOrEmpty(key))
            return string.Empty;

        var slash = key.LastIndexOfAny(['/', '\\']);
        if (slash >= 0)
            key = key[(slash + 1)..];

        var dot = key.IndexOf('.');
        if (dot > 0)
            key = key[..dot];

        const string prefix = "TEXT_";
        var name = key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? key[prefix.Length..]
            : key;

        var first = name.IndexOf('_');
        if (first <= 0)
            return name;

        var second = name.IndexOf('_', first + 1);
        if (second < 0)
            return name;

        // Most keys are SCRIPT_NAME + a five digit id. Keep both.
        if (uint.TryParse(name.AsSpan(first + 1, second - first - 1), out _))
            return name[..second];

        return name[..first];
    }

    /// <summary>The first candidate for a key, kept while the artifact is indexed.</summary>
    internal static ScriptCandidate CreateCandidate(string display, string? scriptKey)
        => new(display, KeySpeakerTokens(scriptKey), string.IsNullOrEmpty(scriptKey) ? [] : [scriptKey]);

    /// <summary>Keeps one more line for an English key that several script lines share.</summary>
    internal static void AddCandidate(List<ScriptCandidate> candidates, string? scriptKey, string display)
    {
        var tokens = KeySpeakerTokens(scriptKey);

        for (var i = 0; i < candidates.Count; i++)
        {
            // The same dump key can arrive again from the other variant's artifact, with that
            // variant's Chinese. It is one line, not a second scene.
            if (!string.IsNullOrEmpty(scriptKey) && Array.IndexOf(candidates[i].Keys, scriptKey) >= 0)
                return;

            if (!string.Equals(candidates[i].Display, display, StringComparison.Ordinal))
                continue;

            if (!string.IsNullOrEmpty(scriptKey))
                candidates[i] = candidates[i].WithKey(scriptKey, tokens);

            return;
        }

        candidates.Add(new ScriptCandidate(
            display, tokens, string.IsNullOrEmpty(scriptKey) ? [] : [scriptKey]));
    }

    /// <summary>The words in a script key that can name a speaker.</summary>
    internal static string[] KeySpeakerTokens(string? scriptKey)
    {
        if (string.IsNullOrEmpty(scriptKey))
            return [];

        var tokens = new List<string>(4);

        var start = -1;
        for (var i = 0; i <= scriptKey.Length; i++)
        {
            var isLetter = i < scriptKey.Length && char.IsLetter(scriptKey[i]);
            if (isLetter)
            {
                if (start < 0)
                    start = i;
                continue;
            }

            if (start < 0)
                continue;

            var length = i - start;
            if (length >= 4)
            {
                var token = scriptKey[start..i].ToUpperInvariant();
                if (!string.Equals(token, "TEXT", StringComparison.Ordinal) && !tokens.Contains(token))
                    tokens.Add(token);
            }

            start = -1;
        }

        return tokens.Count == 0 ? [] : tokens.ToArray();
    }

    private static bool AllLinesInOneCutscene(List<ScriptCandidate> candidates, uint cutsceneId)
    {
        if (cutsceneId != 0)
            return candidates.All(candidate => HoldsCutscene(candidate, cutsceneId));

        uint scene = 0;
        foreach (var candidate in candidates)
        {
            foreach (var key in candidate.Keys)
            {
                var keyScene = CutsceneIdOf(key);
                if (keyScene == 0)
                    return false;

                if (scene == 0)
                    scene = keyScene;
                else if (scene != keyScene)
                    return false;
            }
        }

        return scene != 0;
    }

    private static bool AllSayTheSame(List<ScriptCandidate> candidates)
    {
        if (candidates.Count < 2)
            return false;

        var first = TextWithoutPunctuation(candidates[0].Display);
        if (first.Length == 0)
            return false;

        for (var i = 1; i < candidates.Count; i++)
        {
            if (!string.Equals(first, TextWithoutPunctuation(candidates[i].Display), StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static string TextWithoutPunctuation(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var builder = new System.Text.StringBuilder(text.Length);
        foreach (var character in MacroNormalizer.CleanForDisplay(text))
        {
            if (char.IsPunctuation(character) || char.IsWhiteSpace(character) || char.IsSymbol(character))
                continue;

            builder.Append(character);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Whether this key has a chocobo stand variant, so only the stand's unlock state can pick
    /// one. Every other line must not read that game state.
    /// </summary>
    public bool HasChocoboTaxiStandVariant(string key)
        => _ambiguous.TryGetValue(key, out var candidates)
           && candidates.Exists(static candidate => HoldsUnlockLine(candidate));

    private static string Describe(ScriptCandidate candidate)
        => candidate.Keys.Length == 0 ? "(no key)" : string.Join(" / ", candidate.Keys);

    private static bool HoldsUnlockLine(ScriptCandidate candidate)
        => Array.Exists(candidate.Keys, key => key.Contains("_UNLOCK", StringComparison.OrdinalIgnoreCase));

    private static ScriptCandidate? OnlyMatch(
        List<ScriptCandidate> candidates,
        Predicate<ScriptCandidate> matches)
    {
        ScriptCandidate? match = null;

        foreach (var candidate in candidates)
        {
            if (!matches(candidate))
                continue;

            if (match is not null)
                return null;

            match = candidate;
        }

        return match;
    }

    private static bool HoldsCutscene(ScriptCandidate candidate, uint cutsceneId)
        => Array.Exists(candidate.Keys, key => CutsceneIdOf(key) == cutsceneId);

    private static bool CandidateMatchesAnyHint(ScriptCandidate candidate, IReadOnlyList<string> hints)
    {
        foreach (var key in candidate.Keys)
        {
            var family = ScriptFamilyOf(key);
            if (family.Length == 0)
                continue;

            foreach (var hint in hints)
            {
                var hintFamily = ScriptFamilyOf(hint);
                if (hintFamily.Length == 0)
                    continue;

                if (string.Equals(family, hintFamily, StringComparison.OrdinalIgnoreCase)
                    || family.StartsWith($"{hintFamily}_", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// The words of a speaker name that can appear in a dump key. Titles and short words are
    /// dropped; "Jullus" and "Emet-Selch" both survive.
    /// </summary>
    private static string[] SpeakerWords(string? speakerName)
    {
        if (string.IsNullOrWhiteSpace(speakerName))
            return [];

        var words = new List<string>(4);
        var whole = new StringBuilder(speakerName.Length);

        var start = -1;
        for (var i = 0; i <= speakerName.Length; i++)
        {
            var isLetter = i < speakerName.Length && char.IsLetter(speakerName[i]);
            if (isLetter)
            {
                whole.Append(char.ToUpperInvariant(speakerName[i]));

                if (start < 0)
                    start = i;

                continue;
            }

            if (start < 0)
                continue;

            if (i - start >= 4)
                words.Add(speakerName[start..i].ToUpperInvariant());

            start = -1;
        }

        // A dump key writes a two-part name as one token: "G'raha Tia" is GRAHATIA. The
        // apostrophe and the space are not part of the name, so the whole name without them is
        // a candidate too.
        var fullName = whole.ToString();
        if (fullName.Length >= 4 && !words.Contains(fullName))
            words.Add(fullName);

        return words.Count == 0 ? [] : words.ToArray();
    }

    private static bool CandidateIsSpokenBy(ScriptCandidate candidate, string[] speakerWords)
    {
        foreach (var token in candidate.SpeakerTokens)
        {
            foreach (var word in speakerWords)
            {
                if (string.Equals(token, word, StringComparison.Ordinal))
                    return true;
            }
        }

        return false;
    }

    public void WriteTo(BinaryWriter writer)
    {
        IndexCacheFormat.WriteStrings(writer, _byKey);
        IndexCacheFormat.WriteStrings(writer, _englishByKey);

        writer.Write(_rowByKey.Count);
        foreach (var (key, rowId) in _rowByKey)
        {
            writer.Write(key);
            writer.Write(rowId);
        }

        writer.Write(_sheetByKey.Count);
        foreach (var (key, sheet) in _sheetByKey)
        {
            writer.Write(key);
            writer.Write(sheet);
        }

        writer.Write(_textByRow.Count);
        foreach (var ((sheet, rowId), text) in _textByRow)
        {
            writer.Write(sheet);
            writer.Write(rowId);
            writer.Write(text);
        }

        writer.Write(_sceneByEnglish.Count);
        foreach (var (key, scene) in _sceneByEnglish)
        {
            writer.Write(key);
            writer.Write(scene);
        }

        IndexCacheFormat.WriteStringSet(writer, _sceneConflicts);

        writer.Write(_templates.Count);
        foreach (var (template, chinese) in _templates)
        {
            writer.Write(template);
            writer.Write(chinese);
        }

        writer.Write(_ambiguous.Count);
        foreach (var (key, candidates) in _ambiguous)
        {
            writer.Write(key);
            writer.Write(candidates.Count);

            foreach (var candidate in candidates)
            {
                writer.Write(candidate.Display);

                writer.Write(candidate.SpeakerTokens.Length);
                foreach (var token in candidate.SpeakerTokens)
                    writer.Write(token);

                writer.Write(candidate.Keys.Length);
                foreach (var scriptKey in candidate.Keys)
                    writer.Write(scriptKey);
            }
        }
    }

    public void ReadFrom(BinaryReader reader)
    {
        IndexCacheFormat.ReadStrings(reader, _byKey);
        IndexCacheFormat.ReadStrings(reader, _englishByKey);

        var rows = reader.ReadInt32();
        for (var i = 0; i < rows; i++)
            _rowByKey.Add(reader.ReadString(), reader.ReadUInt32());

        var sheets = reader.ReadInt32();
        for (var i = 0; i < sheets; i++)
            _sheetByKey.Add(reader.ReadString(), reader.ReadString());

        var texts = reader.ReadInt32();
        for (var i = 0; i < texts; i++)
            _textByRow.Add((reader.ReadString(), reader.ReadUInt32()), reader.ReadString());

        var scenes = reader.ReadInt32();
        for (var i = 0; i < scenes; i++)
            _sceneByEnglish.Add(reader.ReadString(), reader.ReadUInt32());

        IndexCacheFormat.ReadStringSet(reader, _sceneConflicts);

        var templates = reader.ReadInt32();
        for (var i = 0; i < templates; i++)
            _templates.Add((reader.ReadString(), reader.ReadString()));

        var ambiguous = reader.ReadInt32();
        for (var i = 0; i < ambiguous; i++)
        {
            var key = reader.ReadString();
            var count = reader.ReadInt32();
            var candidates = new List<ScriptCandidate>(count);

            for (var j = 0; j < count; j++)
            {
                var display = reader.ReadString();

                var tokens = new string[reader.ReadInt32()];
                for (var t = 0; t < tokens.Length; t++)
                    tokens[t] = reader.ReadString();

                var dumpKeys = new string[reader.ReadInt32()];
                for (var k = 0; k < dumpKeys.Length; k++)
                    dumpKeys[k] = reader.ReadString();

                candidates.Add(new ScriptCandidate(display, tokens, dumpKeys));
            }

            _ambiguous.Add(key, candidates);
        }
    }

    /// <summary>One line of a script key that more than one Chinese line shares.</summary>
    /// <remarks>
    /// <see cref="Keys"/> holds every dump key whose Chinese is <see cref="Display"/>, because two
    /// keys with the same Chinese are one candidate. The speaker token, the cutscene number, and
    /// the <c>UNLOCK</c> marker are all read from those keys.
    /// </remarks>
    internal readonly record struct ScriptCandidate(string Display, string[] SpeakerTokens, string[] Keys)
    {
        public ScriptCandidate WithKey(string scriptKey, string[] tokens)
        {
            var knownKeys = Keys;
            var knownTokens = SpeakerTokens;

            if (Array.IndexOf(knownKeys, scriptKey) >= 0)
                return this;

            var added = Array.FindAll(tokens, token => Array.IndexOf(knownTokens, token) < 0);

            string[] keys = knownKeys.Length == 0 ? [scriptKey] : [.. knownKeys, scriptKey];
            string[] allTokens;
            if (added.Length == 0)
            {
                allTokens = knownTokens;
            }
            else
            {
                allTokens = [.. knownTokens, .. added];
            }

            return this with { Keys = keys, SpeakerTokens = allTokens };
        }
    }
}
