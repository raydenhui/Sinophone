# ClientStructs, hooks, and Lua research

What this project has learned about the game's UI structures. Read this before researching a new
surface, so you do not repeat a dead end or pick a method that already failed.

**Last checked:** 2026-11-27, against FFXIVClientStructs 7.57.0.9414, as shipped with the Dalamud
distribution pulled by `Dalamud.NET.SDK/15.0.0`. A game patch moves fields and vtable slots. When a
claim here does not match the installed assembly, the assembly wins; update this file.

## How to read the labels

- **Used** means the repo calls it today. The file is named.
- **Available** means the type or member exists in the installed FFXIVClientStructs or Dalamud, but
  the repo does not call it yet. Check the member before using it.
- **Unverified** means it was seen once, or comes from another plugin, and needs a live check.

## 1. Use the highest layer that answers the question

| Need | Use | Not |
| --- | --- | --- |
| Addon opened, refreshed, closed, drawn | Dalamud `IAddonLifecycle` | A hook on the addon's draw |
| Draw a plate name | Dalamud `INamePlateGui.OnNamePlateUpdate` | The nameplate addon's text node |
| Change a chat log line | Dalamud `IChatGui.ChatMessage` | The chat log node |
| Read a sheet | `IDataManager` | Screen text |
| Run a SeString macro | `ISeStringEvaluator` | A hand-written macro parser |
| Pre-layout text | The addon's own `VirtualTable->OnSetup` / `OnRefresh` | A hook on the UI module's draw |
| Text the game measures before it draws | `AtkValue` strings before layout | A text node write after layout |
| State the addon does not expose | `UIState`, `EventFramework` | Screen scraping |

The default for a new surface is `IAddonLifecycle` plus a text node write, or an `AtkValue` swap if
the addon exposes its line as values. Reach for a raw detour only when the Dalamud event cannot run
at the right moment.

## 2. Raw hooks in use

| Surface | Hook | Address or slot | Callback shape | File |
| --- | --- | --- | --- | --- |
| `Talk`, `TalkSubtitle` pre-layout | `AtkUnitBase.VirtualTable->OnSetup` and `->OnRefresh` | Per addon class; the pointer is read from the live addon | `void(AtkUnitBase*, uint count, AtkValue*)`, refresh returns `bool` | `Handlers/TalkHandler.cs:155` |
| Battle talk | `UIModule` vtable | indices **198** (`ShowBattleTalk`), **199** (`ShowBattleTalkImage`), **201** (`ShowBattleTalkSound`) | see below | `Handlers/BattleTalkHandler.cs:39` |
| Yell chat bubble | `NpcYellBalloon.MemberFunctionPointers.OpenBalloon` | named function pointer | see below | `Handlers/ChatBubbleHandler.cs:105` |
| Game font (dev builds only) | `ReadSqPack` | signature `40 56 41 56 48 83 EC ?? 0F BE 02` | `byte(nint, SeFileDescriptor*, int, bool)` | `Fonts/FontReplacementService.cs:31` |
| Game font | `ReadFile` | signature in `FontReplacementService.cs:32` | same shape; called, not hooked | same |
| Font reload | `AtkModule` vtable | index **43**, called as `(atkModule, false, true)` | `void(AtkModule*, bool, bool)` | `Fonts/FontReplacementService.cs:308` |

Battle talk delegate signatures, copied from the code:

```csharp
void ShowBattleTalk(UIModule*, CStringPointer name, CStringPointer text, float duration, byte style);
void ShowBattleTalkImage(UIModule*, CStringPointer name, CStringPointer text, float duration,
                         uint image, byte style, int sound, uint entityId);
void ShowBattleTalkSound(UIModule*, CStringPointer name, CStringPointer text, float duration,
                         int sound, byte style);
```

Yell balloon:

```csharp
void OpenBalloon(NpcYellBalloon* balloon, CStringPointer text, float playTime, bool softOpen,
                 float openDelay, bool printToLog, bool reducedCloseChecks, bool ignoreRangeCheck,
                 byte parentBone);
```

Notes:

- The vtable indices for battle talk and the font reload are game-version dependent. They are kept
  as named constants with a comment; if a patch breaks them, re-find the function, do not add a
  retry loop.
