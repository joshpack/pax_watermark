using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using PaxWatermark.Imaging;

namespace PaxWatermark.Views;

/// <summary>
/// The live preview: the photo at the current zoom with the watermark drawn on top, exactly where the saved file will
/// have it. Drag the watermark to move it; click anywhere else on the photo to move it there.
/// </summary>
public sealed class StampCanvas : Control
{
    private static readonly IPen Outline = new Pen(new SolidColorBrush(Color.Parse("#B000C8FF")), 1, new DashStyle([4, 3], 0));
    private static readonly IPen PhotoEdge = new Pen(new SolidColorBrush(Color.Parse("#272C39")), 1);
    private static readonly Cursor Move = new(StandardCursorType.SizeAll);

    private Bitmap? _photo, _watermark;
    private Placement _placement = Placement.Default;
    private double _zoom = 1;
    private bool _dragging;
    private Vector _grabOffset;

    /// <summary>The watermark moved (raised while dragging).</summary>
    public event Action<Placement>? PlacementChanged;

    /// <summary>A drag ended: a good moment to save the position.</summary>
    public event Action? DragFinished;

    public Bitmap? Photo
    {
        get => _photo;
        set { _photo = value; InvalidateMeasure(); InvalidateVisual(); }
    }

    public Bitmap? Watermark
    {
        get => _watermark;
        set { _watermark = value; InvalidateVisual(); }
    }

    public Placement Placement
    {
        get => _placement;
        set { _placement = value; InvalidateVisual(); }
    }

    /// <summary>Screen pixels per photo pixel.</summary>
    public double Zoom
    {
        get => _zoom;
        set { _zoom = Math.Clamp(value, 0.02, 8); InvalidateMeasure(); InvalidateVisual(); }
    }

    private Size PhotoSize => _photo is null ? default : new Size(_photo.PixelSize.Width, _photo.PixelSize.Height);

    protected override Size MeasureOverride(Size availableSize) => PhotoSize * _zoom;

    /// <summary>The watermark's center and size in this control's coordinates.</summary>
    private (Point Center, Size Size) WatermarkBox()
    {
        if (_photo is null || _watermark is null)
            return default;
        var ps = PhotoSize;
        var (w, h) = _placement.PixelSize(_photo.PixelSize.Width, _watermark.PixelSize.Width, _watermark.PixelSize.Height);
        return (new Point(_placement.CenterX * ps.Width * _zoom, _placement.CenterY * ps.Height * _zoom), new Size(w * _zoom, h * _zoom));
    }

    public override void Render(DrawingContext context)
    {
        if (_photo is null)
            return;
        var bounds = new Rect(PhotoSize * _zoom);
        context.DrawImage(_photo, new Rect(_photo.Size), bounds);
        context.DrawRectangle(Brushes.Transparent, PhotoEdge, bounds);   // also makes the whole photo clickable
        if (_watermark is null)
            return;

        var (center, size) = WatermarkBox();
        var rect = new Rect(-size.Width / 2, -size.Height / 2, size.Width, size.Height);
        using (context.PushTransform(Matrix.CreateRotation(_placement.Rotation * Math.PI / 180) * Matrix.CreateTranslation(center.X, center.Y)))
        {
            using (context.PushOpacity(Math.Clamp(_placement.Opacity, 0, 1)))
                context.DrawImage(_watermark, new Rect(_watermark.Size), rect);
            context.DrawRectangle(null, Outline, rect.Inflate(1));
        }
    }

    private bool HitsWatermark(Point p)
    {
        if (_watermark is null)
            return false;
        var (center, size) = WatermarkBox();
        var a = -_placement.Rotation * Math.PI / 180;
        var d = p - center;
        var lx = d.X * Math.Cos(a) - d.Y * Math.Sin(a);
        var ly = d.X * Math.Sin(a) + d.Y * Math.Cos(a);
        return Math.Abs(lx) <= size.Width / 2 + 2 && Math.Abs(ly) <= size.Height / 2 + 2;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        if (_photo is null || _watermark is null || !point.Properties.IsLeftButtonPressed)
            return;
        var p = point.Position;
        // Grabbed the watermark: keep the grab point under the mouse. Clicked elsewhere: jump there.
        _grabOffset = HitsWatermark(p) ? WatermarkBox().Center - p : default;
        _dragging = true;
        e.Pointer.Capture(this);
        MoveTo(p);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        if (_dragging)
            MoveTo(p);
        Cursor = _dragging || HitsWatermark(p) ? Move : null;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging)
            return;
        _dragging = false;
        e.Pointer.Capture(null);
        DragFinished?.Invoke();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_dragging)
        {
            _dragging = false;
            DragFinished?.Invoke();
        }
    }

    private void MoveTo(Point p)
    {
        var target = p + _grabOffset;
        var ps = PhotoSize * _zoom;
        if (ps.Width <= 0 || ps.Height <= 0)
            return;
        Placement = _placement with
        {
            CenterX = Math.Clamp(target.X / ps.Width, 0, 1),
            CenterY = Math.Clamp(target.Y / ps.Height, 0, 1),
        };
        PlacementChanged?.Invoke(_placement);
    }
}
