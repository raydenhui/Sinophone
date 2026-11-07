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
    private const int CacheFormatVersion = 1;

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
            var scriptEntryCount = reader.ReadInt32();

            var result = new TranslationStore(
                primaryVariant,
                usedFallback,
                descriptors,
                displayResolver,
                displayResolver,
                playerName,
                log);

            result.ReadCacheBody(reader);
            result._scriptEntryCount = scriptEntryCount;

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
                writer.Write(store._scriptEntryCount);
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
        WriteStrings(writer, _textLookup);
        WriteStrings(writer, _nameLookup);
        WriteStrings(writer, _branchNeutralLookup);
        WriteStringSet(writer, _ambiguousBranchKeys);
        WriteStrings(writer, _addonLookup);

        writer.Write(_styleLookup.Count);
        foreach (var (key, style) in _styleLookup)
        {
            writer.Write(key);
            WriteNullableUshort(writer, style.ForegroundKey);
            WriteNullableUshort(writer, style.GlowKey);
        }

        writer.Write(_nameRowLookup.Count);
        foreach (var ((sheet, rowId), value) in _nameRowLookup)
        {
            writer.Write(sheet);
            writer.Write(rowId);
            writer.Write(value);
        }

        writer.Write(_addonTemplates.Count);
        foreach (var (template, chinese, indices) in _addonTemplates)
        {
            writer.Write(template);
            writer.Write(chinese);

            if (indices is null)
            {
                writer.Write(-1);
                continue;
            }

            writer.Write(indices.Length);
            foreach (var index in indices)
                writer.Write(index);
        }

        writer.Write(_scriptTemplates.Count);
        foreach (var (template, chinese) in _scriptTemplates)
        {
            writer.Write(template);
            writer.Write(chinese);
        }

        WriteStringSet(writer, _addonTemplateKeys);

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

        WriteStrings(writer, _scriptKeyLookup);
    }

    private void ReadCacheBody(BinaryReader reader)
    {
        ReadStrings(reader, _textLookup);
        ReadStrings(reader, _nameLookup);
        ReadStrings(reader, _branchNeutralLookup);
        ReadStringSet(reader, _ambiguousBranchKeys);
        ReadStrings(reader, _addonLookup);

        var count = reader.ReadInt32();
        for (var i = 0; i < count; i++)
        {
            var key = reader.ReadString();
            var foreground = ReadNullableUshort(reader);
            var glow = ReadNullableUshort(reader);
            _styleLookup.Add(key, new UiTextStyle(foreground, glow));
        }

        count = reader.ReadInt32();
        for (var i = 0; i < count; i++)
            _nameRowLookup.Add((reader.ReadString(), reader.ReadUInt32()), reader.ReadString());

        count = reader.ReadInt32();
        for (var i = 0; i < count; i++)
        {
            var template = reader.ReadString();
            var chinese = reader.ReadString();
            var length = reader.ReadInt32();

            int[]? indices = null;
            if (length >= 0)
            {
                indices = new int[length];
                for (var j = 0; j < length; j++)
                    indices[j] = reader.ReadInt32();
            }

            _addonTemplates.Add((template, chinese, indices));
        }

        count = reader.ReadInt32();
        for (var i = 0; i < count; i++)
            _scriptTemplates.Add((reader.ReadString(), reader.ReadString()));

        ReadStringSet(reader, _addonTemplateKeys);

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

        ReadStrings(reader, _scriptKeyLookup);
    }

    private static void WriteNullableUshort(BinaryWriter writer, ushort? value)
    {
        writer.Write(value.HasValue);
        if (value.HasValue)
            writer.Write(value.Value);
    }

    private static ushort? ReadNullableUshort(BinaryReader reader)
        => reader.ReadBoolean() ? reader.ReadUInt16() : null;

    private static void WriteStrings(BinaryWriter writer, Dictionary<string, string> map)
    {
        writer.Write(map.Count);
        foreach (var (key, value) in map)
        {
            writer.Write(key);
            writer.Write(value);
        }
    }

    private static void ReadStrings(BinaryReader reader, Dictionary<string, string> map)
    {
        var count = reader.ReadInt32();
        for (var i = 0; i < count; i++)
            map.Add(reader.ReadString(), reader.ReadString());
    }

    private static void WriteStringSet(BinaryWriter writer, HashSet<string> set)
    {
        writer.Write(set.Count);
        foreach (var value in set)
            writer.Write(value);
    }

    private static void ReadStringSet(BinaryReader reader, HashSet<string> set)
    {
        var count = reader.ReadInt32();
        for (var i = 0; i < count; i++)
            set.Add(reader.ReadString());
    }
}