- `Talk` and `TalkSubtitle` have **separate** vtable entries. A hook placed for one does not serve
  the other, so the handler installs a pair per addon. The callback closes over the addon name,
  because the vtable entry is shared by every instance of that addon class.
- Do not read `addon->VirtualTable` after the addon closed. The handler installs only on
  `PostSetup`, `PostRefresh`, `PostUpdate`, and `PreDraw`.
- `ReadSqPack` runs on the game's file thread. Never throw through it; fall back to
  `hook.Original`.

## 3. Dalamud events in use

| Event | Used for | File |
| --- | --- | --- |
| `IAddonLifecycle` `PostSetup`, `PostRefresh`, `PostUpdate`, `PreDraw`, `PostHide`, `PostClose`, `PreFinalize` | Every addon surface | `Handlers/` |
| `INamePlateGui.OnNamePlateUpdate` | Native nameplates, before the plate is baked | `Handlers/NamePlateNameWriter.cs` |
| `INamePlateGui.RequestRedraw` | Apply or drop names at once | same |
| `IChatGui.ChatMessage` | Put English back into the Story tab | `Handlers/ChatLogRestorer.cs` |
| `IGameGui.HoveredItemChanged` | Item tooltip name | `Handlers/ItemNameHandler.cs` |
| `ITargetManager.Target` | Link the Talk line to the NPC's event handler | `Handlers/TalkEventReader.cs` |
| `IGameGui.GetAddonByName<AtkUnitBase>(name, 1)` | Reach an addon outside a lifecycle event | several |
| `AddonArgs.GetAddonPointer<AtkUnitBase>()` | Reach the addon inside a lifecycle event | several |

`IChatGui.ChatMessage` hands `IHandleableChatMessage`; cast to `IMutableChatMessage` to replace
`Message` and `Sender`. Check `LogKind` (`NPCDialogue`, `NPCDialogueAnnouncements`,
`SystemMessage` for the Story tab).

## 4. Addon reference

Type names below were found in the installed `FFXIVClientStructs.dll`. A type existing does not
mean its fields are named or stable; check the assembly before relying on a field.

### 4.1 `Talk` — the dialogue window

- Addon name: `Talk`. Struct: `AddonTalk` (Used).
- Text source: `AtkValue[0]` is the line, `AtkValue[1]` is the speaker name (Used).
- Text nodes: node **2** is the speaker name, node **3** is the dialogue (Used; confirmed from a
  live scan). `NativeTextWriter.TalkNameNodeId` and `TalkTextNodeId`.
- Other text nodes: fields `AtkTextNode220`, `AtkTextNode228`, `AtkTextNode238`, `AtkTextNode240`,
  `AtkTextNode248` (Used for the segment retry). The width of `AtkTextNode228` is the wrap target.
- Pre-layout write: swap `AtkValue[0]` and `[1]` before calling the original, then put the caller's
  values back after. The game measures and sizes the box for the Chinese. `SwappedValues` in
  `TalkHandler.cs:259`.
- Node write is the fallback: `NativeTextWriter.WriteFitted`, then reapply on every `PreDraw`
  because the game rewrites the node while the text stream runs.
- The line is wrapped with `CjkLineWrapper` to the width of node 228 minus one glyph, because the
  CJK glyphs are wider than the Chinese client's and the game would start a new line with a closing
  mark (`TalkHandler.WrapForTalkBox`).
- `WindowNode` and `RootNode` give the panel size for the layout log. Both may be null.

### 4.2 `TalkSubtitle` — the cutscene subtitle band

- Addon name: `TalkSubtitle`. Struct: `AddonTalkSubtitle` (Used).
- Text source: `AtkValue[0]` only. There is no name value (Used).
- Text nodes: ids **2, 3, 4**; the band carries three (Used for measurement and the layout log).
  `SubtitleFirstTextNodeId = 2`.
- Fields `SubtitleMaxWidth` and `SubtitleScale` are read for the layout log (Used). They suggest the
  band fits one line by scaling, not by growing. Do not write the nodes: the band draws the line it
  was measured with, so the `AtkValue` swap is the whole surface.
- A subtitle is not wrapped before the write. The band's own width and scale decide the fit.
- A subtitle has no speaker, so name lookup and the Chinese-name bracket do not apply.

### 4.3 `_MiniTalk` — chat bubbles

