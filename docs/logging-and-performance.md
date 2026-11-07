# Logging and performance

### Logging

Add a debug log where a future reader would otherwise have to guess. Good places:

- A lookup that fails when you expected it to succeed.
- A branch that hides the overlay or skips a sheet.
- A load step with a count and a duration.

Use the right level:

- `Debug`: per-event detail, lookups, sheet indexing, skipped rows.
- `Info`: startup summary, artifact load time and entry counts. Once per load, not per frame.
- `Warning`: something missing that the plugin can work around.
- `Error`: a feature is broken and the user will notice.

Never log inside `OverlayWindow.Draw` or `PreDraw`. Those run every frame. Log when the content
changes instead.

### Log groups

The settings window has a **Logs** tab that turns each group of detail messages on or off:
dialogue lookups, native text writes, selection dialogs, nameplates, item names,
translation data, the plugin itself, and the debug probes. This is how a user silences the
one-line-per-nameplate `Native name:` messages without losing the rest.

Write logs through the component's `PluginLog`, not `IPluginLog` directly. The group is
chosen where the component is constructed, so a call site only names the message. `Debug`
and `Info` obey the switch. `Warning` and `Error` always show, so turning a group off cannot
hide a failure. A new group needs an entry in `LogCategory`, `Configuration`, and the Logs
tab.

Use structured logging. Pass values as parameters, not by string concatenation:

```csharp
log.Debug("No translation for {Length} char line.", text.Length);
```

Do not put `[Sinophone]` in log messages. Dalamud already shows the plugin name for each plugin
log line, so the prefix appears twice. Chat messages (`ChatGui.Print`) are not plugin logs and do
keep the prefix, because the game chat does not add it.

### Performance

The overlay draws every frame while a Talk window is open. Keep these rules:

- No allocation in `Draw` or `PreDraw`. Reuse lists and buffers. `OverlayWindow` already keeps a
  `_blocks` list; clear it, do not create a new one.
- No string building on a cache hit. `TryGetDialogue` should return the stored string, not a copy.
- Do not re-read game sheets per frame. Index once at load.
- Rebuild the font atlas only when font settings change. `FontManager.Rebuild` already checks this;
  keep the check.
- Build the atlas for the glyph ranges the text needs. `FontManager.GlyphRanges` lists Latin,
  punctuation, and CJK. `UnicodeRanges.All` makes ImGui try every codepoint in Unicode, which froze
  the game for several seconds at load.
- Build the atlas asynchronously. `FontManager` creates its own atlas with
  `FontAtlasAutoRebuildMode.Async` and calls `BuildFontsAsync`, so rasterizing the CJK ranges runs
  on a worker task. The plugin-private UiBuilder atlas rebuilds on the draw thread. The store is
  not swapped in until the atlas reports ready, so Chinese never draws as boxes.
- Normalization runs once per lookup. Do not call it twice for the same text.
- Keep the index cache working. The first load after install parses the artifact and indexes the
  sheets. Later loads read `Sinophone.index.bin` and skip both. The cache is written on the index
  worker task, never on the game thread. Log its read and write time at `Debug` under the data
  group. A cache miss is normal after a game patch, a settings change, or a data update.

When you add work to a per-frame path, say in the PR what the cost is and why it is acceptable.
