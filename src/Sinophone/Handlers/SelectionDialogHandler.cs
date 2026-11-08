using FFXIVClientStructs.FFXIV.Client.UI;

namespace Sinophone.Handlers;

/// <summary>
/// Translates the game's selection dialogs in place. Native mode only: the English
/// text in the addon is replaced with Chinese, and no overlay is shown.
/// </summary>
/// <remarks>
/// The dialog addons are small trees of text nodes. The handler walks the current
/// tree, looks each visible string up in the translation store, and writes the
/// result back into the same node. A miss leaves the English text alone.
/// </remarks>
public sealed class SelectionDialogHandler : IDisposable
{
    private static readonly string[] AddonNames =
    [
        "SelectYesno",
        "SelectString",
        "SelectIconString",
        "SelectOk",
        "CutSceneSelectString",
    ];

    private const int MaxTreeDepth = 64;

    /// <summary>
    /// The cutscene addon builds its option list after setup. Try again for a short
    /// time on later frames, then stop so a dialog with no translations does not
    /// walk its tree every frame.
    /// </summary>
    private const int MaxLateAttempts = 12;

    private static readonly Regex QuotedText = new(@"[“""]([^”""]+)[”""]", RegexOptions.CultureInvariant);
    private static readonly Regex EnglishLevel = new(@"\bLevel:\s*(\d+)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex EnglishItemLevel = new(@"\bItem level:\s*(\d+)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex QuestBattleLevel = new(@"<Sheet\(QuestBattle,IntegerParameter\(2\),3\)\s*/>", RegexOptions.CultureInvariant);

    /// <summary>
    /// A name the Chinese row reads from another sheet, such as the retainer's class
    /// or an item. The English line shows the name the game drew in that slot.
    /// </summary>
    private static readonly Regex SheetNameValue = new(
        @"<\s*Sheet\([^<>]*\)\s*/>",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>
    /// A macro that renders its value from the expression inside the tag, such as
    /// <c>&lt;Format(IntegerParameter(1),FF022C)/&gt;</c>. The tag itself has no text,
    /// so the value must be substituted before the macro strip removes the tag.
    /// </summary>
    private static readonly Regex FormattedValue = new(
        @"<(?:Format|Digit)\(IntegerParameter\((\d+)\)[^<>]*/>",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// A SetTime macro. It fills the date and time values the row draws, and draws
    /// nothing itself, so the whole tag goes.
    /// </summary>
    private static readonly Regex TimeSetter = new(
        @"<\s*Time\([^<>]*\)\s*/>",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>
    /// A date or clock value drawn from a SetTime macro, such as
    /// <c>&lt;Value&gt;TopLevelParameter(222)&lt;/Value&gt;</c>. TwoDigitValue pads
    /// the value to two digits.
    /// </summary>
    private static readonly Regex TimeValue = new(
        @"<(Value|TwoDigitValue)>TopLevelParameter\((\d+)\)</\1>",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>A numeric date such as 8/27, 27/8, 8/27/2026 or 2026/8/27.</summary>
    private static readonly Regex NumericDate = new(
        @"(?<![\d/])(\d{1,4})/(\d{1,2})(?:/(\d{1,4}))?(?![\d/])",
        RegexOptions.CultureInvariant);

    /// <summary>A clock value such as 8:00, 8:00:00 or 8:00 p.m.</summary>
    private static readonly Regex ClockValue = new(
        @"(?<![\d:])(\d{1,2}):(\d{2})(?::(\d{2}))?(?:\s*([aApP])\.?\s*[mM]\.?)?",
        RegexOptions.CultureInvariant);

    /// <summary>UI icon placeholders from the Chinese client; they are not text.</summary>
    private static readonly Regex PrivateUseCharacter = new(@"[\uE000-\uF8FF]", RegexOptions.CultureInvariant);

    private readonly IAddonLifecycle _addonLifecycle;
    private readonly IGameGui _gameGui;
    private readonly PluginLog _log;
    private readonly Configuration _configuration;
    private readonly Func<TranslationStore?> _getStore;
    private readonly NativeTextWriter _nativeWriter;
    private readonly IAddonLifecycle.AddonEventDelegate _onAddonEvent;

    private readonly List<nint> _nodes = [];
    private readonly Dictionary<string, List<nint>> _translated = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _lateAttempts = new(StringComparer.Ordinal);

    private bool _disposed;

    public SelectionDialogHandler(
        IAddonLifecycle addonLifecycle,
        IGameGui gameGui,
        IPluginLog log,
        Configuration configuration,
        Func<TranslationStore?> getStore,
        NativeTextWriter nativeWriter)
    {
        _addonLifecycle = addonLifecycle;
        _gameGui = gameGui;
        _log = new PluginLog(log, configuration, LogCategory.SelectionDialogs);
        _configuration = configuration;
        _getStore = getStore;
        _nativeWriter = nativeWriter;
        _onAddonEvent = OnAddonEvent;

        foreach (var addonName in AddonNames)
        {
            _addonLifecycle.RegisterListener(AddonEvent.PostSetup, addonName, _onAddonEvent);
            _addonLifecycle.RegisterListener(AddonEvent.PostRefresh, addonName, _onAddonEvent);
            _addonLifecycle.RegisterListener(AddonEvent.PostHide, addonName, _onAddonEvent);
            _addonLifecycle.RegisterListener(AddonEvent.PostClose, addonName, _onAddonEvent);
            _addonLifecycle.RegisterListener(AddonEvent.PreFinalize, addonName, _onAddonEvent);
            _addonLifecycle.RegisterListener(AddonEvent.PreDraw, addonName, _onAddonEvent);
        }
    }

    private unsafe void OnAddonEvent(AddonEvent eventType, AddonArgs args)
    {
        if (_disposed)
            return;

        var addonName = args.AddonName;

        if (eventType == AddonEvent.PreDraw)
        {
            if (!IsEnabled(addonName))
            {
                // Turned off while the dialog is open. Put the game's own text back.
                ClearTracked(addonName);
                return;
            }

            Reapply(addonName);
            TranslateLate(addonName);
            return;
        }

        if (eventType is AddonEvent.PostHide or AddonEvent.PostClose or AddonEvent.PreFinalize)
        {
            // A delayed hide from an old dialog must not clear text that a newer
            // dialog has already written into a reused node.
            if (eventType == AddonEvent.PreFinalize)
                ClearTracked(addonName);
            return;
        }

        if (!IsEnabled(addonName))
            return;

        if (eventType == AddonEvent.PostSetup)
            ForgetTracked(addonName);

        _lateAttempts[addonName] = 0;
        Translate(addonName);
    }

    private unsafe void Translate(string addonName)
    {
        var store = _getStore();
        if (store is null)
            return;

        var addon = GetAddon(addonName);
        if (addon is null)
            return;

        _nodes.Clear();

        // Most addons expose their text under RootNode, but the cutscene selection
        // addon can have its nodes only in UldManager's list. Walk both.
        if (addon->RootNode is not null)
            CollectTextNodes(addon->RootNode, 0);

        if (addon->UldManager.NodeList is not null)
        {
            for (var i = 0; i < addon->UldManager.NodeListCount; i++)
                CollectTextNodes(addon->UldManager.NodeList[i], 0);
        }

        CollectTypedTextNodes(addonName, addon);

        var traceNodes = string.Equals(addonName, "CutSceneSelectString", StringComparison.Ordinal)
                         && _lateAttempts.GetValueOrDefault(addonName) <= 1;
        if (traceNodes)
            _log.Debug("CutSceneSelectString scan: {Count} text nodes.", _nodes.Count);

        foreach (var nodeAddress in _nodes)
        {
            var node = (AtkTextNode*)nodeAddress;
            var nodeId = node->NodeId;
            if (nodeId == 0 || _nativeWriter.IsStillOursAt(addonName, nodeAddress))
                continue;

            var source = TalkHandler.ExtractPlainText(node->NodeText.AsSpan());
            if (string.IsNullOrWhiteSpace(source))
                continue;

            if (traceNodes)
                _log.Debug("CutSceneSelectString node {NodeId}: {Text}", nodeId, source);

            if (!TryTranslate(store, source, out var chinese))
            {
                // Drop a saved translation if this node has moved on to a line we
                // cannot translate. English stays on screen.
                _nativeWriter.ClearIfStillOursAt(addonName, nodeAddress);
                continue;
            }

            var fitted = FitPromptLines(node, source, chinese);
            if (_nativeWriter.WriteAt(addonName, nodeAddress, fitted))
            {
                Remember(addonName, nodeAddress);
                _log.Debug("Selection dialog {Addon} node {NodeId}: {Source} -> {Chinese}",
                    addonName, nodeId, source, fitted);
            }
        }
    }

    private unsafe AtkUnitBase* GetAddon(string addonName)
        => _gameGui.GetAddonByName<AtkUnitBase>(addonName, 1);

    /// <summary>
    /// Some dialogs keep their prompt and options in typed component fields, not
    /// in the normal node tree. Add those text nodes to the scan.
    /// </summary>
    private unsafe void CollectTypedTextNodes(string addonName, AtkUnitBase* addon)
    {
        switch (addonName)
        {
            case "SelectYesno":
            {
                var yesNo = (AddonSelectYesno*)addon;
                if (yesNo->PromptText is not null)
                    AddNode(yesNo->PromptText);

                AddButtonNode(yesNo->YesButton);
                AddButtonNode(yesNo->NoButton);
                AddButtonNode(yesNo->AtkComponentButton238);
                break;
            }

            case "SelectOk":
            {
                var selectOk = (AddonSelectOk*)addon;
                if (selectOk->PromptText is not null)
                    AddNode(selectOk->PromptText);

                AddButtonNode(selectOk->OkButton);
                break;
            }

            case "SelectString":
                AddListNodes(((AddonSelectString*)addon)->PopupMenu.PopupMenu.List);
                break;

            case "SelectIconString":
                AddListNodes(((AddonSelectIconString*)addon)->PopupMenu.PopupMenu.List);
                break;

            case "CutSceneSelectString":
            {
                var cutScene = (AddonCutSceneSelectString*)addon;
                if (cutScene->TextLabel is not null && cutScene->TextLabel->AtkTextNode is not null)
                    AddNode(cutScene->TextLabel->AtkTextNode);

                AddListNodes(cutScene->OptionList);
                break;
            }
        }
    }

    private unsafe void AddButtonNode(AtkComponentButton* button)
    {
        if (button is not null && button->ButtonTextNode is not null)
            AddNode(button->ButtonTextNode);
    }

    private unsafe void AddListNodes(AtkComponentList* list)
    {
        if (list is null)
            return;

        var count = list->ListLength;
        for (var i = 0; i < count; i++)
        {
            var renderer = list->GetItemRenderer(i);
            if (renderer is not null && renderer->ButtonTextNode is not null)
                AddNode(renderer->ButtonTextNode);
        }
    }

    private unsafe void AddNode(AtkTextNode* textNode)
    {
        var address = (nint)textNode;
        if (!_nodes.Contains(address))
            _nodes.Add(address);
    }

    private unsafe void CollectTextNodes(AtkResNode* node, int depth)
    {
        if (node is null || depth > MaxTreeDepth)
            return;

        for (var current = node; current is not null; current = current->NextSiblingNode)
        {
            if (current->Type == NodeType.Text)
            {
                var textNode = current->GetAsAtkTextNode();
                if (textNode is not null)
                    AddNode(textNode);
            }

            // Some addons, such as the SelectString popup, keep their entries in a
            // component's own node list. Walk that list as well as the normal child
            // tree so those entries are not missed.
            if ((ushort)current->Type >= 1000)
            {
                var componentNode = (AtkComponentNode*)current;
                var component = componentNode->Component;
                if (component is not null && component->UldManager.NodeList is not null)
                {
                    for (var i = 0; i < component->UldManager.NodeListCount; i++)
                        CollectTextNodes(component->UldManager.NodeList[i], depth + 1);
                }
            }

            if (current->ChildNode is not null)
                CollectTextNodes(current->ChildNode, depth + 1);
        }
    }

    private bool IsEnabled(string addonName)
        => _configuration.Enabled
           && _configuration.ShowInSelectionDialogs
           && _configuration.IsSelectionDialogEnabled(addonName);

    private void TranslateLate(string addonName)
    {
        if (!IsEnabled(addonName))
            return;

        var attempts = _lateAttempts.GetValueOrDefault(addonName);
        if (attempts >= MaxLateAttempts)
            return;

        _lateAttempts[addonName] = attempts + 1;
        Translate(addonName);
    }

    private void Reapply(string addonName)
    {
        if (!IsEnabled(addonName))
            return;

        if (!_translated.TryGetValue(addonName, out var nodeIds))
            return;

        foreach (var address in nodeIds)
            _nativeWriter.ReapplyAt(addonName, address);
    }

    private void Remember(string addonName, nint address)
    {
        if (!_translated.TryGetValue(addonName, out var addresses))
        {
            addresses = [];
            _translated[addonName] = addresses;
        }

        if (!addresses.Contains(address))
            addresses.Add(address);
    }

    private void ClearTracked(string addonName)
    {
        _lateAttempts.Remove(addonName);

        if (!_translated.Remove(addonName, out var addresses))
            return;

        foreach (var address in addresses)
            _nativeWriter.ClearIfStillOursAt(addonName, address);
    }

    /// <summary>
    /// Drops saved state for a recycled dialog without touching the node text.
    /// Clearing here can erase a prompt that the new dialog has just written.
    /// </summary>
    private void ForgetTracked(string addonName)
    {
        _lateAttempts.Remove(addonName);

        if (!_translated.Remove(addonName, out var addresses))
            return;

        foreach (var address in addresses)
            _nativeWriter.ForgetAt(addonName, address);
    }

    /// <summary>
    /// Looks up one selection-dialog string. Short UI labels are also tried with a
    /// trailing period, because the Addon sheet stores "Yes." while the button can
    /// show "Yes".
    /// </summary>
    internal static bool TryTranslate(TranslationStore store, string? text, out string chinese)
    {
        chinese = string.Empty;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        var plainText = MacroNormalizer.CleanForDisplay(text);

        if (store.TryGetAddonLabel(plainText, out var addonLabel))
        {
            chinese = ResolveAddonText(addonLabel, plainText);
            return chinese.Length > 0;
        }

        if (store.TryGetDialogue(text, out chinese))
            return true;

        if (store.TryGetAddonText(plainText, out var addonText, out var captures, out var parameterIndices))
        {
            chinese = ResolveAddonText(addonText, plainText, captures, store, parameterIndices);
            return chinese.Length > 0;
        }

        var trimmed = text.TrimEnd('.', '。', '!', '！', '?', '？');
        if (trimmed.Length > 0
            && trimmed != text
            && store.TryGetDialogue(trimmed, out chinese))
        {
            return true;
        }

        // "Yes" / "No" / "OK" / "Cancel" are short. Do not apply this to normal
        // dialogue, where adding a period could match a different line.
        if (text.Length <= 16
            && text.Length > 0
            && !text.EndsWith('.')
            && store.TryGetDialogue(string.Concat(text, "."), out chinese))
        {
            return true;
        }

        chinese = string.Empty;
        return false;
    }

    /// <summary>
    /// A selection dialog is laid out for the line count of the English text. A
    /// translation with more lines grows the text node past the box and down over
    /// the controls below it, such as the item icon in an exchange prompt. Join the
    /// lines when the joined line still fits the box the game sized for the English
    /// text; keep them when it does not, since a wrapped line would overlap anyway.
    /// </summary>
    internal static string FitPromptLines(
        string source,
        string chinese,
        ushort maxWidth,
        ushort maxHeight,
        Func<string, (ushort Width, ushort Height)> measure)
    {
        if (maxWidth == 0 || maxHeight == 0)
            return chinese;

        if (CountLines(chinese) <= CountLines(source))
            return chinese;

        var joined = JoinLines(chinese);
        var (width, height) = measure(joined);
        return width <= maxWidth && height <= maxHeight ? joined : chinese;
    }

    private static unsafe string FitPromptLines(AtkTextNode* node, string source, string chinese)
        => FitPromptLines(source, chinese, node->Width, node->Height,
            text => MeasureText(node, text));

    private static unsafe (ushort Width, ushort Height) MeasureText(AtkTextNode* node, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var buffer = new byte[bytes.Length + 1];
        bytes.CopyTo(buffer, 0);

        fixed (byte* pointer = buffer)
        {
            ushort width = 0;
            ushort height = 0;
            node->GetTextDrawSize(&width, &height, pointer);
            return (width, height);
        }
    }

    private static int CountLines(string text)
    {
        var lines = 1;
        foreach (var ch in text)
        {
            if (ch == '\n')
                lines++;
        }

        return lines;
    }

    /// <summary>Joins the lines of a translated prompt into one, trimming each line.</summary>
    private static string JoinLines(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var line in text.Split('\n'))
            builder.Append(line.Trim());

        return builder.ToString();
    }

    /// <summary>
    /// Fills the parameters in an Addon-sheet prompt from the live English text.
    /// The sheet stores UNKNOWN where the game puts the quest name and levels.
    /// </summary>
    internal static string ResolveAddonText(
        string chinese,
        string liveEnglish,
        IReadOnlyList<string>? captures = null,
        TranslationStore? store = null,
        IReadOnlyList<int>? parameterIndices = null,
        DateTime? now = null)
    {
        var questName = QuotedText.Match(liveEnglish).Groups[1].Value;
        var level = EnglishLevel.Match(liveEnglish).Groups[1].Value;
        var itemLevel = EnglishItemLevel.Match(liveEnglish).Groups[1].Value;

        var resolved = ResolveItemMacros(chinese, liveEnglish, captures, store);
        resolved = ResolveSheetNames(resolved, captures, parameterIndices, store);

        // A date or clock in the row is drawn from a SetTime macro. The game
        // converts the timestamp to the player's local time, and it does not pass
        // the result to the addon, so the values are read back from the English.
        if (!TryResolveTimeValues(resolved, liveEnglish, now ?? DateTime.Now, out resolved))
            return string.Empty;

        // Addon rows such as "Garden n: Patch n" use the first two integer
        // parameters. The numbers in the live prompt are their fallback value.
        var numbers = new List<string>();
        var currentNumber = new StringBuilder();
        foreach (var ch in liveEnglish)
        {
            if (char.IsDigit(ch))
            {
                currentNumber.Append(ch);
            }
            else if (currentNumber.Length > 0)
            {
                numbers.Add(currentNumber.ToString());
                currentNumber.Clear();
            }
        }
        if (currentNumber.Length > 0)
            numbers.Add(currentNumber.ToString());

        // A Format macro has no text of its own: it renders the value of the
        // expression inside the tag. Replace the whole tag, or the macro strip
        // removes the value along with the tag.
        var missingValue = false;
        resolved = FormattedValue.Replace(resolved, match =>
        {
            if (!int.TryParse(match.Groups[1].Value, out var index))
                return match.Value;

            if (parameterIndices is not null && captures is not null)
            {
                var count = Math.Min(parameterIndices.Count, captures.Count);
                for (var i = 0; i < count; i++)
                {
                    if (parameterIndices[i] == index)
                        return captures[i].Trim();
                }
            }

            if (index > 0 && index <= numbers.Count)
                return numbers[index - 1];

            missingValue = true;
            return match.Value;
        });

        if (missingValue)
            return string.Empty;

        if (parameterIndices is not null && captures is not null)
        {
            var count = Math.Min(parameterIndices.Count, captures.Count);
            for (var i = 0; i < count; i++)
            {
                var index = parameterIndices[i];
                if (index <= 0)
                    continue;

                var captured = captures[i];
                resolved = resolved.Replace($"StringParameter({index})", captured, StringComparison.Ordinal);
                var number = Regex.Match(captured, @"\d+");
                if (number.Success)
                    resolved = resolved.Replace($"IntegerParameter({index})", number.Value, StringComparison.Ordinal);
            }
        }

        resolved = QuestBattleLevel.Replace(resolved, level);
        resolved = Regex.Replace(resolved, @"StringParameter\(1\)", questName);
        if (level.Length > 0)
            resolved = Regex.Replace(resolved, @"IntegerParameter\(2\)", level);
        if (itemLevel.Length > 0)
            resolved = Regex.Replace(resolved, @"IntegerParameter\(3\)", itemLevel);

        for (var i = 0; i < numbers.Count; i++)
            resolved = resolved.Replace($"IntegerParameter({i + 1})", numbers[i], StringComparison.Ordinal);
        resolved = Regex.Replace(resolved, @"IntegerParameter(\d+)", string.Empty);

        // If a required parameter could not be filled, do not show a broken
        // Chinese prompt. Leaving the original English is safer.
        if (resolved.Contains("IntegerParameter(", StringComparison.Ordinal)
            || resolved.Contains("StringParameter(", StringComparison.Ordinal)
            || resolved.Contains("PlayerParameter(", StringComparison.Ordinal)
            || resolved.Contains("TopLevelParameter(", StringComparison.Ordinal)
            || resolved.Contains("<Sheet(", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        // Private-use characters are UI icons, not text. The English client font
        // does not draw them.
        resolved = PrivateUseCharacter.Replace(resolved, string.Empty);

        return MacroNormalizer.CleanForDisplay(resolved);
    }

    /// <summary>
    /// Fills a name the Chinese row reads from another sheet. The English line
    /// shows the name the game drew, so the store's name lookup translates it. A
    /// name the store does not know keeps the English prompt.
    /// </summary>
    private static string ResolveSheetNames(
        string chinese,
        IReadOnlyList<string>? captures,
        IReadOnlyList<int>? parameterIndices,
        TranslationStore? store)
    {
        if (store is null
            || captures is null
            || !chinese.Contains("<Sheet(", StringComparison.OrdinalIgnoreCase))
        {
            return chinese;
        }

        // A capture with a parameter index fills an IntegerParameter slot, so it is
        // not a name. The names are the captures the template drew from a Sheet.
        var used = new bool[captures.Count];
        for (var i = 0; i < captures.Count; i++)
        {
            used[i] = parameterIndices is not null
                      && i < parameterIndices.Count
                      && parameterIndices[i] > 0;
        }

        return SheetNameValue.Replace(chinese, match =>
        {
            for (var i = 0; i < captures.Count; i++)
            {
                if (used[i] || captures[i].Length == 0)
                    continue;

                if (store.TryGetName(captures[i], out var name)
                    || store.TryGetAddonLabel(captures[i], out name))
                {
                    used[i] = true;
                    return name;
                }
            }

            return match.Value;
        });
    }

    /// <summary>
    /// Fills the date and time values a Chinese Addon row draws from a SetTime
    /// macro. Returns false when a value the row needs is not in the English line,
    /// because a half-filled date reads worse than the English.
    /// </summary>
    private static bool TryResolveTimeValues(
        string chinese,
        string liveEnglish,
        DateTime now,
        out string resolved)
    {
        resolved = TimeSetter.Replace(chinese, string.Empty);

        if (!resolved.Contains("TopLevelParameter(", StringComparison.Ordinal))
            return true;

        var date = ReadDate(liveEnglish, now);
        var clock = ReadClock(liveEnglish);

        var missing = false;
        resolved = TimeValue.Replace(resolved, match =>
        {
            var value = ReadTopLevelValue(match.Groups[2].Value, date, clock);
            if (value is null)
            {
                missing = true;
                return match.Value;
            }

            return string.Equals(match.Groups[1].Value, "TwoDigitValue", StringComparison.OrdinalIgnoreCase)
                ? value.PadLeft(2, '0')
                : value;
        });

        return !missing;
    }

    /// <summary>
    /// Reads the date the game drew in the English line. The year is often left
    /// out. The prompt names a time close to now, so a month and day that have
    /// already passed belong to next year.
    /// </summary>
    private static DateTime? ReadDate(string text, DateTime now)
    {
        var match = NumericDate.Match(text);
        if (!match.Success)
            return null;

        var first = int.Parse(match.Groups[1].Value);
        var second = int.Parse(match.Groups[2].Value);
        var year = now.Year;
        var hasYear = match.Groups[3].Success;

        if (match.Groups[1].Value.Length >= 4)
        {
            // Year first, as in 2026/8/27.
            if (!hasYear)
                return null;

            year = first;
            first = second;
            second = int.Parse(match.Groups[3].Value);
        }
        else if (hasYear)
        {
            year = int.Parse(match.Groups[3].Value);
            if (year < 100)
                year += 2000;
        }

        // The English client writes the month first. A number above 12 can only
        // be a day, so a client that writes the day first still reads right.
        var month = first;
        var day = second;
        if (first > 12)
        {
            month = second;
            day = first;
        }

        if (month is < 1 or > 12 || day is < 1 or > 31)
            return null;

        if (day > DateTime.DaysInMonth(year, month))
            return null;

        var date = new DateTime(year, month, day);
        if (!hasYear && date < now.Date)
            date = date.AddYears(1);

        return date;
    }

    /// <summary>Reads the clock time the game drew in the English line.</summary>
    private static (int Hour, int Minute, int Second)? ReadClock(string text)
    {
        var match = ClockValue.Match(text);
        if (!match.Success)
            return null;

        var hour = int.Parse(match.Groups[1].Value);
        var minute = int.Parse(match.Groups[2].Value);
        var second = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0;

        if (match.Groups[4].Success)
        {
            var afternoon = char.ToLowerInvariant(match.Groups[4].Value[0]) == 'p';
            hour = (hour % 12) + (afternoon ? 12 : 0);
        }

        if (hour > 23 || minute > 59 || second > 59)
            return null;

        return (hour, minute, second);
    }

    /// <summary>
    /// Reads one date or clock value by its TopLevelParameter index. The SetTime
    /// macro sets 222 year, 221 month, 220 weekday, 219 day, 218 hour, 217 minute,
    /// 216 second and 215 millisecond.
    /// </summary>
    private static string? ReadTopLevelValue(
        string indexText,
        DateTime? date,
        (int Hour, int Minute, int Second)? clock)
    {
        if (!int.TryParse(indexText, out var index))
            return null;

        if (index is 215 or 216 or 217 or 218)
        {
            if (clock is not { } clockTime)
                return null;

            return index switch
            {
                215 => "0",
                216 => clockTime.Second.ToString(),
                217 => clockTime.Minute.ToString(),
                _ => clockTime.Hour.ToString(),
            };
        }

        if (date is not { } parsedDate)
            return null;

        return index switch
        {
            219 => parsedDate.Day.ToString(),
            220 => ((int)parsedDate.DayOfWeek + 1).ToString(),
            221 => parsedDate.Month.ToString(),
            222 => parsedDate.Year.ToString(),
            _ => null,
        };
    }

    private static string ResolveItemMacros(
        string chinese,
        string liveEnglish,
        IReadOnlyList<string>? captures,
        TranslationStore? store)
    {
        if (!chinese.Contains("Sheet(Item,IntegerParameter(", StringComparison.Ordinal))
            return chinese;

        // Addon row 6413: "Prepare the bed with <item> and <item>?"
        var match = Regex.Match(liveEnglish, @"^Prepare the bed with (.+) and (.+)\?$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!match.Success)
            return chinese;

        var first = NormalizeItemName(match.Groups[1].Value);
        var second = NormalizeItemName(match.Groups[2].Value);

        if (store is not null)
        {
            if (store.TryGetName(first, out var firstChinese))
                first = firstChinese;
            if (store.TryGetName(second, out var secondChinese))
                second = secondChinese;
        }

        chinese = Regex.Replace(chinese, @"<Sheet\(Item,IntegerParameter\(1\),0\)\s*/>", first);
        return Regex.Replace(chinese, @"<Sheet\(Item,IntegerParameter\(2\),0\)\s*/>", second);
    }

    private static string NormalizeItemName(string name)
    {
        var normalized = MacroNormalizer.NormalizeKey(name);

        if (normalized.StartsWith("an ", StringComparison.OrdinalIgnoreCase))
            return normalized[3..];

        if (normalized.StartsWith("a ", StringComparison.OrdinalIgnoreCase))
            return normalized[2..];

        if (normalized.StartsWith("the ", StringComparison.OrdinalIgnoreCase))
            return normalized[4..];

        return normalized;
    }

    /// <summary>
    /// Puts the game's own text back into every dialog this handler has changed.
    /// Called when a setting turns the surface off, so an open dialog does not
    /// keep Chinese until it closes.
    /// </summary>
    public void RestoreAll()
    {
        if (_disposed)
            return;

        foreach (var addonName in AddonNames)
            ClearTracked(addonName);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        foreach (var addonName in AddonNames)
        {
            _addonLifecycle.UnregisterListener(AddonEvent.PostSetup, addonName, _onAddonEvent);
            _addonLifecycle.UnregisterListener(AddonEvent.PostRefresh, addonName, _onAddonEvent);
            _addonLifecycle.UnregisterListener(AddonEvent.PostHide, addonName, _onAddonEvent);
            _addonLifecycle.UnregisterListener(AddonEvent.PostClose, addonName, _onAddonEvent);
            _addonLifecycle.UnregisterListener(AddonEvent.PreFinalize, addonName, _onAddonEvent);
            _addonLifecycle.UnregisterListener(AddonEvent.PreDraw, addonName, _onAddonEvent);
            ClearTracked(addonName);
        }
    }
}