- Addon name: `_MiniTalk`. Struct: `AddonMiniTalk` (Used).
- Entries in `AddonMiniTalk.TalkBubbles`, each with `BubbleTextNode`, `BubbleNineGridNode`, and
  `BubbleResNode` (Used).
- A yell bubble is started by `NpcYellBalloon.OpenBalloon`, so the hook can translate before the
  game measures it (Used). A default `ENpcBase.Balloon` has no such call; its text node is written
  in place on every draw, guarded by a hash of the text (Used).
- Fit the background, not the text node. The node's width and flags are what the game wraps at, and
  changing them makes it write hard breaks into the line. `BubbleLayout.FitTo` changes the nine-grid
  and the bubble res node only.
- Padding rule: keep the margin the game drew for the English line only when it is a real inset
  (larger than zero and no wider than the nine-grid corners). Otherwise use the corners. This rule
  is `BubbleGeometry.Padding` and is fully tested without the game.
- Do not restore the previous line's sizes before capturing a new one. The capture is then the old
  line's background, which produced a tall empty bubble. Do not restore on `PostHide` either: the
  addon hides and shows between lines and each restore plus rewrite rebuilds the font cache.
- Do not write a text node for a line with no translation; remember it so it is not retried every
  frame.

### 4.4 `ItemDetail` — the item tooltip

- Addon name: `ItemDetail`. Struct: `AddonItemDetail` (Used).
- Name node: `AddonItemDetail.ItemNameText` (Used). A missing node falls back to the overlay.
- Hovered item id: `IGameGui.HoveredItemChanged`. Raw id offsets (Used):
  - `>= 2,000,000` is an event item, which lives in `EventItem`, not `Item` (no lookup today).
  - `>= 1,000,000` is a high-quality item; subtract the offset.
  - `>= 500,000` is a collectible item; subtract the offset.
- The tooltip sizes the name node to the English name. Clear `WordWrap` and `Ellipsis`, grow the
  width to the measured text, and put the height back to the size the game gave the English name.
  The header draws at the bottom of the node, so an extra line stacks upward; growing the height
  pushes the English line down over the description.
- `GetTextDrawSize` measures with the node's own font. `AtkTextMeasure` supplies a null-terminated
  UTF-8 buffer; a buffer without the terminator makes the game read past the text.
- The tooltip rewrites its own name while open, so reapply on `PreDraw`, the same guarded per-frame
  write as `Talk`.
- On hide and finalize, drop the saved text; the node may already belong to another addon.

### 4.5 Selection dialogs

Addon names: `SelectYesno`, `SelectString`, `SelectIconString`, `SelectOk`, `CutSceneSelectString`
(Used). Text lives in text nodes, not values.

| Addon | Typed fields used |
| --- | --- |
| `SelectYesno` | `PromptText`, `YesButton`, `NoButton`, `AtkComponentButton238` |
| `SelectOk` | `PromptText`, `OkButton` |
| `SelectString` / `SelectIconString` | `PopupMenu.PopupMenu.List` item renderers |
| `CutSceneSelectString` | `TextLabel->AtkTextNode`, `OptionList` |

- Node discovery walks `RootNode`, `UldManager.NodeList`, and the typed fields (Used). The cutscene
  addon can keep its nodes only in `UldManager`.
- The game lays the dialog out for the English line count. If the Chinese needs more lines,
  `FitPromptLines` joins them into one line and writes it only when it fits the node the game sized.
  Otherwise the extra line reaches over what sits below the prompt.
- `PreDraw` reapplies, then retries up to 12 times, because `CutSceneSelectString` fills its options
  after `PostSetup`. Clear on `PreFinalize`, not on the earlier hide/close, because nodes are reused.

### 4.6 Journal family — not implemented, research targets

The installed ClientStructs has these types (Available, not used):

- `AddonJournalDetail`, `AddonJournalAccept`, `AddonJournalResult`.
- There is no `AddonJournal` type. The main journal window may still have an addon name of
  `Journal`; confirm at runtime.
- Agents: `AgentQuestJournal` with `IsAddonReady`, `IsAddonShown`, `IsAddonHidden`,
  `GetAddonStatus`, `ShowAddon`, `HideAddon`, `FocusAddon`, `OpenForQuest`, `ShowOnMap`.
  `AgentJournalAccept` and `AgentJournalResult` exist with the usual agent show/hide helpers.
