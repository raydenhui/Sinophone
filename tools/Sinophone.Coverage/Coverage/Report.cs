using System.Text;
using System.Text.Json;

namespace Sinophone.Coverage;

/// <summary>Writes the per-component coverage report and the JSON the gate compares.</summary>
internal static class Report
{
    public static void Write(
        string outputDirectory,
        string variant,
        string gameVersion,
        Dictionary<string, Tally> results)
    {
        Directory.CreateDirectory(outputDirectory);

        var components = results.Keys.OrderBy(name => name, StringComparer.Ordinal).ToList();
        var total = results.Values.Sum(tally => tally.Total);
        var resolved = results.Values.Sum(tally => tally.Resolved);

        var markdown = new StringBuilder();
        markdown.AppendLine("# Translation coverage");
        markdown.AppendLine();
        markdown.AppendLine($"Variant **{variant}**, artifact game version **{gameVersion}**, generated {DateTime.Now:yyyy-MM-dd HH:mm}.");
        markdown.AppendLine();
        markdown.AppendLine("Each case is one English string a component can show, resolved through that component's own");
        markdown.AppendLine("`Resolve`. Rows with a value the game fills at runtime are counted as cases with game values");
        markdown.AppendLine("substituted. The display half (nodes, overlay, fonts) is not measured.");
        markdown.AppendLine();
        markdown.AppendLine("`Text` is the lookup as a miss sees it, with the line only. `Id` adds the row id the game");
        markdown.AppendLine("sometimes passes, which is what separates two lines that share one English string.");
        markdown.AppendLine();
        markdown.AppendLine("Labels the game builds itself (`8:00 a.m.`, `Lv. UNKNOWN`) are counted apart as");
        markdown.AppendLine("**Skipped**, because no dictionary can key them.");
        markdown.AppendLine();
        markdown.AppendLine("| Component | Cases | Text | Text coverage | Id | Id coverage | Skipped | Top reason for a miss |");
        markdown.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |");

        foreach (var name in components)
        {
            var tally = results[name];
            var coverage = tally.Total == 0 ? 0 : (double)tally.Resolved / tally.Total;
            var topReason = tally.Reasons.OrderByDescending(entry => entry.Value).FirstOrDefault();
            var reason = topReason.Value > 0
                ? $"{topReason.Key} {topReason.Value:N0}"
                : tally.NoChinese > 0 ? $"no Chinese {tally.NoChinese:N0}" : "-";

            var byId = tally.Total == 0 ? 0 : (double)tally.ResolvedById / tally.Total;
            markdown.AppendLine(
                $"| {name} | {tally.Total:N0} | {tally.Resolved:N0} | {coverage:P1} | {tally.ResolvedById:N0} | " +
                $"{byId:P1} | {tally.Skipped:N0} | {reason} |");
        }

        var byIdTotal = results.Values.Sum(tally => tally.ResolvedById);
        markdown.AppendLine(
            $"| **all** | **{total:N0}** | **{resolved:N0}** | **{(total == 0 ? 0 : (double)resolved / total):P1}** | " +
            $"**{byIdTotal:N0}** | **{(total == 0 ? 0 : (double)byIdTotal / total):P1}** | " +
            $"**{results.Values.Sum(tally => tally.Skipped):N0}** | |");
        markdown.AppendLine();

        markdown.AppendLine("## Why a shared line was not answered");
        markdown.AppendLine();
        markdown.AppendLine("A script line that several dump entries share is answered only when the game names the");
        markdown.AppendLine("speaker or the cutscene. This is what the misses are made of.");
        markdown.AppendLine();
        markdown.AppendLine("| Component | Reason | Cases |");
        markdown.AppendLine("| --- | --- | ---: |");

        foreach (var name in components)
        {
            foreach (var (reason, count) in results[name].Reasons.OrderByDescending(entry => entry.Value))
                markdown.AppendLine($"| {name} | {reason} | {count:N0} |");
        }

        markdown.AppendLine();
        markdown.AppendLine("## By dynamic value");
        markdown.AppendLine();
        markdown.AppendLine("| Component | Value | Cases | Translated | Coverage |");
        markdown.AppendLine("| --- | --- | ---: | ---: | ---: |");

        foreach (var name in components)
        {
            var tally = results[name];
            foreach (var (tag, count) in tally.ByTag.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            {
                var hit = tally.ResolvedByTag.GetValueOrDefault(tag);
                markdown.AppendLine($"| {name} | {tag} | {count:N0} | {hit:N0} | {(count == 0 ? 0 : (double)hit / count):P1} |");
            }
        }

        markdown.AppendLine();

        foreach (var name in components)
        {
            var tally = results[name];
            if (tally.Misses.Count == 0)
                continue;

            markdown.AppendLine($"## {name}: misses");
            markdown.AppendLine();

            foreach (var miss in tally.Misses.Take(20))
            {
                var english = OneLine(miss.English);
                markdown.AppendLine($"- `{miss.Outcome}` `{miss.Tags}` {miss.Source}: {english}");
            }

            if (tally.Misses.Count > 20)
                markdown.AppendLine($"- … and {tally.Misses.Count - 20} more (the JSON holds up to 200 per component)");

            markdown.AppendLine();
        }

        markdown.AppendLine("## Runtime-built text");
        markdown.AppendLine();
        markdown.AppendLine("Not measured yet. Text the game assembles at runtime (\"You obtained 3 × …\") has no source");
        markdown.AppendLine("row to enumerate, so it needs its own cases captured in game. The hook for them is");
        markdown.AppendLine("`Runner.RuntimeCases`, which is empty today.");
        markdown.AppendLine();

        File.WriteAllText(Path.Combine(outputDirectory, "coverage.md"), markdown.ToString());

        var json = JsonSerializer.Serialize(
            new
            {
                Variant = variant,
                GameVersion = gameVersion,
                GeneratedAt = DateTimeOffset.Now,
                Components = results.ToDictionary(
                    entry => entry.Key,
                    entry => new
                    {
                        entry.Value.Total,
                        entry.Value.Resolved,
                        entry.Value.ResolvedById,
                        entry.Value.NoChinese,
                        ByTag = entry.Value.ByTag,
                        ResolvedByTag = entry.Value.ResolvedByTag,
                        Misses = entry.Value.Misses.Select(miss => new
                        {
                            Miss = miss.Outcome.ToString(),
                            miss.Source,
                            miss.Tags,
                            miss.Reason,
                            miss.English,
                        }),
                    }),
            },
            new JsonSerializerOptions { WriteIndented = true });

        File.WriteAllText(Path.Combine(outputDirectory, "coverage.json"), json);
    }

    /// <summary>
    /// Writes the totals only, which is small enough to keep in the repository and is what the gate
    /// compares. Refresh it with the same command plus <c>--baseline</c>.
    /// </summary>
    public static void WriteBaseline(string path, string variant, Dictionary<string, Tally> results)
    {
        var json = JsonSerializer.Serialize(
            new
            {
                Variant = variant,
                GeneratedAt = DateTimeOffset.Now,
                Components = results.ToDictionary(
                    entry => entry.Key,
                    entry => new
                    {
                        entry.Value.Total,
                        entry.Value.Resolved,
                        entry.Value.ResolvedById,
                        entry.Value.Skipped,
                    }),
            },
            new JsonSerializerOptions { WriteIndented = true });

        File.WriteAllText(path, json);
    }

    /// <summary>Fails when a component translates fewer cases than the baseline did.</summary>
    public static int Gate(string baselinePath, Dictionary<string, Tally> results)
    {
        using var baseline = JsonDocument.Parse(File.ReadAllText(baselinePath));
        if (!baseline.RootElement.TryGetProperty("Components", out var components))
            throw new InvalidDataException($"{baselinePath} has no Components.");

        var failures = 0;

        foreach (var entry in components.EnumerateObject())
        {
            if (!results.TryGetValue(entry.Name, out var tally))
            {
                Console.Error.WriteLine($"GATE {entry.Name}: component is missing from the run.");
                failures++;
                continue;
            }

            var before = entry.Value.GetProperty("Resolved").GetInt32();
            var beforeById = entry.Value.TryGetProperty("ResolvedById", out var byId) ? byId.GetInt32() : before;

            if (tally.Resolved < before)
            {
                Console.Error.WriteLine($"GATE {entry.Name}: text {tally.Resolved:N0}, baseline {before:N0}.");
                failures++;
            }

            if (tally.ResolvedById < beforeById)
            {
                Console.Error.WriteLine($"GATE {entry.Name}: id {tally.ResolvedById:N0}, baseline {beforeById:N0}.");
                failures++;
            }
        }

        return failures == 0 ? 0 : 1;
    }

    private static string OneLine(string text)
        => text.Replace("\r", " ").Replace("\n", " ").Trim();
}
