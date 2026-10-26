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
   translation in its own ImGui window and loads a separate CJK font.

The project is in early development. Expect the config format, artifact format, and sheet list
to change.

## Architecture

```
src/Sinophone/
  Plugin.cs               Entry point. Services, windows, /sinophone command, config wiring.
  Configuration.cs        Saved settings. Changing a field changes the config schema.
  Data/
    TranslationArtifact.cs  Artifact shape: Sheets[sheet][rowId][field] = string[]
    TranslationStore.cs     Loads embedded artifacts. Builds English -> Chinese lookups.
    SheetCatalog.cs         Which game sheets to index and how to read each one.
    MacroNormalizer.cs      Text cleanup for matching and for display.
  Handlers/
    TalkHandler.cs        Watches the Talk and TalkSubtitle addons. Reads text. Feeds the overlay.
  UI/
    OverlayWindow.cs      The translation overlay.
    ConfigWindow.cs       Settings window.
    FontManager.cs        CJK font loading and font atlas rebuilds.
tools/Sinophone.DataPrep/
  Program.cs              CSV -> gzipped JSON converter.
```

Data flow, in order:

```
CSV dump --(DataPrep)--> Sinophone.<lang>.json.gz --(embedded)--> TranslationStore
live game sheet row --(SheetCatalog)--> English text --(MacroNormalizer)--> dictionary key
Talk addon event --(TalkHandler)--> English text + name --(TranslationStore)--> Chinese
Chinese --(OverlayWindow)--> screen
```

Two files hold the same sheet list: `SheetCatalog.Create` and the `Sheets` array in
`tools/Sinophone.DataPrep/Program.cs`. If you add, remove, or rename a sheet, change both in the
same commit. A mismatch is silent: the artifact loads but rows are skipped.

## Commands

The plugin targets `net10.0-windows7.0` and `win-x64`. It needs a local Dalamud install to
resolve the `Dalamud.NET.SDK/15.0.0` SDK. Set `DALAMUD_HOME` to that folder, or let the SDK
find XIVLauncher's copy.

Use the scripts. They check their inputs and give a clear error instead of failing halfway.

```sh
# Build everything
pwsh -File scripts/Build.ps1

# Run all tests
pwsh -File scripts/Test.ps1

# Run a subset
pwsh -File scripts/Test.ps1 -Filter "FullyQualifiedName~MacroNormalizer"

# Download Dalamud libraries if you do not have a game install
pwsh -File scripts/Get-DalamudLib.ps1

# Build the CN and TC artifacts from the datamining dumps
pwsh -File scripts/New-TranslationData.ps1 -CnSource <cn-csv> -TcSource <tc-csv> -GameVersion <patch>

# Regenerate the test fixture (rarely needed)
pwsh -File scripts/Update-TestFixture.ps1 -CnSource <cn-csv> -TcSource <tc-csv> -EnSource <en-csv>
```

The underlying commands, if you need to run one step by hand:

```sh
dotnet build Sinophone.sln -c Release
dotnet test Sinophone.sln -c Release

dotnet run --project tools/Sinophone.DataPrep -- \
  --source <csv-dir> --language CN \
  --output src/Sinophone/Resources/Sinophone.cn.json.gz --game-version <patch>
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

## Testing

### Unit tests

Every function that can be tested without the game gets a unit test. Priority order:

1. `MacroNormalizer.NormalizeKey` and `CleanForDisplay`. Macro stripping, invisible characters,
   whitespace collapse, line break handling, empty input.
2. `TranslationStore` lookup: exact match, missing key, empty key, fallback ordering.
3. `Configuration` defaults and `ConfigWindow` reset behavior.

Use xUnit. Put tests in `tests/Sinophone.Tests`. Name tests
`Method_Behaviour_WhenCondition`.

Test projects use `Dalamud.NET.Sdk` only to resolve `DalamudLibPath`, and
`tests/Directory.Build.targets` flips the Dalamud references to `Private="true"` so they are
copied next to the test assemblies. Without that, tests fail at runtime with
`Could not load file or assembly 'Dalamud'`.

### Integration tests

These cover the parts that wire systems together. Put them in `tests/Sinophone.IntegrationTests`.

Required coverage:

- `TranslationStore.Load` reads the fixture artifacts and resolves every fixture row.
- The artifact on disk has the shape `TranslationStore` and `SheetCatalog` expect.
- `TalkHandler.Resolve` picks the right content for a line, for both variants.
- DalaMock can build a container with every service the plugin asks for.

The DalaMock tests skip themselves when no FFXIV install is found, because DalaMock needs the
game's sqpack files. Set `SINOPHONE_FFXIV_PATH` to the folder containing `game/` to run them.

DalaMock cannot drive addon events: `MockAddonLifecycle` accepts listeners and never calls them,
and Dalamud keeps the addon args constructors internal. Do not try to fake addon events. Test the
decision logic directly through `TalkHandler.Resolve`, and cover the rest in game.

### Test data

Do not commit full translation artifacts. They are large and change every game patch.

Commit the small fixture in `tests/fixtures/` instead: eight real rows per sheet, in both
variants, plus the English text in `rows.json`. It is checked in, so tests are fast and offline.

Regenerate it with `scripts/Update-TestFixture.ps1`, which runs DataPrep's `--fixture` mode. Never
hand-edit `rows.json` or the `.json.gz` files.

### DalaMock

DalaMock runs the plugin outside the game. Use `DalaMock.Core` (currently 6.2.1, which targets
`net10.0-windows7.0`). Build a `MockContainer` with `CreateWindow = false` and `askPath: false`;
without those two flags it opens an ImGui window and a native folder picker.

Do not mock what you can test directly. `MacroNormalizer` is a static class with no services;
test it plainly.

### Writing tests

- Each test checks one behaviour, and the name says which: `Load_FallsBackToOtherVariant_ForMissingKeys`.
- A failing test should tell you what broke. Assert on the specific value, not just "not null".
- When a test fails, decide whether the test or the code is wrong before changing either.
  `NormalizeKey_StripsLineBreakMacros` failed on first run and exposed a real bug: `<br>` was
  deleted instead of becoming a space. Fix the code, not the test.

### In-game testing

Some behavior can only be checked in the game: overlay position, font rendering, and how the
overlay behaves during cutscenes. Unit tests cannot cover these.

When a change needs in-game testing, put a checklist in the pull request body:

```markdown
### In-game test

