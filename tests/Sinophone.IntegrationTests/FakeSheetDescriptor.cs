using Sinophone.Data;

namespace Sinophone.IntegrationTests;

/// <summary>
/// A sheet descriptor backed by a dictionary instead of a live Lumina sheet.
/// Lets the store be tested without game data.
/// </summary>
internal sealed class FakeSheetDescriptor : ISheetDescriptor
{
    private readonly Dictionary<uint, IReadOnlyList<IReadOnlyList<string>>> _rows;

    /// <summary>Every text is its own single variant.</summary>
    public FakeSheetDescriptor(string name, string fieldName, bool isNameSheet, Dictionary<uint, string[]> rows)
        : this(name, fieldName, isNameSheet, ToVariants(rows))
    {
    }

    /// <summary>Rows already split into variants, for conditional tests.</summary>
    public FakeSheetDescriptor(
        string name,
        string fieldName,
        bool isNameSheet,
        Dictionary<uint, IReadOnlyList<IReadOnlyList<string>>> rows)
    {
        Name = name;
        FieldName = fieldName;
        IsNameSheet = isNameSheet;
        _rows = rows;
    }

    public string Name { get; }

    public string FieldName { get; }

    public bool IsNameSheet { get; }

    public bool TryExtract(uint rowId, out IReadOnlyList<IReadOnlyList<string>> texts)
    {
        if (_rows.TryGetValue(rowId, out var row))
        {
            texts = row;
            return true;
        }

        texts = Array.Empty<IReadOnlyList<string>>();
        return false;
    }

    private static Dictionary<uint, IReadOnlyList<IReadOnlyList<string>>> ToVariants(
        Dictionary<uint, string[]> rows)
    {
        var result = new Dictionary<uint, IReadOnlyList<IReadOnlyList<string>>>(rows.Count);

        foreach (var (rowId, texts) in rows)
        {
            var variants = new IReadOnlyList<string>[texts.Length];
            for (var i = 0; i < texts.Length; i++)
                variants[i] = new[] { texts[i] };

            result[rowId] = variants;
        }

        return result;
    }
}
