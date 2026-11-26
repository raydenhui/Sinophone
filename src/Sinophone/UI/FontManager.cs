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
    private readonly InterfaceText _text;
    private readonly PluginLog _log;

    private IFontHandle? _handle;
    private string? _activePath;
    private float _activeSize;
    private bool _buildFailed;
    private bool _disposed;

    private IFontHandle? _uiHandle;
    private InterfaceLanguage? _uiLanguage;
    private bool _uiResolved;

    public FontManager(IUiBuilder uiBuilder, Configuration configuration, InterfaceText text, PluginLog log)
    {
        _configuration = configuration;
        _text = text;
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
    /// Font for the settings window when its language is Chinese. Null for English and
    /// Japanese, which the default Dalamud font already draws.
    /// </summary>
    public IFontHandle? UiHandle => _uiHandle;

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

        // The settings-window font uses the same custom path, so it may need rebuilding too.
        _uiResolved = false;

        var path = ResolveFontPath(
            _configuration,
            _configuration.Variant == ChineseVariant.Traditional,
            out var isSystemFont);
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

    /// <summary>
    /// Keeps the settings-window font in step with its language. Call it while drawing the
    /// window: the language can change in Dalamud's settings without any event reaching us.
    /// </summary>
    public void EnsureUiFont()
    {
        if (_disposed)
            return;

        var language = _text.Language;
        if (_uiResolved && _uiLanguage == language)
            return;

        _uiResolved = true;
        _uiLanguage = language;

        var path = InterfaceLanguageResolver.NeedsCjkFont(language)
            ? ResolveFontPath(_configuration, language == InterfaceLanguage.TraditionalChinese, out _)
            : null;

        _uiHandle?.Dispose();
        _uiHandle = null;

        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return;

        _uiHandle = _atlas.NewDelegateFontHandle(e => e.OnPreBuild(toolkit =>
        {
            var ranges = default(FluentGlyphRangeBuilder)
                .With(GlyphRanges)
                .Build();

            var fontConfig = new SafeFontConfig
            {
                SizePx = UiBuilder.DefaultFontSizePx,
                GlyphRanges = ranges,
            };

            toolkit.Font = toolkit.AddFontFromFile(path, fontConfig);
        }));

        _ = BuildAsync(_uiHandle);
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

    private static string? ResolveFontPath(Configuration configuration, bool traditional, out bool isSystemFont)
    {
        isSystemFont = false;

        var custom = configuration.CustomFontPath;
        if (!string.IsNullOrWhiteSpace(custom) && File.Exists(custom))
            return custom;

        var fontsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        if (string.IsNullOrEmpty(fontsDirectory))
            fontsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");

        if (FindFont(fontsDirectory, traditional ? TraditionalFontCandidates : SimplifiedFontCandidates) is { } path)
        {
            isSystemFont = true;
            return path;
        }

        // Last resort: use the other script's font rather than rendering tofu.
        if (FindFont(fontsDirectory, traditional ? SimplifiedFontCandidates : TraditionalFontCandidates) is { } other)
        {
            isSystemFont = true;
            return other;
        }

        return null;
    }

    private static string? FindFont(string directory, string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            var path = Path.Combine(directory, candidate);
            if (File.Exists(path))
                return path;
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
        _uiHandle?.Dispose();
        _uiHandle = null;
        _atlas.Dispose();
    }
}
