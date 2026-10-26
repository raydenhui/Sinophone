# Text coverage

Which sheets Sinophone translates today, which ones are missing, and what to add next.

The data comes from the CN and TC CSV dumps in the two datamining repositories. The reference for
"all text" is [FFXIVChnTextPatch-GP](https://github.com/GpointChen/FFXIVChnTextPatch-GP): it patches
444 top-level sheets. Sinophone indexes 12.

Row counts below are rows containing Chinese text in the CN dump, counted on 2026-10-26. They show
how much text a sheet adds. They do not predict how many lines the game will hit at runtime.

## Indexed today

| Sheet | Field | Kind | Used for |
| --- | --- | --- | --- |
| `DefaultTalk` | `Text[0..2]` | Dialogue | Standard NPC dialogue |
| `Balloon` | `Dialogue` | Dialogue | NPC chat bubbles |
| `NpcYell` | `Text` | Dialogue | Enemy and boss shouts |
| `InstanceContentTextData` | `Text` | Dialogue | Dungeon and raid narration |
| `ContentTalk` | `Text` | Dialogue | Duty NPC lines |
| `SnipeTalk` | `Text` | Dialogue | Snipe event dialogue |
| `GuildleveAssignmentTalk` | `Talk[0..7]` | Dialogue | Guildleve NPC dialogue |
| `CraftLeveTalk` | `Talk[0..5]` | Dialogue | Crafting leve dialogue |
| `ENpcResident` | `Singular` | Name | NPC speaker names |
| `BNpcName` | `Singular` | Name | Enemy names |
| `SnipeTalkName` | `Name` | Name | Snipe event names |
| `EObjName` | `Singular` | Name | Names of objects you interact with |

Both files must change together: `SheetCatalog.Create` and the `Sheets` array in
`tools/Sinophone.DataPrep/Program.cs`. `FixtureBuilder` keeps its own smaller list on purpose; it
only needs sheets that the tests use. The Data workflow downloads whatever DataPrep lists, so it
needs no change.

## Missing dialogue

### Sheets with named columns

These can be added to DataPrep by name. Each still needs the matching Lumina property in
`SheetCatalog`.

| Sheet | Field | Rows | What it contains |
| --- | --- | ---: | --- |
| `GoldSaucerTextData` | `Text` | 71 | Gold Saucer text |
| `CSBonusTextData` | `Text` | 10 | Content bonus text |
| `RideShootingTextData` | `String` | 3 | Ride shooting minigame |
| `QuickChatTransient` | `Text{Output}` | 39 | Quick chat phrases |
| `MKDLore` | `Name`, `Description` | 60 | Occult Crescent lore |
| `PublicContentTextData` | `TextData` | 726 | Public content narration |
| `PartyContentTextData` | `Data` | 41 | Party content text |

### Sheets with unnamed columns

The CN and TC dumps give these a positional header (`#,,,,`) and a type row. Pick the column by
index. The English community dump names the same fields, but its column order differs for some
sheets, so do not copy its order.

| Sheet | Text column | Rows | What it contains |
| --- | --- | ---: | --- |
| `GimmickTalk` | last column | 67 | Object interaction text |
| `GimmickBill` | column 1 | 95 | Signboards and world text |
| `GimmickYesNo` | columns 1, 2, 3 | 161 | Prompt, yes label, no label |
| `GoldSaucerTalk` | columns 18 and up | 477 | Message, then choice labels |
| `MassivePcContentTextData` | column 1 | 499 | Large-scale content text |
| `WKSMissionText` | column 1 | 386 | Cosmic Exploration missions |
| `WKSText` | column 1 | 29 | Cosmic Exploration text |
| `WKSEmergencyInfoText` | column 1 | 102 | Cosmic Exploration alerts |
| `HWDDevLevelWebText` | columns 1, 2 | 125 | Firmament text, in-progress text |
| `InclusionShopWelcomText` | column 1 | 14 | Inclusive shop welcome |

Column numbers count the key column as 0, so "column 1" is the first field after the row id.

`GimmickTalk`, `GimmickBill`, `GimmickYesNo`, and `GoldSaucerTalk` are likely shown in the Talk
window. The others have their own UI and need a new handler. Verify each in game before claiming
it works.

## Checked and found empty

These sheets look like text sources but contain no prose. Do not spend time on them.

- `ContentNpcTalk`, `ContentDirectorBattleTalk`, `MassivePcContentBattleTalk`: row ids that point
  at `ContentTalk`.
- `ItemConvertTalk`, `ChatBubbleSampeTextList`, `AddonTalkParam`, `ContentTalkParam`: numbers and
  flags.
- `MovieSubtitle`, `MovieSubtitle500`, `MovieSubtitleVoyage`: start and end times only.
- `CutsceneName`, `SwitchTalk`, `SwitchTalkVariation`, `TinyCustomTalk`, `Story`, `ScenarioTree`,
  `ScenarioTreeTips`: no Chinese text in the CN dump.
- `CustomTalk`: only option labels (`MainOption` 263 rows, `SubOption` 76 rows). The lines it
  starts come from `DefaultTalk` and the talk sheets it points at.

## Column layout

The CN and TC dumps export the raw game schema. Named columns match Lumina property names, which is
why the current 11 sheets work. Unnamed columns do not: Lumina may expose them as `UnknownN`, and
the game keeps string columns at the end of the row.

Check the property against a real row before adding a descriptor. A wrong field compiles and
silently misses every lookup.

## Future sheets

### Names

Items, actors, and places. These feed the name lookups and any new surface that shows a name.

- `Item`: `Singular`, `Name`, `Description`. The largest win and the largest file (18 MB of CSV).
- `PlaceName`, `PlaceNameReplace`.
- `GatheringPointName`, `FishingSpot`, `FishParameter`.
- `Mount`, `Companion`, `Pet`, `Emote`, `Orchestrion`, `Title`, `ClassJob`, `ClassJobCategory`.
- `Action`, `ActionTransient`, `Status`, `Trait`, `TraitTransient`, `GeneralAction`, `CraftAction`,
  `PvPAction`.

### Items and shops

- `ItemUICategory`, `ItemSearchCategory`, `ItemSeries`, `ItemSpecialBonus`, `Materia`.
- `SpecialShop`, `GilShop`, `GCShop`, `FccShop`, `InclusionShop`, `CollectablesShop`,
  `DisposalShop`, `Stain`.

### Quests and journal

- `Quest` (name only), `CompleteJournal`, `JournalCategory`, `JournalGenre`, `JournalSection`.
- `Completion`, `LegacyQuest`, `LeveString`.
- `Description`, `DescriptionPage`, `DescriptionString`, `DescriptionStandAlone`.

### UI and system

- `Addon`, `AddonTransient`, `Hud`, `MainCommand`, `MainCommandCategory`.
- `LogMessage`, `Error`, `LogKind`, `LogFilter`, `OnlineStatus`.
- `HowTo`, `HowToPage`, `Guide*`, `MultipleHelp*`, `ContentsTutorial*`, `EventTutorial*`.
- `TextCommand`, `TextCommandParam`.
- `Map`, `TerritoryType`, `Town`.

### Achievements and collection

- `Achievement`, `AchievementCategory`, `AchievementKind`.
- `TripleTriadCard`, `TripleTriadCardType`, `TripleTriadRule`.
- `SpearfishingItem`, `SpearfishingEcology`.
- `SkyIsland2Mission*`, `WKS*`, `MKD*` names.

The long tail is the GP list above. Do not try to match all 1,235 CSVs in the dump; many have no
display text at all.

## How text reaches the screen

The overlay only covers the Talk window. More sheets alone will not show more text.

| Surface | Route | Status |
| --- | --- | --- |
| Talk, subtitles, chat bubbles | Overlay | Works |
| Target info | New handler: read the target's name id, look up the name, draw near `TargetInfo` | Not started |
| Item tooltip | New handler: read the item id from `ItemDetail`, draw near the tooltip | Not started |
| Inventory, shops, menus, chat | Native text replacement, then the CJK font mod | Planned as separate items |

Full parity with FFXIVChnTextPatch-GP is not reachable with an overlay. That patch rewrites the game
files, so it also changes text inside lists and strings assembled at runtime ("You obtained 3 ×
..."). A dictionary matches whole strings, so runtime-built text stays English.

## Category filtering

Add a category to the sheet descriptors and filter when indexing:

1. `SheetCategory` enum: `Dialogue`, `Names`, `Items`, `Actions`, `Quests`, `Ui`, `Content`.
2. `ISheetDescriptor` gains `Category`.
3. `TranslationStore.Index` skips categories the user turned off.
4. Settings gets one checkbox per category. Dialogue and names on by default.

The artifact does not need a format change: the category belongs to the descriptor, and the sheet
name in the artifact already identifies the rows.

## Size

Only the selected fields are stored, not the whole CSV. No full artifact has been measured yet.
For scale: `DefaultTalk.csv` is 2.5 MB, `ENpcResident.csv` is 2.6 MB, `Item.csv` is 18 MB, and
`Action.csv` is 14 MB. Most of `Item` and `Action` is numbers, so the text fields compress well.

Adding `Item` names and descriptions is the big step. If downloads get uncomfortable, split into a
core artifact (dialogue and names) and a full artifact (items and UI), and let the updater fetch
what the settings ask for. Measure before splitting.

## Order of work

1. Named-column dialogue sheets: `PublicContentTextData`, `PartyContentTextData`,
   `GoldSaucerTextData`, `CSBonusTextData`, `QuickChatTransient`, `RideShootingTextData`,
   `MKDLore`.
2. Unnamed-column dialogue sheets. Map one sheet, add it, check it in game, then the next.
3. Name sheets plus the target-info and item-tooltip overlays.
4. Category filtering.
5. Native UI replacement and the font mod.

## Adding a sheet

1. Add an entry to `Sheets` in `tools/Sinophone.DataPrep/Program.cs` with the CSV column names, or
   with the positional fallback when the dump has no names.
2. Add the matching descriptor in `SheetCatalog.Create`, using the Lumina property.
3. If the sheet should be in the test fixture, add it to `FixtureSheets` and `EnglishColumns` in
   `tools/Sinophone.DataPrep/FixtureBuilder.cs`, regenerate with `scripts/Update-TestFixture.ps1`,
   and add a unit test that a fixture row resolves.
4. Build the artifact and check the entry count in the Status section.
5. Check it in game. A sheet can index fine and still never reach a surface.

## References

- [ffxiv-datamining-cn](https://github.com/thewakingsands/ffxiv-datamining-cn)
- [ffxiv-datamining-tc](https://github.com/thewakingsands/ffxiv-datamining-tc)
- [ffxiv-datamining (English, community field names)](https://github.com/xivapi/ffxiv-datamining)
- [FFXIVChnTextPatch-GP sheet list](https://github.com/GpointChen/FFXIVChnTextPatch-GP/tree/master/resource/rawexd)
