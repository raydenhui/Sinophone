# Architecture

```
src/Sinophone/
  Plugin.cs               Entry point. Services, windows, /sinophone command, config wiring.
  Configuration.cs        Saved settings. Changing a field changes the config schema.
  Data/
    TranslationArtifact.cs  Artifact shape: Sheets[sheet][rowId][field] = string[]
    DialogueContext.cs      Optional runtime ids: sheet row + slot, or script text key.
    TranslationStore.cs     Loads artifacts. Resolves by id first, then English text.
    TranslationDataUpdater.cs Downloads published artifacts into the plugin config directory.
    SheetCatalog.cs         Which game sheets to index and how to read each one.
    MacroNormalizer.cs      Text cleanup for matching and for display.
    ConditionalText.cs      Picks a branch when the CN/TC text carries <If> tags.
    SeStringVariants.cs     Splits a conditional SeString into one string per branch.
    GenderedPronouns.cs     Collapses gendered pronouns so a script line matches either branch.
    ChineseConverter.cs     Optional simplified/traditional conversion, built from OpenCC.
  Handlers/
    TalkHandler.cs        Watches the Talk and TalkSubtitle addons. Reads text. Feeds the overlay.
    TalkEventReader.cs    Reads the active event row and Lua text keys for Talk.
    NativeTextWriter.cs   Writes Chinese into a game text node for the alpha native mode.
    TargetNameHandler.cs  Watches the TargetInfo addon. Feeds the target name window.
    ItemNameHandler.cs    Watches the hovered item. Feeds the item name window.
  UI/
    OverlayWindow.cs      The translation overlay.
    TargetNameWindow.cs   Small window under the target info with the target's Chinese name.
    ItemNameWindow.cs     Small window above the cursor with the hovered item's Chinese name.
    ConfigWindow.cs       Settings window.
    FontManager.cs        CJK font loading and font atlas rebuilds.
tools/Sinophone.DataPrep/
  Program.cs              CSV -> gzipped JSON converter.
  ScriptDump.cs           Reads the script dumps under custom/, cut_scene/, and the rest.
```

Data flow, in order:

```
CSV dump --(DataPrep)--> Sinophone.<lang>.json.gz --(embedded or data release)--> TranslationStore
live game sheet row --(SheetCatalog)--> English text --(MacroNormalizer)--> dictionary key
script dump --(DataPrep)--> Scripts section (English + translation) --(TranslationStore)--> dictionary key
Talk addon event --(TalkEventReader)--> event row id / Lua text key --(TranslationStore)--> Chinese
Talk addon event --(TalkHandler)--> English text + name --(TranslationStore)--> Chinese (fallback)
Chinese --(ChineseConverter)--> converted Chinese, when the setting is on
converted Chinese --(OverlayWindow)--> screen
```

Some dialogue has no Lumina row at all. It lives in the game's script dumps, which the dumps
repositories store under `custom/`, `cut_scene/`, `quest/`, and a few smaller folders. `ScriptDump`
reads those files, pairs them across languages by text key (`TEXT_...`), and writes a `Scripts`
section into the artifact. The English text travels in the artifact because there is no sheet to
read it from.

`quest/` also holds journal steps, objectives, and unused entries, which no Talk window shows.
`ScriptDump` skips keys with `_SEQ_`, `_TODO_`, or `_QIB_`. `_SYSTEM_` keys are kept, because
quest objects and story triggers draw them in the Talk window. That is about 221,000 lines, the
largest part of the artifact.

### Id-first dialogue lookup

The game exposes ids for some dialogue. `TranslationStore.TryGetDialogue` accepts a
`DialogueContext` and tries them before the English text:

1. A script text key from the game's `LuaTexts` map. The artifact keeps script entries
   keyed by the dump's `TEXT_...` key, so this is an exact match.
2. A sheet row id and field. A known slot wins; otherwise the store compares the
   English text with that row's own slots. This is what separates two rows that share
   the same English line.
3. The existing normalized English lookup, including the player-name and gendered
   pronoun fallbacks.

Every layer fails open. A missing event, a changed game structure, or an old artifact
falls through to text, so a surface without an id keeps the old behaviour.

For script lines, the game's `LuaTexts` value is matched against the live line with a
single wildcard only where the script marks a dynamic span, such as a player name or
Grand Company rank. The rest of the line still has to match exactly. This lets a line
resolve from the game's own key even when the English dump stores the dynamic span as
a blank.

`TalkEventReader` reads `EventFramework` and the active handlers at Talk time.
The current target's `EventHandler` is the first choice, because the event state is
cleared when the Talk window opens and `Scene` is -1 for most handlers. If no handler
owns the target, the reader falls back to `EventState1`/`EventState2`, then to the old
scene filter. `TalkEventSurfaces` maps an event content id to a sheet row; currently
that is `DefaultTalk`, `GuildleveAssignmentTalk`, `CraftLeveTalk`, `GimmickTalk`,
`GimmickBill`, and `GoldSaucerTalk`. A row is used only when exactly one active handler
maps to one, so an ambiguous scene does not guess. Quest and custom talk lines get
candidate script keys from `LuaEventHandler.LuaTexts`. `DefaultTalk` row ids are packed
as `(content << 16) | entry`, so use `EventId.Id`, not `EntryId`; the other mapped sheets
use `EntryId` as their row id.

