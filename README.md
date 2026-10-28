# Sinophone

A [Dalamud](https://github.com/goatcorp/Dalamud) plugin for **Final Fantasy XIV** that displays the
official Simplified (CN) or Traditional (TC) Chinese translation of dialogue in an overlay above the
game's Talk window.

## Game language support

Currently only the **English** client is supported: dialogue is matched by the English text the
game displays, which is then mapped to Chinese.

Support for the other three game languages — **Japanese, German, and French** — is planned.

## Requirements

- Dalamud API 15 (`Dalamud.NET.SDK/15.0.0`)
- .NET 10 (`net10.0-windows7.0`)

## Install

Sinophone is not in the official Dalamud plugin list. Add it as a custom repository:

1. Open Dalamud settings (`/xlsettings`) and go to **Experimental** → **Custom Plugin Repositories**.
2. Add `https://raw.githubusercontent.com/raydenhui/Sinophone/main/pluginmaster.json` and save.
3. Open the plugin installer, search for **Sinophone**, and install it.

On first use, run `/sinophone` and click **Download / update data**. Translation data is downloaded
separately so it can be updated without a new plugin build.

## Font setup with TexTools

The overlay does not need this: it loads a Chinese font from Windows (or the font path set in the
plugin settings). This is only for the alpha in-game text mode, which writes the translation into
the game's own dialogue box and needs the game to draw Chinese itself.

The game's font files have no Chinese glyphs, so replace them:

1. Install [TexTools](https://github.com/TexTools/FFXIV_TexTools_UI/releases).
2. Pick a font from [AtmoOmen/FFXIVFonts](https://github.com/AtmoOmen/FFXIVFonts). There are
   several fonts; each folder holds one as a `.ttmp2` modpack. Use whichever you like.
3. Import the `.ttmp2` in TexTools and let it patch the game.
4. In `/sinophone`, turn on **Alpha: In-game text** → **Write the translation into the game's
   dialogue box**.

Penumbra does not replace the game font, so the text stays as boxes or `=` there.

## Status

**Not usable yet.** Some NPC dialogue is still not detected, so the overlay misses lines during
normal play. Settings and the translation data format may change without notice.

The first usable release is planned for **0.2.0**. Until then, expect gaps and changes.

## License

MIT — see [LICENSE](LICENSE).
