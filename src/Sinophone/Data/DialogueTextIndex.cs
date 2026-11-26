namespace Sinophone.Data;

/// <summary>The alias one script line attaches to its speaker, such as "(-Radio-)".</summary>
internal readonly record struct SpeakerAlias(string English, string Chinese);

/// <summary>
/// Exact dialogue text and the fallbacks that share its shape: sheet keys, the speaker alias each
/// script line carries, and keys with the gender or time-of-day branch words collapsed.
/// </summary>
internal sealed class DialogueTextIndex
{
    private readonly Dictionary<string, string> _text = new(StringComparer.Ordinal);
    private readonly HashSet<string> _sheetKeys = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SpeakerAlias> _aliases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _branchNeutral = new(StringComparer.Ordinal);
    private readonly HashSet<string> _ambiguousBranchKeys = new(StringComparer.Ordinal);
    private int _scriptEntryCount;

    public int Count => _text.Count;

    /// <summary>Script-dump lines in the text lookup. Already counted in <see cref="Count"/>.</summary>
    public int ScriptEntryCount => _scriptEntryCount;

    /// <summary>Collapsed keys that match a line written for another gender or time of day.</summary>
    internal int BranchNeutralCount => _branchNeutral.Count;

    /// <summary>Collapsed keys two lines share, which are not used at all.</summary>
    internal int AmbiguousBranchKeyCount => _ambiguousBranchKeys.Count;

    /// <summary>
    /// The English text of every sheet row, with the stored Chinese. Sorted, so a sample taken
    /// from it is the same on every run.
    /// </summary>
    public IReadOnlyList<(string English, string Chinese)> SheetEntries()
    {
        var entries = new List<(string English, string Chinese)>(_sheetKeys.Count);
        foreach (var key in _sheetKeys)
        {
            if (key.Length > 0)
                entries.Add((key, _text[key]));
        }

        entries.Sort(static (left, right) => string.CompareOrdinal(left.English, right.English));
        return entries;
    }

    public bool Contains(string key) => _text.ContainsKey(key);

    public bool TryGetExact(string key, out string stored)
        => _text.TryGetValue(key, out stored!);

    /// <summary>Adds one sheet row. A key that already exists is only replaced when it held kana.</summary>
    public KanaTextAdd AddSheetText(string key, string display)
    {
        var result = KanaText.TryAddPreferred(_text, key, display);
        if (result == KanaTextAdd.Added)
            _sheetKeys.Add(key);

        return result;
    }

    /// <summary>
    /// Adds one script line. <paramref name="sheetOwnsOtherText"/> is true when a sheet row owns
    /// the key with different Chinese, so the caller can keep the sheet text as the fallback and
    /// let the script candidates choose by their own identity.
    /// </summary>
    public bool AddScriptText(string key, string display, out bool sheetOwnsOtherText)
    {
        if (_text.TryAdd(key, display))
        {
            _scriptEntryCount++;
            sheetOwnsOtherText = false;
            return true;
        }

        sheetOwnsOtherText = _sheetKeys.Contains(key)
                             && !string.Equals(_text[key], display, StringComparison.Ordinal);
        return false;
    }

    /// <summary>Removes a script line because the key turned out to be ambiguous.</summary>
    public bool RemoveScriptText(string key)
    {
        if (!_text.Remove(key))
            return false;

        _scriptEntryCount--;
        return true;
    }

    /// <summary>
    /// Keeps a key with the branch words collapsed, so a line written for another gender or time
    /// of day still matches. A collapsed key that two lines share is not used at all.
    /// </summary>
    public void AddBranchNeutral(string key, string display)
    {
        var neutral = CollapseBranchWords(key);
        if (ReferenceEquals(neutral, key) || _text.ContainsKey(neutral))
            return;

        if (!_branchNeutral.TryAdd(neutral, display)
            && !string.Equals(_branchNeutral[neutral], display, StringComparison.Ordinal))
        {
            _ambiguousBranchKeys.Add(neutral);
        }
    }

    /// <summary>
    /// Drops a collapsed key that pointed at a line which has just become ambiguous.
    /// </summary>
    public void DropBranchNeutral(string key, string display)
    {
        var collapsed = CollapseBranchWords(key);
        if (!ReferenceEquals(collapsed, key)
            && _branchNeutral.TryGetValue(collapsed, out var known)
            && string.Equals(known, display, StringComparison.Ordinal))
        {
            _branchNeutral.Remove(collapsed);
        }
    }

    public bool TryGetBranchNeutral(string key, out string stored)
    {
        stored = string.Empty;

        if (_branchNeutral.Count == 0)
            return false;

        var neutral = CollapseBranchWords(key);
        if (ReferenceEquals(neutral, key))
            return false;

        if (_ambiguousBranchKeys.Contains(neutral)
            || !_branchNeutral.TryGetValue(neutral, out stored!))
        {
            stored = string.Empty;
            return false;
        }

        return true;
    }

    public void AddAlias(string key, string english, string chinese)
        => _aliases.TryAdd(key, new SpeakerAlias(english, chinese));

    public bool TryGetAlias(string key, out SpeakerAlias alias)
        => _aliases.TryGetValue(key, out alias);

    /// <summary>Collapses every word the English dump may have resolved to another branch.</summary>
    public static string CollapseBranchWords(string key)
        => TimeOfDayWords.Neutralize(GenderedNouns.Neutralize(GenderedPronouns.Neutralize(key)));

    public void WriteTo(BinaryWriter writer)
    {
        IndexCacheFormat.WriteStrings(writer, _text);
        IndexCacheFormat.WriteStringSet(writer, _sheetKeys);
        IndexCacheFormat.WriteStrings(writer, _branchNeutral);
        IndexCacheFormat.WriteStringSet(writer, _ambiguousBranchKeys);

        writer.Write(_aliases.Count);
        foreach (var (key, alias) in _aliases)
        {
            writer.Write(key);
            writer.Write(alias.English);
            writer.Write(alias.Chinese);
        }

        writer.Write(_scriptEntryCount);
    }

    public void ReadFrom(BinaryReader reader)
    {
        IndexCacheFormat.ReadStrings(reader, _text);
        IndexCacheFormat.ReadStringSet(reader, _sheetKeys);
        IndexCacheFormat.ReadStrings(reader, _branchNeutral);
        IndexCacheFormat.ReadStringSet(reader, _ambiguousBranchKeys);

        var aliases = reader.ReadInt32();
        for (var i = 0; i < aliases; i++)
            _aliases.Add(reader.ReadString(), new SpeakerAlias(reader.ReadString(), reader.ReadString()));

        _scriptEntryCount = reader.ReadInt32();
    }
}
