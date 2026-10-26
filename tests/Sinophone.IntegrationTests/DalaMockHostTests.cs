using DalaMock.Core.Configuration;
using DalaMock.Core.Plugin;
using Xunit;

namespace Sinophone.IntegrationTests;

/// <summary>
/// Boots DalaMock's container to check the plugin's service wiring without the game.
/// </summary>
/// <remarks>
/// DalaMock builds a Lumina <c>GameData</c> at startup and needs the game's sqpack
/// files for that. Without an install these tests skip instead of failing, so CI
/// stays green. Set <c>SINOPHONE_FFXIV_PATH</c> to the folder containing
/// <c>game/</c> to run them locally.
///
/// DalaMock cannot exercise addon events: its MockAddonLifecycle accepts listeners
/// and never invokes them, and Dalamud keeps the addon args constructors internal.
/// That behaviour is covered by <see cref="TalkHandlerResolveTests"/> and by the
/// in-game checklist in AGENTS.md.
/// </remarks>
public class DalaMockHostTests
{
    [SkippableFact]
    public void Container_ProvidesTheServicesThePluginUses()
    {
        var container = CreateContainer();
        var services = container.GetMockServices().Select(s => s.ServiceName).ToList();

        // The services Plugin, TalkHandler and FontManager pull in.
        Assert.Contains("Data Manager", services);
        Assert.Contains("Addon Lifecycle", services);
        Assert.Contains("Plugin Log", services);
    }

    [SkippableFact]
    public void Container_StartsWithNoWindow()
    {
        var container = CreateContainer();

        Assert.Empty(container.GetWindows());
    }

    [SkippableFact]
    public void PluginLoader_CanRegisterThePluginType()
    {
        var container = CreateContainer();
        var loader = container.GetPluginLoader();

        var plugin = loader.AddPlugin(typeof(Sinophone.Plugin));

        Assert.NotNull(plugin);
        Assert.True(loader.HasPluginsLoaded);
    }

    /// <summary>Finds the FFXIV install folder, or skips the test when there is none.</summary>
    private static string RequireGamePath()
    {
        var configured = Environment.GetEnvironmentVariable("SINOPHONE_FFXIV_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
            return configured;

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrEmpty(appData))
        {
            var standard = Path.Combine(appData, "XIVLauncher", "game");
            if (Directory.Exists(standard))
                return standard;
        }

        Skip.If(true,
            "No FFXIV install found. Set SINOPHONE_FFXIV_PATH to the folder containing game/ to run this test.");
        return string.Empty;
    }

    private static MockContainer CreateContainer()
    {
        var gamePath = RequireGamePath();

        // CreateWindow = false keeps this headless: no ImGui window, no textures,
        // no key state. askPath = false stops DalaMock opening a native folder picker.
        return new MockContainer(
            new MockDalamudConfiguration
            {
                CreateWindow = false,
                GamePath = new DirectoryInfo(gamePath),
            },
            askPath: false);
    }
}
