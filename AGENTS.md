# AGENTS.md

Guidance for coding agents working on Sinophone. Read this before changing code.

## Project

Sinophone is a Dalamud plugin for Final Fantasy XIV. It shows the official Simplified (CN) or
Traditional (TC) Chinese translation of dialogue in an overlay above the game's Talk window.

The plugin does not call a translation service. It ships a lookup table built from official
Chinese game data, then matches English text read from the game against it.

Three ideas explain the whole plugin:

1. **Data is built ahead of time.** `tools/Sinophone.DataPrep` turns datamining CSV files into
   gzipped JSON. Those files are embedded in the plugin assembly.
2. **English text is the key.** At load, the plugin reads each row of the live game sheets and
   maps the English text to the Chinese text. Lookup is a dictionary hit at runtime.
3. **The game cannot draw Chinese.** Its fonts have no CJK glyphs, so the plugin draws the
   translation in its own ImGui window and loads a separate CJK font. A font mod replaces the
   game's font files and makes the game itself draw Chinese; the alpha native mode uses that to
   write into the game's own dialogue box. It is off by default.

The project is in early development. Expect the config format, artifact format, and sheet list
to change.

## Architecture

The plugin is a data pipeline plus a runtime lookup plus addon handlers. DataPrep builds
gzipped artifacts from game dumps; `TranslationStore` indexes them; handlers feed the
overlay or the native text writer.

Read [docs/architecture.md](docs/architecture.md) for the file map, data flow, id-first
dialogue lookup, overlay behavior, native text mode, and runtime data updates.

The rule to keep in mind when changing the matcher: try ids before text, and let every
id layer fail open. `DefaultTalk` row ids are packed, so use `EventId.Id`, not `EntryId`.

Chinese `Addon` rows write runtime values as `<Format(IntegerParameter(n),...)/>` and
`<Time(...)/>`. Lumina parses both as value macros. Substitute the whole `Format` tag with the
value the English line showed. Replacing only the inner expression lets the macro strip remove
the number. A `Time` tag draws nothing: it fills the date and clock registers the row reads with
`TopLevelParameter(n)`. Fill those from the date and clock the English line shows, and drop the
`Time` tag.

The English line is the only source for a date. A SetTime prompt is built from the rendered
English text, so a missing value stays English. The English client writes the date month first,
and a number above 12 marks the day-first form.

## Commands

The plugin targets `net10.0-windows7.0` and `win-x64`. It needs a local Dalamud install to
resolve the `Dalamud.NET.SDK/15.0.0` SDK. Set `DALAMUD_HOME` to that folder, or let the SDK
find XIVLauncher's copy.

Use the scripts. They check their inputs and give a clear error instead of failing halfway.

```sh
# Build everything
pwsh -File scripts/Build.ps1

# Build and link the plugin into a Dalamud dev plugin folder for in-game testing
pwsh -File scripts/Deploy-DevPlugin.ps1 -DevPluginPath <dev-plugin-folder>

# Run all tests
pwsh -File scripts/Test.ps1

# Run a subset
pwsh -File scripts/Test.ps1 -Filter "FullyQualifiedName~MacroNormalizer"

# Download Dalamud libraries if you do not have a game install
pwsh -File scripts/Get-DalamudLib.ps1

# Build the CN and TC artifacts from the datamining dumps
pwsh -File scripts/New-TranslationData.ps1 -CnSource <cn-csv> -TcSource <tc-csv> -EnSource <en-csv> -GameVersion <patch>

# Regenerate the test fixture (rarely needed)
pwsh -File scripts/Update-TestFixture.ps1 -CnSource <cn-csv> -TcSource <tc-csv> -EnSource <en-csv>

# Regenerate the simplified/traditional conversion tables from OpenCC
pwsh -File scripts/New-ConversionData.ps1
```

The underlying commands, if you need to run one step by hand:

```sh
dotnet build Sinophone.sln -c Release
dotnet test Sinophone.sln -c Release

dotnet run --project tools/Sinophone.DataPrep -- \
  --source <csv-dir> --language CN \
  --script-source <cn-dump-root> --en-source <en-dump-root> \
  --output src/Sinophone/Resources/Sinophone.cn.json.gz --game-version <patch>

# Print the sheet and script folder names the tool expects (the data workflow uses these)
dotnet run --project tools/Sinophone.DataPrep -- --list-sheets
dotnet run --project tools/Sinophone.DataPrep -- --list-scripts
```

