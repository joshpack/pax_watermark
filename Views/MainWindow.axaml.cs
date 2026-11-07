using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PaxWatermark.Imaging;
using PaxWatermark.Services;
using SkiaSharp;

namespace PaxWatermark.Views;

/// <summary>The whole app: open a photo and a watermark, place it, save one image or stamp a folder.</summary>
public partial class MainWindow : Window
{
    private static readonly FilePickerFileType ImageFiles = new("Images")
    {
        Patterns = Stamper.InputExtensions.Select(e => "*" + e).ToArray(),
        MimeTypes = ["image/jpeg", "image/png", "image/webp", "image/bmp", "image/gif"],
    };

    private static readonly IBrush Good = new SolidColorBrush(Color.Parse("#19F58C"));
    private static readonly IBrush Info = new SolidColorBrush(Color.Parse("#00C8FF"));
    private static readonly IBrush Warn = new SolidColorBrush(Color.Parse("#F5B14C"));
    private static readonly IBrush Bad = new SolidColorBrush(Color.Parse("#F2666A"));

    private readonly AppSettings _s;
    private SKBitmap? _photo, _watermark;
    private string? _photoPath, _watermarkPath;
    private bool _loading, _busy;
    private CancellationTokenSource? _batchCancel;

    public MainWindow() : this(new AppSettings())
    {
    }

