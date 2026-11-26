# Translation workflow

This document explains how Sinophone builds translation data, loads and caches it, searches for matches, applies fallbacks, and hooks each in-game dialogue and UI surface.

## End-to-end architecture

Sinophone translates the client's text (English, Japanese, German, or French) using pre-built CN/TC lookup tables. No external translation API is called at runtime. Sheet rows and script lines are matched by the game's ids where it exposes one, so the same data serves every client language.

```mermaid
flowchart LR
    subgraph Offline["Offline Data Pipeline"]
        CSV["CN / TC / EN CSVs and Script Dumps"]
        DP["Sinophone.DataPrep"]
        GZ["Sinophone.cn.json.gz / Sinophone.tc.json.gz"]
        CSV --> DP --> GZ
    end

    subgraph Startup["Startup & Background Updates"]
        UPD["TranslationDataUpdater"]
        CACHE["TranslationStoreCache (Sinophone.index.bin)"]
        EXT["SheetExtraction (4 ms/frame slices)"]
        STORE["TranslationStore"]
        FONT["FontManager (Async ImGui Atlas)"]
        GZ --> UPD --> CACHE
        UPD --> EXT --> STORE
        CACHE --> STORE
    end

    subgraph Surfaces["In-Game Surface Handlers"]
        TALK["TalkHandler (Talk, TalkSubtitle)"]
        SEL["SelectionDialogHandler (5 Selection Addons)"]
        BUB["ChatBubbleHandler (_MiniTalk)"]
        NP["NamePlateNameWriter (Nameplates)"]
        ITEM["ItemNameHandler (ItemDetail)"]
        CHAT["ChatLogRestorer (IChatGui)"]
    end

    STORE --> TALK
    STORE --> SEL
    STORE --> BUB
    STORE --> NP
    STORE --> ITEM
    FONT --> TALK
    FONT --> ITEM
    TALK --> CHAT
    BUB --> CHAT
```

### Surface summary

| Component | In-game target | Hook mechanism | Search method | Display output | Fallback behavior |
| --- | --- | --- | --- | --- | --- |
| `TalkHandler` | `Talk`, `TalkSubtitle` | `IAddonLifecycle` + `AddonTalk.VirtualTable` (`OnSetup`, `OnRefresh`) | ID-first (`LuaTexts` key + row id / `EventFramework` row / cutscene sheet row), ambiguous script disambiguation, exact text, player placeholder, script template, branch-neutral | `OverlayWindow` (default, and all `TalkSubtitle`) or native `AtkValue` / nodes (`NativeTextMode`) | Text-node segment retry; `NativeTextWriter.WriteFitted` node fallback; `OverlayWindow`; optional English when `HideWhenNoTranslation` is off |
| `SelectionDialogHandler` | `SelectYesno`, `SelectString`, `SelectIconString`, `SelectOk`, `CutSceneSelectString` | `IAddonLifecycle` (`PostSetup`, `PostRefresh`, `PreDraw`, `PostHide`, `PostClose`, `PreFinalize`) | `Addon` label -> dialogue lookup with the live or sibling cutscene scene and event script hints -> `Addon` template (evaluated, or filled from the live line) -> punctuation trim -> short label `+ "."` | In-place `AtkTextNode` (`NativeTextWriter.WriteAt`) | Unresolved templates or misses leave original English; `FitPromptLines` keeps multi-line layout if single-line exceeds node bounds |
| `ChatBubbleHandler` | `_MiniTalk` (yells & default balloons) | `NpcYellBalloon.OpenBalloon` detour + `IAddonLifecycle` on `_MiniTalk` | Yells: `NpcYell.YellInfo` row ID -> dialogue lookup; Default balloons: plain text -> dialogue lookup | Yells: pre-layout text replacement; Balloons: in-place `BubbleTextNode` + `BubbleLayout.GrowTo` | Misses keep English and are hashed so they are not re-queried every frame; no overlay |
| `BattleTalkHandler` | `UIModule` battle talk (`ShowBattleTalk`, `ShowBattleTalkImage`, `ShowBattleTalkSound`) | Virtual table hook at indices 198, 199, 201 | `NpcYell` row id -> dialogue lookup (id first), otherwise the normalized text; name via the line's dump alias, then `TryGetName` | UTF-8 name and text swapped before the game measures the box | A miss, a disabled setting, or a failed lookup passes the game's own pointers through; no overlay |
| `NamePlateNameWriter` | Visible NPC / object nameplates | `INamePlateGui.OnNamePlateUpdate` | Case-insensitive `TryGetName` (with punctuation trim) | `NamePlateUpdateHandler.NameParts.Text` before game bakes plate | Skips players; misses keep English; no overlay |
| `ItemNameHandler` | Hovered item (`ItemDetail`) | `IGameGui.HoveredItemChanged` + `IAddonLifecycle` on `ItemDetail` | Strips HQ/Collectible offsets -> `Item` sheet English name -> `TryGetName` | `ItemNameWindow` above cursor (default) or `ItemDetail.ItemNameText` (`ItemNameNativeMode`) | Skips `EventItem` (`>= 2,000,000`); missing `ItemNameText` node falls back to `ItemNameWindow` |
| `ChatLogRestorer` | Story tab & yell log lines | `IChatGui.ChatMessage` | Exact match against 64-entry MRU `(Chinese, English)` history | Rewrites `IMutableChatMessage.Message` and `Sender` to English | Unmatched story messages stay as-is and log at debug level |

---

## Data preparation, loading, and caching

### 1. Offline build (`Sinophone.DataPrep`)

`tools/Sinophone.DataPrep` reads datamining CSV dumps and produces gzipped JSON (`TranslationArtifact`, format version 2):

