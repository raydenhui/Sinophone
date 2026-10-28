using System.Text;
using Sinophone.DataPrep;

namespace Sinophone.Tests;

/// <summary>
/// The script dumps do not look like the top-level sheets. The CN and TC files have
/// three header rows, the English file has none, and the text key sits in column 1.
/// </summary>
public sealed class ScriptDumpTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "sinophone-scriptdump-" + Guid.NewGuid().ToString("N"));

    public ScriptDumpTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch (IOException)
        {
            // A leftover temp folder is harmless.
        }
    }

    [Fact]
    public void LoadFile_ReadsEnglishDump_WithoutHeaderRows()
    {
        var path = Write("custom/000/Test_00001.csv",
            "0,TEXT_TEST_00001_HELLO,\"Hello, world.\"\n1,TEXT_TEST_00001_BYE,Goodbye.\n");

        var entries = ScriptDump.LoadFile(path);

        Assert.Equal("Hello, world.", entries["TEXT_TEST_00001_HELLO"]);
        Assert.Equal("Goodbye.", entries["TEXT_TEST_00001_BYE"]);
    }

    [Fact]
    public void LoadFile_SkipsLocalizedHeaderRows()
    {
        var path = Write("custom/000/Test_00001.csv",
            "﻿key,0,1\n#,, \nint32,str,str\n0,\"TEXT_TEST_00001_HELLO\",\"你好。\"\n");

        var entries = ScriptDump.LoadFile(path);

        Assert.Single(entries);
        Assert.Equal("你好。", entries["TEXT_TEST_00001_HELLO"]);
    }

    [Fact]
    public void LoadFile_KeepsLineBreaks_InsideQuotedText()
    {
        var path = Write("custom/000/Test_00001.csv",
            "0,TEXT_TEST_00001_HELLO,\"line one\r\nline two\"\n");

        var entries = ScriptDump.LoadFile(path);

        Assert.Equal("line one\r\nline two", entries["TEXT_TEST_00001_HELLO"]);
    }

    [Fact]
    public void Build_PairsByTextKey_AndSkipsLinesWithoutEnglish()
    {
        Write("cn/custom/000/Test_00001.csv",
            "key,0,1\n#,, \nint32,str,str\n" +
            "0,\"TEXT_A\",\"甲\"\n1,\"TEXT_B\",\"乙\"\n2,\"TEXT_C\",\"丙\"\n");
        Write("en/custom/000/Test_00001.csv",
            "0,TEXT_A,Alpha\n1,TEXT_B,\n2,TEXT_C,Charlie\n");

        var scripts = ScriptDump.Build(Path.Combine(_root, "cn"), Path.Combine(_root, "en"));

        Assert.Equal(2, scripts.Count);
        Assert.Equal("Alpha", scripts["TEXT_A"].English);
        Assert.Equal("甲", scripts["TEXT_A"].Text);
        Assert.Equal("Charlie", scripts["TEXT_C"].English);
        Assert.False(scripts.ContainsKey("TEXT_B"));
    }

    [Fact]
    public void TakePaired_StopsAfterTheRequestedCount()
    {
        Write("cn/custom/000/Test_00001.csv",
            "key,0,1\n#,, \nint32,str,str\n" +
            "0,\"TEXT_A\",\"甲\"\n1,\"TEXT_B\",\"乙\"\n2,\"TEXT_C\",\"丙\"\n");
        Write("en/custom/000/Test_00001.csv",
            "0,TEXT_A,Alpha\n1,TEXT_B,Bravo\n2,TEXT_C,Charlie\n");

        var scripts = ScriptDump.TakePaired(
            Path.Combine(_root, "cn"),
            Path.Combine(_root, "en"),
            "custom",
            2);

        Assert.Equal(2, scripts.Count);
        Assert.Equal(new[] { "TEXT_A", "TEXT_B" }, scripts.Select(entry => entry.Key));
    }

    private string Write(string relativePath, string content)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }
}
