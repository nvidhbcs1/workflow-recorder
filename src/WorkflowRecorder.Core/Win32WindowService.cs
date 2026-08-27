using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace WorkflowRecorder.Core;

public static class Win32WindowService
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(NativePoint point);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(nint handle, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint handle, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint handle, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint handle, out NativeRect rect);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(nint handle);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint handle);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint handle);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint handle, int attribute, out NativeRect value, int size);

    private delegate bool EnumWindowsCallback(nint handle, nint parameter);

    private const uint Root = 2;
    private const int ExtendedFrameBounds = 9;

    public static WindowContext Foreground() => FromHandle(GetForegroundWindow());

    public static WindowContext AtPoint(int x, int y)
    {
        var child = WindowFromPoint(new NativePoint { X = x, Y = y });
        var root = child == 0 ? 0 : GetAncestor(child, Root);
        return FromHandle(root == 0 ? child : root);
    }

    public static bool Exists(nint handle) => handle != 0 && IsWindow(handle);

    public static bool IsMinimized(nint handle) => handle != 0 && IsIconic(handle);

    public static IReadOnlyList<WindowContext> ListTopLevelWindows()
    {
        var windows = new List<WindowContext>();
        EnumWindows((handle, _) =>
        {
            var window = FromHandle(handle);
            if (window.IsValid && !string.IsNullOrWhiteSpace(window.Title))
            {
                windows.Add(window);
            }
            return true;
        }, 0);
        return windows;
    }

    public static WindowContext FromHandle(nint handle)
    {
        if (handle == 0 || !IsWindow(handle) || !IsWindowVisible(handle))
        {
            return new WindowContext();
        }

        GetWindowThreadProcessId(handle, out var processId);
        var titleBuffer = new StringBuilder(2048);
        GetWindowText(handle, titleBuffer, titleBuffer.Capacity);
        var title = titleBuffer.ToString();
        var processName = "Unknown";
        try
        {
            processName = Process.GetProcessById((int)processId).ProcessName;
        }
        catch
        {
            // A window can disappear between the Win32 calls.
        }

        var bounds = new RectInfo();
        if (TryGetBounds(handle, out var rect))
        {
            bounds.Left = rect.Left;
            bounds.Top = rect.Top;
            bounds.Width = Math.Max(0, rect.Right - rect.Left);
            bounds.Height = Math.Max(0, rect.Bottom - rect.Top);
        }

        return new WindowContext
        {
            Handle = handle,
            ProcessId = (int)processId,
            ProcessName = processName,
            Title = title,
            Bounds = bounds
        };
    }

    private static bool TryGetBounds(nint handle, out NativeRect rect)
    {
        try
        {
            if (DwmGetWindowAttribute(handle, ExtendedFrameBounds, out rect, Marshal.SizeOf<NativeRect>()) == 0)
            {
                return true;
            }
        }
        catch (DllNotFoundException)
        {
            // Fall back on older Windows installations.
        }
        return GetWindowRect(handle, out rect);
    }
}
