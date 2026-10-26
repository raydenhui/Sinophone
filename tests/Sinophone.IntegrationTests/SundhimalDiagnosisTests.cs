using DalaMock.Core.Configuration;
using DalaMock.Core.Plugin;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;

namespace Sinophone.IntegrationTests;

/// <summary>
/// Diagnostic: search the live sheets for a line the plugin failed to translate.
/// Writes matches to %TEMP%\sinophone-sundhimal.txt. Skips when the game path is not set.
/// </summary>
public class SundhimalDiagnosisTests
{
    // Distinctive fragments from the line the addon reported. The raw sheet text may
    // split the phrase with macros, so check a few pieces.
    private static readonly string[] Fragments =
    {
        "aetherial travel",
        "queries you might have",
        "Sundhimal",
        "aetherial",
    };

    private static readonly string ReportPath =
        Path.Combine(Path.GetTempPath(), "sinophone-sundhimal.txt");

    [SkippableFact]
    public void FindSundhimalLine()
    {
        var gamePath = Environment.GetEnvironmentVariable("SINOPHONE_FFXIV_PATH");
        Skip.If(string.IsNullOrWhiteSpace(gamePath) || !Directory.Exists(gamePath), "No game path.");

        if (File.Exists(ReportPath))
            File.Delete(ReportPath);

        var container = new MockContainer(
            new MockDalamudConfiguration
            {
                CreateWindow = false,
                GamePath = new DirectoryInfo(gamePath),
            },
            askPath: false);

        var gameData = container.GetGameData();

        SearchDefaultTalk(gameData);
        SearchSheet<Balloon>(gameData, "Balloon", row => row.Dialogue);
        SearchSheet<NpcYell>(gameData, "NpcYell", row => row.Text);
        SearchSheet<InstanceContentTextData>(gameData, "InstanceContentTextData", row => row.Text);
        SearchSheet<ContentTalk>(gameData, "ContentTalk", row => row.Text);
        SearchSheet<SnipeTalk>(gameData, "SnipeTalk", row => row.Text);
        SearchCollection<GuildleveAssignmentTalk>(gameData, "GuildleveAssignmentTalk", row => row.Talk);
        SearchCollection<CraftLeveTalk>(gameData, "CraftLeveTalk", row => row.Talk);
        SearchSheet<GoldSaucerTextData>(gameData, "GoldSaucerTextData", row => row.Text);
        SearchSheet<CSBonusTextData>(gameData, "CSBonusTextData", row => row.Text);
        SearchSheet<RideShootingTextData>(gameData, "RideShootingTextData", row => row.String);
        SearchSheet<QuickChatTransient>(gameData, "QuickChatTransient", row => row.TextOutput);
        SearchSheet<PublicContentTextData>(gameData, "PublicContentTextData", row => row.TextData);
        SearchSheet<PartyContentTextData>(gameData, "PartyContentTextData", row => row.Data);
        SearchSheet<ENpcResident>(gameData, "ENpcResident", row => row.Singular);
        SearchCustomTalk(gameData);
    }

    // CustomTalk is not in the plugin's sheet set. Search every text-typed field so we
    // can tell whether the line lives in a sheet we do not index.
    private static void SearchCustomTalk(Lumina.GameData gameData)
    {
        var sheet = gameData.GetExcelSheet<CustomTalk>();
        if (sheet is null)
            return;

        var properties = typeof(CustomTalk).GetProperties()
            .Where(p => p.PropertyType == typeof(string)
                        || p.PropertyType == typeof(ReadOnlySeString)
                        || p.PropertyType == typeof(Lumina.Excel.Collection<ReadOnlySeString>))
            .ToArray();

        foreach (var row in sheet)
        {
            foreach (var property in properties)
            {
                var raw = property.GetValue(row);
                var texts = raw switch
                {
                    string s => new[] { s },
                    ReadOnlySeString s => new[] { s.ExtractText() },
                    Lumina.Excel.Collection<ReadOnlySeString> c =>
                        Enumerable.Range(0, c.Count).Select(i => c[i].ExtractText()).ToArray(),
                    _ => Array.Empty<string>(),
                };

                for (var slot = 0; slot < texts.Length; slot++)
                {
                    var text = texts[slot];
                    if (string.IsNullOrEmpty(text))
                        continue;

                    foreach (var fragment in Fragments)
                    {
                        if (text.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                        {
                            File.AppendAllText(
                                ReportPath,
                                $"FOUND [{fragment}] CustomTalk row {row.RowId} field {property.Name} slot {slot}: {text}{Environment.NewLine}");
                            break;
                        }
                    }
                }
            }
        }
    }

    private static void SearchDefaultTalk(Lumina.GameData gameData)
    {
        var sheet = gameData.GetExcelSheet<DefaultTalk>();
        if (sheet is null)
            return;

        foreach (var row in sheet)
        {
            for (var i = 0; i < row.Text.Count; i++)
            {
                Report("DefaultTalk", row.RowId, i, row.Text[i]);
            }
        }
    }

    private static void SearchSheet<T>(Lumina.GameData gameData, string name, Func<T, ReadOnlySeString> extract)
        where T : struct, Lumina.Excel.IExcelRow<T>
    {
        var sheet = gameData.GetExcelSheet<T>();
        if (sheet is null)
            return;

        foreach (var row in sheet)
            Report(name, row.RowId, 0, extract(row));
    }

    private static void SearchCollection<T>(Lumina.GameData gameData, string name, Func<T, Lumina.Excel.Collection<ReadOnlySeString>> extract)
        where T : struct, Lumina.Excel.IExcelRow<T>
    {
        var sheet = gameData.GetExcelSheet<T>();
        if (sheet is null)
            return;

        foreach (var row in sheet)
        {
            var slots = extract(row);
            for (var i = 0; i < slots.Count; i++)
                Report(name, row.RowId, i, slots[i]);
        }
    }

    private static void Report(string sheet, uint rowId, int slot, ReadOnlySeString value)
    {
        var text = value.ExtractText();
        foreach (var fragment in Fragments)
        {
            if (text.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                File.AppendAllText(
                    ReportPath,
                    $"FOUND [{fragment}] {sheet} row {rowId} slot {slot}: {text}{Environment.NewLine}");
                return;
            }
        }
    }
}
