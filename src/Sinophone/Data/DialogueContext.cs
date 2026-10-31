namespace Sinophone.Data;

/// <summary>
/// One row and text field in a game sheet. The slot is the index inside a
/// multi-slot field such as <c>DefaultTalk.Text</c>; -1 means the caller does not
/// know which slot is on screen.
/// </summary>
public readonly record struct DialogueRowId(string Sheet, uint RowId, string Field, int Slot = -1);

/// <summary>
/// Identifiers the game exposes for the line being shown. A lookup tries these
/// before falling back to normalized English text. Every field is optional so a
/// surface without an id keeps working exactly as before.
/// </summary>
public readonly record struct DialogueContext(
    DialogueRowId? Row = null,
    IReadOnlyList<string>? ScriptKeys = null);
