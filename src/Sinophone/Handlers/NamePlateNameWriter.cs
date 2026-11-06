using FFXIVClientStructs.FFXIV.Client.UI;

namespace Sinophone.Handlers;

/// <summary>
/// Writes the Chinese name of every visible NPC into the game's own nameplate, so the
/// game draws it above the NPC's head with its own font and scaling.
/// </summary>
/// <remarks>
/// The nameplate addon bakes each plate's name into a texture. A new text only reaches
/// the screen after the plate is marked <c>NeedsToBeBaked</c>, so every write sets it.
/// The bake is sized from <c>NamePlateObject.TextW</c> and <c>TextH</c>. Those hold the
/// size of the English name, which is one line, so a two-line name sets them from the
/// new text and restores them when the plate goes back to English.
/// <para>
/// The game owns the name text and puts the English name back when it updates the plate.
/// This class remembers what it wrote for each of the 50 slots and restores it after the
/// addon update. It forgets a slot once the game gives that slot to another object.
/// </para>
/// <para>
/// A visible NPC with the same name as another gets the same translated name, because a
/// plate slot has no object id. Player characters are left alone, as their names are
/// chosen by the player.
/// </para>
/// </remarks>
public sealed unsafe class NamePlateNameWriter
{
    private const string AddonName = "NamePlate";

    private readonly IGameGui _gameGui;
    private readonly Configuration _configuration;
    private readonly Func<TranslationStore?> _getStore;
    private readonly IPluginLog _log;
    private readonly SlotState?[] _slots = new SlotState?[AddonNamePlate.NumNamePlateObjects];

    private bool _enabled;
    private bool _loggedSizeReset;

    public NamePlateNameWriter(
        IGameGui gameGui,
        Configuration configuration,
        Func<TranslationStore?> getStore,
        IPluginLog log)
    {
        _gameGui = gameGui;
        _configuration = configuration;
        _getStore = getStore;
        _log = log;
    }

    /// <summary>Slots currently holding text this writer put there.</summary>
    public int ChangedPlateCount => _slots.Count(state => state is { Written: not null });

    /// <summary>
    /// Starts translating plates. Nothing is written until the next addon update, so
    /// the nameplate text is always read after the game set it.
    /// </summary>
    public void Enable() => _enabled = true;

    /// <summary>Stops translating and puts the game's English names back.</summary>
    public void Disable()
    {
        _enabled = false;
        RestoreAll();
    }

    /// <summary>
    /// Puts the English names back and looks every plate up again. Call after the
    /// translation data changes, because the Chinese text may belong to another variant.
    /// </summary>
    public void Reset() => RestoreAll();

