using Dalamud.Game.Addon.Lifecycle;
using Sinophone.Handlers;

namespace Sinophone.IntegrationTests;

/// <summary>
/// Guards the check that decides when the overlay should disappear. The game
/// hides the Talk addon at the end of a conversation instead of closing it, so
/// watching only PostClose left the last line on screen.
/// </summary>
public class TalkHandlerLifecycleTests
{
    [Theory]
    [InlineData(AddonEvent.PostClose, true)]
    [InlineData(AddonEvent.PostHide, true)]
    [InlineData(AddonEvent.PreFinalize, true)]
    [InlineData(AddonEvent.PreSetup, false)]
    [InlineData(AddonEvent.PreRefresh, false)]
    public void ShouldHideOverlay_ReturnsTrue_WhenAddonIsGone(AddonEvent eventType, bool expected)
    {
        Assert.Equal(expected, TalkHandler.ShouldHideOverlay(eventType));
    }
}