Display resolves Grand Company parameters 52, 53, and 54 against the player's
current company and rank. A line can contain one block per company, and only the
current company block should render.

To add a surface, prefer a handler-specific reader and reuse `DialogueContext` rather
than adding a second lookup path. The `Talk probe` tab in Debug builds logs the event
ids, scene data and Lua text pairs, so the mapping can be checked in game before it is
trusted. Do not read game memory on a per-frame path; these reads happen on addon
events only.

### Overlay window

`OverlayWindow` is a Dalamud `Window`. Two Dalamud details are easy to get wrong:

- `AllowClickthrough` only controls whether the title bar's "Make clickthrough" option exists.
  `IsClickthrough` is the real state. Dalamud applies `NoInputs` only when `IsClickthrough` is true,
  so set both.
- Dalamud re-applies a non-null `Window.Position` every frame. Setting it once keeps forcing it
  unless the plugin sets `Position = null`. The overlay snaps only on first show or while
  `LockOverlay` is on, and clears `Position` otherwise so a drag sticks.
- A conversation ends by hiding the Talk addon, not by closing it. `PostClose` alone leaves the
  last line on screen. `TalkHandler` hides the overlay on `PostHide` and `PreFinalize` too.

### Native text mode

Alpha and off by default. `Configuration.NativeTextMode` switches the Talk dialog from the overlay
to the game's own dialogue box: `TalkHandler` resolves the line as usual, then `NativeTextWriter`
writes the Chinese into the addon's text node. The settings section is labelled **Alpha: In-game
text** so it does not read as a finished feature.

- Node 2 is the speaker name and node 3 is the dialogue in the `Talk` addon, confirmed from the
  live addon log. Re-confirm with the debug probe in `Diagnostics/NativeTextProbe.cs` (Debug builds
  only) before changing either constant.
- The game's font has no CJK glyphs. Without a font mod the line shows the missing-glyph character,
  so the settings section warns and links the README font setup. The plugin cannot detect the mod.
- A write saves the game's text for that node first. `RestoreAll` puts it back when the setting is
  turned off and on plugin unload. A second write must not save Chinese as the original, so the
  writer only saves when the node does not already hold text this writer put there.
- The game redraws the line while its text stream runs, so a write on `PostSetup` or `PostRefresh`
  lasts about one frame and the English comes back. `TalkHandler` therefore calls
  `NativeTextWriter.Reapply` on every `PreDraw`, which puts the Chinese back. This is the one
  guarded per-frame write: `Reapply` compares `NodeText.AsSpan()` with the bytes it last wrote, so a
  matching node costs no allocation and no write. Do not replace it with a one-shot write.
- An untranslated line, a missing store, or a failed write calls `Forget`, which drops the saved
  node without writing it. Without that, `Reapply` would keep the previous line's Chinese over the
  game's new English line.
- Only `Talk` is covered. `TalkSubtitle` and the chat bubbles keep the overlay until their nodes
  are checked in game. A failed write falls back to the overlay.

### Runtime data updates

The plugin reads artifacts from its config directory first and falls back to the copies embedded in
the assembly. `TranslationDataUpdater` fills that directory from the `data` GitHub release, which
`.github/workflows/data.yml` rebuilds weekly from the datamining CSVs.

- The release is tagged `data` and holds `manifest.json`, `Sinophone.cn.json.gz`, and
  `Sinophone.tc.json.gz`. The base URL is hardcoded in `TranslationDataUpdater`; renaming the
  repository breaks every installed copy.
- The manifest `version` is a hash of the translated sheets and script lines, not a date.
  `GeneratedAt` and `GameVersion` are left out, because they change on every build. The data
  workflow compares the new version with the published one and skips the upload when they match,
  so clients do not download again. `Configuration.LastDataVersion` records what is installed.
- `Scripts` is optional. Old readers ignore it, and a missing section means no script lines, so
  the artifact format version stays at 1.
- Both variants are downloaded by default. Turning off **Download both language variants**
  downloads only the selected one, which keeps an update small; the fallback then has no other
  data to read. `HasSimplifiedData` and `HasTraditionalData` say which files match
  `LastDataVersion`, and `OpenData` ignores a file without the matching flag, so a file left over
  from an older version is never mixed with current data.
- `manifest.json` has an optional checksum per artifact. Downloads go to a `.download` temp file and
  are only moved into place after the gzip, artifact format version, and sheet count are validated.
  Keep that order: a partial file must never replace working data.
- A completed download only raises a flag. `Plugin.OnTick` rebuilds the store on the game thread.
  Do not touch windows, fonts, or Lumina sheets from the updater's worker task.
- If a downloaded file cannot be parsed, `TranslationStore` retries with the embedded copy instead
  of running with no translations. The fallback opener is passed in by `Plugin`.
- Bump `formatVersion` only together with a change to `TranslationArtifact`, and keep the updater
  rejecting versions it does not understand.

Two files hold the same sheet list: `SheetCatalog.Create` and the `Sheets` array in
`tools/Sinophone.DataPrep/Program.cs`. If you add, remove, or rename a sheet, change both in the
same commit. A mismatch is silent: the artifact loads but rows are skipped. The data workflow runs
`--list-sheets` on the tool, so it does not keep a third copy of the list.

Script folders have one list, `ScriptDump.Directories`. `--list-scripts` exposes it, and the data
workflow uses that output for its sparse checkouts.
