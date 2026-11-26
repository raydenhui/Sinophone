namespace Sinophone.Data;

public sealed partial class TranslationStore
{
    private void Index(
        TranslationArtifact artifact,
        IReadOnlyList<ISheetDescriptor> descriptors,
        SheetExtraction extraction,
        PluginLog log)
    {
        var indexedRows = 0;
        var indexedSlots = 0;

        foreach (var descriptor in descriptors)
        {
            if (!artifact.Sheets.TryGetValue(descriptor.Name, out var rows))
            {
                log.Debug("Artifact has no rows for sheet {Sheet}.", descriptor.Name);
                continue;
            }

            var isNameSheet = descriptor.IsNameSheet;
            var isAddon = string.Equals(descriptor.Name, "Addon", StringComparison.Ordinal);

            foreach (var (rowKey, fields) in rows)
            {
                if (!uint.TryParse(rowKey, out var rowId))
                    continue;

                if (!fields.TryGetValue(descriptor.FieldName, out var chineseSlots) || chineseSlots.Length == 0)
                    continue;

                if (!extraction.TryGet(descriptor.Name, descriptor.FieldName, rowId, out var extracted))
                    continue;

                var englishSlots = extracted.EnglishSlots;

                var slotCount = Math.Min(englishSlots.Count, chineseSlots.Length);

                string[]? rowSlots = null;
                var idKey = (Sheet: descriptor.Name, RowId: rowId, Field: descriptor.FieldName);

                if (!isNameSheet)
                {
                    if (!_rowLookup.TryGetValue(idKey, out rowSlots))
                    {
                        rowSlots = new string[chineseSlots.Length];
                        _rowLookup[idKey] = rowSlots;
                    }
                    else if (rowSlots.Length < chineseSlots.Length)
                    {
                        Array.Resize(ref rowSlots, chineseSlots.Length);
                        _rowLookup[idKey] = rowSlots;
                    }
                }

                for (var i = 0; i < slotCount; i++)
                {
                    var display = PrepareDisplay(chineseSlots[i]);
                    if (display.Length == 0)
                        continue;

                    RegisterStyle(display, chineseSlots[i]);

                    if (isNameSheet)
                        _names.AddRow(descriptor.Name, rowId, display);

                    if (rowSlots is not null && string.IsNullOrEmpty(rowSlots[i]))
                        rowSlots[i] = display;

                    var variants = englishSlots[i];
                    for (var v = 0; v < variants.Count; v++)
                    {
                        var key = MacroNormalizer.NormalizeKey(variants[v]);
                        if (key.Length == 0)
                            continue;

                        var result = isNameSheet
                            ? _names.AddText(key, display)
                            : _dialogue.AddSheetText(key, display);

                        if (result == KanaTextAdd.Added)
                        {
                            indexedSlots++;
                        }
                        else if (result == KanaTextAdd.Replaced)
                        {
                            // The CN and TC dumps leave a few rows in Japanese. Another row
                            // with the same English name can hold the localised one, so the
                            // kana row must not win just because its id comes first.
                            log.Debug("Replaced a Japanese row with a Chinese one for {Text}.", key);
                        }

                        // A sheet row can branch on the player's gender too, as "Ah, Master
                        // Player" does against "Ah, Mistress Player". Keep a key with the
                        // gendered words collapsed so either wording finds the row, in case the
                        // live sheet's branch list is not the one the game drew.
                        if (!isNameSheet)
                            _dialogue.AddBranchNeutral(key, display);

                        if (isAddon && !isNameSheet)
                        {
                            _addon.AddLabel(key, display);

                            if (extracted.ParameterTemplate is { } parameterTemplate)
                            {
                                // "Enter ?" is only 7 literal characters and would match
                                // unrelated prompts such as "Enter the estate hall?".
                                _addon.AddParameterized(
                                    parameterTemplate,
                                    chineseSlots[i],
                                    MacroTemplateFor(artifact, rowId));
                            }
                        }

                        if (isAddon)
                        {
                            _addon.AddPromptVariants(
                                variants[v],
                                chineseSlots[i],
                                MacroTemplateFor(artifact, rowId));
                        }
                    }
                }

                indexedRows++;
            }
        }

        var indexedScripts = IndexScripts(artifact, log);

        log.Debug("Indexed {Rows:N0} rows, {Slots:N0} text slots and {Scripts:N0} script lines from the {Language} artifact.",
            indexedRows, indexedSlots, indexedScripts, artifact.Language);
    }

