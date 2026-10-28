namespace Sinophone.Tests;

public class CommandTests
{
    [Theory]
    [InlineData("", "Toggle")]
    [InlineData("   ", "Toggle")]
    [InlineData("toggle", "Toggle")]
    [InlineData("TOGGLE", "Toggle")]
    [InlineData("update", "Update")]
    [InlineData(" Update ", "Update")]
    [InlineData("status", "Status")]
    [InlineData("reload", "Unknown")]
    public void ParseCommand_MapsArguments(string arguments, string expected)
        => Assert.Equal(expected, Plugin.ParseCommand(arguments).ToString());

    [Fact]
    public void ParseCommand_TreatsNullAsToggle()
        => Assert.Equal("Toggle", Plugin.ParseCommand(null).ToString());

    /// <summary>
    /// The native test subcommands exist only in debug builds. A release build must
    /// not recognise them, so a user cannot reach probe code that was compiled out.
    /// </summary>
#if DEBUG
    [Theory]
    [InlineData("native", "NativeTest")]
    [InlineData("native restore", "NativeRestore")]
    [InlineData(" Native Restore ", "NativeRestore")]
    [InlineData("native  restore", "Unknown")] // Internal whitespace is not collapsed.
#else
    [Theory]
    [InlineData("native", "Unknown")]
    [InlineData("native restore", "Unknown")]
    [InlineData(" Native Restore ", "Unknown")]
#endif
    public void ParseCommand_HandlesNativeTestPerBuild(string arguments, string expected)
        => Assert.Equal(expected, Plugin.ParseCommand(arguments).ToString());

    [Fact]
    public void UsageText_AdvertisesNativeTestPerBuild()
    {
#if DEBUG
        Assert.Contains("native", Plugin.UsageText);
#else
        Assert.DoesNotContain("native", Plugin.UsageText);
#endif
        Assert.Contains("/sinophone", Plugin.UsageText);
    }
}
