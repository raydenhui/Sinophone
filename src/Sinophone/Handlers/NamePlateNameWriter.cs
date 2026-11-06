using FFXIVClientStructs.FFXIV.Client.UI;

namespace Sinophone.Handlers;

/// <summary>
/// Writes the translated name of the current target into the game's own nameplate,
/// so the game draws it above the NPC's head with its own font and scaling.
/// </summary>
/// <remarks>
/// The nameplate addon bakes each plate's name into a texture. A new text only reaches
/// the screen after the plate is marked <c>NeedsToBeBaked</c>, so every write sets it.
/// <para>
/// The game owns the name text and puts the English name back when it updates the plate.
/// This class remembers what it wrote and restores it on the next addon update. It stops
/// touching a slot once the game gives that slot to another object.
/// </para>
/// <para>
/// The 50 plate slots are reused by whichever objects are nearby, and a slot has no
/// object id. The only way to find the target's plate is to match its name text, so a
/// visible NPC with the same name also gets the Chinese name.
/// </para>
/// </remarks>
public sealed unsafe class NamePlateNameWriter
{
    private const string AddonName = "NamePlate";

    private readonly IGameGui _gameGui;
    private readonly IPluginLog _log;
    private readonly List<ChangedPlate> _changed = [];

    private string _englishName = string.Empty;
    private byte[] _englishUtf8 = [];
    private string _written = string.Empty;
    private byte[] _writtenUtf8 = [];
    private bool _loggedOverwrite;

    public NamePlateNameWriter(IGameGui gameGui, IPluginLog log)
    {
        _gameGui = gameGui;
        _log = log;
    }

    /// <summary>Slots currently holding text this writer put there.</summary>
    public int ChangedPlateCount => _changed.Count;

    /// <summary>
    /// Sets the name to keep on the target's plate. Passing an empty translated name
    /// clears the target. Does nothing when the target and the text are unchanged.
    /// </summary>
    public void SetTarget(string englishName, string? translatedText)
    {
        if (string.IsNullOrWhiteSpace(translatedText))
        {
            Clear();
            return;
        }

        if (string.Equals(_englishName, englishName, StringComparison.Ordinal)
            && string.Equals(_written, translatedText, StringComparison.Ordinal))
        {
            // Same target: keep whatever is on the plate and let Maintain fix it.
            return;
        }

        Clear();

        _englishName = englishName;
        _englishUtf8 = Encoding.UTF8.GetBytes(englishName);
        _written = translatedText;
        _writtenUtf8 = Encoding.UTF8.GetBytes(translatedText);

        if (!Apply())
            _log.Debug("Native name: no visible nameplate slot shows {English} yet; will retry on update.",
                englishName);
    }

    /// <summary>
    /// Puts the game's English name back and stops tracking the plate. Safe to call
    /// when nothing was written.
    /// </summary>
    public void Clear()
    {
        Restore();
        _englishName = string.Empty;
        _englishUtf8 = [];
        _written = string.Empty;
        _writtenUtf8 = [];
        _loggedOverwrite = false;
    }

    /// <summary>
    /// Runs after the game updates the nameplate addon. Puts the Chinese name back when
    /// the game has replaced it, and keeps the plate marked for a re-bake.
    /// </summary>
    /// <remarks>
    /// Called once per addon update, so this path compares UTF-8 bytes and allocates
    /// nothing. A full slot scan only runs while the target's plate has not been found.
    /// </remarks>
    public void Maintain()
    {
        if (_englishName.Length == 0)
            return;

        var addon = GetAddon();
        if (addon is null)
            return;

        if (_changed.Count == 0)
        {
            // The target's plate was not on screen when the target changed. It may be
            // back now, after the addon update.
            Apply();
            return;
        }

        for (var i = _changed.Count - 1; i >= 0; i--)
        {
            var entry = _changed[i];
            var plate = addon->NamePlateObjectArray + entry.Slot;
            var node = plate->NameText;

            if (node is null)
            {
                _changed.RemoveAt(i);
                continue;
            }

            var current = node->NodeText.AsSpan();
            if (current.SequenceEqual(_writtenUtf8))
                continue;

            if (current.SequenceEqual(entry.OriginalUtf8))
            {
                // The game wrote the English name again. Put the Chinese back.
                node->SetText(_written);
                plate->NeedsToBeBaked = true;

                if (!_loggedOverwrite)
                {
                    // Once per target: a log per update would bury everything else.
                    _loggedOverwrite = true;
                    _log.Debug("Native name: the game rewrote nameplate slot {Slot}; keeping the Chinese name.",
                        entry.Slot);
                }

                continue;
            }

            // Another object owns the slot now, so the saved English name is stale.
            _changed.RemoveAt(i);
        }
    }

    /// <summary>
    /// Finds every visible plate that shows the English name and writes the Chinese.
    /// Returns true when at least one plate was changed.
    /// </summary>
    private bool Apply()
    {
        var addon = GetAddon();
        if (addon is null)
            return false;

        var found = false;
        for (var slot = 0; slot < AddonNamePlate.NumNamePlateObjects; slot++)
        {
            var plate = addon->NamePlateObjectArray + slot;
            if (plate->RootComponentNode is null || plate->NameText is null || !plate->IsVisible)
                continue;

            if (!plate->NameText->NodeText.AsSpan().SequenceEqual(_englishUtf8))
                continue;

            var original = plate->NameText->NodeText.ToString();
            var originalUtf8 = Encoding.UTF8.GetBytes(original);

            plate->NameText->SetText(_written);
            plate->NeedsToBeBaked = true;
            _changed.Add(new ChangedPlate(slot, original, originalUtf8, _writtenUtf8));

            _log.Debug("Native name: wrote {Written} into nameplate slot {Slot} for {English}.",
                _written, slot, _englishName);
            found = true;
        }

        return found;
    }

    private void Restore()
    {
        if (_changed.Count == 0)
            return;

        var addon = GetAddon();
        if (addon is null)
        {
            // The addon is gone, and its nodes went with it. The game rebuilds the
            // plates from its own state, so there is nothing to put back.
            _changed.Clear();
            return;
        }

        foreach (var entry in _changed)
        {
            var plate = addon->NamePlateObjectArray + entry.Slot;
            var node = plate->NameText;
            if (node is null || !node->NodeText.AsSpan().SequenceEqual(entry.WrittenUtf8))
                continue;

            node->SetText(entry.Original);
            plate->NeedsToBeBaked = true;
        }

        _log.Debug("Native name: restored {Count} nameplate slot(s).", _changed.Count);
        _changed.Clear();
    }

    private AddonNamePlate* GetAddon()
    {
        var addon = _gameGui.GetAddonByName<AtkUnitBase>(AddonName, 1);
        return addon is null ? null : (AddonNamePlate*)addon;
    }

    private readonly record struct ChangedPlate(int Slot, string Original, byte[] OriginalUtf8, byte[] WrittenUtf8);
}