- Component: `AtkComponentJournalCanvas268` and `AtkUldComponentDataJournalCanvas`. The journal
  uses a canvas-like list of text that may not be plain child nodes.

Open questions to answer in the game before writing a handler:

1. Which node id or component field holds the visible quest name, description, objective, reward
   names, and the accept/complete body?
2. Does the journal scroll by moving nodes or by rewriting them? A write must survive a scroll.
3. Does the game clip, reflow, or grow when the Chinese is longer? Do not change node sizes before
   watching the game's own behavior.
4. Does the two-column layout survive a node write?
5. Do `JournalAccept` / `JournalResult` expose the quest id and reward rows through `AtkValue`s? If
   they do, prefer the value swap.
6. Which `Description` field does the addon draw: `TextLong`, `TextShort`, or `TextCommentary`?
   Do not index all three before this is known.

If a native write cannot be made safe, an overlay anchored to the journal text area is a valid
fallback. The requirement is that the text displays correctly, not that it is native.

### 4.7 Available but not used

These exist in the installed assembly and may answer a future need. Check each field before use;
none of them is covered by a test today.

| API | Could be used for | Caution |
| --- | --- | --- |
| `AtkUnitBase.VirtualTable->OnSetup` / `OnRefresh` on any addon | The same pre-layout swap `Talk` uses, for a new addon | The vtable entry is shared by every instance of the addon class |
| `AgentQuestJournal` (`IsAddonReady`, `ShowAddon`, `HideAddon`, `OpenForQuest`, `GetAddonStatus`, `FocusAddon`, `ShowOnMap`) | Knowing whether the journal is up, and which quest it opened | The agent may open a different addon than `JournalDetail` |
| `AgentJournalAccept`, `AgentJournalResult` | The accept/complete windows | Their own fields and callbacks are not mapped yet |
| `AtkComponentJournalCanvas268`, `AtkUldComponentDataJournalCanvas` | Journal text that is drawn in a canvas, not as ordinary child nodes | Unverified; scan the component before writing |
| `IUnlockState` | Reading unlock state through Dalamud instead of `UIState` pointers | The plugin currently reads `UIState.IsChocoboTaxiStandUnlocked` |
| `ISeStringEvaluator` | Running a SeString macro locally | Already used by the selection template path |
| `IClientState`, `IObjectTable` | Current player, target, and object identity | Do not read them per frame if a cached value is enough |
| `AddonJournalDetail.GetTextNodeById` and the other `AtkUnitBase` helpers | Node access once the journal is probed | Node ids are unknown; do not guess one |
| `NpcYellBalloon` members other than `OpenBalloon` | Bubble lifetime and close behavior | The hook covers the line; reading more state needs a live check |
| `AgentItemDetail.ItemId`, `IGameGui.HoveredItem` | The tooltip's item row id directly | The item handler still goes id -> English name -> text; a row-id lookup would skip that |
| `InstanceContentTextClip.ResolvedRowId` | A subtitle row id without text matching | Confirm when the clip is readable during `TalkSubtitle` |
| `Character.NameId` | The `BNpcName` row behind a nameplate | `BattleNpc.BaseId` is the model id, not the name id |

## 5. Event framework and Lua text

`TalkEventReader` reads `EventFramework.Instance()` when a line is resolved (Used). Everything here
is optional: if a structure moves, the lookup falls back to text and the English stays.

### 5.1 What is read

| Field | Use |
| --- | --- |
| `framework->Scene`, `SceneGameObjectId`, `SceneData` | The current scene, for the probe and the handler fallback |
| `framework->EventState1`, `EventState2` | The interaction the player is in; each holds an `EventId` and object id |
| `framework->EventHandlerModule.EventHandlerMap` | Every active handler |
| `handler->Info.EventId` | Packed `(content << 16) | entry` |
| `handler->EventGameObject`, `SceneGameObject` | Links a handler to the target the player talked to |
| `handler->Scene` | Fallback filter when no event state is active; `-1` for many handlers |
| `UIState.QuestTodoList.Todo.TrackedQuests` | Quest ids that narrow a shared script line |
| `UIState.IsChocoboTaxiStandUnlocked(id)` | Picks the `_UNLOCK` stand greeting; only read when the line has a stand variant |
| `PlayCutSceneTask.CutscenePath` under `EventSceneModule.TaskManager.Tasks` | The scene number, from the last path segment such as `VoiceMan_06005` |

