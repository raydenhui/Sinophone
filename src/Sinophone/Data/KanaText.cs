namespace Sinophone.Data;

/// <summary>What <see cref="KanaText.TryAddPreferred"/> did with one key.</summary>
internal enum KanaTextAdd
{
    /// <summary>The key was new.</summary>
    Added,

    /// <summary>The key held Japanese and now holds the localised text.</summary>
    Replaced,

    /// <summary>The key was there and stays as it was.</summary>
    Unchanged,
}

/// <summary>
/// A few CN and TC rows were never localised and still hold Japanese. When two rows share one
/// English name or line, the store prefers the row without kana.
/// </summary>
internal static class KanaText
{
    public static bool HasKana(string text)
    {
        foreach (var character in text)
        {
            if (character is >= 'ぁ' and <= 'ゖ'
                or >= '゙' and <= 'ゟ'
                or >= 'ァ' and <= 'ヺ'
                or >= 'ヽ' and <= 'ヾ')
            {
                return true;
            }

            if (character is >= 'ｦ' and <= 'ﾝ' && character != 'ｰ')
                return true;
        }

        return false;
    }

    /// <summary>
    /// Adds a value unless the key exists. An existing Japanese value is replaced by a localised
    /// one, so a row is not chosen by its id alone. A replacement is not a new key, so it does
    /// not count as an indexed entry.
    /// </summary>
    public static KanaTextAdd TryAddPreferred(Dictionary<string, string> map, string key, string display)
    {
        if (map.TryAdd(key, display))
            return KanaTextAdd.Added;

        if (!HasKana(map[key]) || HasKana(display))
            return KanaTextAdd.Unchanged;

        map[key] = display;
        return KanaTextAdd.Replaced;
    }
}
