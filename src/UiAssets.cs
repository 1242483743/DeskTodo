using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace DesktopTodo
{
    internal static class UiAssets
    {
        private static BitmapSource internalIcon;
        private static BitmapSource applicationIcon;
        private static BitmapSource floatingIcon;

        internal static BitmapSource FloatingIcon
        {
            get
            {
                if (floatingIcon != null) return floatingIcon;
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("DesktopTodo.FloatingIcon.png"))
                {
                    if (stream == null) throw new InvalidOperationException("找不到悬浮图标资源。");
                    BitmapImage image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
                    image.StreamSource = stream; image.EndInit(); image.Freeze();
                    FormatConvertedBitmap converted = new FormatConvertedBitmap(image, System.Windows.Media.PixelFormats.Bgra32, null, 0);
                    int stride = image.PixelWidth * 4; byte[] pixels = new byte[stride * image.PixelHeight];
                    converted.CopyPixels(pixels, stride, 0);
                    int left = image.PixelWidth, top = image.PixelHeight, right = 0, bottom = 0;
                    for (int y = 0; y < image.PixelHeight; y++) for (int x = 0; x < image.PixelWidth; x++)
                        if (pixels[y * stride + x * 4 + 3] > 8)
                        { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y); }
                    if (left > right) return image;
                    int pad = Math.Max(2, image.PixelWidth / 100);
                    left = Math.Max(0, left - pad); top = Math.Max(0, top - pad);
                    right = Math.Min(image.PixelWidth - 1, right + pad); bottom = Math.Min(image.PixelHeight - 1, bottom + pad);
                    CroppedBitmap cropped = new CroppedBitmap(image, new Int32Rect(left, top, right - left + 1, bottom - top + 1));
                    cropped.Freeze(); floatingIcon = cropped; return cropped;
                }
            }
        }

        internal static BitmapSource ApplicationIcon
        {
            get
            {
                if (applicationIcon != null) return applicationIcon;
                try
                {
                    string executable = Assembly.GetExecutingAssembly().Location;
                    using (System.Drawing.Icon icon = System.Drawing.Icon.ExtractAssociatedIcon(executable))
                    {
                        if (icon != null)
                        {
                            BitmapSource source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(64, 64));
                            source.Freeze(); applicationIcon = source; return applicationIcon;
                        }
                    }
                }
                catch { }
                return InternalIcon;
            }
        }

        internal static BitmapSource InternalIcon
        {
            get
            {
                if (internalIcon != null) return internalIcon;
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("DesktopTodo.InternalIcon.png"))
                {
                    if (stream == null) throw new InvalidOperationException("找不到软件内图标资源。");
                    BitmapImage bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                    bitmap.StreamSource = stream;
                    bitmap.EndInit();
                    bitmap.Freeze();

                    // The supplied artwork intentionally has a large transparent canvas.
                    // Crop an even 10% margin so the icon remains legible at 30-42 DIP.
                    int insetX = Math.Max(0, (int)Math.Round(bitmap.PixelWidth * 0.10));
                    int insetY = Math.Max(0, (int)Math.Round(bitmap.PixelHeight * 0.10));
                    int width = Math.Max(1, bitmap.PixelWidth - insetX * 2);
                    int height = Math.Max(1, bitmap.PixelHeight - insetY * 2);
                    CroppedBitmap cropped = new CroppedBitmap(bitmap, new Int32Rect(insetX, insetY, width, height));
                    cropped.Freeze();
                    internalIcon = cropped;
                    return internalIcon;
                }
            }
        }
    }
}
