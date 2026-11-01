using Dalamud.Hooking;
using InteropGenerator.Runtime;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace Sinophone.Handlers;

/// <summary>
/// Development capture for the parameters passed to Addon text formatting.
/// Issue #90 needs the real values instead of parsing the formatted text.
/// </summary>
public sealed unsafe class AddonTextParameterCapture : IDisposable
{
    private readonly IPluginLog _log;
    private readonly Hook<RaptureTextModule.Delegates.FormatAddonText2Int> _twoInt;
    private readonly Hook<RaptureTextModule.Delegates.FormatAddonText2IntInt> _twoIntInt;
    private readonly Hook<RaptureTextModule.Delegates.FormatAddonText2IntIntUInt> _twoIntIntUInt;
    private readonly Hook<RaptureTextModule.Delegates.FormatAddonText1IntIntUInt> _oneIntIntUInt;

    public AddonTextParameterCapture(IGameInteropProvider hooks, IPluginLog log)
    {
        _log = log;

        _twoInt = hooks.HookFromAddress<RaptureTextModule.Delegates.FormatAddonText2Int>(
            RaptureTextModule.Addresses.FormatAddonText2Int.Value, DetourTwoInt);
        _twoIntInt = hooks.HookFromAddress<RaptureTextModule.Delegates.FormatAddonText2IntInt>(
            RaptureTextModule.Addresses.FormatAddonText2IntInt.Value, DetourTwoIntInt);
        _twoIntIntUInt = hooks.HookFromAddress<RaptureTextModule.Delegates.FormatAddonText2IntIntUInt>(
            RaptureTextModule.Addresses.FormatAddonText2IntIntUInt.Value, DetourTwoIntIntUInt);
        _oneIntIntUInt = hooks.HookFromAddress<RaptureTextModule.Delegates.FormatAddonText1IntIntUInt>(
            RaptureTextModule.Addresses.FormatAddonText1IntIntUInt.Value, DetourOneIntIntUInt);

        _twoInt.Enable();
        _twoIntInt.Enable();
        _twoIntIntUInt.Enable();
        _oneIntIntUInt.Enable();
    }

    private CStringPointer DetourTwoInt(RaptureTextModule* module, uint addonId, int value)
    {
        Log(addonId, value);
        return _twoInt.Original(module, addonId, value);
    }

    private CStringPointer DetourTwoIntInt(RaptureTextModule* module, uint addonId, int value1, int value2)
    {
        Log(addonId, value1, value2);
        return _twoIntInt.Original(module, addonId, value1, value2);
    }

    private CStringPointer DetourTwoIntIntUInt(
        RaptureTextModule* module, uint addonId, int value1, int value2, uint value3)
    {
        Log(addonId, value1, value2, (int)value3);
        return _twoIntIntUInt.Original(module, addonId, value1, value2, value3);
    }

    private CStringPointer DetourOneIntIntUInt(
        RaptureTextModule* module, uint addonId, int value1, int value2, uint value3)
    {
        Log(addonId, value1, value2, (int)value3);
        return _oneIntIntUInt.Original(module, addonId, value1, value2, value3);
    }

    private void Log(uint addonId, params int[] values)
    {
        // Narrow the trace to the Addon-sheet range around the garden prompt while
        // investigating issue #90. Remove this filter once the mapping is known.
        if (addonId is >= 6000 and <= 7000)
            _log.Debug("Addon text format {AddonId}: {Values}", addonId, string.Join(", ", values));
    }

    public void Dispose()
    {
        _twoInt.Dispose();
        _twoIntInt.Dispose();
        _twoIntIntUInt.Dispose();
        _oneIntIntUInt.Dispose();
    }
}
