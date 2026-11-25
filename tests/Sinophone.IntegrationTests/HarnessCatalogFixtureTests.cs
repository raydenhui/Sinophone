using Sinophone.Diagnostics;

namespace Sinophone.IntegrationTests;

/// <summary>
/// Case building from a real store. The unit project has no store, so this is where a run's
/// cases are checked against the fixture: every case must hold English, the Chinese the store
/// actually answers with, and no line the harness would drop.
/// </summary>
public class HarnessCatalogFixtureTests
{
    [Fact]
    public void FromStore_BuildsCasesFromTheFixture()
    {
        var store = FixtureStore.Load(ChineseVariant.Traditional);

        var cases = HarnessCatalog.FromStore(HarnessSurface.Talk, store, limit: 5);

        Assert.NotEmpty(cases);
        Assert.True(cases.Count <= 5);
    }

    [Fact]
    public void FromStore_EveryCaseHoldsTheEnglishAndTheChinese()
    {
        var store = FixtureStore.Load(ChineseVariant.Traditional);

        var cases = HarnessCatalog.FromStore(HarnessSurface.Talk, store, limit: 5);

        Assert.All(cases, harnessCase =>
        {
            Assert.False(string.IsNullOrWhiteSpace(harnessCase.English));
            Assert.False(string.IsNullOrWhiteSpace(harnessCase.ExpectedChinese));
        });
    }

    [Fact]
    public void FromStore_EveryCaseIsAnsweredByTheStore()
    {
        var store = FixtureStore.Load(ChineseVariant.Traditional);
        var cases = HarnessCatalog.FromStore(HarnessSurface.Talk, store, limit: 5);

        Assert.All(cases, harnessCase =>
        {
            Assert.True(
                store.TryGetDialogue(harnessCase.English, out var chinese),
                $"The store did not answer '{harnessCase.English}'.");
            Assert.Equal(harnessCase.ExpectedChinese, chinese);
        });
    }

    [Fact]
    public void FromStore_CasesAreStable_BetweenCalls()
    {
        var store = FixtureStore.Load(ChineseVariant.Traditional);

        var first = HarnessCatalog.FromStore(HarnessSurface.Talk, store, limit: 5);
        var second = HarnessCatalog.FromStore(HarnessSurface.Talk, store, limit: 5);

        Assert.Equal(
            first.Select(static harnessCase => harnessCase.English).ToArray(),
            second.Select(static harnessCase => harnessCase.English).ToArray());
    }

    [Fact]
    public void FromStore_WithAZeroLimit_ReturnsNothing()
    {
        var store = FixtureStore.Load(ChineseVariant.Traditional);

        Assert.Empty(HarnessCatalog.FromStore(HarnessSurface.Talk, store, limit: 0));
    }

    [Fact]
    public void FromStore_KeepsTheSampleLimit()
    {
        var store = FixtureStore.Load(ChineseVariant.Traditional);

        var cases = HarnessCatalog.FromStore(HarnessSurface.Talk, store, limit: 2);

        Assert.True(cases.Count <= 2);
    }

    [Fact]
    public void FromStore_DropsNoMarkupLines()
    {
        var store = FixtureStore.Load(ChineseVariant.Traditional);

        var cases = HarnessCatalog.FromStore(HarnessSurface.Talk, store, limit: 100);

        Assert.All(cases, harnessCase =>
        {
            Assert.False(HarnessAssertions.HasMacroLeak(harnessCase.English), harnessCase.English);
            Assert.False(HarnessAssertions.HasMacroLeak(harnessCase.ExpectedChinese), harnessCase.ExpectedChinese);
        });
    }

    [Fact]
    public void SheetTextEntries_AreSorted()
    {
        var store = FixtureStore.Load(ChineseVariant.Traditional);

        var entries = store.SheetTextEntries;
        var sorted = entries
            .Select(static entry => entry.English)
            .OrderBy(static english => english, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(sorted, entries.Select(static entry => entry.English).ToArray());
    }

    [Fact]
    public void SheetTextEntries_EveryEntryResolves()
    {
        var store = FixtureStore.Load(ChineseVariant.Traditional);

        Assert.All(store.SheetTextEntries, entry =>
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.English));
            Assert.False(string.IsNullOrWhiteSpace(entry.Chinese));
        });
    }
}
