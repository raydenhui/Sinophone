namespace Sinophone.UI;

/// <summary>
/// Renders the Chinese translation above the game's Talk dialog. The game's own
/// dialog cannot display CJK glyphs, so translated text is drawn in an ImGui window
/// positioned over the bottom of the screen.
/// </summary>
public sealed class OverlayWindow : Window
{
    private static readonly Vector4 NameColor = new(1f, 0.84f, 0.35f, 1f);
    private static readonly Vector4 ChineseColor = new(1f, 1f, 1f, 1f);
    private static readonly Vector4 OriginalColor = new(0.72f, 0.76f, 0.82f, 1f);

    private readonly Configuration _configuration;
    private readonly FontManager _fontManager;
    private readonly List<TextBlock> _blocks = new(4);

    private bool _hasContent;
    private bool _applyPosition;
    private bool _positionInitialized;

    public OverlayWindow(Configuration configuration, FontManager fontManager)
        : base("Sinophone Overlay###SinophoneOverlay", ImGuiWindowFlags.NoTitleBar)
    {
        _configuration = configuration;
        _fontManager = fontManager;

        IsOpen = true;
        RespectCloseHotkey = false;
        ShowCloseButton = false;
        AllowPinning = false;
        DisableWindowSounds = true;
        AllowClickthrough = configuration.Clickthrough;
        IsClickthrough = configuration.Clickthrough;
        BgAlpha = configuration.OverlayOpacity;
    }

    public bool IsShowing => _hasContent;

    public void SetContent(
        string chineseText,
        string originalText,
        string chineseName,
        string originalName,
        bool isSubtitle)
    {
        _blocks.Clear();

        var hasTranslation = !string.IsNullOrWhiteSpace(chineseText);

        if (_configuration.ShowSpeakerName)
        {
            var name = !string.IsNullOrWhiteSpace(chineseName)
                ? chineseName
                : _configuration.ShowOriginal
                    ? originalName
                    : string.Empty;

            if (!string.IsNullOrWhiteSpace(name))
                _blocks.Add(new TextBlock(name.Trim(), NameColor));
        }

        if (hasTranslation)
        {
            _blocks.Add(new TextBlock(chineseText, ChineseColor));

            if (_configuration.ShowOriginal && !string.IsNullOrWhiteSpace(originalText))
                _blocks.Add(new TextBlock(originalText, OriginalColor));
        }
        else if (!string.IsNullOrWhiteSpace(originalText))
        {
            _blocks.Add(new TextBlock(originalText, OriginalColor));
        }

        _hasContent = _blocks.Count > 0;

        // Only snap to the computed position the first time the overlay appears or while
        // the user asked to lock it. Otherwise every new line would undo a drag.
        _applyPosition = _hasContent && (_configuration.LockOverlay || !_positionInitialized);

        _ = isSubtitle;
    }

    public void Hide()
    {
        _hasContent = false;
        _applyPosition = false;
        _blocks.Clear();
    }

    public override bool DrawConditions()
        => _hasContent && _configuration.Enabled;

    public override void PreDraw()
    {
        base.PreDraw();

        var flags = ImGuiWindowFlags.NoTitleBar
                    | ImGuiWindowFlags.NoResize
                    | ImGuiWindowFlags.NoScrollbar
                    | ImGuiWindowFlags.NoScrollWithMouse
                    | ImGuiWindowFlags.NoCollapse
                    | ImGuiWindowFlags.NoSavedSettings
                    | ImGuiWindowFlags.NoBringToFrontOnFocus
                    | ImGuiWindowFlags.NoNavFocus
                    | ImGuiWindowFlags.NoFocusOnAppearing;

        if (_configuration.LockOverlay)
            flags |= ImGuiWindowFlags.NoMove;

        if (_configuration.Clickthrough)
            flags |= ImGuiWindowFlags.NoInputs;

        Flags = flags;
        AllowClickthrough = _configuration.Clickthrough;
        IsClickthrough = _configuration.Clickthrough;
        BgAlpha = _configuration.OverlayOpacity;

        var scale = ImGuiHelpers.GlobalScale;
        var width = MathF.Max(240f * scale, _configuration.OverlayWidth * scale);
        var height = MeasureHeight(width);

        Size = new Vector2(width, height);
        SizeCondition = ImGuiCond.Always;

        if (_configuration.LockOverlay || _applyPosition)
        {
            var viewport = ImGuiHelpers.MainViewport;
            var x = viewport.Pos.X + (viewport.Size.X - width) * 0.5f;
            var y = viewport.Pos.Y + viewport.Size.Y - _configuration.OffsetFromBottom * scale - height;

            Position = new Vector2(x, y);
            PositionCondition = ImGuiCond.Always;
            _applyPosition = false;
            _positionInitialized = true;
        }
        else
        {
            // Dalamud keeps applying a non-null Position every frame. Clear it so the
            // window keeps the spot the user dragged it to.
            Position = null;
        }
    }

    public override void Draw()
    {
        var fontHandle = _fontManager.Handle;
        using var font = fontHandle?.Push();

        for (var i = 0; i < _blocks.Count; i++)
        {
            var block = _blocks[i];

            ImGui.PushStyleColor(ImGuiCol.Text, block.Color);
            ImGui.TextWrapped(block.Text);
            ImGui.PopStyleColor();
        }
    }

    private float MeasureHeight(float width)
    {
        var fontHandle = _fontManager.Handle;
        using var font = fontHandle?.Push();

        var style = ImGui.GetStyle();
        var wrapWidth = MathF.Max(1f, width - style.WindowPadding.X * 2f);
        var height = style.WindowPadding.Y * 2f;

        for (var i = 0; i < _blocks.Count; i++)
        {
            height += ImGui.CalcTextSize(_blocks[i].Text, false, wrapWidth).Y;

            if (i < _blocks.Count - 1)
                height += style.ItemSpacing.Y;
        }

        return height;
    }

    private readonly record struct TextBlock(string Text, Vector4 Color);
}
