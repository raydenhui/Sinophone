using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sinophone.Diagnostics;

/// <summary>
/// Writes a case file per lookup that missed or used a loose path. A dev build
/// turns it on; the files are what a person or an agent reads instead of the log.
/// </summary>
/// <remarks>
/// One directory per plugin load, one JSON file per case, and a small index and
/// summary. Repeats update the count instead of writing the same case again.
/// Every write is guarded: a broken folder turns recording off and leaves the
/// translation alone.
/// </remarks>
public sealed class FailureRecorder : IDisposable
{
    public const int MaxCases = 5_000;

    private static readonly JsonSerializerOptions CaseJson = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private static readonly JsonSerializerOptions CompactJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly object _sync = new();
    private readonly FailureLevel _level;
    private readonly Action<string>? _wroteFile;
    private readonly Action<string>? _warning;
    private readonly DateTimeOffset _startedAt = DateTimeOffset.Now;
    private readonly List<FailureCase> _pending = [];
    private readonly HashSet<FailureCase> _dirty = [];
    private readonly Dictionary<string, FailureCase> _casesBySubject = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _repeats = new(StringComparer.Ordinal);
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly Timer _timer;

    private bool _disabled;
    private bool _disposed;
    private bool _warned;

    internal FailureRecorder(
        string root,
        FailureBuildInfo build,
        FailureLevel level = FailureLevel.Fallback,
        Action<string>? wroteFile = null,
        Action<string>? warning = null)
    {
        Root = root;
        Build = build;
        _level = level;
        _wroteFile = wroteFile;
        _warning = warning;

        Directory.CreateDirectory(root);
        SessionDirectory = Path.Combine(root, $"session-{DateTime.Now:yyyy-MM-dd'T'HH-mm-ss}");
        Directory.CreateDirectory(Path.Combine(SessionDirectory, "cases"));
        WriteLatest();

        _timer = new Timer(_ => Flush(), null, 2_000, 2_000);
    }

    /// <summary>The folder holding every session, such as <c>pluginConfigs/Sinophone/failures</c>.</summary>
    public string Root { get; }

    /// <summary>The folder for this plugin load.</summary>
    public string SessionDirectory { get; }

    internal FailureBuildInfo Build { get; }

    public bool Enabled => !_disabled && !_disposed;

