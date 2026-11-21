using Sinophone.Data;

namespace Sinophone.Coverage;

/// <summary>
/// One sheet of the English datamining dump, read as the slots the store pairs with the artifact.
/// </summary>
/// <remarks>
/// The English dump is the only English source the plugin has offline: the artifact stores the
/// Chinese keyed by row id. Columns are resolved by name so a multi-slot field taken from
/// <c>Text[0]</c>, <c>Text[1]</c>, and so on keeps the artifact's slot order.
/// </remarks>
internal sealed class EnCsv
{
    private readonly Dictionary<uint, string[]> _slots;

    private EnCsv(int[] columns, Dictionary<uint, string[]> slots)
    {
        Columns = columns;
        _slots = slots;
    }

    /// <summary>Column indices the sheet's field was found at, in slot order.</summary>
    public int[] Columns { get; }

    public int SlotCount => Columns.Length;

    public static EnCsv Load(string path, string field)
    {
        var rows = Csv.Parse(File.ReadAllText(path));
        var headerIndex = rows.FindIndex(row => row.Length > 0 && row[0].Trim() == "#");
        if (headerIndex < 0)
            throw new InvalidDataException($"{Path.GetFileName(path)} has no '#' header row.");

        var header = rows[headerIndex];
        var columns = ResolveColumns(header, field);
        if (columns.Length == 0)
            throw new InvalidDataException(
                $"{Path.GetFileName(path)} has no column for '{field}'. Header: {string.Join(", ", header.Take(12))}");

        var slots = new Dictionary<uint, string[]>(rows.Count);
        for (var i = headerIndex + 1; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Length == 0 || !uint.TryParse(row[0].Trim(), out var rowId))
                continue;

            var texts = new string[columns.Length];
            for (var c = 0; c < columns.Length; c++)
                texts[c] = columns[c] < row.Length ? row[columns[c]] : string.Empty;

            slots[rowId] = texts;
        }

        return new EnCsv(columns, slots);
    }

    public bool TryGetSlots(uint rowId, out string[] texts) => _slots.TryGetValue(rowId, out texts!);

    /// <summary>
    /// Finds the field's columns: <c>Text</c> alone, or <c>Text[0]</c>, <c>Text[1]</c>… in slot
    /// order for a field the dump splits.
    /// </summary>
    private static int[] ResolveColumns(string[] header, string field)
    {
        var exact = Array.FindIndex(header, name => string.Equals(name.Trim(), field, StringComparison.OrdinalIgnoreCase));
        if (exact >= 0)
            return [exact];

        var numbered = new List<(int Slot, int Column)>();
        for (var i = 0; i < header.Length; i++)
        {
            var name = header[i].Trim();
            if (!name.StartsWith(field + "[", StringComparison.OrdinalIgnoreCase) || !name.EndsWith(']'))
                continue;

            if (int.TryParse(name[(field.Length + 1)..^1], out var slot))
                numbered.Add((slot, i));
        }

        return numbered.OrderBy(entry => entry.Slot).Select(entry => entry.Column).ToArray();
    }
}

/// <summary>A sheet descriptor over the English dump, so the store indexes without the game.</summary>
internal sealed class EnCsvSheetDescriptor : ISheetDescriptor
{
    private readonly EnCsv _csv;

    public EnCsvSheetDescriptor(string name, string fieldName, bool isNameSheet, EnCsv csv)
    {
        Name = name;
        FieldName = fieldName;
        IsNameSheet = isNameSheet;
        _csv = csv;
    }

    public string Name { get; }

    public string FieldName { get; }

    public bool IsNameSheet { get; }

    public bool TryExtract(uint rowId, out IReadOnlyList<IReadOnlyList<string>> texts)
    {
        if (!_csv.TryGetSlots(rowId, out var slots))
        {
            texts = [];
            return false;
        }

        var variants = new IReadOnlyList<string>[slots.Length];
        for (var i = 0; i < slots.Length; i++)
            variants[i] = SplitConditionals(slots[i]);

        texts = variants;
        return true;
    }

    /// <summary>
    /// One string per conditional branch, so a row with a gender conditional gets a key for each
    /// wording the game can show. The live extractor does the same from the SeString.
    /// </summary>
    private static IReadOnlyList<string> SplitConditionals(string text)
    {
        if (!ConditionalText.HasConditional(text))
            return [text];

        var first = ConditionalText.Resolve(text, _ => true);
        var second = ConditionalText.Resolve(text, _ => false);
        return string.Equals(first, second, StringComparison.Ordinal) ? [first] : [first, second];
    }
}
