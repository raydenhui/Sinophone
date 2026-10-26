namespace Sinophone.Handlers;

/// <summary>
/// Watches the item under the mouse and feeds the Chinese item name to the item
/// name window.
/// </summary>
/// <remarks>
/// The game raises <see cref="IGameGui.HoveredItemChanged"/> when the hovered
/// item changes, so nothing is read per frame.
/// </remarks>
public sealed class ItemNameHandler : IDisposable
{
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
    private readonly Configuration _configuration;
    private readonly Func<TranslationStore?> _getStore;
    private readonly ItemNameWindow _window;
    private readonly IPluginLog _log;
    private readonly EventHandler<ulong> _onHoveredItemChanged;

    private bool _disposed;

    public ItemNameHandler(
        IGameGui gameGui,
        IDataManager dataManager,
        Configuration configuration,
        Func<TranslationStore?> getStore,
        ItemNameWindow window,
        IPluginLog log)
    {
        _gameGui = gameGui;
        _dataManager = dataManager;
        _configuration = configuration;
        _getStore = getStore;
        _window = window;
        _log = log;
        _onHoveredItemChanged = OnHoveredItemChanged;

        _gameGui.HoveredItemChanged += _onHoveredItemChanged;
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

    private void Show(ulong hoveredItem)
    {
        if (!_configuration.Enabled || !_configuration.ShowItemName)
        {
            _window.Hide();
            return;
        }

        var itemId = GetBaseItemId(hoveredItem);
        var englishName = GetEnglishName(itemId);
        var content = Resolve(_getStore(), itemId, englishName);

        if (content is null)
        {
            if (itemId != 0 && !string.IsNullOrEmpty(englishName))
                _log.Debug("[Sinophone] No Chinese name for item {ItemId} ({Name}).", itemId, englishName);

            _window.Hide();
            return;
        }

        _window.SetName(content.Value.ChineseName, content.Value.EnglishName);
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
        _window.Hide();
    }
}
