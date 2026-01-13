using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace AutoPilotAgent.Automation.Services;

public sealed class GridOverlay
{
    private const int DefaultRows = 8;
    private const int DefaultCols = 10;

    public int Rows { get; }
    public int Cols { get; }

    public GridOverlay(int rows = DefaultRows, int cols = DefaultCols)
    {
        Rows = rows;
        Cols = cols;
    }

    public string CreateGridOverlayDataUrl(string sourceDataUrl, int imageWidth, int imageHeight)
    {
        if (string.IsNullOrWhiteSpace(sourceDataUrl) || !sourceDataUrl.Contains("base64,"))
        {
            return sourceDataUrl;
        }

        try
        {
            var base64 = sourceDataUrl.Substring(sourceDataUrl.IndexOf("base64,") + 7);
            var bytes = Convert.FromBase64String(base64);

            using var ms = new MemoryStream(bytes);
            using var original = Image.FromStream(ms);
            using var bmp = new Bitmap(original.Width, original.Height, PixelFormat.Format24bppRgb);
            
            using (var g = Graphics.FromImage(bmp))
            {
                g.DrawImage(original, 0, 0);
                DrawGrid(g, bmp.Width, bmp.Height);
            }

            using var outMs = new MemoryStream();
            var codec = ImageCodecInfo.GetImageEncoders().FirstOrDefault(c => c.MimeType == "image/jpeg");
            if (codec is null)
            {
                return sourceDataUrl;
            }

            using var encParams = new EncoderParameters(1);
            encParams.Param[0] = new EncoderParameter(Encoder.Quality, 75L);
            bmp.Save(outMs, codec, encParams);

            return "data:image/jpeg;base64," + Convert.ToBase64String(outMs.ToArray());
        }
        catch
        {
            return sourceDataUrl;
        }
    }

    private void DrawGrid(Graphics g, int width, int height)
    {
        var cellWidth = width / (float)Cols;
        var cellHeight = height / (float)Rows;

        using var pen = new Pen(Color.FromArgb(180, 255, 0, 0), 2);
        using var font = new Font("Arial", Math.Max(10, Math.Min(cellWidth, cellHeight) / 4), FontStyle.Bold);
        using var brush = new SolidBrush(Color.FromArgb(200, 255, 255, 0));
        using var bgBrush = new SolidBrush(Color.FromArgb(150, 0, 0, 0));

        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Draw grid lines
        for (var row = 0; row <= Rows; row++)
        {
            var y = row * cellHeight;
            g.DrawLine(pen, 0, y, width, y);
        }

        for (var col = 0; col <= Cols; col++)
        {
            var x = col * cellWidth;
            g.DrawLine(pen, x, 0, x, height);
        }

        // Draw cell numbers
        var cellNum = 1;
        for (var row = 0; row < Rows; row++)
        {
            for (var col = 0; col < Cols; col++)
            {
                var x = col * cellWidth + cellWidth / 2;
                var y = row * cellHeight + cellHeight / 2;

                var text = cellNum.ToString();
                var size = g.MeasureString(text, font);
                
                var textX = x - size.Width / 2;
                var textY = y - size.Height / 2;

                // Draw background for readability
                g.FillRectangle(bgBrush, textX - 2, textY - 2, size.Width + 4, size.Height + 4);
                g.DrawString(text, font, brush, textX, textY);

                cellNum++;
            }
        }
    }

    public (int x, int y) GetCellCenter(int cellNumber, int screenWidth, int screenHeight)
    {
        if (cellNumber < 1 || cellNumber > Rows * Cols)
        {
            return (screenWidth / 2, screenHeight / 2);
        }

        var zeroIndex = cellNumber - 1;
        var row = zeroIndex / Cols;
        var col = zeroIndex % Cols;

        var cellWidth = screenWidth / (float)Cols;
        var cellHeight = screenHeight / (float)Rows;

        var x = (int)(col * cellWidth + cellWidth / 2);
        var y = (int)(row * cellHeight + cellHeight / 2);

        return (x, y);
    }

    public (double rx, double ry) GetCellCenterRelative(int cellNumber)
    {
        if (cellNumber < 1 || cellNumber > Rows * Cols)
        {
            return (0.5, 0.5);
        }

        var zeroIndex = cellNumber - 1;
        var row = zeroIndex / Cols;
        var col = zeroIndex % Cols;

        var rx = (col + 0.5) / Cols;
        var ry = (row + 0.5) / Rows;

        return (rx, ry);
    }
}
