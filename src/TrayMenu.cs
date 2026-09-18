using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

namespace DesktopTodo
{
    internal sealed class TrayMenu
    {
        internal readonly Window Window;
        internal readonly List<Button> Buttons = new List<Button>();
        private bool closing, closed, selected;
        private Action selectedAction;
        internal TrayMenu(TodoWindow owner, Action show, Action hideTray, Action toggleDesktop, Action settings, Action exit)
        {
            Window = (Window)XamlReader.Parse(@"<Window xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Width='216' SizeToContent='Height' WindowStyle='None' ResizeMode='NoResize' AllowsTransparency='True' Background='Transparent' ShowInTaskbar='False' Topmost='True' FontFamily='Microsoft YaHei UI' FontSize='13' UseLayoutRounding='True'>
              <Window.Resources><Style TargetType='Button'><Setter Property='Foreground' Value='{DynamicResource TextBrush}'/><Setter Property='FocusVisualStyle' Value='{x:Null}' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'/><Setter Property='Cursor' Value='Hand'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'><Border Name='Surface' Background='Transparent' CornerRadius='6' Padding='14,9'><ContentPresenter HorizontalAlignment='Left' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Surface' Property='Background' Value='{DynamicResource HoverBrush}'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Surface' Property='Background' Value='{DynamicResource HoverBrush}'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style></Window.Resources>
              <Border Background='{DynamicResource CardBrush}' BorderBrush='{DynamicResource LineBrush}' BorderThickness='1' CornerRadius='10' Padding='5' SnapsToDevicePixels='False'><StackPanel/></Border></Window>");
            foreach (string key in new[] { "CardBrush", "TextBrush", "LineBrush", "HoverBrush" }) Window.Resources[key] = owner.Window.Resources[key];
            Window.Title = "DeskTodo 托盘菜单";
            StackPanel items = (StackPanel)((Border)Window.Content).Child;
            Add(items, "显示 " + owner.State.Title, show);
            Add(items, "隐藏托盘", hideTray);
            Add(items, owner.State.DesktopMode ? "取消嵌入桌面" : "嵌入桌面", toggleDesktop);
            items.Children.Add(new Border { Height = 1, Background = (Brush)Window.Resources["LineBrush"], Margin = new Thickness(9, 5, 9, 5) });
            Add(items, "设置", settings); Add(items, "退出", exit);
            Window.Closing += delegate { closing = true; };
            Window.Closed += delegate
            {
                closed = true;
                Action action = selectedAction; selectedAction = null;
                // Run after WPF has unwound closing/deactivation and mouse capture.
                if (action != null && !Window.Dispatcher.HasShutdownStarted)
                    Window.Dispatcher.BeginInvoke(DispatcherPriority.Normal, action);
            };
            Window.Deactivated += delegate
            {
                if (!closing && !closed)
                    Window.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Close));
            };
            Window.PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
        }
        private void Add(StackPanel items, string title, Action action)
        {
            Button button = new Button { Content = title, FocusVisualStyle = null };
            System.Windows.Automation.AutomationProperties.SetName(button, title);
            button.Click += delegate
            {
                if (closing || closed || selected) return;
                selected = true; selectedAction = action; Close();
            };
            Buttons.Add(button); items.Children.Add(button);
        }
        internal void ShowAtCursor()
        {
            Native.Point cursor; Native.GetPhysicalCursorPos(out cursor);
            ShowAt(cursor);
        }
        internal void ShowAt(Native.Point cursor)
        {
            if (closing || closed) return;
            Window.Show();
            if (closing || closed) return;
            IntPtr handle = new WindowInteropHelper(Window).Handle;
            // Enter the cursor's monitor in physical coordinates before calculating
            // the final popup size, so mixed-DPI taskbars use their own DPI.
            using (new Native.PhysicalDpiScope()) Native.SetWindowPos(handle, IntPtr.Zero, cursor.X, cursor.Y, 0, 0, 1 | Native.SWP_NOZORDER);
            Window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate
            {
                if (closing || closed || !Window.IsVisible || !Native.IsWindow(handle)) return;
                Native.Rect rectangle; Native.PhysicalWindowRect(handle, out rectangle);
                Native.Point at = Placement(cursor, Native.MonitorAt(cursor).Work, rectangle.Right - rectangle.Left, rectangle.Bottom - rectangle.Top);
                using (new Native.PhysicalDpiScope()) Native.SetWindowPos(handle, new IntPtr(-1), at.X, at.Y, 0, 0, 1);
                Window.Activate(); Buttons[0].Focus();
            }));
        }
        internal static Native.Point Placement(Native.Point cursor, System.Drawing.Rectangle work, int width, int height)
        {
            return new Native.Point { X = Math.Max(work.Left, Math.Min(work.Right - width, cursor.X - width + 16)),
                Y = Math.Max(work.Top, Math.Min(work.Bottom - height, cursor.Y - height - 8)) };
        }
        internal void Close()
        {
            if (closing || closed) return;
            closing = true; Window.Close();
        }
    }
}
