using Sinophone.Diagnostics;

namespace Sinophone.Tests;

/// <summary>
/// The parts of the harness that decide what a test is, without touching the game:
/// which surfaces exist, how a case reports itself, and how the Talk driver reads a run.
/// </summary>
public class HarnessCaseTests
{
    [Fact]
    public void HasTag_IsCaseInsensitive()
    {
        var harnessCase = new HarnessCase("id", HarnessSurface.Talk, "Hello", Tags: ["Long"]);

        Assert.True(harnessCase.HasTag("long"));
    }

    [Fact]
    public void HasTag_IsFalse_WhenTheCaseHasNoTags()
        => Assert.False(new HarnessCase("id", HarnessSurface.Talk, "Hello").HasTag("long"));

    [Fact]
    public void HasTag_IsFalse_ForAnotherTag()
    {
        var harnessCase = new HarnessCase("id", HarnessSurface.Talk, "Hello", Tags: ["short"]);

        Assert.False(harnessCase.HasTag("long"));
    }

    [Fact]
    public void Case_DefaultsToEveryCheck()
        => Assert.Equal(HarnessCheck.All, new HarnessCase("id", HarnessSurface.Talk, "Hello").Checks);

    [Fact]
    public void HarnessCheck_All_HoldsEveryNamedCheck()
    {
        var named = Enum.GetValues<HarnessCheck>()
            .Where(static check => check != HarnessCheck.None && check != HarnessCheck.All);

        Assert.All(named, check => Assert.True(HarnessCheck.All.HasFlag(check), check.ToString()));
    }

    [Fact]
    public void HarnessSurface_ListsTheSurfacesTheTabCanShow()
    {
        var surfaces = Enum.GetValues<HarnessSurface>();

        Assert.Contains(HarnessSurface.Talk, surfaces);
        Assert.Contains(HarnessSurface.ChatBubble, surfaces);
    }

    [Fact]
    public void HarnessOutcome_HasTheFourStatesTheTabDraws()
    {
        Assert.True(Enum.IsDefined(typeof(HarnessOutcome), HarnessOutcome.Passed));
        Assert.True(Enum.IsDefined(typeof(HarnessOutcome), HarnessOutcome.Failed));
        Assert.True(Enum.IsDefined(typeof(HarnessOutcome), HarnessOutcome.Skipped));
        Assert.True(Enum.IsDefined(typeof(HarnessOutcome), HarnessOutcome.NotRun));
    }
}
