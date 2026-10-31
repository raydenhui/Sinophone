#if DEBUG
namespace Sinophone.Diagnostics;

/// <summary>
/// Debug-only probe for issue #56. Logs the event ids, scene data and Lua text keys
/// active while a Talk addon refreshes, so the id lookup can be checked against the
/// real game before it is trusted for a surface.
/// </summary>
/// <remarks>
/// Everything that touches this class is behind <c>#if DEBUG</c>, so a release build
/// contains no probe and no probe tab. Nothing is written or saved; the probe only
/// reads state that Dalamud and FFXIVClientStructs already expose.
/// </remarks>
public sealed class TalkContextProbe : IDisposable
{
    private static readonly string[] Addons = ["Talk", "TalkSubtitle"];

    private readonly IAddonLifecycle _addonLifecycle;
    private readonly ITargetManager _targetManager;
    private readonly IPluginLog _log;
    private readonly TalkEventReader _reader = null!;
    private readonly IAddonLifecycle.AddonEventDelegate _onAddonEvent;
    private bool _disposed;

    public TalkContextProbe(IAddonLifecycle addonLifecycle, ITargetManager targetManager, IPluginLog log)
    {
        _addonLifecycle = addonLifecycle;
        _targetManager = targetManager;
        _log = log;
        _reader = new TalkEventReader(log);
        _onAddonEvent = OnAddonEvent;

        foreach (var addon in Addons)
        {
            _addonLifecycle.RegisterListener(AddonEvent.PostSetup, addon, _onAddonEvent);
            _addonLifecycle.RegisterListener(AddonEvent.PostRefresh, addon, _onAddonEvent);
        }
    }

    /// <summary>When false, the listener stays registered but does no work.</summary>
    public bool Enabled { get; set; }

    /// <summary>Short result of the last probe, shown in the debug tab.</summary>
    public string LastResult { get; private set; } = "No probe yet.";

    /// <summary>Reads and logs the current event state once.</summary>
    public void LogCurrent()
    {
        var target = _targetManager.Target;
        _log.Info("Talk probe target: {Name} ({ObjectId}), address {Address}.",
            target?.Name.ToString() ?? "(none)",
            target?.GameObjectId ?? 0,
            target?.Address ?? 0);

        var snapshot = _reader.ReadSnapshot();
        if (snapshot is null)
        {
            LastResult = "EventFramework is not available.";
            _log.Info("Talk probe: {Result}", LastResult);
            return;
        }

        var sceneData = string.Join(",", snapshot.SceneData.Take(16));
        _log.Info(
            "Talk probe: scene {Scene}, object {SceneObjectId}, scene data [{SceneData}] ({SceneDataCount}), " +
            "event state 1 {State1}, event state 2 {State2}, all handlers {HandlerCount}, matched {MatchedCount}.",
            snapshot.Scene,
            snapshot.SceneGameObjectId,
            sceneData,
            snapshot.SceneData.Count,
            Describe(snapshot.EventState1),
            Describe(snapshot.EventState2),
            snapshot.HandlerCount,
            snapshot.Handlers.Count);

        foreach (var handler in snapshot.Handlers.Take(8))
        {
            _log.Info(
                "Talk probe handler: event {EventId}, content {ContentId}, entry {EntryId}, scene {Scene}, lua texts {LuaCount}.",
                handler.EventId,
                handler.ContentId,
                handler.EntryId,
                handler.Scene,
                handler.LuaTexts?.Count ?? 0);

            if (handler.LuaTexts is null)
                continue;

            foreach (var (key, value) in handler.LuaTexts.Take(12))
                _log.Info("Talk probe lua text: {Key} = {Value}", key, value);
        }

        LastResult = $"Event state 1 {Describe(snapshot.EventState1)}; {snapshot.Handlers.Count} matching handler(s). See the plugin log.";
    }

    /// <summary>
    /// Dumps the Talk addon's own strings and Param. If the game stores the row or
    /// text key on the addon, this is where the id path gets it without guessing
    /// which event handler is active.
    /// </summary>
    private unsafe void LogAddon(AddonArgs args)
    {
        try
        {
            var addon = args.GetAddonPointer<AtkUnitBase>();
            if (addon is null)
                return;

            var talk = (AddonTalk*)addon;
            _log.Info("Talk probe addon: param {Param}, strings: {Strings}.",
                talk->Param,
                string.Join(" | ", new[]
                {
                    DescribeString(talk->String268),
                    DescribeString(talk->String2D0),
                    DescribeString(talk->String338),
                    DescribeString(talk->String408),
                    DescribeString(talk->String470),
                    DescribeString(talk->String4D8),
                    DescribeString(talk->String540),
                }));
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "Talk probe addon read failed.");
        }
    }

    private static string DescribeString(FFXIVClientStructs.FFXIV.Client.System.String.Utf8String value)
    {
        var text = value.ToString().Trim();
        return text.Length == 0 ? "(empty)" : $"[{text.Length}] {text[..Math.Min(text.Length, 80)]}";
    }

    private static string Describe(TalkEventState state)
        => state.EventId == 0
            ? "none"
            : $"event {state.EventId}, content {state.ContentId}, entry {state.EntryId}, object {state.ObjectId}, current {state.CurrentEvent}, param {state.CurrentEventParam}";

    private void OnAddonEvent(AddonEvent eventType, AddonArgs args)
    {
        if (!Enabled || _disposed)
            return;

        _log.Debug("Talk probe triggered by {Addon} {Event}.", args.AddonName, eventType);
        LogAtkValues(args);
        LogAddon(args);
        LogCurrent();
    }

    private unsafe void LogAtkValues(AddonArgs args)
    {
        AtkValue* values;
        uint count;

        switch (args)
        {
            case AddonSetupArgs setup when setup.AtkValues != IntPtr.Zero && setup.AtkValueCount > 0:
                values = (AtkValue*)setup.AtkValues;
                count = setup.AtkValueCount;
                break;

            case AddonRefreshArgs refresh when refresh.AtkValues != IntPtr.Zero && refresh.AtkValueCount > 0:
                values = (AtkValue*)refresh.AtkValues;
                count = refresh.AtkValueCount;
                break;

            default:
                return;
        }

        var builder = new StringBuilder();

        for (uint i = 0; i < count; i++)
        {
            ref var value = ref values[i];

            if (i > 0)
                builder.Append(", ");

            switch (value.Type)
            {
                case AtkValueType.Int:
                    builder.Append(i).Append(':').Append(value.Int);
                    break;
                case AtkValueType.UInt:
                    builder.Append(i).Append(':').Append(value.UInt);
                    break;
                case AtkValueType.Bool:
                    builder.Append(i).Append(':').Append(value.Bool);
                    break;
                case AtkValueType.Float:
                    builder.Append(i).Append(':').Append(value.Float);
                    break;
                case AtkValueType.Int64:
                    builder.Append(i).Append(':').Append(value.Int64);
                    break;
                case AtkValueType.UInt64:
                    builder.Append(i).Append(':').Append(value.UInt64);
                    break;
                default:
                    builder.Append(i).Append(':').Append(value.Type);
                    break;
            }
        }

        _log.Info("Talk probe AtkValues: {Values}", builder.ToString());
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        foreach (var addon in Addons)
        {
            _addonLifecycle.UnregisterListener(AddonEvent.PostSetup, addon, _onAddonEvent);
            _addonLifecycle.UnregisterListener(AddonEvent.PostRefresh, addon, _onAddonEvent);
        }
    }
}
#endif