Handler selection (Used):

1. If the player has a target, use the handler whose `EventGameObject` or `SceneGameObject` is that
   address. This is the only link that survives the event state being cleared when `Talk` opens.
2. Otherwise use handlers whose event id is in `EventState1` or `EventState2`.
3. If neither state holds an id, fall back to `handler->Scene == framework->Scene`.
4. A row is used only when exactly one handler maps to a known sheet row.

### 5.2 Lua handler

Sinophone does not call the game's Lua. It reads the state the Lua event handler leaves behind; the
text map is the only Lua data used. Do not look for a Lua function to call before checking whether
`LuaTexts` or the dump already has the answer.

`LuaEventHandler` is the handler type for Quest, CustomTalk, and GuildLeveAssignment events (Used).
It exposes:

- `LuaKey` — the script name, such as `REGUNI5BASEOMICRON_00760`.
- `LuaTexts` — a map of `(RowId, Key, Value)`, where `Key` is the game's own text key and `Value`
  is the line the client displays.

Rules learned:

- Cast to `LuaEventHandler` **only** for `Quest`, `CustomTalk`, and `GuildLeveAssignment`. Other
  handler kinds do not derive from it and the cast reads past the allocation. Add a content kind
  only after confirming its handler type.
- A Lua key can be reused for another menu line, so the key alone is not identity. Keep the key with
  its live `RowId` and the English line from the dump.
- The live map only exposes lines the script currently holds. When it is empty, the text fallback is
  the only path.
- `LuaTexts.Value` may contain dynamic spans the game marks. `ScriptTextTemplate` builds the
  template; do not compare raw strings.
- Fallback scan: when no active handler has the line, walk every handler in the map and collect keys
  whose text matches. This keeps the script family when the active filter misses the quest. It is
  more expensive, so it runs only when needed.

### 5.3 Key grammar in the dumps

| Pattern | Meaning |
| --- | --- |
| `TEXT_<script>_<number>_<SPEAKER>_...` | The name part of the key, such as `..._JULLUS` or `..._GRAHATIA`; a two-part name is matched as a whole, without punctuation |
| `TEXT_VOICEMAN_06005_...` | Cutscene **6005**. The number is the scene, not the task id |
| `..._UNLOCK...` | The chocobo stand variant shown when the stand is locked |
| `TEXT_<script>_...` without a speaker or scene | Must not be used to pick between different lines |

- A cutscene task id is **not** a scene number. `PlayCutSceneTask.Id` would pick the wrong line.
- A path with no number leaves the scene at 0, so a `VOICEMAN` key cannot be narrowed by it.
- A selection dialog can outlive the cutscene task. Every option comes from one scene, so an option
  whose dump key names exactly one scene gives the scene for its siblings.
- A stand greeting is the same English line for a newly unlocked stand and for every later visit.
  Only the stand's own row id and the `_UNLOCK` token separate them.

### 5.4 Content id to sheet row

`TalkEventSurfaces.TryGetRow` (Used):

| `EventHandlerContent` | Sheet | Row id |
| --- | --- | --- |
| `DefaultTalk` | `DefaultTalk` | `EventId.Id` — the packed id, **not** `EntryId` |
| `GuildLeveAssignment` | `GuildleveAssignmentTalk` | `EntryId` |
| `CraftLeveClient` | `CraftLeveTalk` | `EntryId` |
| `GimmickAccessor`, `GimmickRect` | `GimmickTalk` | `EntryId` |
| `GimmickBill` | `GimmickBill` | `EntryId` |
| `GoldSaucerTalk` | `GoldSaucerTalk` | `EntryId` |
| `ChocoboTaxiStand` | none | The stand state is used, not a row |
| `Quest`, `CustomTalk` | script text keys | No direct row |
| `ContentNpc` | `ContentTalk` links | No direct line |

`DefaultTalk` is the trap: its `EntryId` is only the low half of the row id. Use `EventId.Id`.

### 5.5 What was measured about ids (2026-10-29)

These numbers come from a research pass over the CN/TC/EN dumps. They are evidence for choosing
lookup keys, not a promise that a key is reachable at runtime.

