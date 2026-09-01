using System.Collections.Concurrent;

namespace WorkflowRecorder.Core;

public sealed class RecorderEngine : IDisposable
{
    private readonly InputHookService _hooks = new();
    private readonly SemaphoreSlim _eventGate = new(1, 1);
    private readonly ConcurrentBag<Task> _pending = [];
    private System.Threading.Timer? _windowTimer;
    private SessionStore? _store;
    private RecorderSettings _settings = new();
    private nint _lastWindow;
    private nint _pinnedWindow;
    private int _pinnedProcessId;
    private System.Windows.Forms.Screen? _pinnedScreen;
    private int _shortcutRecordingSuspended;
    private bool _disposed;

    public bool IsRecording => _store is not null;
    public RecordingSession? Session => _store?.Session;
    public WindowContext? PinnedWindow => _pinnedWindow == 0 ? null : Win32WindowService.FromHandle(_pinnedWindow);
    public string? CaptureTargetDescription => _pinnedScreen is not null
        ? $"Entire screen — {_pinnedScreen.DeviceName} ({_pinnedScreen.Bounds.Width}×{_pinnedScreen.Bounds.Height})"
        : PinnedWindow is { IsValid: true } window
            ? $"{window.ProcessName} — {window.Title}"
            : null;
    public bool ShortcutRecordingSuspended => Volatile.Read(ref _shortcutRecordingSuspended) != 0;
    public event EventHandler<WorkflowEvent>? EventRecorded;
    public event EventHandler<string>? StatusChanged;
    public event EventHandler? MilestoneNoteRequested;

    public RecorderEngine()
    {
        _hooks.MouseClicked += OnMouseClicked;
        _hooks.MousePathCompleted += OnMousePathCompleted;
        _hooks.ShortcutPressed += OnShortcutPressed;
        _hooks.MilestoneNoteRequested += (_, _) => MilestoneNoteRequested?.Invoke(this, EventArgs.Empty);
    }