**Setup**
- Plugin loaded, `/sinophone` opens the settings window.
- Variant: Traditional. Fallback: on.

**Steps**
1. Talk to an NPC with dialogue.
2. Wait for the text to appear.

**Expected**
- Overlay appears above the Talk window, centered.
- Chinese line on top, English line below in grey.
- Speaker name in yellow above the Chinese line.

**If it fails**
- No overlay: check the Status section for "No translation data loaded".
- Chinese shows as boxes: font not found, check the Status section.
- Wrong position: check "Distance from screen bottom".
```

Fill in the real steps and expected result. Do not write "test in game" and stop.

## Logging and performance

### Logging

Add a debug log where a future reader would otherwise have to guess. Good places:

- A lookup that fails when you expected it to succeed.
- A branch that hides the overlay or skips a sheet.
- A load step with a count and a duration.

Use the right level:

- `Debug`: per-event detail, lookups, sheet indexing, skipped rows.
- `Info`: startup summary, artifact load time and entry counts. Once per load, not per frame.
- `Warning`: something missing that the plugin can work around.
- `Error`: a feature is broken and the user will notice.

Never log inside `OverlayWindow.Draw` or `PreDraw`. Those run every frame. Log when the content
changes instead.

Use structured logging. Pass values as parameters, not by string concatenation:

```csharp
log.Debug("[Sinophone] No translation for {Length} char line.", text.Length);
```

### Performance

The overlay draws every frame while a Talk window is open. Keep these rules:

- No allocation in `Draw` or `PreDraw`. Reuse lists and buffers. `OverlayWindow` already keeps a
  `_blocks` list; clear it, do not create a new one.
- No string building on a cache hit. `TryGetDialogue` should return the stored string, not a copy.
- Do not re-read game sheets per frame. Index once at load.
- Rebuild the font atlas only when font settings change. `FontManager.Rebuild` already checks this;
  keep the check.
- Normalization runs once per lookup. Do not call it twice for the same text.

When you add work to a per-frame path, say in the PR what the cost is and why it is acceptable.

## Writing

Write plain text. Use short sentences and common words.

- Say "use" not "utilize". Say "start" not "initialize". Say "check" not "ascertain".
- Say "we do not have data for this line" not "the lookup exhibits a miss condition".
- One idea per sentence. If a sentence has three commas, split it.
- Explain what the reader needs to know. Do not pad.

This applies to commit messages, PR bodies, code comments, log messages, and documentation.

Comments explain why, not what. The code already says what it does.

## Boundaries

**Always**

- Run the build and the tests before opening a pull request.
- Add a test when you add a function that can be tested.
- Add a debug log at a new decision point.
- Change both sheet lists together.
- Update this file when you learn something a future agent needs.

**Never**

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
  a miss.
- **Sheet coverage.** Only dialogue in the indexed sheets is translated. Text built at runtime is
  not.
- **Windows only.** The plugin targets `win-x64` and looks for fonts in the Windows font folder.
- **Data lag.** The CN and TC dumps trail the live game, so new lines may have no translation.

## References

- [Dalamud plugin docs](https://dalamud.dev/)
- [Dalamud API reference](https://dalamud.dev/api/)
- [Dalamud source](https://github.com/goatcorp/Dalamud)
- [ffxiv-datamining-cn](https://github.com/thewakingsands/ffxiv-datamining-cn)
- [ffxiv-datamining-tc](https://github.com/thewakingsands/ffxiv-datamining-tc)
- [DalaMock](https://github.com/Critical-Impact/DalaMock)
- [AGENTS.md format](https://agents.md/)

When you find a source that explains something this file does not, add it here with one line
saying what it covers.
