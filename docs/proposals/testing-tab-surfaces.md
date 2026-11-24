The testing tab drives Talk and chat bubbles. The other surfaces still have no run, so a change
to them is still checked by hand. This note covers the rest, with the drive path for each so the
work is one decision per surface rather than an investigation.

Planned as one issue ("test(testing): drive the remaining surfaces from the harness"), opened
when the repository allows it.

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

## 3. Item tooltip

**Drive path:** set `IGameGui.HoveredItem` to an item id. The handler writes on
`HoveredItemChanged`. Showing the tooltip still needs the game to draw it, so either the run
opens `ItemDetail` with `OpenAddon`, or it is an armed run.

**What to assert:** the name node holds the Chinese, the node width grew to the text with
`WordWrap` cleared, and the height did not grow (the English line must not be pushed down).

**Risk:** a tooltip the game did not open may not lay out the name node.

## 4. Nameplates

**Drive path:** target or mouseover a character, then `INamePlateGui.RequestRedraw()`. The plate
belongs to a real object, so a run cannot inject a fake name.

**What to assert:** the plate text holds the Chinese name with the English in brackets, and the
plate width did not change (`TextW` and `TextH` must not be touched; the docs say changing them
damaged the bake atlas).

**Ordering:** drive one object the developer targets, not a batch. Plate slots carry no object
id, so a batch would measure whatever happened to be on screen.

## 5. TalkSubtitle

**Drive path:** the same shape as the Talk driver, but the addon is `TalkSubtitle` and it needs a
voiced cutscene.

**What to assert:** the subtitle node holds the Chinese and nothing is clipped by the subtitle
band.

**Ordering:** an armed run only. Opening `TalkSubtitle` outside a cutscene shows nothing useful.

## 6. Chat log restore

**Drive path:** subscribe to `IChatGui.ChatMessage` while a native run plays, and record the text
of every Story-tab message.

**What to assert:** no Story-tab message holds Chinese. `ChatLogRestorer` exists for exactly this,
and nothing checks it today.

**Why it matters:** it is the surface most likely to leak the translation to other plugins, which
is the thing this plugin exists to avoid.

## 7. Glyph coverage, as a shipped check

The harness has `GlyphProbe`, which asks the game's font manager whether a codepoint has a glyph.
It is used per case today. The docs say the plugin cannot tell whether the font mod is installed,
and this may make that possible.

**Work:** count missing glyphs over every distinct CJK codepoint in the artifact once at load,
then show a Status-tab warning when the count is not zero. This is production code, not a test,
so it needs its own decision about the cost.

## 8. Batch runs over the whole dictionary

**Drive path:** the store-backed case builder already samples. A batch would run every sheet line
through the real addon, throttled to a few lines a second.

**What to assert:** the same checks, counted instead of listed. The output is a coverage number
per failure class, which is the in-game half of what `Test-Coverage.ps1` measures offline.

**Risk:** the chat log Story tab fills with every line the game shows. Run it with the story tab
cleared, or accept the noise.

## Done when

- Each surface above either has a driver and a row in `docs/native-mode-checklist.md`, or is
  recorded here as impossible with the reason.
- `docs/testing.md` names the surfaces a run covers and the ones it cannot.
