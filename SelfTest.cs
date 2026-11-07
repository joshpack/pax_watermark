using System.Text;
using Avalonia;
using Avalonia.LogicalTree;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using PaxWatermark.Imaging;
using PaxWatermark.Services;
using PaxWatermark.Views;
using SkiaSharp;

namespace PaxWatermark;

/// <summary>
/// --self-test DIR: stamps generated pictures (no real photos are read) one at a time and as a batch, checks the
/// outputs, renders the main window (with its PAX title bar) to PNGs and checks remembered placement and the
/// single-instance hand-off. It runs on Avalonia's headless platform: no OS window is created, so nothing appears on the screen.
/// </summary>
internal static class SelfTest
{
    public static string? Dir { get; set; }

    private static bool _ok;
    private static StringBuilder _r = new();

    private static void Check(string name, bool pass, string detail = "")
    {
        _r.AppendLine($"[{(pass ? "PASS" : "FAIL")}] {name}{(detail.Length > 0 ? " - " + detail : "")}");
        _ok &= pass;
    }

    /// <summary>Runs everything on the headless platform, writes report.txt, and returns the exit code.</summary>
    public static int RunAndReport()
    {
        var report = new StringBuilder();
        bool ok;
        try
        {
            ok = Run(report);
        }
        catch (Exception ex)
        {
            report.AppendLine("CRASH: " + ex);
            ok = false;
        }

        report.AppendLine(ok ? "RESULT: PASS" : "RESULT: FAIL");
        File.WriteAllText(Path.Combine(Dir!, "report.txt"), report.ToString());
        return ok ? 0 : 1;
    }

