using System.Globalization;
using System.Text.Json;

namespace Sinophone.Fonts;

/// <summary>One file inside a <c>.ttmp2</c> pack: a byte range in its data blob.</summary>
public readonly record struct Ttmp2Entry(long Offset, long Size);

/// <summary>
/// Copies the font files out of a user-supplied pack into the plugin folder. The pack itself is
/// only read. The plugin ships no font data.
/// </summary>
public static class FontPackExtractor
{
    internal const string ManifestEntryName = "TTMPL.mpl";
    internal const string DataEntryName = "TTMPD.mpd";

    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    /// <summary>
    /// Copies every required font file from <paramref name="sourcePath"/> into
    /// <paramref name="destinationDirectory"/>. The source is a <c>.ttmp2</c> file or a folder
    /// that holds the files, such as an unpacked mod.
    /// </summary>
    public static FontPackResult Extract(string sourcePath, string destinationDirectory)
    {
        try
        {
            if (Directory.Exists(sourcePath))
                return ExtractFolder(sourcePath, destinationDirectory);

            if (File.Exists(sourcePath))
                return ExtractTtmp2(sourcePath, destinationDirectory);

            return FontPackResult.Failure($"The font pack path does not exist: {sourcePath}");
        }
        catch (Exception ex)
        {
            return FontPackResult.Failure($"Could not read the font pack: {ex.Message}");
        }
    }

    private static FontPackResult ExtractTtmp2(string packPath, string destinationDirectory)
    {
        using var archive = ZipFile.OpenRead(packPath);
        var manifest = FindEntry(archive, ManifestEntryName);
        if (manifest is null)
            return FontPackResult.Failure($"The font pack has no {ManifestEntryName} manifest.");

        Dictionary<string, Ttmp2Entry> entries;
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var stream = manifest.Open())
        {
            if (!TryParseManifest(stream, out entries, out var error, seenPaths))
                return FontPackResult.Failure(error);
        }

        var missing = MissingFiles(entries.Keys);
        if (missing.Count > 0)
        {
            var hint = UnsupportedPackHint(seenPaths);
            return hint is null ? MissingResult(missing) : FontPackResult.Failure(hint);
        }

        var data = FindEntry(archive, DataEntryName);
        if (data is null)
            return FontPackResult.Failure($"The font pack has no {DataEntryName} data file.");

        Directory.CreateDirectory(destinationDirectory);
        var localPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var dataStream = data.Open();

        // A zip entry stream is forward-only, and the entries are byte ranges at arbitrary
        // offsets. Copy a non-seekable data file into memory so the ranges can be read in any
        // order. A stored entry of a real pack is about 25 MB.
        using var buffer = dataStream.CanSeek ? null : CopyToMemory(dataStream);
        Stream dataSource = buffer ?? dataStream;

        foreach (var gamePath in FontPackFiles.Required)
        {
            var entry = entries[gamePath];
            if (entry.Offset < 0 || entry.Size <= 0 || entry.Offset + entry.Size > dataSource.Length)
            {
                return FontPackResult.Failure(
                    $"The font pack entry for {gamePath} points outside its data file.");
            }

            var target = Path.Combine(destinationDirectory, FontPackFiles.NameOf(gamePath));
            CopyRange(dataSource, entry.Offset, entry.Size, target);
            localPaths[gamePath] = Path.GetFullPath(target);
        }

