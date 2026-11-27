# Sinophone

[English](README.en.md) · [简体中文](README.zh-CN.md) · [繁體中文](README.md)

A [Dalamud](https://github.com/goatcorp/Dalamud) plugin for **Final Fantasy XIV** that shows the
official Simplified (CN) or Traditional (TC) Chinese translation of the game's text. It covers:

- Dialogue above the Talk window, or written into the game's own dialogue box.
- Cutscene subtitles above the game's subtitle band, or written into the band itself.
- Battle talk, the large line a boss, dungeon narration, or a duty NPC shows near the top of the
  screen.
- Chat bubbles above NPCs.
- Selection dialogs, such as yes/no, string, and icon prompts.
- Nameplates above NPCs and objects.
- The name in the item tooltip.

The overlay needs no font mod: it draws its own window and loads a Chinese font from Windows. Only
the native modes, which write Chinese into the game's own windows, need one (see
[Font setup](#font-setup)).

## Game language support

The client can run in **English, Japanese, German, or French**. The translation is always Chinese.

Sheet text, names, item names, and cutscene subtitles are matched by the game's own row ids, so
they work in any client language. Script dialogue resolves by id when the game exposes one. When it
does not, the English script dump is still the key, so a Japanese, German, or French client can
miss that line. A few selection-dialog values are read from the line in English or Japanese forms,
so a German or French prompt can also stay as it is.

The settings window is separate. It follows Dalamud's own UI language and carries English,
Simplified Chinese, Traditional Chinese, and Japanese. Change it in the plugin settings if you
want the window in another language.

## Compatibility

Sinophone only changes what is drawn on screen. It does not change the game's files on disk or the
text other plugins read, so other plugins still see the game's own text. It works alongside them.

## Requirements

- Dalamud API 15 (`Dalamud.NET.SDK/15.0.0`)
- .NET 10 (`net10.0-windows7.0`)
- Optional: a Chinese font for the native modes (see [Font setup](#font-setup))

## Install

Sinophone is not in the official Dalamud plugin list. Add it as a custom repository:

1. Open Dalamud settings (`/xlsettings`) and go to **Experimental** → **Custom Plugin Repositories**.
2. Add `https://raw.githubusercontent.com/raydenhui/Sinophone/main/pluginmaster.json` and save.
3. Open the plugin installer, search for **Sinophone**, and install it.

The first load downloads the translation data (about 60 MB) automatically, so it can be updated
without a new plugin build. If the download fails, run `/sinophone` and click **Download / update
data**.

## Font setup

The overlay does not need this: it loads a Chinese font from Windows (or the font path set in the
plugin settings). This is only for the native modes, which write the translation into the game's
own windows and need the game to draw Chinese itself.

The game's font files have no Chinese glyphs, so replace them with a font that has them. Pick one
from [AtmoOmen/FFXIVFonts](https://github.com/AtmoOmen/FFXIVFonts): the repository has several
fonts and each folder holds one as a `.ttmp2` modpack. Give the pack to the plugin, or install it
with one of the two mod tools.

Some folders hold two packs. Use the one ending in `(Global)` on the global client. The other pack
replaces the Chinese client's `chnaxis_*` and `font_chn_*` files, which the global client never
loads, so it leaves the game font unchanged.

### Sinophone

The plugin can use the pack itself, without a mod manager.

1. Open `/sinophone` and go to **General**.
2. Turn on **Replace the game font with a font pack**, or use **Choose a .ttmp2** / **Choose a
   folder**.
3. Click **Reload the game font**.

The replacement is game-wide, because every native surface draws with the same game font. Turn the
setting off, or click **Restore the game's original font**, to put the game's font back. The plugin
reads the pack from your disk. It ships no font data and never changes the pack. If Penumbra
already replaces the game font, the plugin leaves the font to Penumbra.

### TexTools

1. Install [TexTools](https://github.com/TexTools/FFXIV_TexTools_UI/releases).
2. Import the `.ttmp2` and let it patch the game.

### Penumbra

1. Install [Penumbra](https://github.com/xivdev/Penumbra) and turn it on.
2. Import the same `.ttmp2` and enable it. To apply it without restarting, open Penumbra's
   **Settings** tab, go to **Advanced**, and click **Reload Fonts**.

Penumbra collections are per character. If the pack is enabled but the game still misses glyphs,
check the collection the current character uses, or use the plugin's built-in replacement instead.

### Either way

Native modes are off by default. Turn on the ones you want in `/sinophone`. Without a font pack or
a mod, every native-mode line shows the missing-glyph character instead of Chinese.

## Status

Sinophone can now be used normally. Expect bugs and gaps: the CN and TC dumps trail the live game,
so new or rewritten lines can have no translation, and some surfaces still show the game's own
text. The settings and the translation data format may still change between releases.

If a line is not translated, or something looks wrong, open an
[issue](https://github.com/raydenhui/Sinophone/issues). Feature requests are welcome there too.

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
- [Echoglossian](https://github.com/lokinmodar/Echoglossian), where the idea for this plugin came
  from.
- [fftrans](https://github.com/raydocs/fftrans) for a second, independent Talk handler that
  confirmed the same re-apply pattern.

The Chinese text itself comes from the game's own Simplified and Traditional localizations.

## License

Apache-2.0 — see [LICENSE](LICENSE) and [NOTICE](NOTICE).
