using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace DesktopTodo
{
    internal sealed class ReminderPopup : IDisposable
    {
        internal readonly Window Window;
        internal readonly TodoItem Item;
        internal event Action Snoozed, Dismissed, Closed;
        internal ReminderPopup(AppState state, TodoItem item)
        {
            Item = item;
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("DesktopTodo.ReminderWindow.xaml"))
                Window = (Window)XamlReader.Load(stream);
            Window.FindName("ReminderIcon").As<Image>().Source = UiAssets.InternalIcon;
            ApplyTheme(state);
            Window.FindName("ReminderTime").As<TextBlock>().Text = DateTime.Now.ToString("M月d日 HH:mm");
            Window.FindName("ReminderItems").As<TextBlock>().Text = item.Text;
            Window.FindName("SnoozeButton").As<Button>().Click += delegate { if (Snoozed != null) Snoozed(); Close(); };
            Window.FindName("DismissButton").As<Button>().Click += delegate { if (Dismissed != null) Dismissed(); Close(); };
            Window.FindName("ReminderHeader").As<FrameworkElement>().MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            { if (e.ButtonState == MouseButtonState.Pressed) Window.DragMove(); };
            Window.Closed += delegate { Window.Topmost = false; if (Closed != null) Closed(); };
        }
        private void ApplyTheme(AppState state)
        {
            ThemePalette palette = ThemeCatalog.Get(state.ThemeId);
            string[] colors = state.DarkMode ? palette.Dark : palette.Light;
            for (int i = 0; i < ThemeCatalog.ResourceNames.Length; i++)
                Window.Resources[ThemeCatalog.ResourceNames[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
        }
        internal void Show()
        {
            Window.Show(); Window.Activate();
        }
        private void Close() { if (Window.IsVisible) Window.Close(); }
        public void Dispose() { Close(); }
    }
    internal static class CastExtension
    {
        internal static T As<T>(this object value) where T : class { return value as T; }
    }
}
