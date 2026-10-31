using System.Text.RegularExpressions;

namespace Sinophone.Data;

/// <summary>
/// Answers the player conditions used in localized dialogue. Player parameters
/// 52, 53, and 54 are the Grand Company ranks for Maelstrom, Twin Adder, and
/// Immortal Flames. A condition for a company the player is not in must be false;
/// otherwise every company block in the text would render and the name would repeat.
/// </summary>
public static partial class PlayerConditionEvaluator
{
    private const int LimsaParameter = 52;
    private const int GridaniaParameter = 53;
    private const int UldahParameter = 54;

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();

    public static bool? Evaluate(
        string condition,
        bool isFemale,
        uint grandCompanyRowId,
        int grandCompanyRank)
    {
        var compact = Whitespace().Replace(condition.Trim(), string.Empty);

        if (compact.Contains("PlayerParameter(4)", StringComparison.OrdinalIgnoreCase))
            return isFemale;

        foreach (var (parameter, expectedRow) in new[]
                 {
                     (LimsaParameter, 1u),
                     (GridaniaParameter, 2u),
                     (UldahParameter, 3u),
                 })
        {
            if (!compact.Contains($"PlayerParameter({parameter})", StringComparison.OrdinalIgnoreCase))
                continue;

            if (grandCompanyRowId != expectedRow)
                return false;

            if (compact.StartsWith("GreaterThan(", StringComparison.OrdinalIgnoreCase)
                && compact.EndsWith(",0)", StringComparison.Ordinal))
            {
                return grandCompanyRank > 0;
            }

            if (compact.StartsWith("LessThan(", StringComparison.OrdinalIgnoreCase)
                && compact.EndsWith(",127)", StringComparison.Ordinal))
            {
                return grandCompanyRank < 127;
            }

            return true;
        }

        return null;
    }
}
