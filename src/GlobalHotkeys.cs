using System;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace DesktopTodo
{
    internal sealed class GlobalHotkeys : IDisposable
    {
        private readonly HwndSource source;
        private readonly Action show, mini;
        private string currentShow = "", currentMini = "";
        private bool disposed;
        internal IntPtr Handle { get { return source.Handle; } }
        internal GlobalHotkeys(Action show, Action mini)
        {
            this.show = show; this.mini = mini;
            HwndSourceParameters parameters = new HwndSourceParameters("DeskTodo shortcuts") { ParentWindow = new IntPtr(-3) };
            source = new HwndSource(parameters); source.AddHook(Hook);
        }
        internal static bool Parse(string value, out uint modifiers, out uint key)
        {
            modifiers = 0; key = 0;
            if (String.IsNullOrWhiteSpace(value)) return true;
            try
            {
                KeyGesture gesture = new KeyGestureConverter().ConvertFromInvariantString(value) as KeyGesture;
                if (gesture == null || gesture.Key == Key.None || (gesture.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) == 0) return false;
                if ((gesture.Modifiers & ModifierKeys.Alt) != 0) modifiers |= 1;
                if ((gesture.Modifiers & ModifierKeys.Control) != 0) modifiers |= 2;
                if ((gesture.Modifiers & ModifierKeys.Shift) != 0) modifiers |= 4;
                if ((gesture.Modifiers & ModifierKeys.Windows) != 0) modifiers |= 8;
                key = (uint)KeyInterop.VirtualKeyFromKey(gesture.Key); return key != 0;
            }
            catch { return false; }
        }
        internal string Configure(string showKey, string miniKey)
        {
            uint showMods, showVk, miniMods, miniVk;
            if (!Parse(showKey, out showMods, out showVk) || !Parse(miniKey, out miniMods, out miniVk)) return "请使用 Ctrl、Alt 或 Win 加字母/数字，例如 Ctrl+Alt+D；留空可关闭。";
            if (showVk != 0 && showVk == miniVk && showMods == miniMods) return "两个功能不能使用相同快捷键。";
            if (showKey == currentShow && miniKey == currentMini) return null;
            UnregisterHotKey(source.Handle, 1); UnregisterHotKey(source.Handle, 2);
            bool first = showVk == 0 || RegisterHotKey(source.Handle, 1, showMods | 0x4000, showVk);
            bool second = first && (miniVk == 0 || RegisterHotKey(source.Handle, 2, miniMods | 0x4000, miniVk));
            if (!second)
            {
                UnregisterHotKey(source.Handle, 1); UnregisterHotKey(source.Handle, 2);
                uint mods, vk;
                if (Parse(currentShow, out mods, out vk) && vk != 0) RegisterHotKey(source.Handle, 1, mods | 0x4000, vk);
                if (Parse(currentMini, out mods, out vk) && vk != 0) RegisterHotKey(source.Handle, 2, mods | 0x4000, vk);
                return "快捷键被其他程序占用，原有快捷键已保留。请换一个组合。";
            }
            currentShow = showKey; currentMini = miniKey; return null;
        }
        private IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == 0x0312 && !disposed) { handled = true; if (wParam.ToInt32() == 1) show(); else if (wParam.ToInt32() == 2) mini(); }
            return IntPtr.Zero;
        }
        public void Dispose() { if (disposed) return; disposed = true; UnregisterHotKey(source.Handle, 1); UnregisterHotKey(source.Handle, 2); source.RemoveHook(Hook); source.Dispose(); }
        [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    }
}
