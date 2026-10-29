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

## Font setup

The overlay does not need this: it loads a Chinese font from Windows (or the font path set in the
plugin settings). This is only for the alpha in-game text mode, which writes the translation into
the game's own dialogue box and needs the game to draw Chinese itself.

The game's font files have no Chinese glyphs, so replace them with a font that has them. Pick one
from [AtmoOmen/FFXIVFonts](https://github.com/AtmoOmen/FFXIVFonts): the repository has several
fonts and each folder holds one as a `.ttmp2` modpack. Then install it with either tool.

### TexTools

1. Install [TexTools](https://github.com/TexTools/FFXIV_TexTools_UI/releases).
2. Import the `.ttmp2` and let it patch the game.

### Penumbra

1. Install [Penumbra](https://github.com/xivdev/Penumbra) and turn it on.
2. Import the same `.ttmp2` and enable it. Restart the game if the font does not change.

### After either route

In `/sinophone`, turn on **Alpha: In-game text** → **Write the translation into the game's dialogue
box**. Without a font mod, every native-mode line shows the missing-glyph character instead of
Chinese.

## Status

**Not usable yet.** Some NPC dialogue is still not detected, so the overlay misses lines during
normal play. Settings and the translation data format may change without notice.

The first usable release is planned for **0.2.0**. Until then, expect gaps and changes.

## Thanks

Sinophone is built on other people's work. Thank you to:

- [Dalamud](https://github.com/goatcorp/Dalamud) for the plugin framework, the addon lifecycle
  events, and the ImGui wrapper.
- [FFXIVClientStructs](https://github.com/aers/FFXIVClientStructs) for the game structures that
  native mode reads and writes.
- [ImGui](https://github.com/ocornut/imgui) for the overlay.
- [ffxiv-datamining-cn](https://github.com/thewakingsands/ffxiv-datamining-cn) and
  [ffxiv-datamining-tc](https://github.com/thewakingsands/ffxiv-datamining-tc) for the Chinese game
  data.
- [xivapi/ffxiv-datamining](https://github.com/xivapi/ffxiv-datamining) for the English CSVs and
  script dumps.
- [OpenCC](https://github.com/BYVoid/OpenCC) for the simplified and traditional conversion tables.
  See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
- [AtmoOmen/FFXIVFonts](https://github.com/AtmoOmen/FFXIVFonts) for the font modpack that lets the
  game draw Chinese.
- [TexTools](https://github.com/TexTools/FFXIV_TexTools_UI) and
  [Penumbra](https://github.com/xivdev/Penumbra) for the two ways to install that font modpack.
- [FFXIVChnTextPatch-GP](https://github.com/GpointChen/FFXIVChnTextPatch-GP) for the sheet list and
  the reference for which game text is worth translating.
- [Echoglossian](https://github.com/lokinmodar/Echoglossian) deserves the most credit for native
  mode. It already puts translated text into FFXIV's own windows, and its Talk handler is the
  reference this project worked from: which `Talk` nodes hold the name and the line, why one write
  is not enough because the text stream keeps redrawing the node, that the refresh values can carry
  the replacement, and the layout flags and font sizes a translated line needs. Issue #54 rests on
  what it had already solved.
- [fftrans](https://github.com/raydocs/fftrans) for a second, independent Talk handler that
  confirmed the same re-apply pattern.

The Chinese text itself comes from the game's own Simplified and Traditional localizations.

## License

MIT — see [LICENSE](LICENSE).