- **Sheets (`Program.cs`)**: Reads named columns (or fixed positional columns for unnamed sheets such as `NpcYell`, `SnipeTalk`, `GimmickTalk`, `Addon`, etc.) into `Sheets[sheetName][rowId][fieldName] = string[]`. Empty slots are kept as empty strings so indices stay aligned with live Lumina sheets.
- **Scripts (`ScriptDump.cs`)**: Reads 14 script folders (`content`, `custom`, `cut_scene`, `dungeon`, `guild_order`, `leve`, `opening`, `quest`, `raid`, `shop`, `story`, `system`, `transport`, `warp`). It pairs localized CN/TC lines with English lines by `TEXT_...` key and keeps each row's sheet name and row id. In `quest/`, it drops `_SEQ_`, `_TODO_`, and `_QIB_` keys (journal/objectives/unused) while keeping `_SYSTEM_` keys used in dialogue.

### 2. Runtime updates (`TranslationDataUpdater`)

At startup (if `CheckForDataUpdates` is enabled or no data exists and the assembly has no embedded copy), `TranslationDataUpdater`:

1. Fetches `manifest.json` from the `data` GitHub release.
2. Compares `manifest.Version` (a content hash) with `Configuration.LastDataVersion` and per-variant flags (`HasSimplifiedData`, `HasTraditionalData`).
3. Downloads needed `.json.gz` files to `<file>.download`, verifies SHA-256 checksum, gzip decompression, `FormatVersion == 1`, and `Sheets.Count > 0`.
4. Atomically moves valid files into the plugin config directory and sets `_reloadRequested`.
5. `Plugin.OnTick` consumes `_reloadRequested` on the game thread and triggers a background store reload while the existing store keeps serving.

### 3. Startup load & index caching (`Plugin`, `TranslationStore`, `TranslationStoreCache`)

Because Dalamud forbids reading Lumina sheets off the game thread and synchronous CJK font rasterization stalls rendering, startup work is split across worker tasks and frame-budgeted game-thread slices.

```mermaid
sequenceDiagram
    participant P as Plugin (Game Thread)
    participant W as Worker Task
    participant SE as SheetExtraction
    participant FM as FontManager (Async Atlas)

    P->>FM: Rebuild() starts BuildFontsAsync()
    P->>P: BuildStoreCacheKey() and SheetCatalog.Create(DataManager)
    P->>W: Task.Run(TryLoadCache or Parse)

    alt Cache hit (Sinophone.index.bin key matches)
        W-->>P: Returns cached TranslationStore
    else Cache miss or stale key
        W-->>P: Returns ParsedArtifacts
        loop Each frame in Plugin.OnTick (4 ms budget)
            P->>SE: Extract(SheetReadBudget = 4 ms)
        end
        P->>W: Task.Run(TranslationStore.Build + SaveCache)
        W-->>P: Returns built TranslationStore
    end

    P->>FM: Check FontManager.IsReady
    Note over P,FM: Store waits in _storeAwaitingFont until atlas is ready
    P->>P: SwapStore() -> refresh Overlay, NamePlates, ItemNameHandler
```

- **Cache key (`StoreCacheKey.Build`)**: Combines `Variant`, `UseFallbackVariant`, `Conversion`, `TranslationStore.IndexFormatVersion`, `GetGameDataVersion()`, the client language (`DataManager.Language`), and `SourceIdentity` (size + UTC ticks for downloaded files, or SHA-256 for embedded resources) for both CN and TC. The language is part of the key because the index is keyed on the live client's text.
- **Variant fallback**: If `UseFallbackVariant` is enabled, `TranslationStore.Build` indexes the primary artifact first and the other variant second (`TryAdd` ensures the primary variant always wins). If a downloaded file fails to parse, `TranslationStore` falls back to `OpenEmbeddedResource`.
- **Client language**: `SheetCatalog` reads `dataManager.GetExcelSheet<T>()` with no language argument, so every descriptor reads the language the client is running. The store pairs that text with the artifact's Chinese by row id. A launcher language change makes the cache key stale and rebuilds the index.
- **Eager vs. late display resolution**: During indexing, lines without conditionals or `ObjectParameter` macros are resolved once (`ResolveSheetMacros`, `CleanForDisplay`, `ChineseConverter.Convert`) and stored ready to display. Lines containing `<If(`, `<Switch(`, or `ObjectParameter(` remain raw and are resolved per lookup because their output depends on the active player's gender, Grand Company rank, or character name.

---

## Lookup indices, normalization, and fallbacks (`TranslationStore`)

### Key normalization (`MacroNormalizer.NormalizeKey`)

Before any text lookup or indexing, English strings pass through `NormalizeKey`:

1. **Strip invisible/control characters**: Removes soft hyphens, zero-width spaces/joiners, BOM, replacement chars (`\uFFFD`), and raw SeString control bytes (`U+0002`, etc.); turns non-breaking spaces into spaces.
2. **Replace line breaks**: `<br>` and `<br/>` become `" "` so adjacent words do not glue together.
3. **Strip tags and emphasis**: Removes remaining `<...>` SeString macro tags and `*` / `**` markdown emphasis markers from script dumps. A tag whose contents are all lowercase, such as `<squeal>`, is kept: the dumps write a sound that way and the game draws it as text. Real macros carry a capital, a digit or a slash, so they still go.
4. **Collapse whitespace**: Collapses any run of whitespace (tabs, newlines, multiple spaces) into a single space and trims both ends.

### Dialogue search order (`TranslationStore.TryGetDialogue`)