The plugin finds artifacts by filename suffix (`Sinophone.cn.json.gz`, `Sinophone.tc.json.gz`).
Do not rename them. `Build.ps1` fails when no artifacts exist, because a plugin built without
them compiles fine and translates nothing.

### Scripts

Any step you run more than once belongs in `scripts/` as a PowerShell 7 script. Do not leave
multi-step work as prose in a PR description.

Rules for scripts:

- Target PowerShell 7 (`pwsh`). It runs on Windows, Linux, and macOS, so one file covers all.
- Start with `Set-StrictMode -Version Latest` and `$ErrorActionPreference = 'Stop'`.
- Accept paths as parameters. Never hardcode a user path, drive letter, or Dalamud location.
- Check inputs before acting and print what the script will do.
- A script must be safe to run twice. Creating a directory or regenerating an artifact is fine.
  Deleting or overwriting user files is not.
- Add `-WhatIf` support for anything that writes or deletes.
- Keep the script under about 100 lines. If it grows past that, it is probably two scripts.

## Git workflow

`main` is the only long-lived branch and must always build.

1. **Branch per feature.** Create the branch from an up-to-date `main` before the first edit.
   Use a prefix: `feat/`, `fix/`, `docs/`, `chore/`, `test/`, `refactor/`.
2. **Commit at milestones.** One commit per working stage, not per file and not one giant commit
   at the end. A stage is something you can describe in one sentence, such as "add the sheet
   descriptor" or "wire the handler to the overlay".
3. **Push when a stage is done.** Do not sit on unpushed work across sessions.
4. **Open one pull request when the feature is complete.** Not per stage. The PR body says what
   changed, why, and how it was tested.
5. **Squash merge, then delete the branch.** The squash commit message follows the same rules as
   a normal commit.

Commit messages use Conventional Commits, matching the existing history:

```
<type>(<scope>): <short summary>

<optional body: what changed and why>
```

Types in use: `feat`, `fix`, `docs`, `test`, `refactor`, `chore`, `perf`. Scope is the area, such
as `data`, `ui`, `handler`, `tooling`.

Never force-push to `main`. Never commit to `main` directly.

### Releases

Push a `vX.Y.Z` tag to release. `.github/workflows/release.yml` builds `latest.zip` with the
Dalamud packager, attaches it to the release, and updates `pluginmaster.json` on `main` with the
assembly version and a new `LastUpdate`.

`pluginmaster.json` is the custom repository manifest users add in Dalamud. It must stay a
JSON array, even when it has one plugin, or Dalamud cannot load the repository. Its download
links point at `releases/latest/download/latest.zip`, so they do not change per release. Keep the `data` release
separate and out of "latest": it holds translation data, not plugin builds.

## Testing

Run `pwsh -File scripts/Test.ps1` before opening a pull request. Add a unit test for
anything that can be tested without the game, and an integration test for wiring that
uses the checked-in fixture.

Read [docs/testing.md](docs/testing.md) for test layout, fixture rules, naming, and the
in-game checklist format. Native text changes also need
[docs/native-mode-checklist.md](docs/native-mode-checklist.md).

## Logging and performance

Use structured logs at the right level, and never log inside `Draw` or `PreDraw`. Keep
per-frame paths allocation-free and do not re-index game sheets per frame.

Write through the component's `PluginLog`, not `IPluginLog`. It filters `Debug` and `Info`
by the group the user picks in the settings **Logs** tab. Warnings and errors always show. A
new group needs an entry in `LogCategory`, `Configuration`, and the tab.

Read [docs/logging-and-performance.md](docs/logging-and-performance.md) for the rules.

## Writing

Write plain text. Use short sentences and common words.

- Say "use" not "utilize". Say "start" not "initialize". Say "check" not "ascertain".
- Say "we do not have data for this line" not "the lookup exhibits a miss condition".
- One idea per sentence. If a sentence has three commas, split it.
- Explain what the reader needs to know. Do not pad.

This applies to commit messages, PR bodies, code comments, log messages, and documentation.

Comments explain why, not what. The code already says what it does.

The readme comes in three files. `README.en.md` is the source: change it first, then update the
translations. `README.md` is Traditional Chinese and is what GitHub shows by default;
`README.zh-CN.md` is Simplified Chinese. Keep the links and section order the same in all three.

## Boundaries

**Always**

