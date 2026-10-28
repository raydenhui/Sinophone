using System.Net.Http;
using System.Security.Cryptography;

namespace Sinophone.Data;

/// <summary>
/// Snapshot of the updater's public state. Replaced as a whole so the UI can read
/// it from the draw thread while the download runs in the background.
/// </summary>
public sealed record DataUpdateState(
    bool IsBusy,
    string Status,
    float Progress,
    bool HasUpdate,
    string? LastError);

/// <summary>
/// Downloads the generated translation artifacts published by the repository's
/// data workflow and installs them into the plugin config directory.
/// </summary>
/// <remarks>
/// The download is validated before anything is replaced: the gzip must
/// decompress to an artifact with a supported format version and at least one
/// sheet. Files are written next to the destination first and moved into place
/// only after validation, so a failed or cancelled download never leaves the
/// plugin without usable data.
/// </remarks>
public sealed class TranslationDataUpdater : IDisposable
{
    private const string ManifestFileName = "manifest.json";

    private const string ReleaseBaseUrl = "https://github.com/raydenhui/Sinophone/releases/download/data/";

    private static readonly HttpClient Client = CreateClient();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly Configuration _configuration;
    private readonly string _dataDirectory;
    private readonly IPluginLog _log;
    private readonly object _gate = new();

    private DataUpdateState _state = new(false, "Not checked yet.", 0f, false, null);
    private CancellationTokenSource? _cts;
    private volatile bool _reloadRequested;
    private bool _disposed;

    public TranslationDataUpdater(Configuration configuration, string dataDirectory, IPluginLog log)
    {
        _configuration = configuration;
        _dataDirectory = dataDirectory;
        _log = log;
    }

    public DataUpdateState State => Volatile.Read(ref _state);

    public string DataFilePath(ChineseVariant variant)
        => Path.Combine(
            _dataDirectory,
            variant == ChineseVariant.Traditional
                ? TranslationStore.ResourceTcSuffix
                : TranslationStore.ResourceCnSuffix);

    /// <summary>
    /// Opens the downloaded artifact for a variant, falling back to the copy
    /// embedded in the assembly when the file is missing or unreadable.
    /// </summary>
    public Stream? OpenData(ChineseVariant variant)
    {
        var path = DataFilePath(variant);
        var fileExists = File.Exists(path);

        if (!ShouldUseDownloadedFile(variant, fileExists))
        {
            if (fileExists)
                _log.Debug("Ignoring {Variant} data because it is not the installed version.", variant);

            return TranslationStore.OpenEmbeddedResource(variant);
        }

        try
        {
            return File.OpenRead(path);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Could not open downloaded data at {Path}; using the embedded copy.", path);
        }

        return TranslationStore.OpenEmbeddedResource(variant);
    }

    /// <summary>
    /// True when the file on disk came from the last completed update. A file left
    /// behind by an older version must not be mixed with the current one, which can
    /// happen once downloading both variants is turned off.
    /// </summary>
    internal bool ShouldUseDownloadedFile(ChineseVariant variant, bool fileExists)
        => fileExists && IsInstalled(variant);

    /// <summary>True when a file the current settings need is not on disk.</summary>
    public bool IsMissingRequiredFile()
        => RequiredVariants().Any(variant => !File.Exists(DataFilePath(variant)));

    /// <summary>Checks the release manifest without downloading anything.</summary>
    public void StartCheck() => Start("Checking for updates...", CheckAsync);

    /// <summary>Downloads the artifacts if the published version differs from the installed one.</summary>
    public void StartUpdate() => Start("Preparing to download...", UpdateAsync);

    public void Cancel()
    {
        lock (_gate)
        {
            _cts?.Cancel();
        }
    }

    /// <summary>
    /// Returns true once after new data has been installed, so the caller can
    /// rebuild the store on the game thread.
    /// </summary>
    public bool ConsumeReloadRequest()
    {
        // Called every frame, so the common case must not take the lock.
        if (!_reloadRequested)
            return false;

        lock (_gate)
        {
            if (!_reloadRequested)
                return false;

            _reloadRequested = false;
            return true;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;

            // A running operation disposes its own source in RunAsync.
            _cts?.Cancel();
        }
    }

    /// <summary>
    /// The variants that should be on disk: the configured one, plus the other one
    /// when the user downloads both.
    /// </summary>
    internal static IReadOnlyList<ChineseVariant> RequiredVariants(ChineseVariant variant, bool downloadBoth)
        => downloadBoth ? [variant, Other(variant)] : [variant];

