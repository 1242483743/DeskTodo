using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DesktopTodo
{
    // Opacity changes wait for WPF's next render. Suppress only this HWND's
    // native pixels during proxy movement, without Hide()/losing capture, so
    // an old live frame cannot overlap the translucent preview or its shadow.
    internal sealed class GestureWindowClip : IDisposable
    {
        private readonly IntPtr window;
        private IntPtr original;
        private bool disposed;
        internal GestureWindowClip(IntPtr hwnd)
        {
            window = hwnd;
            original = CreateRectRgn(0, 0, 0, 0);
            if (original == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (GetWindowRgn(window, original) == 0) { DeleteObject(original); original = IntPtr.Zero; }
            IntPtr empty = CreateRectRgn(0, 0, 0, 0);
            if (empty == IntPtr.Zero || SetWindowRgn(window, empty, true) == 0)
            {
                if (empty != IntPtr.Zero) DeleteObject(empty);
                if (original != IntPtr.Zero) { DeleteObject(original); original = IntPtr.Zero; }
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            // A successful SetWindowRgn transfers ownership of empty to Windows.
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            if (Native.IsWindow(window) && SetWindowRgn(window, original, true) != 0) original = IntPtr.Zero;
            if (original != IntPtr.Zero) { DeleteObject(original); original = IntPtr.Zero; }
        }
        internal static bool PixelsSuppressed(IntPtr hwnd)
        {
            IntPtr region = CreateRectRgn(0, 0, 0, 0);
            try { return region != IntPtr.Zero && GetWindowRgn(hwnd, region) == 1; } // NULLREGION
            finally { if (region != IntPtr.Zero) DeleteObject(region); }
        }
        [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
        [DllImport("user32.dll")] private static extern int GetWindowRgn(IntPtr hwnd, IntPtr region);
        [DllImport("user32.dll", SetLastError = true)] private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);
    }
}
