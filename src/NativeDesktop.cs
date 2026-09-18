using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace DesktopTodo
{
    public static class Native
    {
        public const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
        public const long WS_CHILD = 0x40000000L, WS_POPUP = 0x80000000L, WS_EX_TRANSPARENT = 0x20L,
            WS_EX_TOOLWINDOW = 0x80L, WS_EX_APPWINDOW = 0x40000L, WS_EX_NOACTIVATE = 0x08000000L;
        public const uint SWP_NOZORDER = 4, SWP_NOACTIVATE = 0x10, SWP_FRAMECHANGED = 0x20, SWP_SHOWWINDOW = 0x40;
        public delegate bool EnumProc(IntPtr hwnd, IntPtr param);
        [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback, IntPtr param);
        [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetParent(IntPtr hwnd, IntPtr parent);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] public static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll")] public static extern bool GetPhysicalCursorPos(out Point point);
        [DllImport("user32.dll")] public static extern bool ScreenToClient(IntPtr hwnd, ref Point point);
        [DllImport("user32.dll")] public static extern IntPtr SetFocus(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hwnd, StringBuilder text, int length);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll")] public static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hwnd, uint command);
        [DllImport("user32.dll")] public static extern IntPtr GetThreadDpiAwarenessContext();
        [DllImport("user32.dll")] public static extern IntPtr GetWindowDpiAwarenessContext(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern int GetAwarenessFromDpiAwarenessContext(IntPtr value);
        [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
        internal sealed class PhysicalDpiScope : IDisposable
        {
            private readonly IntPtr previous;
            public PhysicalDpiScope() { previous = SetThreadDpiAwarenessContext(new IntPtr(-4)); }
            public void Dispose() { if (previous != IntPtr.Zero) SetThreadDpiAwarenessContext(previous); }
        }
        internal static bool PhysicalWindowRect(IntPtr hwnd, out Rect rect)
        { using (new PhysicalDpiScope()) return GetWindowRect(hwnd, out rect); }
        internal sealed class MonitorArea
        {
            public IntPtr Handle;
            public System.Drawing.Rectangle Bounds, Work;
        }
        [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Bounds, Work; public uint Flags; }
        private delegate bool MonitorProc(IntPtr monitor, IntPtr dc, IntPtr rect, IntPtr value);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(Point point, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromRect(ref Rect rect, uint flags);
        [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorProc callback, IntPtr value);
        private static MonitorArea ReadMonitor(IntPtr handle)
        {
            MonitorInfo info = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
            if (!GetMonitorInfo(handle, ref info)) throw new Win32Exception("无法读取显示器工作区");
            return new MonitorArea { Handle = handle,
                Bounds = System.Drawing.Rectangle.FromLTRB(info.Bounds.Left, info.Bounds.Top, info.Bounds.Right, info.Bounds.Bottom),
                Work = System.Drawing.Rectangle.FromLTRB(info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom) };
        }
        internal static MonitorArea MonitorAt(Point point)
        { using (new PhysicalDpiScope()) return ReadMonitor(MonitorFromPoint(point, 2)); }
        internal static MonitorArea MonitorFor(Rect rect)
        { using (new PhysicalDpiScope()) return ReadMonitor(MonitorFromRect(ref rect, 2)); }
        internal static List<MonitorArea> PhysicalMonitors()
        {
            using (new PhysicalDpiScope())
            {
                List<MonitorArea> result = new List<MonitorArea>();
                EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate(IntPtr monitor, IntPtr dc, IntPtr rect, IntPtr value) { result.Add(ReadMonitor(monitor)); return true; }, IntPtr.Zero);
                return result;
            }
        }
        [StructLayout(LayoutKind.Sequential)] public struct WindowPos { public IntPtr Hwnd, After; public int X, Y, Width, Height; public uint Flags; }
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int command);
        public static string ClassName(IntPtr hwnd) { StringBuilder text = new StringBuilder(256); GetClassName(hwnd, text, text.Capacity); return text.ToString(); }

        public static IntPtr FindDesktopHost()
        {
            IntPtr shell = GetShellWindow();
            if (shell == IntPtr.Zero || ClassName(shell) != "Progman") return IntPtr.Zero;
            // Discover the actual desktop-icons container rather than assuming a WorkerW layout.
            // Recent Windows 11 builds place SHELLDLL_DefView directly under Progman.
            if (FindWindowEx(shell, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero) return shell;
            IntPtr result = IntPtr.Zero;
            uint shellPid; GetWindowThreadProcessId(shell, out shellPid);
            EnumWindows(delegate(IntPtr hwnd, IntPtr param)
            {
                uint pid; GetWindowThreadProcessId(hwnd, out pid);
                if (pid == shellPid && IsWindowVisible(hwnd) && ClassName(hwnd) == "WorkerW" &&
                    FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                { result = hwnd; return false; }
                return true;
            }, IntPtr.Zero);
            return result;
        }
        public static IntPtr FindDesktopSurface()
        {
            if (TestSurface == new IntPtr(-1)) return IntPtr.Zero;
            if (TestSurface != IntPtr.Zero) return TestSurface;
            IntPtr host = FindDesktopHost();
            if (host == IntPtr.Zero) return IntPtr.Zero;
            IntPtr view = FindWindowEx(host, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (view == IntPtr.Zero) return IntPtr.Zero;
            IntPtr icons = FindWindowEx(view, IntPtr.Zero, "SysListView32", null);
            return icons != IntPtr.Zero && IsWindowVisible(icons) ? icons : view;
        }
        // Only used by isolated self-tests; production leaves this zero.
        internal static IntPtr TestSurface;
    }

    public sealed class DesktopAttachment
    {
        public IntPtr Handle, Parent;
        private long originalStyle;
        private long originalExStyle;
        private bool hasGeometry;
        private int screenX, screenY, pixelWidth, pixelHeight;
        public Action<string> Log;
        public bool IsAttached { get { return Parent != IntPtr.Zero && Native.IsWindow(Handle) && Native.IsWindow(Parent) && Native.GetParent(Handle) == Parent; } }
        public void Initialize(Window window, bool toolWindow)
        {
            Handle = new WindowInteropHelper(window).Handle;
            originalStyle = Native.GetWindowLongPtr(Handle, Native.GWL_STYLE).ToInt64();
            long ex = Native.GetWindowLongPtr(Handle, Native.GWL_EXSTYLE).ToInt64();
            originalExStyle = ex;
            Native.SetWindowLongPtr(Handle, Native.GWL_EXSTYLE, new IntPtr(toolWindow ? (ex | Native.WS_EX_TOOLWINDOW) & ~Native.WS_EX_APPWINDOW : (ex & ~Native.WS_EX_TOOLWINDOW) | Native.WS_EX_APPWINDOW));
            HwndSource source = HwndSource.FromHwnd(Handle);
            if (source != null) source.AddHook(KeepGeometry);
        }
        private IntPtr KeepGeometry(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == 0x0046 && hasGeometry && Parent != IntPtr.Zero && Native.IsWindow(Parent) && lParam != IntPtr.Zero)
            {
                // WPF Window caches screen coordinates, but a child HWND uses parent-client
                // coordinates. Prevent subsequent WPF layout/show operations relocating it.
                Native.WindowPos pos = (Native.WindowPos)Marshal.PtrToStructure(lParam, typeof(Native.WindowPos));
                Native.Point point = new Native.Point { X = screenX, Y = screenY };
                using (new Native.PhysicalDpiScope()) Native.ScreenToClient(Parent, ref point);
                if ((pos.Flags & 2) == 0) { pos.X = point.X; pos.Y = point.Y; }
                if ((pos.Flags & 1) == 0) { pos.Width = pixelWidth; pos.Height = pixelHeight; }
                Marshal.StructureToPtr(pos, lParam, false);
            }
            return IntPtr.Zero;
        }
        public bool Attach() { return Attach(Native.FindDesktopSurface()); }
        public bool Attach(IntPtr parent)
        {
            if (parent == IntPtr.Zero || !Native.IsWindow(parent)) return false;
            Native.Rect rect; Native.PhysicalWindowRect(Handle, out rect);
            long ex = Native.GetWindowLongPtr(Handle, Native.GWL_EXSTYLE).ToInt64();
            Native.SetWindowPos(Handle, new IntPtr(-2), 0, 0, 0, 0, 0x1 | 0x2 | Native.SWP_NOACTIVATE);
            Native.SetWindowLongPtr(Handle, Native.GWL_EXSTYLE, new IntPtr((ex | Native.WS_EX_TOOLWINDOW) & ~Native.WS_EX_APPWINDOW & ~8L));
            long style = Native.GetWindowLongPtr(Handle, Native.GWL_STYLE).ToInt64();
            Native.SetWindowLongPtr(Handle, Native.GWL_STYLE, new IntPtr((style | Native.WS_CHILD) & ~Native.WS_POPUP));
            Native.SetParent(Handle, parent);
            if (Native.GetParent(Handle) != parent)
            {
                Native.SetWindowLongPtr(Handle, Native.GWL_STYLE, new IntPtr(originalStyle));
                Native.SetWindowLongPtr(Handle, Native.GWL_EXSTYLE, new IntPtr(originalExStyle));
                return false;
            }
            // Host INSIDE the icon surface, not as a sibling of the opaque desktop icon layer.
            // Show Desktop now moves our parent, without hiding/restoring or re-stacking this HWND.
            Parent = parent;
            Move(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
            Native.SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x1 | 0x2 | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED);
            if (Log != null) Log("Embedded in icon surface: " + Describe());
            return true;
        }
        public void Detach()
        {
            if (!Native.IsWindow(Handle)) return;
            Native.Rect rect; Native.GetWindowRect(Handle, out rect);
            hasGeometry = false; Parent = IntPtr.Zero;
            Native.SetParent(Handle, IntPtr.Zero);
            Native.SetWindowLongPtr(Handle, Native.GWL_STYLE, new IntPtr(originalStyle));
            Native.SetWindowLongPtr(Handle, Native.GWL_EXSTYLE, new IntPtr((originalExStyle & ~Native.WS_EX_TOOLWINDOW) | Native.WS_EX_APPWINDOW));
            Parent = IntPtr.Zero;
            Native.SetWindowPos(Handle, new IntPtr(-2), 0, 0, 0, 0, 0x1 | 0x2 | Native.SWP_NOACTIVATE);
            Move(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        }
        public void Move(int screenX, int screenY, int width, int height)
        {
            using (new Native.PhysicalDpiScope())
            {
            Native.Rect current;
            if (!Native.IsWindow(Handle)) return;
            this.screenX = screenX; this.screenY = screenY;
            pixelWidth = width; pixelHeight = height; hasGeometry = true;
            if (Native.GetWindowRect(Handle, out current) && current.Left == screenX && current.Top == screenY &&
                current.Right - current.Left == width && current.Bottom - current.Top == height)
                return;
            Native.Point point = new Native.Point { X = screenX, Y = screenY };
            if (Parent != IntPtr.Zero) Native.ScreenToClient(Parent, ref point);
            if (!Native.SetWindowPos(Handle, IntPtr.Zero, point.X, point.Y, width, height, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法调整窗口位置或大小");
            }
        }
        // Rounded transparency is rendered by WPF so its edge remains antialiased while
        // the HWND changes size. These gesture hooks intentionally do no native clipping.
        public void BeginResize() { }
        public void EndResize() { }
        public string Describe()
        {
            Native.Rect rect; Native.PhysicalWindowRect(Handle, out rect);
            Native.Rect parentRect; Native.PhysicalWindowRect(Parent, out parentRect);
            return "window=" + Handle + " parent=" + Native.GetParent(Handle) + " host=" + Parent +
                " hostClass=" + Native.ClassName(Parent) + " desktopPinned=" + IsAttached + " visible=" + Native.IsWindowVisible(Handle) +
                " child=" + ((Native.GetWindowLongPtr(Handle, Native.GWL_STYLE).ToInt64() & Native.WS_CHILD) != 0) +
                " topmost=" + ((Native.GetWindowLongPtr(Handle, Native.GWL_EXSTYLE).ToInt64() & 8) != 0) +
                " hostRect=" + parentRect.Left + "," + parentRect.Top + "," + (parentRect.Right - parentRect.Left) + "," + (parentRect.Bottom - parentRect.Top) +
                " rect=" + rect.Left + "," + rect.Top + "," + (rect.Right - rect.Left) + "," + (rect.Bottom - rect.Top);
        }
    }
}
