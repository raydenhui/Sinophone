using Dalamud.Game.ClientState.Objects.Enums;

namespace Sinophone.Handlers;

/// <summary>
/// Watches the <c>TargetInfo</c> addon and feeds the Chinese name of the targeted
/// NPC or object to the target name window. In native mode it turns on the nameplate
/// writer, which translates every visible NPC name instead.
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
    private readonly NamePlateNameWriter _namePlate;

    private readonly IAddonLifecycle.AddonEventDelegate _onTargetInfoEvent;
    private bool _disposed;

    public TargetNameHandler(
        IAddonLifecycle addonLifecycle,
        ITargetManager targetManager,
        Configuration configuration,
        Func<TranslationStore?> getStore,
        TargetNameWindow window,
        NamePlateNameWriter namePlate)
    {
        _addonLifecycle = addonLifecycle;
        _targetManager = targetManager;
        _configuration = configuration;
        _getStore = getStore;
        _window = window;
        _namePlate = namePlate;
        _onTargetInfoEvent = OnTargetInfoEvent;

        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, TargetInfoAddonName, _onTargetInfoEvent);
        _addonLifecycle.RegisterListener(AddonEvent.PostRefresh, TargetInfoAddonName, _onTargetInfoEvent);
        _addonLifecycle.RegisterListener(AddonEvent.PostClose, TargetInfoAddonName, _onTargetInfoEvent);
    }

    /// <summary>
    /// Re-reads the current target and the settings. Pass <paramref name="namesChanged"/>
    /// when the translation data changed: the plates then need a new lookup.
    /// </summary>
    public void Refresh(bool namesChanged = false)
    {
        if (_disposed)
            return;

        if (namesChanged)
            _namePlate.Reset();

        if (_configuration.Enabled
            && _configuration.ShowTargetName
            && _configuration.TargetNameDisplay == TargetNameDisplayMode.NativeNamePlate)
        {
            // The overlay stays hidden, because the game draws these names itself.
            _window.Hide();
            _namePlate.Enable();
            return;
        }

        // Every other state uses the overlay, or nothing. Restore the plates first,
        // so switching modes does not leave Chinese above the NPCs' heads.
        _namePlate.Disable();

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

    private void OnTargetInfoEvent(AddonEvent eventType, AddonArgs args)
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
        if (objectKind == ObjectKind.Pc)
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

        _addonLifecycle.UnregisterListener(AddonEvent.PostSetup, TargetInfoAddonName, _onTargetInfoEvent);
        _addonLifecycle.UnregisterListener(AddonEvent.PostRefresh, TargetInfoAddonName, _onTargetInfoEvent);
        _addonLifecycle.UnregisterListener(AddonEvent.PostClose, TargetInfoAddonName, _onTargetInfoEvent);

        _window.Hide();
        _namePlate.Disable();
    }
}
