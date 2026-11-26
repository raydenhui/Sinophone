using System.Diagnostics;

namespace Sinophone.Data;

/// <summary>
/// English text read from the live sheets for one artifact row, plus the Addon
/// template when the row has one. Reading a sheet is a game-thread operation.
/// </summary>
internal sealed class ExtractedRow
{
    public ExtractedRow(
        IReadOnlyList<IReadOnlyList<string>> englishSlots,
        AddonParameterTemplate? parameterTemplate)
    {
        EnglishSlots = englishSlots;
        ParameterTemplate = parameterTemplate;
    }

    public IReadOnlyList<IReadOnlyList<string>> EnglishSlots { get; }

    public AddonParameterTemplate? ParameterTemplate { get; }
}

/// <summary>
/// The English half of the index, read from the live sheets. Collection can be
/// spread over several frames with <see cref="Extract"/>; the finished data holds
/// only strings, so the index build can run on a worker task.
/// </summary>
internal sealed class SheetExtraction
{
    private readonly ParsedArtifacts _parsed;
    private readonly IReadOnlyList<ISheetDescriptor> _descriptors;
    private readonly Dictionary<(string Sheet, string Field, uint RowId), ExtractedRow> _rows = new();

    /// <summary>Row ids of the sheet being read. Built one sheet at a time.</summary>
    private readonly List<uint> _rowsForDescriptor = new();
    private int _descriptorIndex;
    private int _next;

    public SheetExtraction(ParsedArtifacts parsed, IReadOnlyList<ISheetDescriptor> descriptors)
    {
        _parsed = parsed;
        _descriptors = descriptors;
    }

    public bool IsComplete
        => _descriptorIndex >= _descriptors.Count && _next >= _rowsForDescriptor.Count;

    public int RowCount => _rows.Count;

    /// <summary>
    /// Reads live sheet rows until the budget is spent. The caller runs this on the
    /// game thread between frames, so a large artifact never stalls one frame.
    /// </summary>
    public void Extract(TimeSpan budget)
    {
        var stopwatch = Stopwatch.StartNew();

        while (true)
        {
            if (_next < _rowsForDescriptor.Count)
            {
                ExtractRow(_descriptors[_descriptorIndex - 1], _rowsForDescriptor[_next]);
                _next++;
            }
            else if (_descriptorIndex < _descriptors.Count)
            {
                // Build one sheet's row list at a time, so the planning work is
                // spread out with the reads.
                CollectRows(_descriptors[_descriptorIndex], _rowsForDescriptor);
                _descriptorIndex++;
                _next = 0;
                continue;
            }
            else
            {
                return;
            }

            // Check every row. A row can take longer than the whole budget, so a
            // batch check would let one frame read far past it.
            if (stopwatch.Elapsed >= budget)
                return;
        }
    }

    private void ExtractRow(ISheetDescriptor descriptor, uint rowId)
    {
        if (!descriptor.TryExtract(rowId, out var englishSlots))
            return;

        AddonParameterTemplate? parameterTemplate = null;
        if (descriptor is IParameterizedTemplateDescriptor parameterized
            && parameterized.TryBuildTemplate(rowId, out var built))
        {
            parameterTemplate = built;
        }

        _rows[(descriptor.Name, descriptor.FieldName, rowId)] =
            new ExtractedRow(englishSlots, parameterTemplate);
    }

    public bool TryGet(string sheet, string field, uint rowId, out ExtractedRow row)
    {
        if (_rows.TryGetValue((sheet, field, rowId), out var extracted))
        {
            row = extracted;
            return true;
        }

        row = null!;
        return false;
    }

    private void CollectRows(ISheetDescriptor descriptor, List<uint> rows)
    {
        rows.Clear();
        var seen = new HashSet<uint>();
        AddRows(_parsed.Primary, descriptor, rows, seen);
        AddRows(_parsed.Fallback, descriptor, rows, seen);
    }

    private static void AddRows(
        TranslationArtifact? artifact,
        ISheetDescriptor descriptor,
        List<uint> rows,
        HashSet<uint> seen)
    {
        if (artifact is null || !artifact.Sheets.TryGetValue(descriptor.Name, out var sheetRows))
            return;

        foreach (var (rowKey, fields) in sheetRows)
        {
            if (!uint.TryParse(rowKey, out var rowId))
                continue;

            if (!fields.TryGetValue(descriptor.FieldName, out var chinese) || chinese.Length == 0)
                continue;

            if (seen.Add(rowId))
                rows.Add(rowId);
        }
    }
}

/// <summary>
/// Artifacts that have been read and deserialized but not indexed yet. Holds no
/// game objects, so it is safe to hand between a worker task and the game thread.
/// </summary>
public sealed class ParsedArtifacts
{
    internal ParsedArtifacts(
        ChineseVariant variant,
        TranslationArtifact? primary,
        TranslationArtifact? fallback)
    {
        Variant = variant;
        Primary = primary;
        Fallback = fallback;
    }

    public ChineseVariant Variant { get; }

    internal TranslationArtifact? Primary { get; }

    internal TranslationArtifact? Fallback { get; }
}
