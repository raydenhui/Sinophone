using Dalamud.Game.Gui.NamePlate;
using Dalamud.Game.Text.SeStringHandling;

namespace Sinophone.Handlers;

/// <summary>
/// Translates the game's own nameplates, so the game draws the Chinese name above every
/// visible NPC with the font, size, and position it uses for the English name.
/// </summary>
/// <remarks>
/// The name is replaced through Dalamud's nameplate API, before the game lays the plate
/// out. The game then measures the text and bakes the plate at its own size, so a wider
/// Chinese name is not clipped and the shared bake texture stays intact.
/// <para>
/// Writing the text node after the addon update and marking the plate for baking does not
/// do that. The bake keeps the size the game gave the English name, so the sides of a
/// wider name are cut off, and fitting the text by changing the plate's size fields fights
/// the game's own measuring and damages names on other plates.
/// </para>
/// <para>
/// A plate slot carries no object id, so the translation follows the name text. Two visible
/// NPCs with the same name both get the same Chinese name. Player names are left alone.
/// </para>
/// </remarks>
public sealed class NamePlateNameWriter
{
    private readonly INamePlateGui _namePlateGui;
    private readonly Configuration _configuration;
    private readonly Func<TranslationStore?> _getStore;
    private readonly IPluginLog _log;
    private readonly INamePlateGui.OnPlateUpdateDelegate _onPlateUpdate;

    private bool _enabled;

    public NamePlateNameWriter(
        INamePlateGui namePlateGui,
        Configuration configuration,
        Func<TranslationStore?> getStore,
        IPluginLog log)
    {
        _namePlateGui = namePlateGui;
        _configuration = configuration;
        _getStore = getStore;
        _log = log;
        _onPlateUpdate = OnPlateUpdate;
    }

    /// <summary>
    /// Starts translating plates. <see cref="Reset"/> asks the game to lay out every
    /// visible plate again, so plates already on screen change without waiting for an update.
    /// </summary>
    public void Enable()
    {
        if (_enabled)
            return;

        _enabled = true;
        _namePlateGui.OnNamePlateUpdate += _onPlateUpdate;
        _namePlateGui.RequestRedraw();
        _log.Debug("Native names: translating every visible NPC nameplate.");
    }

    /// <summary>Stops translating and asks the game to draw its own names again.</summary>
    public void Disable()
    {
        if (!_enabled)
            return;

        _enabled = false;
        _namePlateGui.OnNamePlateUpdate -= _onPlateUpdate;

        // The game rewrites every name from its own data on a full update, which puts the
        // English names back on screen.
        _namePlateGui.RequestRedraw();
        _log.Debug("Native names: stopped translating nameplates.");
    }

    /// <summary>
    /// Lays out every visible plate again. Call after the translation data changes, because
    /// the Chinese text may belong to another variant.
    /// </summary>
    public void Reset()
    {
        if (_enabled)
            _namePlateGui.RequestRedraw();
    }

    /// <summary>
    /// Builds the text written into the nameplate. A plate holds one line, so the English
    /// name follows the Chinese name in brackets when it is shown.
    /// </summary>
    internal static string FormatNamePlateText(string chineseName, string englishName, bool showEnglish)
    {
        if (!showEnglish || string.IsNullOrWhiteSpace(englishName))
            return chineseName;

        return $"{chineseName} ({englishName})";
    }

    /// <summary>
    /// Runs inside the nameplate addon update, before the game sets the plate's text. Only
    /// plates with a new or changed name are passed in, so this is not a per-frame path.
    /// </summary>
    private void OnPlateUpdate(
        INamePlateUpdateContext context,
        IReadOnlyList<INamePlateUpdateHandler> handlers)
    {
        if (!_enabled)
            return;

        foreach (var handler in handlers)
        {
            // Player names are chosen by the player and are never translated.
            if (handler.PlayerCharacter is not null)
                continue;

            var englishName = handler.Name.TextValue;
            if (englishName.Length == 0)
                continue;

            var chineseName = Translate(englishName);
            if (chineseName is null)
                continue;

            var text = FormatNamePlateText(chineseName, englishName, _configuration.TargetNameShowEnglish);
            handler.NameParts.Text = new SeStringBuilder().AddText(text).Build();

            _log.Debug("Native name: {English} -> {Chinese}.", englishName, text);
        }
    }

    private string? Translate(string englishName)
    {
        var store = _getStore();
        return store is not null && store.TryGetName(englishName, out var chineseName)
            ? chineseName
            : null;
    }
}
