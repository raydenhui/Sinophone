using Dalamud.Game.Text.Evaluator;
using Sinophone.Diagnostics;

namespace Sinophone.Handlers;

/// <summary>
/// The text work for a selection dialog: find the line, fill its runtime values, fit the prompt
/// to the box, and read the date the game drew. Kept out of the addon hooks so it can be tested
/// without the game.
/// </summary>
internal static class SelectionPromptText
{
    private static readonly Regex QuotedText = new(
        @"[“""「『]([^”""」』]+)[”""」』]",
        RegexOptions.CultureInvariant);

    private static readonly Regex EnglishLevel = new(
        @"\b(?:Level|レベル)\s*[:：]?\s*(\d+)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex EnglishItemLevel = new(
        @"(?:Item level|アイテムレベル)\s*[:：]?\s*(\d+)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex QuestBattleLevel = new(@"<Sheet\(QuestBattle,IntegerParameter\(2\),3\)\s*/>", RegexOptions.CultureInvariant);

    /// <summary>
    /// A name the Chinese row reads from another sheet, such as the retainer's class
    /// or an item. The English line shows the name the game drew in that slot.
    /// </summary>
    private static readonly Regex SheetNameValue = new(
        @"<\s*Sheet\([^<>]*\)\s*/>",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>A number as the English shows it, including thousands separators.</summary>
    private static readonly Regex EnglishDigits = new(
        @"\d[\d,]*",
        RegexOptions.CultureInvariant);

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

    /// <summary>A numeric date such as 8/27, 27/8, 8/27/2026, 2026-08-27 or 27.8.</summary>
    private static readonly Regex NumericDate = new(
        @"(?<![\d/.-])(\d{1,4})[/.-](\d{1,2})(?:[/.-](\d{1,4}))?(?![\d/.-])",
        RegexOptions.CultureInvariant);

    /// <summary>A Japanese date such as 2013年8月27日 or 8月27日.</summary>
    private static readonly Regex JapaneseDate = new(
        @"(?:(\d{4})年)?(\d{1,2})月(\d{1,2})日",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// A clock value such as 8:00, 8:00:00, 8:00 p.m., 8時, 8時00分, 午前8時 or 午後8時.
    /// </summary>
    private static readonly Regex ClockValue = new(
        @"(?<![\d:])(?:(午前|午後)\s*)?(\d{1,2})(?::(\d{2})(?::(\d{2}))?|時(\d{1,2})?分?)(?:\s*([aApP])\.?\s*[mM]\.?)?",
        RegexOptions.CultureInvariant);

    /// <summary>UI icon placeholders from the Chinese client; they are not text.</summary>
    private static readonly Regex PrivateUseCharacter = new(@"[\uE000-\uF8FF]", RegexOptions.CultureInvariant);

    /// <summary>
    /// The scene every text in one dialog belongs to, or 0 when they name different scenes or
    /// none. Options from two scenes are not narrowed, because one of them would be wrong.
    /// </summary>
    internal static uint DialogCutscene(TranslationStore store, IEnumerable<string> texts)
    {
        uint scene = 0;

        foreach (var text in texts)
        {
            if (!store.TryGetCutsceneId(text, out var candidate))
                continue;

            if (scene == 0)
                scene = candidate;
            else if (scene != candidate)
                return 0;
        }

        return scene;
    }

    /// <summary>
    /// Looks up one selection-dialog string. Short UI labels are also tried with a
    /// trailing period, because the Addon sheet stores "Yes." while the button can
    /// show "Yes".
    /// </summary>
    internal static bool TryTranslate(TranslationStore store, string? text, out string chinese)
        => TryTranslate(store, text, default, null, out chinese);

    internal static bool TryTranslate(
        TranslationStore store,
        string? text,
        DialogueContext context,
        out string chinese)
        => TryTranslate(store, text, context, null, out chinese);

    internal static bool TryTranslate(
        TranslationStore store,
        string? text,
        DialogueContext context,
        ISeStringEvaluator? evaluator,
        out string chinese,
        FailureTrace? trace = null)
    {
        chinese = string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            trace?.Step("dialog.text", "empty");
            return false;
        }

        var plainText = MacroNormalizer.CleanForDisplay(text);

        if (store.TryGetAddonLabel(plainText, out var addonLabel, trace))
        {
            chinese = ResolveAddonText(addonLabel, plainText);
            if (chinese.Length > 0)
            {
                trace?.Step("dialog.path", "label");
                return true;
            }

            trace?.Step("dialog.label", "unresolved");
        }

        // The context matters here: a cutscene choice is a VOICEMAN script line, and several
        // cutscenes can share one English option. The scene number picks the right one.
        if (store.TryGetDialogue(text, in context, speakerName: null, out chinese, trace))
        {
            // A line found by its text can hold values the dialog does not pass, such as the price
            // in "Pay 2,000 gil?". The same resolve the templates use fills them from the English
            // line, and leaves the prompt English when a value cannot be filled.
            chinese = ResolveAddonText(chinese, plainText);
            if (chinese.Length > 0)
            {
                trace?.Step("dialog.path", "dialogue");
                return true;
            }

            trace?.Step("dialog.dialogue", "unresolved");
        }

        if (store.TryGetAddonText(
                plainText,
                out var addonText,
                out var captures,
                out var parameterIndices,
                out var macroTemplate,
                trace))
        {
            var evaluated = EvaluateAddonTemplate(evaluator, macroTemplate, captures, parameterIndices);
            if (evaluated is not null)
            {
                chinese = MacroNormalizer.CleanForDisplay(evaluated);
                if (chinese.Length > 0)
                {
                    trace?.Step("dialog.path", "evaluator");
                    return true;
                }

                trace?.Step("dialog.evaluator", "empty");
                return false;
            }

            chinese = ResolveAddonText(addonText, plainText, captures, store, parameterIndices);
            if (chinese.Length > 0)
            {
                trace?.Step("dialog.path", "template", approximate: true);
                return true;
            }

            trace?.Step("dialog.template", "unresolved");
        }

        // The retries keep the scene. A cutscene option that needs one of them would otherwise
        // miss even though the scene was available.
        var trimmed = text.TrimEnd('.', '。', '!', '！', '?', '？');
        if (trimmed.Length > 0
            && trimmed != text
            && store.TryGetDialogue(trimmed, in context, null, out chinese, trace))
        {
            trace?.Step("dialog.path", "trimmed", approximate: true);
            return true;
        }

        // "Yes" / "No" / "OK" / "Cancel" are short. Do not apply this to normal
        // dialogue, where adding a period could match a different line.
        if (text.Length <= 16
            && text.Length > 0
            && !text.EndsWith('.')
            && store.TryGetDialogue(string.Concat(text, "."), in context, null, out chinese, trace))
        {
            trace?.Step("dialog.path", "period", approximate: true);
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
    /// Evaluates the converted Chinese template with the values captured from the
    /// live prompt. Returns null when the template cannot be evaluated here and the
    /// caller should use the replacement path.
    /// </summary>
    /// <remarks>
    /// A <c>settime</c> template needs the unix timestamp the game passed, which the
    /// rendered date does not hold. A <c>sheet</c> template would read the client's
    /// own sheet, which is the wrong language. Both keep the old path.
    /// </remarks>
    internal static string? EvaluateAddonTemplate(
        ISeStringEvaluator? evaluator,
        string? macroTemplate,
        IReadOnlyList<string>? values,
        IReadOnlyList<int>? indices)
    {
        if (evaluator is null || string.IsNullOrEmpty(macroTemplate))
            return null;

        if (macroTemplate.Contains("settime(", StringComparison.Ordinal)
            || macroTemplate.Contains("<sheet(", StringComparison.Ordinal))
        {
            return null;
        }

        var locals = BuildAddonLocals(values, indices);

        try
        {
            var text = evaluator.EvaluateMacroString(macroTemplate, locals).ExtractText();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Places each captured value at the parameter index the Chinese row uses.
    /// A value that is a whole number becomes a number, so <c>lnum</c> can read it;
    /// anything else stays a string for <c>lstr</c>.
    /// </summary>
    internal static SeStringParameter[] BuildAddonLocals(
        IReadOnlyList<string>? values,
        IReadOnlyList<int>? indices)
    {
        var highest = 0;
        if (indices is not null)
        {
            foreach (var index in indices)
                highest = Math.Max(highest, index);
        }

        var locals = new SeStringParameter[highest];
        if (indices is null || values is null)
            return locals;

        var count = Math.Min(indices.Count, values.Count);
        for (var i = 0; i < count; i++)
        {
            var index = indices[i];
            if (index <= 0 || index > locals.Length)
                continue;

            var value = values[i].Trim();
            var numeric = value.Replace(",", string.Empty, StringComparison.Ordinal);
            locals[index - 1] = uint.TryParse(numeric, out var number)
                ? new SeStringParameter(number)
                : new SeStringParameter(value);
        }

        return locals;
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
        // parameters. The numbers in the live prompt are their fallback value, and a price keeps the
        // separators the English showed: "2,000" is one number, not two.
        var numbers = new List<string>();
        foreach (System.Text.RegularExpressions.Match match in EnglishDigits.Matches(liveEnglish))
            numbers.Add(match.Value);

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
    /// Reads the date the game drew in the live line. The year is often left out.
    /// The prompt names a time close to now, so a month and day that have already
    /// passed belong to next year.
    /// </summary>
    private static DateTime? ReadDate(string text, DateTime now)
    {
        var japanese = JapaneseDate.Match(text);
        if (japanese.Success)
        {
            var hasYear = japanese.Groups[1].Success;
            var year = hasYear ? int.Parse(japanese.Groups[1].Value) : now.Year;
            return BuildDate(
                year,
                int.Parse(japanese.Groups[2].Value),
                int.Parse(japanese.Groups[3].Value),
                hasYear,
                now);
        }

        var match = NumericDate.Match(text);
        if (!match.Success)
            return null;

        var first = int.Parse(match.Groups[1].Value);
        var second = int.Parse(match.Groups[2].Value);
        var numericYear = now.Year;
        var numericHasYear = match.Groups[3].Success;

        if (match.Groups[1].Value.Length >= 4)
        {
            // Year first, as in 2026/8/27.
            if (!numericHasYear)
                return null;

            numericYear = first;
            first = second;
            second = int.Parse(match.Groups[3].Value);
        }
        else if (numericHasYear)
        {
            numericYear = int.Parse(match.Groups[3].Value);
            if (numericYear < 100)
                numericYear += 2000;
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

        return BuildDate(numericYear, month, day, numericHasYear, now);
    }

    private static DateTime? BuildDate(int year, int month, int day, bool hasYear, DateTime now)
    {
        if (month is < 1 or > 12 || day is < 1 or > 31)
            return null;

        if (day > DateTime.DaysInMonth(year, month))
            return null;

        var date = new DateTime(year, month, day);
        if (!hasYear && date < now.Date)
            date = date.AddYears(1);

        return date;
    }

    /// <summary>Reads the clock time the game drew in the live line.</summary>
    private static (int Hour, int Minute, int Second)? ReadClock(string text)
    {
        var match = ClockValue.Match(text);
        if (!match.Success)
            return null;

        var hour = int.Parse(match.Groups[2].Value);
        var minute = match.Groups[3].Success
            ? int.Parse(match.Groups[3].Value)
            : match.Groups[5].Success ? int.Parse(match.Groups[5].Value) : 0;
        var second = match.Groups[4].Success ? int.Parse(match.Groups[4].Value) : 0;

        var morning = string.Equals(match.Groups[1].Value, "午前", StringComparison.Ordinal);
        var afternoon = string.Equals(match.Groups[1].Value, "午後", StringComparison.Ordinal)
                        || (match.Groups[6].Success
                            && char.ToLowerInvariant(match.Groups[6].Value[0]) == 'p');

        if (morning)
            hour %= 12;
        else if (afternoon)
            hour = (hour % 12) + 12;

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
}
