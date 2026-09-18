using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace DesktopTodo
{
    // A frozen, physical-pixel surface, not a second WPF Window. It never applies
    // WM_DPICHANGED's suggested resize to a bitmap that is already pixel-sized.
    internal sealed class GesturePreview : IDisposable
    {
        private const string ClassName = "DesktopTodo.PhysicalGesturePreview";
        private static readonly WindowProc procedure = PreviewProcedure;
        private static bool registered;
        private Bitmap source;
        private IntPtr dc, surface, oldSurface;
        private int surfaceWidth, surfaceHeight;
        internal IntPtr Handle { get; private set; }
        internal Native.Rect LastRect { get; private set; }

        internal GesturePreview(BitmapSource bitmap, Native.Rect rect)
        {
            try
            {
                source = new Bitmap(bitmap.PixelWidth, bitmap.PixelHeight, PixelFormat.Format32bppPArgb);
                BitmapData data = source.LockBits(new Rectangle(0, 0, source.Width, source.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
                try { bitmap.CopyPixels(System.Windows.Int32Rect.Empty, data.Scan0, data.Stride * data.Height, data.Stride); }
                finally { source.UnlockBits(data); }
                using (new Native.PhysicalDpiScope())
                {
                    RegisterPreviewClass();
                    Handle = CreateWindowEx(0x00080000U | 0x80U | 0x20U | 0x08000000U, ClassName, "DeskTodo 手势预览",
                        0x80000000U, rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top,
                        IntPtr.Zero, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
                    if (Handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                    dc = CreateCompatibleDC(IntPtr.Zero);
                    if (dc == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                    Update(rect);
                    // The bitmap is fully populated before the HWND is made visible.
                    Native.SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 1 | 2 | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
                    // Do not make the live WPF card transparent until the compositor has
                    // actually presented this replacement surface.
                    FlushComposition();
                }
            }
            catch { Dispose(); throw; }
        }

        internal void Update(Native.Rect rect)
        {
            int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException("rect");
            using (new Native.PhysicalDpiScope())
            {
                if (width == surfaceWidth && height == surfaceHeight)
                {
                    if (!Native.SetWindowPos(Handle, IntPtr.Zero, rect.Left, rect.Top, 0, 0, 1 | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE))
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                else
                {
                    BitmapInfo info = new BitmapInfo();
                    info.Header.Size = 40; info.Header.Width = width; info.Header.Height = -height;
                    info.Header.Planes = 1; info.Header.BitCount = 32;
                    IntPtr pixels;
                    IntPtr nextSurface = CreateDIBSection(dc, ref info, 0, out pixels, IntPtr.Zero, 0);
                    if (nextSurface == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                    IntPtr previous = SelectObject(dc, nextSurface);
                    if (surface == IntPtr.Zero) oldSurface = previous;
                    else DeleteObject(surface);
                    surface = nextSurface; surfaceWidth = width; surfaceHeight = height;
                    using (Bitmap target = new Bitmap(width, height, width * 4, PixelFormat.Format32bppPArgb, pixels))
                    using (Graphics graphics = Graphics.FromImage(target))
                    {
                        graphics.CompositingMode = CompositingMode.SourceCopy;
                        graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
                        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        graphics.Clear(Color.Transparent);
                        graphics.DrawImage(source, new Rectangle(0, 0, width, height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel);
                    }
                    Native.Point destination = new Native.Point { X = rect.Left, Y = rect.Top }, origin = new Native.Point();
                    PixelSize size = new PixelSize { Width = width, Height = height };
                    Blend blend = new Blend { ConstantAlpha = 255, AlphaFormat = 1 };
                    if (!UpdateLayeredWindow(Handle, IntPtr.Zero, ref destination, ref size, dc, ref origin, 0, ref blend, 2))
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                LastRect = rect;
            }
        }

        private static void RegisterPreviewClass()
        {
            if (registered) return;
            WindowClass windowClass = new WindowClass();
            windowClass.Size = (uint)Marshal.SizeOf(typeof(WindowClass));
            windowClass.Procedure = procedure; windowClass.Instance = GetModuleHandle(null); windowClass.Name = ClassName;
            if (RegisterClassEx(ref windowClass) == 0 && Marshal.GetLastWin32Error() != 1410)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            registered = true;
        }
        private static IntPtr PreviewProcedure(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
        {
            if (message == 0x02E0) return IntPtr.Zero; // Frozen physical pixels; no automatic DPI resize.
            if (message == 0x0084) return new IntPtr(-1); // HTTRANSPARENT
            if (message == 0x0021) return new IntPtr(3); // MA_NOACTIVATE
            return DefWindowProc(hwnd, message, wParam, lParam);
        }
        internal static void FlushComposition()
        {
            try { DwmFlush(); }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
        public void Dispose()
        {
            if (Handle != IntPtr.Zero) { DestroyWindow(Handle); Handle = IntPtr.Zero; }
            if (dc != IntPtr.Zero && oldSurface != IntPtr.Zero) SelectObject(dc, oldSurface);
            if (surface != IntPtr.Zero) { DeleteObject(surface); surface = IntPtr.Zero; }
            if (dc != IntPtr.Zero) { DeleteDC(dc); dc = IntPtr.Zero; }
            if (source != null) { source.Dispose(); source = null; }
        }

        private delegate IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct WindowClass
        {
            public uint Size, Style; public WindowProc Procedure; public int ClassExtra, WindowExtra;
            public IntPtr Instance, Icon, Cursor, Background; public string Menu, Name; public IntPtr SmallIcon;
        }
        [StructLayout(LayoutKind.Sequential)] private struct BitmapHeader
        { public uint Size; public int Width, Height; public ushort Planes, BitCount; public uint Compression, ImageSize; public int XPels, YPels; public uint ColorsUsed, ColorsImportant; }
        [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo { public BitmapHeader Header; public uint Colors; }
        [StructLayout(LayoutKind.Sequential)] private struct PixelSize { public int Width, Height; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] private struct Blend { public byte Operation, Flags, ConstantAlpha, AlphaFormat; }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
        [DllImport("dwmapi.dll")] private static extern int DwmFlush();
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassEx(ref WindowClass windowClass);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateWindowEx(uint ex, string cls, string title, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DefWindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr destinationDC, ref Native.Point destination, ref PixelSize size, IntPtr sourceDC, ref Native.Point origin, uint key, ref Blend blend, uint flags);
        [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr pixels, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    }
}
