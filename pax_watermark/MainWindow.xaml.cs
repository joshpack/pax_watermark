using Microsoft.Win32;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace pax_watermark
{
    public partial class MainWindow : Window
    {
        private Point _lastMousePos;
        private bool _isDragging = false;
        private double _zoom = 1.0;

        public MainWindow()
        {
            InitializeComponent();

            // Initialize watermark settings
            WatermarkImage.Opacity = 0.7;

            // Enable zooming via mouse wheel
            ScrollArea.PreviewMouseWheel += ScrollArea_MouseWheel;
        }

        // Load main image from file
        private void LoadImage_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "Image files (*.png;*.jpg)|*.png;*.jpg" };
            if (dlg.ShowDialog() != true) return;

            var bitmap = new BitmapImage(new Uri(dlg.FileName));
            MainImage.Source = bitmap;

            // Reset transforms and position
            Canvas.SetLeft(MainImage, 0);
            Canvas.SetTop(MainImage, 0);
            MainImage.RenderTransform = Transform.Identity;

            // Resize canvas to image size
            ImageCanvas.Width = bitmap.PixelWidth;
            ImageCanvas.Height = bitmap.PixelHeight;

            FitToWindowCheckBox.IsChecked = false;
        }

        // Load watermark image and place it in canvas
        private void LoadWatermark_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "Image files (*.png;*.jpg)|*.png;*.jpg" };
            if (dlg.ShowDialog() != true) return;

            var wm = new BitmapImage(new Uri(dlg.FileName));
            WatermarkImage.Source = wm;

            // Position watermark initially
            Canvas.SetLeft(WatermarkImage, 50);
            Canvas.SetTop(WatermarkImage, 50);
        }

        // Save current canvas (main image + watermark) as PNG
        private void SaveImage_Click(object sender, RoutedEventArgs e)
        {
            if (MainImage.Source == null || WatermarkImage.Source == null) return;

            var bmp = new RenderTargetBitmap(
                (int)ImageCanvas.Width,
                (int)ImageCanvas.Height,
                96, 96,
                PixelFormats.Pbgra32);
            ImageCanvas.Measure(new Size(ImageCanvas.Width, ImageCanvas.Height));
            ImageCanvas.Arrange(new Rect(0, 0, ImageCanvas.Width, ImageCanvas.Height));
            bmp.Render(ImageCanvas);

            var dlg = new SaveFileDialog { Filter = "PNG Image|*.png" };
            if (dlg.ShowDialog() == true)
            {
                using var fs = new FileStream(dlg.FileName, FileMode.Create);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bmp));
                encoder.Save(fs);
            }
        }

        // Batch watermark multiple images in a folder
        private void BatchApply_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog();
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

            foreach (var file in Directory.GetFiles(dlg.SelectedPath, "*.jpg"))
            {
                LoadSingleFile(file);
                SaveWatermarkedImage(file);
            }

            MessageBox.Show("Batch watermarking complete.");
        }

        // Load single file in batch
        private void LoadSingleFile(string path)
        {
            var bm = new BitmapImage(new Uri(path));
            MainImage.Source = bm;
            Canvas.SetLeft(MainImage, 0);
            Canvas.SetTop(MainImage, 0);
            ImageCanvas.Width = bm.PixelWidth;
            ImageCanvas.Height = bm.PixelHeight;
        }

        // Save watermark applied version during batch operation
        private void SaveWatermarkedImage(string originalPath)
        {
            var bmp = new RenderTargetBitmap((int)ImageCanvas.Width,
                                             (int)ImageCanvas.Height,
                                             96, 96,
                                             PixelFormats.Pbgra32);
            ImageCanvas.Measure(new Size(ImageCanvas.Width, ImageCanvas.Height));
            ImageCanvas.Arrange(new Rect(0, 0, ImageCanvas.Width, ImageCanvas.Height));
            bmp.Render(ImageCanvas);

            string outpath = Path.Combine(Path.GetDirectoryName(originalPath),
                                           "watermarked_" + Path.GetFileName(originalPath));
            using var fs = new FileStream(outpath, FileMode.Create);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            encoder.Save(fs);
        }

        // Toggle fit-to-window scaling
        private void FitToWindowCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (MainImage.Source is BitmapSource bmp)
            {
                double scaleX = ScrollArea.ActualWidth / bmp.PixelWidth;
                double scaleY = ScrollArea.ActualHeight / bmp.PixelHeight;
                _zoom = Math.Min(scaleX, scaleY);
                MainImage.RenderTransform = new ScaleTransform(_zoom, _zoom);
            }
        }

        private void FitToWindowCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            MainImage.RenderTransform = Transform.Identity;
            _zoom = 1.0;
        }

        // Watermark opacity change
        private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (WatermarkImage != null)
                WatermarkImage.Opacity = e.NewValue;
        }

        // Watermark rotation change
        private void RotationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (WatermarkRotate != null)
                WatermarkRotate.Angle = e.NewValue;
        }

        // Watermark scale change
        private void ScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (WatermarkScale != null)
            {
                WatermarkScale.ScaleX = e.NewValue;
                WatermarkScale.ScaleY = e.NewValue;
            }
        }

        // Mouse wheel zoom handler
        private void ScrollArea_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            double factor = e.Delta > 0 ? 1.1 : 0.9;
            _zoom *= factor;
            MainImage.RenderTransform = new ScaleTransform(_zoom, _zoom);
        }

        // Start dragging watermark
        private void Watermark_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDragging = true;
            _lastMousePos = e.GetPosition(ImageCanvas);
            WatermarkImage.CaptureMouse();
        }

        // Move watermark along with mouse
        private void Watermark_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging) return;

            var pos = e.GetPosition(ImageCanvas);
            double dx = pos.X - _lastMousePos.X;
            double dy = pos.Y - _lastMousePos.Y;

            Canvas.SetLeft(WatermarkImage, Canvas.GetLeft(WatermarkImage) + dx);
            Canvas.SetTop(WatermarkImage, Canvas.GetTop(WatermarkImage) + dy);

            _lastMousePos = pos;
        }

        // End dragging watermark
        private void Watermark_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            WatermarkImage.ReleaseMouseCapture();
        }
    }
}
