# Testing

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
They use the checked-in fixture and run offline, so they do not need a game install.

Required coverage:

- `TranslationStore.Load` reads the fixture artifacts and resolves every fixture row.
- The artifact on disk has the shape `TranslationStore` and `SheetCatalog` expect.
- `TalkHandler.Resolve` picks the right content for a line, for both variants.

Addon events cannot be tested outside the game. Dalamud keeps the addon args constructors
internal, so do not try to fake them. Test the decision logic directly through
`TalkHandler.Resolve`, and cover the rest with the in-game checklist.

### Test data

Do not commit full translation artifacts. They are large and change every game patch.

Commit the small fixture in `tests/fixtures/` instead: eight real rows per sheet and a few script
lines, in both variants, plus the English text in `rows.json`. It is checked in, so tests are fast
and offline.

Regenerate it with `scripts/Update-TestFixture.ps1`, which runs DataPrep's `--fixture` mode. Never
hand-edit `rows.json` or the `.json.gz` files.

### Writing tests

- Do not mock what you can test directly. `MacroNormalizer` is a static class with no services;
  test it plainly.
- Each test checks one behaviour, and the name says which: `Load_FallsBackToOtherVariant_ForMissingKeys`.
- A failing test should tell you what broke. Assert on the specific value, not just "not null".
- When a test fails, decide whether the test or the code is wrong before changing either.
  `NormalizeKey_StripsLineBreakMacros` failed on first run and exposed a real bug: `<br>` was
  deleted instead of becoming a space. Fix the code, not the test.

### Property tests

A rule that says "for every line" or "for every input" is tested by generating many inputs,
not by adding one more example. `CjkLineWrapperPropertyTests` and `MacroNormalizerPropertyTests`
build a few thousand lines from a fixed seed and check the rule on all of them.

- Generate from a seed. A failure then names one input and always names the same one.
- Generate realistic shapes, not random characters. A line that starts with two speaker aliases
  is not a shape the dumps write, so a rule that fails on it is testing something the game
  cannot produce. `CjkLineWrapperPropertyTests` found exactly this: the mark rules hold on
  Chinese text, not on arbitrary mixes of marks.
- Assert the rule the code promises, not a stronger one. `CjkLineWrapper` can leave a line
  starting with a closing mark when the run of marks is longer than the line. The test says so
  instead of failing on a case the code never claimed to handle.
- Check the test can fail. Break the rule in the source, watch the property test go red, then
  put it back. A property test that passes over input it never rejects proves nothing.

### Hook callbacks fail open

Every handler callback is called by the game inside a frame, so a throw there can reach the
game. `HookGuard.Run` catches it, logs a warning, and leaves the English text. A new handler
routes its callback through it; `HookGuardTests` checks the guard, so the rule is tested once
instead of once per handler. The older handlers keep their own `try/catch`.

### Coverage suite

`tools/Sinophone.Coverage` measures how much of each component's text the plugin can translate,
against the real artifacts rather than the fixture. It is an evaluation run, not part of
`scripts/Test.ps1` and not run in CI; run it when you want the numbers, after a data update, or
before a release:

```sh
pwsh -File scripts/Test-Coverage.ps1                  # tc, using the plugin's own artifacts
pwsh -File scripts/Test-Coverage.ps1 -Variant cn      # simplified data
pwsh -File scripts/Test-Coverage.ps1 -Language ja     # measure the sheet text a JP client shows
pwsh -File scripts/Test-Coverage.ps1 -Gate tools/Sinophone.Coverage/coverage-baseline.json
pwsh -File scripts/Test-Coverage.ps1 -Baseline tools/Sinophone.Coverage/coverage-baseline.json
```

It writes `coverage.md` and `coverage.json` next to the artifacts. What it does:

- Loads the text side of each sheet from the xivapi/ffxiv-datamining CSVs, downloaded once and
  cached, so the store can be built without the game. `tools/Sinophone.Coverage/Coverage/Sources.cs`
  says which sheet feeds which component; a sheet that gains a handler has to gain a line there or
  the report counts it under "shown by nobody".
- Measures one client language at a time. `-Language` defaults to `en`; `ja`, `de`, and `fr` measure
  the sheet text that client shows. Script lines stay keyed on the English dump, so a run in
  another language covers the sheet surfaces only, and the report names the language it measured.
- Enumerates every English string a component can show: every row, slot, and conditional branch,
  plus every script-dump key.
- Runs each one through that component's own `Resolve`, not a copy of it. The display half is not
  measured: no node, overlay, or font is involved.
- Reports two numbers per component. **Text** is the lookup as a miss sees it, with the line alone.
  **Id** adds the row id the game sometimes passes, which is what separates two lines that share one
  English string. For a `VOICEMAN` key the id column also carries the scene number from the key,
  because that is what the game reads out of the cutscene path when the line plays in one.
- Counts labels the game builds itself (`8:00 a.m.`, `Lv. UNKNOWN`) as **Skipped**, not as misses:
  no dictionary can key them, so counting them would understate the number.
- Builds the Addon match template from the dump text, both shapes the game fills a value into: the
  literal word `UNKNOWN`, and a run of two spaces such as the armoire prompt's item name. The gap
  shape is also probed with a value in it, so a prompt like `Placing Copper Ore in the armoire…`
  is measured. Rows whose English is only
  placeholders are still counted apart, because the client builds those labels itself.
