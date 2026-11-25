using Sinophone.Diagnostics;

namespace Sinophone.Tests;

/// <summary>
/// Guards the split between the harness core and the testing tab's engine code.
/// <para>
/// CI builds Release, so this normally runs without <c>SINOPHONE_TESTING</c>. The tests check the
/// two halves agree with the build switch, so they hold in a Release run and in a Debug run:
/// the core is always present, and the engine code is present exactly when the switch is set. A
/// future change that moves a game type into the core, or drops the switch, fails here instead of
/// showing up as a released DLL that still holds developer code.
/// </para>
/// </summary>
public class HarnessBuildTests
{
    private static System.Reflection.Assembly Assembly => typeof(Configuration).Assembly;

    [Fact]
    public void HarnessCore_IsAlwaysPresent()
    {
        Assert.NotNull(Assembly.GetType("Sinophone.Diagnostics.HarnessCase"));
        Assert.NotNull(Assembly.GetType("Sinophone.Diagnostics.HarnessRun"));
        Assert.NotNull(Assembly.GetType("Sinophone.Diagnostics.HarnessSession"));
        Assert.NotNull(Assembly.GetType("Sinophone.Diagnostics.HarnessAssertions"));
        Assert.NotNull(Assembly.GetType("Sinophone.UI.HarnessTabState"));
    }

    [Theory]
    [InlineData("Sinophone.Diagnostics.NativeTextProbe")]
    [InlineData("Sinophone.Diagnostics.TalkContextProbe")]
    [InlineData("Sinophone.Diagnostics.ChatBubbleHarness")]
    [InlineData("Sinophone.Diagnostics.GlyphProbe")]
    [InlineData("Sinophone.Diagnostics.HarnessHost")]
    public void EngineCode_IsPresent_ExactlyWhenTheSwitchIsSet(string typeName)
        => Assert.Equal(HarnessBuild.IsTesting, Assembly.GetType(typeName) is not null);

    [Fact]
    public void TestingTabConstructor_Exists_ExactlyWhenTheSwitchIsSet()
    {
        var hasHostConstructor = typeof(UI.ConfigWindow).GetConstructors()
            .Any(static constructor => constructor.GetParameters()
                .Any(static parameter => parameter.ParameterType.Name == "HarnessHost"));

        Assert.Equal(HarnessBuild.IsTesting, hasHostConstructor);
    }

    [Fact]
    public void ReleaseBuild_IsTheConfigurationCIUses()
    {
        // CI runs Release. This test documents the assumption; the others hold either way.
        var configuration = Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyConfigurationAttribute), false)
            .OfType<System.Reflection.AssemblyConfigurationAttribute>()
            .FirstOrDefault();

        // A Debug test run is allowed, so only fail when the attribute claims Release but the
        // switch says testing is on.
        if (configuration is not null && configuration.Configuration == "Release")
            Assert.False(HarnessBuild.IsTesting);
    }
}
