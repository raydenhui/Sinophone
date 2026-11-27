using System.IO.Compression;
using System.Text;
using System.Text.Json;

using Sinophone.Fonts;

namespace Sinophone.Tests;

/// <summary>
/// Reading a font pack: the required file list, the TexTools manifest, and the two ways a
/// user can supply the files. None of this needs the game.
/// </summary>
public class FontPackTests
{
    [Fact]
    public void Required_HasTheWholeInGameFontSet()
    {
        Assert.Equal(25, FontPackFiles.Required.Count);
        Assert.Equal(
            FontPackFiles.Required.Count,
            FontPackFiles.Required.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(FontPackFiles.Required, static path => Assert.StartsWith("common/font/", path));
        Assert.Contains("common/font/axis_12.fdt", FontPackFiles.Required);
        Assert.Contains("common/font/font1.tex", FontPackFiles.Required);
        Assert.Contains("common/font/font2.tex", FontPackFiles.Required);
    }

    [Fact]
    public void Normalize_LowercasesAndUsesForwardSlashes()
        => Assert.Equal("common/font/axis_12.fdt", FontPackFiles.Normalize(@"\Common\Font\AXIS_12.fdt "));

    [Fact]
    public void IsRequired_IgnoresCaseAndSeparators()
        => Assert.True(FontPackFiles.IsRequired(@"COMMON\FONT\Font1.TEX"));

    [Fact]
    public void IsRequired_IsFalseForAnotherFile()
        => Assert.False(FontPackFiles.IsRequired("common/font/font_lobby1.tex"));

    [Fact]
    public void NameOf_ReturnsTheFileName()
        => Assert.Equal("axis_12.fdt", FontPackFiles.NameOf("common/font/axis_12.fdt"));

    [Fact]
    public void FitsDescriptor_StopsAtTheGameBuffer()
    {
        Assert.True(FontPackFiles.FitsDescriptor(new string('a', 259)));
        Assert.False(FontPackFiles.FitsDescriptor(new string('a', 260)));
    }

    [Fact]
    public void TryParseManifest_KeepsTheFontEntries()
    {
        var json = Manifest([("common/font/axis_12.fdt", 10, 20), ("common/font/font_lobby1.tex", 30, 40)]);

        Assert.True(FontPackExtractor.TryParseManifest(ToStream(json), out var entries, out var error));
        Assert.Empty(error);
        Assert.Single(entries);
        Assert.Equal(new Ttmp2Entry(10, 20), entries["common/font/axis_12.fdt"]);
    }

    [Fact]
    public void TryParseManifest_IgnoresCaseAndBackslashes()
    {
        var json = Manifest([(@"Common\Font\AXIS_14.FDT", 1, 2)]);

        Assert.True(FontPackExtractor.TryParseManifest(ToStream(json), out var entries, out _));
        Assert.Equal(new Ttmp2Entry(1, 2), entries["common/font/axis_14.fdt"]);
    }

    [Fact]
    public void TryParseManifest_ReadsAUtf8Bom()
    {
        var bytes = Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(Manifest([("common/font/font1.tex", 0, 5)])))
            .ToArray();

        Assert.True(FontPackExtractor.TryParseManifest(new MemoryStream(bytes), out var entries, out _));
        Assert.Single(entries);
    }

    [Fact]
    public void TryParseManifest_FailsWithoutAList()
    {
        Assert.False(FontPackExtractor.TryParseManifest(ToStream("{}"), out _, out var error));
        Assert.Contains("lists no files", error);
    }

    [Fact]
    public void TryParseManifest_FailsOnBrokenJson()
    {
        Assert.False(FontPackExtractor.TryParseManifest(ToStream("{"), out _, out var error));
        Assert.Contains("not valid JSON", error);
    }

    [Fact]
    public void TryParseManifest_FailsOnAnEntryWithoutASize()
    {
        var json = """{"SimpleModsList":[{"FullPath":"common/font/font1.tex","ModOffset":0}]}""";

        Assert.False(FontPackExtractor.TryParseManifest(ToStream(json), out _, out var error));
        Assert.Contains("without a path, offset or size", error);
    }

    [Fact]
    public void Extract_ReadsEveryFileOutOfATtmp2()
    {
        var source = TempDirectory();
        var destination = TempDirectory();
        try
        {
            var files = ToyFiles();
            var pack = Path.Combine(source, "pack.ttmp2");
            WritePack(pack, files);

            var result = FontPackExtractor.Extract(pack, destination);

            Assert.True(result.Success, result.Message);
            Assert.Equal(FontPackFiles.Required.Count, result.LocalPaths.Count);
            foreach (var (gamePath, bytes) in files)
            {
                var local = result.LocalPaths[gamePath];
                Assert.Equal(FontPackFiles.NameOf(gamePath), Path.GetFileName(local));
                Assert.Equal(bytes, File.ReadAllBytes(local));
            }
        }
        finally
        {
            Delete(source);
            Delete(destination);
        }
    }

    [Fact]
    public void Extract_NamesTheFilesAPackIsMissing()
    {
        var source = TempDirectory();
        var destination = TempDirectory();
        try
        {
            var files = ToyFiles().Where(pair => pair.Path != "common/font/axis_18.fdt").ToArray();
            var pack = Path.Combine(source, "pack.ttmp2");
            WritePack(pack, files);

            var result = FontPackExtractor.Extract(pack, destination);

            Assert.False(result.Success);
            Assert.Contains("axis_18.fdt", result.Message);
            Assert.Contains("missing 1", result.Message);
            Assert.DoesNotContain("Chinese client", result.Message);
        }
        finally
        {
            Delete(source);
            Delete(destination);
        }
    }

