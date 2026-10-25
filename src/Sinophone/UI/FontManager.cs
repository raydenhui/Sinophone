using System.Text.Unicode;

namespace Sinophone.UI;

/// <summary>
/// Owns the ImGui font used to render Chinese text. The game's bundled fonts do
/// not contain CJK glyphs, so a Windows system CJK font (or a user-supplied file)
/// is merged into a dedicated atlas handle.
/// </summary>
public sealed class FontManager : IDisposable
{
    private static readonly string[] TraditionalFontCandidates = ["msjh.ttc", "msjh.ttf", "mingliub.ttc"];
    private static readonly string[] SimplifiedFontCandidates = ["msyh.ttc", "msyh.ttf", "simsun.ttc"];

    private readonly IFontAtlas _atlas;
    private readonly Configuration _configuration;

    private IFontHandle? _handle;
    private string? _activePath;
    private float _activeSize;
    private bool _disposed;

    public FontManager(IFontAtlas atlas, Configuration configuration)
    {
        _atlas = atlas;
        _configuration = configuration;
        Rebuild();
    }

    /// <summary>Font handle to push while drawing Chinese text. Null when no usable font was found.</summary>
    public IFontHandle? Handle => _handle;

    /// <summary>Path of the font currently loaded, or null when a system font was unavailable.</summary>
    public string? ActiveFontPath => _activePath;

    /// <summary>True when the active font came from Windows rather than the user's custom path.</summary>
    public bool UsingSystemFont { get; private set; }

    /// <summary>
    /// Rebuilds the atlas when the configured font path, size, or variant changed.
    /// Safe to call every time the config window saves.
    /// </summary>
    public void Rebuild()
    {
        if (_disposed)
            return;

        var path = ResolveFontPath(_configuration, out var isSystemFont);
        var size = _configuration.FontSize;

        if (_handle is not null && _activePath == path && Math.Abs(_activeSize - size) < 0.01f)
        {
            UsingSystemFont = isSystemFont;
            return;
        }

        _handle?.Dispose();
        _handle = null;
        _activePath = path;
        _activeSize = size;
        UsingSystemFont = isSystemFont;

        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return;

        _handle = _atlas.NewDelegateFontHandle(e => e.OnPreBuild(toolkit =>
        {
            var ranges = default(FluentGlyphRangeBuilder)
                .With(UnicodeRanges.All)
                .Build();

            var fontConfig = new SafeFontConfig
            {
                SizePx = size,
                GlyphRanges = ranges,
            };

            toolkit.Font = toolkit.AddFontFromFile(path, fontConfig);
        }));
    }

    private static string? ResolveFontPath(Configuration configuration, out bool isSystemFont)
    {
        isSystemFont = false;

        var custom = configuration.CustomFontPath;
        if (!string.IsNullOrWhiteSpace(custom) && File.Exists(custom))
            return custom;

        var candidates = configuration.Variant == ChineseVariant.Traditional
            ? TraditionalFontCandidates
            : SimplifiedFontCandidates;

        var fontsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        if (string.IsNullOrEmpty(fontsDirectory))
            fontsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");

        foreach (var candidate in candidates)
        {
            var path = Path.Combine(fontsDirectory, candidate);
            if (File.Exists(path))
            {
                isSystemFont = true;
                return path;
            }
        }

        // Last resort: use the other variant's font rather than rendering tofu.
        var otherCandidates = configuration.Variant == ChineseVariant.Traditional
            ? SimplifiedFontCandidates
            : TraditionalFontCandidates;

        foreach (var candidate in otherCandidates)
        {
            var path = Path.Combine(fontsDirectory, candidate);
            if (File.Exists(path))
            {
                isSystemFont = true;
                return path;
            }
        }

        return null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _handle?.Dispose();
        _handle = null;
    }
}
