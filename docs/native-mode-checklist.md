# Native mode checklist

Native text mode writes Chinese into the game's own windows. Tests cannot check it: addon payloads
cannot be driven outside the game, so someone has to look at the screen.

Run this list for every change that touches native mode, and put the result in the pull request.

## Setup

Two runs are needed:

- **Without a Chinese font.** No font pack and no font mod: every line shows the missing-glyph
  character. The plugin cannot detect the font, so this is the only warning the user gets.
- **With a Chinese font installed.** The in-game Chinese is readable. See the README font setup
  section.

## Before a bubble run

Chat bubbles keep the text and the node sizes a run leaves in them until the game restarts. A plugin
reload does not reset them, so a bubble that an earlier build changed can show fragments, stale
Chinese, or nothing even though the new build is fine. Restart the game after changing bubble code,
and write down which build was running when a row failed.

## Checks

| Check | Expected |
| --- | --- |
| Font pack on, no font mod installed | Chinese in the dialogue box, nameplates, chat bubbles, and battle talk |
| Change the font pack path, then click **Reload the game font** | The new font appears without restarting the game |
| A pack missing one file | The pack is refused; the status names the missing file; the stock font does not change |
| A plugin config folder whose path is too long for the game | The pack is refused with the path-length message |
| Penumbra with a font mod active, font pack on | The status says Penumbra owns the font; the font does not change |
| Turn the font pack off while a line is open | The stock font comes back after the reload |
| Unload the plugin with the font pack on | The stock font comes back |
| Talk to an NPC with a short line | Chinese in the dialogue box, no overlay above it |
| Talk to an NPC with a long line | Text wraps inside the box, nothing clipped |
| A long native Talk line that takes more than one line | The lines are spaced apart, not drawn on top of each other |
| A line whose last character is a full stop, near the box width | The full stop stays with its sentence; no line starts with `。`, `，`, `；`, `：`, `？` or `！` |
| A native Talk line, checked in the chat log's Story tab | Chinese in the dialogue box, the English line in the story log |
| A yell bubble with the print-to-log flag | Chinese in the bubble, the English line in the chat log |
| `GimmickBill` row 66 at the Ktisis Hyperboreia sign | All eight Chinese lines stay inside the panel, top and bottom |
| A line long enough that the Chinese needs more room than the English | The box grows for the Chinese, or the line shrinks to fit; it never draws outside the panel |
| A long Chinese speaker name or title | The name stays inside the header |
| Let a line finish revealing | Chinese stays after the reveal ends; it does not revert to English |
| Talk to an NPC whose line has no translation | English line stays English; no Chinese from the previous line appears |
| Speaker name | Chinese name in the name field |
| Close the dialogue, talk again | No stale text from the previous line |
| Disable the plugin while dialogue is open | Original text comes back |
| Toggle native mode off | Game text returns immediately |
| Change Chinese variant in settings | Next line uses the new variant |
| Cutscene subtitle | Chinese in the game's own subtitle band, no overlay above it |
| A subtitle line longer than the band | Note whether the band grows, the text scales down, or the line runs past the band |
| A subtitle with no translation | English line stays in the band |
| A subtitle whose band opens before the layout hook is in place (check the log) | Overlay shows the line; the next line goes native |
| Turn on chat bubble translation and talk near an NPC with a yell bubble | Chinese in the game's own bubble, above the speaker |
| A yell bubble whose line has no translation | English stays; no Chinese from the previous bubble |
| A long bubble line | Lines wrap inside the bubble; the background grows with the text, nothing clipped |
| A bubble whose Chinese is wider than the English line, such as a 20+ character shout | The Chinese wraps inside the bubble; no text runs past the background on either side |
| A bubble whose Chinese is much shorter than the English line | The background shrinks to the Chinese plus the game's margin; no wide empty box |
| A bubble line followed by a shorter line in the same slot | The second background fits the second line; it does not keep the first line's size |
| A bubble line followed by a longer line in the same slot | The second background follows the new Chinese; the bubble does not walk sideways |
| The same bubble slot after several lines | The background matches the line on screen; no empty area grows across the conversation |
| A yell bubble and a default balloon one after the other | Both are sized for the Chinese; neither keeps the English bubble size |
| A bubble that says only a sound, such as `<squeal> <squeal>` | The Chinese sound shows; the bubble is sized for it |
| Two or three bubbles at once, such as several NPCs shouting | Each bubble keeps its own text; no bubble overlaps another |
| An NPC default balloon (`ENpcBase.Balloon`), not a yell | Chinese in the bubble; note whether the background grew |
| Turn chat bubbles off while a bubble is open | The English bubble text comes back |
| A battle talk yell (enemy shout in a duty) with bubbles on | The bubble handler leaves it alone; battle talk is a separate setting |
| Battle talk on, boss shout | Chinese in the name field and in the line; the box is sized for the Chinese |
| Battle talk on, dungeon narration | Chinese line with no name field |
| Each style: plain, image, and sound | All three go through the hook; note any style that stays English |
| A line queued before the plugin was enabled | Chinese when it plays |
| A long battle talk line | Wraps inside the box; nothing runs off the screen edge |
| A battle talk line with no translation | English stays; no Chinese from the previous line |
| Turn battle talk off during a fight | The line on screen stays Chinese until it hides; the next line is English |
| Battle talk without a Chinese font | Missing-glyph characters; the settings text warns about the font |
| A line the plugin cannot fit, such as one wider than the screen | The English line stays; no Chinese is drawn outside the box |
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

## Using the Testing tab

A dev plugin build has a **Testing** tab in the settings window. The **Talk** sub-tab can write a
case's English line into an open Talk window and check it every frame; the **Chat bubbles** sub-tab
starts a bubble above a picked object. Both write a JSON report, and a PNG per case when screenshots
are on. Use a run as the first pass over the rows above. A run cannot see whether a Chinese font is
installed or judge whether the text reads well, so the eye check still decides.

A run writes into the live addon and does not put the player's own line back. Talk to an NPC again
after a run before judging a row.

## Done when

- Every row above has been checked and the result is written in the pull request.
- Screenshots are attached for the long-line and bubble cases.
- Any failing row has an issue opened with the screenshot.

## Related

- [Text coverage](text-coverage.md) for which sheets supply the text.
- `docs/` is for checklists and coverage notes; keep steps out of the code comments.
