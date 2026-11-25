using System.Runtime.InteropServices;

using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

using FileMode = FFXIVClientStructs.FFXIV.Client.System.File.FileMode;

namespace Sinophone.Fonts;

/// <summary>
/// Replaces the game's own font files with a pack the user supplies, so native mode can draw
/// Chinese without Penumbra or TexTools.
/// </summary>
/// <remarks>
/// The game reads every resource through <c>ReadSqPack</c>. When the descriptor asks for one of
/// the font files the pack replaces, this service points the descriptor at the extracted file
/// and lets the game's own <c>ReadFile</c> read it. Every other file is left alone.
/// <para/>
/// The file redirection follows the approach of the MIT-licensed VFXEditor code at commit
/// <c>8be61a5</c> (<c>VFXEditor/Interop/ResourceLoader.Replace.cs</c>), which several Dalamud
/// plugins also use. The signatures, the descriptor layout and the stack file interface are
/// theirs. See THIRD-PARTY-NOTICES.md.
/// </remarks>
public sealed unsafe class FontReplacementService : IDisposable
{
    // Signatures used by the MIT-licensed VFXEditor code at commit 8be61a5, and by Penumbra's
    // GameData. ReadSqPack is a file thread entry; ReadFile is the game's own unpacked-file read.
    private const string ReadSqPackSignature = "40 56 41 56 48 83 EC ?? 0F BE 02";
    private const string ReadFileSignature =
        "48 89 5C 24 ?? 48 89 6C 24 ?? 48 89 74 24 ?? 57 41 54 41 55 41 56 41 57 48 81 EC ?? ?? ?? ?? " +
        "48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 84 24 ?? ?? ?? ?? 48 63 42";

    // The atk module slot that unloads and rebuilds the game's fonts. Penumbra's FontReloader
    // calls the same slot. ClientStructs does not name it, so the index is kept here.
    private const int FontReloadVTableIndex = 43;

    private const int FileInterfaceOffset = 0x30;
    private const int ResourceHandleOffset = 0x50;
    private const int FilePathOffset = 0x70;
    private const int FilePathLength = FontPackFiles.MaxLocalPathLength;
    private const int FileInterfacePathOffset = 0x21;

    private const string FontDirectoryName = "fonts";

    private static readonly string[] PenumbraResolveLabels =
    [
        "Penumbra.ResolvePlayerPath",
        "Penumbra.ResolveDefaultPath",
    ];

    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly IGameInteropProvider _interop;
    private readonly ISigScanner _sigScanner;
    private readonly PluginLog _log;
    private readonly string _fontDirectory;

    private Hook<ReadSqPackDelegate>? _readSqPackHook;
    private ReadFileDelegate? _readFile;
    private Dictionary<string, string> _localPaths = new(StringComparer.OrdinalIgnoreCase);
    private bool _requested;
    private string _packPath = string.Empty;
    private bool _disposed;

    public FontReplacementService(
        Configuration configuration,
        IDalamudPluginInterface pluginInterface,
        IGameInteropProvider interop,
        ISigScanner sigScanner,
        IPluginLog log)
    {
        _pluginInterface = pluginInterface;
        _interop = interop;
        _sigScanner = sigScanner;
        _log = new PluginLog(log, configuration, LogCategory.NativeText);
        _fontDirectory = Path.Combine(pluginInterface.GetPluginConfigDirectory(), FontDirectoryName);
    }

    /// <summary>What the settings show. Kept short so it fits beside the path field.</summary>
    public string Status { get; private set; } = "Off.";

    /// <summary>True while the file reader is redirected to a pack.</summary>
    public bool IsActive => _readSqPackHook is not null;

    /// <summary>
    /// Turns the replacement on or off. The call is cheap when the setting did not change, but
    /// it does copy the pack again: a user who replaced the pack file in place expects the new
    /// one to be used.
    /// </summary>
    public void Apply(bool enabled, string? packPath)
    {
        _requested = enabled;
        _packPath = packPath ?? string.Empty;
        ApplyCurrent();
    }

    /// <summary>Reads the pack again and reloads the game font. The settings offer this.</summary>
    public void Reapply() => ApplyCurrent();

