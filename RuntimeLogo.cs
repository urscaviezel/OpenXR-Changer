using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OpenXRChanger;
public static class RuntimeLogo
{
    static readonly Dictionary<string, BitmapSource> Cache = new(StringComparer.OrdinalIgnoreCase);
    static BitmapSource Read(string path)
    {
        var key = path + "|" + File.GetLastWriteTimeUtc(path).Ticks;
        if (Cache.TryGetValue(key, out var cached)) return cached;
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.UriSource = new Uri(path); image.EndInit(); image.Freeze();
        Cache[key] = image; return image;
    }
    static Rect AlphaBounds(BitmapSource source)
    {
        var bitmap = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int w = bitmap.PixelWidth, h = bitmap.PixelHeight;
        if (w > 4096 || h > 4096) return new Rect(0, 0, 1, 1);
        var pixels = new byte[w * h * 4]; bitmap.CopyPixels(pixels, w * 4, 0);
        int left = w, top = h, right = -1, bottom = -1;
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            if (pixels[(y * w + x) * 4 + 3] > 24) { left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y); }
        return right < left ? new Rect(0, 0, 1, 1) : new Rect((double)left / w, (double)top / h, (double)(right - left + 1) / w, (double)(bottom - top + 1) / h);
    }
    // Render the same cropped logo used by the cards into a compact tray image.
    // The caller owns the bitmap and disposes it when rebuilding the menu.
    public static System.Drawing.Bitmap? CreateTrayBitmap(RuntimeEntry runtime)
    {
        var visual = Create(runtime);
        if (visual == null) return null;
        visual.Width = 24; visual.Height = 24;
        visual.Measure(new Size(24, 24));
        visual.Arrange(new Rect(0, 0, 24, 24));
        var rendered = new RenderTargetBitmap(48, 48, 192, 192, PixelFormats.Pbgra32);
        rendered.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rendered));
        using var stream = new MemoryStream();
        encoder.Save(stream); stream.Position = 0;
        using var decoded = new System.Drawing.Bitmap(stream);
        return new System.Drawing.Bitmap(decoded);
    }

    public static FrameworkElement? Create(RuntimeEntry runtime)
    {
        try
        {
            bool custom = !string.IsNullOrEmpty(runtime.Logo);
            var file = runtime.Name switch
            {
                "SteamVR" => "transparent-steam.png", "Meta" => "transparent-meta.png",
                "PimaxXR" => "transparent-pimax.png", "ViveVR" => "transparent-vive.png",
                "Virtual Desktop · VDXR" => "transparent-vd.png",
                "Windows Mixed Reality" => "WMR.png", "Varjo" => "Varjo.png", _ => ""
            };
            if (!custom && file == "") return null;
            var source = Read(custom ? Store.Resolve(runtime.Logo) : Path.Combine(Store.Root, "assets", file));
            var bounds = AlphaBounds(source);
            bool white = !custom && (file == "WMR.png" || file == "Varjo.png");
            if (white && file == "WMR.png") bounds.Height *= .52;
            var brush = new ImageBrush(source) { Viewbox = bounds, ViewboxUnits = BrushMappingMode.RelativeToBoundingBox, Stretch = Stretch.Uniform };
            var rectangle = new System.Windows.Shapes.Rectangle { Width = 40, Height = 40, ToolTip = runtime.Name };
            RenderOptions.SetBitmapScalingMode(rectangle, BitmapScalingMode.HighQuality);
            if (white) { rectangle.Fill = Brushes.White; rectangle.OpacityMask = brush; }
            else rectangle.Fill = brush;
            return rectangle;
        }
        catch { return null; }
    }
}