    /// <summary>
    /// Indexes dialogue that has no Lumina row. The artifact carries both the English
    /// key and the translated text, because there is no sheet to read the English from.
    /// </summary>
    private int IndexScripts(TranslationArtifact artifact, PluginLog log)
    {
        var added = 0;

        // Keys this artifact added, so a collision inside one artifact is told apart
        // from the other variant simply owning the line.
        var indexedInThisArtifact = new Dictionary<string, ScriptIndex.ScriptCandidate>(StringComparer.Ordinal);

        // The scene each English line belongs to, from every key in this artifact. A line with
        // a key outside a cutscene, or keys from two cutscenes, cannot name one scene.
        var scenesInThisArtifact = new Dictionary<string, uint>(StringComparer.Ordinal);
        var sceneConflicts = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (scriptKey, entry) in artifact.Scripts)
        {
            // Event speakers such as "Distant Echo" often exist only as a script
            // alias, not as a row in a name sheet. Learn the Chinese alias from the
            // translated line so the speaker node can use it.
            var englishAlias = MacroNormalizer.ExtractSpeakerPrefix(entry.English);
            var chineseAlias = MacroNormalizer.ExtractSpeakerPrefix(entry.Text);
            if (englishAlias.Length > 0
                && chineseAlias.Length > 0
                && !string.Equals(englishAlias, "Player", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(englishAlias, MacroNormalizer.PlayerNamePlaceholder, StringComparison.OrdinalIgnoreCase))
            {
                var aliasKey = MacroNormalizer.NormalizeKey(englishAlias);
                var aliasText = MacroNormalizer.CleanForDisplay(chineseAlias);
                if (aliasKey.Length > 0 && aliasText.Length > 0)
                    _names.AddIfMissing(aliasKey, aliasText);
            }

            // The dumps may prefix the English line with a speaker alias such as
            // "(-Radio-)". The game draws that name in a separate node, so the
            // dialogue text lookup must not include it.
            var key = MacroNormalizer.NormalizeKey(MacroNormalizer.StripSpeakerPrefix(entry.English));
            if (key.Length == 0 || string.Equals(key, "UNKNOWN", StringComparison.OrdinalIgnoreCase))
            {
                log.Debug("Skipping script entry with placeholder English: {ScriptKey}", scriptKey);
                continue;
            }

            var display = PrepareDisplay(entry.Text);
            if (display.Length == 0)
                continue;

            if (englishAlias.Length > 0 && chineseAlias.Length > 0)
            {
                var aliasText = MacroNormalizer.CleanForDisplay(chineseAlias);
                if (aliasText.Length > 0)
                    _dialogue.AddAlias(key, englishAlias.Trim(), aliasText);
            }

            ScriptIndex.NoteScene(scenesInThisArtifact, sceneConflicts, key, scriptKey);

            RegisterStyle(display, entry.Text);

            // Some script dumps leave a double-space gap where a runtime value
            // such as an item name is inserted. Keep a template for that case.
            if (entry.English.Contains("  ", StringComparison.Ordinal))
            {
                var blankTemplate = ScriptTextTemplate.BuildBlankGapTemplate(
                    MacroNormalizer.StripSpeakerPrefix(entry.English));
                if (ScriptTextTemplate.LiteralLength(blankTemplate) >= 4)
                    _scripts.AddTemplate(blankTemplate, display);
            }

            // The dump key is the game's own id for this line. The game sometimes
            // exposes it through the event handler, so keep it separate from the
            // English key and let an id lookup win over duplicate text.
            if (!string.IsNullOrWhiteSpace(scriptKey))
                _scripts.AddKey(scriptKey, display, key, entry.Sheet, entry.RowId);

            if (_scripts.HasAmbiguous(key))
            {
                _scripts.AddCandidate(key, scriptKey, display);
                continue;
            }

            if (indexedInThisArtifact.TryGetValue(key, out var first))
            {
                if (string.Equals(first.Display, display, StringComparison.Ordinal))
                    continue;

                // Two script lines share this English key with different Chinese. The
                // key cannot tell them apart, so it must not answer either one. Keep
                // both candidates so the speaker name can still choose.
                _dialogue.RemoveScriptText(key);
                added--;

                _scripts.PromoteToAmbiguous(key, first, scriptKey, display);

                // The collapsed key would give the same wrong answer for the other
                // wording of the line.
                _dialogue.DropBranchNeutral(key, first.Display);

                continue;
            }

            if (!_dialogue.AddScriptText(key, display, out var sheetOwnsOtherText))
            {
                // A sheet row owns this English text, but a script line also uses it and
                // says something else. Keep the sheet text as the text fallback and let
                // the script candidates be selected by their own identity.
                if (sheetOwnsOtherText)
                    _scripts.AddCandidate(key, scriptKey, display);

                // Otherwise the configured variant already owns this line. Its collapsed key
                // is built, and the other variant's Chinese only conflicts with it, so the
                // whole index would look ambiguous.
                continue;
            }

            added++;
            indexedInThisArtifact[key] = ScriptIndex.CreateCandidate(display, scriptKey);

            // The English dump holds one branch's wording, so keep a key with the
            // branch words collapsed for the others. A line that already matches
            // exactly keeps priority, because the exact lookup runs first.
            _dialogue.AddBranchNeutral(key, display);
        }

        _scripts.MergeScenes(scenesInThisArtifact, sceneConflicts);

        if (_dialogue.BranchNeutralCount > 0)
        {
            log.Debug("Indexed {Count:N0} collapsed script keys.", _dialogue.BranchNeutralCount);
        }

        if (_dialogue.AmbiguousBranchKeyCount > 0)
        {
            log.Debug(
                "{Count:N0} collapsed script keys map to more than one line and are not used.",
                _dialogue.AmbiguousBranchKeyCount);
        }

        if (_scripts.AmbiguousKeyCount > 0)
        {
            log.Debug(
                "{Count:N0} script keys map to more than one line; the speaker name picks one.",
                _scripts.AmbiguousKeyCount);
        }

        return added;
    }

    /// <summary>The converted macro string the artifact stored for one Addon row.</summary>
    private static string? MacroTemplateFor(TranslationArtifact artifact, uint rowId)
        => artifact.AddonTemplates.TryGetValue(rowId.ToString(), out var macro) ? macro : null;

    private void RegisterStyle(string display, string rawText)
    {
        if (UiTextStyle.TryParse(rawText, out var style))
            _styleLookup.TryAdd(display, style);
    }
}