    public static bool Run(StringBuilder report)
    {
        _r = report;
        _ok = true;
        var dir = Dir!;
        report.AppendLine($"PAX Watermark {typeof(SelfTest).Assembly.GetName().Version?.ToString(3)} self-test");
        report.AppendLine($"Settings folder: {(AppSettings.Folder.StartsWith(dir, StringComparison.OrdinalIgnoreCase) ? "<test dir>\\data" : "PAXWM_DATA")}");

        using var photo = Photo(1600, 1000);
        using var watermark = Watermark(640, 240);
        Check("Generated photo and watermark", photo.Width == 1600 && watermark.Width == 640);

        // ---- one image
        var placement = new Placement(0.5, 0.5, 0.4, 1, 0);
        using (var stamped = Stamper.Stamp(photo, watermark, placement))
        {
            Check("Stamp keeps full resolution", stamped.Width == 1600 && stamped.Height == 1000, $"{stamped.Width}x{stamped.Height}");
            var center = stamped.GetPixel(800, 500) != photo.GetPixel(800, 500);
            var corner = stamped.GetPixel(4, 4) == photo.GetPixel(4, 4);
            Check("Watermark drawn at the center, corner untouched", center && corner);

            foreach (var name in new[] { "single.jpg", "single.png", "single.webp" })
            {
                var path = Path.Combine(dir, name);
                Stamper.Save(stamped, path);
                using var back = Stamper.Load(path);
                var format = Format(path);
                Check($"Save + reload {name}", back.Width == 1600 && back.Height == 1000 && format == Stamper.FormatFor(path),
                    $"{back.Width}x{back.Height}, {format}");
            }
        }

        using (var turned = Stamper.Stamp(photo, watermark, new Placement(0.8, 0.82, 0.3, 0.6, -20)))
        {
            Stamper.Save(turned, Path.Combine(dir, "single-rotated.png"));
            // Half opacity: the pixel at the watermark's center is a mix, not the photo and not the watermark.
            var p = turned.GetPixel(1280, 820);
            Check("Rotated, 60% opacity stamp", p != photo.GetPixel(1280, 820));
        }

        // ---- batch over three generated images (plus a file that isn't one)
        var batchDir = Path.Combine(dir, "batch");
        if (Directory.Exists(batchDir))
            Directory.Delete(batchDir, recursive: true);
        Directory.CreateDirectory(batchDir);
        var inputs = new (string Name, int W, int H)[] { ("landscape.jpg", 1200, 800), ("portrait.png", 800, 1200), ("square.webp", 1000, 1000) };
        foreach (var (name, w, h) in inputs)
        {
            using var img = Photo(w, h);
            Stamper.Save(img, Path.Combine(batchDir, name));
        }

        File.WriteAllText(Path.Combine(batchDir, "notes.txt"), "not an image");
        var watermarkFile = Path.Combine(batchDir, "logo.png");
        Stamper.Save(watermark, watermarkFile);   // the watermark itself in the folder must be left out

        var progressCalls = 0;
        var result = Batch.Run(batchDir, watermark, Placement.Default, watermarkFile, (_, _, _) => progressCalls++);
        Check("Batch stamped 3 of 3", result.Done == 3 && result.Failed.Count == 0, $"done {result.Done}, failed {result.Failed.Count}, progress calls {progressCalls}");
        Check("Watermark file and notes.txt left out", !File.Exists(Path.Combine(result.OutputFolder, "logo.png")) && !File.Exists(Path.Combine(result.OutputFolder, "notes.txt")));
        foreach (var (name, w, h) in inputs)
        {
            var output = Path.Combine(result.OutputFolder, name);
            if (!File.Exists(output))
            {
                Check($"Batch output {name}", false, "missing");
                continue;
            }

            using var back = Stamper.Load(output);
            using var source = Stamper.Load(Path.Combine(batchDir, name));
            // Same relative spot on every size: the watermark's center sits at 78% / 82% of each photo.
            var x = (int)(Placement.Default.CenterX * w);
            var y = (int)(Placement.Default.CenterY * h);
            Check($"Batch output {name}", back.Width == w && back.Height == h && Format(output) == Stamper.FormatFor(name) && back.GetPixel(x, y) != source.GetPixel(x, y),
                $"{back.Width}x{back.Height}, {Format(output)}");
        }

        // A second run replaces its own outputs and never picks up the "watermarked" folder.
        var again = Batch.Run(batchDir, watermark, Placement.Default, watermarkFile);
        Check("Batch run twice", again.Done == 3 && Directory.GetFiles(result.OutputFolder).Length == 3);

        // ---- EXIF orientation: a sideways phone photo (orientation 6 = turn 90° clockwise)
        using (var raw = new SKBitmap(new SKImageInfo(300, 200, SKColorType.Bgra8888, SKAlphaType.Premul)))
        {
            using (var c = new SKCanvas(raw))
            {
                c.Clear(new SKColor(30, 60, 200));
                using var red = new SKPaint { Color = new SKColor(230, 20, 20) };
                c.DrawRect(0, 0, 60, 60, red);   // top-left in the stored pixels
            }

            using var jpeg = raw.Encode(SKEncodedImageFormat.Jpeg, 95);
            var exifPath = Path.Combine(dir, "exif-rotated.jpg");
            File.WriteAllBytes(exifPath, WithOrientation(jpeg.ToArray(), 6));
            using var upright = Stamper.Load(exifPath);
            var px = upright.GetPixel(upright.Width - 10, 10);   // after turning, the red block is top-right
            Check("EXIF orientation applied", upright.Width == 200 && upright.Height == 300 && px.Red > 150 && px.Blue < 100,
                $"{upright.Width}x{upright.Height}, top-right pixel {px}");
        }

        Check("Unreadable file is reported, not a crash", Throws(() => Stamper.Load(Path.Combine(batchDir, "notes.txt"))));

        // ---- settings
        var s = new AppSettings { Size = 0.33, Opacity = 0.44, Rotation = 12, PhotoFolder = dir, FitToWindow = false };
        s.Save();
        var loaded = AppSettings.Load();
        Check("Settings saved and loaded", File.Exists(AppSettings.FilePath) && loaded.Size == 0.33 && loaded.Opacity == 0.44 && loaded.Rotation == 12 && loaded.PhotoFolder == dir && !loaded.FitToWindow);

        // ---- the window, off-screen
        var samples = Path.Combine(dir, "samples");
        Directory.CreateDirectory(samples);
        Stamper.Save(photo, Path.Combine(samples, "lake-sunset.jpg"));
        Stamper.Save(watermark, Path.Combine(samples, "studio-logo.png"));
        RenderWindow("window-empty.png", null);
        RenderWindow("window.png", w =>
        {
            w.SetPhoto(Stamper.Load(Path.Combine(samples, "lake-sunset.jpg")), Path.Combine(samples, "lake-sunset.jpg"));
            w.SetWatermark(Stamper.Load(Path.Combine(samples, "studio-logo.png")), Path.Combine(samples, "studio-logo.png"));
            w.SetStatus("Watermark studio-logo.png (640 × 240). Drag it on the photo, or use the sliders.");
        });

        CheckPlacement();
        CheckSingleInstance();
        return _ok;
    }

