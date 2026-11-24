#if SINOPHONE_TESTING
using System.Text;

namespace Sinophone.Diagnostics;

/// <summary>
/// Takes a PNG of the game screen so a case can be reviewed later.
/// </summary>
/// <remarks>
/// Nothing here blocks the game thread. Capturing a viewport finishes a frame later, so
/// <see cref="Request"/> only starts the work and <see cref="Pump"/> collects it. That means a
/// capture shows the frame after the case settled, which is the same line while a surface holds
/// it. A capture never fails a case; the first failure is logged and captures stay off.
/// </remarks>
public sealed class HarnessCapture
{
    private readonly ITextureProvider _textures;
    private readonly ITextureReadbackProvider _readback;
    private readonly PluginLog _log;
    private readonly Queue<Task<string?>> _pending = new();
    private readonly List<string> _captured = [];
    private bool _reportedFailure;

    public HarnessCapture(ITextureProvider textures, ITextureReadbackProvider readback, PluginLog log)
    {
        _textures = textures;
        _readback = readback;
        _log = log;
    }

    /// <summary>Where captures go. Created on first use. Empty disables capturing.</summary>
    public string Directory { get; init; } = string.Empty;

    /// <summary>True when the folder is set, so a capture can be requested.</summary>
    public bool IsAvailable => Directory.Length > 0;

    /// <summary>Paths written so far.</summary>
    public IReadOnlyList<string> Captured => _captured;

    /// <summary>Captures still in flight.</summary>
    public int PendingCount => _pending.Count;

    /// <summary>
    /// Starts a capture of the current frame and saves it as <paramref name="name"/>. Returns at
    /// once; the file appears after the next render.
    /// </summary>
    public void Request(string name)
    {
        if (!IsAvailable)
            return;

        try
        {
            System.IO.Directory.CreateDirectory(Directory);

            var container = FindPngContainer();
            if (container == Guid.Empty)
            {
                ReportUnavailable("no PNG encoder is available");
                return;
            }

            var args = new ImGuiViewportTextureArgs
            {
                ViewportId = ImGui.GetMainViewport().ID,
                TakeBeforeImGuiRender = true,
            };

            _pending.Enqueue(CaptureAndSave(args, container, Path.Combine(Directory, Sanitize(name) + ".png")));
        }
        catch (Exception ex)
        {
            ReportUnavailable(ex.Message);
        }
    }

    /// <summary>Collects finished captures. Call once per frame.</summary>
    public void Pump()
    {
        while (_pending.Count > 0 && _pending.Peek().IsCompleted)
        {
            var task = _pending.Dequeue();

            if (task.Status == TaskStatus.RanToCompletion && task.Result is { } path)
                _captured.Add(path);
        }
    }

    private async Task<string?> CaptureAndSave(ImGuiViewportTextureArgs args, Guid container, string path)
    {
        try
        {
            using var wrap = await _textures.CreateFromImGuiViewportAsync(args).ConfigureAwait(false);
            await _readback.SaveToFileAsync(wrap, container, path).ConfigureAwait(false);
            return path;
        }
        catch (Exception ex)
        {
            ReportUnavailable(ex.Message);
            return null;
        }
    }

    private Guid FindPngContainer()
    {
        foreach (var codec in _readback.GetSupportedImageEncoderInfos())
        {
            if (codec.Extensions.Any(static extension =>
                    extension.Equals(".png", StringComparison.OrdinalIgnoreCase)))
            {
                return codec.ContainerGuid;
            }
        }

        return Guid.Empty;
    }

    private void ReportUnavailable(string reason)
    {
        if (_reportedFailure)
            return;

        _reportedFailure = true;
        _log.Warning("Testing tab: could not capture the screen ({Reason}); captures are off.", reason);
    }

    private static string Sanitize(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var ch in name)
            builder.Append(char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-');

        return builder.Length == 0 ? "capture" : builder.ToString();
    }
}
#endif
