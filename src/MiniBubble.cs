using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace DesktopTodo
{
    internal sealed class MiniBubble : IDisposable
    {
        private readonly AppState state;
        private readonly Action restore, moved;
        private readonly Window window;
        private readonly DispatcherTimer hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(320) };
        private string edge;
        private bool disposed, dragging, placed;
        internal Window Surface { get { return window; } }
        internal string DockedEdge { get { return edge; } }

        internal MiniBubble(AppState state, Action restore, Action moved)
        {
            this.state = state; this.restore = restore; this.moved = moved;
            edge = state.MiniEdge;
            window = new Window
            {
                Width = 62, Height = 62, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
                AllowsTransparency = true, Background = Brushes.Transparent, ShowInTaskbar = false,
                Topmost = state.MiniAlwaysOnTop, Opacity = 0, SizeToContent = SizeToContent.Manual,
                FontFamily = new FontFamily("Microsoft YaHei UI")
            };
            Border surface = new Border
            {
                Width = 62, Height = 62, Background = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand,
            };
            Grid content = new Grid();
            Image appIcon = new Image { Source = UiAssets.FloatingIcon, Width = 60, Height = 60, Stretch = Stretch.Uniform, IsHitTestVisible = false };
            RenderOptions.SetBitmapScalingMode(appIcon, BitmapScalingMode.HighQuality);
            content.Children.Add(appIcon);
            surface.Child = content; window.Content = surface;
            window.Loaded += delegate { EnsureVisible(); };
            surface.MouseEnter += delegate { hideTimer.Stop(); if (!dragging) RevealFromEdge(); };
            surface.MouseLeave += delegate { if (!dragging) hideTimer.Start(); };
            hideTimer.Tick += delegate
            {
                hideTimer.Stop();
                Native.Rect rect; Native.Point cursor;
                if (!dragging && PhysicalRect(out rect) && Native.GetPhysicalCursorPos(out cursor) &&
                    !IsCursorInside(rect, cursor)) HideAtEdge();
            };
            surface.MouseLeftButtonDown += delegate
            {
                Native.Rect before;
                if (!PhysicalRect(out before)) return;
                dragging = true;
                try { window.DragMove(); } catch { }
                dragging = false;
                Native.Rect after;
                if (!PhysicalRect(out after)) return;
                double dx = Math.Abs(after.Left - before.Left), dy = Math.Abs(after.Top - before.Top);
                if (dx < 3 && dy < 3) { if (restore != null) restore(); }
                else
                {
                    DockAfterDrag(after);
                    if (moved != null) moved();
                }
            };
            window.Closed += delegate { hideTimer.Stop(); disposed = true; };
        }

        internal static bool IsCursorInside(Native.Rect rect, Native.Point cursor)
        { return cursor.X >= rect.Left && cursor.X < rect.Right && cursor.Y >= rect.Top && cursor.Y < rect.Bottom; }

        internal static Native.Rect ConstrainRect(Native.Rect rect, System.Drawing.Rectangle work)
        {
            int width = Math.Min(rect.Right - rect.Left, work.Width), height = Math.Min(rect.Bottom - rect.Top, work.Height);
            int left = Math.Max(work.Left, Math.Min(work.Right - width, rect.Left));
            int top = Math.Max(work.Top, Math.Min(work.Bottom - height, rect.Top));
            return new Native.Rect { Left = left, Top = top, Right = left + width, Bottom = top + height };
        }

        internal static Native.Rect EdgeRect(Native.Rect rect, System.Drawing.Rectangle work, string dockEdge, bool hidden)
        {
            rect = ConstrainRect(rect, work);
            int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
            int visibleWidth = Math.Max(18, width / 2), visibleHeight = Math.Max(18, height / 2);
            if (dockEdge == "Left") rect.Left = hidden ? work.Left - width + visibleWidth : work.Left;
            else if (dockEdge == "Right") rect.Left = hidden ? work.Right - visibleWidth : work.Right - width;
            else if (dockEdge == "Top") rect.Top = hidden ? work.Top - height + visibleHeight : work.Top;
            else if (dockEdge == "Bottom") rect.Top = hidden ? work.Bottom - visibleHeight : work.Bottom - height;
            rect.Right = rect.Left + width; rect.Bottom = rect.Top + height;
            return rect;
        }

        internal static string NearestEdge(Native.Rect rect, System.Drawing.Rectangle work)
        {
            Native.Rect inside = ConstrainRect(rect, work);
            int width = inside.Right - inside.Left, height = inside.Bottom - inside.Top;
            int threshold = Math.Max(18, Math.Min(width, height) / 3);
            int[] distances = { Math.Abs(inside.Left - work.Left), Math.Abs(work.Right - inside.Right), Math.Abs(inside.Top - work.Top), Math.Abs(work.Bottom - inside.Bottom) };
            string[] names = { "Left", "Right", "Top", "Bottom" };
            int best = 0;
            for (int i = 1; i < distances.Length; i++) if (distances[i] < distances[best]) best = i;
            return distances[best] <= threshold ? names[best] : null;
        }

        private bool PhysicalRect(out Native.Rect rect)
        {
            rect = new Native.Rect();
            IntPtr handle = new WindowInteropHelper(window).Handle;
            return handle != IntPtr.Zero && Native.PhysicalWindowRect(handle, out rect);
        }

        private void ApplyRect(Native.Rect rect)
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;
            Native.SetWindowPos(handle, IntPtr.Zero, rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top,
                Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        }

        internal void EnsureVisible()
        {
            if (disposed || !window.IsLoaded) return;
            Native.Rect current;
            if (!PhysicalRect(out current)) return;
            int width = current.Right - current.Left, height = current.Bottom - current.Top;
            Native.Rect desired;
            if (!Double.IsNaN(state.MiniX) && !Double.IsNaN(state.MiniY))
            {
                desired = new Native.Rect { Left = (int)Math.Round(state.MiniX), Top = (int)Math.Round(state.MiniY) };
                desired.Right = desired.Left + width; desired.Bottom = desired.Top + height;
            }
            else
            {
                Native.Point cursor; Native.GetPhysicalCursorPos(out cursor);
                System.Drawing.Rectangle work = Native.MonitorAt(cursor).Work;
                desired = new Native.Rect { Left = work.Right - width - 28, Top = work.Top + 70, Right = work.Right - 28, Bottom = work.Top + 70 + height };
                edge = null;
            }
            System.Drawing.Rectangle targetWork = Native.MonitorFor(desired).Work;
            desired = String.IsNullOrEmpty(edge) ? ConstrainRect(desired, targetWork) : EdgeRect(desired, targetWork, edge, true);
            ApplyRect(desired); Remember(desired); placed = true;
        }

        private void DockAfterDrag(Native.Rect rect)
        {
            System.Drawing.Rectangle work = Native.MonitorFor(rect).Work;
            rect = ConstrainRect(rect, work);
            edge = NearestEdge(rect, work);
            if (!String.IsNullOrEmpty(edge)) rect = EdgeRect(rect, work, edge, true);
            ApplyRect(rect); Remember(rect);
        }

        private void RevealFromEdge()
        {
            if (!placed || String.IsNullOrEmpty(edge)) return;
            Native.Rect rect; if (!PhysicalRect(out rect)) return;
            ApplyRect(EdgeRect(rect, Native.MonitorFor(rect).Work, edge, false));
        }

        private void HideAtEdge()
        {
            if (!placed || String.IsNullOrEmpty(edge)) return;
            Native.Rect rect; if (!PhysicalRect(out rect)) return;
            rect = EdgeRect(rect, Native.MonitorFor(rect).Work, edge, true);
            ApplyRect(rect); Remember(rect);
        }

        private void Remember(Native.Rect rect)
        {
            state.MiniX = rect.Left; state.MiniY = rect.Top; state.MiniEdge = edge;
        }

        internal void Show()
        {
            if (disposed) return;
            window.Show();
            double target = Math.Max(0.4, Math.Min(1, state.MiniIconOpacityPercent / 100.0));
            window.BeginAnimation(Window.OpacityProperty, new DoubleAnimation(0, target, TimeSpan.FromMilliseconds(170)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            hideTimer.Stop();
            try { window.Close(); } catch { }
        }
    }
}
