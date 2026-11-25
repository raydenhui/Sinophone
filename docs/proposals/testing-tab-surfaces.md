The testing tab drives Talk and chat bubbles. Battle talk, selection dialogs, glyph coverage as a
shipped check, and batch runs still have no run, so a change to them is still checked by hand.
This note covers the rest, with the drive path for each so the work is one decision per surface
rather than an investigation.

Item tooltips and nameplates were checked in game and work, so their automated coverage is
deferred. Chat log restore is dropped. Native `TalkSubtitle` display is a feature issue (#199),
not a harness surface, so it is not covered here.

Tracked as #198.

## What the harness already gives a new surface

A surface needs one class: an `IHarnessDriver` that puts a case on screen, measures what the
game drew, and cleans up. Everything else comes for free:

- The case list, the sample from the store, and the store-backed case builder.
- The settle rule (text unchanged for four frames, or a timeout).
- The checks: translated, no macro, fits, no leading closing mark, glyphs, no revert.
- The report, the JSON file, and the Results table.
- The revert tracker, which catches "showed Chinese then went back to English".

The first driver is `ChatBubbleHarnessDriver`. Copy its shape.

---

## 1. Battle talk

**Drive path:** `UIModuleInterface.ShowBattleTalk` (vtable 198), `ShowBattleTalkImage` (199), and
`ShowBattleTalkSound` (201). `BattleTalkHandler` already detours all three, so calling them runs
the handler and the game draws the box.

**What to assert:** the `_BattleTalk` text node holds the Chinese, the box closes on its own,
and no macro leaked into the box.

**Risk:** the box may need a duty or a special UI state to draw. Try it in a quiet zone first.

**Why it matters:** three reasoning paths (plain, image, sound) and a queued line are on the 1.0
checklist and nothing measures them today.

## 2. Selection dialogs

**Drive path:** open `SelectYesno`, `SelectString`, `SelectIconString`, or `SelectOk` with the
values the game sends, through `RaptureAtkModule.OpenAddon` or the addon's own `OnSetup`. The
handler reads the prompt and the options from the addon, so it needs a real addon, not a copy.

**What to assert:** the prompt was translated, every option was translated, and the Chinese
prompt fits the node the game sized for the English one. That last check is the whole point: the
handler already joins lines when the Chinese needs more, and a run would show whether the rule
holds for a real dialog.

**Risk:** opening the addon outside an event may leave it empty. If so, the run becomes an armed
run: the developer opens a dialog and the harness measures it.

## 3. Glyph coverage, as a shipped check

The harness has `GlyphProbe`, which asks the game's font manager whether a codepoint has a glyph.
It is used per case today. The docs say the plugin cannot tell whether the font mod is installed,
and this may make that possible.

**Work:** count missing glyphs over every distinct CJK codepoint in the artifact once at load,
then show a Status-tab warning when the count is not zero. This is production code, not a test,
so it needs its own decision about the cost.

## 4. Batch runs over the whole dictionary

**Drive path:** the store-backed case builder already samples. A batch would run every sheet line
through the real addon, throttled to a few lines a second.

**What to assert:** the same checks, counted instead of listed. The output is a coverage number
per failure class, which is the in-game half of what `Test-Coverage.ps1` measures offline.

**Risk:** the chat log Story tab fills with every line the game shows. Run it with the story tab
cleared, or accept the noise.

---

## Deferred: item tooltip

Checked in game: the Chinese name is written into the tooltip header and the rest of the layout
stays where the game drew it. The rows in `docs/native-mode-checklist.md` stay the check. Revisit
if the tooltip code changes.

Drive path for then: set `IGameGui.HoveredItem` to an item id; the handler writes on
`HoveredItemChanged`. Assert the name node holds the Chinese, the node width grew with `WordWrap`
cleared, and the height did not grow.

## Deferred: nameplates

Checked in game: every visible plate shows the Chinese name with the English in brackets, and
player characters are skipped. The plate belongs to a real object, so a run cannot inject a fake
name anyway. The checklist rows stay the check. Revisit if the nameplate code changes.

Drive path for then: target or mouseover a character, then `INamePlateGui.RequestRedraw()`. Assert
the plate text is the Chinese name plus the English in brackets, and that `TextW` and `TextH` were
not touched.

## Dropped: chat log restore

A run would have checked that the Story tab stays English after a native Talk line. The Story tab
already shows the line in the language the settings ask for and works, and the restorer only feeds
Talk lines, so a harness surface adds nothing. Dropped rather than carried as pending work.

---

## Done when

- Each remaining surface either has a driver and a row in `docs/native-mode-checklist.md`, or is
  recorded as deferred or dropped with the reason.
- `docs/testing.md` names the surfaces a run covers and the ones it cannot.
