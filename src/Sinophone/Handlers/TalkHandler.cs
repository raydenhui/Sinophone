namespace Sinophone.Handlers;

/// <summary>
/// Watches the <c>Talk</c> and <c>TalkSubtitle</c> addons, looks up the Chinese
/// translation for the English text in their AtkValues, and feeds it to the overlay.
/// </summary>
public sealed class TalkHandler : IDisposable
{
    private const string TalkAddonName = "Talk";
    private const string TalkSubtitleAddonName = "TalkSubtitle";

    private const uint TextValueIndex = 0;
    private const uint NameValueIndex = 1;

    private readonly IAddonLifecycle _addonLifecycle;
    private readonly IPluginLog _log;
    private readonly Configuration _configuration;
    private readonly Func<TranslationStore?> _getStore;
    private readonly OverlayWindow _overlay;

    private readonly IAddonLifecycle.AddonEventDelegate _onAddonEvent;
    private bool _disposed;

    public TalkHandler(
        IAddonLifecycle addonLifecycle,
        IPluginLog log,
        Configuration configuration,
        Func<TranslationStore?> getStore,
        OverlayWindow overlay)
    {
        _addonLifecycle = addonLifecycle;
        _log = log;
        _configuration = configuration;
        _getStore = getStore;
        _overlay = overlay;
        _onAddonEvent = OnAddonEvent;

        Register(TalkAddonName);
        Register(TalkSubtitleAddonName);
    }

    private void Register(string addonName)
    {
        _addonLifecycle.RegisterListener(AddonEvent.PreSetup, addonName, _onAddonEvent);
        _addonLifecycle.RegisterListener(AddonEvent.PreRefresh, addonName, _onAddonEvent);
        _addonLifecycle.RegisterListener(AddonEvent.PostClose, addonName, _onAddonEvent);
    }

    private void Unregister(string addonName)
    {
        _addonLifecycle.UnregisterListener(AddonEvent.PreSetup, addonName, _onAddonEvent);
        _addonLifecycle.UnregisterListener(AddonEvent.PreRefresh, addonName, _onAddonEvent);
        _addonLifecycle.UnregisterListener(AddonEvent.PostClose, addonName, _onAddonEvent);
    }

    private unsafe void OnAddonEvent(AddonEvent eventType, AddonArgs args)
    {
        if (_disposed)
            return;

        var addonName = args.AddonName;

        if (eventType == AddonEvent.PostClose)
        {
            _overlay.Hide();
            return;
        }

        if (!_configuration.Enabled)
        {
            _overlay.Hide();
            return;
        }

        var isSubtitle = string.Equals(addonName, TalkSubtitleAddonName, StringComparison.Ordinal);

        if (isSubtitle && !_configuration.ShowInTalkSubtitle)
            return;

        if (!isSubtitle && !_configuration.ShowInTalk)
            return;

        var store = _getStore();
        if (store is null)
            return;

        if (!TryGetAtkValues(args, out var values, out var count))
            return;

        var englishText = ReadStringValue(values, TextValueIndex, count);
        var englishName = _configuration.ShowSpeakerName
            ? ReadStringValue(values, NameValueIndex, count)
            : null;

        var content = Resolve(store, _configuration, englishText, englishName);

        if (content is null)
            _overlay.Hide();
        else
            _overlay.SetContent(content.Value.ChineseText, englishText!, content.Value.ChineseName,
                englishName ?? string.Empty, isSubtitle);
    }

    /// <summary>
    /// Decides what the overlay should show for one dialogue line. Pure apart from
    /// the store lookup, so tests can call it without building addon args.
    /// Returns null when nothing should be shown.
    /// </summary>
    internal static OverlayContent? Resolve(
        TranslationStore store,
        Configuration configuration,
        string? englishText,
        string? englishName)
    {
        if (string.IsNullOrWhiteSpace(englishText))
            return null;

        var hasText = store.TryGetDialogue(englishText, out var chineseText);

        if (!hasText && configuration.HideWhenNoTranslation)
            return null;

        var chineseName = string.Empty;
        if (configuration.ShowSpeakerName && !string.IsNullOrWhiteSpace(englishName))
            store.TryGetName(englishName, out chineseName);

        return new OverlayContent(chineseText ?? string.Empty, chineseName ?? string.Empty);
    }

    internal readonly record struct OverlayContent(string ChineseText, string ChineseName);

    private static unsafe bool TryGetAtkValues(AddonArgs args, out AtkValue* values, out uint count)
    {
        switch (args)
        {
            case AddonSetupArgs setup when setup.AtkValues != IntPtr.Zero && setup.AtkValueCount > 0:
                values = (AtkValue*)setup.AtkValues;
                count = setup.AtkValueCount;
                return true;

            case AddonRefreshArgs refresh when refresh.AtkValues != IntPtr.Zero && refresh.AtkValueCount > 0:
                values = (AtkValue*)refresh.AtkValues;
                count = refresh.AtkValueCount;
                return true;

            default:
                values = null;
                count = 0;
                return false;
        }
    }

    private unsafe string? ReadStringValue(AtkValue* values, uint index, uint count)
    {
        if (values is null || index >= count)
            return null;

        try
        {
            ref var value = ref values[index];

            if (value.Type != AtkValueType.String || !value.String.HasValue)
                return null;

            var text = MemoryHelper.ReadSeStringAsString(
                out _,
                (nint)value.String.Value,
                8192,
                true,
                string.Empty);

            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "[Sinophone] Failed to read AtkValue {Index}.", index);
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Unregister(TalkAddonName);
        Unregister(TalkSubtitleAddonName);
        _overlay.Hide();
    }
}
