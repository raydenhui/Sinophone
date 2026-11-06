using Sinophone.Data;

namespace Sinophone.Tests;

public class TimeOfDayWordsTests
{
    [Fact]
    public void Neutralize_GivesSameKey_ForDayAndEvening()
        => Assert.Equal(
            TimeOfDayWords.Neutralize("Greetin's, lad. What can I do for ye this fine day?"),
            TimeOfDayWords.Neutralize("Greetin's, lad. What can I do for ye this fine evenin'?"));

    [Theory]
    [InlineData("Good morning, adventurer.")]
    [InlineData("Good afternoon, adventurer.")]
    [InlineData("Good evening, adventurer.")]
    [InlineData("Good night, adventurer.")]
    public void Neutralize_GivesSameKey_ForEveryTimeOfDay(string text)
        => Assert.Equal(
            TimeOfDayWords.Neutralize("Good morning, adventurer."),
            TimeOfDayWords.Neutralize(text));

    [Theory]
    [InlineData("today")]
    [InlineData("daytime")]
    [InlineData("morningstar")]
    [InlineData("knight")]
    public void Neutralize_LeavesWordsThatOnlyContainATimeWord(string text)
        => Assert.Same(text, TimeOfDayWords.Neutralize(text));

    [Fact]
    public void Neutralize_ReturnsSameInstance_WhenNoTimeWordIsPresent()
    {
        const string key = "Sundhimal welcomes you to Gridania.";

        Assert.Same(key, TimeOfDayWords.Neutralize(key));
    }

    [Fact]
    public void Neutralize_ReturnsEmpty_ForEmptyInput()
        => Assert.Equal(string.Empty, TimeOfDayWords.Neutralize(string.Empty));
}
