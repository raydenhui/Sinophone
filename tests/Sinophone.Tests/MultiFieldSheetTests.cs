using System.IO.Compression;
using System.Text;
using Sinophone.Data;

namespace Sinophone.Tests;

/// <summary>
/// Some sheets carry more than one text field. The artifact keeps both fields on
/// the same row, and each descriptor indexes its own field.
/// </summary>
public class MultiFieldSheetTests
{
    [Fact]
    public void Load_IndexesTwoFieldsOfTheSameSheet()
    {
        var nameDescriptor = new FakeSheetDescriptor("Item", "Name", true,
            new Dictionary<uint, string[]> { [1] = ["Potion"] });
        var descriptionDescriptor = new FakeSheetDescriptor("Item", "Description", false,
            new Dictionary<uint, string[]> { [1] = ["A restorative."] });

        const string json = """
            {"FormatVersion":1,"Language":"TC","GeneratedAt":"test","GameVersion":"test","Sheets":{"Item":{"1":{"Name":["藥水"],"Description":["恢復藥。"]}}}}
            """;

        var store = TranslationStore.Load(
            [nameDescriptor, descriptionDescriptor],
            ChineseVariant.Traditional,
            false,
            _ => ToGzipStream(json),
            new FakePluginLog());

        Assert.NotNull(store);
        Assert.True(store.TryGetName("Potion", out var name));
        Assert.Equal("藥水", name);
        Assert.True(store.TryGetDialogue("A restorative.", out var description));
        Assert.Equal("恢復藥。", description);
    }

    [Fact]
    public void Load_SkipsFieldWhenTheArtifactDoesNotHaveIt()
    {
        var nameDescriptor = new FakeSheetDescriptor("Item", "Name", true,
            new Dictionary<uint, string[]> { [1] = ["Potion"] });
        var descriptionDescriptor = new FakeSheetDescriptor("Item", "Description", false,
            new Dictionary<uint, string[]> { [1] = ["A restorative."] });

        const string json = """
            {"FormatVersion":1,"Language":"TC","GeneratedAt":"test","GameVersion":"test","Sheets":{"Item":{"1":{"Name":["藥水"]}}}}
            """;

        var store = TranslationStore.Load(
            [nameDescriptor, descriptionDescriptor],
            ChineseVariant.Traditional,
            false,
            _ => ToGzipStream(json),
            new FakePluginLog());

        Assert.NotNull(store);
        Assert.True(store.TryGetName("Potion", out _));
        Assert.False(store.TryGetDialogue("A restorative.", out _));
    }

    private static MemoryStream ToGzipStream(string json)
    {
        var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionMode.Compress, leaveOpen: true))
        using (var writer = new StreamWriter(gzip, new UTF8Encoding(false)))
        {
            writer.Write(json);
        }

        output.Position = 0;
        return output;
    }
}
