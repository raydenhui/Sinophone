namespace Sinophone.Tests;

public class CommandTests
{
    [Theory]
    [InlineData("", Plugin.CommandAction.Toggle)]
    [InlineData("   ", Plugin.CommandAction.Toggle)]
    [InlineData("toggle", Plugin.CommandAction.Toggle)]
    [InlineData("TOGGLE", Plugin.CommandAction.Toggle)]
    [InlineData("update", Plugin.CommandAction.Update)]
    [InlineData(" Update ", Plugin.CommandAction.Update)]
    [InlineData("status", Plugin.CommandAction.Status)]
    [InlineData("reload", Plugin.CommandAction.Unknown)]
    public void ParseCommand_MapsArguments(string arguments, Plugin.CommandAction expected)
        => Assert.Equal(expected, Plugin.ParseCommand(arguments));

    [Fact]
    public void ParseCommand_TreatsNullAsToggle()
        => Assert.Equal(Plugin.CommandAction.Toggle, Plugin.ParseCommand(null));
}
