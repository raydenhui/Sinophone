using Dalamud.Game.Chat;
using Dalamud.Game.Text.SeStringHandling;

namespace Sinophone.Handlers;

/// <summary>
/// Puts the English line back into a chat message that the game built from a translation this
/// plugin laid out.
/// </summary>
/// <remarks>
/// Native mode shows Chinese in the game's own windows, and the game sometimes logs the text it was
/// given: the Story tab of the chat log logs NPC dialogue, and a yell bubble with the print-to-log
/// flag logs the yell. Restoring the caller's values is not enough, because the log line can be
/// built from the addon's copy. A short history of the lines this plugin laid out lets the message
/// be matched and the English text put back before the game writes it, so the display stays Chinese
/// and everything else keeps reading English.
/// </remarks>
public sealed class ChatLogRestorer : IDisposable
{
    /// <summary>How many lines are remembered. A log line arrives within a frame or two.</summary>
    private const int History = 16;

    private readonly IChatGui _chatGui;
    private readonly PluginLog _log;
    private readonly ChatLogHistory _history = new();
    private bool _disposed;

    public ChatLogRestorer(IChatGui chatGui, Configuration configuration, IPluginLog log)
    {
        _chatGui = chatGui;
        _log = new PluginLog(log, configuration, LogCategory.Dialogue);
        _chatGui.ChatMessage += OnChatMessage;
    }

    /// <summary>
    /// Remembers one laid-out line, so a chat message the game builds from it can be put back. The
    /// wrapped and the unwrapped form are both worth remembering: the game may log either.
    /// </summary>
    public void Remember(string? chinese, string? english)
        => _history.Remember(chinese, english, History);

    /// <summary>Finds the English line for a translated string.</summary>
    internal bool TryGetEnglish(string? text, out string english)
        => _history.TryGetEnglish(text, out english);

    private void OnChatMessage(IHandleableChatMessage message)
    {
        if (_disposed)
            return;

        try
        {
            if (message is not IMutableChatMessage mutable)
                return;

            // The message and its sender are both part of what the game logged: the line and the
            // speaker name were translated. Handle them independently, or fixing the line would
            // skip the name.
            _history.FindReplacements(
                mutable.Message.TextValue,
                mutable.Sender.TextValue,
                out var englishMessage,
                out var englishSender);

            if (englishMessage is not null)
                mutable.Message = new SeStringBuilder().Append(englishMessage).Build();

            if (englishSender is not null)
                mutable.Sender = new SeStringBuilder().Append(englishSender).Build();

            if (englishMessage is not null || englishSender is not null)
            {
                _log.Debug(
                    "Chat log: put the English back for a message the game logged from a translation " +
                    "(line {Line}, name {Name}).",
                    englishMessage is not null, englishSender is not null);
            }
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "Chat log: could not put the English line back; leaving the message as it is.");
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _chatGui.ChatMessage -= OnChatMessage;
    }
}

/// <summary>
/// The last few lines the plugin laid out, as Chinese to English pairs. Kept separate from
/// <see cref="ChatLogRestorer"/> so the matching is tested without a chat service.
/// </summary>
internal sealed class ChatLogHistory
{
    private readonly LinkedList<(string Chinese, string English)> _recent = new();

    /// <summary>Remembers one pair, newest first. Empty and identical pairs add nothing.</summary>
    public void Remember(string? chinese, string? english, int limit)
    {
        if (string.IsNullOrWhiteSpace(chinese) || string.IsNullOrWhiteSpace(english))
            return;

        var original = english.Trim();
        var translated = chinese.Trim();

        if (string.Equals(translated, original, StringComparison.Ordinal))
            return;

        _recent.AddFirst((translated, original));

        while (_recent.Count > limit)
            _recent.RemoveLast();
    }

    /// <summary>Finds the English line for a translated string.</summary>
    public bool TryGetEnglish(string? text, out string english)
    {
        english = string.Empty;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.Trim();

        foreach (var (chinese, original) in _recent)
        {
            if (!string.Equals(trimmed, chinese, StringComparison.Ordinal))
                continue;

            english = original;
            return true;
        }

        return false;
    }

    public int Count => _recent.Count;

    /// <summary>
    /// Finds the English text for a message and its sender in one pass. Either may be null when
    /// that part was not translated.
    /// </summary>
    public void FindReplacements(
        string? message,
        string? sender,
        out string? englishMessage,
        out string? englishSender)
    {
        englishMessage = TryGetEnglish(message, out var line) ? line : null;
        englishSender = TryGetEnglish(sender, out var name) ? name : null;
    }
}