- Script keys are `TEXT_<SCRIPT>_<5-digit>_<REST>`. The 5-digit number is the `Quest` row id minus
  65,536. The join `Quest.Id` ↔ dump file name ↔ `Quest` row is total: 5,397 of 5,397 verified.
- Exact-text lookup over the quest dumps is ambiguous for **4.28 %** of lines: 1,453 English strings
  map to more than one Chinese string, covering 9,449 of 220,728 keys. `DefaultTalk` adds only about
  50 ambiguous strings out of 14,283.
- The name segment in a script key matches an `ENpcResident` name for **61.3 %** of keys, and it
  separates the variants for 533 of the 1,453 ambiguous strings. `Talk` already supplies the speaker
  at `AtkValue[1]`, so this path needs no native read.
- `EventHandler.Info.EventId` identifies the **conversation**, not the line. One `DefaultTalk` row
  covers several `Text` slots and the addon exposes no line cursor. Per-line identity needs the yield
  id, which exists only as a parameter of `ProcessEventYield` /
  `PacketDispatcher.HandleEventYieldPacket` (Dalamud hooks the latter), so it needs a hook rather
  than a state read.
- Instanced-content subtitles expose a real row id: `InstanceContentTextClip.ResolvedRowId` with
  `AddonId` for `TalkSubtitle`, plus `ResolvedBNpcNameRowId`.
- Name rows by object kind: EventNpc `BaseId` is the `ENpcResident` / `ENpcBase` row, EventObj
  `BaseId` is the `EObj` row, but BattleNpc `BaseId` is the **model** id; its name id is
  `Character.NameId` (`BNpcName`).
- Item ids already exist: `IGameGui.HoveredItem`, fed by `AgentItemDetail.ItemId`. The artifact
  stores `Item` rows by id, so a direct row-id lookup is possible instead of name-then-text.
- There is no offline `(questId, scene) -> text key` map. Scene numbers live only in the Lua bytecode
  (`.luab`), so building one is a per-patch decompiler project, not a data-prep step.

## 6. ATK node and value reference

### 6.1 Values

- `AtkValue.Type` must be checked (`IsStringValue`) before reading `String`; the game passes other
  types on some events.
- Read the whole string with `MemoryHelper.ReadSeStringAsString(..., 8192, ...)` and drop macros
  after. Stopping at the first macro cut a line at its italics and lost the line.
- A plain `String` value is caller-owned. For a swap that must be restored, allocate UTF-8 in
  unmanaged memory and keep the pointer alive for several lines (`AllocateValueBuffer`). For a
  deliberate share, `SetManagedString` leaves Chinese for other readers.
- Save the caller's `AtkValue` before changing it and put it back in a `finally`, after the original
  call returns. The game may keep reading values after the line is drawn.

### 6.2 Text nodes

- `AtkUnitBase.GetTextNodeById(id)` is the simple route. `RootNode` and `UldManager.NodeList` cover
  addons that do not expose a typed field.
- `AtkTextNode.NodeText` is a null-terminated SeString. `SetText` writes it;
  `ResizeNodeForCurrentText` fits the node to the new text.
- `GetTextDrawSize(&width, &height, utf8Pointer)` measures with the node's font. The pointer must be
  null-terminated. Use `AtkTextMeasure`.
- `TextFlags.WordWrap` and `TextFlags.Ellipsis` change how the game draws a line. Clear them only
  where the surface draws one line (item tooltip), never on a chat bubble.
- `FontType` selects one of the six game fonts. `node->SetFont(FontType)` exists and is used by the
  dev probe.
- Node ids are per addon. Node 3 in `Talk` is the line; the same id in another addon may be empty or
  another field. Scan before writing.

### 6.3 Measurement and wrapping

- `AtkTextMeasure.Measure` uses the node's own font, so the answer matches the game.
- `CjkLineWrapper` wraps Chinese and keeps closing marks off a line start and opening marks off a
  line end. The game's own wrapping does not apply those rules.
- Leave one glyph of slack when wrapping a box; a line measured to the exact width is the line the
  game breaks again.
- For a selection prompt, compare the joined line against the width and height the game gave the
  English node. Do not assume the node can grow.

## 7. Fonts