```mermaid
flowchart TD
    Start["TryGetDialogue(englishText, context, speakerName)"] --> IdCheck{"context has ScriptKeys or Row?"}

    IdCheck -- Yes --> ScriptKey["1a. _scriptKeyLookup[scriptKey]"]
    ScriptKey -- Hit --> Ret["ResolveDisplay() & return true"]
    ScriptKey -- Miss --> RowLookup["1b. _rowLookup[(Sheet, RowId, Field)]"]
    RowLookup -- "Explicit slot or matching English slot or single slot" --> Ret
    RowLookup -- "Miss or compared slots mismatch (fail open)" --> Norm["NormalizeKey(englishText)"]
    IdCheck -- No --> Norm

    Norm -- Empty --> Fail["Return false"]
    Norm --> AmbigCheck{"2. In _ambiguousScriptKeys?"}
    AmbigCheck -- Yes --> NarrowSpeaker["Narrow by speaker tokens (SpeakerWords)"]
    NarrowSpeaker -- "1 left" --> Ret
    NarrowSpeaker -- ">1 left" --> NarrowCutscene["Narrow by context.CutsceneId (VOICEMAN)"]
    NarrowCutscene -- "1 match" --> Ret
    NarrowCutscene -- "No unique match" --> NarrowSound["Sound-only key (IsOnlySoundMarkers)"]
    NarrowSound -- "Yes" --> Ret
    NarrowSound -- "No" --> NarrowTaxi["Narrow by ChocoboTaxiStandUnlocked (_UNLOCK)"]
    NarrowTaxi -- "1 match" --> Ret
    NarrowTaxi -- "Still ambiguous" --> Fail

    AmbigCheck -- No --> ExactText["3. _textLookup[key]"]
    ExactText -- Hit --> Ret
    ExactText -- Miss --> PlayerSub["4. TryLookupPlayerNamePlaceholder (full name -> first name, 'Player Player' -> 'Player')"]
    PlayerSub -- "Hit in _textLookup or _branchNeutralLookup" --> Ret
    PlayerSub -- Miss --> ScriptTpl["5. TryLookupScriptTemplate (_scriptTemplates blank-gap match)"]
    ScriptTpl -- Hit --> Ret
    ScriptTpl -- Miss --> Branch["6. TryLookupBranchNeutral (collapse pronouns, nouns of address, time-of-day)"]
    Branch -- "Hit (not in _ambiguousBranchKeys)" --> Ret
    Branch -- Miss --> Fail
```

#### Details of each fallback layer

1. **ID-first lookup (`TryGetDialogueById`)**:
   - **Script keys**: Exact case-insensitive match in `_scriptKeyLookup` using `TEXT_...` keys read from `LuaEventHandler.LuaTexts`. When the live map also gives a row id and the artifact has the same id for that key, the pair proves the line and no text comparison runs, so a Japanese line resolves. Without a live row id, the key is used only when the dump paired it with the same English line; the live Lua map can reuse a key for another menu line. A long shared prefix or suffix is accepted, because the dump can resolve a race or other player word to one value while the live line uses another.
   - **Script sheet rows**: A cutscene subtitle is matched against the live `VoiceMan` sheet, whose path is `PlayCutSceneTask.CutscenePath`. The sheet name and row id are language-independent, so `_scriptRowTextLookup` returns the Chinese line for the same row on any client. A text two rows share is not used.
   - **Sheet rows**: Looks up `(Sheet, RowId, Field)` in `_rowLookup`. If `Slot >= 0`, returns that slot. Otherwise `TrySelectRowSlot` extracts the row's live variants via `SheetCatalog` and picks the slot whose normalized text matches the live text. If the row's English was readable and no slot matched, it **fails open** to text lookup instead of guessing. Only when the live row could not be compared and exactly one translated slot exists does it return that single slot.
2. **Ambiguous script disambiguation (`TryLookupAmbiguousScript`)**:
   - Script lines that share identical English text with different Chinese translations (e.g. `"You bastard!"`) are removed from `_textLookup` and stored in `_ambiguousScriptKeys`.
   - A sheet row can also share an English line with script lines, such as `NpcYell` and a cutscene line. The sheet text stays in `_textLookup` as the fallback, and the script candidates are added to `_ambiguousScriptKeys`, so the speaker, scene, or script hint picks the script line when it can.
   - Narrowing runs in five steps:
     1. **Speaker tokens**: Matches normalized speaker words (and full punctuation-free name, e.g. `G'raha Tia` -> `GRAHATIA`) against key tokens (`KeySpeakerTokens`).
     2. **Cutscene ID**: Matches `context.CutsceneId` against `TEXT_VOICEMAN_<id>_...`.
     3. **Event script hints**: Matches `context.ScriptKeyHints` against the script family of each dump key. The family is the key without its line suffix, such as `REGUNI5BASEOMICRON_00760`, so a Lua handler key, a tracked quest id, and a sibling option's `TEXT_...` key all work.
     4. **Sound-only key**: When the key holds nothing but sound markers (`IsOnlySoundMarkers`), answers the first candidate. Every candidate says the same thing, and a balloon that squeals carries no speaker name or id to narrow with.
     5. **Chocobo taxi stand unlock state**: Matches `_UNLOCK` in keys against `context.ChocoboTaxiStandUnlocked` so newly-unlocked vs already-unlocked greetings resolve cleanly.
   - If more than one candidate remains, lookup returns `false` rather than showing another scene's translation. One exception: when every remaining candidate is in the one cutscene that is playing, the store uses the first (default) branch. The game can hold two versions of one line for different player progress, and the addon exposes no flag for that choice.
3. **Player name placeholder fallback (`TryLookupPlayerNamePlaceholder`)**:
   - Script dumps store `"Player Player"` or `"Player"`, whereas the live game inserts the character's name.
   - Replaces the live `CharacterName` (and, if multi-word, the first name alone) back with `"Player Player"` and then `"Player"`, checking both `_textLookup` and `_branchNeutralLookup`.
4. **Script blank-gap templates (`TryLookupScriptTemplate`)**:
   - Matches `_scriptTemplates`, built from script lines containing runs of 2+ spaces (`ScriptTextTemplate.BuildBlankGapTemplate`) where dynamic values (like item names) are injected at runtime.
