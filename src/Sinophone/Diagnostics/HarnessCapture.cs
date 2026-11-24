#if SINOPHONE_TESTING
using System.Text;
using Dalamud.Interface.Textures;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace Sinophone.Diagnostics;

/// <summary>
/// Takes a PNG of the game screen so a case can be reviewed later.
/// </summary>
/// <remarks>
/// The capture is of the game's own viewport with Dalamud's windows left out, because the
/// question is what the game drew, not what the plugin drew on top of it. Everything here is
/// behind <c>SINOPHONE_TESTING</c>.
/// </remarks>
public sealed class HarnessCapture
{
    private readonly ITextureProvider _textures;
    private readonly ITextureReadbackProvider _readback;
    private readonly PluginLog _log;
    private bool _reportedFailure;

    public HarnessCapture(ITextureProvider textures, ITextureReadbackProvider readback, PluginLog log)
    {
        _textures = textures;
        _readback = readback;
        _log = log;
    }

    /// <summary>Where captures go. Created on first use.</summary>
    public string Directory { get; init; } = string.Empty;

    /// <summary>True when the services needed for a capture are present.</summary>
    public bool IsAvailable => Directory.Length > 0;

    /// <summary>
    /// Grabs the frame and writes it next to the report. Returns the path written, or null when
    /// the capture failed. A failure is logged once; a run should not fill the log with it.
    /// </summary>
    public string? Save(string name)
    {
        if (!IsAvailable)
            return null;

        try
        {
            System.IO.Directory.CreateDirectory(Directory);

            var container = FindPngContainer();
            if (container == Guid.Empty)
            {
                ReportUnavailable("no PNG encoder is available");
                return null;
            }

            var args = new ImGuiViewportTextureArgs
            {
                ViewportId = ImGui.GetMainViewport().ID,
                TakeBeforeImGuiRender = true,
            };

            using var wrap = _textures.CreateFromImGuiViewportAsync(args, name).GetAwaiter().GetResult();

            var path = Path.Combine(Directory, Sanitize(name) + ".png");
            _readback.SaveToFileAsync(wrap, container, path).GetAwaiter().GetResult();
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

    private static string Sanitize(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            builder.Append(char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-');
        }

        return builder.Length == 0 ? "capture" : builder.ToString();
    }
}
#endif
