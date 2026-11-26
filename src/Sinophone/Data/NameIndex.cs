namespace Sinophone.Data;

internal enum NameMatch
{
    None,
    Exact,
    Trimmed,
}

/// <summary>
/// Chinese names keyed by the English name and by the sheet row the game names. Names are matched
/// without case: the sheet stores generic NPC names in lower case ("skywatcher") and the game
/// shows them capitalised ("Skywatcher").
/// </summary>
internal sealed class NameIndex
{
    private readonly Dictionary<string, string> _byText = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string Sheet, uint RowId), string> _byRow = new();

    public int Count => _byText.Count;

    public KanaTextAdd AddText(string key, string display)
        => KanaText.TryAddPreferred(_byText, key, display);

    /// <summary>Adds a speaker alias, which never has a rival row to prefer over.</summary>
    public void AddIfMissing(string key, string display)
        => _byText.TryAdd(key, display);

    public void AddRow(string sheet, uint rowId, string display)
        => _byRow.TryAdd((sheet, rowId), display);

    public bool TryGetRow(string sheet, uint rowId, out string stored)
        => _byRow.TryGetValue((sheet, rowId), out stored!);

    public NameMatch Match(string key, out string stored, out string matchedKey)
    {
        if (_byText.TryGetValue(key, out stored!))
        {
            matchedKey = key;
            return NameMatch.Exact;
        }

        // Speaker aliases can carry punctuation, for example "Pattin?". The name sheet stores
        // the name without it.
        var trimmed = key.TrimEnd('?', '!', '.', '？', '！', '。', '…');
        if (trimmed.Length == 0 || trimmed == key || !_byText.TryGetValue(trimmed, out stored!))
        {
            matchedKey = key;
            return NameMatch.None;
        }

        matchedKey = trimmed;
        return NameMatch.Trimmed;
    }

    public void WriteTo(BinaryWriter writer)
    {
        IndexCacheFormat.WriteStrings(writer, _byText);

        writer.Write(_byRow.Count);
        foreach (var ((sheet, rowId), value) in _byRow)
        {
            writer.Write(sheet);
            writer.Write(rowId);
            writer.Write(value);
        }
    }

    public void ReadFrom(BinaryReader reader)
    {
        IndexCacheFormat.ReadStrings(reader, _byText);

        var count = reader.ReadInt32();
        for (var i = 0; i < count; i++)
            _byRow.Add((reader.ReadString(), reader.ReadUInt32()), reader.ReadString());
    }
}
