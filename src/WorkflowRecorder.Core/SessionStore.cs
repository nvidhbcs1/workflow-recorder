using System.Text.Json;
using System.Text.RegularExpressions;

namespace WorkflowRecorder.Core;

public sealed class SessionStore : IDisposable
{
    private readonly object _gate = new();
    private readonly StreamWriter _eventWriter;
    private bool _disposed;

    public RecordingSession Session { get; }
    public string ManifestPath => Path.Combine(Session.DirectoryPath, "session.json");
    public string EventsPath => Path.Combine(Session.DirectoryPath, "events.jsonl");
    public string ScreenshotsDirectory => Path.Combine(Session.DirectoryPath, "screenshots");

    private SessionStore(RecordingSession session)
    {
        Session = session;
        Directory.CreateDirectory(Session.DirectoryPath);
        Directory.CreateDirectory(ScreenshotsDirectory);
        _eventWriter = new StreamWriter(
            new FileStream(EventsPath, FileMode.Append, FileAccess.Write, FileShare.Read),
            new System.Text.UTF8Encoding(false))
        {
            AutoFlush = true
        };
        SaveManifest();
    }

    public static SessionStore Create(string name, RecorderSettings settings)
    {
        var now = DateTimeOffset.UtcNow;
        var safeName = Slug(name);
        var id = $"{now:yyyyMMdd-HHmmss}-{safeName}";
        var directory = Path.Combine(settings.OutputDirectory, id);
        var session = new RecordingSession
        {
            Id = id,
            Name = string.IsNullOrWhiteSpace(name) ? "Recorded workflow" : name.Trim(),
            StartedAtUtc = now,
            Settings = settings,
            DirectoryPath = directory
        };
        return new SessionStore(session);
    }

    public WorkflowEvent Append(WorkflowEvent item)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            Session.EventCount++;
            item.Step = Session.EventCount;
            if (item.TimestampUtc == default)
            {
                item.TimestampUtc = DateTimeOffset.UtcNow;
            }
            _eventWriter.WriteLine(JsonSerializer.Serialize(item, JsonDefaults.CompactOptions));
            SaveManifest();
            return item;
        }
    }

    public string ScreenshotPathForStep(int step, string suffix = "after")
    {
        var file = $"step-{step:0000}-{Slug(suffix)}.png";
        return Path.Combine(ScreenshotsDirectory, file);
    }

    public string RelativeScreenshotPath(string absolutePath) =>
        Path.GetRelativePath(Session.DirectoryPath, absolutePath).Replace('\\', '/');

    public void Complete()
    {
        lock (_gate)
        {
            if (_disposed || Session.EndedAtUtc is not null)
            {
                return;
            }
            Session.EndedAtUtc = DateTimeOffset.UtcNow;
            SaveManifest();
        }
    }

    public static RecordingSession LoadSession(string sessionDirectory)
    {
        var manifest = Path.Combine(sessionDirectory, "session.json");
        var session = JsonSerializer.Deserialize<RecordingSession>(File.ReadAllText(manifest), JsonDefaults.Options)
            ?? throw new InvalidDataException($"Invalid session manifest: {manifest}");
        session.DirectoryPath = Path.GetFullPath(sessionDirectory);
        return session;
    }

    public static IReadOnlyList<WorkflowEvent> LoadEvents(string sessionDirectory)
    {
        var path = Path.Combine(sessionDirectory, "events.jsonl");
        if (!File.Exists(path))
        {
            return [];
        }

        var events = new List<WorkflowEvent>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }
            var item = JsonSerializer.Deserialize<WorkflowEvent>(line, JsonDefaults.CompactOptions);
            if (item is not null)
            {
                events.Add(item);
            }
        }
        return events;
    }

    public static string Slug(string value)
    {
        var cleaned = Regex.Replace(value.Trim().ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(cleaned) ? "workflow" : cleaned[..Math.Min(cleaned.Length, 60)];
    }

    private void SaveManifest()
    {
        var temporary = ManifestPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(Session, JsonDefaults.Options));
        File.Move(temporary, ManifestPath, true);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            Complete();
            _eventWriter.Dispose();
            _disposed = true;
        }
    }
}
