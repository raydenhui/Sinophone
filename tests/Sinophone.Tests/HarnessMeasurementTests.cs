using Sinophone.Diagnostics;

namespace Sinophone.Tests;

/// <summary>
/// The measurement carries what the checks read. Its defaults are what a surface reports when it
/// could not measure something, so the defaults have to mean "not measured", not "zero".
/// </summary>
public class HarnessMeasurementTests
{
    [Fact]
    public void Default_ReportsNothingMeasured()
    {
        var measurement = new HarnessMeasurement();

        Assert.Null(measurement.Drawn);
        Assert.Null(measurement.English);
        Assert.Null(measurement.Expected);
        Assert.Equal(0, measurement.TextWidth);
        Assert.Equal(0, measurement.TextHeight);
        Assert.Equal(0, measurement.BoxWidth);
        Assert.Equal(0, measurement.BoxHeight);
        Assert.Equal(0, measurement.Reverts);
        Assert.Equal(0, measurement.MissingGlyphs);
        Assert.Null(measurement.SkipReason);
    }

    [Fact]
    public void Passed_IsTrue_WithNoFindings()
        => Assert.True(new HarnessMeasurement().Passed);

    [Fact]
    public void Passed_IsTrue_WhenEveryFindingPassed()
    {
        var measurement = new HarnessMeasurement
        {
            Findings =
            [
                new HarnessFinding(HarnessCheck.Translated, true, "ok"),
                new HarnessFinding(HarnessCheck.Fits, true, "ok"),
            ],
        };

        Assert.True(measurement.Passed);
    }

    [Fact]
    public void Passed_IsFalse_WhenOneFindingFailed()
    {
        var measurement = new HarnessMeasurement
        {
            Findings =
            [
                new HarnessFinding(HarnessCheck.Translated, true, "ok"),
                new HarnessFinding(HarnessCheck.Fits, false, "too wide"),
            ],
        };

        Assert.False(measurement.Passed);
    }
}
