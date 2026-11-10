using SkiaSharp;

namespace pxdWatermark.Imaging;

/// <summary>
/// Where the watermark goes, relative to the photo, so the same placement fits photos of any size:
/// the watermark's center as a fraction of the photo's width and height, its width as a fraction of the photo's width,
/// its opacity (0 to 1) and its rotation in degrees (clockwise).
/// </summary>
public readonly record struct Placement(double CenterX, double CenterY, double Size, double Opacity, double Rotation)
{
    public static Placement Default => new(0.78, 0.82, 0.25, 0.7, 0);

    /// <summary>The watermark's size in photo pixels.</summary>
    public (double Width, double Height) PixelSize(int photoWidth, int wmWidth, int wmHeight)
    {
        var w = Math.Max(1, Size * photoWidth);
        return (w, w * wmHeight / Math.Max(1, wmWidth));
    }
}

/// <summary>Loading, stamping and saving at the photo's full resolution, all done with Skia.</summary>
public static class Stamper
{
    /// <summary>File types that can be opened. BMP and GIF are read but saved as PNG.</summary>
    public static readonly string[] InputExtensions = [".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif"];

    public static bool IsImage(string path) =>
        InputExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Decodes an image file into premultiplied BGRA pixels, turned the way the camera meant (EXIF orientation).
    /// Throws <see cref="InvalidDataException"/> when the file isn't a readable image.
    /// </summary>
    public static SKBitmap Load(string path)
    {
        using var stream = File.OpenRead(path);
        return Load(stream, Path.GetFileName(path));
    }

    public static SKBitmap Load(Stream stream, string name = "image")
    {
        // SKCodec wants a seekable stream; copy so the file isn't held open (or locked) any longer than needed.
        var data = new MemoryStream();
        stream.CopyTo(data);
        data.Position = 0;
        using var codec = SKCodec.Create(data) ?? throw new InvalidDataException($"{name} is not an image this app can read.");
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var raw = new SKBitmap(info);
        var result = codec.GetPixels(info, raw.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            raw.Dispose();
            throw new InvalidDataException($"{name} could not be decoded ({result}).");
        }

        var origin = codec.EncodedOrigin;
        if (origin == SKEncodedOrigin.TopLeft)
            return raw;
        using (raw)
            return Orient(raw, origin);
    }

    /// <summary>Applies an EXIF orientation, so a sideways phone photo comes out upright.</summary>
    public static SKBitmap Orient(SKBitmap src, SKEncodedOrigin origin)
    {
        float w = src.Width, h = src.Height;
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        // x' = ScaleX*x + SkewX*y + TransX, y' = SkewY*x + ScaleY*y + TransY
        var m = origin switch
        {
            SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),     // mirrored
            SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1), // upside down
            SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),   // mirrored, upside down
            SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),       // transposed
            SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),     // turn 90° clockwise
            SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1), // transverse
            SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),   // turn 90° counter-clockwise
            _ => SKMatrix.Identity,
        };
        var dst = new SKBitmap(new SKImageInfo(swap ? src.Height : src.Width, swap ? src.Width : src.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(dst);
        canvas.Clear(SKColors.Transparent);
        canvas.SetMatrix(m);
        canvas.DrawBitmap(src, 0, 0);
        return dst;
    }

    /// <summary>A new bitmap the size of <paramref name="photo"/> with the watermark drawn on it.</summary>
    public static SKBitmap Stamp(SKBitmap photo, SKBitmap watermark, Placement p)
    {
        var result = new SKBitmap(new SKImageInfo(photo.Width, photo.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.Transparent);
        canvas.DrawBitmap(photo, 0, 0);

        var (w, h) = p.PixelSize(photo.Width, watermark.Width, watermark.Height);
        canvas.Translate((float)(p.CenterX * photo.Width), (float)(p.CenterY * photo.Height));
        canvas.RotateDegrees((float)p.Rotation);
        var dest = new SKRect((float)(-w / 2), (float)(-h / 2), (float)(w / 2), (float)(h / 2));

        // Mipmaps keep a big logo smooth when it is shrunk a lot; cubic keeps it sharp when it is enlarged.
        var sampling = w < watermark.Width
            ? new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)
            : new SKSamplingOptions(SKCubicResampler.Mitchell);
        using var image = SKImage.FromBitmap(watermark);
        using var paint = new SKPaint { IsAntialias = true, Color = SKColors.White.WithAlpha((byte)Math.Round(Math.Clamp(p.Opacity, 0, 1) * 255)) };
        canvas.DrawImage(image, dest, sampling, paint);
        return result;
    }

    /// <summary>The format a file is saved in, from its extension: JPEG, WebP, or PNG for anything else.</summary>
    public static SKEncodedImageFormat FormatFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => SKEncodedImageFormat.Jpeg,
        ".webp" => SKEncodedImageFormat.Webp,
        _ => SKEncodedImageFormat.Png,
    };

    /// <summary>
    /// The extension an output keeps: the photo's own one when it can be written (JPEG, PNG, WebP), otherwise .png,
    /// so the file's contents always match its name.
    /// </summary>
    public static string OutputExtension(string sourcePath)
    {
        var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
        return ext is ".jpg" or ".jpeg" or ".png" or ".webp" ? ext : ".png";
    }

    /// <summary>Encodes in the format the file name asks for and writes it (via a temp file, so a failed save leaves no half file).</summary>
    public static void Save(SKBitmap bitmap, string path, int quality = 92)
    {
        var format = FormatFor(path);
        SKBitmap? flat = null;
        try
        {
            var source = bitmap;
            if (format == SKEncodedImageFormat.Jpeg && !IsOpaque(bitmap))
            {
                // JPEG has no transparency: put see-through parts on white instead of letting them turn black.
                flat = new SKBitmap(bitmap.Info);
                using var c = new SKCanvas(flat);
                c.Clear(SKColors.White);
                c.DrawBitmap(bitmap, 0, 0);
                source = flat;
            }

            using var data = source.Encode(format, quality) ?? throw new IOException("The image could not be encoded.");
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            var temp = path + ".tmp";
            using (var fs = File.Create(temp))
                data.SaveTo(fs);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            flat?.Dispose();
        }
    }

    private static bool IsOpaque(SKBitmap bmp)
    {
        if (bmp.AlphaType == SKAlphaType.Opaque)
            return true;
        var span = bmp.GetPixelSpan();
        for (var i = 3; i < span.Length; i += 4)
            if (span[i] != 255)
                return false;
        return true;
    }
}
