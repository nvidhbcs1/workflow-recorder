using System.Runtime.InteropServices;

namespace WorkflowRecorder.App;

/// <summary>
/// Hosts the Desktop Window Manager's live thumbnail for a chosen app window.
/// Unlike PrintWindow, DWM thumbnails remain useful when the app is covered by
/// the recorder window or renders its content on the GPU.
/// </summary>
internal sealed class WindowThumbnailPreview : IDisposable
{
    private const uint ThumbnailRectDestination = 0x00000001;
    private const uint ThumbnailOpacity = 0x00000004;
    private const uint ThumbnailVisible = 0x00000008;

    private nint _thumbnail;
    private nint _sourceWindow;

    public bool Show(Form host, Control viewport, nint sourceWindow, out string? error)
    {
        error = null;
        if (host.IsDisposed || viewport.IsDisposed || sourceWindow == nint.Zero)
        {
            error = "The selected window is unavailable.";
            return false;
        }

        if (_thumbnail == nint.Zero || _sourceWindow != sourceWindow)
        {
            Hide();
            var result = DwmRegisterThumbnail(host.Handle, sourceWindow, out _thumbnail);
            if (result < 0 || _thumbnail == nint.Zero)
            {
                _thumbnail = nint.Zero;
                error = $"Windows could not create a live preview (0x{result:X8}).";
                return false;
            }
            _sourceWindow = sourceWindow;
        }

        var topLeft = host.PointToClient(viewport.PointToScreen(Point.Empty));
        var bounds = viewport.ClientRectangle;
        var properties = new DwmThumbnailProperties
        {
            Flags = ThumbnailRectDestination | ThumbnailOpacity | ThumbnailVisible,
            Destination = new NativeRect
            {
                Left = topLeft.X,
                Top = topLeft.Y,
                Right = topLeft.X + bounds.Width,
                Bottom = topLeft.Y + bounds.Height
            },
            Opacity = byte.MaxValue,
            Visible = 1
        };
        var update = DwmUpdateThumbnailProperties(_thumbnail, ref properties);
        if (update >= 0)
        {
            return true;
        }

        error = $"Windows could not position the live preview (0x{update:X8}).";
        Hide();
        return false;
    }

    public void Hide()
    {
        if (_thumbnail != nint.Zero)
        {
            DwmUnregisterThumbnail(_thumbnail);
            _thumbnail = nint.Zero;
        }
        _sourceWindow = nint.Zero;
    }

    public void Dispose() => Hide();

    [DllImport("dwmapi.dll")]
    private static extern int DwmRegisterThumbnail(nint destinationWindow, nint sourceWindow, out nint thumbnail);

    [DllImport("dwmapi.dll")]
    private static extern int DwmUnregisterThumbnail(nint thumbnail);

    [DllImport("dwmapi.dll")]
    private static extern int DwmUpdateThumbnailProperties(nint thumbnail, ref DwmThumbnailProperties properties);

    [StructLayout(LayoutKind.Sequential)]
    private struct DwmThumbnailProperties
    {
        public uint Flags;
        public NativeRect Destination;
        public NativeRect Source;
        public byte Opacity;
        public int Visible;
        public int SourceClientAreaOnly;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