    /// <summary>PaxChrome.RememberPlacement: a moved and resized window is saved and restored; a spot off every screen is ignored.</summary>
    private static void CheckPlacement()
    {
        const string key = "selftest";
        var file = Path.Combine(AppSettings.Folder, "windows.json");
        if (File.Exists(file))
            File.Delete(file);

        var a = OffScreen(new MainWindow(new AppSettings()));
        PaxStandard.PaxChrome.RememberPlacement(a, key);
        a.Show();
        Pump();
        a.Width = 900;
        a.Height = 600;
        a.Position = new PixelPoint(-29000, -29000);
        a.UpdateLayout();
        Pump();
        a.Close();
        var json = File.Exists(file) ? File.ReadAllText(file) : "";
        Check("Placement saved to windows.json", json.Contains("\"selftest\"") && json.Contains("\"Width\":900") && json.Contains("\"Height\":600") && json.Contains("\"X\":-29000"), json);

        // Size comes back; the position is off every screen, so it is ignored and the window keeps its own spot.
        var b = OffScreen(new MainWindow(new AppSettings()));
        PaxStandard.PaxChrome.RememberPlacement(b, key);
        Check("Placement restored (size), off-screen spot ignored", b.Width == 900 && b.Height == 600 && b.Position == new PixelPoint(-30000, -30000),
            $"{b.Width}x{b.Height} at {b.Position}");

        // An on-screen spot and the maximized flag come back too (this window is never shown).
        File.WriteAllText(file, """{"selftest":{"X":120,"Y":90,"Width":1000,"Height":640,"Maximized":true}}""");
        var c = OffScreen(new MainWindow(new AppSettings()));
        PaxStandard.PaxChrome.RememberPlacement(c, key);
        Check("Placement restored (position, maximized)", c.Position == new PixelPoint(120, 90) && c.WindowState == WindowState.Maximized && c.Width == 1000,
            $"{c.Width}x{c.Height} at {c.Position}, {c.WindowState}");
        Check("Title bar: icon, name, minimize / maximize / close",
            HasTitleBar(c, "PAX Watermark", captionButtons: 3));
    }

    /// <summary>A second start hands its arguments to the running copy over the named pipe.</summary>
    private static void CheckSingleInstance()
    {
        var name = "PaxWatermark-selftest-" + Guid.NewGuid().ToString("N")[..8];
        string[]? got = null;
        using var done = new ManualResetEventSlim();
        using (SingleInstance.Listen(name, args => { got = args; done.Set(); }))
        {
            var sent = SingleInstance.Send(name, ["C:\\Photos\\sample photo.jpg"]);
            done.Wait(3000);
            Check("Second start forwards its arguments to the running copy", sent && got is ["C:\\Photos\\sample photo.jpg"], got is null ? "nothing received" : string.Join("|", got));
        }
    }

    private static void Pump()
    {
        for (var i = 0; i < 5; i++)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }

    private static MainWindow OffScreen(MainWindow w)
    {
        w.WindowStartupLocation = WindowStartupLocation.Manual;
        w.Position = new PixelPoint(-30000, -30000);
        w.ShowActivated = false;
        w.ShowInTaskbar = false;
        return w;
    }

    private static bool HasTitleBar(Window w, string title, int captionButtons)
    {
        var all = w.GetLogicalDescendants().ToList();
        return w.WindowDecorations == WindowDecorations.BorderOnly
               && all.OfType<TextBlock>().Any(t => t.Text == title)
               && all.OfType<Image>().Any(i => i.Width == 18)
               && all.OfType<Button>().Count(b => b.Classes.Contains("pax-caption")) == captionButtons
               && all.OfType<Button>().Any(b => b.Classes.Contains("pax-close"));
    }

    private static void RenderWindow(string file, Action<MainWindow>? setup)
    {
        var settings = new AppSettings { FitToWindow = true, Placement = new Placement(0.79, 0.84, 0.3, 0.8, -8) };
        var window = new MainWindow(settings)
        {
            // Headless already keeps it off the screen; these keep it that way if run on a real platform.
            WindowStartupLocation = WindowStartupLocation.Manual,
            Position = new PixelPoint(-30000, -30000),
            ShowActivated = false,
            ShowInTaskbar = false,
            Width = 1180,
            Height = 760,
        };
        window.Show();
        setup?.Invoke(window);
        window.UpdateLayout();
        window.UpdateLayout();   // fit-to-window changes the zoom during the first pass
        Pump();
        using var bmp = new RenderTargetBitmap(new PixelSize(1180, 760));
        bmp.Render(window);
        var path = Path.Combine(Dir!, file);
        bmp.Save(path, new PngBitmapEncoderOptions());
        window.Close();
        Check($"Window rendered to {file}", File.Exists(path) && new FileInfo(path).Length > 10_000, $"{new FileInfo(path).Length / 1024} KB");
    }

    private static SKEncodedImageFormat Format(string path)
    {
        using var codec = SKCodec.Create(path);
        return codec?.EncodedFormat ?? (SKEncodedImageFormat)(-1);
    }

    private static bool Throws(Action a)
    {
        try
        {
            a();
            return false;
        }
        catch (InvalidDataException)
        {
            return true;
        }
    }