5. **Branch-neutral fallback (`TryLookupBranchNeutral`)**:
   - English script dumps resolve conditionals to a single branch. If a player triggers the opposite branch, exact match misses.
   - `CollapseBranchWords` replaces `GenderedPronouns` (`he/him/his/himself/she/her/hers/herself`), `GenderedNouns` (`lad/lass/sir/madam`), and `TimeOfDayWords` (`morning/mornin/afternoon/evening/evenin/noon/day/night`) with `\u001F` and searches `_branchNeutralLookup`. Any collapsed key shared by multiple lines with different Chinese is blacklisted in `_ambiguousBranchKeys`.

### Name lookup (`TranslationStore.TryGetName`)

- Uses `StringComparer.OrdinalIgnoreCase` on `_nameLookup` because sheets such as `ENpcResident` store lowercase names (`skywatcher`) while the game displays capitalized names (`Skywatcher`).
- Includes `ENpcResident`, `BNpcName`, `EObjName`, `SnipeTalkName`, `Item`, `EventItem`, `MKDLore.Name`, and script speaker aliases parsed from `(-Alias-)` prefixes.
- A few CN/TC rows were never localised and hold Japanese. When several rows share one English name, a row without kana replaces a kana row, so the localised duplicate wins even when the kana row has the lower id. The middle dot and long-vowel mark are not treated as kana, because Chinese names use them too.
- **Fallback**: If the full key misses, trims trailing punctuation (`?`, `!`, `.`, `？`, `！`, `。`, `…`) and retries `_nameLookup`.

### Display cleanup (`ResolveDisplay` -> `MacroNormalizer.CleanForDisplay` -> `ChineseConverter`)

When returning a translation for display:
1. `ResolveSheetMacros` replaces `<Sheet(Sheet,RowId,Col)/>` tags using `_nameRowLookup`.
2. `ConditionalText.Resolve` evaluates `<If(...)>` and `<Switch(...)>` tags:
   - `PlayerParameter(4)` -> `PlayerState.Sex == Sex.Female`
   - `PlayerParameter(52/53/54)` -> Grand Company row ID (1/2/3) and rank (`GreaterThan(...,0)` / `LessThan(...,127)`)
   - Unknown conditions default to true / first `<Case>`.
3. `MacroNormalizer.CleanForDisplay` substitutes `ObjectParameter(1)` with `PlayerState.CharacterName`, drops `<UIForeground>`, `<UIGlow>`, and `<UnknownXX>` tag+payload pairs (saving `UIForeground`/`UIGlow` keys separately in `_styleLookup` for native nodes), strips remaining `<...>` tags, and normalizes line endings.
4. `ChineseConverter.Convert` applies OpenCC longest-match conversion (`s2t` or `t2s`) only when `IsMostlySourceScript` confirms the text is not already in the target script.

---

## Per-component workflows

Every handler callback below runs inside the game's frame, so a throw there can reach the game.
Each handler catches it and leaves the English text. The item name and selection dialog handlers
share `HookGuard.Run`, which logs a warning with the surface name; the others wrap their callbacks
directly. A hook that fails costs one untranslated line, never a throw into the game.

### 1. Talk & TalkSubtitle (`TalkHandler` + `TalkEventReader`)

`TalkHandler` handles both `Talk` (standard dialogue box) and `TalkSubtitle` (cutscene subtitles).

#### Hooking & lifecycle
- Subscribes to `IAddonLifecycle` events for `Talk` and `TalkSubtitle`: `PostSetup`, `PostRefresh`, `PostClose`, `PostHide`, `PreFinalize`.
- For `Talk` only, also subscribes to `PostUpdate` and `PreDraw` (for native node reapplication and layout logging), and installs Detour hooks (`IGameInteropProvider.HookFromAddress`) on `AddonTalk.VirtualTable->OnSetup` and `OnRefresh` (`EnsureValueHook`).
- A `Talk` line can appear while `PlayCutSceneTask` is unreadable. The handler remembers the scene of a line that names exactly one (`TryGetCutsceneId`) and passes it to later lines that several scenes share. The remembered scene is cleared on `PostSetup`, `PostClose`, and `PreFinalize`.
- A cutscene can reuse one English line and one speaker for two player-progress branches, such as `The significance of these details...` in scene 6006. The addon exposes no branch id, and both lines belong to that scene, so the store uses the first (default) branch instead of leaving the line English.

```mermaid
sequenceDiagram
    participant Game as Game / AddonTalk
    participant Hook as TalkHandler (OnSetup / OnRefresh Hook)
    participant Reader as TalkEventReader
    participant Store as TranslationStore
    participant Wrap as CjkLineWrapper
    participant Log as ChatLogRestorer
    participant Event as TalkHandler (OnAddonEvent)

    Game->>Hook: Call OnSetup / OnRefresh (AtkValue* values)
    alt NativeTextMode ON & Talk & Store ready
        Hook->>Reader: ReadContext(englishText, Target.Address)
        Reader-->>Hook: DialogueContext (Row, ScriptKeys, CutsceneId, TaxiUnlocked)
        Hook->>Store: TryGetDialogue + TryGetName
        alt Translation found
            Hook->>Wrap: WrapForTalkBox(addon, chineseText)
            Hook->>Hook: Swap AtkValue[0..1] to unmanaged UTF-8 buffer (or ManagedString if NativeTextSharesTranslation)
            Hook->>Log: Remember(wrapped, english) and Remember(unwrapped, english) and Remember(chineseName, englishName)
            Hook->>Game: Call Original(addon, count, values) with Chinese
            Note over Game: Game measures & sizes dialogue box for Chinese
            Hook->>Game: RestoreValues() puts caller's original AtkValues back (unless sharing)
        else Miss
            Hook->>Game: Call Original() with English
        end
    end

    Game->>Event: PostSetup / PostRefresh
    alt IsLaidOutByHook(englishText, _laidOutChineseText)
        Event->>Event: ForgetNativeText & Overlay.Hide (already laid out)
    else Overlay mode, TalkSubtitle, or hook unavailable/missed
        Event->>Reader: ReadContext(englishText, Target.Address)
        Event->>Store: Resolve(englishText, englishName, context, intParams)
        opt Miss on Talk AtkValue[0]
            Event->>Event: GetTextNodeCandidates() reads AtkTextNode220..248 and retries Resolve
        end
        alt NativeTextMode ON (hook fallback)
            Event->>Event: NativeTextWriter.WriteFitted (shrinks FontSize down to 10)
            alt Cannot fit
                Event->>Event: Restore node & show OverlayWindow
            end
        else Overlay mode or TalkSubtitle
            Event->>Event: OverlayWindow.SetContent()
        end
    end

    Note over Game,Event: Each PostUpdate / PreDraw on Talk calls NativeTextWriter.Reapply() (byte-span compare)
    Game->>Event: PostClose / PostHide / PreFinalize
    Event->>Event: OverlayWindow.Hide()
```

