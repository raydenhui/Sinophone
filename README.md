# Sinophone

A [Dalamud](https://github.com/goatcorp/Dalamud) plugin for **Final Fantasy XIV** that shows the
official **Simplified (CN)** or **Traditional (TC)** Chinese translation of dialogue in an overlay
above the game's Talk window — alongside the English original.

The game client ships fonts and dialogue boxes that cannot render CJK glyphs, so Sinophone draws
translated text in its own ImGui window positioned over the bottom of the screen.

```
┌─ Sinophone overlay ─────────────────────┐
│ 敏菲利亚                                 │  ← speaker name (translated)
│ 欢迎来到拂晓血盟。                        │  ← dialogue (translated)
│ Welcome to the Scions of the Seventh Dawn.│  ← original (optional)
└─────────────────────────────────────────┘
```

## How it works

Sinophone never talks to a translation service. It ships a **lookup table** built from the
official Chinese game data, and matches English text read from the live game against it.

1. **Build time** — `tools/Sinophone.DataPrep` reads the CSV dumps from
   [ffxiv-datamining-cn](https://github.com/thewakingsands/ffxiv-datamining-cn) /
   [ffxiv-datamining-tc](https://github.com/thewakingsands/ffxiv-datamining-tc) and writes a
   gzipped JSON artifact per language. These are embedded into the plugin assembly.
2. **Load time** — `TranslationStore` opens the embedded artifact(s) and, for every row, reads the
   English text from the matching Lumina sheet. English text becomes the dictionary key, Chinese
   text the value. Both dialogue sheets and name sheets are indexed.
3. **Runtime** — `TalkHandler` listens to the `Talk` and `TalkSubtitle` addon lifecycle events,
   reads the English text and speaker name out of the addon's `AtkValues`, looks them up, and hands
   the result to `OverlayWindow`.

Because lookup is keyed on the **English** text, the plugin works on any client locale that
displays English in the Talk window.

### Translation coverage

| Sheet | Field | Kind |
| --- | --- | --- |
| `DefaultTalk` | `Text` | dialogue (3 slots) |
| `Balloon` | `Dialogue` | dialogue |
| `NpcYell` | `Text` | dialogue |
| `InstanceContentTextData` | `Text` | dialogue |
| `ContentTalk` | `Text` | dialogue |
| `SnipeTalk` | `Text` | dialogue |
| `GuildleveAssignmentTalk` | `Talk` | dialogue (8 slots) |
| `CraftLeveTalk` | `Talk` | dialogue (6 slots) |
| `ENpcResident` | `Singular` | speaker name |
| `BNpcName` | `Singular` | speaker name |
| `SnipeTalkName` | `Name` | speaker name |

The sheet list lives in two places that must stay in sync: `SheetCatalog` (plugin) and the
`Sheets` array in `Sinophone.DataPrep` (tool).

### Text matching

English text from the game and from the datamining dump differ in ways that break naive matching,
so `MacroNormalizer` normalizes both sides before comparison:

- SeString macros such as `<...>` and `<br>` are stripped
- invisible characters (soft hyphen, zero-width space/joiner, BOM) are removed
- non-breaking spaces become regular spaces
- all whitespace, including line breaks, collapses to a single space

Chinese text is cleaned separately for display: macros stripped, line breaks preserved, trailing
whitespace trimmed.

### Variant fallback

The configured variant is indexed first and always wins. With *"Fill missing rows from the other
variant"* enabled, the other variant is indexed second, so it only fills keys the primary variant
does not cover. This matters because the CN and TC releases are not always at the same patch.

## Building

Requires the .NET SDK (the plugin builds against `net10.0-windows7.0` and the DataPrep tool against
`net9.0`) and a local Dalamud installation — the `Dalamud.NET.SDK/15.0.0` SDK and the Dalamud
assemblies are resolved from your Dalamud dev setup. The plugin targets `win-x64`.

### 1. Generate the translation artifacts

The artifacts are **not** committed, and the plugin embeds them at compile time — building without
them produces a plugin with no translations. Generate them first:

```sh
# from a checkout of the datamining CSVs
dotnet run --project tools/Sinophone.DataPrep -- \
  --source path/to/ffxiv-datamining-cn \
  --language CN \
  --output src/Sinophone/Resources/Sinophone.cn.json.gz \
  --game-version 7.4

dotnet run --project tools/Sinophone.DataPrep -- \
  --source path/to/ffxiv-datamining-tc \
  --language TC \
  --output src/Sinophone/Resources/Sinophone.tc.json.gz \
  --game-version 7.4
```

`--language` accepts `CN` or `TC`; `--game-version` is optional metadata. The tool prints per-sheet
row and slot counts, and skips (with a warning) any sheet whose CSV is missing or has no expected
columns.

The plugin loads artifacts by filename suffix (`Sinophone.cn.json.gz` / `Sinophone.tc.json.gz`),
so the names must match.

### 2. Build

```sh
dotnet build Sinophone.sln -c Release
```

## Usage

Open the settings window with `/sinophone`, or via Dalamud's plugin installer.

### Settings

**General**

| Setting | Default | Description |
| --- | --- | --- |
| Enable translations | on | Master switch; hides the overlay when off |
| Chinese variant | Traditional | Which official translation to show |
| Fill missing rows from the other variant | on | Fall back to the other variant for untranslated lines |
| Show in Talk dialog | on | React to the main `Talk` window |
| Show in Talk subtitles | on | React to `TalkSubtitle` (cutscene / duty subtitles) |

**Text**

| Setting | Default | Description |
| --- | --- | --- |
| Show speaker name | on | Show the translated speaker name |
| Also show original English text | on | Show the English line below the translation |
| Hide when no translation is found | on | Hide the overlay entirely instead of showing English only |

**Overlay**

| Setting | Default | Description |
| --- | --- | --- |
| Lock overlay position | on | Pin the overlay above the Talk window |
| Click-through | on | Mouse input passes through the overlay |
| Background opacity | 0.72 | Overlay background alpha |
| Font size | 20 px | Rendered font size |
| Width | 620 | Overlay width in game pixels |
| Distance from screen bottom | 180 | Vertical offset used when positioning |
| Custom font path | empty | Optional `.ttf` / `.ttc` / `.otf` file |

Changes apply and save immediately. **Preview overlay** renders a sample line so you can check the
font; **Reset layout** and **Reset all settings** restore defaults.

### Fonts

The game's bundled fonts have no CJK glyphs, so Sinophone loads a separate font atlas:

- a **custom font path**, if set and the file exists;
- otherwise a **Windows system font** — `msjh` / `mingliub` for Traditional, `msyh` / `simsun` for
  Simplified;
- otherwise the **other variant's** system font;
- otherwise no font, in which case Chinese renders as boxes (the status section warns about this).

Fonts are rebuilt automatically when the variant, font size, or font path changes.

## Project layout

```
src/Sinophone/
  Plugin.cs              Entry point: services, windows, command, config wiring
  Configuration.cs       Persisted settings
  Data/
    TranslationArtifact.cs  Artifact schema: Sheets[sheet][rowId][field] = string[]
    TranslationStore.cs     Loads embedded artifacts, builds English → Chinese lookups
    SheetCatalog.cs         Which Lumina sheets/fields to index, and how to read them
    MacroNormalizer.cs      Text normalization for matching and display
  Handlers/
    TalkHandler.cs       Addon lifecycle hooks; reads AtkValues; feeds the overlay
  UI/
    OverlayWindow.cs     Positioned ImGui overlay above the Talk window
    ConfigWindow.cs      Settings window
    FontManager.cs       CJK font atlas management
tools/Sinophone.DataPrep/
  Program.cs             CSV → gzipped JSON artifact converter
```

## Limitations

- Only dialogue that appears in the indexed sheets is translated; dynamically assembled text is not.
- Matching is exact after normalization — a line that differs by even one character misses.
- The CN and TC dumps lag behind the current game patch, so newer lines may have no translation.
- Windows only (`win-x64`).

## License

MIT — see [LICENSE](LICENSE).
