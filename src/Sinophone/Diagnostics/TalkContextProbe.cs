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
    private readonly IPluginLog _log;
    private readonly TalkEventReader _reader = null!;
    private readonly IAddonLifecycle.AddonEventDelegate _onAddonEvent;
    private bool _disposed;

    public TalkContextProbe(IAddonLifecycle addonLifecycle, IPluginLog log)
    {
        _addonLifecycle = addonLifecycle;
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
        var snapshot = _reader.ReadSnapshot();
        if (snapshot is null)
        {
            LastResult = "EventFramework is not available.";
            _log.Info("Talk probe: {Result}", LastResult);
            return;
        }

        var sceneData = string.Join(",", snapshot.SceneData.Take(16));
        _log.Info(
            "Talk probe: scene {Scene}, scene object {SceneObjectId}, scene data [{SceneData}] ({SceneDataCount} values), handlers: {HandlerCount}.",
            snapshot.Scene,
            snapshot.SceneGameObjectId,
            sceneData,
            snapshot.SceneData.Count,
            snapshot.Handlers.Count);

        foreach (var handler in snapshot.Handlers)
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

            foreach (var (key, value) in handler.LuaTexts.Take(40))
                _log.Info("Talk probe lua text: {Key} = {Value}", key, value);
        }

        LastResult = $"Scene {snapshot.Scene}; {snapshot.Handlers.Count} handler(s). See the plugin log.";
    }

    private void OnAddonEvent(AddonEvent eventType, AddonArgs args)
    {
        if (!Enabled || _disposed)
            return;

        _log.Debug("Talk probe triggered by {Addon} {Event}.", args.AddonName, eventType);
        LogCurrent();
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