    public MainWindow(AppSettings settings)
    {
        _s = settings;
        InitializeComponent();
        Icon = AppIcon.Create(128);
        Ctl<ContentControl>("HeaderArt").Content = AppIcon.Art();

        _loading = true;
        Ctl<Slider>("SizeSlider").Value = Math.Round(_s.Placement.Size * 100);
        Ctl<Slider>("OpacitySlider").Value = Math.Round(_s.Placement.Opacity * 100);
        Ctl<Slider>("RotationSlider").Value = Math.Round(_s.Placement.Rotation);
        Ctl<ToggleButton>("FitToggle").IsChecked = _s.FitToWindow;
        Stage.Placement = _s.Placement;
        _loading = false;

        foreach (var name in new[] { "SizeSlider", "OpacitySlider", "RotationSlider" })
            Ctl<Slider>(name).PropertyChanged += (_, e) =>
            {
                if (e.Property == RangeBase.ValueProperty)
                    SliderMoved();
            };

        Ctl<Button>("OpenPhotoButton").Click += async (_, _) => await PickPhoto();
        Ctl<Button>("OpenWatermarkButton").Click += async (_, _) => await PickWatermark();
        Ctl<Button>("SaveButton").Click += async (_, _) => await SaveStamped();
        Ctl<Button>("BatchButton").Click += async (_, _) => await BatchFolder();
        Ctl<Button>("ResetButton").Click += (_, _) => SetPlacement(Placement.Default, syncSliders: true);
        Ctl<Button>("ActualSizeButton").Click += (_, _) => SetZoom(1);
        Ctl<ToggleButton>("FitToggle").IsCheckedChanged += (_, _) =>
        {
            if (_loading)
                return;
            _s.FitToWindow = Ctl<ToggleButton>("FitToggle").IsChecked == true;
            if (_s.FitToWindow)
                ApplyFit();
        };

        Stage.PlacementChanged += p =>
        {
            _s.Placement = p;
            ShowValues();
        };
        Stage.DragFinished += _s.Save;

        Viewer.AddHandler(PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);
        Viewer.PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty && _s.FitToWindow)
                ApplyFit();
        };

        BuildSpots();
        ShowValues();
        UpdateState();
        Closing += (_, _) =>
        {
            _batchCancel?.Cancel();
            _s.Save();
        };
    }

    private StampCanvas Stage => Ctl<StampCanvas>("Preview");
    private ScrollViewer Viewer => Ctl<ScrollViewer>("Scroll");

    private T Ctl<T>(string name) where T : Control => this.FindControl<T>(name)!;

    // ---------------------------------------------------------------- photo and watermark

    /// <summary>Shows a photo that is already decoded (the self-test uses this directly).</summary>
    public void SetPhoto(SKBitmap photo, string? path)
    {
        var old = _photo;
        _photo = photo;
        _photoPath = path;
        Stage.Photo = ToAvalonia(photo);
        old?.Dispose();
        Ctl<TextBlock>("PhotoName").Text = path is null ? "Untitled" : Path.GetFileName(path);
        Ctl<TextBlock>("PhotoInfo").Text = $"{photo.Width} × {photo.Height} px · saved at full size";
        if (_s.FitToWindow)
            ApplyFit();
        UpdateState();
    }

    public void SetWatermark(SKBitmap watermark, string? path)
    {
        var old = _watermark;
        _watermark = watermark;
        _watermarkPath = path;
        var shown = ToAvalonia(watermark);
        Stage.Watermark = shown;
        Ctl<Image>("WatermarkThumb").Source = shown;
        old?.Dispose();
        Ctl<TextBlock>("WatermarkName").Text = path is null ? "Untitled" : Path.GetFileName(path);
        Ctl<TextBlock>("WatermarkInfo").Text = $"{watermark.Width} × {watermark.Height} px";
        UpdateState();
    }

    /// <summary>Opens the watermark used last time, if it is still there.</summary>
    public async Task RestoreLastWatermark()
    {
        if (_s.WatermarkFile is { } last && File.Exists(last))
            await LoadWatermark(last, quiet: true);
    }

    private async Task PickPhoto()
    {
        if (_busy)
            return;
        var path = await PickFile("Open a photo", _s.PhotoFolder);
        if (path is null)
            return;
        _s.PhotoFolder = Path.GetDirectoryName(path);
        await LoadPhoto(path);
    }

    private async Task PickWatermark()
    {
        if (_busy)
            return;
        var path = await PickFile("Open a watermark image", _s.WatermarkFolder ?? _s.PhotoFolder);
        if (path is null)
            return;
        _s.WatermarkFolder = Path.GetDirectoryName(path);
        await LoadWatermark(path, quiet: false);
    }

    public Task OpenPhoto(string path) => LoadPhoto(path);

    private async Task LoadPhoto(string path)
    {
        SetStatus($"Opening {Path.GetFileName(path)}…", Info);
        SetBusy(true);
        try
        {
            var bmp = await Task.Run(() => Stamper.Load(path));
            SetPhoto(bmp, path);
            SetStatus(_watermark is null
                ? $"Opened {Path.GetFileName(path)} ({bmp.Width} × {bmp.Height}). Now open a watermark."
                : $"Opened {Path.GetFileName(path)} ({bmp.Width} × {bmp.Height}). Drag the watermark into place.", Good);
            _s.Save();
        }
        catch (Exception ex)
        {
            SetStatus($"Couldn't open {Path.GetFileName(path)}: {ex.Message}", Bad);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task LoadWatermark(string path, bool quiet)
    {
        SetBusy(true);
        try
        {
            var bmp = await Task.Run(() => Stamper.Load(path));
            SetWatermark(bmp, path);
            _s.WatermarkFile = path;
            _s.Save();
            SetStatus(quiet
                ? $"Using last watermark {Path.GetFileName(path)}. Open a photo to start."
                : $"Watermark {Path.GetFileName(path)} ({bmp.Width} × {bmp.Height}). Drag it on the photo, or use the sliders.", Good);
        }
        catch (Exception ex)
        {
            if (!quiet)
                SetStatus($"Couldn't open {Path.GetFileName(path)}: {ex.Message}", Bad);
        }
        finally
        {
            SetBusy(false);
        }
    }

    // ---------------------------------------------------------------- saving

    private async Task SaveStamped()
    {
        if (_photo is null || _watermark is null || _busy)
            return;

        var folder = _s.SaveFolder ?? (_photoPath is null ? null : Path.GetDirectoryName(_photoPath));
        var baseName = _photoPath is null ? "photo" : Path.GetFileNameWithoutExtension(_photoPath);
        var ext = _photoPath is null ? ".png" : Stamper.OutputExtension(_photoPath);
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save the stamped image",
            SuggestedFileName = baseName + "_watermarked" + ext,
            DefaultExtension = ext.TrimStart('.'),
            ShowOverwritePrompt = true,
            SuggestedStartLocation = await FolderAt(folder),
            FileTypeChoices =
            [
                new FilePickerFileType("JPEG image") { Patterns = ["*.jpg", "*.jpeg"], MimeTypes = ["image/jpeg"] },
                new FilePickerFileType("PNG image") { Patterns = ["*.png"], MimeTypes = ["image/png"] },
                new FilePickerFileType("WebP image") { Patterns = ["*.webp"], MimeTypes = ["image/webp"] },
            ],
        });
        var path = file?.TryGetLocalPath();
        if (path is null)
            return;

        // Only JPEG, PNG and WebP can be written: anything else becomes .png, so the name matches what's inside.
        if (Stamper.OutputExtension(path) != Path.GetExtension(path).ToLowerInvariant())
            path = Path.ChangeExtension(path, ".png");
        if (_photoPath is not null && string.Equals(Path.GetFullPath(path), Path.GetFullPath(_photoPath), StringComparison.OrdinalIgnoreCase))
        {
            SetStatus("That's the original photo. Pick another name so the original stays as it is.", Warn);
            return;
        }

        _s.SaveFolder = Path.GetDirectoryName(path);
        var photo = _photo;
        var watermark = _watermark;
        var placement = Stage.Placement;
        SetBusy(true);
        SetStatus($"Saving {Path.GetFileName(path)}…", Info);
        try
        {
            await Task.Run(() =>
            {
                using var stamped = Stamper.Stamp(photo, watermark, placement);
                Stamper.Save(stamped, path);
            });
            SetStatus($"Saved {path} ({photo.Width} × {photo.Height}).", Good);
        }
        catch (Exception ex)
        {
            SetStatus($"Couldn't save {Path.GetFileName(path)}: {ex.Message}", Bad);
        }
        finally
        {
            SetBusy(false);
            _s.Save();
        }
    }

    // ---------------------------------------------------------------- batch

    private async Task BatchFolder()
    {
        if (_batchCancel is not null)
        {
            _batchCancel.Cancel();
            return;
        }

        if (_watermark is null || _busy)
            return;

        var start = _s.BatchFolder ?? (_photoPath is null ? _s.PhotoFolder : Path.GetDirectoryName(_photoPath));
        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Stamp every image in this folder",
            AllowMultiple = false,
            SuggestedStartLocation = await FolderAt(start),
        });
        var folder = picked.FirstOrDefault()?.TryGetLocalPath();
        if (folder is null)
            return;
        _s.BatchFolder = folder;

        var count = Batch.FilesIn(folder, _watermarkPath).Count;
        if (count == 0)
        {
            SetStatus($"No images in {folder} (looked for {string.Join(", ", Stamper.InputExtensions)}).", Warn);
            return;
        }

        var watermark = _watermark;
        var placement = Stage.Placement;
        _batchCancel = new CancellationTokenSource();
        var cancel = _batchCancel.Token;
        SetBusy(true);
        Ctl<Button>("BatchButton").Content = "Stop batch";
        Ctl<Button>("BatchButton").IsEnabled = true;
        try
        {
            var result = await Task.Run(() => Batch.Run(folder, watermark, placement, _watermarkPath,
                (i, total, name) => Dispatcher.UIThread.Post(() =>
                {
                    if (name.Length > 0)
                        SetStatus($"Stamping {i + 1} of {total}: {name}", Info);
                }), cancel));

            var text = $"Stamped {result.Done} of {count} image{(count == 1 ? "" : "s")} into {result.OutputFolder}.";
            if (result.Skipped > 0)
                text += $" Stopped, {result.Skipped} not done.";
            if (result.Failed.Count > 0)
                text += $" {result.Failed.Count} failed: {string.Join("; ", result.Failed.Take(3))}";
            SetStatus(text, result.Failed.Count > 0 || result.Skipped > 0 ? Warn : Good);
        }
        catch (Exception ex)
        {
            SetStatus($"Batch stopped: {ex.Message}", Bad);
        }
        finally
        {
            _batchCancel.Dispose();
            _batchCancel = null;
            Ctl<Button>("BatchButton").Content = "Batch: stamp a folder…";
            SetBusy(false);
            _s.Save();
        }
    }

    // ---------------------------------------------------------------- placement

    private void SliderMoved()
    {
        if (_loading)
            return;
        SetPlacement(Stage.Placement with
        {
            Size = Ctl<Slider>("SizeSlider").Value / 100,
            Opacity = Ctl<Slider>("OpacitySlider").Value / 100,
            Rotation = Ctl<Slider>("RotationSlider").Value,
        }, syncSliders: false);
    }

    private void SetPlacement(Placement p, bool syncSliders)
    {
        Stage.Placement = p;
        _s.Placement = p;
        if (syncSliders)
        {
            _loading = true;
            Ctl<Slider>("SizeSlider").Value = Math.Round(p.Size * 100);
            Ctl<Slider>("OpacitySlider").Value = Math.Round(p.Opacity * 100);
            Ctl<Slider>("RotationSlider").Value = Math.Round(p.Rotation);
            _loading = false;
        }

        ShowValues();
    }

    private void ShowValues()
    {
        var p = Stage.Placement;
        Ctl<TextBlock>("SizeValue").Text = $"{p.Size * 100:0}%";
        Ctl<TextBlock>("OpacityValue").Text = $"{p.Opacity * 100:0}%";
        Ctl<TextBlock>("RotationValue").Text = $"{p.Rotation:0}°";
        Ctl<TextBlock>("PositionValue").Text = $"{p.CenterX * 100:0}% · {p.CenterY * 100:0}%";
    }

    /// <summary>Nine buttons that put the watermark in a corner, an edge middle or the center, a small margin from the edge.</summary>
    private void BuildSpots()
    {
        var grid = Ctl<UniformGrid>("SpotGrid");
        for (var i = 0; i < 9; i++)
        {
            var spot = i;
            var b = new Button
            {
                Margin = new Thickness(2), MinHeight = 0, Padding = new Thickness(0), CornerRadius = new CornerRadius(5),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
                Content = new Avalonia.Controls.Shapes.Ellipse { Width = 6, Height = 6, Fill = Info },
            };
            ToolTip.SetTip(b, new[] { "Top left", "Top", "Top right", "Left", "Center", "Right", "Bottom left", "Bottom", "Bottom right" }[i]);
            b.Click += (_, _) => SetPlacement(SpotPlacement(spot), syncSliders: false);
            grid.Children.Add(b);
        }
    }

    private Placement SpotPlacement(int spot)
    {
        var p = Stage.Placement;
        double W = _photo?.Width ?? 1000, H = _photo?.Height ?? 1000;
        var ratio = _watermark is null ? 1 : (double)_watermark.Height / _watermark.Width;
        var w = p.Size * W;
        var h = w * ratio;
        // The space the turned watermark takes up, so a rotated logo doesn't poke out of the photo.
        var a = p.Rotation * Math.PI / 180;
        var bw = Math.Abs(w * Math.Cos(a)) + Math.Abs(h * Math.Sin(a));
        var bh = Math.Abs(w * Math.Sin(a)) + Math.Abs(h * Math.Cos(a));
        var margin = 0.03 * Math.Min(W, H);
        double X(int c) => c switch { 0 => (margin + bw / 2) / W, 1 => 0.5, _ => 1 - (margin + bw / 2) / W };
        double Y(int r) => r switch { 0 => (margin + bh / 2) / H, 1 => 0.5, _ => 1 - (margin + bh / 2) / H };
        return p with { CenterX = Math.Clamp(X(spot % 3), 0, 1), CenterY = Math.Clamp(Y(spot / 3), 0, 1) };
    }

    // ---------------------------------------------------------------- zoom

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (_photo is null)
            return;
        e.Handled = true;
        var before = Stage.Zoom;
        var imagePoint = e.GetPosition(Stage) / before;
        var viewPoint = e.GetPosition(Viewer);
        SetZoom(before * Math.Pow(1.15, e.Delta.Y));

        // Keep the spot under the mouse where it was.
        Viewer.UpdateLayout();
        var z = Stage.Zoom;
        var origin = Stage.TranslatePoint(new Point(0, 0), Viewer) ?? default;
        var drift = new Point(origin.X + imagePoint.X * z, origin.Y + imagePoint.Y * z) - viewPoint;
        Viewer.Offset = new Vector(Viewer.Offset.X + drift.X, Viewer.Offset.Y + drift.Y);
    }

    /// <summary>Sets the preview zoom by hand, which turns "Fit to window" off.</summary>
    private void SetZoom(double zoom)
    {
        if (_s.FitToWindow)
        {
            _loading = true;
            Ctl<ToggleButton>("FitToggle").IsChecked = false;
            _s.FitToWindow = false;
            _loading = false;
        }

        Stage.Zoom = zoom;
        ShowZoom();
    }

    private void ApplyFit()
    {
        if (_photo is null)
            return;
        var area = Viewer.Bounds.Size;
        if (area.Width < 40 || area.Height < 40)
            return;
        const double pad = 2 * 12 + 2;   // the preview's margin, plus a pixel each side so no scroll bar appears
        Stage.Zoom = Math.Min((area.Width - pad) / _photo.Width, (area.Height - pad) / _photo.Height);
        ShowZoom();
    }

    private void ShowZoom() => Ctl<TextBlock>("ZoomText").Text = _photo is null ? "" : $"{Stage.Zoom * 100:0}%";

    // ---------------------------------------------------------------- keys

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || !e.KeyModifiers.HasFlag(KeyModifiers.Control))
            return;
        switch (e.Key)
        {
            case Key.O when e.KeyModifiers.HasFlag(KeyModifiers.Shift): _ = PickWatermark(); break;
            case Key.O: _ = PickPhoto(); break;
            case Key.S: _ = SaveStamped(); break;
            case Key.D0 or Key.NumPad0: Ctl<ToggleButton>("FitToggle").IsChecked = true; break;
            case Key.D1 or Key.NumPad1: SetZoom(1); break;
            default: return;
        }

        e.Handled = true;
    }

    // ---------------------------------------------------------------- helpers

    private void UpdateState()
    {
        Ctl<StackPanel>("EmptyState").IsVisible = _photo is null;
        Ctl<Button>("SaveButton").IsEnabled = !_busy && _photo is not null && _watermark is not null;
        Ctl<Button>("BatchButton").IsEnabled = _batchCancel is not null || (!_busy && _watermark is not null);
        Ctl<Button>("OpenPhotoButton").IsEnabled = !_busy;
        Ctl<Button>("OpenWatermarkButton").IsEnabled = !_busy;
        Ctl<Button>("ActualSizeButton").IsEnabled = _photo is not null;
        ShowZoom();
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        UpdateState();
    }

    public void SetStatus(string text, IBrush? color = null)
    {
        Ctl<TextBlock>("StatusText").Text = text;
        Ctl<Avalonia.Controls.Shapes.Ellipse>("StatusDot").Fill = color ?? Good;
    }

    public string Status => Ctl<TextBlock>("StatusText").Text ?? "";

    private async Task<string?> PickFile(string title, string? folder)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [ImageFiles, FilePickerFileTypes.All],
            SuggestedStartLocation = await FolderAt(folder),
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    private async Task<IStorageFolder?> FolderAt(string? path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            return null;
        try
        {
            return await StorageProvider.TryGetFolderFromPathAsync(path);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>A copy of the Skia pixels as an Avalonia bitmap for the screen (the Skia one is kept for saving).</summary>
    private static Bitmap ToAvalonia(SKBitmap b) =>
        new(PixelFormat.Bgra8888, AlphaFormat.Premul, b.GetPixels(), new PixelSize(b.Width, b.Height), new Vector(96, 96), b.RowBytes);
}
