namespace Sinophone.Data;

/// <summary>
/// Shared primitives for the index cache. Each index writes its own state, so the format sits
/// next to the collections it describes.
/// </summary>
internal static class IndexCacheFormat
{
    public static void WriteStrings(BinaryWriter writer, Dictionary<string, string> map)
    {
        writer.Write(map.Count);
        foreach (var (key, value) in map)
        {
            writer.Write(key);
            writer.Write(value);
        }
    }

    public static void ReadStrings(BinaryReader reader, Dictionary<string, string> map)
    {
        var count = reader.ReadInt32();
        for (var i = 0; i < count; i++)
            map.Add(reader.ReadString(), reader.ReadString());
    }

    public static void WriteStringSet(BinaryWriter writer, HashSet<string> set)
    {
        writer.Write(set.Count);
        foreach (var value in set)
            writer.Write(value);
    }

    public static void ReadStringSet(BinaryReader reader, HashSet<string> set)
    {
        var count = reader.ReadInt32();
        for (var i = 0; i < count; i++)
            set.Add(reader.ReadString());
    }

    public static void WriteNullableUshort(BinaryWriter writer, ushort? value)
    {
        writer.Write(value.HasValue);
        if (value.HasValue)
            writer.Write(value.Value);
    }

    public static ushort? ReadNullableUshort(BinaryReader reader)
        => reader.ReadBoolean() ? reader.ReadUInt16() : null;
}
