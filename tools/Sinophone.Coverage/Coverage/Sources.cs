namespace Sinophone.Coverage;

/// <summary>How a component reads a row, and therefore which resolve the probe calls.</summary>
internal enum SourceKind
{
    TalkRow,
    Balloon,
    Yell,
    BattleTalkRow,
    BattleTalkText,
    SelectionAddon,
    Name,
    Item,
}

/// <summary>One sheet field that some surface reads, and the component that shows it.</summary>
/// <param name="Component">Empty when the sheet is indexed but no handler shows it.</param>
internal sealed record Source(
    string Component,
    string Sheet,
    string Field,
    bool IsName,
    SourceKind Kind,
    string? EnColumnName = null)
{
    /// <summary>
    /// Column in the English dump. It differs from <see cref="Field"/> where the datamining dump
    /// names the column instead of leaving it unnamed, which is what the Lumina field calls it.
    /// </summary>
    public string EnColumn => EnColumnName ?? Field;

    public override string ToString() => $"{Sheet}.{Field}";
}

/// <summary>
/// Which sheet feeds which component. This is the map the report measures against, so a sheet that
/// gains a handler has to gain a line here, or the report says it is shown by nobody.
/// </summary>
internal static class Sources
{
    public static readonly Source[] All =
    [
        // Talk window, overlay or native.
        new("Talk", "DefaultTalk", "Text", false, SourceKind.TalkRow),
        new("Talk", "GimmickTalk", "Message", false, SourceKind.TalkRow),
        new("Talk", "Addon", "Text", false, SourceKind.TalkRow),
        new("Talk", "GuildleveAssignmentTalk", "Talk", false, SourceKind.TalkRow),
        new("Talk", "CraftLeveTalk", "Talk", false, SourceKind.TalkRow),
        new("Talk", "GimmickYesNo", "Message", false, SourceKind.TalkRow),
        new("Talk", "GoldSaucerTalk", "Message", false, SourceKind.TalkRow),
        new("Talk", "MassivePcContentTextData", "Text", false, SourceKind.TalkRow),
        new("Talk", "SnipeTalk", "Text", false, SourceKind.TalkRow),
        new("Talk", "WKSMissionText", "Text", false, SourceKind.TalkRow),
        new("Talk", "WKSText", "Text", false, SourceKind.TalkRow),
        new("Talk", "WKSEmergencyInfoText", "Text", false, SourceKind.TalkRow),
        new("Talk", "HWDDevLevelWebText", "Unknown0", false, SourceKind.TalkRow, EnColumnName: "Text"),
        new("Talk", "HWDDevLevelWebText", "Unknown1", false, SourceKind.TalkRow, EnColumnName: "InProgressText"),
        new("Talk", "InclusionShopWelcomText", "Unknown0", false, SourceKind.TalkRow, EnColumnName: "Text"),
        new("Talk", "QuickChatTransient", "Text", false, SourceKind.TalkRow, EnColumnName: "TextOutput"),
        new("Talk", "PublicContentTextData", "TextData", false, SourceKind.TalkRow),
        new("Talk", "PartyContentTextData", "Data", false, SourceKind.TalkRow),
        new("Talk", "RideShootingTextData", "String", false, SourceKind.TalkRow),
        new("Talk", "MKDLore", "Description", false, SourceKind.TalkRow),

        // Chat bubbles.
        new("Chat bubbles", "Balloon", "Dialogue", false, SourceKind.Balloon),
        new("Chat bubbles", "NpcYell", "Text", false, SourceKind.Yell),

        // Battle talk.
        new("Battle talk", "NpcYell", "Text", false, SourceKind.BattleTalkRow),
        new("Battle talk", "InstanceContentTextData", "Text", false, SourceKind.BattleTalkText),
        new("Battle talk", "ContentTalk", "Text", false, SourceKind.BattleTalkText),

        // Selection dialogs.
        new("Selection", "Addon", "Text", false, SourceKind.SelectionAddon),
        new("Selection", "GimmickYesNo", "YesButton", false, SourceKind.SelectionAddon),
        new("Selection", "GimmickYesNo", "NoButton", false, SourceKind.SelectionAddon),
        new("Selection", "GoldSaucerTalk", "ChoicesText", false, SourceKind.SelectionAddon),

        // Nameplates.
        new("Nameplates", "ENpcResident", "Singular", true, SourceKind.Name),
        new("Nameplates", "BNpcName", "Singular", true, SourceKind.Name),
        new("Nameplates", "EObjName", "Singular", true, SourceKind.Name),
        new("Nameplates", "SnipeTalkName", "Name", true, SourceKind.Name),

        // Item name.
        new("Item name", "Item", "Name", true, SourceKind.Item),
        new("Item name", "EventItem", "Singular", true, SourceKind.Item),

        // Indexed, but no handler shows these yet. The report lists them so the map stays honest.
        new("", "Warp", "Question", false, SourceKind.TalkRow),
        new("", "CSBonusTextData", "Text", false, SourceKind.TalkRow),
        new("", "GoldSaucerTextData", "Text", false, SourceKind.TalkRow),
        new("", "GimmickBill", "Text", false, SourceKind.TalkRow),
        new("", "MKDLore", "Name", true, SourceKind.Name),
    ];

    /// <summary>Distinct sheet and field pairs, one descriptor each.</summary>
    public static IEnumerable<(string Sheet, string Field, string EnColumn, bool IsName)> Descriptors()
        => All
            .GroupBy(source => (source.Sheet, source.Field))
            .Select(group => (group.Key.Sheet, group.Key.Field, group.First().EnColumn, group.First().IsName));

    public static IEnumerable<string> SheetsInUse()
        => All.Select(source => source.Sheet).Distinct(StringComparer.Ordinal).OrderBy(sheet => sheet, StringComparer.Ordinal);
}
