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

### Coverage suite

`tools/Sinophone.Coverage` measures how much of each component's text the plugin can translate,
against the real artifacts rather than the fixture. It is an evaluation run, not part of
`scripts/Test.ps1`; run it when you want the numbers, after a data update, or before a release:

```sh
pwsh -File scripts/Test-Coverage.ps1                  # tc, using the plugin's own artifacts
pwsh -File scripts/Test-Coverage.ps1 -Variant cn      # simplified data
pwsh -File scripts/Test-Coverage.ps1 -Gate tools/Sinophone.Coverage/coverage-baseline.json
pwsh -File scripts/Test-Coverage.ps1 -Baseline tools/Sinophone.Coverage/coverage-baseline.json
```

It writes `coverage.md` and `coverage.json` next to the artifacts. What it does:

- Loads the English side of each sheet from the xivapi/ffxiv-datamining CSVs, downloaded once and
  cached, so the store can be built without the game. `tools/Sinophone.Coverage/Coverage/Sources.cs`
  says which sheet feeds which component; a sheet that gains a handler has to gain a line there or
  the report counts it under "shown by nobody".
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
- Builds the Addon match template from the dump text, where the game fills a value at the literal
  word `UNKNOWN`, so the parameterised prompt path is reachable offline. Rows whose English is only
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

### In-game testing

Some behavior can only be checked in the game: overlay position, font rendering, and how the
overlay behaves during cutscenes. Unit tests cannot cover these.

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
