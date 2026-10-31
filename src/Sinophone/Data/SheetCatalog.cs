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

    /// <summary>
    /// Extracts the English text slots for a row. Each slot holds one string per
    /// conditional branch, because a row with a gender conditional shows different
    /// text to different players. Empty when the row is unusable.
    /// </summary>
    bool TryExtract(uint rowId, out IReadOnlyList<IReadOnlyList<string>> texts);
}

/// <summary>
/// Typed implementation of <see cref="ISheetDescriptor"/> bound to one Lumina sheet.
/// </summary>
public sealed class SheetDescriptor<T> : ISheetDescriptor where T : struct, IExcelRow<T>
{
    private readonly ExcelSheet<T>? _sheet;
    private readonly Func<T, IReadOnlyList<IReadOnlyList<string>>> _extract;

    public SheetDescriptor(string name, string fieldName, bool isNameSheet, ExcelSheet<T>? sheet, Func<T, IReadOnlyList<IReadOnlyList<string>>> extract)
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

    public bool TryExtract(uint rowId, out IReadOnlyList<IReadOnlyList<string>> texts)
    {
        texts = Array.Empty<IReadOnlyList<string>>();

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
        var descriptors = new List<ISheetDescriptor>(35);

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

        // Dialogue sheets added for issue #19. HWDDevLevelWebText and
        // InclusionShopWelcomText expose unnamed columns, which Lumina surfaces as
        // Unknown0 and Unknown1.
        Add<GimmickTalk>(dataManager, descriptors, "GimmickTalk", "Message", false,
            static row => ExtractSingle(row.Message));
        Add<GimmickBill>(dataManager, descriptors, "GimmickBill", "Text", false,
            static row => ExtractSingle(row.Text));
        Add<GimmickYesNo>(dataManager, descriptors, "GimmickYesNo", "Message", false,
            static row => ExtractSingle(row.Message));
        Add<GimmickYesNo>(dataManager, descriptors, "GimmickYesNo", "YesButton", false,
            static row => ExtractSingle(row.YesButton));
        Add<GimmickYesNo>(dataManager, descriptors, "GimmickYesNo", "NoButton", false,
            static row => ExtractSingle(row.NoButton));
        Add<GoldSaucerTalk>(dataManager, descriptors, "GoldSaucerTalk", "Message", false,
            static row => ExtractSingle(row.Message));
        Add<GoldSaucerTalk>(dataManager, descriptors, "GoldSaucerTalk", "ChoicesText", false,
            static row => ExtractCollection(row.ChoicesText));
        Add<MassivePcContentTextData>(dataManager, descriptors, "MassivePcContentTextData", "Text", false,
            static row => ExtractSingle(row.Text));
        Add<WKSMissionText>(dataManager, descriptors, "WKSMissionText", "Text", false,
            static row => ExtractSingle(row.Text));
        Add<WKSText>(dataManager, descriptors, "WKSText", "Text", false,
            static row => ExtractSingle(row.Text));
        Add<WKSEmergencyInfoText>(dataManager, descriptors, "WKSEmergencyInfoText", "Text", false,
            static row => ExtractSingle(row.Text));
        Add<HWDDevLevelWebText>(dataManager, descriptors, "HWDDevLevelWebText", "Unknown0", false,
            static row => ExtractSingle(row.Unknown0));
        Add<HWDDevLevelWebText>(dataManager, descriptors, "HWDDevLevelWebText", "Unknown1", false,
            static row => ExtractSingle(row.Unknown1));
        Add<InclusionShopWelcomText>(dataManager, descriptors, "InclusionShopWelcomText", "Unknown0", false,
            static row => ExtractSingle(row.Unknown0));

        Add<Addon>(dataManager, descriptors, "Addon", "Text", false,
            static row => ExtractSingle(row.Text));

        Add<MKDLore>(dataManager, descriptors, "MKDLore", "Name", true,
            static row => ExtractSingle(row.Name));
        Add<MKDLore>(dataManager, descriptors, "MKDLore", "Description", false,
            static row => ExtractSingle(row.Description));

        Add<ENpcResident>(dataManager, descriptors, "ENpcResident", "Singular", true,
            static row => ExtractSingle(row.Singular));
        Add<BNpcName>(dataManager, descriptors, "BNpcName", "Singular", true,
            static row => ExtractSingle(row.Singular));
        Add<EObjName>(dataManager, descriptors, "EObjName", "Singular", true,
            static row => ExtractSingle(row.Singular));
        Add<SnipeTalkName>(dataManager, descriptors, "SnipeTalkName", "Name", true,
            static row => ExtractSingle(row.Name));
        Add<Item>(dataManager, descriptors, "Item", "Name", true,
            static row => ExtractSingle(row.Name));

        return descriptors;
    }

    private static void Add<T>(IDataManager dataManager, List<ISheetDescriptor> descriptors, string name, string fieldName, bool isNameSheet, Func<T, IReadOnlyList<IReadOnlyList<string>>> extract)
        where T : struct, IExcelRow<T>
    {
        var sheet = dataManager.GetExcelSheet<T>();
        descriptors.Add(new SheetDescriptor<T>(name, fieldName, isNameSheet, sheet, extract));
    }

    private static IReadOnlyList<IReadOnlyList<string>> ExtractSingle(ReadOnlySeString value)
    {
        var variants = SeStringVariants.Extract(value);

        foreach (var variant in variants)
        {
            if (!string.IsNullOrEmpty(variant))
                return new IReadOnlyList<string>[] { variants };
        }

        return Array.Empty<IReadOnlyList<string>>();
    }

    // Some Lumina sheets expose a text field as a plain string instead of a SeString.
    private static IReadOnlyList<IReadOnlyList<string>> ExtractSingle(string value)
        => string.IsNullOrEmpty(value)
            ? Array.Empty<IReadOnlyList<string>>()
            : new IReadOnlyList<string>[] { new[] { value } };

    private static IReadOnlyList<IReadOnlyList<string>> ExtractCollection(Collection<ReadOnlySeString> values)
    {
        var count = values.Count;
        if (count == 0)
            return Array.Empty<IReadOnlyList<string>>();

        var result = new IReadOnlyList<string>[count];
        var any = false;

        for (var i = 0; i < count; i++)
        {
            var variants = SeStringVariants.Extract(values[i]);
            result[i] = variants;

            foreach (var variant in variants)
            {
                if (!string.IsNullOrEmpty(variant))
                {
                    any = true;
                    break;
                }
            }
        }

        return any ? result : Array.Empty<IReadOnlyList<string>>();
    }
}
