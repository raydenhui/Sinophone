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
    /// The native test subcommands exist only in a testing build. A release build must not
    /// recognise them, so a user cannot reach probe code that was compiled out. The test asks
    /// the plugin assembly which build it is, so one test covers both configurations.
    /// </summary>
    [Theory]
    [InlineData("native", "NativeTest")]
    [InlineData("native restore", "NativeRestore")]
    [InlineData(" Native Restore ", "NativeRestore")]
    [InlineData("native  restore", "Unknown")] // Internal whitespace is not collapsed.
    public void ParseCommand_HandlesNativeTestPerBuild(string arguments, string testingBuildResult)
    {
        var expected = Sinophone.Diagnostics.HarnessBuild.IsTesting
            ? testingBuildResult
            : "Unknown";

        Assert.Equal(expected, Plugin.ParseCommand(arguments).ToString());
    }

    [Fact]
    public void UsageText_AdvertisesNativeTestPerBuild()
    {
        var advertises = Sinophone.Diagnostics.HarnessBuild.IsTesting;

        Assert.Equal(advertises, Plugin.UsageText.Contains("native"));
        Assert.Contains("/sinophone", Plugin.UsageText);
    }
}
