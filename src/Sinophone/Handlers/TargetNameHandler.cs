using Dalamud.Game.ClientState.Objects.Enums;

namespace Sinophone.Handlers;

/// <summary>
/// Watches the <c>TargetInfo</c> addon and feeds the Chinese name of the targeted
/// NPC or object to the target name window.
/// </summary>
/// <remarks>
/// Addon events are used instead of a per-frame poll. Reading
/// <see cref="ITargetManager.Target"/> builds a game object wrapper, so doing it
/// every frame would allocate for no reason.
/// </remarks>
public sealed class TargetNameHandler : IDisposable
{
    private const string TargetInfoAddonName = "TargetInfo";

    private readonly IAddonLifecycle _addonLifecycle;
    private readonly ITargetManager _targetManager;
    private readonly Configuration _configuration;
    private readonly Func<TranslationStore?> _getStore;
    private readonly TargetNameWindow _window;

    private readonly IAddonLifecycle.AddonEventDelegate _onAddonEvent;
    private bool _disposed;

    public TargetNameHandler(
        IAddonLifecycle addonLifecycle,
        ITargetManager targetManager,
        Configuration configuration,
        Func<TranslationStore?> getStore,
        TargetNameWindow window)
    {
        _addonLifecycle = addonLifecycle;
        _targetManager = targetManager;
        _configuration = configuration;
        _getStore = getStore;
        _window = window;
        _onAddonEvent = OnAddonEvent;

        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, TargetInfoAddonName, _onAddonEvent);
        _addonLifecycle.RegisterListener(AddonEvent.PostRefresh, TargetInfoAddonName, _onAddonEvent);
        _addonLifecycle.RegisterListener(AddonEvent.PostClose, TargetInfoAddonName, _onAddonEvent);
    }

    /// <summary>Re-reads the current target. Call after a settings or store change.</summary>
    public void Refresh()
    {
        if (_disposed)
            return;

        if (!_configuration.Enabled || !_configuration.ShowTargetName)
        {
            _window.Hide();
            return;
        }

        var target = _targetManager.Target;
        var content = Resolve(_getStore(), target?.ObjectKind, target?.Name.TextValue);

        if (content is null)
            _window.Hide();
        else
            _window.SetName(content.Value.ChineseName, content.Value.EnglishName);
    }

    private void OnAddonEvent(AddonEvent eventType, AddonArgs args)
    {
        if (_disposed)
            return;

        if (eventType == AddonEvent.PostClose)
        {
            _window.Hide();
            return;
        }

        Refresh();
    }

    /// <summary>
    /// Decides what to show for one target. Pure apart from the store lookup, so
    /// tests can call it without a game object. Returns null when nothing should
    /// be shown.
    /// </summary>
    internal static TargetNameContent? Resolve(
        TranslationStore? store,
        ObjectKind? objectKind,
        string? englishName)
    {
        if (store is null || objectKind is null)
            return null;

        // Player names are chosen by the player and are never translated.
        if (objectKind == ObjectKind.Player)
            return null;

        if (string.IsNullOrWhiteSpace(englishName))
            return null;

        if (!store.TryGetName(englishName, out var chineseName))
            return null;

        return new TargetNameContent(chineseName, englishName);
    }

    internal readonly record struct TargetNameContent(string ChineseName, string EnglishName);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _addonLifecycle.UnregisterListener(AddonEvent.PostSetup, TargetInfoAddonName, _onAddonEvent);
        _addonLifecycle.UnregisterListener(AddonEvent.PostRefresh, TargetInfoAddonName, _onAddonEvent);
        _addonLifecycle.UnregisterListener(AddonEvent.PostClose, TargetInfoAddonName, _onAddonEvent);

        _window.Hide();
    }
}