    /// <summary>Puts a minimal EXIF block with the given orientation right after the JPEG's start marker.</summary>
    private static byte[] WithOrientation(byte[] jpeg, ushort orientation)
    {
        byte[] app1 =
        [
            0xFF, 0xE1, 0x00, 0x22,                         // APP1, length 34
            (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0,
            (byte)'I', (byte)'I', 0x2A, 0x00, 0x08, 0, 0, 0, // little-endian TIFF, first IFD at 8
            0x01, 0x00,                                     // one entry
            0x12, 0x01, 0x03, 0x00, 0x01, 0, 0, 0,          // Orientation, SHORT, count 1
            (byte)orientation, 0, 0, 0,
            0, 0, 0, 0,                                     // no next IFD
        ];
        return [.. jpeg[..2], .. app1, .. jpeg[2..]];
    }

    // ---------------------------------------------------------------- generated pictures

    /// <summary>A made-up sunset over a lake, any size.</summary>
    public static SKBitmap Photo(int w, int h)
    {
        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var c = new SKCanvas(bmp);
        float W = w, H = h, horizon = H * 0.62f;
        using (var sky = new SKPaint { Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, horizon),
                   [new SKColor(22, 18, 64), new SKColor(140, 52, 120), new SKColor(255, 140, 70), new SKColor(255, 206, 120)],
                   [0, 0.45f, 0.85f, 1], SKShaderTileMode.Clamp) })
            c.DrawRect(0, 0, W, horizon + 1, sky);

        var sunR = Math.Min(W, H) * 0.09f;
        using (var glow = new SKPaint { IsAntialias = true, Shader = SKShader.CreateRadialGradient(new SKPoint(W * 0.62f, horizon - sunR * 0.9f), sunR * 3.2f,
                   [new SKColor(255, 240, 190, 200), new SKColor(255, 180, 90, 0)], SKShaderTileMode.Clamp) })
            c.DrawCircle(W * 0.62f, horizon - sunR * 0.9f, sunR * 3.2f, glow);
        using (var sun = new SKPaint { IsAntialias = true, Color = new SKColor(255, 236, 170) })
            c.DrawCircle(W * 0.62f, horizon - sunR * 0.9f, sunR, sun);

        void Hills(float baseY, float amp, float phase, SKColor color)
        {
            using var path = new SKPath();
            path.MoveTo(0, H);
            for (var x = 0f; x <= W; x += W / 60)
                path.LineTo(x, baseY - amp * (0.6f + 0.4f * MathF.Sin(x / W * 7 + phase)) * (0.7f + 0.3f * MathF.Sin(x / W * 17 + phase * 2)));
            path.LineTo(W, H);
            path.Close();
            using var p = new SKPaint { IsAntialias = true, Color = color };
            c.DrawPath(path, p);
        }

        Hills(horizon, H * 0.16f, 1.3f, new SKColor(92, 44, 92));
        Hills(horizon, H * 0.09f, 4.1f, new SKColor(54, 28, 66));

        using (var lake = new SKPaint { Shader = SKShader.CreateLinearGradient(new SKPoint(0, horizon), new SKPoint(0, H),
                   [new SKColor(230, 130, 90), new SKColor(70, 40, 90), new SKColor(16, 18, 40)], [0, 0.4f, 1], SKShaderTileMode.Clamp) })
            c.DrawRect(0, horizon, W, H - horizon, lake);
        using (var shimmer = new SKPaint { IsAntialias = true, Color = new SKColor(255, 230, 170, 120), StrokeWidth = Math.Max(1, H / 300), StrokeCap = SKStrokeCap.Round })
            for (var i = 0; i < 14; i++)
            {
                var y = horizon + (H - horizon) * (0.06f + i * 0.065f);
                var half = sunR * (1.4f - i * 0.07f);
                c.DrawLine(W * 0.62f - half, y, W * 0.62f + half, y, shimmer);
            }

        return bmp;
    }

    /// <summary>A made-up studio logo on a transparent background.</summary>
    public static SKBitmap Watermark(int w, int h)
    {
        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var c = new SKCanvas(bmp);
        c.Clear(SKColors.Transparent);
        var r = new SKRect(h * 0.08f, h * 0.08f, w - h * 0.08f, h * 0.92f);
        using (var frame = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = h * 0.035f, Color = SKColors.White })
            c.DrawRoundRect(r, h * 0.16f, h * 0.16f, frame);
        using var typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) ?? SKTypeface.Default;
        using var big = new SKFont(typeface, h * 0.36f);
        using var small = new SKFont(typeface, h * 0.13f);
        using var white = new SKPaint { IsAntialias = true, Color = SKColors.White };
        using var shadow = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 110), ImageFilter = SKImageFilter.CreateBlur(h * 0.02f, h * 0.02f) };
        foreach (var paint in new[] { shadow, white })
        {
            var dy = paint == shadow ? h * 0.015f : 0;
            c.DrawText("SAMPLE", w / 2f, h * 0.56f + dy, SKTextAlign.Center, big, paint);
            c.DrawText("STUDIO · PHOTOGRAPHY", w / 2f, h * 0.78f + dy, SKTextAlign.Center, small, paint);
        }

        return bmp;
    }
}
