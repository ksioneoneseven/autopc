using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.RegularExpressions;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using BitmapDecoder = Windows.Graphics.Imaging.BitmapDecoder;

namespace AutoPilotAgent.Automation.Services;

public sealed class ScreenTextFinder
{
    private readonly OcrEngine _ocrEngine;

    public ScreenTextFinder()
    {
        _ocrEngine = OcrEngine.TryCreateFromUserProfileLanguages() 
            ?? throw new InvalidOperationException("OCR engine not available");
    }

    public async Task<List<TextMatch>> FindTextOnScreenAsync(Rectangle screenRect, string searchText)
    {
        var matches = new List<TextMatch>();
        
        using var bmp = CaptureScreen(screenRect);
        if (bmp == null) return matches;

        var ocrResult = await RunOcrAsync(bmp);
        if (ocrResult == null) return matches;

        var searchLower = searchText.ToLowerInvariant();
        var searchWords = searchText.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in ocrResult.Lines)
        {
            var lineText = line.Text.ToLowerInvariant();
            
            // Check if line contains the search text
            if (lineText.Contains(searchLower) || searchWords.All(w => lineText.Contains(w.ToLowerInvariant())))
            {
                // Find the bounding box for the matching words
                var bounds = GetLineBounds(line);
                matches.Add(new TextMatch
                {
                    Text = line.Text,
                    Bounds = new Rectangle(
                        screenRect.Left + (int)bounds.X,
                        screenRect.Top + (int)bounds.Y,
                        (int)bounds.Width,
                        (int)bounds.Height),
                    Confidence = 1.0
                });
            }
            else
            {
                // Check individual words
                foreach (var word in line.Words)
                {
                    if (word.Text.ToLowerInvariant().Contains(searchLower) ||
                        searchLower.Contains(word.Text.ToLowerInvariant()))
                    {
                        matches.Add(new TextMatch
                        {
                            Text = word.Text,
                            Bounds = new Rectangle(
                                screenRect.Left + (int)word.BoundingRect.X,
                                screenRect.Top + (int)word.BoundingRect.Y,
                                (int)word.BoundingRect.Width,
                                (int)word.BoundingRect.Height),
                            Confidence = 0.8
                        });
                    }
                }
            }
        }

        // Sort by confidence and position (top-left first)
        return matches
            .OrderByDescending(m => m.Confidence)
            .ThenBy(m => m.Bounds.Y)
            .ThenBy(m => m.Bounds.X)
            .ToList();
    }

    public async Task<List<TextMatch>> GetAllTextOnScreenAsync(Rectangle screenRect)
    {
        var results = new List<TextMatch>();
        
        using var bmp = CaptureScreen(screenRect);
        if (bmp == null) return results;

        var ocrResult = await RunOcrAsync(bmp);
        if (ocrResult == null) return results;

        foreach (var line in ocrResult.Lines)
        {
            var bounds = GetLineBounds(line);
            results.Add(new TextMatch
            {
                Text = line.Text,
                Bounds = new Rectangle(
                    screenRect.Left + (int)bounds.X,
                    screenRect.Top + (int)bounds.Y,
                    (int)bounds.Width,
                    (int)bounds.Height),
                Confidence = 1.0
            });
        }

        return results;
    }

    private static Bitmap? CaptureScreen(Rectangle rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return null;
        
        try
        {
            var bmp = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(rect.Left, rect.Top, 0, 0, rect.Size, CopyPixelOperation.SourceCopy);
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    private async Task<OcrResult?> RunOcrAsync(Bitmap bmp)
    {
        try
        {
            // Convert bitmap to byte array
            using var ms = new System.IO.MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            var bytes = ms.ToArray();

            // Create WinRT stream from bytes
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(bytes.AsBuffer());
            stream.Seek(0);

            var decoder = await BitmapDecoder.CreateAsync(stream);
            var softwareBitmap = await decoder.GetSoftwareBitmapAsync();

            return await _ocrEngine.RecognizeAsync(softwareBitmap);
        }
        catch
        {
            return null;
        }
    }

    private static Windows.Foundation.Rect GetLineBounds(OcrLine line)
    {
        if (line.Words.Count == 0)
            return new Windows.Foundation.Rect(0, 0, 0, 0);

        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = 0, maxY = 0;

        foreach (var word in line.Words)
        {
            minX = Math.Min(minX, word.BoundingRect.X);
            minY = Math.Min(minY, word.BoundingRect.Y);
            maxX = Math.Max(maxX, word.BoundingRect.X + word.BoundingRect.Width);
            maxY = Math.Max(maxY, word.BoundingRect.Y + word.BoundingRect.Height);
        }

        return new Windows.Foundation.Rect(minX, minY, maxX - minX, maxY - minY);
    }
}

public sealed class TextMatch
{
    public required string Text { get; init; }
    public Rectangle Bounds { get; init; }
    public double Confidence { get; init; }
    
    public Point Center => new(Bounds.X + Bounds.Width / 2, Bounds.Y + Bounds.Height / 2);
}
