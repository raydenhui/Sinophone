using Sinophone.Data;

namespace Sinophone.Tests;

public class UiTextStyleTests
{
    [Fact]
    public void TryParse_ReadsForegroundAndGlowColors()
    {
        const string text =
            "<UIForeground>F20223</UIForeground><UIGlow>F201FC</UIGlow>測試<UIGlow>01</UIGlow><UIForeground>01</UIForeground>";

        Assert.True(UiTextStyle.TryParse(text, out var style));
        Assert.Equal(0xF20223u, style.Foreground);
        Assert.Equal(0xF201FCu, style.Glow);
    }

    [Fact]
    public void TryParse_ReturnsFalse_WhenOnlyResetTagsArePresent()
    {
        const string text = "<UIForeground>01</UIForeground><UIGlow>01</UIGlow>測試";

        Assert.False(UiTextStyle.TryParse(text, out _));
    }
}