- Splits script misses by why the line was not answered: `ambiguous: no speaker`, `one speaker`,
  `several speakers`, `cutscene`, or `not indexed`. The first needs an id the game does not have;
  the last three are what the speaker and cutscene narrowing recover in game.
- Builds the runtime-built cases from real rows: a line that writes the `Player Player`
  placeholder gets a fixed test name, a line with a double-space gap gets a fixed value, and the
  result is looked up exactly as the game does. The labels the client formats itself (clock, date,
  level) are listed as expected to stay English instead of being counted as misses.
- Reads the speaker alias out of the dump line (`(-Retainer-)`, `(-???-)`) and uses it as the
  speaker, because the game draws that name in the name node and not in the text.
- Writes a small baseline with `-Baseline`, which `-Gate` compares. A data update changes the
  totals, so refresh the baseline in the same pull request as the data.
- Feeds dynamic values with game data: `IntegerParameter` gets several vectors, `Player` gets a
  real name, a conditional is measured once per branch, and an empty or letterless label is skipped
  because it carries no text to translate. The report breaks the numbers down by those categories,
  so a miss class shows up as a batch rather than a line.

Two limits are deliberate. Runtime-built text ("You obtained 3 × …") has no source row to
enumerate; `Runner.RuntimeCases` is the empty hook for it. And the run pairs the dumps the artifact
was built from, so it measures the matcher, not the live client's version skew: the report prints
the artifact's game version, and a dump newer than the artifact will show up as misses.

### Coverage of the release

[docs/release-1.0-checklist.md](release-1.0-checklist.md) holds the gates 1.0.0 has to pass, and
each item names how to verify it. The coverage run and the in-game checklist are the two objective
measures the release is judged against.

### The Testing tab

The settings window has a **Testing** tab, shown only when the build defines `SINOPHONE_TESTING`
and Dalamud loaded the plugin as a dev plugin. It replaces the two debug probes with one tab of
sub-tabs: **Runs**, **Talk**, **Chat bubbles**, **Native text**, **Talk probe**, and **Results**.

A run sends a list of English lines through one surface and records what the game drew. A run is
frame-driven, so the game keeps drawing between cases. Each case waits for the text to stop
changing, then checks it:

| Check | What it proves |
| --- | --- |
| Translated | A translation reached the surface, not the English line |
| No macro | No `<Sheet(`, `<StringParameter(`, or other tag is drawn as text |
| Fits | The text is no larger than the room the game gave it |
| No leading closing mark | No rendered line starts with `。` or another closing mark |
| Glyphs | Every character has a glyph in the game font |
| No revert | The game did not put the English back after the translation appeared |

A run writes `harness-YYYYMMDD-HHMMSS.json` next to the plugin config, under `harness/`. The
Results sub-tab draws the last run as a table.

**What is compiled where.** The harness core is always compiled, so `Test.ps1` covers it: the
case model, the settle rule, the checks, the run schedule, the session, the report, the revert
tracker, and the tab's own state. The engine half — the drivers, the bubble and Talk surfaces, the
glyph probe, and the screenshot capture — is behind `SINOPHONE_TESTING`. A Debug build defines it
and a release build does not, so a released DLL holds none of that code. A release build can opt
in with `dotnet build -c Release -p:IncludeTesting=true`. CI runs that build after the tests, so
the engine half is at least compiled on every pull request even though a run needs the game.

Add a surface by writing one `IHarnessDriver` and a sub-tab; the checks, the settle rule, and the
report come with it. `docs/proposals/testing-tab-surfaces.md` lists the surfaces and their drive
paths.

`HarnessBuild.IsTesting` says which half this assembly has. `HarnessBuildTests` checks it against
the types the build should contain, so a change that moves a game type into the core, or drops the
switch, fails a test instead of shipping developer code in a release.


**What a run cannot do.** An injected line has no live event handler behind it, so the id-first
lookup is not exercised. Only an armed Talk run, where the harness measures the line on screen,
covers it; start that run while the line is visible, and it measures one line per run. It gets the
English for the shown Chinese from the line the plugin logged when it laid it out, so a line the
plugin did not translate is skipped rather than passed. A Talk run reads the addon's own text node,
so it needs native text mode: with the overlay, the node keeps the English line and every case
fails. A bubble run needs chat bubble translation on. A run also needs the game, so it is not part
of CI.

### In-game testing

Some behavior can only be checked in the game: overlay position, font rendering, and how the
overlay behaves during cutscenes. Unit tests cannot cover these. The Testing tab automates part of
this; the checklist below still covers the parts a run cannot drive.

Build and link the plugin into Dalamud's dev plugin folder with
`scripts/Deploy-DevPlugin.ps1`. Add that folder under **Experimental** → **Dev Plugin Locations**
once; the script creates the plugin folder inside it and links it to the build output.

On a machine with the game installed, run that script after every code change, not only before
in-game testing. The game loads the plugin from the linked build output, so a change that is built
but not relinked leaves the game running old code. A plain `dotnet build` is not enough.

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

Native text mode has a longer list that covers wrapping, stale text, and the font mod. Run
[native-mode-checklist.md](native-mode-checklist.md) for every change to that path.
