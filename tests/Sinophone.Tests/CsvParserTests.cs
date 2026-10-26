using Sinophone.DataPrep;

namespace Sinophone.Tests;

public class CsvParserTests
{
    [Fact]
    public void Parse_SplitsCommaSeparatedFields()
    {
        var records = Csv.Parse("a,b,c\n1,2,3\n");

        Assert.Equal(2, records.Count);
        Assert.Equal(["a", "b", "c"], records[0]);
        Assert.Equal(["1", "2", "3"], records[1]);
    }

    [Fact]
    public void Parse_KeepsCommasInsideQuotes()
    {
        var records = Csv.Parse("\"a,b\",c\n");

        Assert.Single(records);
        Assert.Equal(["a,b", "c"], records[0]);
    }

    [Fact]
    public void Parse_KeepsLineBreaksInsideQuotes()
    {
        var records = Csv.Parse("\"line one\r\nline two\",b\n");

        Assert.Single(records);
        Assert.Equal("line one\r\nline two", records[0][0]);
        Assert.Equal("b", records[0][1]);
    }

    [Fact]
    public void Parse_UnescapesDoubledQuotes()
    {
        var records = Csv.Parse("\"say \"\"hello\"\"\",b\n");

        Assert.Single(records);
        Assert.Equal("say \"hello\"", records[0][0]);
    }

    [Fact]
    public void Parse_HandlesCrLfAndLf()
    {
        var records = Csv.Parse("a,b\r\nc,d\ne,f");

        Assert.Equal(3, records.Count);
        Assert.Equal("e", records[2][0]);
    }

    [Fact]
    public void Parse_PreservesEmptyFields()
    {
        var records = Csv.Parse("a,,c\n");

        Assert.Single(records);
        Assert.Equal(["a", "", "c"], records[0]);
    }

    [Fact]
    public void Parse_SkipsBlankLines()
    {
        var records = Csv.Parse("a,b\n\n\nc,d\n");

        Assert.Equal(2, records.Count);
    }

    [Fact]
    public void Parse_ReturnsNoRecords_ForEmptyInput()
        => Assert.Empty(Csv.Parse(string.Empty));

    [Fact]
    public void Parse_HandlesLastRowWithoutTrailingNewline()
    {
        var records = Csv.Parse("a,b\nc,d");

        Assert.Equal(2, records.Count);
        Assert.Equal(["c", "d"], records[1]);
    }
}
