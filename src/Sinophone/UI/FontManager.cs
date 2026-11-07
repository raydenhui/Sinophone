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

    /// <summary>
    /// The glyphs the overlay can draw: Latin, punctuation, and CJK. Building the
    /// atlas for all of Unicode stalled the game at load, because ImGui tries every
    /// codepoint in the range before drawing a frame.
    /// </summary>
    private static readonly UnicodeRange[] GlyphRanges =
    [
        UnicodeRanges.BasicLatin,
        UnicodeRanges.Latin1Supplement,
        UnicodeRanges.LatinExtendedA,
        UnicodeRanges.LatinExtendedB,
        UnicodeRanges.LatinExtendedAdditional,
        UnicodeRanges.GreekandCoptic,
        UnicodeRanges.Cyrillic,
        UnicodeRanges.GeneralPunctuation,
        UnicodeRanges.CjkSymbolsandPunctuation,
        UnicodeRanges.Hiragana,
        UnicodeRanges.Katakana,
        UnicodeRanges.CjkUnifiedIdeographsExtensionA,
        UnicodeRanges.CjkUnifiedIdeographs,
        UnicodeRanges.CjkCompatibilityIdeographs,
        UnicodeRanges.HalfwidthandFullwidthForms,
    ];

    private readonly IFontAtlas _atlas;
    private readonly Configuration _configuration;
    private readonly PluginLog _log;

    private IFontHandle? _handle;
    private string? _activePath;
    private float _activeSize;
    private bool _buildFailed;
    private bool _disposed;

    public FontManager(IUiBuilder uiBuilder, Configuration configuration, PluginLog log)
    {
        _configuration = configuration;
        _log = log;

        // A private atlas in async mode builds on a worker task. The plugin-private
        // UiBuilder atlas rebuilds on the draw thread, where rasterizing the CJK
        // ranges froze the game for seconds at load.
        _atlas = uiBuilder.CreateFontAtlas(FontAtlasAutoRebuildMode.Async, debugName: "Sinophone");
        Rebuild();
    }

    /// <summary>Font handle to push while drawing Chinese text. Null when no usable font was found.</summary>
    public IFontHandle? Handle => _handle;

    /// <summary>
    /// True when the handle can draw. A failed build counts as ready, so a broken
    /// font never holds the translation back forever.
    /// </summary>
    public bool IsReady
        => _handle is null || _handle.Available || _handle.LoadException is not null || _buildFailed;

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
        _buildFailed = false;
        _activePath = path;
        _activeSize = size;
        UsingSystemFont = isSystemFont;

        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return;

        _handle = _atlas.NewDelegateFontHandle(e => e.OnPreBuild(toolkit =>
        {
            var ranges = default(FluentGlyphRangeBuilder)
                .With(GlyphRanges)
                .Build();

            var fontConfig = new SafeFontConfig
            {
                SizePx = size,
                GlyphRanges = ranges,
            };

            toolkit.Font = toolkit.AddFontFromFile(path, fontConfig);
        }));

        // Async mode never rebuilds on its own. Starting the build here keeps the
        // glyph rasterization off the draw thread.
        _ = BuildAsync(_handle);
    }

    private async Task BuildAsync(IFontHandle handle)
    {
        try
        {
            await _atlas.BuildFontsAsync().ConfigureAwait(false);

            if (handle.LoadException is { } loadException)
                _log.Error(loadException, "Failed to build the CJK font atlas.");
            else
                _log.Debug("Built the CJK font atlas.");
        }
        catch (Exception ex)
        {
            // Do not hold the store back forever if the build never finishes.
            _buildFailed = true;
            _log.Error(ex, "Failed to build the CJK font atlas.");
        }
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
        _atlas.Dispose();
    }
}
