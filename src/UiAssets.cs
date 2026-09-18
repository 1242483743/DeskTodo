using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media.Imaging;

namespace DesktopTodo
{
    internal static class UiAssets
    {
        private static BitmapSource internalIcon;

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
