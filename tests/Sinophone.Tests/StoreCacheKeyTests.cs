using Dalamud.Game;
using Sinophone.Data;

namespace Sinophone.Tests;

/// <summary>
/// The index is keyed on the live client's text, so the client language must be
/// part of the cache key. Without it a language switch reuses the old index and
/// every lookup misses.
/// </summary>
public class StoreCacheKeyTests
{
    [Fact]
    public void Build_ChangesWhenTheClientLanguageChanges()
    {
        var english = Build(ClientLanguage.English);
        var japanese = Build(ClientLanguage.Japanese);

        Assert.NotEqual(english, japanese);
    }

    [Fact]
    public void Build_IsStableForTheSameInputs()
    {
        Assert.Equal(Build(ClientLanguage.English), Build(ClientLanguage.English));
    }

    [Theory]
    [InlineData("game-1", "cn-1", "tc-1")]
    [InlineData("game-2", "cn-1", "tc-1")]
    [InlineData("game-1", "cn-2", "tc-1")]
    [InlineData("game-1", "cn-1", "tc-2")]
    public void Build_ChangesWhenAnyInputChanges(string game, string cn, string tc)
    {
        var baseline = Build(ClientLanguage.English, game: "game-0", cn: "cn-0", tc: "tc-0");
        var changed = Build(ClientLanguage.English, game: game, cn: cn, tc: tc);

        Assert.NotEqual(baseline, changed);
    }

    [Fact]
    public void Build_ChangesWithTheIndexFormatVersion()
    {
        var current = StoreCacheKey.Build(
            ChineseVariant.Simplified, false, ChineseConversion.None, 9,
            "game", ClientLanguage.English, "cn", "tc");
        var next = StoreCacheKey.Build(
            ChineseVariant.Simplified, false, ChineseConversion.None, 10,
            "game", ClientLanguage.English, "cn", "tc");

        Assert.NotEqual(current, next);
    }

    private static string Build(
        ClientLanguage language,
        string game = "game",
        string cn = "cn",
        string tc = "tc")
        => StoreCacheKey.Build(
            ChineseVariant.Simplified,
            false,
            ChineseConversion.None,
            TranslationStore.IndexFormatVersion,
            game,
            language,
            cn,
            tc);
}
