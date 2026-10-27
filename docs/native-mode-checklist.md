# Native mode checklist

Native text mode writes Chinese into the game's own windows. Tests cannot check it: addon payloads
cannot be driven outside the game, so someone has to look at the screen.

Run this list for every change that touches native mode, and put the result in the pull request.

## Setup

Two runs are needed:

- **Without the font mod.** Native mode should warn and not show boxes.
- **With the font mod installed.** Chinese should be readable.

## Checks

| Check | Expected |
| --- | --- |
| Talk to an NPC with a short line | Chinese in the dialogue box, original hidden or shown per setting |
| Talk to an NPC with a long line | Text wraps inside the box, nothing clipped |
| Speaker name | Chinese name in the name field |
| Close the dialogue, talk again | No stale text from the previous line |
| Disable the plugin while dialogue is open | Original text comes back |
| Cutscene subtitle | Chinese subtitle, correct position |
| Chat bubble from an NPC | Bubble grows to fit, not clipped |
| Toggle native mode off | Game text returns immediately |
| Change Chinese variant in settings | Next line uses the new variant |

## Done when

- Every row above has been checked and the result is written in the pull request.
- Screenshots are attached for the long-line and bubble cases.
- Any failing row has an issue opened with the screenshot.

## Related

- [Text coverage](text-coverage.md) for which sheets supply the text.
- `docs/` is for checklists and coverage notes; keep steps out of the code comments.
