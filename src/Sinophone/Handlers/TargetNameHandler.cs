using Dalamud.Game.ClientState.Objects.Enums;

namespace Sinophone.Handlers;

/// <summary>
/// Watches the <c>TargetInfo</c> addon and feeds the Chinese name of the targeted
/// NPC or object to the target name surface: either the plugin window or the game's
/// own nameplate above the target's head.
/// </summary>
/// <remarks>
/// Addon events are used instead of a per-frame poll. Reading
/// <see cref="ITargetManager.Target"/> builds a game object wrapper, so doing it
/// every frame would allocate for no reason.
/// </remarks>
public sealed class TargetNameHandler : IDisposable
{
    private const string TargetInfoAddonName = "TargetInfo";
    private const string NamePlateAddonName = "NamePlate";

    private readonly IAddonLifecycle _addonLifecycle;
    private readonly ITargetManager _targetManager;
    private readonly Configuration _configuration;
    private readonly Func<TranslationStore?> _getStore;
    private readonly TargetNameWindow _window;
    private readonly NamePlateNameWriter _namePlate;

    private readonly IAddonLifecycle.AddonEventDelegate _onTargetInfoEvent;
    private readonly IAddonLifecycle.AddonEventDelegate _onNamePlateUpdate;
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
        _onNamePlateUpdate = OnNamePlateUpdate;

        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, TargetInfoAddonName, _onTargetInfoEvent);
        _addonLifecycle.RegisterListener(AddonEvent.PostRefresh, TargetInfoAddonName, _onTargetInfoEvent);
        _addonLifecycle.RegisterListener(AddonEvent.PostClose, TargetInfoAddonName, _onTargetInfoEvent);
        _addonLifecycle.RegisterListener(AddonEvent.PostUpdate, NamePlateAddonName, _onNamePlateUpdate);
    }

    /// <summary>Re-reads the current target. Call after a settings or store change.</summary>
    public void Refresh()
    {
        if (_disposed)
            return;

        if (_configuration.Enabled
            && _configuration.ShowTargetName
            && _configuration.TargetNameDisplay == TargetNameDisplayMode.NativeNamePlate)
        {
            RefreshNative();
            return;
        }

        // Every other state uses the overlay, or nothing. Clear the nameplate first,
        // so switching modes does not leave Chinese above the last target's head.
        _namePlate.Clear();

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

    /// <summary>
    /// Resolves the target and writes it into the game's nameplate. The overlay stays
    /// hidden, because the game draws this name itself.
    /// </summary>
    private void RefreshNative()
    {
        _window.Hide();

        var target = _targetManager.Target;
        var content = Resolve(_getStore(), target?.ObjectKind, target?.Name.TextValue);

        if (content is null)
            _namePlate.Clear();
        else
            _namePlate.SetTarget(content.Value.EnglishName, FormatNamePlateText(
                content.Value.ChineseName,
                content.Value.EnglishName,
                _configuration.TargetNameShowEnglish));
    }

    private void OnTargetInfoEvent(AddonEvent eventType, AddonArgs args)
    {
        if (_disposed)
            return;

        if (eventType == AddonEvent.PostClose)
        {
            _window.Hide();
            _namePlate.Clear();
            return;
        }

        Refresh();
    }

    /// <summary>
    /// Runs after the game updated the nameplate addon, which is when the game may have
    /// put the English name back. The writer only reads its own slots here.
    /// </summary>
    private void OnNamePlateUpdate(AddonEvent eventType, AddonArgs args)
    {
        if (_disposed)
            return;

        _namePlate.Maintain();
    }

    /// <summary>
    /// Builds the text written into the nameplate. The Chinese name stays on the first
    /// line; the English name goes on a second line in brackets when it is shown.
    /// </summary>
    internal static string FormatNamePlateText(string chineseName, string englishName, bool showEnglish)
    {
        if (!showEnglish || string.IsNullOrWhiteSpace(englishName))
            return chineseName;

        return $"{chineseName}\n({englishName})";
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
        _addonLifecycle.UnregisterListener(AddonEvent.PostUpdate, NamePlateAddonName, _onNamePlateUpdate);

        _window.Hide();
        _namePlate.Clear();
    }
}
