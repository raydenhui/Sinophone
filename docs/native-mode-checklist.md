# Native mode checklist

Native text mode writes Chinese into the game's own windows. Tests cannot check it: addon payloads
cannot be driven outside the game, so someone has to look at the screen.

Run this list for every change that touches native mode, and put the result in the pull request.

## Setup

Two runs are needed:

- **Without the font mod.** Every line shows the missing-glyph character. The plugin cannot detect
  the mod, so this is the only warning the user gets.
- **With the font mod installed.** Chinese should be readable. See the README font setup section.

## Checks

| Check | Expected |
| --- | --- |
| Talk to an NPC with a short line | Chinese in the dialogue box, no overlay above it |
| Talk to an NPC with a long line | Text wraps inside the box, nothing clipped |
| Let a line finish revealing | Chinese stays after the reveal ends; it does not revert to English |
| Talk to an NPC whose line has no translation | English line stays English; no Chinese from the previous line appears |
| Speaker name | Chinese name in the name field |
| Close the dialogue, talk again | No stale text from the previous line |
| Disable the plugin while dialogue is open | Original text comes back |
| Toggle native mode off | Game text returns immediately |
| Change Chinese variant in settings | Next line uses the new variant |
| Cutscene subtitle | Overlay still used; native mode does not cover subtitles yet |
| Chat bubble from an NPC | Overlay still used; native mode does not cover bubbles yet |
| Line with oblique words | Note whether the English fragments stay on screen. If they do, open an issue |
| Yes/No dialog | Prompt and button labels are Chinese; no overlay |
| String selection dialog | Prompt and every option are Chinese; no overlay |
| Icon selection dialog | Every option is Chinese; no overlay |
| OK dialog | Prompt and OK label are Chinese; no overlay |
| Cutscene string selection | Prompt and every option are Chinese; no overlay |
| Selection dialog with an untranslated option | That option stays English; other options still translate |
| Close and reopen a selection dialog | No stale text from the previous dialog |
| Turn on "Show Chinese names on nameplates" near several NPCs and objects | Every visible plate shows the Chinese name, not only the target |
| Turn "Show the English name too" off | Every plate shows Chinese only |
| Look at a player character | The player's name is unchanged |
| Walk away and come back | The plates translate again; no stale names stay on a recycled plate |
| Turn the nameplate setting off | Every changed plate returns to English |
| An object whose nameplate is hidden | Nothing changes; the English name stays |
| Two nearby objects with the same name | Note whether both plates show the Chinese name |
| An object with a long name | The whole name fits on one line, not clipped, with the Chinese name readable |
| Names seen from different camera angles | The same full name at every angle; the left and right sides never get cut off |
| Hover an item with native item names on | Chinese name in the tooltip header; no overlay above the cursor; the Chinese name stays while the tooltip is open, not just for one frame |
| Hover an item whose name has no translation | The English name stays; no stale Chinese from the previous item |
| Move the mouse across several items quickly | Each tooltip shows the name of the item under the mouse |
| Set the item name language to "Chinese and English" | The tooltip shows the Chinese name with the English name in brackets, on one line when it fits |
| An item whose Chinese and English names are wider than the tooltip | The English name moves to a second line; nothing overlaps the item description |
| Turn native item names off while a tooltip is open | The English name comes back |
| Turn native item names off while a tooltip is open | The English name sits in the same place the game drew it, not higher or lower |
| Hover an item the tooltip cannot show, such as one in a menu that draws its own name | The overlay appears instead of the tooltip being changed |
| An item with a long English name | The whole name fits in the tooltip header, wraps into no narrow column, and the rest of the tooltip layout is unchanged |

## Done when

- Every row above has been checked and the result is written in the pull request.
- Screenshots are attached for the long-line and bubble cases.
- Any failing row has an issue opened with the screenshot.

## Related

- [Text coverage](text-coverage.md) for which sheets supply the text.
- `docs/` is for checklists and coverage notes; keep steps out of the code comments.
