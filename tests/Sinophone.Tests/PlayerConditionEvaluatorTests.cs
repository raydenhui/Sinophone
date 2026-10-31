using Sinophone.Data;

namespace Sinophone.Tests;

/// <summary>
/// Guards the Grand Company conditions that repeat the player's name in some
/// localized lines. Only the player's current company may render.
/// </summary>
public class PlayerConditionEvaluatorTests
{
    [Theory]
    [InlineData("GreaterThan(PlayerParameter(52),0)", 1u, 5, true)]
    [InlineData("GreaterThan(PlayerParameter(52),0)", 2u, 5, false)]
    [InlineData("LessThan(PlayerParameter(53),127)", 2u, 5, true)]
    [InlineData("LessThan(PlayerParameter(53),127)", 1u, 5, false)]
    [InlineData("PlayerParameter(54)", 3u, 0, true)]
    public void Evaluate_UsesOnlyTheCurrentGrandCompany(
        string condition,
        uint companyRow,
        int rank,
        bool expected)
    {
        Assert.Equal(expected, PlayerConditionEvaluator.Evaluate(condition, false, companyRow, rank));
    }

    [Theory]
    [InlineData("GreaterThan(PlayerParameter(52),0)", 1u, 0, false)]
    [InlineData("LessThan(PlayerParameter(52),127)", 1u, 127, false)]
    [InlineData("LessThan(PlayerParameter(52),127)", 1u, 126, true)]
    public void Evaluate_ComparesTheCurrentCompanyRank(
        string condition,
        uint companyRow,
        int rank,
        bool expected)
    {
        Assert.Equal(expected, PlayerConditionEvaluator.Evaluate(condition, false, companyRow, rank));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Evaluate_AnswersPlayerGender(bool isFemale)
    {
        Assert.Equal(isFemale,
            PlayerConditionEvaluator.Evaluate("PlayerParameter(4)", isFemale, 0, 0));
    }

    [Fact]
    public void Evaluate_ReturnsNull_ForUnknownCondition()
    {
        Assert.Null(PlayerConditionEvaluator.Evaluate("Weather(1)", false, 0, 0));
    }
}
