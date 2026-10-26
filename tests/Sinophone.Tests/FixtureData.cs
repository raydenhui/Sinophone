using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sinophone.Tests;

/// <summary>
/// Loads the checked-in fixture rows. Shared by the unit and integration projects
/// through a link to the same file.
/// </summary>
internal static class FixtureData
{
    private static readonly Lazy<Fixture> LazyFixture = new(Load);

    public static Fixture Current => LazyFixture.Value;

    private static Fixture Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "rows.json");
        if (!File.Exists(path))
            throw new FileNotFoundException($"Fixture not found at {path}.", path);

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<Fixture>(json, Options)
               ?? throw new InvalidOperationException("Fixture deserialized to null.");
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    internal sealed class Fixture
    {
        [JsonPropertyName("rows")]
        public List<FixtureRow> Rows { get; set; } = [];
    }

    internal sealed class FixtureRow
    {
        public string Sheet { get; set; } = string.Empty;
        public uint RowId { get; set; }
        public string English { get; set; } = string.Empty;
        public string Cn { get; set; } = string.Empty;
        public string Tc { get; set; } = string.Empty;
    }
}