#### Event context collection (`TalkEventReader`)
1. Finds active `EventHandler` pointers from `EventFramework.Instance()->EventHandlerModule.EventHandlerMap`:
   - First checks handlers whose `EventGameObject` or `SceneGameObject` matches the current target address.
   - Falls back to `EventState1.EventId` / `EventState2.EventId`.
   - Falls back to matching `handler->Scene == framework->Scene`.
2. Maps `ContentId` to sheet rows via `TalkEventSurfaces` (`DefaultTalk` using packed `EventId.Id`; `GuildLeveAssignment`, `CraftLeveClient`, `GimmickAccessor`/`GimmickRect`, `GimmickBill`, and `GoldSaucerTalk` using `EntryId`). A row is only kept if exactly one handler maps to a row.
3. For `Quest`, `CustomTalk`, and `GuildLeveAssignment` handlers, reads `LuaEventHandler->LuaTexts` and keeps keys whose `ScriptTextTemplate` matches the normalized live English line.
4. Reads the scene number from `PlayCutSceneTask.CutscenePath` in `EventSceneModule.TaskManager.Tasks`. A path with no number leaves the scene at 0, because the cutscene row id is not a `VOICEMAN` scene number. Also reads `UIState.IsChocoboTaxiStandUnlocked(standId)` for `ChocoboTaxiStand` handlers.
5. Adds the tracked quest script ids from `UIState.QuestTodoList.Todo.TrackedQuests` as `ScriptKeyHints`. The active handler filter can miss the quest that produced a menu prompt, but the tracked quest still names the family.
6. When no active Lua handler has the line, scans every handler in `EventHandlerMap` for a `LuaTexts` value that matches and adds those keys as hints. This keeps quest variants separate even when the handler is not in the active set.

#### Text handling & fallbacks
- **Extraction**: Reads `AtkValue[0]` (dialogue) and `AtkValue[1]` (speaker) via `MemoryHelper.ReadSeStringAsString`, plus integer parameters from `AtkValue[2..]` (`Int`/`UInt`) to fill `IntegerParameter(n)` in stored text.
- **Speaker alias**: A script dump can prefix the English line with `(-Voice from the Past-)`. `NormalizeKey` removes that prefix for every lookup, because battle talk can pass the live line with the alias still attached while the indexed key has it stripped. The alias also wins over the name field: battle talk can pass the player's name there, and the dump alias is the speaker who actually said the line.
- **Alias per line**: A generic alias such as `(-All-)` is used by several groups with different Chinese labels. `_lineSpeakerAliases` stores the English and Chinese alias with the exact English line, so `(-All-)Aye!` gets `拂曉的同伴們`, not the first `All` the name index saw.
- **Oblique / multi-node fallback**: When `AtkValue[0]` misses in `OnAddonEvent` (e.g. a line with oblique formatting split across nodes where `AtkValue[0]` only holds the first segment), `GetTextNodeCandidates` reads `AtkTextNode220`, `228`, `238`, `240`, and `248` via `ReadOnlySeStringSpan.ExtractText()` and retries `Resolve` (excluding the speaker name node first, then including all segments).
- **Pre-layout CJK wrapping (`CjkLineWrapper`)**: In the `AtkValue` hook, wraps Chinese to `max(textNodeWidth, textBackgroundWidth) - nodeFontSize` using `AtkTextNode.GetTextDrawSize`, because the text node can still hold the previous line's width. It keeps closing punctuation (`。，、．；：？！）］｝」』】〉》”’…`) off line starts, opening punctuation (`（［｛「『【〈《“‘`) off line ends, and rebalancing last lines shorter than 1/4 width.
- **Speaker formatting**: `FormatSpeakerName` builds `{chinese} ({english})`, leaves masked names (`???`) unrevealed, and omits brackets when Chinese equals English.
- **Overlay fallback**: `OverlayWindow` displays Chinese only, or falls back to original English when `HideWhenNoTranslation` is false.

---

### 2. Chat log restoration (`ChatLogRestorer`)

When native `Talk` or `ChatBubbleHandler` feeds Chinese to the game, the game may copy that text into the Story chat tab or chat log from the addon's internal copy even after `AtkValue`s are restored.

- **Hook**: Subscribes to `IChatGui.ChatMessage`.
- **History**: `ChatLogHistory` stores up to 64 recent `(Chinese, English)` pairs (both wrapped and unwrapped dialogue lines, plus speaker names) as an MRU linked list.
- **Handling**: In `OnChatMessage`, checks `IMutableChatMessage.Message.TextValue` and `Sender.TextValue` independently against `ChatLogHistory` and replaces matched text with English `SeString`s.
- **Bypass / reset**: Disabled for `Talk` when `NativeTextSharesTranslation` is on; toggling that setting calls `Clear()`. Unmatched `NPCDialogue`, `NPCDialogueAnnouncements`, and `SystemMessage` events log a debug line to aid diagnosis.

