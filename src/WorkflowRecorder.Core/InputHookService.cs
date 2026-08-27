using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WorkflowRecorder.Core;

public sealed record MouseClickEvent(int X, int Y, string Button, DateTimeOffset TimestampUtc);
public sealed record MousePathEvent(int StartX, int StartY, int EndX, int EndY, int DistancePixels, int DurationMilliseconds, DateTimeOffset TimestampUtc);
public sealed record ShortcutEvent(string Shortcut, string Kind, DateTimeOffset TimestampUtc);

public sealed class InputHookService : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WhMouseLl = 14;
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyUp = 0x0105;
    private const int WmLButtonDown = 0x0201;
    private const int WmRButtonDown = 0x0204;
    private const int WmMButtonDown = 0x0207;
    private const int WmMouseMove = 0x0200;
    private const uint WmQuit = 0x0012;
    private const uint PmNoRemove = 0x0000;

    private readonly HashSet<int> _pressed = [];
    private readonly HookProc _mouseProc;
    private readonly HookProc _keyboardProc;
    private readonly ManualResetEventSlim _hookReady = new(false);
    private PointerSample? _pathStart;
    private PointerSample? _pathLast;
    private double _pathDistance;
    private nint _mouseHook;
    private nint _keyboardHook;
    private Thread? _hookThread;
    private uint _hookThreadId;
    private Exception? _startFailure;
    private bool _disposed;

    public event EventHandler<MouseClickEvent>? MouseClicked;
    public event EventHandler<MousePathEvent>? MousePathCompleted;
    public event EventHandler<ShortcutEvent>? ShortcutPressed;

    public InputHookService()
    {
        _mouseProc = MouseCallback;
        _keyboardProc = KeyboardCallback;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_hookThread is not null)
        {
            return;
        }

        _startFailure = null;
        _hookReady.Reset();
        _hookThread = new Thread(RunHookLoop)
        {
            IsBackground = true,
            Name = "Workflow Recorder input hooks"
        };
        _hookThread.Start();

        if (!_hookReady.Wait(TimeSpan.FromSeconds(5)))
        {
            Stop();
            throw new TimeoutException("Timed out while starting Windows input hooks.");
        }
        if (_startFailure is not null)
        {
            var failure = _startFailure;
            Stop();
            throw new InvalidOperationException("Unable to install Windows input hooks.", failure);
        }
    }

    public void Stop()
    {
        var thread = _hookThread;
        var threadId = _hookThreadId;
        if (thread is not null && threadId != 0)
        {
            PostThreadMessage(threadId, WmQuit, 0, 0);
        }
        if (thread is not null && thread != Thread.CurrentThread)
        {
            thread.Join(TimeSpan.FromSeconds(2));
        }
        _hookThread = null;
        _pressed.Clear();
        _pathStart = null;
        _pathLast = null;
        _pathDistance = 0;
    }

    private void RunHookLoop()
    {
        try
        {
            _hookThreadId = GetCurrentThreadId();
            // Calling PeekMessage first creates this thread's message queue before Stop can post WM_QUIT.
            PeekMessage(out _, 0, 0, 0, PmNoRemove);

            using var process = Process.GetCurrentProcess();
            using var module = process.MainModule;
            var moduleHandle = GetModuleHandle(module?.ModuleName);
            _mouseHook = SetWindowsHookEx(WhMouseLl, _mouseProc, moduleHandle, 0);
            _keyboardHook = SetWindowsHookEx(WhKeyboardLl, _keyboardProc, moduleHandle, 0);
            if (_mouseHook == 0 || _keyboardHook == 0)
            {
                throw new InvalidOperationException($"Win32 error: {Marshal.GetLastWin32Error()}");
            }

            _hookReady.Set();
            while (GetMessage(out var message, 0, 0, 0) > 0)
            {
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }
        }
        catch (Exception error)
        {
            _startFailure = error;
            _hookReady.Set();
        }
        finally
        {
            if (_mouseHook != 0)
            {
                UnhookWindowsHookEx(_mouseHook);
                _mouseHook = 0;
            }
            if (_keyboardHook != 0)
            {
                UnhookWindowsHookEx(_keyboardHook);
                _keyboardHook = 0;
            }
            _hookThreadId = 0;
            _hookReady.Set();
        }
    }

    private nint MouseCallback(int code, nint message, nint parameter)
    {
        if (code >= 0)
        {
            var msg = unchecked((int)(long)message);
            var data = Marshal.PtrToStructure<MouseHookStruct>(parameter);
            var timestamp = DateTimeOffset.UtcNow;
            if (msg == WmMouseMove)
            {
                TrackPointer(data.Point.X, data.Point.Y, timestamp);
            }
            var button = msg switch
            {
                WmLButtonDown => "left",
                WmRButtonDown => "right",
                WmMButtonDown => "middle",
                _ => null
            };
            if (button is not null)
            {
                CompletePointerPath(data.Point.X, data.Point.Y, timestamp);
                MouseClicked?.Invoke(this, new MouseClickEvent(data.Point.X, data.Point.Y, button, timestamp));
            }
        }
        return CallNextHookEx(_mouseHook, code, message, parameter);
    }

    private nint KeyboardCallback(int code, nint message, nint parameter)
    {
        if (code >= 0)
        {
            var msg = unchecked((int)(long)message);
            var data = Marshal.PtrToStructure<KeyboardHookStruct>(parameter);
            var vk = unchecked((int)data.VirtualKey);
            if (msg is WmKeyUp or WmSysKeyUp)
            {
                _pressed.Remove(vk);
            }
            else if ((msg is WmKeyDown or WmSysKeyDown) && _pressed.Add(vk))
            {
                var shortcut = DescribeShortcut((Keys)vk, DateTimeOffset.UtcNow);
                if (shortcut is not null)
                {
                    ShortcutPressed?.Invoke(this, shortcut);
                }
            }
        }
        return CallNextHookEx(_keyboardHook, code, message, parameter);
    }

    private void TrackPointer(int x, int y, DateTimeOffset timestamp)
    {
        var next = new PointerSample(x, y, timestamp);
        if (_pathLast is not null)
        {
            _pathDistance += Math.Sqrt(Math.Pow(next.X - _pathLast.X, 2) + Math.Pow(next.Y - _pathLast.Y, 2));
        }
        else
        {
            _pathStart = next;
        }
        _pathLast = next;
    }

    private void CompletePointerPath(int x, int y, DateTimeOffset timestamp)
    {
        TrackPointer(x, y, timestamp);
        var start = _pathStart;
        var end = _pathLast;
        if (start is not null && end is not null)
        {
            var duration = (int)Math.Round((end.Timestamp - start.Timestamp).TotalMilliseconds);
            var distance = (int)Math.Round(_pathDistance);
            if (ShouldRecordCursorPath(distance, duration))
            {
                MousePathCompleted?.Invoke(this, new MousePathEvent(
                    start.X, start.Y, end.X, end.Y, distance, duration, timestamp));
            }
        }
        _pathStart = new PointerSample(x, y, timestamp);
        _pathLast = _pathStart;
        _pathDistance = 0;
    }

    private static ShortcutEvent? DescribeShortcut(Keys key, DateTimeOffset timestamp) =>
        ClassifyKeyForRecording(
            key,
            IsDown(Keys.LControlKey) || IsDown(Keys.RControlKey),
            IsDown(Keys.LMenu) || IsDown(Keys.RMenu),
            IsDown(Keys.LShiftKey) || IsDown(Keys.RShiftKey),
            IsDown(Keys.LWin) || IsDown(Keys.RWin),
            timestamp);

    public static ShortcutEvent? ClassifyKeyForRecording(
        Keys key,
        bool ctrl,
        bool alt,
        bool shift,
        bool win,
        DateTimeOffset timestamp)
    {
        if (key is Keys.LControlKey or Keys.RControlKey or Keys.ControlKey or
            Keys.LMenu or Keys.RMenu or Keys.Menu or
            Keys.LShiftKey or Keys.RShiftKey or Keys.ShiftKey or
            Keys.LWin or Keys.RWin)
        {
            return null;
        }

        var commandKey = key is Keys.Enter or Keys.Tab or Keys.Escape or Keys.Delete or Keys.Insert or
            Keys.Up or Keys.Down or Keys.Left or Keys.Right or Keys.Home or Keys.End or Keys.Prior or Keys.Next ||
            key is >= Keys.F1 and <= Keys.F24;
        if (!ctrl && !alt && !win && !commandKey)
        {
            return null;
        }

        var parts = new List<string>();
        if (ctrl) parts.Add("Ctrl");
        if (alt) parts.Add("Alt");
        if (shift) parts.Add("Shift");
        if (win) parts.Add("Win");
        parts.Add(FriendlyKey(key));
        var kind = alt && key == Keys.Tab
            ? "window-switch"
            : (!ctrl && !alt && !win && commandKey ? "command-key" : "shortcut");
        return new ShortcutEvent(string.Join('+', parts), kind, timestamp);
    }

    public static bool ShouldRecordCursorPath(int distancePixels, int durationMilliseconds) =>
        distancePixels >= 90 && durationMilliseconds is > 0 and <= 5000;

    private static bool IsDown(Keys key) => (GetAsyncKeyState((int)key) & 0x8000) != 0;

    private static string FriendlyKey(Keys key) => key switch
    {
        Keys.Return => "Enter",
        Keys.Escape => "Esc",
        Keys.Space => "Space",
        Keys.Prior => "PageUp",
        Keys.Next => "PageDown",
        _ => key.ToString()
    };

    private sealed record PointerSample(int X, int Y, DateTimeOffset Timestamp);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        Stop();
        _disposed = true;
    }

    private delegate nint HookProc(int code, nint message, nint parameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseHookStruct
    {
        public NativePoint Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardHookStruct
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public nint Hwnd;
        public uint Message;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public NativePoint Point;
        public uint Private;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int hookId, HookProc callback, nint module, uint threadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint message, nint parameter);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? moduleName);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(uint threadId, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out NativeMessage message, nint window, uint minFilter, uint maxFilter);

    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out NativeMessage message, nint window, uint minFilter, uint maxFilter, uint removeMessage);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref NativeMessage message);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessage(ref NativeMessage message);
}