    /// <summary>True when the file on disk is the published version and usable.</summary>
    internal static bool IsCurrent(
        string? installedVersion,
        string publishedVersion,
        bool hasFlag,
        bool fileExists)
        => hasFlag
           && fileExists
           && !string.IsNullOrWhiteSpace(publishedVersion)
           && string.Equals(installedVersion ?? string.Empty, publishedVersion, StringComparison.Ordinal);

    internal static bool TryValidateArtifact(string path, out string error)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return TryValidateArtifact(stream, out error);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    internal static bool TryValidateArtifact(Stream stream, out string error)
    {
        error = string.Empty;

        try
        {
            using var gzip = new GZipStream(stream, CompressionMode.Decompress, leaveOpen: true);
            var artifact = JsonSerializer.Deserialize<TranslationArtifact>(gzip);

            if (artifact is null)
            {
                error = "the artifact is empty";
                return false;
            }

            if (artifact.FormatVersion != 1)
            {
                error = $"unsupported format version {artifact.FormatVersion}";
                return false;
            }

            if (artifact.Sheets.Count == 0)
            {
                error = "the artifact contains no sheets";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private void Start(string status, Func<CancellationToken, Task> work)
    {
        CancellationToken token;
        CancellationTokenSource source;

        lock (_gate)
        {
            if (_disposed || _state.IsBusy)
                return;

            source = new CancellationTokenSource();
            _cts = source;
            token = source.Token;

            _state = _state with
            {
                IsBusy = true,
                Status = status,
                Progress = 0f,
                LastError = null,
            };
        }

        _ = Task.Run(() => RunAsync(work, source, token), CancellationToken.None);
    }

    private async Task RunAsync(
        Func<CancellationToken, Task> work,
        CancellationTokenSource source,
        CancellationToken token)
    {
        try
        {
            await work(token).ConfigureAwait(false);
            SetState(s => s with { IsBusy = false, Progress = s.HasUpdate ? 0f : 1f });
        }
        catch (OperationCanceledException)
        {
            SetState(s => s with { IsBusy = false, Progress = 0f, Status = "Download cancelled." });
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Translation data update failed.");
            SetState(s => s with
            {
                IsBusy = false,
                Progress = 0f,
                Status = "Update failed. Existing data was kept.",
                LastError = ex.Message,
            });
        }
        finally
        {
            lock (_gate)
            {
                // Only clear the field if a later run has not replaced it.
                if (ReferenceEquals(_cts, source))
                    _cts = null;
            }

            source.Dispose();
        }
    }

    private async Task CheckAsync(CancellationToken token)
    {
        var manifest = await GetManifestAsync(token).ConfigureAwait(false);

        if (manifest is null)
        {
            SetState(s => s with { Status = "Could not reach the update server.", Progress = 0f });
            return;
        }

        var available = NeedsUpdate(manifest);

        SetState(s => s with
        {
            HasUpdate = available,
            Progress = 0f,
            Status = available
                ? "An update is available."
                : "Translation data is up to date.",
        });
    }

    private async Task UpdateAsync(CancellationToken token)
    {
        var manifest = await GetManifestAsync(token).ConfigureAwait(false)
                       ?? throw new InvalidOperationException("Could not download the data manifest.");

        if (manifest.FormatVersion != 1)
            throw new InvalidOperationException($"Unsupported data format version {manifest.FormatVersion}.");

        if (!NeedsUpdate(manifest))
        {
            SetState(s => s with
            {
                HasUpdate = false,
                Progress = 0f,
                Status = "Translation data is already up to date.",
            });
            return;
        }

        // A file from an older version is not current until it is downloaded again.
        if (!string.Equals(_configuration.LastDataVersion, manifest.Version, StringComparison.Ordinal))
        {
            _configuration.HasSimplifiedData = false;
            _configuration.HasTraditionalData = false;
        }

        var pending = RequiredVariants()
            .Where(variant => !IsCurrent(variant, manifest.Version))
            .ToList();

        Directory.CreateDirectory(_dataDirectory);

        for (var i = 0; i < pending.Count; i++)
        {
            token.ThrowIfCancellationRequested();

            var variant = pending[i];

            SetState(s => s with
            {
                Progress = 0.1f + 0.8f * i / pending.Count,
                Status = $"Downloading {Describe(variant)} data...",
            });

            var fileName = variant == ChineseVariant.Traditional
                ? TranslationStore.ResourceTcSuffix
                : TranslationStore.ResourceCnSuffix;

            var checksum = variant == ChineseVariant.Traditional
                ? manifest.TcSha256
                : manifest.CnSha256;

            await DownloadArtifactAsync(fileName, checksum, DataFilePath(variant), token)
                .ConfigureAwait(false);

            if (variant == ChineseVariant.Traditional)
                _configuration.HasTraditionalData = true;
            else
                _configuration.HasSimplifiedData = true;
        }

        // The caller persists the configuration on the game thread when it
        // handles the reload request; config writes stay off this worker.
        _configuration.LastDataVersion = manifest.Version;

        lock (_gate)
        {
            _reloadRequested = true;
        }

        _log.Info(
            "Installed translation data version {Version} ({Variants}).",
            manifest.Version,
            string.Join(", ", pending));

        SetState(s => s with
        {
            HasUpdate = false,
            Progress = 1f,
            Status = "Translation data updated. Reloading...",
        });
    }

    private async Task DownloadArtifactAsync(
        string fileName,
        string? expectedSha256,
        string destination,
        CancellationToken token)
    {
        var temporaryPath = destination + ".download";

        try
        {
            using var response = await Client
                .GetAsync(ReleaseBaseUrl + fileName, HttpCompletionOption.ResponseHeadersRead, token)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            // The file handle has to be closed before it can be validated and moved.
            {
                await using var remote = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                await using var file = File.Create(temporaryPath);
                await remote.CopyToAsync(file, token).ConfigureAwait(false);
            }

            if (!string.IsNullOrWhiteSpace(expectedSha256))
            {
                var actual = ComputeSha256(temporaryPath);
                if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"{fileName} did not match its published checksum.");
            }

            if (!TryValidateArtifact(temporaryPath, out var error))
                throw new InvalidDataException($"{fileName} is not usable: {error}.");

            File.Move(temporaryPath, destination, overwrite: true);
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    private async Task<TranslationDataManifest?> GetManifestAsync(CancellationToken token)
    {
        try
        {
            using var response = await Client
                .GetAsync(ReleaseBaseUrl + ManifestFileName, token)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _log.Warning("Data manifest request returned {Status}.", response.StatusCode);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            return await JsonSerializer
                .DeserializeAsync<TranslationDataManifest>(stream, JsonOptions, token)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            if (token.IsCancellationRequested)
                throw;

            _log.Warning(ex, "Could not download the data manifest.");
            return null;
        }
    }

    private IReadOnlyList<ChineseVariant> RequiredVariants()
        => RequiredVariants(_configuration.Variant, _configuration.DownloadBothVariants);

    private bool NeedsUpdate(TranslationDataManifest manifest)
        => RequiredVariants().Any(variant => !IsCurrent(variant, manifest.Version));

    private bool IsCurrent(ChineseVariant variant, string publishedVersion)
        => IsCurrent(
            _configuration.LastDataVersion,
            publishedVersion,
            HasInstalledFlag(variant),
            File.Exists(DataFilePath(variant)));

    /// <summary>True when this variant was installed by the last completed update.</summary>
    private bool IsInstalled(ChineseVariant variant)
        => HasInstalledFlag(variant) && !string.IsNullOrWhiteSpace(_configuration.LastDataVersion);

    private bool HasInstalledFlag(ChineseVariant variant)
        => variant == ChineseVariant.Traditional
            ? _configuration.HasTraditionalData
            : _configuration.HasSimplifiedData;

    private static ChineseVariant Other(ChineseVariant variant)
        => variant == ChineseVariant.Traditional ? ChineseVariant.Simplified : ChineseVariant.Traditional;

    private static string Describe(ChineseVariant variant)
        => variant == ChineseVariant.Traditional ? "traditional Chinese" : "simplified Chinese";

    private void SetState(Func<DataUpdateState, DataUpdateState> update)
    {
        lock (_gate)
        {
            _state = update(_state);
        }
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // A leftover temporary file is harmless; the next download overwrites it.
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Sinophone/0.1 (+https://github.com/raydenhui/Sinophone)");
        return client;
    }

    private sealed class TranslationDataManifest
    {
        public int FormatVersion { get; set; } = 1;

        public string Version { get; set; } = string.Empty;

        public string GeneratedAt { get; set; } = string.Empty;

        public string CnSha256 { get; set; } = string.Empty;

        public string TcSha256 { get; set; } = string.Empty;
    }
}
