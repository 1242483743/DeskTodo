using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace DesktopTodo
{
    internal static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            string dataDirectory = Value(args, "--data-dir") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopTodo");
            Storage storage = new Storage(dataDirectory);
            try
            {
                if (Array.IndexOf(args, "--request-exit") >= 0)
                {
                    Directory.CreateDirectory(dataDirectory);
                    File.WriteAllText(Path.Combine(dataDirectory, "exit.request"), DateTime.UtcNow.ToString("o"));
                    return 0;
                }
                if (Array.IndexOf(args, "--self-test") >= 0) return Tests.Run(Value(args, "--output-dir") ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "qa"));
                if (Array.IndexOf(args, "--inspect") >= 0)
                {
                    Directory.CreateDirectory(dataDirectory);
                    IntPtr host = Native.FindDesktopHost();
                    IntPtr surface = Native.FindDesktopSurface();
                    File.WriteAllText(Path.Combine(dataDirectory, "host-inspection.txt"), "host=" + host + " class=" + Native.ClassName(host) + " visible=" + Native.IsWindowVisible(host) +
                        " surface=" + surface + " surfaceClass=" + Native.ClassName(surface) +
                        " processDpi=" + Native.GetAwarenessFromDpiAwarenessContext(Native.GetThreadDpiAwarenessContext()) +
                        " surfaceDpi=" + Native.GetAwarenessFromDpiAwarenessContext(Native.GetWindowDpiAwarenessContext(surface)));
                    return host == IntPtr.Zero ? 1 : 0;
                }
                string mutexName;
                using (SHA256 hash = SHA256.Create()) mutexName = "Local\\DesktopTodo-" + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(dataDirectory).ToUpperInvariant()))).Replace("-", "");
                bool created;
                using (Mutex mutex = new Mutex(true, mutexName, out created))
                {
                    if (!created)
                    {
                        // The running instance observes this marker and brings itself back.
                        Directory.CreateDirectory(dataDirectory);
                        File.WriteAllText(Path.Combine(dataDirectory, "show.request"), DateTime.UtcNow.ToString("o"));
                        return 0;
                    }
                    // An exit request targets the instance running at that moment, not a
                    // later replacement after an unresponsive old instance was closed.
                    string staleExit = Path.Combine(dataDirectory, "exit.request");
                    if (File.Exists(staleExit)) File.Delete(staleExit);
                    AppState state;
                    string initialState = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "initial-state.json");
                    if (!File.Exists(storage.DataPath) && Value(args, "--data-dir") == null && File.Exists(initialState))
                    {
                        state = Storage.Read(initialState); storage.Save(state);
                        storage.Log("First-run state imported; existing data is never overwritten.");
                    }
                    else state = storage.Load();
                    if (Array.IndexOf(args, "--startup") >= 0) state.Locked = true;
                    Application app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    Controller controller = new Controller(app, storage, state, Array.IndexOf(args, "--windowed") >= 0);
                    app.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e)
                    {
                        storage.Log(e.Exception.ToString());
                        MessageBox.Show("操作未成功：\n" + e.Exception.Message, "DeskTodo", MessageBoxButton.OK, MessageBoxImage.Warning);
                        e.Handled = true;
                    };
                    controller.Start(); app.Run(); controller.Dispose();
                }
                return 0;
            }
            catch (Exception e)
            {
                storage.Log(e.ToString());
                MessageBox.Show(e.Message, "DeskTodo 无法启动", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
        }
        private static string Value(string[] args, string key) { int index = Array.IndexOf(args, key); return index >= 0 && index + 1 < args.Length ? args[index + 1] : null; }
    }

    internal sealed class Controller : IDisposable
    {
        private readonly Application app;
        private readonly Storage storage;
        private readonly AppState state;
        private readonly bool forceWindowed;
        private TodoWindow ui;
        private Forms.NotifyIcon tray;
        private DispatcherTimer timer;
        private ReminderPopup reminder;
        private TrayMenu trayMenu;
        private bool exiting, hidden, rebuilding, dirty, disposed;
        private DateTime lastSaveAttempt;
        private IntPtr lastHost;
        internal TodoWindow CurrentUi { get { return ui; } }
        public Controller(Application app, Storage storage, AppState state, bool forceWindowed)
        { this.app = app; this.storage = storage; this.state = state; this.forceWindowed = forceWindowed; }
        public void Start()
        {
            try { if (Native.TestSurface == IntPtr.Zero && Startup.MigrateLegacyCommand()) storage.Log("Startup command migrated to DeskTodo.exe."); }
            catch (Exception e) { storage.Log("Startup command migration skipped: " + e.Message); }
            CreateWindow();
            tray = new Forms.NotifyIcon { Text = "DeskTodo", Icon = CreateTrayIcon(), Visible = !state.HideTray };
            tray.MouseUp += delegate(object sender, Forms.MouseEventArgs e) { if (e.Button == Forms.MouseButtons.Right) Dispatch(delegate { OpenTrayMenu(); }); };
            tray.DoubleClick += delegate { Dispatch(Show); };
            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += delegate { Tick(); }; timer.Start();
            bool initializedReminder = false;
            foreach (TodoItem item in state.Pending)
                if (item.ReminderIntervalMinutes > 0 && item.LastIntervalReminderAt == 0)
                { item.LastIntervalReminderAt = DateTime.UtcNow.Ticks; initializedReminder = true; }
            if (initializedReminder) Save();
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplayChanged;
            if (!String.IsNullOrEmpty(storage.Warning)) MessageBox.Show(storage.Warning, "DeskTodo", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        private void Dispatch(Action action)
        {
            if (exiting || disposed || app.Dispatcher.HasShutdownStarted) return;
            app.Dispatcher.BeginInvoke(new Action(delegate { if (!exiting && !disposed) action(); }));
        }
        private void CreateWindow()
        {
            IntPtr requestedSurface = state.DesktopMode && !forceWindowed ? Native.FindDesktopSurface() : IntPtr.Zero;
            bool attachmentFallback = state.DesktopMode && !forceWindowed && requestedSurface == IntPtr.Zero;
            ui = new TodoWindow(state);
            TodoWindow created = ui;
            ui.Window.ShowInTaskbar = !state.DesktopMode || forceWindowed || attachmentFallback;
            ui.Desktop.Log = storage.Log;
            using (System.Drawing.Icon icon = CreateTrayIcon())
                ui.Window.Icon = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
            ui.Changed = Save; ui.ExitRequested = Exit; ui.HideRequested = Hide;
            ui.ModeChanged = delegate { Dispatch(SwitchMode); }; ui.GetStartup = delegate { return Startup.IsEnabled; };
            ui.RebuildRequested = delegate { Dispatch(Rebuild); };
            ui.SetStartup = delegate(bool enabled) { Startup.SetEnabled(enabled); };
            ui.ExportRequested = Export; ui.ImportRequested = Import;
            ui.Window.SourceInitialized += delegate
            {
                created.InitializeNative(forceWindowed, attachmentFallback, requestedSurface); lastHost = created.Desktop.Parent;
                HwndSource source = HwndSource.FromHwnd(created.Desktop.Handle);
                if (source != null) source.Disposed += delegate { if (!exiting && !rebuilding && ui == created) Dispatch(Rebuild); };
                storage.Log(created.Desktop.Describe());
            };
            ui.Window.Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e)
            {
                if (!exiting && !rebuilding && ui == created)
                {
                    e.Cancel = true;
                    // Hide/Show is illegal inside Window.Closing, even when canceled.
                    Dispatch(delegate { if (ui == created && created.IsReady) Hide(); });
                }
            };
            ui.Window.Show();
            if (hidden) ui.Window.Hide();
            else Native.ShowWindow(ui.Desktop.Handle, 4);
            ui.PlaceOnScreen(false);
            storage.Log("Shown: " + ui.Desktop.Describe());
            ui.IsSaved = !dirty; ui.RefreshStatus();
        }
        private void Rebuild()
        {
            if (exiting || disposed || rebuilding) return;
            rebuilding = true;
            try
            {
                TodoWindow previous = ui;
                if (previous != null) { previous.IsReady = false; try { previous.Window.Close(); } catch { } }
                CreateWindow();
                if (previous != null) ui.RestoreSession(previous);
                storage.Log("Window recreated: " + ui.Desktop.Describe());
            }
            finally { rebuilding = false; }
        }
        internal void SwitchMode()
        {
            if (exiting || disposed || rebuilding) return;
            ui.RememberGeometry();
            // A WPF Window caches owner, taskbar and screen-coordinate state. Never
            // live-detach/reparent a displayed instance when changing its role.
            Rebuild();
            if (state.DesktopMode && !forceWindowed && !ui.Desktop.IsAttached && !ui.IsAttachmentFallback)
            {
                state.DesktopMode = false; Rebuild();
                ui.ShowNotice("桌面挂载失败", "已恢复窗口模式，可稍后在设置中重新嵌入桌面。");
            }
            lastHost = ui.Desktop.Parent; Save(); ui.RefreshStatus();
        }
        internal void Tick()
        {
            if (exiting || disposed || rebuilding) return;
            try
            {
                string request = Path.Combine(storage.DirectoryPath, "show.request");
                if (File.Exists(request)) { File.Delete(request); Show(); }
                string exitRequest = Path.Combine(storage.DirectoryPath, "exit.request");
                if (File.Exists(exitRequest)) { File.Delete(exitRequest); Exit(); return; }
                if (!Native.IsWindow(ui.Desktop.Handle)) { Rebuild(); return; }
                if (state.DesktopMode && !forceWindowed)
                {
                    IntPtr current = Native.FindDesktopSurface();
                    if (current != IntPtr.Zero && (!ui.Desktop.IsAttached || current != lastHost))
                    {
                        // After Explorer restarts the old parent is invalid. Its child HWND
                        // must not be queried for new geometry; retain the last saved screen
                        // coordinates and build a fresh WPF HWND under the new desktop host.
                        if (ui.Desktop.IsAttached) ui.RememberGeometry();
                        Rebuild();
                    }
                }
                if (dirty && DateTime.UtcNow - lastSaveAttempt > TimeSpan.FromSeconds(10)) Save();
                CheckReminder();
                ui.RefreshStatus();
            }
            catch (Exception e) { storage.Log("Tick: " + e.Message); }
        }
        private void Save()
        {
            ApplyTrayVisibility();
            dirty = true; lastSaveAttempt = DateTime.UtcNow;
            try { storage.Save(state); dirty = false; ui.IsSaved = true; ui.SaveError = null; }
            catch (Exception e) { ui.IsSaved = false; ui.SaveError = e.Message; storage.Log("Save failed: " + e.Message); }
            ui.RefreshStatus();
        }
        private void CheckReminder()
        {
            if (reminder != null)
            {
                TodoItem current = reminder.Item;
                if (current.Deleted || current.IsCompleted || (current.ReminderIntervalMinutes == 0 && current.DailyReminderMinutesPlusOne == 0)) reminder.Dispose();
                return;
            }
            DateTime localNow = DateTime.Now, utcNow = DateTime.UtcNow;
            foreach (TodoItem item in state.Pending)
            {
                ReminderDue due = ReminderScheduler.Due(item, localNow, utcNow);
                if (!due.Any) continue;
                TodoItem selected = item;
                ReminderScheduler.MarkShown(selected, due, localNow, utcNow); Save();
                ReminderPopup shown = new ReminderPopup(state, selected); reminder = shown;
                shown.Snoozed += delegate { selected.ReminderSnoozeUntil = DateTime.UtcNow.AddMinutes(10).Ticks; Save(); };
                shown.Dismissed += delegate { selected.ReminderSnoozeUntil = 0; Save(); };
                shown.Closed += delegate { if (reminder == shown) reminder = null; };
                shown.Show(); return;
            }
        }
        internal void Show()
        {
            if (exiting || disposed) return;
            hidden = false;
            if (ui == null || !ui.IsReady || !Native.IsWindow(ui.Desktop.Handle)) { Rebuild(); return; }
            ui.Window.Show(); Native.ShowWindow(ui.Desktop.Handle, 4); ui.PlaceOnScreen(false);
            // An explicit show request is allowed to raise the card once, without making it topmost.
            Native.SetWindowPos(ui.Desktop.Handle, IntPtr.Zero, 0, 0, 0, 0, 0x1 | 0x2 | Native.SWP_NOACTIVATE);
            if (!ui.Desktop.IsAttached) ui.Window.Activate();
        }
        internal void Hide()
        {
            if (exiting || disposed || ui == null || !ui.IsReady) return;
            // A hidden card must always have a recovery entry. Minimizing restores the
            // tray first, even if the user previously hid the tray icon.
            if (state.HideTray) { state.HideTray = false; Save(); ui.RefreshSettingsForTray(); }
            hidden = true; ui.RememberGeometry(); if (dirty) Save(); ui.Window.Hide();
        }
        private void ApplyTrayVisibility()
        {
            if (tray == null) return;
            if (state.HideTray && hidden) Show();
            tray.Visible = !state.HideTray;
        }
        internal bool TrayVisible { get { return tray != null && tray.Visible; } }
        internal void HideTray() { if (exiting || disposed) return; state.HideTray = true; Save(); Show(); ui.RefreshSettingsForTray(); }
        internal void ToggleDesktopFromTray()
        { if (exiting || disposed) return; state.DesktopMode = !state.DesktopMode; SwitchMode(); Show(); }
        internal TrayMenu OpenTrayMenu()
        {
            if (exiting || disposed || rebuilding) return null;
            TrayMenu previous = trayMenu; trayMenu = null;
            if (previous != null) previous.Close();
            TrayMenu opened = new TrayMenu(ui, Show, HideTray, ToggleDesktopFromTray,
                delegate { Show(); ui.SettingsOpen = true; }, Exit);
            trayMenu = opened;
            opened.Window.Closed += delegate { if (trayMenu == opened) trayMenu = null; };
            opened.ShowAtCursor();
            return opened;
        }
        internal void Exit()
        {
            if (exiting || disposed) return;
            ui.RememberGeometry(); Save();
            if (dirty)
            {
                MessageBox.Show("仍有未保存的修改，请检查磁盘空间或文件权限，再点击“重试保存”。软件暂不退出，以免丢失事项。", "DeskTodo", MessageBoxButton.OK, MessageBoxImage.Warning);
                Show(); return;
            }
            exiting = true; Dispose(); ui.Window.Close(); app.Shutdown();
        }
        private void Export()
        {
            Microsoft.Win32.SaveFileDialog dialog = new Microsoft.Win32.SaveFileDialog { Title = "导出待办备份", Filter = "待办备份 (*.json)|*.json", FileName = "待办备份-" + DateTime.Now.ToString("yyyyMMdd") + ".json" };
            if (dialog.ShowDialog() == true)
            {
                if (String.Equals(Path.GetFullPath(dialog.FileName), storage.DataPath, StringComparison.OrdinalIgnoreCase) || String.Equals(Path.GetFullPath(dialog.FileName), storage.DataPath + ".bak", StringComparison.OrdinalIgnoreCase))
                { ui.ShowNotice("无法导出", "请选择其他路径，以免覆盖当前正在使用的数据文件。"); return; }
                storage.Export(state, dialog.FileName);
                ui.ShowNotice("备份已导出", "备份文件已保存到：\n" + dialog.FileName);
            }
        }
        private void Import()
        {
            Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog { Title = "导入待办备份", Filter = "待办备份 (*.json)|*.json|所有文件 (*.*)|*.*", CheckFileExists = true };
            if (dialog.ShowDialog() != true) return;
            AppState imported;
            try { imported = Storage.Read(dialog.FileName); }
            catch (Exception e) { ui.ShowNotice("无法导入", "所选文件不是有效的待办备份：\n" + e.Message); return; }
            int pending = imported.Pending.Count(), completed = imported.Completed.Count();
            ui.ShowConfirmation("导入备份", "备份中有 " + pending + " 条未完成、" + completed + " 条已完成事项。\n\n导入会替换当前清单；软件会先自动保留一份导入前备份。", "导入", delegate
            {
                try
                {
                    if (reminder != null) { ReminderPopup closing = reminder; reminder = null; closing.Dispose(); }
                    string safety = Path.Combine(storage.DirectoryPath, "before-import-" + DateTime.Now.ToString("yyyyMMdd-HHmmssfff") + ".json");
                    storage.Export(state, safety);
                    ApplyImportedState(state, imported);
                    ui.ClearUndoHistory(); ui.RefreshImportedState(); ui.NotifyChanged();
                    ui.ShowNotice("导入完成", "已恢复 " + (pending + completed) + " 条事项。导入前数据已自动备份到：\n" + safety);
                }
                catch (Exception e) { ui.ShowNotice("导入未完成", "当前清单没有被替换：\n" + e.Message); }
            });
        }
        internal static void ApplyImportedState(AppState target, AppState imported)
        {
            // A backup restores content and appearance, while the current computer keeps
            // its safe desktop/window mode, coordinates and dimensions.
            target.Items = imported.Items;
            target.Title = imported.Title; target.ThemeId = imported.ThemeId; target.DarkMode = imported.DarkMode;
            target.HideItemTimes = imported.HideItemTimes; target.ShowItemDates = imported.ShowItemDates;
            target.ShowMinimizeButton = imported.ShowMinimizeButton; target.ShowCloseButton = imported.ShowCloseButton;
            target.DefaultCorner = imported.DefaultCorner; target.DefaultScalePercent = imported.DefaultScalePercent;
            target.Validate();
        }
        private void DisplayChanged(object sender, EventArgs e)
        {
            Dispatch(delegate
            {
                if (ui == null || !ui.IsReady) return;
                if (state.DesktopMode && !forceWindowed && !ui.Desktop.IsAttached) { Rebuild(); return; }
                ui.PlaceOnScreen(false); ui.RememberGeometry(); Save();
            });
        }
        private static System.Drawing.Icon CreateTrayIcon()
        {
            // The supplied ICO is embedded into the executable by build.ps1. Reuse
            // that associated icon for both the tray and WPF window so the distributed
            // three-file package does not need to carry an extra asset.
            try
            {
                string executable = Assembly.GetExecutingAssembly().Location;
                if (!String.IsNullOrEmpty(executable) && File.Exists(executable))
                {
                    System.Drawing.Icon associated = System.Drawing.Icon.ExtractAssociatedIcon(executable);
                    if (associated != null) return associated;
                }
            }
            catch { }
            using (System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap(32, 32))
            using (System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                graphics.Clear(System.Drawing.Color.Transparent);
                using (System.Drawing.Brush brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(59, 121, 92))) graphics.FillEllipse(brush, 1, 1, 30, 30);
                using (System.Drawing.Pen pen = new System.Drawing.Pen(System.Drawing.Color.White, 3))
                { pen.StartCap = pen.EndCap = System.Drawing.Drawing2D.LineCap.Round; graphics.DrawLines(pen, new[] { new System.Drawing.Point(8, 16), new System.Drawing.Point(14, 22), new System.Drawing.Point(24, 10) }); }
                IntPtr handle = bitmap.GetHicon();
                try { return (System.Drawing.Icon)System.Drawing.Icon.FromHandle(handle).Clone(); }
                finally { DestroyIcon(handle); }
            }
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            TrayMenu closingMenu = trayMenu; trayMenu = null;
            if (closingMenu != null) closingMenu.Close();
            if (timer != null) { timer.Stop(); timer = null; }
            if (reminder != null) { ReminderPopup closing = reminder; reminder = null; closing.Dispose(); }
            if (tray != null) { tray.Visible = false; tray.Icon.Dispose(); tray.Dispose(); tray = null; }
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplayChanged;
        }
    }

    // Separate entry points let the UI-checking tool launch test sessions without command-line arguments.
    // Production uses Program; these launchers always isolate test data beside their test executable.
    internal static class WindowedQaProgram
    {
        [STAThread] public static int Main()
        { return Program.Main(new[] { "--windowed", "--data-dir", Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ui-session") }); }
    }
    internal static class DesktopQaProgram
    {
        [STAThread] public static int Main()
        {
            string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "desktop-session");
            Storage store = new Storage(directory);
            if (!File.Exists(store.DataPath)) store.Save(new AppState { DesktopMode = false });
            return Program.Main(new[] { "--data-dir", directory });
        }
    }
    internal static class RepairQaProgram
    {
        [STAThread] public static int Main()
        {
            string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "repair-session");
            Storage store = new Storage(directory);
            AppState state = File.Exists(store.DataPath) ? store.Load() : new AppState();
            state.DesktopMode = false;
            if (state.Items.Count == 0) { state.Add("检查窗口大小调整"); state.Add("检查桌面模式切换"); }
            store.Save(state);
            return Program.Main(new[] { "--data-dir", directory });
        }
    }
}
