using Sinophone.Data;

namespace Sinophone.Tests;

public class GenderedPronounsTests
{
    [Fact]
    public void Neutralize_GivesSameKey_ForMaleAndFemaleWording()
    {
        const string male =
            "One could reduce his body to aether without losing the grip on his spirit.";
        const string female =
            "One could reduce her body to aether without losing the grip on her spirit.";

        Assert.Equal(GenderedPronouns.Neutralize(male), GenderedPronouns.Neutralize(female));
    }

    [Theory]
    [InlineData("He said he would go.")]
    [InlineData("She said she would go.")]
    public void Neutralize_HandlesPronouns_AtSentenceStart(string text)
        => Assert.Equal(
            GenderedPronouns.Neutralize("He said he would go."),
            GenderedPronouns.Neutralize(text));

    [Fact]
    public void Neutralize_CollapsesContractions()
        => Assert.Equal(
            GenderedPronouns.Neutralize("He's the captain."),
            GenderedPronouns.Neutralize("She's the captain."));

    [Fact]
    public void Neutralize_CollapsesObjectAndPossessivePronouns()
        => Assert.Equal(
            GenderedPronouns.Neutralize("Give the letter to him."),
            GenderedPronouns.Neutralize("Give the letter to her."));

    [Theory]
    [InlineData("therapist")]
    [InlineData("history")]
    [InlineData("sherry")]
    [InlineData("hero")]
    public void Neutralize_LeavesPronounsInsideWords(string text)
        => Assert.Same(text, GenderedPronouns.Neutralize(text));

    [Fact]
    public void Neutralize_ReturnsSameInstance_WhenNoPronounIsPresent()
    {
        const string key = "Sundhimal welcomes you to Gridania.";

        Assert.Same(key, GenderedPronouns.Neutralize(key));
    }

    [Fact]
    public void Neutralize_ReturnsEmpty_ForEmptyInput()
        => Assert.Equal(string.Empty, GenderedPronouns.Neutralize(string.Empty));
}
