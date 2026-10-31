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
