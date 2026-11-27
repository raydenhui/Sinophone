using Sinophone.Data;

namespace Sinophone.Tests;

public class GenderedNounsTests
{
    [Fact]
    public void Neutralize_GivesSameKey_ForLadAndLass()
        => Assert.Equal(
            GenderedNouns.Neutralize("Greetin's, lad. What can I do for ye this fine evenin'?"),
            GenderedNouns.Neutralize("Greetin's, lass. What can I do for ye this fine evenin'?"));

    [Fact]
    public void Neutralize_GivesSameKey_ForSirAndMadam()
        => Assert.Equal(
            GenderedNouns.Neutralize("At your service, sir."),
            GenderedNouns.Neutralize("At your service, madam."));

    [Fact]
    public void Neutralize_GivesSameKey_ForManAndWoman()
        => Assert.Equal(
            GenderedNouns.Neutralize("The very man who saved the child."),
            GenderedNouns.Neutralize("The very woman who saved the child."));

    [Fact]
    public void Neutralize_GivesSameKey_ForMenAndWomen()
        => Assert.Equal(
            GenderedNouns.Neutralize("You men are all the same."),
            GenderedNouns.Neutralize("You women are all the same."));

    [Theory]
    [InlineData("womanhood")]
    [InlineData("manhole")]
    [InlineData("manor")]
    public void Neutralize_LeavesLongerWordsAlone(string text)
        => Assert.Same(text, GenderedNouns.Neutralize(text));

    [Fact]
    public void Neutralize_CollapsesContractions()
        => Assert.Equal(
            GenderedNouns.Neutralize("Mind the step, lad's."),
            GenderedNouns.Neutralize("Mind the step, lass's."));

    [Theory]
    [InlineData("ladder")]
    [InlineData("lassie")]
    [InlineData("madame")]
    [InlineData("siren")]
    public void Neutralize_LeavesNounsInsideWords(string text)
        => Assert.Same(text, GenderedNouns.Neutralize(text));

    [Fact]
    public void Neutralize_ReturnsSameInstance_WhenNoNounIsPresent()
    {
        const string key = "Sundhimal welcomes you to Gridania.";

        Assert.Same(key, GenderedNouns.Neutralize(key));
    }

    [Fact]
    public void Neutralize_ReturnsEmpty_ForEmptyInput()
        => Assert.Equal(string.Empty, GenderedNouns.Neutralize(string.Empty));
}
