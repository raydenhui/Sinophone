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

## Done when

- Every row above has been checked and the result is written in the pull request.
- Screenshots are attached for the long-line and bubble cases.
- Any failing row has an issue opened with the screenshot.

## Related

- [Text coverage](text-coverage.md) for which sheets supply the text.
- `docs/` is for checklists and coverage notes; keep steps out of the code comments.
