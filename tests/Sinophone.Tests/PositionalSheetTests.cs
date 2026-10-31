using System.Text;
using Sinophone.DataPrep;

namespace Sinophone.Tests;

/// <summary>
/// Sheets whose CN/TC dumps name no columns are read by position. These tests
/// cover the three positional selectors and the named-column path they replace.
/// </summary>
public class PositionalSheetTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sinophone-positional-" + Guid.NewGuid().ToString("N"));

    public PositionalSheetTests()
        => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void ProcessSheet_ReadsFixedIndex_WhenSpecIsPositional()
    {
        Write("GimmickBill", "key,0", "#,", "int32,str", "1,\"Sign text\"", "2,");

        var spec = new Program.SheetSpec("GimmickBill", "Text", []) { Indices = [1] };
        var rows = Program.ProcessSheet(spec, _directory);

        Assert.NotNull(rows);
        Assert.True(rows!.TryGetValue("1", out var fields));
        Assert.Equal(["Sign text"], fields["Text"]);

        // Row 2 has no text, so it is not indexed.
        Assert.False(rows.ContainsKey("2"));
    }

    [Fact]
    public void ProcessSheet_ReadsNpcYellTextAtItsActualColumn_WhenHeaderIsShifted()
    {
        Write("NpcYell", "key,0,1,2,3,4,5,6,7,8,9,10,11,12,13,14",
            "#,,,,,OutputType,,BalloonTime,IsBalloonSlow,BattleTalkTime,,,Text,,,",
            "int32,uint32,bit&01,bit&02,bit&04,byte,byte,uint32,single,single,bit&08,bit&10,byte,byte,str,uint16",
            "11203,0,False,False,False,3,0,0,0,4,False,False,1,25,\"刚才这些就是\n最后的了吧？\",0");

        var spec = new Program.SheetSpec("NpcYell", "Text", []) { Indices = [14] };
        var rows = Program.ProcessSheet(spec, _directory);

        Assert.NotNull(rows);
        Assert.True(rows!.TryGetValue("11203", out var fields));
        Assert.Equal(["刚才这些就是\n最后的了吧？"], fields["Text"]);
    }

    [Fact]
    public void ProcessSheet_ReadsLastColumn_WhenSpecUsesLastColumn()
    {
        Write("GimmickTalk", "key,0,1,2", "#,,,", "int32,byte,bit&01,str", "1,7,False,\"Object text\"");

        var spec = new Program.SheetSpec("GimmickTalk", "Message", []) { UseLastColumn = true };
        var rows = Program.ProcessSheet(spec, _directory);

        Assert.NotNull(rows);
        Assert.True(rows!.TryGetValue("1", out var fields));
        Assert.Equal(["Object text"], fields["Message"]);
    }

    [Fact]
    public void ProcessSheet_ReadsRangeToEnd_WhenSpecUsesRangeStart()
    {
        Write("GoldSaucerTalk", "key,0,1,2,3", "#,,,,", "int32,str,str,str,str",
            "1,\"Message\",\"Choice A\",\"Choice B\",\"Choice C\"");

        var spec = new Program.SheetSpec("GoldSaucerTalk", "ChoicesText", []) { RangeStart = 2 };
        var rows = Program.ProcessSheet(spec, _directory);

        Assert.NotNull(rows);
        Assert.True(rows!.TryGetValue("1", out var fields));
        Assert.Equal(["Choice A", "Choice B", "Choice C"], fields["ChoicesText"]);
    }

    [Fact]
    public void ProcessSheet_ReadsNamedColumn_WhenSpecUsesHeaderNames()
    {
        Write("DefaultTalk", "key,Text[0]", "#,Text[0]", "int32,str", "1,\"Hello there.\"");

        var spec = new Program.SheetSpec("DefaultTalk", "Text", ["Text[0]"]);
        var rows = Program.ProcessSheet(spec, _directory);

        Assert.NotNull(rows);
        Assert.True(rows!.TryGetValue("1", out var fields));
        Assert.Equal(["Hello there."], fields["Text"]);
    }

    [Fact]
    public void ProcessSheet_ReturnsNull_WhenNamedSpecMatchesNoColumns()
    {
        Write("DefaultTalk", "key,Text[0]", "#,Text[0]", "int32,str", "1,\"Hello there.\"");

        var spec = new Program.SheetSpec("DefaultTalk", "Text", ["Missing"]);
        var rows = Program.ProcessSheet(spec, _directory);

        Assert.Null(rows);
    }

    [Fact]
    public void ProcessSheet_SkipsRows_WhenPositionalTextIsEmpty()
    {
        Write("WKSText", "key,0", "#,", "int32,str", "0,", "1,");

        var spec = new Program.SheetSpec("WKSText", "Text", []) { Indices = [1] };
        var rows = Program.ProcessSheet(spec, _directory);

        Assert.NotNull(rows);
        Assert.Empty(rows!);
    }

    private void Write(string sheet, params string[] lines)
        => File.WriteAllText(
            Path.Combine(_directory, sheet + ".csv"),
            string.Join("\r\n", lines) + "\r\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
}
