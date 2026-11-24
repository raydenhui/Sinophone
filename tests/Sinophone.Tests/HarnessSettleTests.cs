using Sinophone.Diagnostics;

namespace Sinophone.Tests;

/// <summary>
/// The settle rule decides when a case can be measured. Every failure mode here is one a run
/// would hit: a line that never stops changing, a line that settles at once, and a line that
/// settles after the reveal.
/// </summary>
public class HarnessSettleTests
{
    [Fact]
    public void Observe_IsNotSettled_OnTheFirstSample()
    {
        var settle = new HarnessSettle();

        Assert.False(settle.Observe("line"));
        Assert.False(settle.IsSettled);
        Assert.Equal("line", settle.Last);
    }

    [Fact]
    public void Observe_Settles_AfterTheSameTextRepeats()
    {
        var settle = new HarnessSettle();

        for (var index = 0; index < HarnessSettle.SettleFrames - 1; index++)
            Assert.False(settle.Observe("line"));

        Assert.True(settle.Observe("line"));
        Assert.True(settle.IsSettled);
    }

    [Fact]
    public void Observe_RestartsTheCount_WhenTheTextChanges()
    {
        var settle = new HarnessSettle();

        settle.Observe("a");
        settle.Observe("a");
        settle.Observe("a");
        Assert.False(settle.IsSettled);

        // The reveal wrote one more character, so the wait starts again.
        settle.Observe("ab");
        Assert.False(settle.IsSettled);

        for (var index = 0; index < HarnessSettle.SettleFrames - 1; index++)
            Assert.False(settle.Observe("ab"));

        Assert.True(settle.Observe("ab"));
    }

    [Fact]
    public void Observe_CountsEveryChange()
    {
        var settle = new HarnessSettle();

        settle.Observe("a");
        settle.Observe("ab");
        settle.Observe("abc");

        Assert.Equal(2, settle.Changes);
        Assert.Equal(new[] { "a", "ab", "abc" }, settle.Samples);
    }

    [Fact]
    public void Observe_TreatsNullAsEmpty()
    {
        var settle = new HarnessSettle();

        settle.Observe(null);
        settle.Observe(string.Empty);

        Assert.False(settle.Changes > 0);
        Assert.Equal(string.Empty, settle.Last);
    }

    [Fact]
    public void Observe_ReportsNoChange_WhenAnEmptyNodeStaysEmpty()
    {
        var settle = new HarnessSettle();

        settle.Observe(null);
        settle.Observe(null);

        Assert.Equal(0, settle.Changes);
        Assert.Single(settle.Samples);
    }

    [Fact]
    public void Observe_TimesOut_WhenTheTextNeverSettles()
    {
        var settle = new HarnessSettle();

        var stopped = false;
        for (var index = 0; index < HarnessSettle.TimeoutFrames && !stopped; index++)
            stopped = settle.Observe(index.ToString());

        Assert.True(stopped);
        Assert.True(settle.HasTimedOut);
        Assert.False(settle.IsSettled);
    }

    [Fact]
    public void Observe_TimesOut_WhenTheNodeStaysEmpty()
    {
        var settle = new HarnessSettle();

        var stopped = false;
        for (var index = 0; index < HarnessSettle.TimeoutFrames && !stopped; index++)
            stopped = settle.Observe(null);

        Assert.True(stopped);
        Assert.True(settle.HasTimedOut);
        Assert.False(settle.IsSettled);
    }

    [Fact]
    public void Observe_DoesNotTimeOut_BeforeTheLimit()
    {
        var settle = new HarnessSettle();

        for (var index = 0; index < HarnessSettle.TimeoutFrames - 1; index++)
            settle.Observe(index.ToString());

        Assert.False(settle.HasTimedOut);
    }

    [Fact]
    public void Reset_ClearsEverything()
    {
        var settle = new HarnessSettle();
        settle.Observe("a");
        settle.Observe("b");

        settle.Reset();

        Assert.False(settle.IsSettled);
        Assert.False(settle.HasTimedOut);
        Assert.Equal(0, settle.Changes);
        Assert.Empty(settle.Samples);
        Assert.Equal(string.Empty, settle.Last);
    }
}
