using Sinophone.Diagnostics;

namespace Sinophone.Tests;

/// <summary>
/// The revert rule. A surface that shows the translation and then the English again is the bug
/// the Talk window had, and no check on the final text alone would catch it.
/// </summary>
public class HarnessLineTrackerTests
{
    private static HarnessLineTracker Tracking(string? source)
    {
        var tracker = new HarnessLineTracker();
        tracker.Reset(source);
        return tracker;
    }

    [Fact]
    public void Reset_StartsWithNoTranslationAndNoReverts()
    {
        var tracker = Tracking("Hello");

        Assert.False(tracker.SawTranslation);
        Assert.Equal(0, tracker.Reverts);
    }

    [Fact]
    public void Observe_RecordsATranslation()
    {
        var tracker = Tracking("Hello");

        tracker.Observe("你好");

        Assert.True(tracker.SawTranslation);
        Assert.Equal(0, tracker.Reverts);
    }

    [Fact]
    public void Observe_CountsAReplacementAfterATranslation()
    {
        var tracker = Tracking("Hello");

        tracker.Observe("你好");
        tracker.Observe("Hello");

        Assert.Equal(1, tracker.Reverts);
    }

    [Fact]
    public void Observe_CountsEveryReplacement()
    {
        var tracker = Tracking("Hello");

        tracker.Observe("你好");
        tracker.Observe("Hello");
        tracker.Observe("你好");
        tracker.Observe("Hello");

        Assert.Equal(2, tracker.Reverts);
    }

    [Fact]
    public void Observe_DoesNotCountTheSourceLine_BeforeATranslation()
    {
        var tracker = Tracking("Hello");

        tracker.Observe("Hello");
        tracker.Observe("Hello");

        Assert.Equal(0, tracker.Reverts);
        Assert.False(tracker.SawTranslation);
    }

    [Fact]
    public void Observe_IgnoresAnEmptyText()
    {
        var tracker = Tracking("Hello");

        tracker.Observe("你好");
        tracker.Observe(string.Empty);
        tracker.Observe(null);

        Assert.Equal(0, tracker.Reverts);
        Assert.True(tracker.SawTranslation);
    }

    [Fact]
    public void Observe_DoesNotCountAClosedTextAsAReplacement()
    {
        var tracker = Tracking("Hello");

        tracker.Observe("你好");
        tracker.Observe(string.Empty);

        Assert.Equal(0, tracker.Reverts);
    }

    [Fact]
    public void Reset_ClearsTheCount()
    {
        var tracker = Tracking("Hello");
        tracker.Observe("你好");
        tracker.Observe("Hello");

        tracker.Reset("Bye");

        Assert.Equal(0, tracker.Reverts);
        Assert.False(tracker.SawTranslation);
    }

    [Fact]
    public void Reset_ChangesTheSource()
    {
        var tracker = Tracking("Hello");
        tracker.Reset("Bye");

        tracker.Observe("Bye");

        Assert.False(tracker.SawTranslation);
    }

    [Fact]
    public void IsTranslated_IsFalse_ForTheSourceLine()
        => Assert.False(Tracking("Hello").IsTranslated("Hello"));

    [Fact]
    public void IsTranslated_IsTrue_ForADifferentText()
        => Assert.True(Tracking("Hello").IsTranslated("你好"));

    [Fact]
    public void IsTranslated_IsFalse_ForNothing()
    {
        Assert.False(Tracking("Hello").IsTranslated(null));
        Assert.False(Tracking("Hello").IsTranslated(string.Empty));
    }

    [Fact]
    public void IsTranslated_IsTrue_ForAnyText_WhenTheSourceIsUnknown()
    {
        var tracker = Tracking(null);

        Assert.True(tracker.IsTranslated("你好"));
    }
}