- The global client loads 25 files under `common/font/`: 23 `.fdt` font tables and the shared
  `font1.tex` / `font2.tex` atlases. The list is `FontPackFiles.Required`. A pack missing one file
  is refused, because a left-out `.fdt` points into a re-laid-out atlas.
- A Chinese-client pack replaces `chnaxis_*` and `font_chn_*`. The global client never loads those,
  so the pack is detected and refused with a hint to use its `(Global)` version.
- Redirection: hook `ReadSqPack`, and only when the descriptor asks for a font path this service
  supplies:
  1. set `FileMode = LoadUnpackedResource`;
  2. write the UTF-16 path into `descriptor->FilePath` at offset `0x70` and into the file interface
     at offset `0x30`, path at `0x21`;
  3. call the game's own `ReadFile` with the same descriptor.
  The descriptor is 0x278 bytes; `ResourceHandle` is at `0x50`. Every other path calls
  `hook.Original`. The offsets and signatures follow the MIT VFXEditor code at commit `8be61a5`;
  see `THIRD-PARTY-NOTICES.md`.
- Reload: `AtkModule` vtable slot **43** as `(atkModule, false, true)`. Without it the old font
  stays until the client restarts.
- Penumbra coexistence: ask `Penumbra.ResolvePlayerPath`, then `Penumbra.ResolveDefaultPath` over
  IPC. If either resolves a font path to a different file, leave the font to Penumbra. Do not copy
  Penumbra code.
- Glyph check: `AtkStage.Instance()->AtkFontManager->FontSets[(int)fontType]`, `PickFont(false)`,
  then `GetGlyph(codepoint, font)`. A null glyph is the missing-glyph character. This is a strong
  hint, not proof; a font can hold a glyph the game still refuses to draw. Dev builds only
  (`GlyphProbe`, behind `SINOPHONE_TESTING`).

## 8. `AtkValue` macros and the English line

- `<Format(IntegerParameter(n),...)/>` and `<Digit(...)/>` draw a number. Replace the **whole tag**
  with the value the English line showed; replacing only the inner expression lets the macro strip
  remove the number.
- `<Time(...)/>` draws nothing. It fills the date and clock registers the row reads through
  `TopLevelParameter(n)`. Fill those from the date and clock the English line shows, then drop the
  tag. Indices: 222 year, 221 month, 220 weekday, 219 day, 218 hour, 217 minute, 216 second,
  215 millisecond.
- The English client writes the date month first. A number above 12 marks the day-first form.
- `<Sheet(Sheet,RowId,Column)/>` names a value from another sheet. The English line is the only
  source for which name was drawn.
- `<EnNoun>` is a name from another sheet and behaves like `Sheet`: treat it as a wildcard.
- The dumps write a sound such as `<squeal>` in angle brackets. Keep those; they are text, not
  macros. A single capitalised word or a slash in angle brackets is still a macro.
- `<Unknown60>0262</Unknown60>` carries binary data, not text. Drop the whole pair.
- `ObjectParameter(1)` is the player name. Resolve it on every lookup, because the name changes with
  the logged-in character. Indices 55 and 56 are other objects and are dropped.
- The English dumps mark italics with one asterisk and bold with two. The game draws that emphasis
  with a SeString macro, so normalization strips the asterisks and a leading `(-Alias-)` prefix.
- A sheet row with an `If` macro gets one key per branch, so the line the game shows matches.
  `PlayerParameter(4)` is the player's gender and is read from `IPlayerState`; every other
  condition shows the first branch.
- A text built at runtime has no source row and stays English. That is a limit, not a bug.

## 9. Dev probes and how to research a new surface

The Testing tab's engine half is compiled only for dev builds (`SINOPHONE_TESTING`). Its harness
core lives in `Diagnostics/`, not in ImGui draw code, so the rules are unit tested.

| Probe | What it answers |
| --- | --- |
| `NativeTextProbe` | Scan any addon's text nodes, write a sample, switch font, keep the write sticky on refresh |
| `TalkContextProbe` | Log `EventFramework` scene, event states, handler ids, and Lua keys for `Talk` / `TalkSubtitle` |
| `GlyphProbe` | Which characters the loaded font has no glyph for |
| `ChatBubbleHarness` | Start a yell bubble through `OpenBalloon`, measure the bubble nodes, check glyphs |

Protocol for a new addon:

