using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;

namespace DesktopTodo
{
    internal static class SecondaryStartup
    {
        internal static List<Native.MonitorArea> AvailableMonitors()
        {
            try { return Native.PhysicalMonitors(); }
            catch (Win32Exception) { return new List<Native.MonitorArea>(); }
        }

        internal static Native.MonitorArea Current(string deviceName)
        { return Select(AvailableMonitors(), deviceName); }

        // A saved Windows display name is preferred. One remaining secondary is a
        // safe fallback when Windows renumbers a display after a cable/port change.
        internal static Native.MonitorArea Select(IEnumerable<Native.MonitorArea> monitors, string deviceName)
        {
            List<Native.MonitorArea> secondary = monitors.Where(m => !m.IsPrimary).ToList();
            if (secondary.Count == 0) return null;
            if (!String.IsNullOrWhiteSpace(deviceName))
            {
                Native.MonitorArea exact = secondary.FirstOrDefault(m => String.Equals(m.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase));
                if (exact != null) return exact;
                return secondary.Count == 1 ? secondary[0] : null;
            }
            return secondary[0];
        }

        internal static void Capture(AppState state, Native.MonitorArea monitor, Native.Rect rect, bool userMoved = false)
        {
            if (monitor == null || monitor.IsPrimary) return;
            if (userMoved) state.SecondaryDeviceName = monitor.DeviceName;
            if (!String.IsNullOrEmpty(state.SecondaryDeviceName) &&
                !String.Equals(state.SecondaryDeviceName, monitor.DeviceName, StringComparison.OrdinalIgnoreCase)) return;
            state.SecondaryDeviceName = monitor.DeviceName;
            state.SecondaryOffsetX = rect.Left - monitor.Work.Left;
            state.SecondaryOffsetY = rect.Top - monitor.Work.Top;
            double scale = Native.MonitorScale(monitor.Handle);
            state.SecondaryWidth = Math.Max(320, Math.Min(900, (rect.Right - rect.Left) / scale));
            state.SecondaryHeight = Math.Max(360, Math.Min(1300, (rect.Bottom - rect.Top) / scale));
            state.SecondaryPositionSet = true;
        }

        internal static void Restore(AppState state, Native.MonitorArea monitor)
        {
            if (monitor == null || monitor.IsPrimary) return;
            // Keep the saved size in WPF units, while offsets and monitor work areas
            // are physical pixels. Clamp after a resolution or DPI change.
            double widthDip = state.SecondaryPositionSet ? state.SecondaryWidth : state.Width;
            double heightDip = state.SecondaryPositionSet ? state.SecondaryHeight : state.Height;
            double scale = Native.MonitorScale(monitor.Handle);
            double fit = Math.Min(1, Math.Min(monitor.Work.Width / (widthDip * scale), monitor.Work.Height / (heightDip * scale)));
            int width = Math.Max(1, (int)Math.Round(widthDip * scale * fit));
            int height = Math.Max(1, (int)Math.Round(heightDip * scale * fit));
            int x = state.SecondaryPositionSet ? monitor.Work.Left + state.SecondaryOffsetX : monitor.Work.Right - width - 32;
            int y = state.SecondaryPositionSet ? monitor.Work.Top + state.SecondaryOffsetY : monitor.Work.Top + 52;
            state.X = Math.Max(monitor.Work.Left, Math.Min(monitor.Work.Right - width, x));
            state.Y = Math.Max(monitor.Work.Top, Math.Min(monitor.Work.Bottom - height, y));
            state.Width = widthDip;
            state.Height = heightDip;
            state.PositionSet = true;
            state.SecondaryDeviceName = monitor.DeviceName;
        }

        // Return false only for an explicit exit request. A manual second launch
        // writes show.request so settings remain accessible without a secondary.
        internal static bool WaitForDisplay(string directory, Func<Native.MonitorArea> selectedMonitor, int pollMilliseconds = 2000)
        {
            string showRequest = Path.Combine(directory, "show.request");
            string exitRequest = Path.Combine(directory, "exit.request");
            while (true)
            {
                if (File.Exists(exitRequest)) { File.Delete(exitRequest); return false; }
                if (File.Exists(showRequest)) { File.Delete(showRequest); return true; }
                try { if (selectedMonitor() != null) return true; }
                catch (Win32Exception) { /* A display driver may be reconfiguring. Retry. */ }
                Thread.Sleep(pollMilliseconds);
            }
        }
    }
}
