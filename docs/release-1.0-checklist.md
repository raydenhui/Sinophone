# 1.0.0 checklist

The gates that make 1.0.0 a feature-complete, stable, usable release rather than a long alpha.
Every item has a way to verify it: a test, a coverage number, or a row in
[native-mode-checklist.md](native-mode-checklist.md). An item nobody can check is a wish, not a gate.

The 1.0.0 definition, in the project's own words:

- The translation is displayed correctly on every surface that shows it.
- The configuration is complete and lets a player shape the display.
- The major text that carries quest and game-story content has a translation component.

Status: in progress. Scope reviewed 2026-11-27 against the code. Coverage numbers below come from
the TC artifact and the coverage run of 2026-11-27 against the current `data` release.

## 1. Story coverage

The matcher is close to done; the surfaces are not. Measured coverage: battle talk, chat bubbles,
item name, and nameplates 100 %, talk 99.3 %, selection 99.1 %, runtime-built text 98.8 %, script
lines 97.2 % with every miss classified. Runtime text is below the 99 % gate: the 12 misses are
7 player-name lines and 5 Addon prompts that splice a value into a gap.

### Required for 1.0

- [ ] **Quest journal and objective text.** The largest story surface with no component today.
      Quest descriptions, objectives, and progress text appear in `Journal` and `JournalDetail`,
      and are skipped because no overlay watches them. Research the addons first, then choose an
      in-place translation or an anchored overlay. Verify: the journal tab for three quests of
      different lengths, in both variants, nothing clipped and no macro visible.
- [ ] **Quest accept and complete dialogs.** `JournalAccept`, `JournalResult`, and the reward list
      inside them. The player reads the quest's premise here, before the journal exists.
      Verify: accept and complete one quest of each type (MSQ, side, leve), both variants.
- [ ] **`TalkSubtitle` in-game check.** The band takes the line from its `AtkValues`; the overlay is
      the fallback. Implemented by #200. Verify on a voiced cutscene with a font mod and with no
      font, and note whether a long line scales or runs past the band. Close #199 after.
- [ ] **Duty story text.** `ContentTalk`, `InstanceContentTextData`, and battle talk in all three
      styles (plain, image, sound) are implemented. The remaining check is a queued line and a line
      that arrives while another is on screen.
- [ ] **Every sheet in `Sources.cs` that is shown by nobody** gets a written reason to stay English:
      `Warp`, `CSBonusTextData`, `GoldSaucerTextData`, `MKDLore.Name`. `GimmickBill` is already a
      Talk source through `TalkEventReader`.
- [ ] **`EventItem` names in the item tooltip.** The rows are indexed, measured, and served by the
      store; the tooltip handler returns 0 for an event id, so event and key items stay English in
      native mode. Resolve `Item` first, then `EventItem`, and use `EventItem` for the English
      bracket.
- [ ] **One representative story and duty sample** played with Debug logs, and every miss in that
      log triaged into fixed, `ambiguous: no speaker`, or `client label`. Two to three hours of
      story and duty content replaces a full expansion run.
- [ ] **Coverage gate.** Script lines at least 97 %, talk and selection at least 99 %, and `-Gate`
      clean against the committed baseline. Runtime-built text is 98.8 %: triage its 12 misses
      before the gate can pass.

### Gate

- No story surface in the list above left undecided.
- `Test-Coverage.ps1 -Gate` passes after the data changes.

## 2. Display correctness

- [ ] Every row in [native-mode-checklist.md](native-mode-checklist.md) ticked.
- [ ] Two font configurations cover the resource sets: a font mod and no font. One Penumbra
      spot-check confirms a TexTools font or no font does not fight it.
- [ ] No line draws outside its box, wraps into a fragment, or shows a macro (`<Sheet(`,
      `<StringParameter(`, `<Unknown60>`).
- [ ] Selection dialogs: a two-line Chinese prompt in a one-line dialog, a long option list, and an
      icon prompt.
- [ ] Item tooltip and nameplates: a long name, the two-language form, and a masked `???`.
- [ ] Chat bubbles: yells and default balloons, short and long lines, several at once, and a bubble
      whose line has no translation.
- [ ] Native "what other readers see" written per surface in
      [translation-workflow.md](translation-workflow.md). No new per-surface setting: Talk keeps its
      existing switch, the subtitle restores the caller's values, and the in-place surfaces restore
      the game's text where they change it.

## 3. Configuration

- [ ] Every component has its own switch, and every surface that draws has colour, opacity, and
      size control where it makes sense.
- [ ] Overlay: position, lock, click-through, width, font size, custom font path, per-component
      appearance overrides.
- [ ] Game font (dev build only): on/off, pack file or folder, reload, and restore the stock font.
      A release build shows none of these controls and installs none of the hooks.
- [ ] Data: variant, fallback, conversion, auto-update, manual update, and a status readout.
- [ ] Logs: one group per component, plus diagnostics; hidden groups cost nothing.
- [ ] Fresh-install audit: what a new player sees with no Chinese font, and with one. The defaults
      are part of the product, not an afterthought.
- [ ] Config version and migrations documented: selection dialogs, the dev build's all-on switch,
      and every field that **Reset All** resets or preserves. Reset leaves a working install.

## 4. Stability and performance

- [ ] Frame time measured with every component on, in a city and in an eight-player duty: no visible
      hitch on a bubble stream, a battle talk line, or a data reload.
- [ ] Per-frame paths allocate nothing (Talk reapply, bubble sync, tooltip, nameplates), reviewed
      against [logging-and-performance.md](logging-and-performance.md).
- [ ] Every hook fails open and logs; a failed hook leaves English and never throws into the game.
      The nameplate update runs through `HookGuard` like the other callbacks.
- [ ] Data: background download, cache rebuild, fallback artifact, offline start, corrupted artifact.
- [ ] A two-hour session with everything on: no growth in the buffered state, no hitch on reload.
- [ ] Failure modes: no data, half-downloaded data, wrong variant, plugin reload mid-dialogue.

## 5. Release hygiene

- [ ] [architecture.md](architecture.md), [translation-workflow.md](translation-workflow.md),
      [testing.md](testing.md), [text-coverage.md](text-coverage.md), and `AGENTS.md` match the code.
- [ ] The three readmes agree, including the font wording.
- [ ] Manifest fields reviewed; the release notes list every pull request since the previous tag.
- [ ] CI builds and tests a clean checkout; the release workflow verified on a tag.
- [ ] The translation data release is rebuilt separately from the plugin `latest` release.
- [ ] Non-goals written down, so 1.0 can be reached (see below).

## Non-goals for 1.0

Stated here so they do not creep back into the gates:

- **Runtime-built text** beyond the classes the coverage tool measures (player name, blank gap,
  client labels). Text with no source row stays English by design.
- **Native menus, lists, and shops.** Whole-UI replacement is not this plugin's goal.
- **Shipping font data.** A dev build can replace the game font from a pack the user already has,
  but the plugin ships no font files. A pack built for the Chinese client is refused, because the
  global client never loads those files.
- **Windows that draw no story text.** `Warp`, `CSBonusTextData`, `GoldSaucerTextData`, and
  `MKDLore.Name` stay English unless the Phase 3 review finds one of them carries story text.
- **Script dialogue on a non-English client.** Sheet surfaces are keyed by row id and work in any
  language the global client offers. A script line with no row id is keyed on the English dump, so
  it can stay English. This is a known limit, not a 1.0 gate.