        return Validate(localPaths);
    }

    private static FontPackResult ExtractFolder(string folder, string destinationDirectory)
    {
        var filesByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            filesByName.TryAdd(Path.GetFileName(file), file);

        var missing = FontPackFiles.Required
            .Where(gamePath => !filesByName.ContainsKey(FontPackFiles.NameOf(gamePath)))
            .ToArray();
        if (missing.Length > 0)
        {
            var hint = UnsupportedPackHint(filesByName.Keys);
            return hint is null ? MissingResult(missing) : FontPackResult.Failure(hint);
        }

        Directory.CreateDirectory(destinationDirectory);
        var localPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var gamePath in FontPackFiles.Required)
        {
            var target = Path.Combine(destinationDirectory, FontPackFiles.NameOf(gamePath));
            CopyFile(filesByName[FontPackFiles.NameOf(gamePath)], target);
            localPaths[gamePath] = Path.GetFullPath(target);
        }

        return Validate(localPaths);
    }

    /// <summary>
    /// Reads a TexTools manifest and keeps the entries that name a required font file. The
    /// format is a JSON object with a <c>SimpleModsList</c> array. Field names are matched
    /// without case, because packs written by other tools vary.
    /// </summary>
    public static bool TryParseManifest(
        Stream stream,
        out Dictionary<string, Ttmp2Entry> entries,
        out string error)
        => TryParseManifest(stream, out entries, out error, null);

    internal static bool TryParseManifest(
        Stream stream,
        out Dictionary<string, Ttmp2Entry> entries,
        out string error,
        ICollection<string>? seenPaths)
    {
        entries = new Dictionary<string, Ttmp2Entry>(StringComparer.OrdinalIgnoreCase);
        error = string.Empty;

        try
        {
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var bytes = memory.ToArray().AsSpan();
            if (bytes.StartsWith(Utf8Bom))
                bytes = bytes[Utf8Bom.Length..];

            using var document = JsonDocument.Parse(bytes.ToArray());
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !TryGetProperty(document.RootElement, "SimpleModsList", out var list)
                || list.ValueKind != JsonValueKind.Array)
            {
                error = $"The {ManifestEntryName} manifest lists no files.";
                return false;
            }

            foreach (var item in list.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object
                    || !TryGetProperty(item, "FullPath", out var pathElement)
                    || pathElement.ValueKind != JsonValueKind.String
                    || !TryGetProperty(item, "ModOffset", out var offsetElement)
                    || offsetElement.ValueKind != JsonValueKind.Number
                    || !TryGetProperty(item, "ModSize", out var sizeElement)
                    || sizeElement.ValueKind != JsonValueKind.Number)
                {
                    error = $"The {ManifestEntryName} manifest has an entry without a path, offset or size.";
                    return false;
                }

                var gamePath = FontPackFiles.Normalize(pathElement.GetString() ?? string.Empty);
                seenPaths?.Add(gamePath);
                if (!FontPackFiles.IsRequired(gamePath))
                    continue;

                entries[gamePath] = new Ttmp2Entry(offsetElement.GetInt64(), sizeElement.GetInt64());
            }

            return true;
        }
        catch (JsonException ex)
        {
            error = $"The {ManifestEntryName} manifest is not valid JSON: {ex.Message}";
            return false;
        }
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static ZipArchiveEntry? FindEntry(ZipArchive archive, string name)
        => archive.GetEntry(name)
           ?? archive.Entries.FirstOrDefault(entry =>
               string.Equals(entry.FullName, name, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<string> MissingFiles(IEnumerable<string> provided)
    {
        var present = new HashSet<string>(provided, StringComparer.OrdinalIgnoreCase);
        return FontPackFiles.Required.Where(gamePath => !present.Contains(gamePath)).ToArray();
    }

    private static FontPackResult MissingResult(IReadOnlyList<string> missing)
        => FontPackResult.Failure(
            $"The font pack is missing {missing.Count} of the {FontPackFiles.Required.Count} font " +
            $"files the game loads. A partial pack garbles the fonts it leaves out. Missing: " +
            string.Join(", ", missing) + ".");

    /// <summary>
    /// A pack for the Chinese client replaces different font files, so on the global client it
    /// would leave the game font unchanged. Say that instead of only listing the missing files.
    /// </summary>
    private static string? UnsupportedPackHint(IEnumerable<string> provided)
    {
        var chineseClientPack = provided.Any(static path =>
            path.Contains("chnaxis", StringComparison.OrdinalIgnoreCase)
            || path.Contains("font_chn", StringComparison.OrdinalIgnoreCase));

        return chineseClientPack
            ? "This pack is for the Chinese client: it replaces chnaxis_*.fdt and font_chn_*.tex. " +
              "The global client loads common/font/axis_*.fdt and font1/font2.tex. Use the pack's " +
              "(Global) version."
            : null;
    }

    private static FontPackResult Validate(Dictionary<string, string> localPaths)
    {
        foreach (var (gamePath, localPath) in localPaths)
        {
            if (!File.Exists(localPath))
                return FontPackResult.Failure($"The font file for {gamePath} could not be written: {localPath}");

            if (!FontPackFiles.FitsDescriptor(localPath))
            {
                return FontPackResult.Failure(
                    "The plugin folder path is too long for the game's file name buffer. " +
                    "Move XIVLauncher's plugin configuration folder to a shorter path, or turn the font pack off.");
            }
        }

        return new FontPackResult(
            true,
            $"Loaded {localPaths.Count} font files from the pack.",
            localPaths);
    }

    private static MemoryStream CopyToMemory(Stream stream)
    {
        var memory = new MemoryStream();
        stream.CopyTo(memory);
        memory.Position = 0;
        return memory;
    }

    private static void CopyRange(Stream data, long offset, long size, string target)
    {
        data.Seek(offset, SeekOrigin.Begin);
        var temporary = target + ".tmp";
        try
        {
            using (var output = File.Create(temporary))
            {
                var buffer = new byte[81920];
                var remaining = size;
                while (remaining > 0)
                {
                    var read = data.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                    if (read <= 0)
                        throw new EndOfStreamException(
                            $"The font pack ended before the file at {offset.ToString(CultureInfo.InvariantCulture)}.");

                    output.Write(buffer, 0, read);
                    remaining -= read;
                }
            }

            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static void CopyFile(string source, string target)
    {
        var temporary = target + ".tmp";
        try
        {
            File.Copy(source, temporary, overwrite: true);
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
}

/// <summary>The outcome of reading a pack: the local file for each game path, or a reason.</summary>
public sealed record FontPackResult(
    bool Success,
    string Message,
    IReadOnlyDictionary<string, string> LocalPaths)
{
    private static readonly IReadOnlyDictionary<string, string> NoPaths =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public static FontPackResult Failure(string message) => new(false, message, NoPaths);
}
