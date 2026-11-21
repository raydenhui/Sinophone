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
                    _store, _configuration, english, context, Dynamic.IntegerVectors[0]);

            case SourceKind.BattleTalkRow:
                return BattleTalkHandler.Resolve(
                    _store, _configuration, english, speaker, context,
                    Dynamic.IntegerVectors[0])?.ChineseText;

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
