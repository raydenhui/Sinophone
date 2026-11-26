using System.Text.RegularExpressions;
using Sinophone.Localization;

namespace Sinophone.Tests;

public class InterfaceStringsTests
{
    public static TheoryData<InterfaceLanguage> Languages => new()
    {
        InterfaceLanguage.English,
        InterfaceLanguage.SimplifiedChinese,
        InterfaceLanguage.TraditionalChinese,
        InterfaceLanguage.Japanese,
    };

    [Theory]
    [MemberData(nameof(Languages))]
    public void EveryString_IsFilled(InterfaceLanguage language)
    {
        var strings = InterfaceStrings.For(language);

        foreach (var property in typeof(InterfaceStrings).GetProperties())
        {
            var value = (string)property.GetValue(strings)!;
            Assert.False(
                string.IsNullOrWhiteSpace(value),
                $"{language}.{property.Name} is empty");
        }
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void FormatPlaceholders_MatchEnglish(InterfaceLanguage language)
    {
        var english = InterfaceStrings.For(InterfaceLanguage.English);
        var translated = InterfaceStrings.For(language);

        foreach (var property in typeof(InterfaceStrings).GetProperties())
        {
            var expected = Placeholders((string)property.GetValue(english)!);
            var actual = Placeholders((string)property.GetValue(translated)!);

            Assert.True(
                expected.SetEquals(actual),
                $"{language}.{property.Name} uses {string.Join(", ", actual)}, " +
                $"English uses {string.Join(", ", expected)}");
        }
    }

    [Fact]
    public void Auto_UsesTheEnglishTable()
        => Assert.Same(InterfaceStrings.For(InterfaceLanguage.English),
            InterfaceStrings.For(InterfaceLanguage.Auto));

    private static HashSet<int> Placeholders(string text)
        => Regex.Matches(text, @"\{(\d+)")
            .Select(static match => int.Parse(match.Groups[1].Value))
            .ToHashSet();
}
