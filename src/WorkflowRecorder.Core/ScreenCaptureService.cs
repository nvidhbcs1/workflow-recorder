using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace WorkflowRecorder.Core;

public static class ScreenCaptureService
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(nint handle, nint deviceContext, uint flags);

    private const uint RenderFullContent = 2;

    public sealed record CaptureResult(bool Success, string? Source = null, string? Error = null);

    /// <summary>
    /// Captures an in-memory, scaled preview for the recorder UI. The caller owns and must dispose the returned image.
    /// </summary>
    public static Bitmap? CreateWindowPreview(WindowContext window, Size maximumSize, out string? source, out string? error)
    {
        source = null;
        error = null;
        if (!window.IsValid || window.Bounds.Width <= 0 || window.Bounds.Height <= 0 || window.Bounds.Width > 12000 || window.Bounds.Height > 12000)
        {
            error = "The selected window is unavailable or has invalid bounds.";
            return null;
        }

        try
        {
            using var image = new Bitmap(window.Bounds.Width, window.Bounds.Height, PixelFormat.Format32bppArgb);
            source = TryPrintWindow(window, image) ? "window-handle" : null;
            if (source is null)
            {
                if (Win32WindowService.IsMinimized(window.Handle))
                {
                    error = "The selected window is minimized and cannot be previewed.";
                    return null;
                }

                using var graphics = Graphics.FromImage(image);
                graphics.CopyFromScreen(
                    window.Bounds.Left,
                    window.Bounds.Top,
                    0,
                    0,
                    new Size(window.Bounds.Width, window.Bounds.Height),
                    CopyPixelOperation.SourceCopy);
                source = "visible-screen";
            }

            return ScaleForPreview(image, maximumSize);
        }
        catch (ExternalException exception)
        {
            error = exception.Message;
            return null;
        }
        catch (ArgumentException exception)
        {
            error = exception.Message;
            return null;
        }
    }

    /// <summary>
    /// Captures an in-memory, scaled preview for a selected display. The caller owns and must dispose the returned image.
    /// </summary>
    public static Bitmap? CreateScreenPreview(RectInfo bounds, Size maximumSize, out string? error)
    {
        error = null;
        if (bounds.Width <= 0 || bounds.Height <= 0 || bounds.Width > 12000 || bounds.Height > 12000)
        {
            error = "The selected screen has invalid bounds.";
            return null;
        }

        try
        {
            using var image = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(image))
            {
                graphics.CopyFromScreen(
                    bounds.Left,
                    bounds.Top,
                    0,
                    0,
                    new Size(bounds.Width, bounds.Height),
                    CopyPixelOperation.SourceCopy);
            }
            return ScaleForPreview(image, maximumSize);
        }
        catch (ExternalException exception)
        {
            error = exception.Message;
            return null;
        }
        catch (ArgumentException exception)
        {
            error = exception.Message;
            return null;
        }
    }

    public static CaptureResult CaptureWindow(
        WindowContext window,
        string outputPath,
        int? clickX = null,
        int? clickY = null,
        int? step = null)
    {
        if (!window.IsValid || window.Bounds.Width > 12000 || window.Bounds.Height > 12000)
        {
            return new CaptureResult(false, Error: "The target window is unavailable or has invalid bounds.");
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            using var image = new Bitmap(window.Bounds.Width, window.Bounds.Height, PixelFormat.Format32bppArgb);
            var source = TryPrintWindow(window, image) ? "window-handle" : null;
            if (source is null)
            {
                if (Win32WindowService.IsMinimized(window.Handle))
                {
                    return new CaptureResult(false, Error: "The target window is minimized and could not be rendered.");
                }
                using var screenGraphics = Graphics.FromImage(image);
                screenGraphics.CopyFromScreen(
                    window.Bounds.Left,
                    window.Bounds.Top,
                    0,
                    0,
                    new Size(window.Bounds.Width, window.Bounds.Height),
                    CopyPixelOperation.SourceCopy);
                source = "target-screen-fallback";
            }

            if (clickX is not null && clickY is not null)
            {
                using var graphics = Graphics.FromImage(image);
                var localX = clickX.Value - window.Bounds.Left;
                var localY = clickY.Value - window.Bounds.Top;
                DrawMarker(graphics, localX, localY, step);
            }
            image.Save(outputPath, ImageFormat.Png);
            return new CaptureResult(true, source);
        }
        catch (ExternalException error)
        {
            return new CaptureResult(false, Error: error.Message);
        }
        catch (ArgumentException error)
        {
            return new CaptureResult(false, Error: error.Message);
        }
    }

    public static CaptureResult CaptureScreen(
        RectInfo bounds,
        string outputPath,
        int? clickX = null,
        int? clickY = null,
        int? step = null)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0 || bounds.Width > 12000 || bounds.Height > 12000)
        {
            return new CaptureResult(false, Error: "The selected screen has invalid bounds.");
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            using var image = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(image))
            {
                graphics.CopyFromScreen(
                    bounds.Left,
                    bounds.Top,
                    0,
                    0,
                    new Size(bounds.Width, bounds.Height),
                    CopyPixelOperation.SourceCopy);
            }

            if (clickX is not null && clickY is not null)
            {
                using var graphics = Graphics.FromImage(image);
                DrawMarker(graphics, clickX.Value - bounds.Left, clickY.Value - bounds.Top, step);
            }
            image.Save(outputPath, ImageFormat.Png);
            return new CaptureResult(true, "entire-screen");
        }
        catch (ExternalException error)
        {
            return new CaptureResult(false, Error: error.Message);
        }
        catch (ArgumentException error)
        {
            return new CaptureResult(false, Error: error.Message);
        }
    }

    private static bool TryPrintWindow(WindowContext window, Bitmap image)
    {
        using var graphics = Graphics.FromImage(image);
        graphics.Clear(Color.Transparent);
        var deviceContext = graphics.GetHdc();
        try
        {
            if (!PrintWindow(window.Handle, deviceContext, RenderFullContent))
            {
                return false;
            }
        }
        finally
        {
            graphics.ReleaseHdc(deviceContext);
        }
        return !LooksBlank(image);
    }

    private static Bitmap ScaleForPreview(Image image, Size maximumSize)
    {
        if (maximumSize.Width <= 0 || maximumSize.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumSize), "Preview dimensions must be positive.");
        }

        var scale = Math.Min(maximumSize.Width / (double)image.Width, maximumSize.Height / (double)image.Height);
        var width = Math.Max(1, (int)Math.Round(image.Width * scale));
        var height = Math.Max(1, (int)Math.Round(image.Height * scale));
        var preview = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(preview);
        graphics.Clear(Color.White);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.DrawImage(image, new Rectangle(0, 0, width, height));
        return preview;
    }

    private static bool LooksBlank(Bitmap image)
    {
        var darkOrTransparent = 0;
        var samples = 0;
        for (var row = 1; row <= 8; row++)
        {
            for (var column = 1; column <= 8; column++)
            {
                var pixel = image.GetPixel(column * (image.Width - 1) / 9, row * (image.Height - 1) / 9);
                samples++;
                if (pixel.A < 8 || (pixel.R < 5 && pixel.G < 5 && pixel.B < 5))
                {
                    darkOrTransparent++;
                }
            }
        }
        return darkOrTransparent >= samples * 9 / 10;
    }

    private static void DrawMarker(Graphics graphics, int x, int y, int? step)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var halo = new SolidBrush(Color.FromArgb(65, 255, 55, 55));
        using var ring = new Pen(Color.FromArgb(245, 220, 25, 25), 5);
        graphics.FillEllipse(halo, x - 28, y - 28, 56, 56);
        graphics.DrawEllipse(ring, x - 20, y - 20, 40, 40);

        if (step is null)
        {
            return;
        }

        var label = step.Value.ToString();
        using var font = new Font("Segoe UI", 10, FontStyle.Bold);
        var size = graphics.MeasureString(label, font);
        var labelRect = new RectangleF(x + 18, y - 25, size.Width + 12, size.Height + 6);
        using var labelBrush = new SolidBrush(Color.FromArgb(240, 190, 15, 15));
        using var textBrush = new SolidBrush(Color.White);
        graphics.FillRoundedRectangle(labelBrush, labelRect, 7);
        graphics.DrawString(label, font, textBrush, labelRect.Left + 6, labelRect.Top + 3);
    }

    private static void FillRoundedRectangle(this Graphics graphics, Brush brush, RectangleF rect, float radius)
    {
        using var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        graphics.FillPath(brush, path);
    }
}