    public RecordingSession Start(string name, RecorderSettings settings)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsRecording)
        {
            throw new InvalidOperationException("A recording is already active.");
        }

        _settings = settings;
        SetShortcutRecordingSuspended(false);
        _pinnedScreen = null;
        var isScreenTarget = settings.CaptureTargetKind == CaptureTargetKind.Screen;
        _pinnedWindow = isScreenTarget || settings.TargetWindowHandle is not long handle ? 0 : (nint)handle;
        var target = _pinnedWindow == 0 ? new WindowContext() : Win32WindowService.FromHandle(_pinnedWindow);
        if (isScreenTarget)
        {
            _pinnedScreen = ResolveScreen(settings.TargetScreenDeviceName);
            if (_pinnedScreen is null)
            {
                throw new InvalidOperationException("The selected screen is no longer available. Refresh targets and select it again.");
            }
            settings.TargetScreenDeviceName = _pinnedScreen.DeviceName;
        }
        else if (_pinnedWindow != 0 && !target.IsValid)
        {
            throw new InvalidOperationException("The selected target window is no longer available. Refresh the window list and select it again.");
        }
        if (!isScreenTarget && settings.RequireTargetWindow && !target.IsValid)
        {
            throw new InvalidOperationException("Select a target window before starting the recording.");
        }
        _pinnedProcessId = target.ProcessId;
        _store = SessionStore.Create(name, settings);
        _lastWindow = 0;
        _hooks.Start();
        if (settings.CaptureWindowChanges)
        {
            _windowTimer = new System.Threading.Timer(
                _ => PollWindow(),
                null,
                0,
                Math.Max(200, settings.WindowPollMilliseconds));
        }
        AddAnnotation("Recording started.", !isScreenTarget && target.IsValid);
        if (!string.IsNullOrWhiteSpace(CaptureTargetDescription))
        {
            StatusChanged?.Invoke(this, $"Capturing: {CaptureTargetDescription}");
        }
        StatusChanged?.Invoke(this, $"Recording to {_store.Session.DirectoryPath}");
        return _store.Session;
    }

    public async Task<RecordingSession?> StopAsync()
    {
        if (_store is null)
        {
            return null;
        }

        SetShortcutRecordingSuspended(false);
        _hooks.Stop();
        if (_windowTimer is not null)
        {
            await _windowTimer.DisposeAsync();
        }
        _windowTimer = null;
        await DrainPendingAsync();
        await AppendAsync(new WorkflowEvent
        {
            Type = "session",
            Action = "stop",
            Note = "Recording stopped."
        }, false);

        var session = _store.Session;
        _store.Complete();
        _store.Dispose();
        _store = null;
        StatusChanged?.Invoke(this, $"Saved {session.EventCount} events to {session.DirectoryPath}");
        return session;
    }

    public void AddAnnotation(string note, bool captureScreenshot = true)
    {
        if (_store is null)
        {
            throw new InvalidOperationException("No recording is active.");
        }
        Queue(AppendAsync(new WorkflowEvent
        {
            Type = "annotation",
            Action = "note",
            Note = note,
            TimestampUtc = DateTimeOffset.UtcNow
        }, captureScreenshot));
    }

    public void SetShortcutRecordingSuspended(bool suspended) =>
        Volatile.Write(ref _shortcutRecordingSuspended, suspended ? 1 : 0);

    public WindowContext PinTargetWindow(nint handle)
    {
        var window = Win32WindowService.FromHandle(handle);
        if (!window.IsValid)
        {
            throw new InvalidOperationException("The selected target window is no longer available.");
        }
        _pinnedWindow = window.Handle;
        _pinnedProcessId = window.ProcessId;
        _pinnedScreen = null;
        _settings.CaptureTargetKind = CaptureTargetKind.Window;
        _settings.TargetWindowHandle = (long)window.Handle;
        StatusChanged?.Invoke(this, $"Target locked: {window.ProcessName} — {window.Title}");
        return window;
    }

    public async Task AddAnnotationWithScreenshotAsync(string note, string sourceImagePath, string? application = null, string? windowTitle = null)
    {
        var store = _store ?? throw new InvalidOperationException("No recording is active.");
        var fullSourcePath = Path.GetFullPath(sourceImagePath);
        if (!File.Exists(fullSourcePath))
        {
            throw new FileNotFoundException("The supplied screenshot was not found.", fullSourcePath);
        }

        await _eventGate.WaitAsync();
        try
        {
            var step = store.Session.EventCount + 1;
            var destination = store.ScreenshotPathForStep(step, "provided");
            using (var source = System.Drawing.Image.FromFile(fullSourcePath))
            using (var normalized = new System.Drawing.Bitmap(source))
            {
                normalized.Save(destination, System.Drawing.Imaging.ImageFormat.Png);
            }
            var target = ResolveWindow(null);
            var item = new WorkflowEvent
            {
                Type = "annotation",
                Action = "note",
                Note = note,
                Application = application ?? (target.IsValid ? target.ProcessName : null),
                ProcessId = target.ProcessId == 0 ? null : target.ProcessId,
                WindowTitle = Truncate(windowTitle ?? target.Title, 500),
                Screenshot = store.RelativeScreenshotPath(destination),
                ScreenshotSource = "provided-image",
                TimestampUtc = DateTimeOffset.UtcNow
            };
            store.Append(item);
            EventRecorded?.Invoke(this, item);
        }
        finally
        {
            _eventGate.Release();
        }
    }

    private void OnMouseClicked(object? sender, MouseClickEvent click)
    {
        if (_store is null)
        {
            return;
        }
        Queue(RecordMouseAsync(click));
    }

    private void OnShortcutPressed(object? sender, ShortcutEvent shortcut)
    {
        if (_store is null)
        {
            return;
        }
        if (ShortcutRecordingSuspended)
        {
            return;
        }
        if (!_settings.CaptureCommandKeys)
        {
            return;
        }
        Queue(AppendAsync(new WorkflowEvent
        {
            Type = "keyboard",
            Action = shortcut.Kind,
            Shortcut = shortcut.Shortcut,
            TimestampUtc = shortcut.TimestampUtc
        }, true));
    }

    private void OnMousePathCompleted(object? sender, MousePathEvent path)
    {
        if (_store is null || !_settings.CaptureCursorPaths)
        {
            return;
        }
        Queue(RecordCursorPathAsync(path));
    }

    private async Task RecordMouseAsync(MouseClickEvent click)
    {
        var window = Win32WindowService.AtPoint(click.X, click.Y);
        if (!window.IsValid || IsExcluded(window) || !MatchesPinnedTarget(window, click.X, click.Y))
        {
            return;
        }

        var control = UiAutomationInspector.FromPoint(click.X, click.Y);
        var sensitive = control?.IsPassword == true;
        await AppendAsync(new WorkflowEvent
        {
            Type = "mouse",
            Action = $"{click.Button}-click",
            X = click.X,
            Y = click.Y,
            TimestampUtc = click.TimestampUtc,
            Control = sensitive
                ? new UiControlInfo { ControlType = control?.ControlType, IsPassword = true }
                : control,
            Sensitive = sensitive
        }, !sensitive, window);
    }

    private async Task RecordCursorPathAsync(MousePathEvent path)
    {
        var window = Win32WindowService.AtPoint(path.EndX, path.EndY);
        if (!window.IsValid || IsExcluded(window) || !MatchesPinnedTarget(window, path.EndX, path.EndY))
        {
            return;
        }

        await AppendAsync(new WorkflowEvent
        {
            Type = "pointer",
            Action = "move",
            X = path.EndX,
            Y = path.EndY,
            TimestampUtc = path.TimestampUtc,
            CursorPath = new CursorPathInfo
            {
                StartX = path.StartX,
                StartY = path.StartY,
                EndX = path.EndX,
                EndY = path.EndY,
                DistancePixels = path.DistancePixels,
                DurationMilliseconds = path.DurationMilliseconds
            }
        }, false, window);
    }

    private void PollWindow()
    {
        if (_store is null)
        {
            return;
        }
        var window = Win32WindowService.Foreground();
        if (!window.IsValid || window.Handle == _lastWindow || IsExcluded(window) || !MatchesPinnedTarget(window))
        {
            return;
        }
        _lastWindow = window.Handle;
        Queue(AppendAsync(new WorkflowEvent
        {
            Type = "window",
            Action = "focus",
            TimestampUtc = DateTimeOffset.UtcNow
        }, false, window));
    }

    private async Task AppendAsync(WorkflowEvent item, bool captureScreenshot, WindowContext? suppliedWindow = null)
    {
        var store = _store;
        if (store is null)
        {
            return;
        }

        await _eventGate.WaitAsync();
        try
        {
            var window = ResolveWindow(suppliedWindow);
            var isRecorderAnnotation = _pinnedScreen is not null && suppliedWindow is null && item.Type is "annotation" or "session";
            if ((IsExcluded(window) && !isRecorderAnnotation) || (suppliedWindow is not null && !MatchesPinnedTarget(window)))
            {
                return;
            }
            if (!IsExcluded(window))
            {
                item.Application ??= window.ProcessName;
                item.ProcessId ??= window.ProcessId == 0 ? null : window.ProcessId;
                item.WindowTitle ??= Truncate(window.Title, 500);
            }

            var predictedStep = store.Session.EventCount + 1;
            if (captureScreenshot && _settings.CaptureScreenshots && !item.Sensitive)
            {
                var delay = GetScreenshotDelayMilliseconds(_settings, item);
                await Task.Delay(Math.Max(0, delay));
                window = ResolveWindow(suppliedWindow);
                var screenshotPath = store.ScreenshotPathForStep(predictedStep);
                var capture = _pinnedScreen is null
                    ? ScreenCaptureService.CaptureWindow(window, screenshotPath, item.X, item.Y, predictedStep)
                    : ScreenCaptureService.CaptureScreen(ToRectInfo(_pinnedScreen), screenshotPath, item.X, item.Y, predictedStep);
                if (capture.Success)
                {
                    item.Screenshot = store.RelativeScreenshotPath(screenshotPath);
                    item.ScreenshotSource = capture.Source;
                }
                else if (!string.IsNullOrWhiteSpace(capture.Error))
                {
                    StatusChanged?.Invoke(this, $"Screenshot skipped: {capture.Error}");
                }
            }

            store.Append(item);
            EventRecorded?.Invoke(this, item);
        }
        finally
        {
            _eventGate.Release();
        }
    }

    private WindowContext ResolveWindow(WindowContext? suppliedWindow)
    {
        if (suppliedWindow is not null)
        {
            return suppliedWindow;
        }
        return _pinnedWindow == 0
            ? Win32WindowService.Foreground()
            : Win32WindowService.FromHandle(_pinnedWindow);
    }

    public static int GetScreenshotDelayMilliseconds(RecorderSettings settings, WorkflowEvent item) =>
        item.Type == "keyboard" && (item.Action is "command-key" or "window-switch")
            ? settings.CommandKeyScreenshotDelayMilliseconds
            : settings.ScreenshotDelayMilliseconds;

    private bool MatchesPinnedTarget(WindowContext window, int? x = null, int? y = null)
    {
        if (_pinnedScreen is not null)
        {
            var bounds = _pinnedScreen.Bounds;
            if (x is not null && y is not null)
            {
                return x.Value >= bounds.Left && x.Value < bounds.Right && y.Value >= bounds.Top && y.Value < bounds.Bottom;
            }
            var centerX = window.Bounds.Left + window.Bounds.Width / 2;
            var centerY = window.Bounds.Top + window.Bounds.Height / 2;
            return centerX >= bounds.Left && centerX < bounds.Right && centerY >= bounds.Top && centerY < bounds.Bottom;
        }
        return _pinnedWindow == 0 || (_pinnedProcessId != 0 && window.ProcessId == _pinnedProcessId);
    }

    private static System.Windows.Forms.Screen? ResolveScreen(string? deviceName) =>
        string.IsNullOrWhiteSpace(deviceName)
            ? null
            : System.Windows.Forms.Screen.AllScreens.FirstOrDefault(screen =>
                string.Equals(screen.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase));

    private static RectInfo ToRectInfo(System.Windows.Forms.Screen screen) => new()
    {
        Left = screen.Bounds.Left,
        Top = screen.Bounds.Top,
        Width = screen.Bounds.Width,
        Height = screen.Bounds.Height
    };

    private bool IsExcluded(WindowContext window) =>
        window.IsValid && _settings.ExcludedProcesses.Any(
            excluded => string.Equals(excluded, window.ProcessName, StringComparison.OrdinalIgnoreCase));

    private void Queue(Task task)
    {
        _pending.Add(task);
        _ = task.ContinueWith(
            completed =>
            {
                if (completed.Exception is not null)
                {
                    StatusChanged?.Invoke(this, completed.Exception.GetBaseException().Message);
                }
            },
            TaskScheduler.Default);
    }

    private async Task DrainPendingAsync()
    {
        var tasks = _pending.Where(task => !task.IsCompleted).ToArray();
        if (tasks.Length > 0)
        {
            await Task.WhenAll(tasks);
        }
    }

    private static string? Truncate(string? value, int length)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        return value[..Math.Min(length, value.Length)];
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        if (IsRecording)
        {
            StopAsync().GetAwaiter().GetResult();
        }
        _windowTimer?.Dispose();
        _hooks.Dispose();
        _eventGate.Dispose();
        _disposed = true;
    }
}
