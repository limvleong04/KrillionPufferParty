using System.Drawing.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace PufferHelper;

/// <summary>Windows' built-in text recognition: free, offline, and fast enough to run on every refresh.</summary>
internal static class OcrReader
{
    public static async Task<string> ReadTextAsync(Bitmap bitmap)
    {
        OcrEngine engine = OcrEngine.TryCreateFromUserProfileLanguages()
            ?? throw new InvalidOperationException(
                "Windows text recognition isn't installed for your display language. Add English under Settings > Time & language > Language.");

        // Small text recognises better when enlarged; the engine refuses images above MaxImageDimension.
        int maxEdge = (int)OcrEngine.MaxImageDimension;
        int longEdge = Math.Max(bitmap.Width, bitmap.Height);
        double scale = Math.Min(2.0, (double)maxEdge / longEdge);
        using Bitmap sized = Math.Abs(scale - 1.0) < 0.01 ? new Bitmap(bitmap) : Resize(bitmap, scale);

        using var stream = new MemoryStream();
        sized.Save(stream, ImageFormat.Png);
        stream.Position = 0;

        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream.AsRandomAccessStream());
        using SoftwareBitmap softwareBitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        OcrResult result = await engine.RecognizeAsync(softwareBitmap);
        return string.Join('\n', result.Lines.Select(line => line.Text));
    }

    private static Bitmap Resize(Bitmap source, double scale)
    {
        var size = new Size(Math.Max(1, (int)(source.Width * scale)), Math.Max(1, (int)(source.Height * scale)));
        var result = new Bitmap(size.Width, size.Height, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(result);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.DrawImage(source, new Rectangle(Point.Empty, size));
        return result;
    }
}
