namespace Sinophone.Localization;

/// <summary>
/// Maps a Dalamud UI language code to one of the languages this plugin carries text for.
/// </summary>
public static class InterfaceLanguageResolver
{
    /// <summary>
    /// The explicit setting wins. <see cref="InterfaceLanguage.Auto"/> follows Dalamud.
    /// </summary>
    public static InterfaceLanguage Resolve(InterfaceLanguage setting, string? dalamudLanguage)
        => setting == InterfaceLanguage.Auto ? FromDalamudCode(dalamudLanguage) : setting;

    /// <summary>
    /// Dalamud reports "zh" for Simplified Chinese and "tw" for Traditional Chinese. Those are
    /// not ISO codes, so a culture lookup cannot be used. Anything else falls back to English.
    /// </summary>
    public static InterfaceLanguage FromDalamudCode(string? dalamudLanguage)
        => dalamudLanguage switch
        {
            "ja" => InterfaceLanguage.Japanese,
            "zh" => InterfaceLanguage.SimplifiedChinese,
            "tw" => InterfaceLanguage.TraditionalChinese,
            _ => InterfaceLanguage.English,
        };

    /// <summary>
    /// True when the language needs a font the default ImGui font may not carry. Dalamud's
    /// default font merges Chinese glyphs only when its own UI language is Chinese.
    /// </summary>
    public static bool NeedsCjkFont(InterfaceLanguage language)
        => language is InterfaceLanguage.SimplifiedChinese or InterfaceLanguage.TraditionalChinese;
}