    /// <summary>
    /// Runs after the game updates the nameplate addon. Reads each visible plate, writes
    /// the Chinese name when it has one, and puts the Chinese back when the game has
    /// replaced it.
    /// </summary>
    /// <remarks>
    /// Called once per addon update. A slot the writer already knows costs two span
    /// compares and no allocation. A lookup only runs when a plate shows a new name.
    /// </remarks>
    public void Update()
    {
        if (!_enabled)
            return;

        var addon = GetAddon();
        if (addon is null)
        {
            // The addon is gone, so its nodes went with it. The game rebuilds the plates
            // from its own state, and there is nothing to put back.
            Array.Clear(_slots);
            return;
        }

        for (var slot = 0; slot < _slots.Length; slot++)
        {
            var plate = addon->NamePlateObjectArray + slot;
            var node = plate->NameText;
            if (plate->RootComponentNode is null || node is null || !plate->IsVisible)
                continue;

            var current = node->NodeText.AsSpan();
            var state = _slots[slot];

            if (state is not null)
            {
                if (state.Written is not null && current.SequenceEqual(state.WrittenUtf8))
                {
                    KeepSize(plate, state);
                    continue;
                }

                if (current.SequenceEqual(state.EnglishUtf8))
                {
                    // The game wrote the English name again. Put the Chinese back.
                    if (state.Written is not null)
                        Write(plate, state, replacedByGame: true);

                    continue;
                }

                // Another object owns the slot now, so the saved name is stale.
                _slots[slot] = null;
            }

            Resolve(plate, slot);
        }
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

    private void Resolve(AddonNamePlate.NamePlateObject* plate, int slot)
    {
        var node = plate->NameText;

        // Player names are chosen by the player and are never translated.
        if (plate->IsPlayerCharacter)
        {
            var playerName = node->NodeText.ToString();
            _slots[slot] = SlotState.Skipped(playerName, Encoding.UTF8.GetBytes(playerName));
            return;
        }

        var englishName = node->NodeText.ToString();
        if (englishName.Length == 0)
        {
            _slots[slot] = SlotState.Skipped(englishName, Encoding.UTF8.GetBytes(englishName));
            return;
        }

        var chineseName = Translate(englishName);
        if (chineseName is null)
        {
            // Remember the miss, so this slot is not looked up on every update.
            _slots[slot] = SlotState.Skipped(englishName, Encoding.UTF8.GetBytes(englishName));
            return;
        }

        var written = FormatNamePlateText(chineseName, englishName, _configuration.TargetNameShowEnglish);
        var state = new SlotState(
            englishName,
            Encoding.UTF8.GetBytes(englishName),
            written,
            Encoding.UTF8.GetBytes(written),
            plate->TextW,
            plate->TextH);

        _slots[slot] = state;
        Write(plate, state, replacedByGame: false);

        _log.Debug("Native name: wrote {Written} into nameplate slot {Slot} for {English}.",
            written, slot, englishName);
    }

    private string? Translate(string englishName)
    {
        var store = _getStore();
        return store is not null && store.TryGetName(englishName, out var chineseName)
            ? chineseName
            : null;
    }

    private void Write(AddonNamePlate.NamePlateObject* plate, SlotState state, bool replacedByGame)
    {
        if (state.Written is not { } written)
            return;

        var node = plate->NameText;

        node->SetText(written);
        state.SizeChanged = ApplySize(plate, written);
        state.WrittenTextW = plate->TextW;
        state.WrittenTextH = plate->TextH;
        plate->NeedsToBeBaked = true;

        if (replacedByGame && !state.LoggedReplace)
        {
            // Once per slot: the game that rewrites on every update would bury the log.
            state.LoggedReplace = true;
            _log.Debug("Native name: the game rewrote a nameplate; keeping the Chinese name for {English}.",
                state.English);
        }
    }

    /// <summary>
    /// Sizes the bake for a two-line name. The bake texture is allocated from
    /// <c>TextW</c> and <c>TextH</c>, and the game sized them for one line of English
    /// text, which cuts off the top line. A single-line name keeps the game's size.
    /// </summary>
    private bool ApplySize(AddonNamePlate.NamePlateObject* plate, string text)
    {
        if (!text.Contains('\n'))
            return false;

        var node = plate->NameText;
        node->ResizeNodeForCurrentText();

        plate->TextW = (short)Math.Min(node->Width, short.MaxValue);
        plate->TextH = (short)Math.Min(node->Height, short.MaxValue);
        return true;
    }

    /// <summary>
    /// Puts the two-line size back when the game has reset it. The size is what decides
    /// how much of the name the bake holds, so a reset shows the clipped text again.
    /// </summary>
    private void KeepSize(AddonNamePlate.NamePlateObject* plate, SlotState state)
    {
        if (!state.SizeChanged
            || (plate->TextW == state.WrittenTextW && plate->TextH == state.WrittenTextH))
        {
            return;
        }

        plate->TextW = state.WrittenTextW;
        plate->TextH = state.WrittenTextH;
        plate->NeedsToBeBaked = true;

        if (!_loggedSizeReset)
        {
            _loggedSizeReset = true;
            _log.Debug("Native name: the game reset a nameplate size; keeping the two-line size.");
        }
    }

    private void RestoreAll()
    {
        var addon = GetAddon();

        if (addon is not null)
        {
            for (var slot = 0; slot < _slots.Length; slot++)
            {
                var state = _slots[slot];
                if (state?.Written is null)
                {
                    _slots[slot] = null;
                    continue;
                }

                var plate = addon->NamePlateObjectArray + slot;
                var node = plate->NameText;
                if (node is null || !node->NodeText.AsSpan().SequenceEqual(state.WrittenUtf8))
                {
                    // The game already put its own text back, so there is nothing to undo.
                    _slots[slot] = null;
                    continue;
                }

                node->SetText(state.English);
                if (state.SizeChanged)
                {
                    plate->TextW = state.OriginalTextW;
                    plate->TextH = state.OriginalTextH;
                }

                plate->NeedsToBeBaked = true;
                _slots[slot] = null;
            }
        }

        Array.Clear(_slots);
        _loggedSizeReset = false;
    }

    private AddonNamePlate* GetAddon()
    {
        var addon = _gameGui.GetAddonByName<AtkUnitBase>(AddonName, 1);
        return addon is null ? null : (AddonNamePlate*)addon;
    }

    private sealed class SlotState(
        string english,
        byte[] englishUtf8,
        string? written,
        byte[] writtenUtf8,
        short originalTextW,
        short originalTextH)
    {
        /// <summary>The game's text for this slot when the writer first saw it.</summary>
        public string English { get; } = english;

        public byte[] EnglishUtf8 { get; } = englishUtf8;

        /// <summary>What the writer wrote, or null when the name has no translation.</summary>
        public string? Written { get; } = written;

        public byte[] WrittenUtf8 { get; } = writtenUtf8;

        public short OriginalTextW { get; } = originalTextW;

        public short OriginalTextH { get; } = originalTextH;

        /// <summary>True when the writer enlarged the bake for a two-line name.</summary>
        public bool SizeChanged { get; set; }

        /// <summary>The bake size the writer set, used to put it back after a reset.</summary>
        public short WrittenTextW { get; set; }

        public short WrittenTextH { get; set; }

        /// <summary>Set after the first time the game replaced this slot's text.</summary>
        public bool LoggedReplace { get; set; }

        public static SlotState Skipped(string english, byte[] englishUtf8)
            => new(english, englishUtf8, null, [], 0, 0);
    }
}
