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
        private Point _lastPosition;
        private bool _isDragging;
        private double _scale = 1.0;

        public MainWindow()
        {
            InitializeComponent();
            WatermarkImage.Opacity = 0.7;

            ScrollArea.PreviewMouseWheel += ScrollArea_MouseWheel;
        }

        private void LoadImage_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog();
            if (dlg.ShowDialog() == true)
            {
                var bitmap = new BitmapImage(new Uri(dlg.FileName));
                MainImage.Source = bitmap;

                Canvas.SetLeft(MainImage, 0);
                Canvas.SetTop(MainImage, 0);
                MainImage.RenderTransform = Transform.Identity;

                ImageCanvas.Width = bitmap.PixelWidth;
                ImageCanvas.Height = bitmap.PixelHeight;

                FitToWindowCheckBox.IsChecked = false;
            }
        }

        private void LoadWatermark_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog();
            if (dlg.ShowDialog() == true)
            {
                var bitmap = new BitmapImage(new Uri(dlg.FileName));
                WatermarkImage.Source = bitmap;
                Canvas.SetLeft(WatermarkImage, 50);
                Canvas.SetTop(WatermarkImage, 50);
            }
        }

        private void SaveImage_Click(object sender, RoutedEventArgs e)
        {
            if (MainImage.Source == null || WatermarkImage.Source == null) return;

            RenderTargetBitmap rtb = new(
                (int)ImageCanvas.Width,
                (int)ImageCanvas.Height,
                96, 96, PixelFormats.Pbgra32);
            ImageCanvas.Measure(new Size(ImageCanvas.Width, ImageCanvas.Height));
            ImageCanvas.Arrange(new Rect(0, 0, ImageCanvas.Width, ImageCanvas.Height));
            rtb.Render(ImageCanvas);

            var dlg = new SaveFileDialog { Filter = "PNG Image|*.png" };
            if (dlg.ShowDialog() == true)
            {
                using FileStream fs = new(dlg.FileName, FileMode.Create);
                PngBitmapEncoder encoder = new();
                encoder.Frames.Add(BitmapFrame.Create(rtb));
                encoder.Save(fs);
            }
        }

        private void BatchWatermark_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog();
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var files = Directory.GetFiles(dlg.SelectedPath, "*.jpg");
                foreach (var file in files)
                {
                    var bitmap = new BitmapImage(new Uri(file));
                    MainImage.Source = bitmap;
                    ImageCanvas.Width = bitmap.PixelWidth;
                    ImageCanvas.Height = bitmap.PixelHeight;

                    // Re-render with watermark
                    RenderTargetBitmap rtb = new(
                        (int)ImageCanvas.Width,
                        (int)ImageCanvas.Height,
                        96, 96, PixelFormats.Pbgra32);
                    ImageCanvas.Measure(new Size(ImageCanvas.Width, ImageCanvas.Height));
                    ImageCanvas.Arrange(new Rect(0, 0, ImageCanvas.Width, ImageCanvas.Height));
                    rtb.Render(ImageCanvas);

                    string output = System.IO.Path.Combine(dlg.SelectedPath, "watermarked_" + System.IO.Path.GetFileName(file));
                    using FileStream fs = new(output, FileMode.Create);
                    PngBitmapEncoder encoder = new();
                    encoder.Frames.Add(BitmapFrame.Create(rtb));
                    encoder.Save(fs);
                }

                MessageBox.Show("Batch watermarking completed.");
            }
        }

        private void FitToWindowCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (MainImage.Source == null) return;

            var bitmap = MainImage.Source as BitmapSource;
            double scaleX = ScrollArea.ActualWidth / bitmap.PixelWidth;
            double scaleY = ScrollArea.ActualHeight / bitmap.PixelHeight;
            _scale = Math.Min(scaleX, scaleY);

            MainImage.RenderTransform = new ScaleTransform(_scale, _scale);
        }

        private void FitToWindowCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            MainImage.RenderTransform = Transform.Identity;
            _scale = 1.0;
        }

        private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (WatermarkImage != null)
                WatermarkImage.Opacity = e.NewValue;
        }

        private void RotationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (WatermarkRotate != null)
                WatermarkRotate.Angle = e.NewValue;
        }

        private void Watermark_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDragging = true;
            _lastPosition = e.GetPosition(ImageCanvas);
            WatermarkImage.CaptureMouse();
        }

        private void Watermark_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging) return;

            Point current = e.GetPosition(ImageCanvas);
            double dx = current.X - _lastPosition.X;
            double dy = current.Y - _lastPosition.Y;

            double left = Canvas.GetLeft(WatermarkImage) + dx;
            double top = Canvas.GetTop(WatermarkImage) + dy;

            Canvas.SetLeft(WatermarkImage, left);
            Canvas.SetTop(WatermarkImage, top);

            _lastPosition = current;
        }

        private void ScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (WatermarkScale != null)
            {
                WatermarkScale.ScaleX = e.NewValue;
                WatermarkScale.ScaleY = e.NewValue;
            }
        }

        private void Watermark_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            WatermarkImage.ReleaseMouseCapture();
        }

        private void ScrollArea_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            double zoomFactor = e.Delta > 0 ? 1.1 : 0.9;
            _scale *= zoomFactor;

            MainImage.RenderTransform = new ScaleTransform(_scale, _scale);
        }
    }
}