- Run the build and the tests before opening a pull request.
- Add a test when you add a function that can be tested.
- Add a debug log at a new decision point.
- Change both sheet lists together.
- Update this file when you learn something a future agent needs.

**Never**

- Add a prompt-specific rule. Prefer a general rule that applies to all text, including
  conditionals, item names, and text effects. If a prompt-specific rule is the only solution,
  ask the developer before working on it.
- Commit full translation artifacts or other large generated files.
- Hand-edit a generated file. Change the generator instead.
- Rewrite a whole file to make a small change. See the next section.
- Add a NuGet package without asking.
- Commit to `main` or force-push.
- Log inside a per-frame path.

## Small changes

Make the smallest change that reaches the goal. A large rewrite is a last resort, not a shortcut.

Before you rewrite a file, ask: can I change the one function that is wrong? If yes, do that.
If a rewrite is truly needed, explain in the PR why the small change would not work.

Do not rename things, reformat files, or move code that the task did not ask about. A diff that
touches unrelated lines makes review harder and hides real changes.

Do not add abstractions for a problem you do not have yet. Three similar lines are better than a
premature interface.

## Known limitations

- **English only.** Lookup is keyed on the English text the game displays. The other three game
  languages (Japanese, German, French) are planned but not supported. This is a design change,
  not a setting. Ask before starting that refactor.
- **Exact match.** After normalization, a line must match exactly. One different character means
  a miss. Dialogue with a runtime id is tried by id first; when the game exposes no id, lookup
  falls back to this text match. Names are the exception: `ENpcResident` stores generic NPC names in lower case
  (`skywatcher`) and the game shows them capitalised (`Skywatcher`), so the name lookup ignores
  case. Dialogue lookup stays case-sensitive. The English script dumps mark italics with one
  asterisk and bold with two; the game draws that emphasis with a SeString macro, so
  `NormalizeKey` strips the asterisks and `TalkHandler.ExtractPlainText` drops the macro
  from the text node before lookup. The dumps write client macros the datamining tool cannot
  name as `<Unknown60>0262</Unknown60>`. The payload is binary data, not text, so
  `CleanForDisplay` drops the whole pair instead of leaving `0262` in the line.
- **Conditionals.** A sheet row with an `If` macro gets one key per branch, so the line the game
  shows matches. Script-dump keys come from the English dump, which resolves conditionals to one
  branch. `TranslationStore` keeps a second script index with the branch words collapsed:
  pronouns (`GenderedPronouns`), nouns of address (`GenderedNouns`: lad/lass, sir/madam), and
  the time of day (`TimeOfDayWords`: morning/day/evening/night). A line written for the other
  gender or another time of day still matches. Exact keys win, and the collapsed key is only
  tried after a miss. When two lines collapse to the same key with different Chinese, the key is
  dropped instead of guessing.
  For display, `PlayerParameter(4)` (gender) is resolved from `IPlayerState`; every other condition
  shows the first branch.
- **Player name.** The dumps write `Player Player` where a line names the player, and the game
  writes the real name. The lookup puts the name back before the dictionary hit, and the display
  fills `<Highlight>ObjectParameter(1)</Highlight>` from `IPlayerState.CharacterName`. The other
  parameter indices (55 is a chocobo, 56 one other object) are dropped, so the sentence can read
  with a gap. Text holding an `ObjectParameter` is resolved on each lookup, not at load, because
  the name changes with the logged-in character.
- **Coverage.** Dialogue comes from the indexed sheets and the script dumps, including quest
  dialogue. Quest journal and objective text is skipped because no overlay shows it. Text built at
  runtime is not covered, and text that exists in no dump stays untranslated. Check the datamining
  CSVs before assuming a lookup bug.
- **Conversion is not exact.** The optional simplified/traditional layer uses OpenCC tables with
  one candidate per key, mapped through OpenCC's Taiwan variant table because the TC game data
  uses those forms: 為, 裡, 才, not 爲, 裏, 纔. The converter skips text that is mostly already in
  the target script, so a traditional line is not converted again. That test counts characters, so
  a traditional name that uses a character the simplified side also rewrites, such as 艾默里, can
  still be converted. OpenCC's phrase choices also differ from the localisation: 别只 converts to
  別隻, where the TC data writes 別只. Converting back can pick a different variant, for example
  髮 becomes 发 and then 發. It is a reading aid, not a replacement for the official text of that
  variant.
