namespace Sinophone.Data;

/// <summary>
/// Ties a cached index to the inputs it was built from. The index is keyed on the
/// live client's text, so the client language is part of the key: after a launcher
/// language change the old index must not be reused.
/// </summary>
internal static class StoreCacheKey
{
    public static string Build(
        ChineseVariant variant,
        bool useFallbackVariant,
        ChineseConversion conversion,
        int indexFormatVersion,
        string gameDataVersion,
        ClientLanguage clientLanguage,
        string primarySourceIdentity,
        string fallbackSourceIdentity)
        => string.Join(
            "|",
            variant,
            useFallbackVariant,
            conversion,
            indexFormatVersion,
            gameDataVersion,
            clientLanguage,
            primarySourceIdentity,
            fallbackSourceIdentity);
}
