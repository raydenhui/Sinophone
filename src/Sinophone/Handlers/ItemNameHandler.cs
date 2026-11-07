using FFXIVClientStructs.FFXIV.Client.UI;

namespace Sinophone.Handlers;

/// <summary>
/// Watches the item under the mouse and shows the Chinese item name, either in the
/// item name window or in the game's own item tooltip.
/// </summary>
/// <remarks>
/// The game raises <see cref="IGameGui.HoveredItemChanged"/> when the hovered
/// item changes, so nothing is read per frame.
/// <para>
/// Native mode writes into the tooltip's name node, which the game lays out itself.
/// The handler also listens to the tooltip addon, because the hovered item can change
/// before the addon has its new text. A tooltip with no name node falls back to the
/// overlay, so the setting never shows nothing.
/// </para>
/// </remarks>
public sealed unsafe class ItemNameHandler : IDisposable
{
    private const string ItemDetailAddon = "ItemDetail";

    /// <summary>Space the tooltip keeps on the right of the name.</summary>
    private const int NameRightMargin = 8;

    /// <summary>
    /// The game adds this to the id of a high-quality item.
    /// </summary>
    private const ulong HighQualityOffset = 1_000_000;

    /// <summary>
    /// The game adds this to the id of a collectible item.
    /// </summary>
    private const ulong CollectibleOffset = 500_000;

    /// <summary>
    /// Ids from here up are event items, which live in another sheet and are not
    /// translated yet.
    /// </summary>
    private const ulong EventItemStart = 2_000_000;

    private readonly IGameGui _gameGui;
    private readonly IDataManager _dataManager;
    private readonly IAddonLifecycle _addonLifecycle;
    private readonly Configuration _configuration;
    private readonly Func<TranslationStore?> _getStore;
    private readonly ItemNameWindow _window;
    private readonly NativeTextWriter _nativeWriter;
    private readonly PluginLog _log;
    private readonly EventHandler<ulong> _onHoveredItemChanged;
    private readonly IAddonLifecycle.AddonEventDelegate _onAddonEvent;

    /// <summary>The tooltip name node this handler changed, or 0 when it changed none.</summary>
    private nint _tooltipNode;

    /// <summary>Lines the current tooltip name uses. Two when both languages do not fit on one.</summary>
    private int _tooltipLines = 1;

    private bool _disposed;

