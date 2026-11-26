using System.Buffers;

namespace Sinophone.Handlers;

/// <summary>
/// Measures text with the node's own font, so the answer matches what the game will draw.
/// </summary>
/// <remarks>
/// <c>GetTextDrawSize</c> takes a null-terminated UTF-8 string. A fixed pointer to the byte array
/// alone has no terminator, and the game then reads past the end of the text looking for one.
/// Every measurement goes through <see cref="WriteNullTerminated"/> so that cannot happen.
/// </remarks>
internal static unsafe class AtkTextMeasure
{
    private const int StackBufferSize = 256;

    public static (ushort Width, ushort Height) Measure(AtkTextNode* node, string text)
    {
        var maximum = Encoding.UTF8.GetMaxByteCount(text.Length) + 1;
        byte[]? rented = maximum > StackBufferSize ? ArrayPool<byte>.Shared.Rent(maximum) : null;
        Span<byte> buffer = rented ?? stackalloc byte[StackBufferSize];

        try
        {
            WriteNullTerminated(text, buffer);

            ushort width = 0;
            ushort height = 0;
            fixed (byte* pointer = buffer)
                node->GetTextDrawSize(&width, &height, pointer);

            return (width, height);
        }
        finally
        {
            if (rented is not null)
                ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <summary>Writes the text as UTF-8 and puts a zero byte after it.</summary>
    /// <returns>The number of text bytes, not counting the terminator.</returns>
    internal static int WriteNullTerminated(string text, Span<byte> destination)
    {
        var length = Encoding.UTF8.GetBytes(text, destination);
        destination[length] = 0;
        return length;
    }
}
