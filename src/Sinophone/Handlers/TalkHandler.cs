using FFXIVClientStructs.FFXIV.Client.UI;

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
    private readonly NativeTextWriter _nativeWriter;

    private readonly IAddonLifecycle.AddonEventDelegate _onAddonEvent;
    private bool _disposed;

    public TalkHandler(
        IAddonLifecycle addonLifecycle,
        IPluginLog log,
        Configuration configuration,
        Func<TranslationStore?> getStore,
        OverlayWindow overlay,
        NativeTextWriter nativeWriter)
    {
        _addonLifecycle = addonLifecycle;
        _log = log;
        _configuration = configuration;
        _getStore = getStore;
        _overlay = overlay;
        _nativeWriter = nativeWriter;
        _onAddonEvent = OnAddonEvent;

        Register(TalkAddonName);
        Register(TalkSubtitleAddonName);
    }

    private void Register(string addonName)
    {
        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, addonName, _onAddonEvent);
        _addonLifecycle.RegisterListener(AddonEvent.PostRefresh, addonName, _onAddonEvent);
        _addonLifecycle.RegisterListener(AddonEvent.PostClose, addonName, _onAddonEvent);
        _addonLifecycle.RegisterListener(AddonEvent.PostHide, addonName, _onAddonEvent);
        _addonLifecycle.RegisterListener(AddonEvent.PreFinalize, addonName, _onAddonEvent);
    }

    private void Unregister(string addonName)
    {
        _addonLifecycle.UnregisterListener(AddonEvent.PostSetup, addonName, _onAddonEvent);
        _addonLifecycle.UnregisterListener(AddonEvent.PostRefresh, addonName, _onAddonEvent);
        _addonLifecycle.UnregisterListener(AddonEvent.PostClose, addonName, _onAddonEvent);
        _addonLifecycle.UnregisterListener(AddonEvent.PostHide, addonName, _onAddonEvent);
        _addonLifecycle.UnregisterListener(AddonEvent.PreFinalize, addonName, _onAddonEvent);
    }

    private unsafe void OnAddonEvent(AddonEvent eventType, AddonArgs args)
    {
        if (_disposed)
            return;

        var addonName = args.AddonName;

        if (ShouldHideOverlay(eventType))
        {
            _overlay.Hide();
            _log.Debug("{Addon} {Event}; overlay hidden.", addonName, eventType);
            return;
        }

        _log.Debug("{Event} fired for {Addon} ({ArgType}).",
            eventType, addonName, args.GetType().Name);

        if (!_configuration.Enabled)
        {
            _overlay.Hide();
            _log.Debug("Plugin disabled in settings; overlay hidden.");
            return;
        }

        var isSubtitle = string.Equals(addonName, TalkSubtitleAddonName, StringComparison.Ordinal);

        if (isSubtitle && !_configuration.ShowInTalkSubtitle)
        {
            _log.Debug("{Addon} overlay disabled in settings.", addonName);
            return;
        }

        if (!isSubtitle && !_configuration.ShowInTalk)
        {
            _log.Debug("{Addon} overlay disabled in settings.", addonName);
            return;
        }

        var store = _getStore();
        if (store is null)
        {
            _log.Debug("No translation data loaded; overlay not shown.");
            return;
        }

        if (!TryGetAtkValues(args, out var values, out var count))
        {
            _log.Debug("{Event} for {Addon} carried no usable AtkValues ({ArgType}).",
                eventType, addonName, args.GetType().Name);
            return;
        }

        _log.Debug("{Addon} supplied {Count} AtkValues.", addonName, count);
        LogAtkValues(addonName, values, count);

        var englishText = ReadStringValue(values, TextValueIndex, count);
        var englishName = _configuration.ShowSpeakerName
            ? ReadStringValue(values, NameValueIndex, count)
            : null;

        _log.Debug("{Event} for {Addon}: {TextLength} text chars, name: {HasName}, text: {Text}",
            eventType, addonName, englishText?.Length ?? 0, !string.IsNullOrWhiteSpace(englishName),
            englishText ?? "(null)");

        var content = Resolve(store, _configuration, englishText, englishName);

        if (content is null && string.Equals(addonName, TalkAddonName, StringComparison.Ordinal))
        {
            foreach (var candidate in GetTextNodeCandidates(args, englishName))
            {
                _log.Debug("{Addon} AtkValue text missed; retrying with addon text nodes: {Text}",
                    addonName, candidate);
                content = Resolve(store, _configuration, candidate, englishName);
                if (content is not null)
                {
                    englishText = candidate;
                    break;
                }
            }
        }

        if (content is null)
        {
            _overlay.Hide();
            _log.Debug("{Addon} line has no usable translation; overlay hidden. " +
                       "Text empty: {TextEmpty}, hide when no translation: {HideWhenMissing}.",
                addonName, string.IsNullOrWhiteSpace(englishText), _configuration.HideWhenNoTranslation);
            return;
        }

        if (ShouldUseNativeText(_configuration, addonName) && WriteNativeText(content.Value))
        {
            _overlay.Hide();
            _log.Debug("Native text written for {Addon}; overlay hidden.",
                addonName);
            return;
        }

        _overlay.SetContent(content.Value.ChineseText, englishText!, content.Value.ChineseName,
            englishName ?? string.Empty, isSubtitle);
        _log.Debug("Overlay shown for {Addon}: {ChineseLength} Chinese chars.",
            addonName, content.Value.ChineseText.Length);
    }

    /// <summary>
    /// True when the translation should go into the game's own dialogue box. Native mode
    /// covers the <c>Talk</c> addon only; subtitles keep the overlay until their nodes are
    /// checked in game. A failed write falls back to the overlay.
    /// </summary>
    internal static bool ShouldUseNativeText(Configuration configuration, string addonName)
        => configuration.NativeTextMode
           && string.Equals(addonName, TalkAddonName, StringComparison.Ordinal);

    /// <summary>
    /// Writes the Chinese line, and the Chinese speaker name when the name is shown, into
    /// the Talk addon's text nodes. Returns true when the dialogue line was written.
    /// </summary>
    private bool WriteNativeText(OverlayContent content)
    {
        if (_configuration.ShowSpeakerName && !string.IsNullOrWhiteSpace(content.ChineseName))
            _nativeWriter.Write(TalkAddonName, NativeTextWriter.TalkNameNodeId, content.ChineseName);

        return _nativeWriter.Write(TalkAddonName, NativeTextWriter.TalkTextNodeId, content.ChineseText);
    }

    /// <summary>Puts the game's own text back into every node native mode changed.</summary>
    public void RestoreNativeText() => _nativeWriter.RestoreAll();

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

    /// <summary>
    /// Diagnostic dump of every AtkValue in the event. Shows which index holds the
    /// dialogue text and what the game actually passed.
    /// </summary>
    private unsafe void LogAtkValues(string addonName, AtkValue* values, uint count)
    {
        for (uint i = 0; i < count; i++)
        {
            ref var value = ref values[i];

            if (!IsStringValue(value.Type))
            {
                _log.Debug("  {Addon} AtkValue[{Index}] type {Type}.", addonName, i, value.Type);
                continue;
            }

            var text = ReadStringValue(values, i, count);
            if (text is null)
            {
                _log.Debug("  {Addon} AtkValue[{Index}] string, unreadable.", addonName, i);
                continue;
            }

            var preview = text.Length > 120 ? string.Concat(text.AsSpan(0, 120), "...") : text;
            _log.Debug("  {Addon} AtkValue[{Index}] string, {Length} chars: {Text}",
                addonName, i, text.Length, preview);
        }
    }

    /// <summary>
    /// True for the events that mean the addon is gone. A conversation usually
    /// ends by hiding the Talk addon, not by closing it, so listening for
    /// <see cref="AddonEvent.PostClose"/> alone left the overlay on screen.
    /// </summary>
    internal static bool ShouldHideOverlay(AddonEvent eventType)
        => eventType is AddonEvent.PostClose or AddonEvent.PostHide or AddonEvent.PreFinalize;

    /// <summary>
    /// True for both string kinds. The game sends dialogue as ManagedString
    /// (Managed | String); the plain String kind is a caller-owned cstring. Both
    /// store the pointer in the same union field, so one read path serves both.
    /// </summary>
    internal static bool IsStringValue(AtkValueType type)
        => type == AtkValueType.String || type == AtkValueType.ManagedString;

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

    /// <summary>
    /// Reads the addon's text nodes and joins the dialogue segments. A line
    /// with oblique words is split across several text nodes while
    /// <c>AtkValue[0]</c> carries only the first segment. Returns candidates in
    /// preference order: segments without the speaker name first, then every
    /// segment in case the name is part of the sentence.
    /// </summary>
    private unsafe List<string> GetTextNodeCandidates(AddonArgs args, string? englishName)
    {
        var candidates = new List<string>(2);

        try
        {
            var addon = args.GetAddonPointer<AtkUnitBase>();
            _log.Debug("Reading text nodes for {Addon}: addon found: {Found}.",
                args.AddonName, addon is not null);
            if (addon is null)
                return candidates;

            var talkAddon = (AddonTalk*)addon;
            var textNodes = new[]
            {
                talkAddon->AtkTextNode220,
                talkAddon->AtkTextNode228,
                talkAddon->AtkTextNode238,
                talkAddon->AtkTextNode240,
                talkAddon->AtkTextNode248,
            };

            var segments = new List<string>(textNodes.Length);
            foreach (var node in textNodes)
            {
                if (node is null)
                {
                    _log.Debug("  {Addon} text node: null.", args.AddonName);
                    continue;
                }

                var text = node->NodeText.ToString().Trim();
                _log.Debug("  {Addon} text node {NodeId}: {Length} chars: {Text}",
                    args.AddonName, node->NodeId, text.Length, text);
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                segments.Add(text);
            }

            if (segments.Count == 0)
                return candidates;

            var withoutName = string.Join(" ", segments.Where(s => !IsName(s, englishName)));
            if (!string.IsNullOrWhiteSpace(withoutName))
                candidates.Add(withoutName);

            var withName = string.Join(" ", segments);
            if (!string.IsNullOrWhiteSpace(withName) && withName != withoutName)
                candidates.Add(withName);
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "Failed to read text nodes from {Addon}.", args.AddonName);
        }

        return candidates;
    }

    private static bool IsName(string text, string? englishName)
        => !string.IsNullOrWhiteSpace(englishName)
           && string.Equals(text.Trim(), englishName.Trim(), StringComparison.OrdinalIgnoreCase);

    private unsafe string? ReadStringValue(AtkValue* values, uint index, uint count)
    {
        if (values is null || index >= count)
            return null;

        try
        {
            ref var value = ref values[index];

            if (!IsStringValue(value.Type) || !value.String.HasValue)
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
            _log.Debug(ex, "Failed to read AtkValue {Index}.", index);
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
        _nativeWriter.RestoreAll();
        _overlay.Hide();
    }
}
