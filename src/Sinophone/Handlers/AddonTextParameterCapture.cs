using Dalamud.Hooking;
using InteropGenerator.Runtime;
using FFXIVClientStructs.FFXIV.Component.Text;
using FFXIVClientStructs.STD;
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
    private readonly Hook<RaptureTextModule.Delegates.FormatAddonTextApply> _apply;
    private readonly Hook<RaptureTextModule.Delegates.GetAddonText> _getAddonText;
    private bool _logged6420;

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
        _apply = hooks.HookFromAddress<RaptureTextModule.Delegates.FormatAddonTextApply>(
            RaptureTextModule.Addresses.FormatAddonTextApply.Value, DetourApply);
        _getAddonText = hooks.HookFromAddress<RaptureTextModule.Delegates.GetAddonText>(
            RaptureTextModule.Addresses.GetAddonText.Value, DetourGetAddonText);

        _twoInt.Enable();
        _twoIntInt.Enable();
        _twoIntIntUInt.Enable();
        _oneIntIntUInt.Enable();
        _apply.Enable();
        _getAddonText.Enable();
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

    private CStringPointer DetourGetAddonText(RaptureTextModule* module, uint addonId)
    {
        if (addonId == 6420 && !_logged6420)
        {
            _logged6420 = true;
            _log.Debug("GetAddonText(6420) call stack: {Stack}", Environment.StackTrace);
        }

        return _getAddonText.Original(module, addonId);
    }

    private CStringPointer DetourApply(
        RaptureTextModule* module,
        uint addonId,
        RaptureTextModule.FormatAddonTextApplyMode mode,
        StdDeque<TextParameter>* localParameters,
        FFXIVClientStructs.FFXIV.Client.System.String.Utf8String* formatBuffer,
        FFXIVClientStructs.FFXIV.Client.System.String.Utf8String* normalizationBuffer)
    {
        if (addonId is >= 6000 and <= 7000 && localParameters is not null && localParameters->Count > 0)
        {
            var values = new List<string>(localParameters->Count);
            for (var i = 0; i < localParameters->Count; i++)
            {
                ref var parameter = ref (*localParameters)[i];
                values.Add(parameter.Type == TextParameterType.Integer
                    ? parameter.IntValue.ToString()
                    : parameter.Type.ToString());
            }

            _log.Debug("Addon text apply {AddonId} mode {Mode}: {Values}",
                addonId, mode, string.Join(", ", values));
        }

        return _apply.Original(module, addonId, mode, localParameters, formatBuffer, normalizationBuffer);
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
        _apply.Dispose();
        _getAddonText.Dispose();
    }
}
