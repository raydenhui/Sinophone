namespace Sinophone.Data;

/// <summary>
/// One row and text field in a game sheet. The slot is the index inside a
/// multi-slot field such as <c>DefaultTalk.Text</c>; -1 means the caller does not
/// know which slot is on screen.
/// </summary>
public readonly record struct DialogueRowId(string Sheet, uint RowId, string Field, int Slot = -1);

/// <summary>
/// Identifiers the game exposes for the line being shown. A lookup tries these
/// before falling back to normalized English text. <see cref="ScriptKeyHints"/> carries
/// script names that can narrow a line several scripts share, without being an exact key
/// for this line. <see cref="ScriptRowIds"/> maps each live script key to the row id
/// the game's Lua text map uses, so a key can be trusted without comparing English
/// text. <see cref="ScriptTemplates"/> maps a script key to the template of the live
/// Lua value: the game marks the spans that vary, such as a gender branch, so the dump's
/// wording can be compared against what the live value could have drawn. Every field is
/// optional so a surface without an id keeps working exactly as before.
/// </summary>
public readonly record struct DialogueContext(
    DialogueRowId? Row = null,
    IReadOnlyList<string>? ScriptKeys = null,
    uint CutsceneId = 0,
    bool? ChocoboTaxiStandUnlocked = null,
    IReadOnlyList<string>? ScriptKeyHints = null,
    IReadOnlyDictionary<string, uint>? ScriptRowIds = null,
    DialogueRowId? ScriptRow = null,
    IReadOnlyDictionary<string, string>? ScriptTemplates = null);
