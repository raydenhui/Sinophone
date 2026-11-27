# Architecture

```
src/Sinophone/
  Plugin.cs               Entry point. Services, windows, /sinophone command, config wiring.
  Configuration.cs        Saved settings. Changing a field changes the config schema.
  PluginLog.cs            Log output filtered by the group switches in the settings window.
  LogCategory.cs          The log groups the settings window can turn on or off.
  Data/
    TranslationArtifact.cs  Artifact shape: Sheets[sheet][rowId][field] = string[]
    DialogueContext.cs      Optional runtime ids: sheet row + slot, or script text key.
    TranslationStore.cs     The lookup facade. Resolves by id first, then the client's text.
    TranslationStore.Loading.cs   Load, Parse, Build, and artifact reading.
    TranslationStore.Indexing.cs  Turns parsed artifacts and live sheet text into the indexes.
    TranslationStoreCache.cs      Reads and writes Sinophone.index.bin.
    DialogueTextIndex.cs    Exact dialogue text, sheet keys, speaker aliases, branch-collapsed keys.
    ScriptIndex.cs          Script keys, sheet rows, cutscene scenes, and shared-key candidates.
    AddonIndex.cs           Addon labels and prompt templates with runtime values.
    NameIndex.cs            Names by text and by sheet row, matched without case.
    KanaText.cs             Prefers a localised row over one the dumps left in Japanese.
    IndexCacheFormat.cs     The primitives each index writes into the cache.
    SheetExtraction.cs      English sheet text read in slices; parsed artifacts awaiting indexing.
    TranslationDataUpdater.cs Downloads published artifacts into the plugin config directory.
    SheetCatalog.cs         Which game sheets to index and how to read each one.
    MacroNormalizer.cs      Text cleanup for matching and for display.
    ConditionalText.cs      Picks a branch when the CN/TC text carries <If> tags.
    SeStringVariants.cs     Splits a conditional SeString into one string per branch.
    GenderedPronouns.cs     Collapses gendered pronouns so a script line matches either branch.
    GenderedNouns.cs        Collapses nouns of address (lad/lass, sir/madam) for the same reason.
    TimeOfDayWords.cs       Collapses time-of-day words so a line matches the dump's other branch.
    ChineseConverter.cs     Optional simplified/traditional conversion, built from OpenCC.
                            Skips text that is already mostly in the target script.
  Fonts/
    FontPackFiles.cs      The 25 game font files a pack has to replace.
    FontPackExtractor.cs  Copies those files out of a .ttmp2 pack or a folder.
    FontReplacementService.cs Points the game's file reader at the copied files.
  Handlers/
    TalkHandler.cs        Watches the Talk and TalkSubtitle addons. Reads text. Feeds the overlay.
    TalkEventReader.cs    Reads the active event row and Lua text keys for Talk.
    NativeTextWriter.cs   Writes Chinese into a game text node for the alpha native mode.
    AtkTextMeasure.cs     Measures a text node with a null-terminated UTF-8 string.
    ChatBubbleHandler.cs  Watches _MiniTalk. Translates yells and default balloons in place.
    BubbleGeometry.cs     The pure background-fit rule for a translated bubble.
    BubbleLayout.cs       Reads the bubble nodes and writes the fitted geometry back.
    RecentBubbleTexts.cs  The last Chinese yell lines, so the sync does not look them up.
    SelectionDialogHandler.cs Translates SelectYesno / SelectString / SelectOk in place.
    SelectionPromptText.cs  The dialog's lookup, template fill, date, and line fitting.
    NamePlateNameWriter.cs Translates the game's nameplates through Dalamud's nameplate API.
    ItemNameHandler.cs    Watches the hovered item. Feeds the item name window or the game tooltip.
    HookGuard.cs          Runs a hook callback so a throw does not reach the game's frame.
  Localization/
    InterfaceLanguageResolver.cs Maps Dalamud's UI language code to the four settings languages.
    InterfaceText.cs      Resolves the settings language on each read.
    InterfaceStrings.cs   The settings-window string type. Every member is required.
    EnglishStrings.cs     English table.
    SimplifiedChineseStrings.cs   Simplified Chinese table.
    TraditionalChineseStrings.cs  Traditional Chinese table.
    JapaneseStrings.cs    Japanese table.
  UI/
    OverlayWindow.cs      The translation overlay.
    ItemNameWindow.cs     Small window above the cursor with the hovered item's Chinese name.
    ConfigWindow.cs       Settings window: tabs that are not a surface, and small widgets.
    ConfigWindow.Components.cs  The component tabs, one per surface.
    ConfigWindow.Testing.cs     The dev-only testing tab and its run controls.
    FontManager.cs        CJK font loading and font atlas rebuilds.
tools/Sinophone.DataPrep/
  Program.cs              CSV -> gzipped JSON converter.
  ScriptDump.cs           Reads the script dumps under custom/, cut_scene/, and the rest.
```

Data flow, in order:

```
CSV dump --(DataPrep)--> Sinophone.<lang>.json.gz --(embedded or data release)--> TranslationStore
live game sheet row --(SheetCatalog)--> client-language text --(MacroNormalizer)--> dictionary key
script dump --(DataPrep)--> Scripts section (key, sheet, row id, English + translation) --(TranslationStore)--> dictionary key
Talk addon event --(TalkEventReader)--> event row id / Lua text key and row id --(TranslationStore)--> Chinese
Talk addon event --(TalkHandler)--> client-language text + name --(TranslationStore)--> Chinese (fallback)
Chinese --(ChineseConverter)--> converted Chinese, when the setting is on
converted Chinese --(OverlayWindow)--> screen
```

