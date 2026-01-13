using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using AutoPilotAgent.Automation.Win32;
using AutoPilotAgent.Core.Interfaces;
using AutoPilotAgent.Core.Models;

namespace AutoPilotAgent.Automation.Services;

public sealed class ObservationService : IObservationService
{
    private readonly WindowManager _windowManager;
    public const int GridRows = 12;
    public const int GridCols = 16;
    public const int TotalCells = GridRows * GridCols; // 192 cells

    private readonly object _gate = new();
    private DateTime _lastScreenshotUtc = DateTime.MinValue;
    private string? _lastScreenshotDataUrl;

    public ObservationService(WindowManager windowManager)
    {
        _windowManager = windowManager;
    }

    public ObservationModel Observe(ExecutionResult? lastResult = null)
    {
        var (title, processName) = _windowManager.GetForegroundWindowInfo();

        var screenshot = GetThrottledScreenshotDataUrl();

        return new ObservationModel
        {
            ActiveWindowTitle = title,
            ActiveProcess = processName,
            LastActionSuccess = lastResult?.Success,
            ErrorMessage = lastResult?.ErrorMessage,
            ScreenshotDataUrl = screenshot
        };
    }

    private string? GetThrottledScreenshotDataUrl()
    {
        lock (_gate)
        {
            var now = DateTime.UtcNow;
            if (_lastScreenshotDataUrl is not null && (now - _lastScreenshotUtc) < TimeSpan.FromSeconds(3))
            {
                return _lastScreenshotDataUrl;
            }

            _lastScreenshotUtc = now;
            _lastScreenshotDataUrl = CaptureForegroundClientJpegDataUrl(maxWidth: 640, jpegQuality: 70L);
            return _lastScreenshotDataUrl;
        }
    }

    private string? CaptureForegroundClientJpegDataUrl(int maxWidth, long jpegQuality)
    {
        if (!_windowManager.TryGetForegroundClientRectScreen(out var rect))
        {
            return null;
        }

        var width = Math.Max(1, rect.Right - rect.Left);
        var height = Math.Max(1, rect.Bottom - rect.Top);

        try
        {
            using var bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
            }

            Bitmap final = bmp;
            if (width > maxWidth)
            {
                var scale = maxWidth / (double)width;
                var newW = Math.Max(1, (int)Math.Round(width * scale));
                var newH = Math.Max(1, (int)Math.Round(height * scale));
                final = new Bitmap(newW, newH, PixelFormat.Format24bppRgb);
                using var g2 = Graphics.FromImage(final);
                g2.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g2.DrawImage(bmp, 0, 0, newW, newH);
            }

            // Draw numbered grid overlay
            DrawGridOverlay(final);

            using var ms = new MemoryStream();
            var codec = ImageCodecInfo.GetImageEncoders().FirstOrDefault(c => c.MimeType == "image/jpeg");
            if (codec is null)
            {
                return null;
            }

            using var encParams = new EncoderParameters(1);
            encParams.Param[0] = new EncoderParameter(Encoder.Quality, Math.Clamp(jpegQuality, 30L, 90L));
            final.Save(ms, codec, encParams);

            if (!ReferenceEquals(final, bmp))
            {
                final.Dispose();
            }

            var base64 = Convert.ToBase64String(ms.ToArray());
            return "data:image/jpeg;base64," + base64;
        }
        catch
        {
            return null;
        }
    }

    private static void DrawGridOverlay(Bitmap bmp)
    {
        var width = bmp.Width;
        var height = bmp.Height;
        var cellWidth = width / (float)GridCols;
        var cellHeight = height / (float)GridRows;

        using var g = Graphics.FromImage(bmp);
        using var pen = new Pen(Color.FromArgb(180, 255, 0, 0), 1);
        using var font = new Font("Arial", Math.Max(8, Math.Min(cellWidth, cellHeight) / 5), FontStyle.Bold);
        using var textBrush = new SolidBrush(Color.FromArgb(220, 255, 255, 0));
        using var bgBrush = new SolidBrush(Color.FromArgb(140, 0, 0, 0));

        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Draw grid lines
        for (var row = 0; row <= GridRows; row++)
        {
            var y = row * cellHeight;
            g.DrawLine(pen, 0, y, width, y);
        }

        for (var col = 0; col <= GridCols; col++)
        {
            var x = col * cellWidth;
            g.DrawLine(pen, x, 0, x, height);
        }

        // Draw cell numbers
        var cellNum = 1;
        for (var row = 0; row < GridRows; row++)
        {
            for (var col = 0; col < GridCols; col++)
            {
                var x = col * cellWidth + cellWidth / 2;
                var y = row * cellHeight + cellHeight / 2;

                var text = cellNum.ToString();
                var size = g.MeasureString(text, font);

                var textX = x - size.Width / 2;
                var textY = y - size.Height / 2;

                g.FillRectangle(bgBrush, textX - 1, textY - 1, size.Width + 2, size.Height + 2);
                g.DrawString(text, font, textBrush, textX, textY);

                cellNum++;
            }
        }
    }
}
