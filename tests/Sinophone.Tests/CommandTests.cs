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
}
