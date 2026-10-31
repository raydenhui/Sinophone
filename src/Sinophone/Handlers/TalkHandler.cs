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
    private readonly TalkEventReader _eventReader;
    private readonly ITargetManager _targetManager;

    private readonly IAddonLifecycle.AddonEventDelegate _onAddonEvent;
    private bool _disposed;

    public TalkHandler(
        IAddonLifecycle addonLifecycle,
        IPluginLog log,
        ITargetManager targetManager,
        Configuration configuration,
        Func<TranslationStore?> getStore,
        OverlayWindow overlay,
        NativeTextWriter nativeWriter)
    {
        _addonLifecycle = addonLifecycle;
        _log = log;
        _targetManager = targetManager;
        _configuration = configuration;
        _getStore = getStore;
        _overlay = overlay;
        _nativeWriter = nativeWriter;
        _eventReader = new TalkEventReader(log);
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

        // Native mode covers Talk only. Every draw is where the Chinese has to be put
        // back, because the game's text stream replaces it after one frame.
        if (string.Equals(addonName, TalkAddonName, StringComparison.Ordinal))
            _addonLifecycle.RegisterListener(AddonEvent.PreDraw, addonName, _onAddonEvent);
    }

    private void Unregister(string addonName)
    {
        _addonLifecycle.UnregisterListener(AddonEvent.PostSetup, addonName, _onAddonEvent);
        _addonLifecycle.UnregisterListener(AddonEvent.PostRefresh, addonName, _onAddonEvent);
        _addonLifecycle.UnregisterListener(AddonEvent.PostClose, addonName, _onAddonEvent);
        _addonLifecycle.UnregisterListener(AddonEvent.PostHide, addonName, _onAddonEvent);
        _addonLifecycle.UnregisterListener(AddonEvent.PreFinalize, addonName, _onAddonEvent);

        if (string.Equals(addonName, TalkAddonName, StringComparison.Ordinal))
            _addonLifecycle.UnregisterListener(AddonEvent.PreDraw, addonName, _onAddonEvent);
    }

    private unsafe void OnAddonEvent(AddonEvent eventType, AddonArgs args)
    {
        if (_disposed)
            return;

        var addonName = args.AddonName;

        if (eventType == AddonEvent.PreDraw)
        {
            ReapplyNativeText(addonName);
            return;
        }

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
            _nativeWriter.RestoreAll();
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
            ForgetNativeText(addonName);
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
        var integerParameters = ReadIntegerParameters(values, count);
        if (integerParameters.Count > 0)
            _log.Debug("{Addon} integer parameters: {Parameters}.", addonName, string.Join(", ", integerParameters));

        var englishText = ReadStringValue(values, TextValueIndex, count);
        var englishName = _configuration.ShowSpeakerName
            ? ReadStringValue(values, NameValueIndex, count)
            : null;

        _log.Debug("{Event} for {Addon}: {TextLength} text chars, name: {HasName}, text: {Text}",
            eventType, addonName, englishText?.Length ?? 0, !string.IsNullOrWhiteSpace(englishName),
            englishText ?? "(null)");

        var context = string.Equals(addonName, TalkAddonName, StringComparison.Ordinal)
            ? _eventReader.ReadContext(englishText, _targetManager.Target?.Address ?? 0)
            : default;

        var content = Resolve(store, _configuration, englishText, englishName, context, integerParameters);

        if (content is null && string.Equals(addonName, TalkAddonName, StringComparison.Ordinal))
        {
            foreach (var candidate in GetTextNodeCandidates(args, englishName))
            {
                _log.Debug("{Addon} AtkValue text missed; retrying with addon text nodes: {Text}",
                    addonName, candidate);
                content = Resolve(
                    store,
                    _configuration,
                    candidate,
                    englishName,
                    _eventReader.ReadContext(candidate),
                    integerParameters);
                if (content is not null)
                {
                    englishText = candidate;
                    break;
                }
            }
        }

        if (content is null)
        {
            ForgetNativeText(addonName);
            _overlay.Hide();
            _log.Debug("{Addon} line has no usable translation; overlay hidden. " +
                       "Text empty: {TextEmpty}, hide when no translation: {HideWhenMissing}.",
                addonName, string.IsNullOrWhiteSpace(englishText), _configuration.HideWhenNoTranslation);
            return;
        }

        if (ShouldUseNativeText(_configuration, addonName))
        {
            if (WriteNativeText(content.Value))
            {
                _overlay.Hide();
                _log.Debug("Native text written for {Addon}; overlay hidden.",
                    addonName);
                return;
            }

            // The write failed, so the overlay is the surface for this line. Keep the
            // previous line's Chinese out of the game's box.
            ForgetNativeText(addonName);
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
    /// True when the Chinese written into the game's dialogue box has to be put back on
    /// each draw. This runs every frame while Talk is visible, so it stays a plain
    /// setting check; the draw-time compare lives in <see cref="NativeTextWriter.Reapply"/>.
    /// </summary>
    internal static bool ShouldReapplyNativeText(Configuration configuration, string addonName)
        => configuration.Enabled && ShouldUseNativeText(configuration, addonName);

    private void ReapplyNativeText(string addonName)
    {
        if (!ShouldReapplyNativeText(_configuration, addonName))
            return;

        // No-ops for a node this writer never changed, so a line on the overlay is
        // left alone.
        _nativeWriter.Reapply(addonName, NativeTextWriter.TalkNameNodeId);
        _nativeWriter.Reapply(addonName, NativeTextWriter.TalkTextNodeId);
    }

    /// <summary>
    /// Stops keeping a line's Chinese on screen. The game's own English line then stays
    /// in place, which is what an untranslated line needs.
    /// </summary>
    private void ForgetNativeText(string addonName)
    {
        if (!string.Equals(addonName, TalkAddonName, StringComparison.Ordinal))
            return;

        _nativeWriter.ClearIfStillOurs(TalkAddonName, NativeTextWriter.TalkNameNodeId);
        _nativeWriter.ClearIfStillOurs(TalkAddonName, NativeTextWriter.TalkTextNodeId);
    }

    /// <summary>
    /// Writes the Chinese line, and the Chinese speaker name when the name is shown, into
    /// the Talk addon's text nodes. Returns true when the dialogue line was written.
    /// </summary>
    private bool WriteNativeText(OverlayContent content)
    {
        if (_configuration.ShowSpeakerName && !string.IsNullOrWhiteSpace(content.ChineseName))
            _nativeWriter.Write(TalkAddonName, NativeTextWriter.TalkNameNodeId, content.ChineseName);
        else
            _nativeWriter.ClearIfStillOurs(TalkAddonName, NativeTextWriter.TalkNameNodeId);

        return _nativeWriter.Write(TalkAddonName, NativeTextWriter.TalkTextNodeId, content.ChineseText, content.Style);
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
        string? englishName,
        DialogueContext context = default,
        IReadOnlyList<int>? integerParameters = null)
    {
        if (string.IsNullOrWhiteSpace(englishText))
            return null;

        var hasText = store.TryGetDialogue(englishText, in context, out var chineseText);

        if (!hasText && configuration.HideWhenNoTranslation)
            return null;

        var chineseName = string.Empty;
        if (configuration.ShowSpeakerName && !string.IsNullOrWhiteSpace(englishName))
            store.TryGetName(englishName, out chineseName);

        var styledText = MacroNormalizer.ResolveIntegerParameters(chineseText, integerParameters);
        store.TryGetStyle(styledText, out var style);

        return new OverlayContent(styledText, chineseName ?? string.Empty, style);
    }

    internal readonly record struct OverlayContent(
        string ChineseText,
        string ChineseName,
        UiTextStyle Style = default);

    private static unsafe List<int> ReadIntegerParameters(AtkValue* values, uint count)
    {
        var parameters = new List<int>();

        for (uint i = 2; i < count; i++)
        {
            ref var value = ref values[i];

            switch (value.Type)
            {
                case AtkValueType.Int:
                    parameters.Add(value.Int);
                    break;
                case AtkValueType.UInt:
                    parameters.Add(unchecked((int)value.UInt));
                    break;
            }
        }

        return parameters;
    }

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

                var text = ExtractPlainText(node->NodeText.AsSpan()).Trim();
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

    /// <summary>
    /// Reads a game string as plain text. The Talk text node holds a SeString, and a
    /// line with italics or bold carries a formatting macro in the middle of it.
    /// Reading the bytes as UTF-8 leaves the macro's payload bytes in the sentence,
    /// so the lookup key never matched. ExtractText keeps the text and drops the
    /// macros.
    /// </summary>
    internal static string ExtractPlainText(ReadOnlySpan<byte> seString)
        => new ReadOnlySeStringSpan(seString).ExtractText();

    private unsafe string? ReadStringValue(AtkValue* values, uint index, uint count)
    {
        if (values is null || index >= count)
            return null;

        try
        {
            ref var value = ref values[index];

            if (!IsStringValue(value.Type) || !value.String.HasValue)
                return null;

            // Stopping at the first formatting macro cut the line at its italics,
            // so an emphasized AtkValue text missed and only the text node retry
            // had a chance. Keep reading and drop the macros instead.
            var text = MemoryHelper.ReadSeStringAsString(
                out _,
                (nint)value.String.Value,
                8192,
                false,
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
