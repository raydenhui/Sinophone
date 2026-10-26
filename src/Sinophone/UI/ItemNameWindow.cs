namespace Sinophone.UI;

/// <summary>
/// Small window that shows the Chinese name of the hovered item next to the
/// mouse cursor. The game cannot draw the characters, so this uses the same
/// font atlas as the dialogue overlay.
/// </summary>
public sealed class ItemNameWindow : Window
{
    private static readonly Vector4 NameColor = new(1f, 0.84f, 0.35f, 1f);
    private static readonly Vector4 OriginalColor = new(0.72f, 0.76f, 0.82f, 1f);

    /// <summary>Distance from the cursor to the window, in unscaled pixels.</summary>
    private const float CursorOffset = 18f;

    private readonly Configuration _configuration;
    private readonly FontManager _fontManager;

    private string _chineseName = string.Empty;
    private string _englishName = string.Empty;
    private bool _hasContent;

    public ItemNameWindow(Configuration configuration, FontManager fontManager)
        : base("Sinophone Item###SinophoneItemName", ImGuiWindowFlags.NoTitleBar)
    {
        _configuration = configuration;
        _fontManager = fontManager;

        IsOpen = true;
        RespectCloseHotkey = false;
        ShowCloseButton = false;
        AllowPinning = false;
        DisableWindowSounds = true;
        AllowClickthrough = true;
        BgAlpha = configuration.OverlayOpacity;
    }

    public void SetName(string chineseName, string englishName)
    {
        _chineseName = chineseName;
        _englishName = englishName;
        _hasContent = !string.IsNullOrWhiteSpace(chineseName);
    }

    public void Hide()
    {
        _hasContent = false;
        _chineseName = string.Empty;
        _englishName = string.Empty;
    }

    public override bool DrawConditions()
        => _hasContent && _configuration.Enabled && _configuration.ShowItemName;

    public override void PreDraw()
    {
        base.PreDraw();

        Flags = ImGuiWindowFlags.NoTitleBar
                | ImGuiWindowFlags.NoResize
                | ImGuiWindowFlags.NoMove
                | ImGuiWindowFlags.NoScrollbar
                | ImGuiWindowFlags.NoScrollWithMouse
                | ImGuiWindowFlags.NoCollapse
                | ImGuiWindowFlags.NoSavedSettings
                | ImGuiWindowFlags.NoBringToFrontOnFocus
                | ImGuiWindowFlags.NoNavFocus
                | ImGuiWindowFlags.NoFocusOnAppearing;

        AllowClickthrough = true;
        BgAlpha = _configuration.OverlayOpacity;

        var size = MeasureSize();
        Size = size;
        SizeCondition = ImGuiCond.Always;

        // Sit above the cursor: the game draws the item tooltip below it, so this
        // does not cover the tooltip. Keep the window on screen near an edge.
        var scale = ImGuiHelpers.GlobalScale;
        var viewport = ImGuiHelpers.MainViewport;
        var mouse = ImGui.GetMousePos();
        var x = MathF.Min(mouse.X + CursorOffset * scale, viewport.Pos.X + viewport.Size.X - size.X);
        var y = MathF.Max(mouse.Y - size.Y - CursorOffset * scale, viewport.Pos.Y);

        Position = new Vector2(x, y);
        PositionCondition = ImGuiCond.Always;
    }

    public override void Draw()
    {
        var fontHandle = _fontManager.Handle;
        using var font = fontHandle?.Push();

        ImGui.PushStyleColor(ImGuiCol.Text, NameColor);
        ImGui.TextUnformatted(_chineseName);
        ImGui.PopStyleColor();

        if (_configuration.ShowOriginal && !string.IsNullOrWhiteSpace(_englishName))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, OriginalColor);
            ImGui.TextUnformatted(_englishName);
            ImGui.PopStyleColor();
        }
    }

    private Vector2 MeasureSize()
    {
        var fontHandle = _fontManager.Handle;
        using var font = fontHandle?.Push();

        var style = ImGui.GetStyle();
        var showOriginal = _configuration.ShowOriginal && !string.IsNullOrWhiteSpace(_englishName);

        var textSize = ImGui.CalcTextSize(_chineseName, false, -1f);
        var width = textSize.X;

        if (showOriginal)
            width = MathF.Max(width, ImGui.CalcTextSize(_englishName, false, -1f).X);

        var height = textSize.Y;
        if (showOriginal)
            height += style.ItemSpacing.Y + ImGui.CalcTextSize(_englishName, false, -1f).Y;

        return new Vector2(
            width + style.WindowPadding.X * 2f,
            height + style.WindowPadding.Y * 2f);
    }
}
