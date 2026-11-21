using Sinophone.Data;
using Sinophone.Handlers;

namespace Sinophone.Coverage;

/// <summary>
/// Calls the resolve a component really uses, so the report measures the shipped path rather than
/// a reimplementation of it. The display half of each handler is not touched: nothing here needs a
/// game, a node, or a font.
/// </summary>
internal sealed class Probes
{
    private readonly TranslationStore _store;
    private readonly Configuration _configuration;

    public Probes(TranslationStore store, Configuration configuration)
    {
        _store = store;
        _configuration = configuration;
    }

    /// <summary>
    /// The integers the game would pass for this line: the numbers the English text shows, in order.
    /// A line with no number gets none, which is what a client does too, rather than a made-up
    /// vector that could make a lookup succeed for the wrong reason.
    /// </summary>
    internal static IReadOnlyList<int>? GameValues(string english)
    {
        var values = new List<int>(4);

        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(
                     english, @"\d[\d,]*", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
        {
            var digits = match.Value.Replace(",", string.Empty, StringComparison.Ordinal);
            if (int.TryParse(digits, out var value))
                values.Add(value);
        }

        return values.Count == 0 ? null : values;
    }

    /// <summary>The store under test, for probes that call it directly.</summary>
    public TranslationStore Store => _store;

    /// <summary>Every component switch on: this measures translation, not the settings.</summary>
    public static Configuration Configuration => new()
    {
        ShowInTalk = true,
        ShowInTalkSubtitle = true,
        ShowInChatBubbles = true,
        ShowInBattleTalk = true,
        ShowInSelectionDialogs = true,
        ShowTargetName = true,
        ShowItemName = true,
        ShowSpeakerName = true,
    };

    /// <summary>
    /// The Chinese the component would show, or null when it would leave the line English.
    /// <paramref name="useId"/> runs the id-first path the game allows when it names the row; with
    /// it false the probe sees the text alone, which is the worst case a miss comes from.
    /// </summary>
    public string? Resolve(Source source, uint rowId, int slot, string english, string? speaker = null, bool useId = true)
    {
        var context = useId
            ? new DialogueContext(new DialogueRowId(source.Sheet, rowId, source.Field, slot))
            : default;

        switch (source.Kind)
        {
            case SourceKind.TalkRow:
                return TalkHandler.Resolve(_store, _configuration, english, speaker, context)?.ChineseText;

            case SourceKind.Balloon:
                return ChatBubbleHandler.Resolve(_store, _configuration, english);

            case SourceKind.Yell:
                return ChatBubbleHandler.Resolve(
                    _store, _configuration, english, context, GameValues(english));

            case SourceKind.BattleTalkRow:
                return BattleTalkHandler.Resolve(
                    _store, _configuration, english, speaker, context,
                    GameValues(english))?.ChineseText;

            case SourceKind.BattleTalkText:
                return BattleTalkHandler.Resolve(_store, _configuration, english, speaker)?.ChineseText;

            case SourceKind.SelectionAddon:
                // The handler's own entry point: addon label, dialogue, then the parameter template.
                return SelectionDialogHandler.TryTranslate(_store, english, out var chinese) ? chinese : null;

            case SourceKind.Name:
                return _store.TryGetName(english, out var name) ? name : null;

            case SourceKind.Item:
                return ItemNameHandler.Resolve(_store, rowId, english)?.ChineseName;

            default:
                return null;
        }
    }
}
