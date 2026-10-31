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
- Normalization runs once per lookup. Do not call it twice for the same text.

When you add work to a per-frame path, say in the PR what the cost is and why it is acceptable.
