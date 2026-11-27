namespace Sinophone.Handlers;

/// <summary>
/// The last few Chinese lines this handler gave a yell. The game copies the line into the bubble
/// text node, so the per-frame sync would otherwise read it as a new English line and look it up
/// again. Comparing the bytes is allocation-free and exact, so a different line never matches.
/// </summary>
internal sealed class RecentBubbleTexts
{
    private const int Capacity = 8;

    private readonly byte[][] _texts = new byte[Capacity][];
    private int _next;

    /// <summary>Remembers a line this handler wrote before the game measured the bubble.</summary>
    public void Remember(string text)
    {
        if (text.Length == 0)
            return;

        _texts[_next] = Encoding.UTF8.GetBytes(text);
        _next = (_next + 1) % Capacity;
    }

    /// <summary>True when the bytes are one of the remembered lines.</summary>
    public bool Contains(ReadOnlySpan<byte> text)
    {
        if (text.IsEmpty)
            return false;

        foreach (var known in _texts)
        {
            if (known is not null && text.SequenceEqual(known))
                return true;
        }

        return false;
    }
}
