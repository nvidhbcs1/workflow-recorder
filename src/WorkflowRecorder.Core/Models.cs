using System.Text.Json.Serialization;

namespace WorkflowRecorder.Core;

public enum CaptureTargetKind
{
    Window,
    Screen
}

public sealed class RecorderSettings
{
    public string OutputDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WorkflowRecorder",
        "Sessions");

    public bool CaptureScreenshots { get; set; } = true;
    public bool CaptureWindowChanges { get; set; } = true;
    public bool CaptureCursorPaths { get; set; } = true;
    public bool CaptureCommandKeys { get; set; } = true;
    public CaptureTargetKind CaptureTargetKind { get; set; } = CaptureTargetKind.Window;
    public long? TargetWindowHandle { get; set; }
    public string? TargetScreenDeviceName { get; set; }
    public bool RequireTargetWindow { get; set; }
    public int ScreenshotDelayMilliseconds { get; set; } = 350;
    public int CommandKeyScreenshotDelayMilliseconds { get; set; } = 900;
    public int WindowPollMilliseconds { get; set; } = 500;
    public List<string> ExcludedProcesses { get; set; } =
    [
        "WorkflowRecorder.App",
        "WorkflowRecorder.Cli"
    ];
}

public sealed class RecordingSession
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? EndedAtUtc { get; set; }
    public string HostName { get; set; } = Environment.MachineName;
    public string OperatingSystem { get; set; } = Environment.OSVersion.VersionString;
    public string RecorderVersion { get; set; } = "1.0.0";
    public int EventCount { get; set; }
    public RecorderSettings Settings { get; set; } = new();

    [JsonIgnore]
    public string DirectoryPath { get; set; } = string.Empty;
}

public sealed class WorkflowEvent
{
    public int Step { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? Application { get; set; }
    public int? ProcessId { get; set; }
    public string? WindowTitle { get; set; }
    public int? X { get; set; }
    public int? Y { get; set; }
    public string? Shortcut { get; set; }
    public CursorPathInfo? CursorPath { get; set; }
    public UiControlInfo? Control { get; set; }
    public string? Screenshot { get; set; }
    public string? ScreenshotSource { get; set; }
    public string? Note { get; set; }
    public bool Sensitive { get; set; }
}

public sealed class CursorPathInfo
{
    public int StartX { get; set; }
    public int StartY { get; set; }
    public int EndX { get; set; }
    public int EndY { get; set; }
    public int DistancePixels { get; set; }
    public int DurationMilliseconds { get; set; }
}

public sealed class UiControlInfo
{
    public string? Name { get; set; }
    public string? ControlType { get; set; }
    public string? AutomationId { get; set; }
    public string? ClassName { get; set; }
    public string? FrameworkId { get; set; }
    public bool IsPassword { get; set; }
    public RectInfo? Bounds { get; set; }
}

public sealed class WindowContext
{
    public nint Handle { get; set; }
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = "Unknown";
    public string Title { get; set; } = string.Empty;
    public RectInfo Bounds { get; set; } = new();
    public bool IsValid => Handle != 0 && Bounds.Width > 0 && Bounds.Height > 0;
}

public sealed class RectInfo
{
    public int Left { get; set; }
    public int Top { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}