    /// <summary>Creates a recorder, or returns null when the folder cannot be prepared.</summary>
    internal static FailureRecorder? TryCreate(
        string root,
        FailureBuildInfo build,
        FailureLevel level,
        Action<string>? wroteFile,
        Action<string>? warning)
    {
        try
        {
            return new FailureRecorder(root, build, level, wroteFile, warning);
        }
        catch (Exception ex)
        {
            warning?.Invoke($"Failure recording is off: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Starts a case for a subject the recorder has not seen. Returns null when the
    /// recorder is off or the subject is empty. A repeat only bumps the count.
    /// </summary>
    public FailureTrace? Begin(string surface, string component, string? subject)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(subject))
            return null;

        var subjectKey = string.Concat(surface, "\u001f", component, "\u001f", subject);

        lock (_sync)
        {
            if (_seen.Add(subjectKey))
                return new FailureTrace(this, subjectKey, surface, component, subject);

            if (_repeats.TryGetValue(subjectKey, out var count))
            {
                _repeats[subjectKey] = count + 1;
                if (_casesBySubject.TryGetValue(subjectKey, out var known))
                    _dirty.Add(known);
            }

            return null;
        }
    }

    internal void Complete(FailureTrace trace)
    {
        if (!Enabled)
            return;

        var record = trace.Case;
        if (record.Approximate && record.Outcome == FailureOutcome.Hit)
            record.Outcome = FailureOutcome.Fallback;

        if (!Keeps(record.Outcome))
        {
            lock (_sync)
                _seen.Remove(trace.SubjectKey);

            return;
        }

        lock (_sync)
        {
            if (_casesBySubject.Count >= MaxCases)
            {
                Disable($"the case limit ({MaxCases}) was reached");
                return;
            }

            _casesBySubject[trace.SubjectKey] = record;
            _repeats[trace.SubjectKey] = 1;
            _pending.Add(record);
        }
    }

    /// <summary>Writes every case queued since the last flush. Safe to call on any thread.</summary>
    public void Flush()
    {
        if (_disposed)
            return;

        var newCases = new List<FailureCase>();
        var rewritten = new List<FailureCase>();

        lock (_sync)
        {
            if (_disabled)
                return;

            newCases.AddRange(_pending);
            _pending.Clear();

            foreach (var record in newCases)
            {
                if (_repeats.TryGetValue(record.SubjectKey, out var count))
                    record.Repeat = count;
            }

            foreach (var record in _dirty)
            {
                if (newCases.Contains(record))
                    continue;

                if (_repeats.TryGetValue(record.SubjectKey, out var count))
                    record.Repeat = count;

                rewritten.Add(record);
            }

            _dirty.Clear();
        }

        if (newCases.Count == 0 && rewritten.Count == 0)
            return;

        try
        {
            Directory.CreateDirectory(Path.Combine(SessionDirectory, "cases"));

            foreach (var record in newCases)
                WriteCase(record, appendIndex: true);

            foreach (var record in rewritten)
                WriteCase(record, appendIndex: false);

            WriteSummary();
        }
        catch (Exception ex)
        {
            Disable($"a file write failed: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Flush();

        lock (_sync)
            _disposed = true;

        _timer.Dispose();
    }

    internal static string MakeId(string subjectKey)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(subjectKey));
        return "c-" + Convert.ToHexString(hash.AsSpan(0, 6)).ToLowerInvariant();
    }

    private bool Keeps(FailureOutcome outcome)
        => _level switch
        {
            FailureLevel.All => true,
            FailureLevel.Miss => outcome is FailureOutcome.Miss or FailureOutcome.Error,
            _ => outcome is FailureOutcome.Miss or FailureOutcome.Error
                or FailureOutcome.Partial or FailureOutcome.Fallback,
        };

    private void WriteCase(FailureCase record, bool appendIndex)
    {
        var path = Path.Combine(SessionDirectory, "cases", record.Id + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(record, CaseJson), Encoding.UTF8);

        if (!appendIndex)
            return;

        var entry = new FailureIndexEntry(
            record.Id,
            record.Time,
            record.Surface,
            record.Outcome,
            record.Reason,
            record.Repeat,
            record.Input.GetValueOrDefault("text"));

        File.AppendAllText(
            Path.Combine(SessionDirectory, "index.jsonl"),
            JsonSerializer.Serialize(entry, CompactJson) + "\n",
            Encoding.UTF8);

        _wroteFile?.Invoke(path);
    }

    private void WriteLatest()
    {
        var latest = new
        {
            session = Path.GetFileName(SessionDirectory),
            path = SessionDirectory,
            startedAt = _startedAt,
        };

        File.WriteAllText(
            Path.Combine(Root, "latest.json"),
            JsonSerializer.Serialize(latest, CaseJson),
            Encoding.UTF8);
    }

    private void WriteSummary()
    {
        List<FailureCase> cases;
        int repeats;

        lock (_sync)
        {
            cases = [.. _casesBySubject.Values];
            repeats = _repeats.Values.Sum();
        }

        var summary = new
        {
            schema = 1,
            startedAt = _startedAt,
            updatedAt = DateTimeOffset.Now,
            cases = cases.Count,
            repeats,
            bySurface = Group(cases, record => record.Surface),
            byOutcome = Group(cases, record => record.Outcome.ToString()),
            byReason = Group(cases, record => record.Reason ?? "(none)"),
        };

        File.WriteAllText(
            Path.Combine(SessionDirectory, "summary.json"),
            JsonSerializer.Serialize(summary, CaseJson),
            Encoding.UTF8);
    }

    private static Dictionary<string, int> Group(
        IEnumerable<FailureCase> cases,
        Func<FailureCase, string> key)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var record in cases)
        {
            var name = key(record);
            counts[name] = counts.GetValueOrDefault(name) + 1;
        }

        return counts;
    }

    private void Disable(string reason)
    {
        lock (_sync)
        {
            if (_disabled)
                return;

            _disabled = true;
        }

        if (_warned)
            return;

        _warned = true;
        _warning?.Invoke($"Failure recording is off: {reason}.");
    }

    private sealed record FailureIndexEntry(
        string Id,
        DateTimeOffset Time,
        string Surface,
        FailureOutcome Outcome,
        string? Reason,
        int Repeat,
        string? Text);
}