    public ItemNameHandler(
        IGameGui gameGui,
        IDataManager dataManager,
        IAddonLifecycle addonLifecycle,
        Configuration configuration,
        Func<TranslationStore?> getStore,
        ItemNameWindow window,
        NativeTextWriter nativeWriter,
        IPluginLog log)
    {
        _gameGui = gameGui;
        _dataManager = dataManager;
        _addonLifecycle = addonLifecycle;
        _configuration = configuration;
        _getStore = getStore;
        _window = window;
        _nativeWriter = nativeWriter;
        _log = new PluginLog(log, configuration, LogCategory.ItemNames);
        _onHoveredItemChanged = OnHoveredItemChanged;
        _onAddonEvent = OnAddonEvent;

        _gameGui.HoveredItemChanged += _onHoveredItemChanged;
        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, ItemDetailAddon, _onAddonEvent);
        _addonLifecycle.RegisterListener(AddonEvent.PostRefresh, ItemDetailAddon, _onAddonEvent);
        _addonLifecycle.RegisterListener(AddonEvent.PreDraw, ItemDetailAddon, _onAddonEvent);
        _addonLifecycle.RegisterListener(AddonEvent.PostHide, ItemDetailAddon, _onAddonEvent);
        _addonLifecycle.RegisterListener(AddonEvent.PreFinalize, ItemDetailAddon, _onAddonEvent);
    }

    /// <summary>Re-reads the hovered item. Call after a settings or store change.</summary>
    public void Refresh()
    {
        if (_disposed)
            return;

        Show(_gameGui.HoveredItem);
    }

    private void OnHoveredItemChanged(object? sender, ulong itemId)
    {
        if (_disposed)
            return;

        Show(itemId);
    }

    /// <summary>
    /// The tooltip addon is rebuilt for each hovered item. The hover can change before
    /// the addon has its new text, so write again once the addon settles. A hide or a
    /// finalize means the tooltip is going away; drop the saved text rather than write
    /// it into a node the game no longer owns.
    /// </summary>
    private void OnAddonEvent(AddonEvent eventType, AddonArgs args)
    {
        if (_disposed)
            return;

        if (eventType == AddonEvent.PreDraw)
        {
            ReapplyTooltip();
            return;
        }

        if (eventType is AddonEvent.PostHide or AddonEvent.PreFinalize)
        {
            ForgetTooltip();
            return;
        }

        Show(_gameGui.HoveredItem);
    }

    /// <summary>
    /// The tooltip writes its own name again while it is open, so a write from an addon
    /// event lasts one frame. Runs on every tooltip draw, the same way the Talk window
    /// keeps its line: matching text costs a byte compare and no allocation.
    /// </summary>
    private void ReapplyTooltip()
    {
        if (_tooltipNode == 0
            || !_configuration.Enabled
            || !_configuration.ShowItemName
            || !_configuration.ItemNameNativeMode)
        {
            return;
        }

        _nativeWriter.ReapplyAt(ItemDetailAddon, _tooltipNode);
        FitTooltipName(_tooltipNode);
    }

    private void Show(ulong hoveredItem)
    {
        if (!_configuration.Enabled || !_configuration.ShowItemName)
        {
            HideAll();
            return;
        }

        var itemId = GetBaseItemId(hoveredItem);
        var englishName = GetEnglishName(itemId);
        var content = Resolve(_getStore(), itemId, englishName);

        if (content is null)
        {
            if (itemId != 0 && !string.IsNullOrEmpty(englishName))
                _log.Debug("No Chinese name for item {ItemId} ({Name}).", itemId, englishName);

            HideAll();
            return;
        }

        if (_configuration.ItemNameNativeMode && TryWriteTooltip(content.Value))
        {
            _window.Hide();
            return;
        }

        // Overlay mode, or native mode with no tooltip node to write. The overlay is
        // the fallback, so the item keeps a Chinese name either way.
        RestoreTooltip();
        _window.SetName(content.Value.ChineseName, content.Value.EnglishName);
    }

    private void HideAll()
    {
        RestoreTooltip();
        _window.Hide();
    }

    /// <summary>
    /// Writes the name into the tooltip's own name node. Returns false when the addon
    /// or the node is missing, so the caller can show the overlay instead.
    /// </summary>
    private bool TryWriteTooltip(ItemNameContent content)
    {
        var node = CurrentNameNode();
        if (node == 0)
        {
            _log.Debug("Native item name: {Addon} has no name node.", ItemDetailAddon);
            return false;
        }

        if (_tooltipNode != 0 && _tooltipNode != node)
            _nativeWriter.ForgetAt(ItemDetailAddon, _tooltipNode);

        var text = FormatItemName(content.ChineseName, content.EnglishName, _configuration.ShowOriginal);
        var (textWidth, _) = MeasureText((AtkTextNode*)node, text);
        var available = AvailableWidth((AtkTextNode*)node);

        // The tooltip is not wide enough for both languages on one line, so the
        // English name goes on a second line instead of running past the window.
        _tooltipLines = available > 0 && textWidth > available ? 2 : 1;
        if (_tooltipLines == 2)
            text = FormatItemName(content.ChineseName, content.EnglishName, _configuration.ShowOriginal, twoLines: true);

        if (!_nativeWriter.WriteAt(ItemDetailAddon, node, text))
            return false;

        _tooltipNode = node;
        FitTooltipName(node);

        var nameNode = (AtkTextNode*)node;
        _log.Debug(
            "Native item name: {English} -> {Text} (node at {X},{Y} {Width}x{Height}, flags {Flags}, {Lines} line(s), {Available} px).",
            content.EnglishName, text, nameNode->X, nameNode->Y, nameNode->Width,
            nameNode->Height, nameNode->TextFlags, _tooltipLines, available);
        return true;
    }

    /// <summary>
    /// The tooltip sizes the name node to the English name, so a longer Chinese name
    /// wraps into narrow lines of a few characters. The header draws one line, so the
    /// wrap comes off and the node grows to the text.
    /// </summary>
    /// <remarks>
    /// The height goes back to what the game gave the English name, and keeps it when
    /// the English name is on a second line. The tooltip draws the line at the bottom of
    /// the node, so the two-line name stacks upward into the header instead of pushing
    /// the English line down over the item description.
    /// </remarks>
    private void FitTooltipName(nint address)
    {
        var node = (AtkTextNode*)address;
        if (node is null)
            return;

        node->TextFlags &= ~(TextFlags.WordWrap | TextFlags.Ellipsis);

        ushort width = 0;
        ushort height = 0;
        node->GetTextDrawSize(&width, &height);

        if (!_nativeWriter.TryGetOriginalSize(ItemDetailAddon, address, out var originalWidth, out var originalHeight))
        {
            originalWidth = node->Width;
            originalHeight = node->Height;
        }

        // The width only grows, so the name is never clipped.
        if (width > originalWidth)
            node->Width = width;

        node->Height = originalHeight;
    }

    /// <summary>Measures a candidate name without writing it into the node.</summary>
    private static unsafe (ushort Width, ushort Height) MeasureText(AtkTextNode* node, string text)
    {
        var utf8 = Encoding.UTF8.GetBytes(text);
        ushort width = 0;
        ushort height = 0;
        fixed (byte* pointer = utf8)
            node->GetTextDrawSize(&width, &height, pointer);

        return (width, height);
    }

    /// <summary>
    /// Width the tooltip gives the name, or 0 when the addon is not up yet. The name
    /// starts after the icon, so the space is what is left of the window from there.
    /// </summary>
    private ushort AvailableWidth(AtkTextNode* node)
    {
        var addon = (AtkUnitBase*)_gameGui.GetAddonByName<AtkUnitBase>(ItemDetailAddon, 1);
        if (addon is null || addon->RootNode is null)
            return 0;

        var available = (int)addon->RootNode->Width - node->X - NameRightMargin;
        return available <= 0 ? (ushort)0 : (ushort)available;
    }

    /// <summary>Puts the game's own item name back into the tooltip.</summary>
    private void RestoreTooltip()
    {
        if (_tooltipNode == 0)
            return;

        // The addon may have closed, and the freed node may hold another addon's
        // text now. Only write when the tooltip still points at the node we changed.
        if (CurrentNameNode() == _tooltipNode)
            _nativeWriter.RestoreAt(ItemDetailAddon, _tooltipNode);
        else
            _nativeWriter.ForgetAt(ItemDetailAddon, _tooltipNode);

        _tooltipNode = 0;
    }

    /// <summary>Drops the saved name without writing it back, for a tooltip that closed.</summary>
    private void ForgetTooltip()
    {
        if (_tooltipNode == 0)
            return;

        _nativeWriter.ForgetAt(ItemDetailAddon, _tooltipNode);
        _tooltipNode = 0;
    }

    private nint CurrentNameNode()
    {
        var addon = (AddonItemDetail*)_gameGui.GetAddonByName<AtkUnitBase>(ItemDetailAddon, 1);
        if (addon is null || addon->ItemNameText is null)
            return 0;

        return (nint)addon->ItemNameText;
    }

    /// <summary>
    /// Builds the name to show. The tooltip normally draws one line, so the English name
    /// follows the Chinese name in brackets, the same shape the nameplates use. When the
    /// two names do not fit on one line, the English name moves to the second line.
    /// </summary>
    internal static string FormatItemName(
        string chineseName,
        string englishName,
        bool showEnglish,
        bool twoLines = false)
    {
        if (!showEnglish || string.IsNullOrWhiteSpace(englishName))
            return chineseName;

        return twoLines
            ? $"{chineseName}\n({englishName})"
            : $"{chineseName} ({englishName})";
    }

    private string? GetEnglishName(uint itemId)
    {
        if (itemId == 0)
            return null;

        var item = _dataManager.GetExcelSheet<Item>().GetRowOrDefault(itemId);
        return item?.Name.ExtractText();
    }

    /// <summary>
    /// Converts the raw hovered item id to an <c>Item</c> row id. The game adds an
    /// offset to tell high-quality and collectible items apart. Event items are
    /// not offset and live in another sheet, so they return 0.
    /// </summary>
    internal static uint GetBaseItemId(ulong hoveredItem)
    {
        if (hoveredItem is 0 or >= EventItemStart)
            return 0;

        if (hoveredItem >= HighQualityOffset)
            return (uint)(hoveredItem - HighQualityOffset);

        if (hoveredItem >= CollectibleOffset)
            return (uint)(hoveredItem - CollectibleOffset);

        return (uint)hoveredItem;
    }

    /// <summary>
    /// Decides what to show for one item. Pure apart from the store lookup, so
    /// tests can call it without a game sheet. Returns null when nothing should
    /// be shown.
    /// </summary>
    internal static ItemNameContent? Resolve(TranslationStore? store, uint itemId, string? englishName)
    {
        if (store is null || itemId == 0)
            return null;

        if (string.IsNullOrWhiteSpace(englishName))
            return null;

        if (!store.TryGetName(englishName, out var chineseName))
            return null;

        return new ItemNameContent(chineseName, englishName);
    }

    internal readonly record struct ItemNameContent(string ChineseName, string EnglishName);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _gameGui.HoveredItemChanged -= _onHoveredItemChanged;
        _addonLifecycle.UnregisterListener(AddonEvent.PostSetup, ItemDetailAddon, _onAddonEvent);
        _addonLifecycle.UnregisterListener(AddonEvent.PostRefresh, ItemDetailAddon, _onAddonEvent);
        _addonLifecycle.UnregisterListener(AddonEvent.PreDraw, ItemDetailAddon, _onAddonEvent);
        _addonLifecycle.UnregisterListener(AddonEvent.PostHide, ItemDetailAddon, _onAddonEvent);
        _addonLifecycle.UnregisterListener(AddonEvent.PreFinalize, ItemDetailAddon, _onAddonEvent);

        RestoreTooltip();
        _window.Hide();
    }
}