1. **Find the addon name.** Open the window and watch `/xldata` or the addon lifecycle log.
2. **Confirm the struct and fields.** Check the installed `FFXIVClientStructs.dll` / XML. Do not
   assume the field names in this file survived a patch.
3. **Scan before writing.** Use `NativeTextProbe` to list text nodes, or add a temporary Debug log
   that walks `RootNode` and `UldManager`. Record the id and the English text in each node.
4. **Find the value path.** If the addon takes `AtkValue`s, log the count, types, and strings on
   setup and refresh. A pre-layout swap is better than a node write when the game sizes the box.
5. **Decide the timing.** Before layout if the game measures the text; after layout only as the
   documented fallback, with a guarded per-frame reapply.
6. **Test the hard cases.** A long Chinese line, a line with a macro, a line with no translation,
   turning the setting off while the window is open, plugin unload, and a second line in the same
   window.
7. **Write the pure rule down.** Sizes, fit rules, and node selection go in a class that unit tests
   can call without the game. The ImGui tab only calls it.
8. **Route the callback through `HookGuard.Run`** (or keep an existing `try/catch`), record misses
   with `FailureTrace`, and add a `PluginLog` debug line at each decision.
9. **Update** `docs/architecture.md`, `docs/translation-workflow.md`,
   `docs/native-mode-checklist.md`, and this file in the same pull request.

## 10. Dead ends and wrong methods

These cost time or caused bugs. Do not retry them.

- **Do not write a text node when the game must measure the line.** The box is already sized for
  the English text; a later node write only resizes the node and the Chinese clips. Use the
  pre-layout value swap.
- **Do not write a nameplate's `NamePlateObject.TextW` / `TextH`.** It cut the sides off a wider
  name and damaged the shared bake atlas. Use `OnNamePlateUpdate`.
- **Do not change a chat bubble text node's width or flags.** The game rewrites the line with hard
  breaks, and later lookups see fragments. Fit the background nodes.
- **Do not restore bubble node sizes on `PostHide`.** The addon hides and shows between lines; each
  restore and rewrite rebuilds the font cache and hitches.
- **Do not write `TalkSubtitle` text nodes.** The band draws the values it measured. Write
  `AtkValue[0]` and restore the caller's value.
- **Do not cast a non-Lua handler to `LuaEventHandler`.** Only Quest, CustomTalk, and
  GuildLeveAssignment derive from it.
- **Do not use `DefaultTalk.EntryId` as the row id.** Use the packed `EventId.Id`.
- **Do not use a cutscene task id as the scene number.** Read it from the path.
- **Do not read game state on every line.** The chocobo stand read is gated on the line having a
  stand variant. A new disambiguation source needs the same gate.
- **Do not read an addon pointer after `PostClose` / `PreFinalize`.** The addon may be freed.
- **Do not measure `GetTextDrawSize` with an unterminated buffer.** The game reads past the text.
- **Do not stop reading an `AtkValue` string at its first macro.** Read the whole SeString, then
  strip.
- **Do not leave a hook without a guard.** A throw inside a callback reaches the game's frame.
- **Do not leave a pointer to freed memory in a value.** Keep the unmanaged buffer for several
  lines. `SetManagedString` is only for the deliberate share path, where the caller frees the value.
- **Do not assume another addon uses the same value indices as `Talk`.** Log the value count and
  types on setup before reading `AtkValue[0]`.
- **Do not treat `AtkValue` text as plain text.** It is a SeString; use
  `ReadSeStringAsString` / `ExtractText`.
- **Do not use a Chinese-client font pack on the global client.** It replaces files the global
  client never loads; use the `(Global)` pack.
- **Do not add a prompt-specific rule.** Prefer a rule that applies to all text. If only a
  prompt-specific fix works, ask the developer.
- **Do not assume the journal has plain child nodes.** Its text may live in a journal canvas
  component; check before writing.

## 11. Where the behaviour is documented

- `docs/architecture.md` — file map, data flow, id-first lookup, native mode.
- `docs/translation-workflow.md` — the full lookup order, every fallback, and the per-surface
  workflow.
- `docs/native-mode-checklist.md` — the in-game rows a native surface must pass.
- `docs/testing.md` — the Testing tab, the harness, and the failure folder.
- `AGENTS.md` — the project rules that outrank this file.
