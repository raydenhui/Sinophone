using System.IO.Compression;
using System.Text.Json;
using Sinophone.Data;

namespace Sinophone.IntegrationTests;

/// <summary>
/// End-to-end checks on the real fixture data: the artifact on disk has the shape
/// <see cref="TranslationStore"/> expects, and every fixture row resolves.
/// </summary>
public class FixturePipelineTests
{
    [Theory]
    [InlineData("Sinophone.cn.json.gz", "CN")]
    [InlineData("Sinophone.tc.json.gz", "TC")]
    public void Artifact_HasTheExpectedShape(string fileName, string expectedLanguage)
    {
        using var stream = FixtureStore.Open(fileName);
        Assert.NotNull(stream);

        using var gzip = new GZipStream(stream!, CompressionMode.Decompress);
        var artifact = JsonSerializer.Deserialize<TranslationArtifact>(gzip);

        Assert.NotNull(artifact);
        Assert.Equal(1, artifact!.FormatVersion);
        Assert.Equal(expectedLanguage, artifact.Language);
        Assert.NotEmpty(artifact.Sheets);
        Assert.NotEmpty(artifact.Scripts);
    }

    [Theory]
    [InlineData("Sinophone.cn.json.gz")]
    [InlineData("Sinophone.tc.json.gz")]
    public void Artifact_RowKeysAreNumericStrings(string fileName)
    {
        using var stream = FixtureStore.Open(fileName);
        using var gzip = new GZipStream(stream!, CompressionMode.Decompress);
        var artifact = JsonSerializer.Deserialize<TranslationArtifact>(gzip)!;

        foreach (var (sheetName, rows) in artifact.Sheets)
        {
            foreach (var rowKey in rows.Keys)
            {
                Assert.True(uint.TryParse(rowKey, out _),
                    $"Sheet {sheetName} has a non-numeric row key: {rowKey}");
            }
        }
    }

    [Theory]
    [InlineData("Sinophone.cn.json.gz", "Text")]
    [InlineData("Sinophone.tc.json.gz", "Text")]
    public void Artifact_DefaultTalkUsesTheFieldNameTheCatalogExpects(string fileName, string fieldName)
    {
        using var stream = FixtureStore.Open(fileName);
        using var gzip = new GZipStream(stream!, CompressionMode.Decompress);
        var artifact = JsonSerializer.Deserialize<TranslationArtifact>(gzip)!;

        var rows = artifact.Sheets["DefaultTalk"];
        Assert.All(rows.Values, fields => Assert.True(fields.ContainsKey(fieldName)));
    }

    [Fact]
    public void EveryFixtureDialogueRow_ResolvesInBothVariants()
    {
        foreach (var variant in new[] { ChineseVariant.Traditional, ChineseVariant.Simplified })
        {
            var store = FixtureStore.Load(variant);
            var expected = variant == ChineseVariant.Traditional ? "tc" : "cn";

            foreach (var row in FixtureData.Current.Rows.Where(r => r.Sheet == "DefaultTalk"))
            {
                Assert.True(store.TryGetDialogue(row.English, out var chinese),
                    $"No {variant} translation for row {row.RowId}.");

                var wanted = expected == "tc" ? row.Tc : row.Cn;
                Assert.Equal(MacroNormalizer.CleanForDisplay(wanted), chinese);
            }
        }
    }

    [Theory]
    [InlineData("WKSText")]
    [InlineData("GimmickTalk")]
    public void EveryFixturePositionalRow_ResolvesInBothVariants(string sheet)
    {
        var rows = FixtureData.Current.Rows.Where(r => r.Sheet == sheet).ToList();
        Assert.NotEmpty(rows);

        foreach (var variant in new[] { ChineseVariant.Traditional, ChineseVariant.Simplified })
        {
            var store = FixtureStore.Load(variant);
            var expected = variant == ChineseVariant.Traditional ? "tc" : "cn";

            foreach (var row in rows)
            {
                Assert.True(store.TryGetDialogue(row.English, out var chinese),
                    $"No {variant} translation for {sheet} row {row.RowId}.");

                var wanted = expected == "tc" ? row.Tc : row.Cn;
                Assert.Equal(MacroNormalizer.CleanForDisplay(wanted), chinese);
            }
        }
    }

    [Fact]
    public void EveryFixtureNameRow_ResolvesInBothVariants()
    {
        foreach (var variant in new[] { ChineseVariant.Traditional, ChineseVariant.Simplified })
        {
            var store = FixtureStore.Load(variant);
            var expected = variant == ChineseVariant.Traditional ? "tc" : "cn";

            foreach (var row in FixtureData.Current.Rows.Where(r => r.Sheet == "ENpcResident"))
            {
                Assert.True(store.TryGetName(row.English, out var name),
                    $"No {variant} name for row {row.RowId}.");

                var wanted = expected == "tc" ? row.Tc : row.Cn;
                Assert.Equal(MacroNormalizer.CleanForDisplay(wanted), name);
            }
        }
    }

    [Fact]
    public void EveryFixtureScript_ResolvesInBothVariants()
    {
        foreach (var variant in new[] { ChineseVariant.Traditional, ChineseVariant.Simplified })
        {
            var store = FixtureStore.Load(variant);
            var expected = variant == ChineseVariant.Traditional ? "tc" : "cn";

            foreach (var script in FixtureData.Current.Scripts)
            {
                Assert.True(store.TryGetDialogue(script.English, out var chinese),
                    $"No {variant} translation for script {script.Key}.");

                var wanted = expected == "tc" ? script.Tc : script.Cn;
                Assert.Equal(MacroNormalizer.CleanForDisplay(wanted), chinese);
            }
        }
    }

    [Fact]
    public void Store_IndexesEveryFixtureRow()
    {
        var store = FixtureStore.Load(ChineseVariant.Traditional);

        var expectedTalk = FixtureData.Current.Rows.Count(r => r.Sheet is "DefaultTalk" or "WKSText" or "GimmickTalk");
        var expectedNames = FixtureData.Current.Rows.Count(r => r.Sheet is "ENpcResident" or "BNpcName");
        var expectedScripts = FixtureData.Current.Scripts.Count;

        Assert.Equal(expectedTalk + expectedScripts, store.TextEntryCount);
        Assert.Equal(expectedScripts, store.ScriptEntryCount);
        Assert.Equal(expectedNames, store.NameEntryCount);
    }

    [Fact]
    public void Variants_DifferForAtLeastOneRow()
    {
        // Guards against the fixture accidentally shipping the same text twice.
        var tc = FixtureStore.Load(ChineseVariant.Traditional);
        var cn = FixtureStore.Load(ChineseVariant.Simplified);

        var differing = FixtureData.Current.Rows
            .Where(r => r.Sheet == "DefaultTalk")
            .Count(r => tc.TryGetDialogue(r.English, out var t)
                        && cn.TryGetDialogue(r.English, out var c)
                        && t != c);

        Assert.True(differing > 0, "CN and TC fixture text are identical.");
    }
}
