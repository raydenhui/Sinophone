using Sinophone.Data;

namespace Sinophone.Tests;

/// <summary>
/// The CN and TC dumps keep conditionals as text such as
/// <c>&lt;If(PlayerParameter(4))&gt;大姐&lt;Else/&gt;小哥&lt;/If&gt;</c>.
/// </summary>
public class ConditionalTextTests
{
    [Fact]
    public void HasConditional_FindsIfAndSwitch()
    {
        Assert.True(ConditionalText.HasConditional("a<If(X)>b<Else/>c</If>"));
        Assert.True(ConditionalText.HasConditional("a<Switch(X)><Case(1)>b</Case></Switch>"));
        Assert.False(ConditionalText.HasConditional("plain text<br>"));
    }

    [Fact]
    public void Resolve_PicksTrueBranch_WhenConditionIsTrue()
    {
        var text = "<If(PlayerParameter(4))>大姐<Else/>小哥</If>，你好。";

        Assert.Equal("大姐，你好。", ConditionalText.Resolve(text, _ => true));
    }

    [Fact]
    public void Resolve_PicksFalseBranch_WhenConditionIsFalse()
    {
        var text = "<If(PlayerParameter(4))>大姐<Else/>小哥</If>，你好。";

        Assert.Equal("小哥，你好。", ConditionalText.Resolve(text, _ => false));
    }

    [Fact]
    public void Resolve_PicksTrueBranch_WhenConditionIsUnknown()
    {
        Assert.Equal("A", ConditionalText.Resolve("<If(SomethingElse)>A<Else/>B</If>", _ => null));
    }

    [Fact]
    public void Resolve_HandlesNestedConditionals()
    {
        var text = "<If(A)>a<If(B)>b<Else/>B</If><Else/>x</If>";

        Assert.Equal("aB", ConditionalText.Resolve(text, condition => condition == "A"));
        Assert.Equal("x", ConditionalText.Resolve(text, _ => false));
    }

    [Fact]
    public void Resolve_PicksFirstCase_ForSwitch()
    {
        var text = "<Switch(X)><Case(1)>一</Case><Case(2)>二</Case></Switch>";

        Assert.Equal("一", ConditionalText.Resolve(text, _ => null));
    }

    [Fact]
    public void Resolve_KeepsOtherTags_ForTheNormalizerToStrip()
    {
        var text = "<If(A)><Highlight>你好</Highlight><Else/>再见</If>";

        Assert.Equal("<Highlight>你好</Highlight>", ConditionalText.Resolve(text, _ => true));
    }
}
