namespace Sinophone.Fonts;

/// <summary>
/// The game font files a pack must replace.
/// </summary>
/// <remarks>
/// The fonts share the two atlas files, so an <c>.fdt</c> left at its original points into an
/// atlas that was re-laid out. Numbers and titles would draw wrong glyphs. A pack must replace
/// the whole set or none of it.
/// </remarks>
public static class FontPackFiles
{
    /// <summary>The longest file path the game's file descriptor can hold.</summary>
    public const int MaxLocalPathLength = 260;

    /// <summary>
    /// Every font file the global client loads, in the order the font pack lists them.
    /// </summary>
    public static IReadOnlyList<string> Required { get; } =
    [
        "common/font/axis_12.fdt",
        "common/font/axis_14.fdt",
        "common/font/axis_18.fdt",
        "common/font/axis_36.fdt",
        "common/font/axis_96.fdt",
        "common/font/jupiter_16.fdt",
        "common/font/jupiter_20.fdt",
        "common/font/jupiter_23.fdt",
        "common/font/jupiter_45.fdt",
        "common/font/jupiter_46.fdt",
        "common/font/jupiter_90.fdt",
        "common/font/meidinger_16.fdt",
        "common/font/meidinger_20.fdt",
        "common/font/meidinger_40.fdt",
        "common/font/miedingermid_10.fdt",
        "common/font/miedingermid_12.fdt",
        "common/font/miedingermid_14.fdt",
        "common/font/miedingermid_18.fdt",
        "common/font/miedingermid_36.fdt",
        "common/font/trumpgothic_184.fdt",
        "common/font/trumpgothic_23.fdt",
        "common/font/trumpgothic_34.fdt",
        "common/font/trumpgothic_68.fdt",
        "common/font/font1.tex",
        "common/font/font2.tex",
    ];

    private static readonly HashSet<string> RequiredSet = new(Required, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Normalizes a pack path or a game path to the form the game uses: forward slashes and
    /// lower case.
    /// </summary>
    public static string Normalize(string path)
        => path.Replace('\\', '/').Trim().TrimStart('/').ToLowerInvariant();

    /// <summary>True when a game path is one of the files a pack has to replace.</summary>
    public static bool IsRequired(string gamePath)
        => RequiredSet.Contains(Normalize(gamePath));

    /// <summary>The file name of a game path, without its folders.</summary>
    public static string NameOf(string gamePath)
        => gamePath[(gamePath.LastIndexOf('/') + 1)..];

    /// <summary>True when the local path fits the game's fixed file descriptor buffer.</summary>
    public static bool FitsDescriptor(string localPath) => localPath.Length < MaxLocalPathLength;
}
