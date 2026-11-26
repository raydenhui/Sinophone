# 1.0.0 checklist

The gates that make 1.0.0 a feature-complete, stable, usable release rather than a long alpha.
Every item has a way to verify it: a test, a coverage number, or a row in
[native-mode-checklist.md](native-mode-checklist.md). An item nobody can check is a wish, not a gate.

The 1.0.0 definition, in the project's own words:

- The translation is displayed correctly on every surface that shows it.
- The configuration is complete and lets a player shape the display.
- The major text that carries quest and game-story content has a translation component.

Status: planning. The numbers below are from the TC artifact and the coverage run of 2026-11-21.

## 1. Story coverage

The matcher is close to done; the surfaces are not. Today's measured coverage: battle talk, chat
bubbles, item name, and nameplates 100 %, talk 99.3 %, selection 99.0 %, runtime-built text 99.4 %,
script lines 97.0 % with every miss classified.

### Required for 1.0

- [ ] **Quest journal and objective text.** The largest story surface with no component today.
      Quest descriptions, objectives, and progress text appear in `Journal` and `JournalDetail`,
      and are skipped because no overlay watches them. They need an in-place translation like the
      selection dialogs, and a placement that does not fight the two-column journal layout.
      Verify: the journal tab for three quests of different lengths, in both variants, nothing
      clipped and no macro visible.
- [ ] **Quest accept and complete dialogs.** `JournalAccept`, `JournalResult`, and the reward list
      inside them. The player reads the quest's premise here, before the journal exists.
      Verify: accept and complete one quest of each type (MSQ, side, leve), both variants.
- [ ] **`TalkSubtitle` end to end.** The band takes the line from its `AtkValues`; the overlay is
      the fallback. Verify on a voiced cutscene, with and without the font mod, and note whether
      a long line scales or runs past the band.
- [ ] **Duty story text.** `ContentTalk`, `InstanceContentTextData`, and battle talk in all three
      styles (plain, image, sound), including a queued line and a line that arrives while another
      is on screen.
- [ ] **Every sheet in `Sources.cs` that is shown by nobody** gets either a component or a written
      reason to stay English: `Warp`, `CSBonusTextData`, `GoldSaucerTextData`, `GimmickBill`,
      `MKDLore`.
- [ ] **`EventItem` names.** The item surface reads `Item` only, so event and key items stay
      English. Either index `EventItem` into the item surface or record it as a non-goal.
- [ ] **One full expansion sample** played with Debug logs, and every miss in that log triaged into
      fixed, `ambiguous: no speaker`, or `client label`. This is the only item that proves the
      coverage numbers against live text.

### Gate

- Coverage: script lines at least 97 %, every other component at least 99 %, `-Gate` clean against
  the committed baseline.
- No story surface in the list above left undecided.

## 2. Display correctness

- [ ] Every row in [native-mode-checklist.md](native-mode-checklist.md) ticked, with a font pack,
      with a mod, and with neither.
- [ ] No line draws outside its box, wraps into a fragment, or shows a macro (`<Sheet(`,
      `<StringParameter(`, `<Unknown60>`).
- [ ] Selection dialogs: a two-line Chinese prompt in a one-line dialog, a long option list, and an
      icon prompt.
- [ ] Item tooltip and nameplates: a long name, the two-language form, and a masked `???`.
- [ ] Chat bubbles: yells and default balloons, short and long lines, several at once, and a bubble
      whose line has no translation.
- [ ] Native "what other readers see" decided for every surface, not only Talk, and documented in
      [translation-workflow.md](translation-workflow.md).

## 3. Configuration

- [ ] Every component has its own switch, and every surface that draws has colour, opacity, and
      size control where it makes sense.
- [ ] Overlay: position, lock, click-through, width, font size, custom font path, per-component
      appearance overrides.
- [ ] Data: variant, fallback, conversion, auto-update, manual update, and a status readout.
- [ ] Logs: one group per component, plus diagnostics; hidden groups cost nothing.
- [ ] Fresh-install audit: what a new player sees with no Chinese font, and with one. The defaults are
      part of the product, not an afterthought.
- [ ] Config version and migrations documented; reset to defaults leaves a working install.

## 4. Stability and performance

- [ ] Frame time measured with every component on, in a city and in an eight-player duty: no visible
      hitch on a bubble stream, a battle talk line, or a data reload.
- [ ] Per-frame paths allocate nothing (Talk reapply, bubble sync, tooltip, nameplates), reviewed
      against [logging-and-performance.md](logging-and-performance.md).
- [ ] Every hook fails open and logs; a failed hook leaves English and never throws into the game.
- [ ] Data: background download, cache rebuild, fallback artifact, offline start, corrupted artifact.
- [ ] A two-hour session with everything on: no growth in the buffered state, no hitch on reload.
- [ ] Failure modes: no data, half-downloaded data, wrong variant, plugin reload mid-dialogue.

## 5. Release hygiene

- [ ] [architecture.md](architecture.md), [translation-workflow.md](translation-workflow.md),
      [testing.md](testing.md), and `AGENTS.md` match the code.
- [ ] The three readmes agree, including the font wording.
- [ ] Manifest fields reviewed; the release notes list every pull request since the previous tag.
- [ ] CI builds and tests a clean checkout; the release workflow verified on a tag.
- [ ] Non-goals written down, so 1.0 can be reached:
      other game languages, runtime-built text beyond the measured classes, native menus and lists,
      and shipping font data.

## Non-goals for 1.0

Stated here so they do not creep back into the gates:

- **Other game languages** (Japanese, German, French). The matcher is keyed on English text; this is
  a design change, not a setting.
- **Runtime-built text** beyond the classes the coverage tool measures (player name, blank gap,
  client labels). Text with no source row stays English by design.
- **Native menus, lists, and shops.** Whole-UI replacement is not this plugin's goal.
- **Replacing the game font.** The plugin can read a user's font pack, but it ships no font data,
  so a user without a pack still needs a mod.
