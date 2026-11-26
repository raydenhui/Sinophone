using Sinophone.Handlers;

namespace Sinophone.Tests;

public class AtkTextMeasureTests
{
    [Fact]
    public void WriteNullTerminated_PutsAZeroAfterTheText()
    {
        var buffer = new byte[4];

        Assert.Equal(3, AtkTextMeasure.WriteNullTerminated("abc", buffer));

        Assert.Equal((byte)'a', buffer[0]);
        Assert.Equal((byte)'b', buffer[1]);
        Assert.Equal((byte)'c', buffer[2]);
        Assert.Equal(0, buffer[3]);
    }

    [Fact]
    public void WriteNullTerminated_WritesUtf8Bytes()
    {
        var buffer = new byte[7];

        Assert.Equal(6, AtkTextMeasure.WriteNullTerminated("你好", buffer));

        Assert.Equal(new byte[] { 0xE4, 0xBD, 0xA0, 0xE5, 0xA5, 0xBD }, buffer[..6]);
        Assert.Equal(0, buffer[6]);
    }

    [Fact]
    public void WriteNullTerminated_HandlesAnEmptyString()
    {
        var buffer = new byte[1];

        Assert.Equal(0, AtkTextMeasure.WriteNullTerminated(string.Empty, buffer));
        Assert.Equal(0, buffer[0]);
    }

    [Fact]
    public void WriteNullTerminated_LeavesTheRestOfTheBufferAlone()
    {
        var buffer = new byte[8];
        buffer.AsSpan().Fill(0xFF);

        var length = AtkTextMeasure.WriteNullTerminated("ab", buffer);

        Assert.Equal(2, length);
        Assert.Equal(0, buffer[2]);
        Assert.All(buffer[3..], value => Assert.Equal(0xFF, value));
    }
}
