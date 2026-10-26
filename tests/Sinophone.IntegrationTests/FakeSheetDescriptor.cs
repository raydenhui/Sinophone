using Sinophone.Data;

namespace Sinophone.IntegrationTests;

/// <summary>
/// A sheet descriptor backed by a dictionary instead of a live Lumina sheet.
/// Lets the store be tested without game data.
/// </summary>
internal sealed class FakeSheetDescriptor : ISheetDescriptor
{
    private readonly Dictionary<uint, string[]> _rows;

    public FakeSheetDescriptor(string name, string fieldName, bool isNameSheet, Dictionary<uint, string[]> rows)
    {
        Name = name;
        FieldName = fieldName;
        IsNameSheet = isNameSheet;
        _rows = rows;
    }

    public string Name { get; }

    public string FieldName { get; }

    public bool IsNameSheet { get; }

    public bool TryExtract(uint rowId, out IReadOnlyList<string> texts)
    {
        if (_rows.TryGetValue(rowId, out var row))
        {
            texts = row;
            return true;
        }

        texts = Array.Empty<string>();
        return false;
    }
}