    private void ApplyCurrent()
    {
        if (_disposed)
            return;

        if (!_requested)
        {
            Disable("Off.");
            return;
        }

        var source = _packPath.Trim().Trim('"');
        if (source.Length == 0)
        {
            Disable("Choose a .ttmp2 font pack or a folder that holds the font files.");
            return;
        }

        // Penumbra and this service both replace the same files. Let the mod manager win when it
        // already provides a font, so two redirects never fight over one descriptor.
        if (PenumbraSuppliesFonts())
        {
            Disable("Penumbra already replaces the game font. The built-in font is off.");
            return;
        }

        var result = FontPackExtractor.Extract(source, _fontDirectory);
        if (!result.Success)
        {
            Disable(result.Message);
            _log.Warning("Font pack refused: {Reason}", result.Message);
            return;
        }

        if (!InstallHooks(out var error))
        {
            Disable(error);
            _log.Warning("Font pack could not be installed: {Reason}", error);
            return;
        }

        Volatile.Write(ref _localPaths, result.LocalPaths.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.OrdinalIgnoreCase));
        Status = $"Active: {result.LocalPaths.Count} files from {DescribeSource(source)}.";
        _log.Info("Font pack active: {Status}", Status);
        ReloadFonts();
    }

    private void Disable(string status)
    {
        var wasActive = _readSqPackHook is not null;
        Volatile.Write(ref _localPaths, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        _readSqPackHook?.Dispose();
        _readSqPackHook = null;
        _readFile = null;

        if (!string.Equals(Status, status, StringComparison.Ordinal))
            _log.Debug("Font: {Status}", status);

        Status = status;

        if (wasActive)
            ReloadFonts();
    }

    private bool InstallHooks(out string error)
    {
        error = string.Empty;
        if (_readSqPackHook is not null)
            return true;

        var readSqPackAddress = FindSignature(ReadSqPackSignature, out error);
        if (readSqPackAddress == 0)
            return false;

        var readFileAddress = FindSignature(ReadFileSignature, out error);
        if (readFileAddress == 0)
            return false;

        try
        {
            _readFile = Marshal.GetDelegateForFunctionPointer<ReadFileDelegate>(readFileAddress);
            _readSqPackHook = _interop.HookFromAddress<ReadSqPackDelegate>(readSqPackAddress, OnReadSqPack);
            _readSqPackHook.Enable();
        }
        catch (Exception ex)
        {
            _readSqPackHook?.Dispose();
            _readSqPackHook = null;
            _readFile = null;
            error = $"The game's file reader could not be hooked: {ex.Message}";
            return false;
        }

        _log.Debug("Font: redirected the game's file reader at {Address:X}.", readSqPackAddress);
        return true;
    }

    private nint FindSignature(string signature, out string error)
    {
        try
        {
            error = string.Empty;
            return _sigScanner.ScanText(signature);
        }
        catch (Exception ex)
        {
            error = $"This game version's font file reader was not found ({ex.Message}). The font pack is off.";
            return 0;
        }
    }

    /// <summary>
    /// The game's file thread calls this for every resource. It only changes the descriptor for
    /// a font path this service has a file for. A failure never reaches the game: the original
    /// read is used instead.
    /// </summary>
    private byte OnReadSqPack(nint fileHandler, SeFileDescriptor* descriptor, int priority, bool isSync)
    {
        var hook = _readSqPackHook;
        if (hook is null)
            return 0;

        string? localPath = null;
        if (descriptor is not null
            && descriptor->ResourceHandle is not null
            && descriptor->FileMode == FileMode.LoadSqPackResource
            && _readFile is not null)
        {
            try
            {
                var gamePath = ReadGamePath(descriptor->ResourceHandle);
                var localPaths = Volatile.Read(ref _localPaths);
                if (gamePath is not null && localPaths.TryGetValue(gamePath, out var candidate))
                    localPath = candidate;
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Font: could not read a game path; using the game's file.");
            }
        }

        if (localPath is null || !FontPackFiles.FitsDescriptor(localPath) || !File.Exists(localPath))
            return hook.Original(fileHandler, descriptor, priority, isSync);

        WriteUnpackedPath(descriptor, localPath);
        return _readFile!(fileHandler, descriptor, priority, isSync);
    }

    private static string? ReadGamePath(ResourceHandle* handle)
    {
        var bytes = handle->FileName.AsSpan();
        if (bytes.IsEmpty)
            return null;

        var path = Encoding.UTF8.GetString(bytes).TrimEnd('\0');
        return path.Length == 0 ? null : path;
    }

    /// <summary>
    /// Points the descriptor at a file on disk instead of the SqPack. The game reads the wide
    /// path from the descriptor and from the file interface it points at, which is why both are
    /// written. The stack buffer lives for the synchronous read that follows.
    /// </summary>
    private static void WriteUnpackedPath(SeFileDescriptor* descriptor, string localPath)
    {
        descriptor->FileMode = FileMode.LoadUnpackedResource;

        var pathBuffer = new Span<char>(descriptor->FilePath, FilePathLength);
        pathBuffer.Clear();
        localPath.AsSpan().CopyTo(pathBuffer);

        var utf16 = Encoding.Unicode.GetBytes(localPath);
        var interfaceSize = 0x20 + utf16.Length + 0x16;
        var interfaceBuffer = stackalloc byte[interfaceSize];
        var interfaceSpan = new Span<byte>(interfaceBuffer, interfaceSize);
        interfaceSpan.Clear();
        utf16.AsSpan().CopyTo(interfaceSpan[FileInterfacePathOffset..]);
        descriptor->FileInterface = interfaceBuffer;
    }

    /// <summary>
    /// Asks the game to unload and rebuild its fonts, so the new files are read. The game does
    /// the same on a UI shader reload; without the call the old font stays until the client
    /// restarts.
    /// </summary>
    private void ReloadFonts()
    {
        try
        {
            var framework = Framework.Instance();
            var uiModule = framework is null ? null : framework->GetUIModule();
            var raptureAtkModule = uiModule is null ? null : uiModule->GetRaptureAtkModule();
            if (raptureAtkModule is null)
            {
                _log.Debug("Font: the UI module is not ready; the font loads when the game builds it.");
                return;
            }

            var atkModule = (AtkModule*)raptureAtkModule;
            var virtualTable = *(void***)atkModule;
            if (virtualTable is null)
                return;

            var reload = (delegate* unmanaged<AtkModule*, bool, bool, void>)virtualTable[FontReloadVTableIndex];
            if (reload == null)
            {
                _log.Warning("Font: the game's font reload entry is empty.");
                return;
            }

            reload(atkModule, false, true);
            _log.Debug("Font: asked the game to reload its fonts.");
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Font: could not ask the game to reload its fonts.");
        }
    }

    /// <summary>
    /// True when Penumbra resolves one of the font files to a file of its own. The result is
    /// asked once per apply, on the framework thread; the file thread only reads the outcome.
    /// </summary>
    private bool PenumbraSuppliesFonts()
    {
        foreach (var label in PenumbraResolveLabels)
        {
            try
            {
                var subscriber = _pluginInterface.GetIpcSubscriber<string, string>(label);
                if (!subscriber.HasFunction)
                    continue;

                foreach (var gamePath in FontPackFiles.Required)
                {
                    var resolved = subscriber.InvokeFunc(gamePath);
                    if (!string.IsNullOrWhiteSpace(resolved)
                        && !string.Equals(resolved, gamePath, StringComparison.OrdinalIgnoreCase))
                    {
                        _log.Debug(
                            "Font: Penumbra resolves {GamePath} to {Resolved}; leaving the font to Penumbra.",
                            gamePath, resolved);
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                _log.Debug(ex, "Font: the Penumbra {Label} check failed.", label);
            }
        }

        return false;
    }

    private static string DescribeSource(string source)
        => Directory.Exists(source) ? new DirectoryInfo(source).Name : Path.GetFileName(source);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Disable("Off.");
    }

    private delegate byte ReadSqPackDelegate(
        nint fileHandler,
        SeFileDescriptor* descriptor,
        int priority,
        bool isSync);

    private delegate byte ReadFileDelegate(
        nint fileHandler,
        SeFileDescriptor* descriptor,
        int priority,
        bool isSync);

    /// <summary>
    /// The file descriptor the game's file thread reads. Only the fields this service touches are
    /// named; the offsets match ClientStructs' <c>FileDescriptor</c>.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 0x278)]
    private struct SeFileDescriptor
    {
        [FieldOffset(0x00)] public FileMode FileMode;

        /// <summary>A pointer to the file interface the game reads the wide path from.</summary>
        [FieldOffset(FileInterfaceOffset)] public void* FileInterface;

        [FieldOffset(ResourceHandleOffset)] public ResourceHandle* ResourceHandle;

        [FieldOffset(FilePathOffset)] public fixed char FilePath[FilePathLength];
    }
}