See [translation-workflow.md](translation-workflow.md) for sequence and flow diagrams of data loading, lookup fallbacks, and each surface handler.

### The indexes

`TranslationStore` is a facade over four indexes. Each one owns its collections, its lookup
methods, and the part of the cache that holds them, so a change to one kind of text has one file
to edit:

- `DialogueTextIndex` holds exact dialogue text. It also keeps the keys a sheet row added, the
  speaker alias each script line carries, and the keys with gender or time-of-day words collapsed.
- `ScriptIndex` holds everything keyed by a dump script key: the Chinese text, the paired English
  line, the sheet row a cutscene subtitle uses, the cutscene scene a `VOICEMAN` key names, the
  blank-gap templates, and the candidates for an English key several script lines share.
- `AddonIndex` holds the short Addon labels and the prompt templates that match runtime values.
- `NameIndex` holds names by text and by sheet row, and matches without case.

`TranslationStore` keeps the id-first sheet row table (`_rowLookup`), UI text styles, the sheet
descriptors, and the display resolver. A new lookup collection belongs to one of the four indexes;
do not add another dictionary to the facade. `SheetExtraction`, `ExtractedRow`, and
`ParsedArtifacts` live in `SheetExtraction.cs`, because they are loading inputs rather than
lookup state.

Some dialogue has no Lumina row at all. It lives in the game's script dumps, which the dumps
repositories store under `custom/`, `cut_scene/`, `quest/`, and a few smaller folders. `ScriptDump`
reads those files, pairs them across languages by text key (`TEXT_...`), and writes a `Scripts`
section into the artifact. Each entry keeps the sheet name and the row id the game's `LuaTexts`
map uses. The English text travels in the artifact because the artifact is built from the English
dump; it is the fallback key, not the only identity.

`quest/` also holds journal steps, objectives, and unused entries, which no Talk window shows.
`ScriptDump` skips keys with `_SEQ_`, `_TODO_`, or `_QIB_`. `_SYSTEM_` keys are kept, because
quest objects and story triggers draw them in the Talk window. That is about 221,000 lines, the
largest part of the artifact.

### Id-first dialogue lookup

The game exposes ids for some dialogue. `TranslationStore.TryGetDialogue` accepts a
`DialogueContext` and tries them before the English text:

1. A script key and row id from the game's `LuaTexts` map. A matching row id proves
   the key is the live line, so no text comparison is needed and any client language
   works. Without a live row id, the store keeps the older English comparison.
2. A script sheet and row id, such as a cutscene subtitle matched against the live
   `VoiceMan` sheet. Row ids are the same in every language.
3. A sheet row id and field. A known slot wins; otherwise the store compares the
   client's text with that row's own slots. This is what separates two rows that share
   the same line.
4. The existing normalized text lookup, including the player-name and gendered
   pronoun fallbacks.

Every layer fails open. A missing event, a changed game structure, or an old artifact
falls through to text, so a surface without an id keeps the old behaviour.

For script lines, the game's `LuaTexts` value is matched against the live line with a
single wildcard only where the script marks a dynamic span, such as a player name or
Grand Company rank. The rest of the line still has to match exactly. This lets a line
resolve from the game's own key even when the dump stores the dynamic span as a blank.
The comparison is same-language, so it works on any client.

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

A script key read from `LuaEventHandler.LuaTexts` is used only when the dump paired
that key with the same English line. The live Lua map can reuse a key for another
menu line, and the old code would then show the dump's unrelated Chinese. The
comparison also accepts the dump's wording when the live Lua value's own template
could have drawn it. The game marks the spans that vary, such as a gender branch, so
a word the branch lists do not know still matches. A template with no wildcard has
to match exactly, so a reused key is still rejected.

