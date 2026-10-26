using System.IO.Compression;
using Sinophone.Data;

namespace Sinophone.IntegrationTests;

/// <summary>
/// Builds a <see cref="TranslationStore"/> from the checked-in fixture artifacts,
/// the same way the plugin builds it from its embedded resources at load.
/// </summary>
internal static class FixtureStore
{
    public static string FixtureDirectory
        => Path.Combine(AppContext.BaseDirectory, "fixtures");

    /// <summary>
    /// Sheet descriptors that mirror the real ones, backed by the fixture rows
    /// so the English text lines up with the artifact row ids.
    /// </summary>
    public static IReadOnlyList<ISheetDescriptor> Descriptors()
    {
        var rows = FixtureData.Current.Rows;

        var talk = rows
            .Where(r => r.Sheet == "DefaultTalk")
            .ToDictionary(r => r.RowId, r => new[] { r.English });

        var names = rows
            .Where(r => r.Sheet == "ENpcResident")
            .ToDictionary(r => r.RowId, r => new[] { r.English });

        var npcNames = rows
            .Where(r => r.Sheet == "BNpcName")
            .ToDictionary(r => r.RowId, r => new[] { r.English });

        // Field names must match SheetCatalog: DefaultTalk uses "Text", the name
        // sheets use "Singular".
        return
        [
            new FakeSheetDescriptor("DefaultTalk", "Text", false, talk),
            new FakeSheetDescriptor("ENpcResident", "Singular", true, names),
            new FakeSheetDescriptor("BNpcName", "Singular", true, npcNames),
        ];
    }

    /// <summary>Opens the fixture artifact for a variant, as the plugin opens its embedded one.</summary>
    public static Stream? Open(string fileName)
    {
        var path = Path.Combine(FixtureDirectory, fileName);
        return File.Exists(path) ? File.OpenRead(path) : null;
    }

    public static Func<ChineseVariant, Stream?> Artifacts()
        => variant => Open(variant == ChineseVariant.Traditional
            ? "Sinophone.tc.json.gz"
            : "Sinophone.cn.json.gz");

    public static TranslationStore Load(ChineseVariant variant, bool useFallback = true)
    {
        var store = TranslationStore.Load(
            Descriptors(),
            variant,
            useFallback,
            Artifacts(),
            new FakePluginLog());

        Assert.NotNull(store);
        return store!;
    }
}
