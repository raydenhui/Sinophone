using Sinophone.Diagnostics;

namespace Sinophone.Tests;

/// <summary>
/// The report is what a reviewer reads after a run and what a later run is compared with, so
/// its shape is part of the feature. The round trip is the important case: a run written to disk
/// and read back must say the same thing.
/// </summary>
public class HarnessReportTests
{
    private static HarnessReport Sample()
    {
        var report = new HarnessReport { Variant = "Traditional", GameVersion = "7.2" };
        report.Add(new HarnessResult(
            "talk-01", HarnessSurface.Talk, HarnessOutcome.Passed, "Hello", "你好")
        {
            Findings = [new HarnessFinding(HarnessCheck.Translated, true, "The expected Chinese was drawn.")],
            Measurement = new HarnessMeasurement { Drawn = "你好", English = "Hello", TextWidth = 40 },
        });

        report.Add(new HarnessResult(
            "bubble-01", HarnessSurface.ChatBubble, HarnessOutcome.Failed, "Bye", "再見", "The English line was drawn."));

        report.Add(new HarnessResult(
            "bubble-02", HarnessSurface.ChatBubble, HarnessOutcome.Skipped, "Later", Detail: "Not in a zone."));

        return report;
    }

    [Fact]
    public void Summary_CountsEachOutcome()
        => Assert.Equal("1 passed, 1 failed, 1 skipped of 3.", Sample().Summary());

    [Fact]
    public void AllPassed_IsFalse_WhenOneCaseFailed()
        => Assert.False(Sample().AllPassed);

    [Fact]
    public void AllPassed_IsTrue_WhenEveryCasePassed()
    {
        var report = new HarnessReport();
        report.Add(new HarnessResult("one", HarnessSurface.Talk, HarnessOutcome.Passed, "Hello"));

        Assert.True(report.AllPassed);
    }

    [Fact]
    public void Add_KeepsTheOrder()
    {
        var report = Sample();

        Assert.Equal(new[] { "talk-01", "bubble-01", "bubble-02" }, report.Results.Select(static result => result.Id).ToArray());
    }

    [Fact]
    public void Add_ReturnsTheResult()
    {
        var report = new HarnessReport();
        var added = new HarnessResult("one", HarnessSurface.Talk, HarnessOutcome.Passed, "Hello");

        Assert.Same(added, report.Add(added));
    }

    [Fact]
    public void ToJson_ThenFromJson_KeepsTheResults()
    {
        var report = Sample();

        var read = HarnessReport.FromJson(report.ToJson());

        Assert.NotNull(read);
        Assert.Equal(report.Summary(), read.Summary());
        Assert.Equal(
            report.Results.Select(static result => result.Id).ToArray(),
            read.Results.Select(static result => result.Id).ToArray());
        Assert.Equal("Traditional", read.Variant);
        Assert.Equal("7.2", read.GameVersion);
    }

    [Fact]
    public void ToJson_ThenFromJson_KeepsTheFindings()
    {
        var read = HarnessReport.FromJson(Sample().ToJson());

        var findings = Assert.Single(read!.Results[0].Findings);
        Assert.Equal(HarnessCheck.Translated, findings.Check);
        Assert.True(findings.Passed);
        Assert.Equal(40, read.Results[0].Measurement!.TextWidth);
    }

    [Fact]
    public void ToJson_WritesOutcomesAsNames()
    {
        var json = Sample().ToJson();

        Assert.Contains("\"Passed\"", json);
        Assert.Contains("\"Failed\"", json);
        Assert.Contains("\"Skipped\"", json);
    }

    [Fact]
    public void FromJson_ReturnsNull_ForEmptyInput()
    {
        Assert.Null(HarnessReport.FromJson(null));
        Assert.Null(HarnessReport.FromJson(""));
        Assert.Null(HarnessReport.FromJson("   "));
    }

    [Fact]
    public void FromJson_ReturnsNull_ForBrokenJson()
        => Assert.Null(HarnessReport.FromJson("{\"Results\": ["));

    [Fact]
    public void FromJson_ReturnsNull_ForAnotherFormatVersion()
    {
        var json = Sample().ToJson().Replace("\"Version\": 1", "\"Version\": 99");

        Assert.Null(HarnessReport.FromJson(json));
    }

    [Fact]
    public void FromJson_ReturnsNull_ForAJsonObjectThatIsNotAReport()
        => Assert.Null(HarnessReport.FromJson("{\"Name\": \"something else\"}"));

    [Fact]
    public void FileNameFor_IsKeyedByTheStartTime()
        => Assert.Equal(
            "harness-20261124-130501.json",
            HarnessReport.FileNameFor(new DateTimeOffset(2026, 11, 24, 13, 5, 1, TimeSpan.Zero)));

    [Fact]
    public void FileNameFor_GivesDifferentNames_ForDifferentRuns()
    {
        var first = HarnessReport.FileNameFor(new DateTimeOffset(2026, 11, 24, 13, 5, 1, TimeSpan.Zero));
        var second = HarnessReport.FileNameFor(new DateTimeOffset(2026, 11, 24, 13, 5, 2, TimeSpan.Zero));

        Assert.NotEqual(first, second);
    }
}
