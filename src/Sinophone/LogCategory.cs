namespace Sinophone;

/// <summary>
/// Groups the plugin's own log messages, so the settings window can hide one kind of
/// detail without hiding the rest.
/// </summary>
public enum LogCategory
{
    /// <summary>Plugin load, unload, and settings changes.</summary>
    Plugin = 0,

    /// <summary>Talk and subtitle lines the plugin reads and looks up.</summary>
    Dialogue = 1,

    /// <summary>Writes into the game's own dialogue box.</summary>
    NativeText = 2,

    /// <summary>Selection dialog prompts and options.</summary>
    SelectionDialogs = 3,

    /// <summary>Nameplate names.</summary>
    NamePlates = 4,

    /// <summary>Item name lookups.</summary>
    ItemNames = 5,

    /// <summary>Translation data loading, indexing, and downloads.</summary>
    Data = 6,

    /// <summary>The debug probes. Debug builds only.</summary>
    Diagnostics = 7,
}
