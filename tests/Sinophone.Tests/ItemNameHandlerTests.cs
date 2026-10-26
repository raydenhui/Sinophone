using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Sinophone.Data;
using Sinophone.Handlers;

namespace Sinophone.Tests;

public class ItemNameHandlerTests
{
    private const string SheetName = "Item";
    private const string FieldName = "Name";

    [Theory]
    [InlineData(4551ul, 4551u)]
    [InlineData(1_004_551ul, 4551u)]
    [InlineData(500_123ul, 123u)]
    [InlineData(0ul, 0u)]
    [InlineData(2_000_001ul, 0u)]
    public void GetBaseItemId_StripsTheOffsetTheGameAdds(ulong hoveredItem, uint expected)
        => Assert.Equal(expected, ItemNameHandler.GetBaseItemId(hoveredItem));

    [Fact]
    public void Resolve_ReturnsChineseName_ForItem()
    {
        var content = ItemNameHandler.Resolve(CreateStore(), 4551, "Fire Crystal");

        Assert.NotNull(content);
        Assert.Equal("火之碎晶", content!.Value.ChineseName);
        Assert.Equal("Fire Crystal", content.Value.EnglishName);
    }

    [Fact]
    public void Resolve_ReturnsNull_WhenStoreIsMissing()
        => Assert.Null(ItemNameHandler.Resolve(null, 4551, "Fire Crystal"));

    [Fact]
    public void Resolve_ReturnsNull_ForZeroItemId()
        => Assert.Null(ItemNameHandler.Resolve(CreateStore(), 0, "Fire Crystal"));

    [Fact]
    public void Resolve_ReturnsNull_ForEmptyName()
        => Assert.Null(ItemNameHandler.Resolve(CreateStore(), 4551, "  "));

    [Fact]
    public void Resolve_ReturnsNull_WhenNameHasNoTranslation()
        => Assert.Null(ItemNameHandler.Resolve(CreateStore(), 9999, "Unknown Item"));

    private static TranslationStore CreateStore()
    {
        var descriptor = new FakeSheetDescriptor(SheetName, FieldName, true,
            new Dictionary<uint, string[]>
            {
                [4551] = ["Fire Crystal"],
            });

        var json = Artifact((SheetName, FieldName, 4551, "火之碎晶"));

        var store = TranslationStore.Load(
            [descriptor],
            ChineseVariant.Traditional,
            false,
            _ => ToGzipStream(json),
            new FakePluginLog());

        Assert.NotNull(store);
        return store;
    }

    private static string Artifact(params (string Sheet, string Field, uint RowId, string Value)[] rows)
    {
        var sheets = rows
            .GroupBy(r => r.Sheet)
            .ToDictionary(
                g => g.Key,
                g => g.ToDictionary(
                    r => r.RowId.ToString(),
                    r => new Dictionary<string, string[]> { [r.Field] = [r.Value] }));

        return JsonSerializer.Serialize(new
        {
            FormatVersion = 1,
            Language = "TC",
            GeneratedAt = "test",
            GameVersion = "test",
            Sheets = sheets,
        });
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