---

### 3. Selection dialogs (`SelectionDialogHandler`)

Covers `SelectYesno`, `SelectString`, `SelectIconString`, `SelectOk`, and `CutSceneSelectString` (native in-place only; no overlay).

#### Hooking & node discovery
- Registers `PostSetup`, `PostRefresh`, `PostHide`, `PostClose`, `PreFinalize`, and `PreDraw` on all five addon names.
- Walks three sources to collect `AtkTextNode*` pointers:
  1. `addon->RootNode` tree (recursing into child/sibling nodes and component `UldManager.NodeList` up to depth 64).
  2. `addon->UldManager.NodeList`.
  3. Typed addon fields (`PromptText`, `YesButton`, `NoButton`, `AtkComponentButton238`, `OkButton`, `PopupMenu.PopupMenu.List` item renderers, `CutSceneSelectString.TextLabel` and `OptionList`).
- A sheet row that branches on the player's gender ("Ah, Master Player" against "Ah, Mistress Player") is indexed a second time with the gendered nouns collapsed, so the wording the live sheet did not keep still finds the row. The same collapsed index covers script lines.
- A shared key whose remaining candidates say the same thing once punctuation and space are gone is answered with the first one, because the localisation of one line can differ only in a closing mark (`一定要凱旋。` / `一定要凱旋`). A shared key whose lines differ in words still stays English.
- A cutscene with no number in its path leaves the scene at 0 rather than passing the cutscene row id, because a row id is not a `VOICEMAN` scene number and can only narrow to the wrong line.
- `CutSceneSelectString` options are `VOICEMAN` script lines, and one English option can sit in several scenes. The handler reads the live cutscene scene through `TalkEventReader.ReadContext` and passes it with the lookup, so the scene number picks the right line; with no scene the store refuses the shared key and the option stays English.
- The live cutscene task can be gone when the choices appear. Every option in one dialog comes from the same cutscene, so the handler asks `TranslationStore.TryGetCutsceneId` for each option and uses the scene that one of them names. `TryGetCutsceneId` answers only when every script key holding that English text is a `VOICEMAN` key from the same scene; a line that also sits in a quest, or in two scenes, names none. If two options name different scenes, the handler does not narrow at all.
- A dialog can also use the same English option in many event scripts, such as `What do you do here?`. The handler collects the active Lua handler's `LuaKey` and every script key an option exposes, and passes them as `DialogueContext.ScriptKeyHints`. The store keeps candidates whose key names the same script family. Hints are kept for the open dialog, so a prompt processed after an option still narrows it on the next retry. A hint that matches nothing leaves the line English.
- `ReadContext` walks the event handler map, so its result is cached per dialog text for a refresh and reused by later nodes and the twelve retry frames. The candidate-row list is logged only when it changes, not for every node and retry.
- On `PreDraw`, calls `Reapply` on tracked node addresses and `TranslateLate` up to `MaxLateAttempts = 12` times (handles `CutSceneSelectString` options populated after `PostSetup`).
- On `PreFinalize` (not `PostHide`/`PostClose`, to avoid clearing recycled nodes), clears tracked addresses.

#### Search & parameter resolution (`TryTranslate` & `ResolveAddonText`)

```mermaid
flowchart TD
    In["Node plain text (ExtractPlainText)"] --> Clean["plainText = CleanForDisplay(text)"]
    Clean --> S1["1. TryGetAddonLabel(plainText)"]
    S1 -- Hit --> Res["ResolveAddonText()"]
    S1 -- Miss --> S2["2. TryGetDialogue(text)"]
    S2 -- Hit --> Fit["FitPromptLines() & WriteAt()"]
    S2 -- Miss --> S3["3. TryGetAddonText(plainText) (templates sorted by literal length desc)"]
    S3 -- Hit --> Res
    S3 -- Miss --> S4["4. Trim trailing punctuation (. 。 ! ！ ? ？) -> TryGetDialogue()"]
    S4 -- Hit --> Fit
    S4 -- Miss --> S5{"5. text.Length <= 16 and no trailing '.'?"}
    S5 -- Yes --> S5Try["TryGetDialogue(text + '.')"]
    S5Try -- Hit --> Fit
    S5Try -- Miss --> KeepEn["ClearIfStillOursAt() -> keep English"]
    S5 -- No --> KeepEn
    Res -- "All parameters resolved" --> Fit
    Res -- "Unresolved macro/parameter left" --> KeepEn
```

- **Template ordering**: `_addonTemplates` are sorted by `LiteralLength` descending, then wildcard count ascending, so specific templates match before broad conditional templates.
- **Parameter resolution (`ResolveAddonText`)**:
  - Resolves `<Sheet(Item,IntegerParameter(...),0)/>` and other `<Sheet(...)/>` captures via `TryGetName` / `TryGetAddonLabel`.
  - Strips `<Time(...)/>` and resolves `<Value|TwoDigitValue>TopLevelParameter(215..222)</...>` by parsing the date (`ReadDate`, handling MM/DD vs DD/MM when first > 12 and inferring next year if date already passed) and clock (`ReadClock`) from the live English string.
  - Substitutes `<Format|Digit(IntegerParameter(n),...)/>`, `StringParameter(n)`, `IntegerParameter(n)`, quoted quest names (`StringParameter(1)`), `Level: N` (`IntegerParameter(2)` / `<Sheet(QuestBattle,...)/>`), and `Item level: N` (`IntegerParameter(3)`).
  - Strips private-use UI icon characters (`U+E000`–`U+F8FF`).
  - **Fail-open safety**: If any `IntegerParameter(`, `StringParameter(`, `PlayerParameter(`, `TopLevelParameter(`, or `<Sheet(` remains unresolved, `ResolveAddonText` returns empty so the node stays English.
