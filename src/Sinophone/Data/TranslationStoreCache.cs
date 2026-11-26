using System.Diagnostics;
using System.Text;

namespace Sinophone.Data;

/// <summary>
/// Reads and writes the finished index, so a later start does not have to parse the
/// artifact and read every live sheet row again.
/// </summary>
/// <remarks>
/// Only the lookup tables are stored. The sheet descriptors hold Lumina objects and
/// are rebuilt on the game thread. A cache is used only when its key matches the
/// current inputs; otherwise the store is rebuilt and the cache replaced.
/// </remarks>
public sealed partial class TranslationStore
{
    /// <summary>Cache file name in the plugin config directory.</summary>
    public const string CacheFileName = "Sinophone.index.bin";

    /// <summary>
    /// Bumped when the shape or meaning of the index changes, so an old cache is
    /// rebuilt instead of read.
    /// </summary>
    private const int CacheFormatVersion = 8;

    private static ReadOnlySpan<byte> CacheMagic => "SNPHIDX1"u8;

    /// <summary>
    /// Loads the index cache when it was built for the same inputs. Returns false
    /// when the file is missing, stale, or unreadable; the caller then builds the
    /// store from the artifacts.
    /// </summary>
    internal static bool TryLoadCache(
        string path,
        string expectedKey,
        IReadOnlyList<ISheetDescriptor> descriptors,
        PluginLog log,
        Func<string, string> displayResolver,
        Func<string?>? playerName,
        out TranslationStore? store)
    {
        store = null;

        if (!File.Exists(path))
        {
            log.Debug("No index cache; the index will be built.");
            return false;
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
            using var reader = new BinaryReader(file, Encoding.UTF8, leaveOpen: true);

            if (!reader.ReadBytes(CacheMagic.Length).AsSpan().SequenceEqual(CacheMagic)
                || reader.ReadInt32() != CacheFormatVersion)
            {
                log.Debug("Ignoring an index cache with a different format.");
                return false;
            }

            if (!string.Equals(reader.ReadString(), expectedKey, StringComparison.Ordinal))
            {
                log.Debug("The index cache was built for other inputs; rebuilding the index.");
                return false;
            }

            var primaryVariant = (ChineseVariant)reader.ReadInt32();
            var usedFallback = reader.ReadBoolean();

            var result = new TranslationStore(
                primaryVariant,
                usedFallback,
                descriptors,
                displayResolver,
                displayResolver,
                playerName,
                log);

            result.ReadCacheBody(reader);

            stopwatch.Stop();
            log.Debug(
                "Loaded the index cache in {Elapsed} ms: {Text:N0} dialogue and {Name:N0} name entries.",
                stopwatch.ElapsedMilliseconds, result.TextEntryCount, result.NameEntryCount);

            store = result;
            return true;
        }
        catch (Exception ex)
        {
            log.Warning(ex, "Could not read the index cache; rebuilding the index.");
            return false;
        }
    }

    /// <summary>
    /// Writes the finished index next to the data files. A failure is logged and
    /// otherwise ignored, because a missing cache only costs a rebuild.
    /// </summary>
    internal static void SaveCache(string path, string key, TranslationStore store, PluginLog log)
    {
        var temporary = path + ".tmp";

        try
        {
            using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
            using (var writer = new BinaryWriter(file, Encoding.UTF8))
            {
                writer.Write(CacheMagic);
                writer.Write(CacheFormatVersion);
                writer.Write(key);
                writer.Write((int)store.PrimaryVariant);
                writer.Write(store.UsedFallback);
                store.WriteCacheBody(writer);
            }

            File.Move(temporary, path, overwrite: true);
            log.Debug("Saved the index cache to {Path}.", path);
        }
        catch (Exception ex)
        {
            log.Warning(ex, "Could not save the index cache.");
            TryDeleteCache(temporary);
        }
    }

    private static void TryDeleteCache(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // A leftover temporary file is harmless; the next save overwrites it.
        }
    }

    private void WriteCacheBody(BinaryWriter writer)
    {
        _dialogue.WriteTo(writer);
        _names.WriteTo(writer);
        _addon.WriteTo(writer);
        _scripts.WriteTo(writer);

        writer.Write(_styleLookup.Count);
        foreach (var (key, style) in _styleLookup)
        {
            writer.Write(key);
            IndexCacheFormat.WriteNullableUshort(writer, style.ForegroundKey);
            IndexCacheFormat.WriteNullableUshort(writer, style.GlowKey);
        }

        writer.Write(_rowLookup.Count);
        foreach (var ((sheet, rowId, field), slots) in _rowLookup)
        {
            writer.Write(sheet);
            writer.Write(rowId);
            writer.Write(field);
            writer.Write(slots.Length);

            foreach (var slot in slots)
            {
                writer.Write(slot is not null);
                if (slot is not null)
                    writer.Write(slot);
            }
        }
    }

    private void ReadCacheBody(BinaryReader reader)
    {
        _dialogue.ReadFrom(reader);
        _names.ReadFrom(reader);
        _addon.ReadFrom(reader);
        _scripts.ReadFrom(reader);

        var count = reader.ReadInt32();
        for (var i = 0; i < count; i++)
        {
            var key = reader.ReadString();
            var foreground = IndexCacheFormat.ReadNullableUshort(reader);
            var glow = IndexCacheFormat.ReadNullableUshort(reader);
            _styleLookup.Add(key, new UiTextStyle(foreground, glow));
        }

        count = reader.ReadInt32();
        for (var i = 0; i < count; i++)
        {
            var sheet = reader.ReadString();
            var rowId = reader.ReadUInt32();
            var field = reader.ReadString();
            var slots = new string[reader.ReadInt32()];

            for (var j = 0; j < slots.Length; j++)
                slots[j] = reader.ReadBoolean() ? reader.ReadString() : null!;

            _rowLookup.Add((sheet, rowId, field), slots);
        }
    }
}