Several script lines can share one English line. The dumps pair by text key, and a generic line such
as "You bastard!" appears in three scenes with three different Chinese texts, one of which names a
different NPC. A sheet row can share the same English line with script lines; the sheet text stays
as the fallback while the script candidates are kept for the identity steps. The text index never
answers a script key directly: `ScriptIndex` keeps the candidates in `_ambiguous` and narrows them
by the speaker name, the cutscene number, and the event
script hints. The hints come from a Lua handler key, the tracked quest ids in
`UIState.QuestTodoList`, a sibling option, or a fallback scan of every Lua handler when the active
filter finds no key. The speaker name is matched against the speaker token in the dump key, such as
`..._JULLUS` for "Jullus"; a two-part name is one token in the key (`..._GRAHATIA` for "G'raha
Tia"), so the whole name without its punctuation is matched as well as each word. The cutscene
number the game is playing is matched against the number in a `VOICEMAN` key:
`TEXT_VOICEMAN_06005_003840_THANCRED` is cutscene 6005. A key with no name and no cutscene, or
with more than one candidate left, stays unanswered and the line stays English. One exception:
when every remaining candidate belongs to the one cutscene that is playing, the first branch
answers. A cutscene can hold two versions of one line for different player progress, and the
addon exposes no flag for that choice. The id path is not affected: the game can still name the
exact script key, which always wins.

The cutscene number comes from the path in `PlayCutSceneTask.CutscenePath` (`cut_scene/060/
VoiceMan_06005` is scene 6005); a path with no number leaves the scene at 0, because the cutscene
row id is not a `VOICEMAN` number. The task is the one with
`EventSceneTaskType.PlayCutScene` under
`EventFramework.EventSceneModule.TaskManager.Tasks`. The task id is the cutscene row, which is not
the number a `VOICEMAN` key carries, so the path is what to read. It is read on the addon event
and travels in `DialogueContext.CutsceneId`; 0 means no cutscene, and every read failure leaves it at
0 so the lookup behaves as before. Each candidate keeps the dump keys that hold its Chinese
(`ScriptCandidate.Keys`), because a line used by two cutscenes needs both keys to be told apart.

A chocobokeep's greeting is a third case with the same shape. The question
(`..._CHOCOBOTAXI_01`) and the announcement for a stand that has just become usable
(`..._CHOCOBOTAXI_UNLOCK_01`) are one English line, so the text cannot pick one.
`UIState.IsChocoboTaxiStandUnlocked(EventId.Id)` can: a `ChocoboTaxiStand` handler's `EventId.Id` is
already the sheet row, and the bit is set once the stand is usable. The reader passes that state in
`DialogueContext.ChocoboTaxiStandUnlocked`, and the ambiguous step keeps the candidate whose key
carries `_UNLOCK` only for a locked stand. No stand handler, more than one stand, a row outside the
sheet, or a read failure leaves it null and the line stays English.

A line that holds only a sound, such as `<squeal> <squeal>`, is the last case. The dumps write the
sound as a lowercase word in angle brackets, and the game draws it as it stands, so `NormalizeKey`
keeps it. Several script lines can share that key with different Chinese, for example 嗷嗷嗷！ and
嗷嗷，嗷嗷。 A balloon has no speaker name and no id, so the candidates cannot be told apart, and
`MacroNormalizer.IsOnlySoundMarkers` answers with the first one: every candidate says the same
thing. Before this, the strip left an empty key, so those lines were skipped and never translated.

To add a surface, prefer a handler-specific reader and reuse `DialogueContext` rather
than adding a second lookup path. The **Testing** tab's `Talk probe` sub-tab logs the event
ids, scene data and Lua text pairs, so the mapping can be checked in game before it is
trusted. Do not read game memory on a per-frame path; these reads happen on addon
events only.

### Testing tab

`Diagnostics/` holds a developer harness plus two probes. The tab appears only when the build
defines `SINOPHONE_TESTING` and Dalamud loaded the plugin as a dev plugin.

- **Always compiled:** `HarnessCase`, `HarnessSettle`, `HarnessAssertions`, `HarnessMeasurement`,
  `HarnessLineTracker`, `HarnessRun`, `HarnessSession`, `HarnessReport`, `HarnessCatalog`,
  `HarnessBubbleTarget`, `HarnessNamePlateSelection`, `HarnessBuild`, and `UI/HarnessTabState`.
  These carry no game types, so `Test.ps1` covers them against a Release build. Put every rule
  here, not in the draw code.
- **`SINOPHONE_TESTING` only:** `HarnessHost`, the surface drivers, `TalkHarnessSurface`,
  `ChatBubbleHarness`, `GlyphProbe`, and `HarnessCapture`. A release DLL holds none of it.
- **Probes:** `NativeTextProbe` and `TalkContextProbe`, on their own sub-tabs.
- **Failure recording:** `FailureCase`, `FailureTrace`, and `FailureRecorder` are always compiled;
  only `Plugin.CreateFailureRecorder` is inside `SINOPHONE_TESTING`, so a release build never
  constructs one. The store and each handler take an optional `FailureTrace?` and record the
  layers they try. A null trace costs one null check, and a dev build writes one JSON file per
  unique case under `pluginConfigs/Sinophone/failures/`.

A run sends `HarnessCase` values through an `IHarnessDriver`, one per frame, and stops a case when
its text has not changed for `HarnessSettle.SettleFrames` samples. `HarnessAssertions` then turns
the measurement into findings. A new surface is one driver class.

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
- The overlay shows the Chinese translation only. When a line has no translation and
  `HideWhenNoTranslation` is off, it falls back to the English line. It never shows both
  languages at once. Colors and opacity fall back to the global values unless the component's
  `...UseGlobalAppearance` switch is off; the Talk and subtitle windows share the Talk values.

### Settings window language

`Configuration.InterfaceLanguage` picks the settings-window language. `Auto`, the default, reads
`PluginInterface.UiLanguage`: Dalamud reports `zh` for Simplified Chinese, `tw` for Traditional
Chinese, `ja` for Japanese, and `en` for English. Any other code falls back to English. An explicit
setting wins over Dalamud's language.

Dalamud does not translate plugin UI, so the plugin carries four `InterfaceStrings` tables. Every
member is `required`, so a language that misses a line fails the build. `InterfaceText` resolves the
table on each read, so a change in Dalamud's settings or in the plugin's own takes effect on the
next frame without a reload. The tab labels carry stable `###` ids so changing the language does
not reset the selected tab.

The settings language is separate from `Configuration.Variant`, which picks the translation data.
Runtime text stays English: the data updater's progress, the font service's status, and command
output. That text is also what the log and chat show, and the game draws chat with its own font,
which has no CJK glyphs on the English client.

The default ImGui font merges Noto Sans CJK only when Dalamud's own UI language is Chinese.
`FontManager.EnsureUiFont` therefore builds a second handle at the default font size, from a system
Chinese font chosen by the settings language, and `ConfigWindow.Draw` pushes it while drawing.
English and Japanese use the default font, which already covers the game's own scripts.

### Native text mode

Alpha and off by default. `Configuration.NativeTextMode` switches the Talk dialog and the cutscene
subtitle band from the overlay to the game's own windows. The Talk component labels this
**Native (game's own windows)** and warns that a font mod is required.

The Chinese goes in before the game lays the window out. `TalkHandler` hooks each addon's own
`OnSetup` and `OnRefresh` (from that addon's `VirtualTable`) and replaces the English strings in the
`AtkValue` array with the translation, so the game measures, wraps, and sizes the window for the
Chinese line. This is the same thing the Chinese client does, and it is the only way a long line
stays inside the box: a node written after layout only makes the text node taller than the panel
the game sized for English. `Talk` and `TalkSubtitle` are separate addon classes, so each gets its
own hook pair from its own vtable.

The caller's `AtkValue` strings are put back inside the same hook call, right after the addon has read
them. The addon copies the line, so the Chinese stays on screen, while everything that reads the
caller's values afterwards - the chat log's Story tab, other plugins that see the caller's data -
sees the English line. The Chinese is handed over as a plain `String` value pointing into a small
ring of unmanaged buffers, so the game never owns the memory and a pointer it keeps stays valid for
the last few lines.

`Configuration.NativeTextSharesTranslation` decides what other readers see for a Talk line. Off
(default) it restores the caller's values and rewrites the chat log message, so the game's log and
other plugins read English. On it leaves the Chinese in the caller's values and records nothing for
the chat restore, so the game's log and other readers get the translation too. The overlay is not
affected either way. Changing the choice clears the chat restore history, so a line laid out under
the old choice is not rewritten under the new one.

The game sometimes logs the text it was given: the chat log's Story tab logs NPC dialogue, and a yell
bubble with the print-to-log flag logs the yell. Restoring the caller's values does not cover a line
the game logs from the addon's own copy, so `ChatLogRestorer` listens to `IChatGui.ChatMessage` and
swaps the message back to the English line and the speaker name when they match one of the last
sixty-four lines the plugin laid out. The window is that large because the game can write a whole
conversation to the Story tab at once, several seconds after the lines were shown; a line repeated in
the conversation takes one slot, not one per appearance. A story message that matches nothing is
logged at debug level with its kind and first line, which is how a missed line is traced. The display stays Chinese and the chat log stays English; the speaker name is restored the
same way.

`NativeTextWriter` is the fallback. It writes the Chinese into the addon's text node (node 2 is the
speaker name, node 3 is the dialogue) when the hook is not in place, then `WriteFitted` shrinks the
font, one step at a time down to `MinimumFitFontSize`, until the node fits the room the addon has.
The budget is the panel's own height, not the node's, because a node that has not been laid out
reports one line and fitting a long line into that draws the remaining lines on top of each other.
The fit also turns `WordWrap` on first: without it the resize measures a long line as one line while
the game still wraps it in the box, which is the same overlap. If nothing fits, it restores the
game's text and the line falls back to the overlay, so a long translation is never drawn outside the
box. The `AtkValue` hook also skips the `PostRefresh` lookup: the values already hold Chinese, so a
second pass would miss.

`TalkSubtitle` takes the same value swap but no node write. The band draws one line and scales it
to `SubtitleMaxWidth`, so the Chinese is not pre-wrapped, the speaker name is not read, and the
`NativeTextSharesTranslation` choice does not apply: a subtitle always gets the caller's values
back. The overlay stays hidden only when the hook laid that English line out; with no hook, no
data, or a line it left English, the overlay draws the line.

The line is wrapped to the Talk text area's width before the game reads the values, by
`CjkLineWrapper`. The Chinese data is broken for the Chinese client's box, and with the CJK font the
glyphs are a little wider, so the game breaks the last character of a data line onto the next
rendered line; a closing mark such as the full stop then starts a line, which Chinese typesetting
does not do. The wrapper measures each candidate line with the Talk text node's own font, wraps to
that width, never starts a line with a closing mark and never ends one with an opening mark, and
keeps the data's own paragraph breaks. When the last line would hold less than a quarter of the
width, it is rebalanced with the line before it, so one character and its mark never sit alone. A box
that is not laid out yet leaves the line alone.

The name field shows the Chinese name with the English name in brackets, the same shape the overlay
and the nameplates use. `TalkHandler.FormatSpeakerName` builds that string, and a masked name such
as `???` stays masked. An empty translation is never written into the values, because an empty
string would blank the dialogue box. A debug log of the name, text, and box heights is written once
per laid-out line, so a box taller than its text can be seen in the log instead of guessed at: a box
that keeps the English height leaves blank space above a shorter translated line, because the box
grows upward.

Do not grow the dialogue panel from the plugin. The game recomputes it, and chasing the layout is
the same fight that damaged the nameplate bake atlas.

The selection dialogs (`SelectYesno`, `SelectString`, `SelectIconString`,
`SelectOk`, and `CutSceneSelectString`) have their own switch,
`Configuration.ShowInSelectionDialogs`, independent of Talk. Their handler
walks the addon's text nodes, looks each visible string up in the store, and
writes the Chinese back into the same node. There is no overlay for these
surfaces. `IsSelectionDialogEnabled` selects which of the five dialogs are
translated. Turning the switch off calls `RestoreAll` so an open dialog gets the
game's text back. The `Addon` sheet supplies short UI labels such as `Yes.`,
`No.`, `OK`, and `Cancel`. A cutscene choice is a `VOICEMAN` script line, and one
English option can sit in several scenes. The handler takes the scene from the live
cutscene task, or, when that task is gone, from another option in the same dialog
whose script key names exactly one scene. When one English option is used by many
event scripts, the handler also collects the active Lua handler's `LuaKey` and the
script keys the dialog exposes, and the store keeps candidates from the same script.

An `Addon` prompt can hold a runtime value, such as the gil amount in the retainer menu. The store
matches those rows as templates: the live line fills the wildcards. DataPrep also converts the
Chinese row to the macro string Lumina parses, and the handler evaluates it with the captured
values. `Format` and `ZeroPaddedValue` keep the value the live line showed, including thousands
separators, so the Chinese prompt shows the real amount. The client draws a name from another
sheet with `EnNoun`, which the template builder treats as a wildcard like `Sheet`.

More than one row can match a prompt. A row that draws a word with a conditional macro matches
almost any text in that place. Templates are ordered so the one with the most literal text is
tried first; it names the prompt most exactly.

The dialog is laid out for the line count of the English text. When the Chinese prompt has more
lines, the handler joins them into one line and writes it only when the joined line measures no
wider or taller than the node the game sized for the English text. Otherwise the extra line keeps
its break and can reach over whatever sits below the prompt, such as the item icon in an exchange
dialog.

A date or clock prompt works the other way round. The Chinese row starts with a `Time` tag that
fills the date and clock values and draws nothing, then reads them back as
`TopLevelParameter(n)`. The game keeps the timestamp to itself, so the handler reads the date and
clock from the live line and fills the slots itself. The forms the Japanese client draws (`年月日`,
`時`/`分`, `午前`/`午後`) are read too. A value it cannot read leaves the English line in place.

The handler no longer skips a node for having no Latin letter. A Japanese prompt is looked up like
any other text; a miss leaves it alone. The dialog's `AtkValues` are logged once per dialog at
debug level, because the exact index mapping to `IntegerParameter(n)` still needs an in-game
check.

A row can also draw a name from another sheet, such as the retainer's class in
`Sheet(ClassJob,IntegerParameter(1),0)`. The English line shows the name the game chose, so the
handler translates that name through the store's name and Addon label lookups. The class names
come from the `Addon` sheet, which carries the client's own class list. A name the store does not
know leaves the English line in place.

- Node 2 is the speaker name and node 3 is the dialogue in the `Talk` addon, confirmed from the
  live addon log. These are the fallback write targets, used when the `AtkValue` hook could not be
  placed. Re-confirm with the `Native text` sub-tab of the Testing tab (testing builds only)
  before changing either constant.
- The game's font has no complete Chinese glyph set. Without a font pack or a mod the line shows
  the missing-glyph character, so the settings section warns and links the README font setup. A
  release build cannot tell whether the font it writes with can draw the characters. A dev build
  can: `GlyphProbe` reads the game's font manager, and the Testing tab reports the characters a
  loaded font has no glyph for.
- A write saves the game's text, colors, flags, and node size for that node first. `RestoreAll`
  puts them back when the setting is turned off and on plugin unload. A second write must not save
  Chinese as the original, so the writer only saves when the node does not already hold text this
  writer put there.
- The game redraws the line while its text stream runs, so a write on `PostSetup` or `PostRefresh`
  lasts about one frame and the English comes back. `TalkHandler` therefore calls
  `NativeTextWriter.Reapply` on every `PreDraw`, which puts the Chinese back. This is the one
  guarded per-frame write: `Reapply` compares `NodeText.AsSpan()` with the bytes it last wrote, so a
  matching node costs no allocation and no write. Do not replace it with a one-shot write.
- An untranslated line, a missing store, or a failed write calls `Forget`, which drops the saved
  node without writing it. Without that, `Reapply` would keep the previous line's Chinese over the
  game's new English line.
- `Talk` writes text nodes and reapplies them every frame. `TalkSubtitle` writes no node, as above.
  A subtitle with no hook or no data falls back to the overlay, and a line with no translation
  stays English. The two addons are separate classes, so each vtable is hooked on its own. A
  subtitle layout logs the band's scale and width once per line (native text log group), so an
  in-game run can tell whether a long line scales or overflows.

#### The game font

`FontReplacementService` lets the plugin supply the Chinese font itself, so native mode does not
need Penumbra or TexTools. Off by default. The setting is on the General tab, because the game
font is shared by every surface and by the rest of the game.

- The user points the setting at a `.ttmp2` font mod or a folder, or picks one with the file
  dialog. `FontPackExtractor` copies the font files into `<plugin config>/fonts`; the pack itself
  is only read. The plugin ships no font data.
- A pack must replace all 25 font files the global client loads. An `.fdt` left at its original
  would point into an atlas that was re-laid out, so a partial pack is refused and the missing
  names are listed. A pack for the Chinese client replaces `chnaxis_*` and `font_chn_*` instead;
  that pack is refused with a message naming the `(Global)` version, because the global client
  loads none of those files and the game font would stay unchanged.
- `FontReplacementService` hooks `ReadSqPack` and reads the game's `ReadFile` by signature. When a
  resource handle asks for a replaced path, it switches the file descriptor to
  `LoadUnpackedResource`, writes the wide path, and lets the game read the file. Every other path
  goes to the original. The approach comes from the MIT VFXEditor code; see
  `THIRD-PARTY-NOTICES.md`.
- Installing and removing the replacement asks the game to unload and rebuild its fonts through
  `AtkModule` virtual slot 43, the same call Penumbra's "Reload Fonts" button makes. ClientStructs
  does not name the slot, so it has to be re-checked after a game patch.
- At apply time the service asks Penumbra over IPC (`ResolvePlayerPath`, then
  `ResolveDefaultPath`) whether a font mod already resolves any of the paths. If it does, the
  built-in font stays off. A descriptor another plugin already redirected is left alone.

### Chat bubbles

`Configuration.ShowInChatBubbles` turns on chat bubble translation. Alpha and off by default, because
the Chinese goes into the game's own bubble and needs a Chinese font. `ChatBubbleHandler` covers the two
things that feed the `_MiniTalk` addon:

- **Yells.** An `NpcYell` row whose output type has the balloon flag. The game starts the bubble
  through `NpcYellBalloon.OpenBalloon`, so the handler hooks that call and replaces the text before
  the bubble is measured. The game then sizes the bubble for the Chinese, and nothing has to be
  reapplied. The row id comes from `NpcYell.Instance().YellInfo`, matched by the yell's own message,
  so two yells that share an English line still resolve. A yell whose output type has only the
  battle talk flag is not touched, so the two surfaces never fight over one line.
- **Default balloons.** An `ENpcBase.Balloon` row. There is no function to hook, so the handler
  translates the bubble's `TalkBubbleEntry.BubbleTextNode` in place. The original text and sizes are
  kept and put back when the setting is turned off and on unload, but **not** on `PostHide`: a bubble
  stream hides and shows the addon between lines, and restoring there put the English back only for
  the next show to translate it again. Every one of those writes rebuilds the node's font cache,
  which showed up as a hitch each time a bubble appeared or vanished. The next line's English text
  replaces the Chinese on its own, so nothing stale is shown.

  Sizing is `BubbleLayout.FitTo`, and it never touches the text node. The node's width and flags
  are what the game wraps at; changing either makes the game write the line back with hard breaks,
  and the next lookup then sees fragments instead of the line. Instead the background is fitted to
  the text: the game drew the English line plus the margin the balloon keeps, so a background that
  holds that line gives the margin to keep. The margin is trusted only when it is a real inset:
  larger than zero and no wider than the nine-grid's fixed corners. A background outside that range
  is holding another line or has not been laid out for this one, and then the corners are the
  padding. The target is always `chinese + padding`, so a shorter translation shrinks and a longer
  one grows. The corners are a fallback, not a floor: applying them to a line that already fits is
  what made the bubble taller than its text (issue 223).

  Width grows to both sides, so the tail the game placed under the text stays in the middle, and
  shrinks from the right, so the left-aligned text keeps its left edge. Height keeps the bottom
  edge, because the text is bottom-aligned and the tail hangs under it. The nine-grid and the
  bubble's res node follow the same size, so they never disagree. The node's size and position come
  back with the game's text on the setting and on unload.

  The fit rule is pure (`BubbleGeometry`); `BubbleLayout` reads the nodes and writes the result.
  `BubbleGeometryTests` covers the rule and `BubbleLayoutTests` runs the writes against allocated
  ATK nodes, so both halves are tested without the game.

  The addon fills the text node itself, and it does that after `PostSetup`, so the node is empty
  when the bubble starts. The handler therefore looks at the nodes on each draw: a hash of the
  node's bytes is compared with the last text seen, and only a text it has not seen is looked up.
  A line with no translation is remembered, so it is not retried on every frame. The same English
  line coming back, which the game does while a balloon's text stream runs, is rewritten from the
  saved Chinese. A different line landing in the same node drops the old translation but **keeps
  the layout the game gave the new line**; restoring the previous line's sizes before capturing put
  back a background that did not hold the new text, which is how a short line inherited a long
  line's empty box. A yell bubble is recognised by the bytes this handler passed to the balloon
  start: `RecentBubbleTexts` keeps the last eight Chinese yell lines, so the sync does not look a
  yell's Chinese up as English and leaves the game's own size alone. This is the same guarded
  per-frame compare the Talk and item surfaces use, without the allocation a lookup would need.

Bubbles carry no speaker name, so the name lookup and the shared-script-line speaker pick are not
used here. The bubble geometry is logged once per translated bubble, because the bubble layout is
the part that has to be checked in game. A bubble that the handler cannot translate keeps the
English text; there is no overlay for this surface.

### Battle talk

`Configuration.ShowInBattleTalk` turns on battle talk: the large line a boss, dungeon narration, or a
duty NPC shows near the top of the screen. Alpha and off by default, because it needs a Chinese font.
The surface is named **Battle talk** in the components tab, next to the font setup link.

The game shows the line through three virtual functions on `UIModule`: `ShowBattleTalk`,
`ShowBattleTalkImage`, and `ShowBattleTalkSound` (virtual table indices 198, 199, and 201, from
ClientStructs' `UIModuleInterface`). Each takes the name and the text as UTF-8 pointers.
`BattleTalkHandler` hooks all three and swaps those pointers for the Chinese, then calls the original.
The game then wraps and sizes its own box for the translation, exactly like the `AtkValue` approach
native Talk mode uses.

Nothing is written into a text node and nothing is restored. A line with no translation is passed
through unchanged, so the game's English stays. The replaced strings live in `Marshal.AllocHGlobal`
buffers, and the last eight are kept: the game copies the line into its own fields, but a copy that
lands after the call returns would otherwise read freed memory. Every hook body is wrapped in
try/catch that logs and calls the original, because an exception inside the game's call is not an
option.

Rows are already indexed. `NpcYell` holds boss and enemy shouts whose output type includes the battle
talk flag; the handler reads `NpcYell.Instance()->YellInfo` for the row id, the name, the
`HideSpeakerName` flag, and the four message parameters. `InstanceContentTextData` holds dungeon
narration and `ContentTalk` holds duty NPC lines, and both match on text alone. The name goes through
`TranslationStore.TryGetName` and `TalkHandler.FormatSpeakerName`, so it follows `ShowSpeakerName` and
keeps a masked `???` masked.

The id comes first: a yell with a row id passes `DialogueRowId("NpcYell", rowId, "Text")`, which
separates two yells with the same English line. Everything else falls back to the normalized text.

`ChatBubbleHandler` filters the same `NpcYell` rows on the balloon flag, so a yell with both flags is
written once per surface and neither handler fights the other. A line turned off mid-display stays
Chinese until its box hides; the next line is English.

### Native nameplates

`Configuration.ShowTargetName` turns on name translation in the game's own nameplates. Every
visible nameplate is translated, not only the target. This covers NPCs and objects such as a
summoning bell, company chest, or door. `NamePlateNameWriter` replaces each plate's name through
Dalamud's nameplate API (`INamePlateGui.OnNamePlateUpdate`). It is the only surface for these
names: there is no overlay window for them.

- The event fires inside the nameplate addon's update, before the game sets the plate's text.
  The game then measures our text and bakes the plate at that size, so a wider Chinese name is
  not clipped.
- Do not write the text node after the addon update and mark the plate `NeedsToBeBaked`. The
  bake keeps the size the game gave the English name, so the sides of a longer name are cut off.
  Do not change `NamePlateObject.TextW` or `TextH` to fit: the game resets them while it
  measures, and the plates share one bake atlas, so the fight corrupts names on other plates,
  including the player's.
- A plate draws one line, so the English name follows the Chinese name in brackets when it is
  shown.
- The event only passes plates with a new or changed name, so this is not a per-frame path. The
  name is looked up in the store each time.
- A plate slot carries no object id, so the translation follows the name text. Two visible objects
  with the same name get the same Chinese name.
- `Enable` and `Disable` subscribe and unsubscribe, then call `RequestRedraw`, which makes the
  game lay out every visible plate again. That shows the Chinese names at once, and puts the
  English names back when the mode is turned off.
- Player characters are skipped. Nameplates must be visible in the game settings; a hidden plate
  is left alone.

### Item names

`ItemNameHandler` reads the item under the mouse and shows its Chinese name in one of two modes.
`ItemNameNativeMode` picks the mode. The English name comes from `Item`; the store's name lookup
gives the Chinese.

- **Overlay** (default). `ItemNameWindow` draws the name above the cursor, so the tooltip stays
  as the game drew it.
- **Native.** The handler writes the name into the game's own tooltip: `ItemDetail`, field
  `ItemNameText`. The game then draws the Chinese with its own font, size, and color, so a font
  mod is required. The write goes through `NativeTextWriter`, the same path the selection dialogs
  use, so the English name is saved and can be put back.
- The handler writes on `HoveredItemChanged` and again when the tooltip addon fires `PostSetup`
  or `PostRefresh`. The hover can change before the addon has its new text, and a write that comes
  first is replaced by the game's own English.
- The tooltip writes its name again while it is open, so a write lasts one frame. Like the Talk
  window, the handler calls `ReapplyAt` on every tooltip `PreDraw`. That path compares the node
  bytes with the last write, so matching text costs no allocation.
- The tooltip sizes the name node to the English name, so a longer Chinese name wraps into lines
  of a few characters. `FitTooltipName` clears `WordWrap` and `Ellipsis` and grows the width to the
  measured text. The height goes back to what the game gave the English name and stays there. It
  runs after every write and on every reapply, because the game can reset the size.
- The name is one line when it fits the tooltip. When the Chinese and English names are wider than
  the space left in the tooltip window, the English name moves to a second line. The height does
  not change: the tooltip draws the line at the bottom of the node, so the Chinese line stacks
  upward into the header and the English line keeps the place where the game drew it. Growing the
  node instead pushes the English line down over the item description. The window width comes from
  the addon's root node; before the addon is up, the name stays on one line.
- A tooltip with no name node, such as one that has not been set up yet, falls back to the overlay.
  Native mode never shows nothing.
- When the item changes, the handler forgets the saved text for the old node. When the setting is
  turned off, it puts the saved English name back, along with the node's saved width and height.
  Without the size, the English name stays at the position the Chinese name needed. `PostHide`
  and `PreFinalize` forget the node instead of writing it, because a freed node can belong to
  another addon by then.
- The node address is checked against the live addon before a restore, so a stale address is never
  written.

`ShowOriginal` puts the English name in brackets after the Chinese name, the same shape the
nameplates use; off shows Chinese only. The overlay keeps the name on one line. Native mode moves
the English name to a second line when both names are too wide for the tooltip.

### Runtime data updates

The plugin reads artifacts from its config directory first and falls back to the copies embedded in
the assembly. `TranslationDataUpdater` fills that directory from the `data` GitHub release, which
`.github/workflows/data.yml` rebuilds from the datamining CSVs. The workflow runs once a day at
00:00 UTC+8 and starts the build only when the CN, TC, or English dump commit moved since the
published manifest, so an unchanged day costs a few seconds.

A release build carries no embedded artifacts, because they are about 60 MB and not committed. A
fresh install therefore has no data until the first download. `Plugin.Initialize` starts
`StartUpdate` when no data file exists and the build has no embedded copy, and also on later loads
while **Download data updates on load** is on. The updater compares the published version with the
installed one, so a load with current data does one manifest request and no download. A download
runs in the background; the store already in memory keeps serving until the new data is indexed.

- The release is tagged `data` and holds `manifest.json`, `Sinophone.cn.json.gz`, and
  `Sinophone.tc.json.gz`. The base URL is hardcoded in `TranslationDataUpdater`; renaming the
  repository breaks every installed copy.
- The manifest `version` is a hash of the translated sheets and script lines, not a date.
  `GeneratedAt` and `GameVersion` are left out, because they change on every build. The data
  workflow compares the new version with the published one. When they match it replaces only the
  manifest, which records the source commits, and leaves the data files alone, so clients do not
  download again. That path keeps the **published checksums**: a rebuild produces different bytes
  even when the content hash matches, because the artifact carries its generation time, so a freshly
  built manifest would describe files that were never uploaded and every client would reject the
  download. A verification step reads the manifest and the data files back through the API and fails
  the job when a checksum disagrees. `Configuration.LastDataVersion` records what is installed.
- `Scripts` is optional. Old readers ignore it, and a missing section means no script lines, so
  the artifact format version stays at 1.
- Both variants are downloaded by default. Turning off **Download both language variants**
  downloads only the selected one, which keeps an update small; the fallback then has no other
  data to read. `HasSimplifiedData` and `HasTraditionalData` say which files match
  `LastDataVersion`, and `OpenData` ignores a file without the matching flag, so a file left over
  from an older version is never mixed with current data.
- Loading the second variant is opt-in (`UseFallbackVariant`, off by default). Indexing both roughly
  doubles load time and memory, and the other variant only fills a few lines: with Traditional
  selected, the Simplified artifact adds 6 script lines the Traditional dump lacks. Keep the default
  off and let users trade startup time for those lines.
- `manifest.json` has an optional checksum per artifact. Downloads go to a `.download` temp file and
  are only moved into place after the gzip, artifact format version, and sheet count are validated.
  Keep that order: a partial file must never replace working data.
- A completed download only raises a flag. `Plugin.OnTick` rebuilds the store on the game thread.
  Do not touch windows, fonts, or Lumina sheets from the updater's worker task.
- A load is split so no step runs in one frame. `TranslationStore.Parse` reads and deserializes the
  artifacts on a worker task. `SheetExtraction` then reads the live Lumina sheets from
  `Plugin.OnTick` in slices of about 4 ms per frame, because Dalamud forbids sheet reads off the
  game thread. `TranslationStore.Build` indexes the extracted English text on a worker task, and
  `Plugin` swaps the store in only after it and the CJK font atlas are ready. Measured on
  2026-11-01 data: parse is about 1.1 s, the sheet reads are about 0.4 s in total, and indexing
  about 1 s with one variant. Do not move the sheet reads into the worker task; that breaks the
  rule above.
- The finished index is cached in `Sinophone.index.bin` in the config directory, including the script
  lines that share one English key, so the speaker and cutscene steps still work on a cache hit. The
  next load reads
  that file instead of parsing the artifact, reading the live sheets, and indexing. Each index
  writes its own part of the cache next to the collections it describes; `TranslationStoreCache`
  writes only the facade's own tables, styles, and sheet slots. The private cache format version
  is bumped when a part's shape changes. A cache is used
  only when its key matches: variant, fallback setting, conversion, game data version, the identity
  of each artifact (file size and write time, or a SHA-256 of the embedded copy's bytes), and
  `TranslationStore.IndexFormatVersion`. The format version is bumped by hand when the index
  entries change, because the assembly identity changes on every build and would throw the cache
  away after every plugin update.
  The key leaves out the player because conditional and `ObjectParameter` lines are resolved on
  each lookup. A miss is not an error: the store is rebuilt and the cache replaced.
- Only one parse may be in flight. A settings change or a new data file replaces the pending task,
  so an older result is dropped before it is built. `Dispose` drops it too.
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
