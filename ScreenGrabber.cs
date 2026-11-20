using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace PufferHelper;

internal static class ScreenGrabber
{
    /// <summary>Copies a rectangle of the screen (screen pixels; the app is per-monitor DPI aware).</summary>
    public static Bitmap Capture(Rectangle area)
    {
        var bitmap = new Bitmap(area.Width, area.Height, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(bitmap);
        g.CopyFromScreen(area.Location, Point.Empty, area.Size);
        return bitmap;
    }

    /// <summary>Returns a copy whose longer edge is at most <paramref name="maxLongEdge"/> pixels.</summary>
    public static Bitmap Shrink(Bitmap source, int maxLongEdge)
    {
        int longEdge = Math.Max(source.Width, source.Height);
        double factor = longEdge > maxLongEdge ? (double)maxLongEdge / longEdge : 1.0;
        var size = new Size(Math.Max(1, (int)Math.Round(source.Width * factor)), Math.Max(1, (int)Math.Round(source.Height * factor)));

        var result = new Bitmap(size.Width, size.Height, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(result);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(source, new Rectangle(Point.Empty, size));
        return result;
    }

    /// <summary>PNG bytes, shrunk so a full 16:9 screen costs about 1,200 input tokens and text stays readable.</summary>
    public static byte[] ToPng(Bitmap source, int maxLongEdge = 1280)
    {
        using var shrunk = Shrink(source, maxLongEdge);
        using var stream = new MemoryStream();
        shrunk.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }
}