    [Fact]
    public void Extract_ExplainsAChineseClientPack()
    {
        var source = TempDirectory();
        var destination = TempDirectory();
        try
        {
            var pack = Path.Combine(source, "pack.ttmp2");
            WritePack(
                pack,
                [],
                Manifest([
                    ("common/font/chnaxis_120.fdt", 0, 10),
                    ("common/font/font_chn_1.tex", 10, 10),
                ]));

            var result = FontPackExtractor.Extract(pack, destination);

            Assert.False(result.Success);
            Assert.Contains("Chinese client", result.Message);
            Assert.Contains("(Global)", result.Message);
        }
        finally
        {
            Delete(source);
            Delete(destination);
        }
    }

    [Fact]
    public void Extract_ExplainsAChineseClientFontFolder()
    {
        var source = TempDirectory();
        var destination = TempDirectory();
        try
        {
            File.WriteAllBytes(Path.Combine(source, "chnaxis_120.fdt"), [1, 2, 3]);
            File.WriteAllBytes(Path.Combine(source, "font_chn_1.tex"), [4, 5, 6]);

            var result = FontPackExtractor.Extract(source, destination);

            Assert.False(result.Success);
            Assert.Contains("Chinese client", result.Message);
            Assert.Contains("(Global)", result.Message);
        }
        finally
        {
            Delete(source);
            Delete(destination);
        }
    }

    [Fact]
    public void Extract_FailsWhenAnEntryPointsPastTheData()
    {
        var source = TempDirectory();
        var destination = TempDirectory();
        try
        {
            var pack = Path.Combine(source, "pack.ttmp2");
            WritePack(
                pack,
                [],
                Manifest(FontPackFiles.Required.Select(static path => (path, 1000L, 10L))));

            var result = FontPackExtractor.Extract(pack, destination);

            Assert.False(result.Success);
            Assert.Contains("outside its data file", result.Message);
        }
        finally
        {
            Delete(source);
            Delete(destination);
        }
    }

    [Fact]
    public void Extract_FailsWithoutAManifest()
    {
        var source = TempDirectory();
        var destination = TempDirectory();
        try
        {
            var pack = Path.Combine(source, "pack.ttmp2");
            WritePack(pack, ToyFiles(), writeManifest: false);

            var result = FontPackExtractor.Extract(pack, destination);

            Assert.False(result.Success);
            Assert.Contains("manifest", result.Message);
        }
        finally
        {
            Delete(source);
            Delete(destination);
        }
    }

    [Fact]
    public void Extract_ReadsAFolderOfFontFiles()
    {
        var source = TempDirectory();
        var destination = TempDirectory();
        try
        {
            var files = ToyFiles();
            foreach (var (gamePath, bytes) in files)
            {
                var path = Path.Combine(source, gamePath);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, bytes);
            }

            var result = FontPackExtractor.Extract(source, destination);

            Assert.True(result.Success, result.Message);
            Assert.Equal(files.Length, result.LocalPaths.Count);
            foreach (var (gamePath, bytes) in files)
                Assert.Equal(bytes, File.ReadAllBytes(result.LocalPaths[gamePath]));
        }
        finally
        {
            Delete(source);
            Delete(destination);
        }
    }

    [Fact]
    public void Extract_FailsForAMissingPath()
    {
        var destination = TempDirectory();
        try
        {
            var result = FontPackExtractor.Extract(
                Path.Combine(Path.GetTempPath(), "sinophone-font-does-not-exist"),
                destination);

            Assert.False(result.Success);
            Assert.Contains("does not exist", result.Message);
        }
        finally
        {
            Delete(destination);
        }
    }

    private static (string Path, byte[] Bytes)[] ToyFiles()
    {
        var files = new (string, byte[])[FontPackFiles.Required.Count];
        for (var index = 0; index < files.Length; index++)
        {
            files[index] = (
                FontPackFiles.Required[index],
                [(byte)index, 0x5A, (byte)(index * 3), 0xC3]);
        }

        return files;
    }

    private static string Manifest(IEnumerable<(string Path, long Offset, long Size)> entries)
        => JsonSerializer.Serialize(new
        {
            SimpleModsList = entries.Select(static entry => new
            {
                Category = "Raw File Imports",
                FullPath = entry.Path,
                ModOffset = entry.Offset,
                ModSize = entry.Size,
                DatFile = "000000",
            }).ToArray(),
        });

    private static void WritePack(
        string path,
        IReadOnlyList<(string Path, byte[] Bytes)> files,
        string? manifest = null,
        bool writeManifest = true)
    {
        using var data = new MemoryStream();
        var entries = new List<(string Path, long Offset, long Size)>();
        foreach (var (filePath, bytes) in files)
        {
            entries.Add((filePath, data.Position, bytes.Length));
            data.Write(bytes);
        }

        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        if (writeManifest)
        {
            manifest ??= Manifest(entries);
            using var manifestStream = archive.CreateEntry("TTMPL.mpl").Open();
            using var writer = new StreamWriter(manifestStream, Encoding.UTF8);
            writer.Write(manifest);
        }

        using var dataStream = archive.CreateEntry("TTMPD.mpd").Open();
        data.Position = 0;
        data.CopyTo(dataStream);
    }

    private static Stream ToStream(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));

    private static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "sinophone-font-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Delete(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}