- **Line fitting (`FitPromptLines`)**: If the Chinese prompt has more `\n` lines than the English source, joins lines into one string and uses it only when `GetTextDrawSize` fits within the node's English `Width` and `Height`.

---

### 4. Chat bubbles (`ChatBubbleHandler`)

Translates `_MiniTalk` bubbles via two distinct paths (native only; no overlay).

```mermaid
sequenceDiagram
    participant Game as Game Engine
    participant YellHook as OnOpenBalloon (Yell Hook)
    participant Sync as SyncBubbles (PreDraw / Setup / Refresh)
    participant Store as TranslationStore
    participant Writer as NativeTextWriter & BubbleLayout
    participant Log as ChatLogRestorer

    alt Path A: Yell Bubble (NpcYell with ShowBalloon flag)
        Game->>YellHook: NpcYellBalloon.OpenBalloon(text, ...)
        YellHook->>Game: Read NpcYell.Instance().YellInfo matching text & ShowBalloon
        YellHook->>Store: TryGetDialogue(english, DialogueRowId("NpcYell", rowId, "Text"))
        alt Translated
            YellHook->>Log: Remember(chinese, english)
            YellHook->>Game: Original(balloon, chineseUtf8, ...)
            Note over Game: Game sizes bubble around Chinese text
        else Untranslated or battle-talk-only
            YellHook->>Game: Original(balloon, english, ...)
        end
    end

    alt Path B: Default Balloon (ENpcBase.Balloon)
        Game->>Sync: _MiniTalk PreDraw / PostSetup / PostRefresh
        loop Each entry in AddonMiniTalk.TalkBubbles
            Sync->>Sync: Compute FNV-1a Hash + length of node->NodeText
            alt Node already translated & still ours
                Sync->>Writer: layout.GrowTo(node)
            else Game rewrote same English line (streaming text)
                Sync->>Writer: ReapplyAt() + layout.GrowTo(node)
            else Previously missed same text
                Note over Sync: Skip lookup (zero allocation)
            else New or changed text
                Sync->>Store: TryGetDialogue(ExtractPlainText(text))
                alt Translated
                    Sync->>Writer: BubbleLayout.Capture(node, nineGrid, bubbleRes)
                    Sync->>Writer: WriteAt() + layout.GrowTo(node)
                    Sync->>Log: Remember(chinese, english)
                else Untranslated
                    Sync->>Sync: Record TextHash/TextLength to avoid retries
                end
            end
        end
    end
```

- **Yells**: `OpenBalloon` detour reads `NpcYell.Instance()->YellInfo` (filtering for `NpcYellOutputFlags.ShowBalloon`), resolves row ID + `MessageParams`, and passes UTF-8 Chinese before the game measures the bubble. Battle-talk-only yells are ignored.
- **Default balloons**: Addon populates `BubbleTextNode` after `PostSetup`, so `SyncBubbles` runs on `PreDraw` using a zero-allocation FNV-1a byte hash + length check.
- **Sound-only lines**: A balloon can hold nothing but a sound marker, such as `<squeal> <squeal>`. `NormalizeKey` keeps a lowercase tag, so the line gets a key at all, and `IsOnlySoundMarkers` answers the first candidate when several script lines share it. Before that rule the key came out empty, the line was skipped before lookup, and the bubble stayed English.
- **Layout sizing (`BubbleLayout`)**: Leaves `BubbleTextNode` width and flags untouched (changing them causes the game to re-break lines into fragments). Instead it changes `BubbleNineGridNode` and `BubbleResNode` by the change in text size (`bubble + (chinese - english)`), which keeps the margin the game drew around the English line without measuring it; a measured margin is either stale or zero, and zero makes the text spill out of both sides. A background that already held the English line follows the change both ways, so a shorter translation does not leave the English line's empty width. The text plus the nine-grid slice offsets is used as a floor only when the background is smaller than the English text, because then it was not laid out for this line and the caps would clip the Chinese. Width grows on both sides and shrinks from the right, because the text node is left-aligned. Height only grows upward, so the bottom edge and tail stay attached. The node's position is captured and restored with the size, and the log reports the size the node actually has after the call.
- **Lifecycle**: `PostHide` intentionally keeps cached translations to prevent font-cache rebuild hitches during multi-line balloon streams. `PreFinalize` calls `ForgetAll`; disabling or unloading calls `RestoreAll` to revert text and node sizes.

---

### 5. Battle talk (`BattleTalkHandler`)

Translates the large line a boss, dungeon narration, or a duty NPC shows near the top of the screen.
Native only; off by default and needs a Chinese font.

```mermaid
flowchart TD
    Game["UIModule.ShowBattleTalk / ShowBattleTalkImage / ShowBattleTalkSound"] --> Hook["BattleTalkHandler detour"]
    Hook --> Enabled{"Enabled && ShowInBattleTalk?"}
    Enabled -- No --> Orig["Call the original with the game's pointers"]
    Enabled -- Yes --> Read["Read NpcYell.Instance().YellInfo for a row with ShowBattleTalk"]
    Read --> Resolve["Resolve (store, config, text, name, context, MessageParams)"]
    Resolve --> Hit{"Translated?"}
    Hit -- No --> Orig
    Hit -- Yes --> Swap["Allocate UTF-8 Chinese name and text; keep the last 8 buffers"]
    Swap --> Orig2["Call the original with the Chinese; the game sizes its own box"]
```

- **Hook**: the three virtual functions come from `UIModule.Instance()`'s virtual table (indices 198,
  199, 201, from `UIModuleInterface`). Each hook body is wrapped in try/catch and calls the original on
  failure, because throwing inside the game's own call is not allowed.
