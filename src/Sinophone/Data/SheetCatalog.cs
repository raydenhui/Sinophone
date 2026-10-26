namespace Sinophone.Data;

/// <summary>
/// Describes one Lumina sheet we translate, plus how to pull its text fields
/// out of a row so the English text can be used as a lookup key.
/// </summary>
public interface ISheetDescriptor
{
    /// <summary>Sheet name, matching the key used in the generated artifact.</summary>
    string Name { get; }

    /// <summary>Row property name that holds the text, matching the artifact field name.</summary>
    string FieldName { get; }

    /// <summary>True when this sheet holds speaker / actor names instead of dialogue.</summary>
    bool IsNameSheet { get; }

    /// <summary>Extracts the English text slots for a row. Empty when the row is unusable.</summary>
    bool TryExtract(uint rowId, out IReadOnlyList<string> texts);
}

/// <summary>
/// Typed implementation of <see cref="ISheetDescriptor"/> bound to one Lumina sheet.
/// </summary>
public sealed class SheetDescriptor<T> : ISheetDescriptor where T : struct, IExcelRow<T>
{
    private readonly ExcelSheet<T>? _sheet;
    private readonly Func<T, IReadOnlyList<string>> _extract;

    public SheetDescriptor(string name, string fieldName, bool isNameSheet, ExcelSheet<T>? sheet, Func<T, IReadOnlyList<string>> extract)
    {
        Name = name;
        FieldName = fieldName;
        IsNameSheet = isNameSheet;
        _sheet = sheet;
        _extract = extract;
    }

    public string Name { get; }

    public string FieldName { get; }

    public bool IsNameSheet { get; }

    public bool TryExtract(uint rowId, out IReadOnlyList<string> texts)
    {
        texts = Array.Empty<string>();

        if (_sheet is null)
            return false;

        var row = _sheet.GetRowOrDefault(rowId);
        if (row is null)
            return false;

        var extracted = _extract(row.Value);
        if (extracted.Count == 0)
            return false;

        texts = extracted;
        return true;
    }
}

/// <summary>
/// The v1 sheet set. Every entry must stay in sync with the DataPrep tool, which
/// writes artifacts keyed by the same sheet and field names.
/// </summary>
public static class SheetCatalog
{
    public static IReadOnlyList<ISheetDescriptor> Create(IDataManager dataManager)
    {
        var descriptors = new List<ISheetDescriptor>(11);

        Add<DefaultTalk>(dataManager, descriptors, "DefaultTalk", "Text", false,
            static row => ExtractCollection(row.Text));
        Add<Balloon>(dataManager, descriptors, "Balloon", "Dialogue", false,
            static row => ExtractSingle(row.Dialogue));
        Add<NpcYell>(dataManager, descriptors, "NpcYell", "Text", false,
            static row => ExtractSingle(row.Text));
        Add<InstanceContentTextData>(dataManager, descriptors, "InstanceContentTextData", "Text", false,
            static row => ExtractSingle(row.Text));
        Add<ContentTalk>(dataManager, descriptors, "ContentTalk", "Text", false,
            static row => ExtractSingle(row.Text));
        Add<SnipeTalk>(dataManager, descriptors, "SnipeTalk", "Text", false,
            static row => ExtractSingle(row.Text));
        Add<GuildleveAssignmentTalk>(dataManager, descriptors, "GuildleveAssignmentTalk", "Talk", false,
            static row => ExtractCollection(row.Talk));
        Add<CraftLeveTalk>(dataManager, descriptors, "CraftLeveTalk", "Talk", false,
            static row => ExtractCollection(row.Talk));

        Add<GoldSaucerTextData>(dataManager, descriptors, "GoldSaucerTextData", "Text", false,
            static row => ExtractSingle(row.Text));
        Add<CSBonusTextData>(dataManager, descriptors, "CSBonusTextData", "Text", false,
            static row => ExtractSingle(row.Text));
        Add<RideShootingTextData>(dataManager, descriptors, "RideShootingTextData", "String", false,
            static row => ExtractSingle(row.String));
        Add<QuickChatTransient>(dataManager, descriptors, "QuickChatTransient", "Text", false,
            static row => ExtractSingle(row.TextOutput));
        Add<PublicContentTextData>(dataManager, descriptors, "PublicContentTextData", "TextData", false,
            static row => ExtractSingle(row.TextData));
        Add<PartyContentTextData>(dataManager, descriptors, "PartyContentTextData", "Data", false,
            static row => ExtractSingle(row.Data));
        Add<MKDLore>(dataManager, descriptors, "MKDLore", "Name", true,
            static row => ExtractSingle(row.Name));
        Add<MKDLore>(dataManager, descriptors, "MKDLore", "Description", false,
            static row => ExtractSingle(row.Description));

        Add<ENpcResident>(dataManager, descriptors, "ENpcResident", "Singular", true,
            static row => ExtractSingle(row.Singular));
        Add<BNpcName>(dataManager, descriptors, "BNpcName", "Singular", true,
            static row => ExtractSingle(row.Singular));
        Add<SnipeTalkName>(dataManager, descriptors, "SnipeTalkName", "Name", true,
            static row => ExtractSingle(row.Name));

        return descriptors;
    }

    private static void Add<T>(IDataManager dataManager, List<ISheetDescriptor> descriptors, string name, string fieldName, bool isNameSheet, Func<T, IReadOnlyList<string>> extract)
        where T : struct, IExcelRow<T>
    {
        var sheet = dataManager.GetExcelSheet<T>();
        descriptors.Add(new SheetDescriptor<T>(name, fieldName, isNameSheet, sheet, extract));
    }

    private static IReadOnlyList<string> ExtractSingle(ReadOnlySeString value)
    {
        var text = value.ExtractText();
        return string.IsNullOrEmpty(text) ? Array.Empty<string>() : new[] { text };
    }

    // Some Lumina sheets expose a text field as a plain string instead of a SeString.
    private static IReadOnlyList<string> ExtractSingle(string value)
        => string.IsNullOrEmpty(value) ? Array.Empty<string>() : new[] { value };

    private static IReadOnlyList<string> ExtractCollection(Collection<ReadOnlySeString> values)
    {
        var count = values.Count;
        if (count == 0)
            return Array.Empty<string>();

        var result = new string[count];
        var any = false;

        for (var i = 0; i < count; i++)
        {
            var text = values[i].ExtractText();
            result[i] = text;

            if (!string.IsNullOrEmpty(text))
                any = true;
        }

        return any ? result : Array.Empty<string>();
    }
}
