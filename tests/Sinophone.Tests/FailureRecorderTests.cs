using System.Text.Json;
using Sinophone.Diagnostics;

namespace Sinophone.Tests;

public class FailureRecorderTests
{
    private static FailureBuildInfo Build()
        => new("test", "tc", "7.4", "en", 11);

    [Theory]
    [InlineData("miss", FailureLevel.Miss)]
    [InlineData("MISS", FailureLevel.Miss)]
    [InlineData("all", FailureLevel.All)]
    [InlineData("fallback", FailureLevel.Fallback)]
    [InlineData("", FailureLevel.Fallback)]
    [InlineData(null, FailureLevel.Fallback)]
    public void Level_ParsesTheEnvironmentValue(string? value, FailureLevel expected)
        => Assert.Equal(expected, FailureLevels.Parse(value));

    [Fact]
    public void Id_IsStableForOneSubjectAndDiffersForAnother()
    {
        var first = FailureRecorder.MakeId("talk\u001ftest\u001fHello.");
        var again = FailureRecorder.MakeId("talk\u001ftest\u001fHello.");
        var other = FailureRecorder.MakeId("talk\u001ftest\u001fHello!");

        Assert.Equal(first, again);
        Assert.NotEqual(first, other);
        Assert.StartsWith("c-", first);
    }

    [Fact]
    public void Miss_WritesTheCaseIndexLatestAndSummary()
    {
        using var folder = new TempFolder();
        var written = new List<string>();
        using var recording = new FailureRecorder(folder.Path, Build(), wroteFile: written.Add);

        var trace = recording.Begin("talk", "test", "Hello there.");
        Assert.NotNull(trace);

        trace!.Step("lookup", "enter");
        trace.Finish(FailureOutcome.Miss, "no-translation");
        recording.Flush();

        var casePath = Path.Combine(recording.SessionDirectory, "cases", trace.Case.Id + ".json");
        Assert.True(File.Exists(casePath));
        Assert.Equal(new[] { casePath }, written);

        using var json = JsonDocument.Parse(File.ReadAllText(casePath));
        var root = json.RootElement;
        Assert.Equal("miss", root.GetProperty("outcome").GetString());
        Assert.Equal("no-translation", root.GetProperty("reason").GetString());
        Assert.Equal("talk", root.GetProperty("surface").GetString());
        Assert.Equal("Hello there.", root.GetProperty("input").GetProperty("text").GetString());
        Assert.Equal("lookup", root.GetProperty("steps")[0].GetProperty("stage").GetString());

        Assert.True(File.Exists(Path.Combine(recording.SessionDirectory, "index.jsonl")));
        Assert.True(File.Exists(Path.Combine(recording.SessionDirectory, "summary.json")));
        Assert.True(File.Exists(Path.Combine(folder.Path, "latest.json")));
    }

    [Fact]
    public void Repeat_KeepsOneFileAndCountsTheCopies()
    {
        using var folder = new TempFolder();
        using var recorder = new FailureRecorder(folder.Path, Build());

        var first = recorder.Begin("talk", "test", "Hello there.");
        Assert.NotNull(first);
        first!.Finish(FailureOutcome.Miss);
        recorder.Flush();

        Assert.Null(recorder.Begin("talk", "test", "Hello there."));
        Assert.Null(recorder.Begin("talk", "test", "Hello there."));
        recorder.Flush();

        var path = Path.Combine(recorder.SessionDirectory, "cases", first.Case.Id + ".json");
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(3, json.RootElement.GetProperty("repeat").GetInt32());
        Assert.Single(Directory.GetFiles(Path.Combine(recorder.SessionDirectory, "cases"), "*.json"));
    }

    [Fact]
    public void FallbackLevel_KeepsAMissAndDropsAnExactHit()
    {
        using var folder = new TempFolder();
        using var recorder = new FailureRecorder(folder.Path, Build(), FailureLevel.Fallback);

        var hit = recorder.Begin("talk", "test", "A hit.");
        hit!.Finish(FailureOutcome.Hit);

        var miss = recorder.Begin("talk", "test", "A miss.");
        miss!.Finish(FailureOutcome.Miss);

        recorder.Flush();

        var cases = Directory.GetFiles(Path.Combine(recorder.SessionDirectory, "cases"), "*.json");
        Assert.Single(cases);
        using var json = JsonDocument.Parse(File.ReadAllText(cases[0]));
        Assert.Equal("A miss.", json.RootElement.GetProperty("input").GetProperty("text").GetString());
    }

    [Fact]
    public void AllLevel_KeepsAnExactHit()
    {
        using var folder = new TempFolder();
        using var recorder = new FailureRecorder(folder.Path, Build(), FailureLevel.All);

        var trace = recorder.Begin("talk", "test", "A hit.");
        trace!.Finish(FailureOutcome.Hit);
        recorder.Flush();

        Assert.Single(Directory.GetFiles(Path.Combine(recorder.SessionDirectory, "cases"), "*.json"));
    }

    [Fact]
    public void ApproximateHit_IsWrittenAsAFallback()
    {
        using var folder = new TempFolder();
        using var recorder = new FailureRecorder(folder.Path, Build(), FailureLevel.Fallback);

        var trace = recorder.Begin("talk", "test", "A loose hit.");
        trace!.Step("text.exact", "hit", approximate: true);
        trace.Finish(FailureOutcome.Hit);
        recorder.Flush();

        var path = Path.Combine(recorder.SessionDirectory, "cases", trace.Case.Id + ".json");
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal("fallback", json.RootElement.GetProperty("outcome").GetString());
        Assert.True(json.RootElement.GetProperty("approximate").GetBoolean());
    }

    [Fact]
    public void LongTextAndManySteps_AreCapped()
    {
        using var folder = new TempFolder();
        using var recorder = new FailureRecorder(folder.Path, Build());

        var trace = recorder.Begin("talk", "test", new string('a', 1_000));
        for (var i = 0; i < 100; i++)
            trace!.Step("step", i.ToString());

        trace!.Finish(FailureOutcome.Miss);
        recorder.Flush();

        var path = Path.Combine(recorder.SessionDirectory, "cases", trace.Case.Id + ".json");
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        var text = json.RootElement.GetProperty("input").GetProperty("text").GetString();

        Assert.NotNull(text);
        Assert.True(text!.Length <= FailureText.MaxLength + 1);
        Assert.True(json.RootElement.GetProperty("steps").GetArrayLength() <= 65);
    }

    [Fact]
    public void TryCreate_ReturnsNullAndWarns_WhenTheRootIsUnusable()
    {
        using var folder = new TempFolder();
        Directory.CreateDirectory(folder.Path);
        var blocker = Path.Combine(folder.Path, "blocker");
        File.WriteAllText(blocker, "not a folder");

        var warnings = new List<string>();
        var recorder = FailureRecorder.TryCreate(
            Path.Combine(blocker, "child"),
            Build(),
            FailureLevel.Fallback,
            null,
            warnings.Add);

        Assert.Null(recorder);
        Assert.Single(warnings);
    }

    [Fact]
    public void DisabledRecorder_ReturnsNoTrace()
    {
        using var folder = new TempFolder();
        using var recorder = new FailureRecorder(folder.Path, Build());
        recorder.Dispose();

        Assert.False(recorder.Enabled);
        Assert.Null(recorder.Begin("talk", "test", "Hello."));
    }

    private sealed class TempFolder : IDisposable
    {
        public string Path { get; } =
            System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "sinophone-failure-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                    Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