- **Lookup**: `Resolve` checks `Enabled` and `ShowInBattleTalk`, looks the line up by `NpcYell` row id
  first and by text second, substitutes `MessageParams` through `ResolveIntegerParameters`, and formats
  the name through `TalkHandler.FormatSpeakerName` when `ShowSpeakerName` is on.
- **Name source**: `NpcYellInfo.Name`, unless the `HideSpeakerName` flag is set. A masked `???` stays
  masked.
- **Buffers**: `Marshal.AllocHGlobal`, last eight kept, freed on dispose. The game copies the line into
  its own fields, but a late copy must not read freed memory.
- **Surface split**: `ChatBubbleHandler` filters the same yells on the balloon flag, so a yell with
  both flags is written once per surface.
- **Off mid-line**: the line on screen stays Chinese until its box hides. The next line is English.

---

### 6. World nameplates (`NamePlateNameWriter`)

Translates visible NPC and object nameplates in place (native only; no overlay).

- **Hook**: Subscribes to `INamePlateGui.OnNamePlateUpdate`. `Enable()`, `Disable()`, and `Reset()` call `INamePlateGui.RequestRedraw()` to immediately redraw all visible plates when toggled or when the store reloads.
- **Handling**: Fires only for plates with new or changed names (not per frame). Skips any plate where `handler.PlayerCharacter is not null`.
- **Search & output**: Looks up `handler.Name.TextValue` via `store.TryGetName`. If found, formats `{chinese} ({english})` (when `TargetNameShowEnglish` is on) or `{chinese}` and sets `handler.NameParts.Text` before the game measures and bakes the nameplate into its shared atlas.
- **Fallback**: Misses leave the original English `NameParts` intact.

---

### 7. Item names (`ItemNameHandler` + `ItemNameWindow`)

Translates the hovered item's name in either a floating window (`ItemNameWindow`) or the game tooltip (`ItemDetail`).

```mermaid
flowchart TD
    Trigger["HoveredItemChanged / ItemDetail PostSetup / PostRefresh"] --> BaseId["GetBaseItemId(hoveredItem)"]
    BaseId -- "0 or >= 2,000,000 (EventItem)" --> Hide["RestoreTooltip() & ItemNameWindow.Hide()"]
    BaseId -- "Subtract 1,000,000 (HQ) or 500,000 (Collectible)" --> Sheet["Read Item sheet English Name"]
    Sheet --> Lookup["store.TryGetName(englishName)"]
    Lookup -- Miss --> Hide
    Lookup -- Hit --> Mode{"ItemNameNativeMode ON?"}

    Mode -- No --> Overlay["RestoreTooltip() & ItemNameWindow.SetName() (above cursor)"]
    Mode -- Yes --> FindNode["Find AddonItemDetail.ItemNameText"]
    FindNode -- "Node missing" --> Overlay
    FindNode -- "Node found" --> WidthCheck{"ShowOriginal && textWidth > AvailableWidth?"}
    WidthCheck -- Yes --> TwoLine["Format: two lines — chinese then (english)"]
    WidthCheck -- No --> OneLine["Format: chinese (english) or chinese"]
    TwoLine --> Write["NativeTextWriter.WriteAt() + FitTooltipName()"]
    OneLine --> Write
    Write -- Success --> HideWin["ItemNameWindow.Hide()"]
    Write -- Failed --> Overlay
```

- **Tooltip fitting (`FitTooltipName`)**: Clears `TextFlags.WordWrap | TextFlags.Ellipsis`, grows `node->Width` to `GetTextDrawSize` if wider than original, and restores `node->Height` to original (in two-line mode, text aligns to the bottom of the node so the Chinese line stacks upward into the header without overlapping the item description).
- **Per-frame reapply**: `ItemDetail` overwrites its own name while open, so `PreDraw` calls `ReapplyAt` + `FitTooltipName` (zero-allocation byte compare when unchanged).
- **Restore safety**: `RestoreTooltip` checks `CurrentNameNode() == _tooltipNode` before writing back; `PostHide` and `PreFinalize` call `ForgetTooltip` without writing to avoid touching recycled addon nodes.

### 8. Game font (`FontReplacementService`)

The native surfaces write Chinese into the game's own windows, which draw with the game's font.
`FontReplacementService` lets the user supply a Chinese font as a `.ttmp2` mod or a folder, with no
Penumbra or TexTools. The overlay is unaffected.

1. **Read the pack.** `FontPackExtractor` reads the TexTools manifest (`TTMPL.mpl`, a JSON object
   with a `SimpleModsList` array of byte ranges into `TTMPD.mpd`), or walks a folder for the same
   file names. It copies the 25 font files the global client loads into `<plugin config>/fonts`
   and refuses a pack that lacks any of them.
2. **Ask Penumbra first.** Over IPC, `Penumbra.ResolvePlayerPath` and then
   `Penumbra.ResolveDefaultPath`. If any font path resolves to another file, Penumbra owns the font
   and the built-in path stays off.
3. **Redirect.** Hook `ReadSqPack` (signature) and read the game's `ReadFile` (signature). For a
   replaced path, set `FileMode = LoadUnpackedResource`, write the UTF-16 path into the descriptor
   at `0x70`, put a file interface with the path at offset `0x21` on the stack, point the
   descriptor's interface field at `0x30` to it, then call the game's `ReadFile`. Other paths, and
   any descriptor another plugin already changed, go to the original.
4. **Reload.** Call `AtkModule` virtual slot 43 with `(atkModule, false, true)` after installing
   and after removing the hooks, so the game rebuilds its fonts from the new files, or from the
   SqPack again.
5. **Toggle off / unload.** Dispose the hooks, clear the map, and reload, so the stock font comes
   back.

The plugin ships no font data: the pack stays the user's file. The redirection follows the MIT
VFXEditor code at commit `8be61a5`; see `THIRD-PARTY-NOTICES.md`.
