using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;

namespace pax_watermark
{
    public static class ImageProcessor
    {
        public static RenderTargetBitmap ApplyWatermark(BitmapImage original, BitmapImage watermark, Point position, double scale = 1.0)
        {
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.DrawImage(original, new Rect(0, 0, original.PixelWidth, original.PixelHeight));

                double width = watermark.PixelWidth * scale;
                double height = watermark.PixelHeight * scale;

                context.DrawImage(watermark, new Rect(position.X, position.Y, width, height));
            }

            var bmp = new RenderTargetBitmap(original.PixelWidth, original.PixelHeight, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(visual);
            return bmp;
        }

        public static void SaveImage(BitmapSource image, string filePath)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var stream = new FileStream(filePath, FileMode.Create);
            encoder.Save(stream);
        }
    }
}
