# Testing tab: what to run on a Windows machine

This branch was written on Linux with no .NET SDK, so it has not been compiled or run. These are
the commands that verify it. Run them in order; each one either passes or tells you what broke.

The glyph probe reads `AtkFontManager`, which landed in FFXIVClientStructs in October 2026. Use a
current Dalamud (15.0.4.1 or later); an older dev install fails to compile the engine half.

## 1. Build and test (no game needed)

```powershell
pwsh -File scripts/Test.ps1
```

This builds Release and runs both test projects. Release matters: it proves the harness core is
compiled without `SINOPHONE_TESTING`, which is the same code a release ships.

Expected: green. New tests are in `Harness*Tests.cs` (unit) and `HarnessCatalogFixtureTests.cs`
(integration).

## 2. Check the testing code is absent from a release build

```powershell
dotnet build src/Sinophone/Sinophone.csproj -c Release
```

Then, in a decompiler or with `strings`, look for any of these in `Sinophone.dll`:

- `HarnessHost`
- `ChatBubbleHarness`
- `TalkHarnessSurface`
- `GlyphProbe`
- `HarnessCapture`

Expected: no matches. These are under `#if SINOPHONE_TESTING`, which a Release build does not
define.

Also expect to **find** `HarnessCase`, `HarnessSession`, `HarnessReport`, and `HarnessAssertions`.
Those are the always-compiled core the unit tests exercise.

## 3. Build with the tab, without the Debug configuration

```powershell
dotnet build src/Sinophone/Sinophone.csproj -c Release -p:IncludeTesting=true
```

Expected: builds, and the `SINOPHONE_TESTING` code is present. Use this only when you want the
tab in a build that is otherwise a release.

## 4. Deploy and check the tab

```powershell
pwsh -File scripts/Deploy-DevPlugin.ps1 -DevPluginPath <dev-plugin-folder>
```

In game, `/sinophone` → **Testing**. Expected:

- The tab is present in this build; the run controls are enabled only because Dalamud loaded a
  dev plugin. Loading the same DLL as a normal plugin shows the tab with runs off.
- Sub-tabs: Runs, Talk, Chat bubbles, Native text, Talk probe, Results.
- Native text and Talk probe behave as they did before.

## 5. Chat bubble run

1. On **Chat bubbles** → **Refresh objects**. The player is first in the list.
2. Click **Show a sample bubble**. A bubble should appear above the player.
3. Click **Close bubble**.
4. Click **Run these bubble cases** with **Use lines from the loaded data** on and 5 cases.
5. Read **Results**.

Expected:

- Each case shows a bubble holding Chinese.
- Results lists each case as Passed, or Failed with a sentence saying which check failed.
- A JSON file appears under `<plugin config dir>/harness/`.
- With **Save a screenshot per case** on, PNGs appear in the same folder. They are written a frame
  after the case ends; if none appear, the log says why.

## 6. Talk run

1. Open a conversation so the Talk window is up.
2. On **Talk**, leave **Write the English line into Talk** on, and run a few cases.

Expected: the box shows the Chinese, and the Results rows say Passed. A line that reverts shows
Failed with "The game replaced the translation".

If every row fails with "The Talk window is not open", the addon was closed between cases.

3. Turn **Write the English line into Talk** off. Talk to an NPC, and while the line is on screen,
   start a run. It measures one line.

Expected: the row passes when the box shows Chinese. A line the plugin could not translate is
Skipped with "No translation reached this line", not passed. The run does not put the player's own
line back, so talk through the line again before checking it yourself.

## 7. What to report back

For each failure, the case id and the result detail are enough. They name the check that failed
(fit, macro, glyphs, revert, or translation), so no screenshot is needed to triage.
