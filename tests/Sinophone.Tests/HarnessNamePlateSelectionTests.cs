using Sinophone.Diagnostics;

namespace Sinophone.Tests;

/// <summary>
/// The rule that picks which names a nameplate run should try. The engine side asks it before
/// it drives anything, so the filtering is the same in every run.
/// </summary>
public class HarnessNamePlateSelectionTests
{
    [Theory]
    [InlineData("Y'shtola")]
    [InlineData("Summoning Bell")]
    [InlineData("AB")]
    public void IsWorthTesting_IsTrue_ForARealName(string name)
        => Assert.True(HarnessNamePlateSelection.IsWorthTesting(name));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("A")]
    [InlineData("???")]
    [InlineData("...")]
    [InlineData("12")]
    public void IsWorthTesting_IsFalse_ForAMaskedOrEmptyName(string? name)
        => Assert.False(HarnessNamePlateSelection.IsWorthTesting(name));

    [Fact]
    public void IsWorthTesting_TrimsSurroundingSpace()
        => Assert.True(HarnessNamePlateSelection.IsWorthTesting("  Bell  "));

    [Fact]
    public void FirstWorthTesting_ReturnsTheFirstRealName()
        => Assert.Equal(
            "Summoning Bell",
            HarnessNamePlateSelection.FirstWorthTesting([null, "???", "A", "Summoning Bell", "Other"]));

    [Fact]
    public void FirstWorthTesting_ReturnsTheFallback_WhenNothingQualifies()
        => Assert.Equal(
            "fallback",
            HarnessNamePlateSelection.FirstWorthTesting(["", "A", "???"], "fallback"));

    [Fact]
    public void FirstWorthTesting_ReturnsNull_WithNoFallback()
        => Assert.Null(HarnessNamePlateSelection.FirstWorthTesting([]));
}