- **Event items.** The item name surface reads the `Item` sheet. Event and key items live in
  `EventItem`, so their names stay untranslated.
- **Windows only.** The plugin targets `win-x64` and looks for fonts in the Windows font folder.
- **Native text mode needs a font mod.** Alpha and off by default. The game only draws Chinese
  once its font files are replaced. TexTools and Penumbra both install the same modpack; the font
  comes from [AtmoOmen/FFXIVFonts](https://github.com/AtmoOmen/FFXIVFonts). Without the mod, native
  mode shows the missing-glyph character and the plugin cannot tell. See
  [README](README.en.md#font-setup).
- **Display language.** The Talk overlay shows Chinese only. When a line has no translation and
  `HideWhenNoTranslation` is off, it shows the English line instead. It never shows both languages
  at once. Nameplates follow `TargetNameShowEnglish` and the item name surface follows
  `ShowOriginal`; both put the English name with the Chinese name, in brackets on one line.
  Selection dialogs have their own switch, `ShowInSelectionDialogs`, independent of Talk native mode.
- **Native item names.** `ItemNameNativeMode` writes the Chinese name into the game's own item
  tooltip (`ItemDetail`, field `ItemNameText`) instead of drawing the overlay. It needs a font mod.
  The name is written on `HoveredItemChanged` and again on the tooltip addon's `PostSetup` and
  `PostRefresh`, because the hover can change before the addon has its new text. A tooltip that
  has no name node falls back to the overlay, so native mode never shows nothing. The tooltip
  writes its own name again while it is open, so the handler also reapplies on every `PreDraw`,
  the same guarded per-frame write the Talk window uses. The tooltip sizes the name node to the
  English name, so the handler clears `WordWrap` and grows the width to the text; do not drop that
  step or a Chinese name wraps into lines of a few characters. The height goes back to the size
  the game gave the English name and stays there, also for a two-line name: the tooltip draws the
  line at the bottom of the node, so an extra line stacks upward, and growing the height pushes
  the English line down over the item description. When both languages are wider than the tooltip
  window, the English name moves to a second line. A write also saves the node's width and height,
  and the restore puts them back, so the English name returns to the exact place it was drawn
  before. The saved English name goes back when the setting is turned off; `PostHide` and
  `PreFinalize` drop it instead, because the node may already belong to another addon.
- **Native nameplates.** `ShowTargetName` puts Chinese names into the game's own nameplates above
  every visible NPC or object, such as a summoning bell, company chest, or door, not only the
  target. It needs a font mod and nameplates visible in the game settings. The names change
  through Dalamud's nameplate API
  (`INamePlateGui.OnNamePlateUpdate`), which runs before the game lays the plate out, so the game
  measures and bakes the Chinese text itself. Do not write the text node after the addon update,
  and do not change `NamePlateObject.TextW` or `TextH`: both cut the sides off a wider name and
  damaged the shared bake atlas. A plate shows one line, so the English name follows the Chinese
  name in brackets. A plate slot carries no object id, so the translation follows the name text;
  two visible objects with the same name both get the Chinese name. `RequestRedraw` applies and
  removes the names at once. Player characters are skipped.
- **Data lag.** The CN and TC dumps trail the live game, so new lines may have no translation. A
  release build carries no data files, so the first load downloads about 60 MB from the `data`
  release. Later loads only check for a new version.

## References

- [Dalamud plugin docs](https://dalamud.dev/)
- [Dalamud API reference](https://dalamud.dev/api/)
- [Dalamud source](https://github.com/goatcorp/Dalamud)
- [ffxiv-datamining-cn](https://github.com/thewakingsands/ffxiv-datamining-cn)
- [ffxiv-datamining-tc](https://github.com/thewakingsands/ffxiv-datamining-tc)
- [xivapi/ffxiv-datamining](https://github.com/xivapi/ffxiv-datamining) — English CSVs and script
  dumps under `csv/en`
- [AGENTS.md format](https://agents.md/)
- [docs/architecture.md](docs/architecture.md) — file map, data flow, id matching, native mode.
- [docs/testing.md](docs/testing.md) — unit, integration, fixture, and in-game testing.
- [docs/logging-and-performance.md](docs/logging-and-performance.md) — log levels and per-frame rules.
- [docs/text-coverage.md](docs/text-coverage.md) — which game sheets carry text, which are indexed,
  and the order to add the rest.

When you find a source that explains something this file does not, add it here with one line
saying what it covers.
