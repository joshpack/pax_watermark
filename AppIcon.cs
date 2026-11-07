using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ShapePath = Avalonia.Controls.Shapes.Path;

namespace PaxWatermark;

/// <summary>The app's mark (a photo with a water drop stamped on its corner), drawn from vector geometry so it stays crisp at every size.</summary>
public static class AppIcon
{
    /// <summary>The photo frame, stroked, in a 100x100 box.</summary>
    public static readonly Geometry Frame = Geometry.Parse("M8,22 Q8,14 16,14 L70,14 Q78,14 78,22 L78,62 Q78,70 70,70 L16,70 Q8,70 8,62 Z");

    /// <summary>Hills and the sun inside the frame, filled.</summary>
    public static readonly Geometry Scene = Geometry.Parse("M16,62 L34,38 L46,52 L54,44 L66,58 L66,62 Z M56,26 A6,6 0 1 1 68,26 A6,6 0 1 1 56,26 Z");

    /// <summary>The drop overlapping the frame's corner, filled.</summary>
    public static readonly Geometry Drop = Geometry.Parse("M76,44 C80,52 94,66 94,78 A18,18 0 0 1 58,78 C58,66 72,52 76,44 Z");

    /// <summary>The shine on the drop, stroked.</summary>
    public static readonly Geometry Shine = Geometry.Parse("M67,79 A9,9 0 0 0 75,88");

    private static readonly Color Dark = Color.Parse("#04140C");

    /// <summary>The mark on its own (no tile), in a 100x100 box, for the window header.</summary>
    public static Control Art(double gap = 6)
    {
        var dark = new SolidColorBrush(Dark);
        var green = new SolidColorBrush(Color.Parse("#19F58C"));
        var art = new Grid { Width = 100, Height = 100 };
        art.Children.Add(new ShapePath { Data = Frame, Stroke = dark, StrokeThickness = 8, StrokeJoin = PenLineJoin.Round });
        art.Children.Add(new ShapePath { Data = Scene, Fill = dark });
        // The green outline cuts the drop free of the frame underneath it (the tile is green in that corner).
        art.Children.Add(new ShapePath { Data = Drop, Fill = dark, Stroke = green, StrokeThickness = gap, StrokeJoin = PenLineJoin.Round });
        art.Children.Add(new ShapePath { Data = Shine, Stroke = green, StrokeThickness = 4, StrokeLineCap = PenLineCap.Round });
        return art;
    }

    /// <summary>The full icon (see <see cref="BrandIcon"/>).</summary>
    public static Control Build(int size)
    {
        return BrandIcon.Build(size);
    }

    public static RenderTargetBitmap Render(int size)
    {
        var tile = Build(size);
        tile.Measure(new Size(size, size));
        tile.Arrange(new Rect(0, 0, size, size));
        var bitmap = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
        bitmap.Render(tile);
        return bitmap;
    }

    public static WindowIcon Create(int size = 128)
    {
        using var bmp = Render(size);
        using var ms = new MemoryStream();
        bmp.Save(ms, new PngBitmapEncoderOptions());
        ms.Position = 0;
        return new WindowIcon(ms);
    }

    /// <summary>A 256 px PNG of the icon (for the README).</summary>
    public static void WritePng(string path, int size = 256)
    {
        using var bmp = Render(size);
        bmp.Save(path, new PngBitmapEncoderOptions());
    }

    /// <summary>Writes a multi-size .ico (PNG entries) of the icon, for the exe's embedded icon.</summary>
    public static void WriteIco(string path)
    {
        int[] sizes = [256, 64, 48, 32, 24, 16];
        var pngs = sizes.Select(s =>
        {
            using var bmp = Render(s);
            using var ms = new MemoryStream();
            bmp.Save(ms, new PngBitmapEncoderOptions());
            return ms.ToArray();
        }).ToList();
        using var bw = new BinaryWriter(File.Create(path));
        bw.Write((short)0); bw.Write((short)1); bw.Write((short)sizes.Length);
        var offset = 6 + 16 * sizes.Length;
        for (var i = 0; i < sizes.Length; i++)
        {
            var b = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
            bw.Write(b); bw.Write(b); bw.Write((byte)0); bw.Write((byte)0); bw.Write((short)1); bw.Write((short)32);
            bw.Write(pngs[i].Length); bw.Write(offset);
            offset += pngs[i].Length;
        }

        foreach (var p in pngs)
            bw.Write(p);
    }
}
