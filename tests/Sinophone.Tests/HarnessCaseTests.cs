using Sinophone.Diagnostics;

namespace Sinophone.Tests;

/// <summary>
/// The parts of the harness that decide what a test is, without touching the game:
/// how a case reports itself and which checks it runs.
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
}
