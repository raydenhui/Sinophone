# Text coverage

Which sheets Sinophone translates today, which ones are missing, and what to add next.

The data comes from the CN and TC CSV dumps in the two datamining repositories. The dump has 1,235
root CSVs, and 402 of them contain Chinese text. Sinophone indexes 30; 372 are left. Issue #38
records the measured totals.

The reference for "all text" is
[FFXIVChnTextPatch-GP](https://github.com/GpointChen/FFXIVChnTextPatch-GP). It ships 444 top-level
CSVs, but only 382 of them contain Chinese, and the dump covers all 382. The other 62 carry no
Chinese.

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
| `GoldSaucerTextData` | `Text` | Dialogue | Gold Saucer text |
| `CSBonusTextData` | `Text` | Dialogue | Content bonus text |
| `RideShootingTextData` | `String` | Dialogue | Ride shooting minigame |
| `QuickChatTransient` | `Text` | Dialogue | Quick chat phrases |
| `PublicContentTextData` | `TextData` | Dialogue | Public content narration |
| `PartyContentTextData` | `Data` | Dialogue | Party content text |
| `GimmickTalk` | `Message` | Dialogue | Object interaction text |
| `GimmickBill` | `Text` | Dialogue | Signboards and world text |
| `GimmickYesNo` | `Message`, `YesButton`, `NoButton` | Dialogue | Prompt, yes label, no label |
| `GoldSaucerTalk` | `Message`, `ChoicesText` | Dialogue | Message, then choice labels |
| `MassivePcContentTextData` | `Text` | Dialogue | Large-scale content text |
| `WKSMissionText` | `Text` | Dialogue | Cosmic Exploration missions |
| `WKSText` | `Text` | Dialogue | Cosmic Exploration text |
| `WKSEmergencyInfoText` | `Text` | Dialogue | Cosmic Exploration alerts |
| `HWDDevLevelWebText` | `Unknown0`, `Unknown1` | Dialogue | Firmament text, in-progress text |
| `InclusionShopWelcomText` | `Unknown0` | Dialogue | Inclusive shop welcome |
| `MKDLore` | `Name` | Name | Occult Crescent lore names |
| `MKDLore` | `Description` | Dialogue | Occult Crescent lore text |
| `ENpcResident` | `Singular` | Name | NPC speaker names |
| `BNpcName` | `Singular` | Name | Enemy names |
| `SnipeTalkName` | `Name` | Name | Snipe event names |
| `EObjName` | `Singular` | Name | Names of objects you interact with |
| `Item` | `Name` | Name | Item names, shown on the item tooltip |

Both files must change together: `SheetCatalog.Create` and the `Sheets` array in
`tools/Sinophone.DataPrep/Program.cs`. `FixtureBuilder` keeps its own smaller list on purpose; it
only needs sheets that the tests use. The Data workflow downloads whatever DataPrep lists, so it
needs no change.

## Positional sheets

These sheets are indexed now. The CN and TC dumps give them a positional header (`#,,,,`) and a
type row, so the plugin reads the text column by index. The English community dump names the same
fields, but its column order differs for some sheets, so do not copy its order.

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
window, so the overlay should pick them up with no new handler. The others have their own UI and
need a handler before the text appears. The in-game check for each sheet is a checklist item on
the pull request that added it.

## Script dumps

Some dialogue has no Lumina row. It lives in the datamining repositories' script folders. The
artifact stores it in its `Scripts` section, keyed by the text key (`TEXT_...`) instead of a sheet
and row id. The English text travels with the artifact because there is no sheet to read it from.

| Folder | Lines indexed | What it holds |
| --- | ---: | --- |
| `quest/` | 211,790 | Quest dialogue, spoken by NPCs during quests |
| `custom/` | 31,023 | NPC interaction dialogue, including the Sundhimal aetherial travel line |
| `cut_scene/` | 27,141 | Voiced cutscene dialogue, shown in `TalkSubtitle` |
| `content/`, `dungeon/`, `raid/`, `transport/`, and the rest | 1,779 | Instance, raid, leve, shop, and transport text |

Entries are paired across CN, TC, and English by text key. A line with no English counterpart is
skipped, because the plugin has nothing to match it against.

`quest/` files hold more than dialogue: journal steps (`_SEQ_`), objectives (`_TODO_`), system
messages (`_SYSTEM_`), and unused markers (`_QIB_`). None of those appear in the Talk window, so
`ScriptDump` drops them. That leaves 211,790 of the 266,230 paired quest lines. Only about 2% of
the kept lines contain markup or parameters, so most match the text the game shows.

The English dump resolves conditionals to one branch, while the CN and TC dumps keep the tags.
Script lines with a gender conditional therefore match only the branch the English dump chose.
Sheet rows do not have this problem: the plugin reads the live SeString and keys every branch.

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
why the named sheets work. Unnamed columns do not: Lumina may expose them as `UnknownN`, and the
game keeps string columns at the end of the row.

Check the property against a real row before adding a descriptor. A wrong field compiles and
silently misses every lookup.

## Future sheets

### Names

Items, actors, and places. These feed the name lookups and any new surface that shows a name.

- `Item`: `Singular` and `Description` are still missing. `Name` is indexed now. The sheet is the
  largest file (18 MB of CSV).
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
| Target info | `TargetNameHandler` reads the target on `TargetInfo` events, draws under the target bar | Works |
| Item tooltip | `ItemNameHandler` reads the hovered item, draws above the cursor | Works |
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

Measured on 2026-10-26 from the artifacts the Data workflow built and published on the `data`
release, using the 11 sheets indexed at the time:

| File | Size |
| --- | ---: |
| `Sinophone.cn.json.gz` | 1.62 MiB |
| `Sinophone.tc.json.gz` | 1.50 MiB |

The updater downloads both variants, so one data update is about 3.1 MiB. These numbers were
measured before the seven named-column dialogue sheets and the ten positional dialogue sheets were
added; those sheets are small and were not re-measured.

Quest dialogue changes this. Measured on 2026-10-28, the 211,790 kept quest lines add about
20.5 MiB of gzipped `Scripts` data per variant, so a built artifact is roughly 28 MiB and one
update downloads both variants, near 56 MiB. At load the extra entries cost roughly 80 MB of
memory per loaded variant.

`Item` is now indexed by `Name` only. An estimate with `Name` and `Description` for every row adds
about 0.77 MiB to CN and 0.69 MiB to TC. That puts each artifact around 2.4 MiB and 2.2 MiB.
Only the selected fields are stored, and most of `Item.csv` is numbers, so it compresses well.

**Decision: keep one artifact per variant.** Splitting into core and full artifacts would add a
manifest, updater, and settings path for a download that is still only a few MiB. Revisit when the
full sheet set pushes one variant past about 8 MiB, or if users report slow updates.

**Full coverage estimate.** Measured on 2026-10-28: indexing every string column of all 402
sheets is about 6.1 MiB gzipped per variant. The script dumps add about 5 MiB, so a full artifact
lands near 11 MiB. That is past the 8 MiB point above, so revisit the split decision before the
sheet set gets there.

## Order of work

1. ~~Unnamed-column dialogue sheets.~~ Indexed in #19. Check each in game.
2. The remaining name sheets. The target-info and item-tooltip overlays are done.
3. Category filtering.
4. Native UI replacement and the font mod.

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
