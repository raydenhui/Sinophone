using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Dalamud.Game.ClientState.Objects.Enums;
using Sinophone.Data;
using Sinophone.Handlers;

namespace Sinophone.Tests;

public class TargetNameHandlerTests
{
    private const string SheetName = "ENpcResident";
    private const string FieldName = "Singular";

    [Fact]
    public void Resolve_ReturnsChineseName_ForNpc()
    {
        var content = TargetNameHandler.Resolve(CreateStore(), ObjectKind.EventNpc, "Y'shtola");

        Assert.NotNull(content);
        Assert.Equal("雅·修特拉", content!.Value.ChineseName);
        Assert.Equal("Y'shtola", content.Value.EnglishName);
    }

    [Fact]
    public void Resolve_ReturnsChineseName_ForObject()
    {
        var content = TargetNameHandler.Resolve(CreateStore(), ObjectKind.EventObj, "Magitek Armor");

        Assert.NotNull(content);
        Assert.Equal("魔导装甲", content!.Value.ChineseName);
    }

    [Fact]
    public void Resolve_ReturnsNull_ForPlayer()
    {
        Assert.Null(TargetNameHandler.Resolve(CreateStore(), ObjectKind.Player, "Y'shtola"));
    }

    [Fact]
    public void Resolve_ReturnsNull_WhenStoreIsMissing()
    {
        Assert.Null(TargetNameHandler.Resolve(null, ObjectKind.EventNpc, "Y'shtola"));
    }

    [Fact]
    public void Resolve_ReturnsNull_ForEmptyName()
    {
        Assert.Null(TargetNameHandler.Resolve(CreateStore(), ObjectKind.EventNpc, "  "));
    }

    [Fact]
    public void Resolve_ReturnsNull_WhenNameHasNoTranslation()
    {
        Assert.Null(TargetNameHandler.Resolve(CreateStore(), ObjectKind.EventNpc, "Someone Else"));
    }

    private static TranslationStore CreateStore()
    {
        var descriptor = new FakeSheetDescriptor(SheetName, FieldName, true,
            new Dictionary<uint, string[]>
            {
                [1] = ["Y'shtola"],
                [2] = ["Magitek Armor"],
            });

        var json = Artifact(
            (SheetName, FieldName, 1, "雅·修特拉"),
            (SheetName, FieldName, 2, "魔导装甲"));

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
