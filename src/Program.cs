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
                        // A second automatic launch must not bypass the display gate.
                        if (Array.IndexOf(args, "--startup") < 0)
                        {
                            Directory.CreateDirectory(dataDirectory);
                            File.WriteAllText(Path.Combine(dataDirectory, "show.request"), DateTime.UtcNow.ToString("o"));
                        }
                        return 0;
                    }
                    // An exit request targets the instance running at that moment, not a
                    // later replacement after an unresponsive old instance was closed.
                    string staleExit = Path.Combine(dataDirectory, "exit.request");
                    if (File.Exists(staleExit)) File.Delete(staleExit);
                    string staleShow = Path.Combine(dataDirectory, "show.request");
                    if (File.Exists(staleShow)) File.Delete(staleShow);
                    AppState state;
                    string initialState = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "initial-state.json");
                    if (!File.Exists(storage.DataPath) && Value(args, "--data-dir") == null && File.Exists(initialState))
                    {
                        state = Storage.Read(initialState); storage.Save(state);
                        storage.Log("First-run state imported; existing data is never overwritten.");
                    }
                    else state = storage.Load();
                    bool automaticStart = Array.IndexOf(args, "--startup") >= 0;
                    if (automaticStart) state.Locked = true;
                    if (state.StartOnSecondaryOnly)
                    {
                        Func<Native.MonitorArea> selected = delegate
                        { return SecondaryStartup.Current(state.SecondaryDeviceName); };
                        if (automaticStart && selected() == null)
                        {
                            storage.Log("Waiting for the selected secondary display before automatic startup.");
                            if (!SecondaryStartup.WaitForDisplay(dataDirectory, selected)) return 0;
                        }
                        Native.MonitorArea monitor = selected();
                        if (monitor != null) SecondaryStartup.Restore(state, monitor);
                        // Both manual and automatic launch show the card. A manual
                        // launch without a display remains available for settings.
                        state.MiniMode = false;
                    }
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
        private MiniBubble mini;
        private LanService lanService;
        private LanAdminWindow lanAdmin;
        private GlobalHotkeys hotkeys;
        private bool exiting, hidden, rebuilding, dirty, disposed;
        private bool secondaryWasAvailable;
        private string previousSelectedSecondaryName;
        private DateTime lastSaveAttempt;
        private IntPtr lastHost;
        private readonly object saveGate = new object();
        private int settingsSaveGeneration;
        private DispatcherTimer settingsSaveTimer;
        private bool settingsPending;
        internal Action<byte[]> SettingsSnapshotWriterForTest;
        internal TodoWindow CurrentUi { get { return ui; } }
        public Controller(Application app, Storage storage, AppState state, bool forceWindowed)
        { this.app = app; this.storage = storage; this.state = state; this.forceWindowed = forceWindowed; }
        public void Start()
        {
            Native.MonitorArea initialSecondary = SecondaryStartup.Current(state.SecondaryDeviceName);
            secondaryWasAvailable = initialSecondary != null;
            previousSelectedSecondaryName = initialSecondary == null ? null : initialSecondary.DeviceName;
            try { if (Native.TestSurface == IntPtr.Zero && Startup.MigrateLegacyCommand()) storage.Log("Startup command migrated to DeskTodo.exe."); }
            catch (Exception e) { storage.Log("Startup command migration skipped: " + e.Message); }
            hidden = state.MiniMode;
            CreateWindow();
            if (Native.TestSurface == IntPtr.Zero)
            {
                hotkeys = new GlobalHotkeys(delegate { Dispatch(Show); }, delegate { Dispatch(delegate { if (state.MiniMode) Show(); else EnterMini(); }); });
                string shortcutError = hotkeys.Configure(state.ShowHotkey, state.MiniHotkey);
                if (shortcutError != null) { storage.Log("Shortcuts: " + shortcutError); ui.Control<System.Windows.Controls.TextBlock>("HotkeyStatus").Text = shortcutError; }
            }
            tray = new Forms.NotifyIcon { Text = "DeskTodo", Icon = CreateTrayIcon(), Visible = !state.HideTray };
            tray.MouseUp += delegate(object sender, Forms.MouseEventArgs e) { if (e.Button == Forms.MouseButtons.Right) Dispatch(delegate { OpenTrayMenu(); }); };
            tray.DoubleClick += delegate { Dispatch(Show); };
            if (state.MiniMode) ShowMiniBubble();
            SyncLanService();
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
            ui.Changed = Save; ui.SettingsChanged = QueueSettingsSave; ui.ExitRequested = Exit; ui.HideRequested = Hide;
            ui.SecondaryPreferenceChanged = delegate
            {
                Native.MonitorArea current = SecondaryStartup.Current(state.SecondaryDeviceName);
                secondaryWasAvailable = current != null;
                previousSelectedSecondaryName = current == null ? null : current.DeviceName;
            };
            ui.MiniRequested = EnterMini;
            ui.ModeChanged = delegate { Dispatch(SwitchMode); }; ui.GetStartup = delegate { return Startup.IsEnabled; };
            ui.RebuildRequested = delegate { Dispatch(Rebuild); };
            ui.SetStartup = delegate(bool enabled) { Startup.SetEnabled(enabled); };
            ui.ExportRequested = Export; ui.ImportRequested = Import;
            ui.LanAdminRequested = OpenLanAdmin;
            ui.ConfigureShortcutsRequested = delegate(string showKey, string miniKey) { return hotkeys == null ? null : hotkeys.Configure(showKey, miniKey); };
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
            ui.IsSaved = !dirty; ui.IsSavePending = settingsPending; ui.RefreshStatus();
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
                ui.ShowNotice("桌面挂载失败", "已恢复为可操作的窗口模式。DeskTodo 会等待 Explorer 桌面恢复并自动重新嵌入，也可以在设置中关闭后重新开启“嵌入桌面”。");
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
                // SystemEvents can arrive while the display driver still reports an
                // intermediate topology. Retry unobtrusively until the chosen screen
                // is visible, without moving a card already on a healthy display.
                if (state.StartOnSecondaryOnly && UpdateSecondaryAvailability())
                {
                    ui.PlaceOnScreen(false); ui.RememberGeometry(); Save(); ui.RefreshSettingsForTray();
                }
                // A slow in-flight settings write must never turn the maintenance
                // timer into a UI-thread wait on the file gate.
                if (dirty && !settingsPending && DateTime.UtcNow - lastSaveAttempt > TimeSpan.FromSeconds(10)) Save();
                CheckReminder();
                ui.RefreshStatus();
            }
            catch (Exception e) { storage.Log("Tick: " + e.Message); }
        }
        private void Save()
        {
            if (settingsSaveTimer != null) settingsSaveTimer.Stop();
            Interlocked.Increment(ref settingsSaveGeneration);
            settingsPending = false; ui.IsSavePending = false;
            ApplyTrayVisibility();
            dirty = true; lastSaveAttempt = DateTime.UtcNow;
            long previousSavedAt = state.LastSavedAt;
            long previousRevision = state.DataRevision;
            state.LastSavedAt = DateTime.UtcNow.Ticks;
            state.DataRevision++;
            try { lock (saveGate) storage.Save(state); dirty = false; ui.IsSaved = true; ui.SaveError = null; SyncLanService(); }
            catch (Exception e) { state.LastSavedAt = previousSavedAt; state.DataRevision = previousRevision; ui.IsSaved = false; ui.SaveError = e.Message; storage.Log("Save failed: " + e.Message); }
            ui.RefreshStatus();
        }
        private void QueueSettingsSave()
        {
            if (exiting || disposed) return;
            Interlocked.Increment(ref settingsSaveGeneration);
            state.DataRevision++; dirty = true; settingsPending = true; lastSaveAttempt = DateTime.UtcNow;
            ApplyTrayVisibility();
            ui.IsSaved = false; ui.IsSavePending = true; ui.SaveError = null; ui.RefreshStatus();
            if (settingsSaveTimer == null)
            {
                settingsSaveTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
                settingsSaveTimer.Tick += delegate { BeginSettingsSave(); };
            }
            settingsSaveTimer.Stop(); settingsSaveTimer.Start();
        }
        internal void BeginSettingsSave()
        {
            if (settingsSaveTimer != null) settingsSaveTimer.Stop();
            if (exiting || disposed || !settingsPending) return;
            int generation = settingsSaveGeneration;
            long savedAt = DateTime.UtcNow.Ticks, previousSavedAt = state.LastSavedAt;
            byte[] snapshot;
            lastSaveAttempt = DateTime.UtcNow;
            try
            {
                // Copy on the UI thread; the worker never traverses mutable tasks
                // or controls. Its byte snapshot is independent of later edits.
                state.LastSavedAt = savedAt;
                try { snapshot = Storage.Serialize(state); }
                finally { state.LastSavedAt = previousSavedAt; }
            }
            catch (Exception e) { FinishSettingsSave(generation, savedAt, e); return; }
            ThreadPool.QueueUserWorkItem(delegate
            {
                Exception error = null;
                try
                {
                    lock (saveGate)
                    {
                        // A direct save/exit or newer change invalidates queued old
                        // snapshots. Serialize writes so an old file cannot win.
                        if (generation != Volatile.Read(ref settingsSaveGeneration)) return;
                        if (SettingsSnapshotWriterForTest != null) SettingsSnapshotWriterForTest(snapshot);
                        else storage.SaveSnapshot(snapshot);
                    }
                }
                catch (Exception e) { error = e; }
                try
                {
                    if (!disposed && !app.Dispatcher.HasShutdownStarted)
                        app.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate { FinishSettingsSave(generation, savedAt, error); }));
                }
                catch (InvalidOperationException) { } // Dispatcher may shut down between the check and posting.
            });
        }
        private void FinishSettingsSave(int generation, long savedAt, Exception error)
        {
            if (disposed || exiting || generation != settingsSaveGeneration) return;
            settingsPending = false; ui.IsSavePending = false;
            if (error == null) { state.LastSavedAt = savedAt; dirty = false; ui.IsSaved = true; ui.SaveError = null; }
            else { dirty = true; ui.IsSaved = false; ui.SaveError = error.Message; storage.Log("Settings save failed: " + error.Message); }
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
            if (state.MiniMode) { ExitMini(); return; }
            bool wasHidden = hidden;
            hidden = false;
            if (ui == null || !ui.IsReady || !Native.IsWindow(ui.Desktop.Handle)) { Rebuild(); return; }
            if (wasHidden || !ui.Window.IsVisible)
            {
                ui.PlaceOnScreen(false);
                ui.Window.Show();
                // An explicit restore raises the card once without making it topmost.
                Native.SetWindowPos(ui.Desktop.Handle, IntPtr.Zero, 0, 0, 0, 0, 0x1 | 0x2 | Native.SWP_NOACTIVATE);
            }
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
            if (state.HideTray && hidden && !state.MiniMode) Show();
            tray.Visible = !state.HideTray;
        }

        internal void EnterMini()
        {
            if (exiting || disposed || state.MiniMode || ui == null || !ui.IsReady) return;
            ui.RememberGeometry();
            hidden = true; state.MiniMode = true;
            if (state.DesktopMode && !forceWindowed)
            {
                state.DesktopMode = false;
                SwitchMode();
            }
            else ui.Window.Hide();
            Save(); ShowMiniBubble();
        }

        private void ShowMiniBubble()
        {
            if (mini != null) mini.Dispose();
            mini = new MiniBubble(state, delegate { Dispatch(ExitMini); }, delegate { Save(); });
            mini.Show();
        }

        internal void ExitMini()
        {
            if (exiting || disposed) return;
            MiniBubble closing = mini; mini = null;
            if (closing != null) closing.Dispose();
            state.MiniMode = false; hidden = false; Save();
            if (ui == null || !ui.IsReady || !Native.IsWindow(ui.Desktop.Handle)) { Rebuild(); return; }
            // Position the hidden surface before showing it. Animating Window.Opacity
            // on a reparented layered HWND can briefly expose an unpainted frame.
            ui.PlaceOnScreen(false);
            ui.Window.Opacity = 1;
            ui.Window.Show();
            if (!ui.Desktop.IsAttached) ui.Window.Activate();
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
                MessageBox.Show("仍有未保存的修改，请检查磁盘空间或文件权限。软件暂不退出，以免丢失事项。", "DeskTodo", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            target.Groups = imported.Groups;
            target.Title = imported.Title; target.ThemeId = imported.ThemeId; target.DarkMode = imported.DarkMode;
            target.Language = imported.Language; target.PendingSort = imported.PendingSort;
            target.WindowOpacityPercent = imported.WindowOpacityPercent; target.MiniAlwaysOnTop = imported.MiniAlwaysOnTop;
            target.MiniIconOpacityPercent = imported.MiniIconOpacityPercent;
            target.ShowCompactButton = imported.ShowCompactButton;
            target.ActiveGroupId = imported.ActiveGroupId;
            target.DisableGroups = imported.DisableGroups; target.CollapsedGroupIds = imported.CollapsedGroupIds;
            target.HideItemTimes = imported.HideItemTimes; target.ShowItemDates = imported.ShowItemDates;
            // Hotkeys belong to this computer; importing another user's backup
            // must not silently replace local system-wide registrations.
            target.ShowMinimizeButton = imported.ShowMinimizeButton; target.ShowCloseButton = imported.ShowCloseButton;
            target.DefaultCorner = imported.DefaultCorner; target.DefaultScalePercent = imported.DefaultScalePercent;
            target.Validate();
        }

        private void SyncLanService()
        {
            if (disposed || exiting) return;
            if (Native.TestSurface != IntPtr.Zero) return; // Isolated UI QA never publishes a LAN listener.
            if (!state.LanSharingEnabled)
            {
                if (lanService != null) { LanService closing = lanService; lanService = null; closing.Dispose(); }
                return;
            }
            if (lanService != null) return;
            try
            {
                lanService = new LanService(CreateLanPeer, HandleLanRequest, storage.Log);
                storage.Log("LAN management enabled on TCP port " + lanService.Port + ".");
            }
            catch (Exception e)
            {
                state.LanSharingEnabled = false;
                try { storage.Save(state); } catch { }
                storage.Log("LAN management could not start: " + e.Message);
                if (ui != null) { ui.RefreshSettingsForTray(); ui.ShowNotice("局域网管理未能开启", "请确认端口没有被其他程序占用，并在 Windows 防火墙中允许 DeskTodo 使用专用网络。\n\n" + e.Message); }
            }
        }

        private LanPeer CreateLanPeer()
        {
            LanPeer peer = null;
            app.Dispatcher.Invoke(new Action(delegate
            {
                Version version = Assembly.GetExecutingAssembly().GetName().Version;
                peer = new LanPeer { InstanceId = state.LanInstanceId, DisplayName = state.LanDisplayName, MachineName = Environment.MachineName, Version = version.Major + "." + version.Minor };
            }));
            return peer;
        }

        private LanSnapshot CreateLanSnapshot()
        {
            LanSnapshot snapshot = new LanSnapshot { Revision = state.DataRevision, Title = state.Title,
                PendingSort = state.PendingSort, DisableGroups = state.DisableGroups };
            foreach (TodoGroup group in state.OrderedGroups) snapshot.Groups.Add(new LanGroupInfo { Id = group.Id, Name = group.Name, Pinned = group.Pinned });
            foreach (TodoItem item in state.Items.Where(i => !i.Deleted)) snapshot.Tasks.Add(new LanTaskInfo
            {
                Id = item.Id, Text = item.Text, GroupId = item.GroupId, Completed = item.IsCompleted,
                CreatedAt = item.CreatedAt, CompletedAt = item.CompletedAt, Pinned = item.Pinned, SortOrder = item.SortOrder,
                ReminderIntervalMinutes = item.ReminderIntervalMinutes, DailyReminderMinutesPlusOne = item.DailyReminderMinutesPlusOne,
                ReminderManaged = item.ReminderManaged
            });
            return snapshot;
        }

        private LanResponse HandleLanRequest(LanRequest request)
        {
            LanResponse response = null;
            app.Dispatcher.Invoke(new Action(delegate
            {
                if (request == null || !state.LanSharingEnabled) { response = LanResponse.Fail("对方未开启局域网管理。"); return; }
                if (!String.Equals((request.AccessCode ?? "").Trim(), state.LanAccessCode, StringComparison.OrdinalIgnoreCase)) { response = LanResponse.Fail("管理口令不正确。"); return; }
                try
                {
                    string operation = (request.Operation ?? "").ToLowerInvariant();
                    if (operation == "snapshot")
                    {
                        // Always authenticate before checking revisions. Unchanged
                        // polls do not allocate/serialize the entire task history.
                        response = new LanResponse { Success = true,
                            Snapshot = request.KnownRevisionPlusOne > 0 && request.KnownRevisionPlusOne - 1 == state.DataRevision
                                ? null : CreateLanSnapshot() };
                        return;
                    }
                    string groupId = String.IsNullOrWhiteSpace(request.GroupId) ? null : request.GroupId;
                    if (groupId != null && !state.Groups.Any(g => g.Id == groupId))
                    {
                        if (operation == "reorder" && String.IsNullOrWhiteSpace(request.TargetId))
                            throw new InvalidOperationException("目标分组已不存在，请刷新后重试。");
                        groupId = null;
                    }
                    if (request.ExpectedRevisionPlusOne > 0 && request.ExpectedRevisionPlusOne - 1 != state.DataRevision)
                        throw new InvalidOperationException("对方待办刚有变化，请刷新后重试，未覆盖其他人的修改。");
                    if (operation == "reorder-group")
                    {
                        if (state.DisableGroups) throw new InvalidOperationException("对方已关闭分组。");
                        if (!ui.CanMoveGroup(request.ItemId, request.TargetId)) throw new InvalidOperationException("只能在相同置顶状态的分组之间调整顺序。");
                        ui.MoveGroup(request.ItemId, request.TargetId, request.After);
                        response = new LanResponse { Success = true, Snapshot = CreateLanSnapshot() }; return;
                    }
                    if (operation == "add")
                    {
                        TodoItem added = state.Add(request.Text, groupId);
                        if (added == null) throw new InvalidOperationException("待办内容不能为空。");
                        if (request.Completed) added.CompletedAt = DateTime.UtcNow.Ticks;
                        ui.NotifyChanged(); ui.Refresh();
                    }
                    else
                    {
                        TodoItem item = state.Items.FirstOrDefault(i => !i.Deleted && i.Id == request.ItemId);
                        if (item == null) throw new InvalidOperationException("这条待办已不存在，请刷新后重试。");
                        if (operation == "delete") ui.Remove(item);
                        else if (operation == "reminder")
                        {
                            if (item.IsCompleted) throw new InvalidOperationException("请给未完成待办设置提醒。");
                            if (request.ReminderIntervalMinutes < 0 || request.ReminderIntervalMinutes > 10080 ||
                                request.DailyReminderMinutesPlusOne < 0 || request.DailyReminderMinutesPlusOne > 1440)
                                throw new InvalidOperationException("提醒时间无效。");
                            item.ReminderIntervalMinutes = request.ReminderIntervalMinutes;
                            item.DailyReminderMinutesPlusOne = request.DailyReminderMinutesPlusOne;
                            item.ReminderManaged = request.ReminderManaged && (item.ReminderIntervalMinutes > 0 || item.DailyReminderMinutesPlusOne > 0);
                            item.LastIntervalReminderAt = item.ReminderIntervalMinutes > 0 ? DateTime.UtcNow.Ticks : 0;
                            item.LastDailyReminderDate = null; item.ReminderSnoozeUntil = 0;
                            ui.NotifyChanged(); ui.Refresh();
                        }
                        else if (operation == "reorder")
                        {
                            TodoItem target = state.Pending.FirstOrDefault(t => t.Id == request.TargetId);
                            if (item.IsCompleted || item == target || (target != null && item.Pinned != target.Pinned) ||
                                (target == null && (!String.IsNullOrWhiteSpace(request.TargetId) || state.DisableGroups)))
                                throw new InvalidOperationException("只能在相同置顶状态的未完成待办之间拖动。");
                            System.Collections.Generic.List<TodoItem> order = state.Pending.ToList();
                            if (!state.DisableGroups) item.GroupId = target == null ? groupId : target.GroupId;
                            order.Remove(item);
                            int index = target == null ? order.FindLastIndex(t => String.Equals(t.GroupId ?? "", item.GroupId ?? "", StringComparison.Ordinal) && t.Pinned == item.Pinned) + 1
                                : order.IndexOf(target) + (request.After ? 1 : 0);
                            order.Insert(Math.Max(0, Math.Min(index, order.Count)), item);
                            ui.ApplyVisibleManualOrder(order); ui.NotifyChanged(); ui.Refresh();
                        }
                        else if (operation == "update")
                        {
                            string text = (request.Text ?? "").Trim();
                            if (text.Length == 0 || text.Length > 500) throw new InvalidOperationException("待办内容须为 1–500 个字。");
                            item.Text = text; item.GroupId = groupId;
                            if (request.Completed != item.IsCompleted)
                            {
                                state.Toggle(item);
                                if (!item.IsCompleted && item.ReminderIntervalMinutes > 0) item.LastIntervalReminderAt = DateTime.UtcNow.Ticks;
                            }
                            ui.NotifyChanged(); ui.Refresh();
                        }
                        else throw new InvalidOperationException("不支持的操作。");
                    }
                    response = new LanResponse { Success = true, Snapshot = CreateLanSnapshot() };
                }
                catch (Exception e) { response = LanResponse.Fail(e.Message); }
            }));
            return response;
        }

        private void OpenLanAdmin()
        {
            if (lanAdmin == null || !lanAdmin.Window.IsLoaded)
            {
                lanAdmin = new LanAdminWindow(state, storage.DirectoryPath);
                lanAdmin.Log = storage.Log;
                LanAdminWindow opened = lanAdmin;
                opened.Window.Closed += delegate { if (lanAdmin == opened) lanAdmin = null; };
            }
            lanAdmin.Show();
        }
        private void DisplayChanged(object sender, EventArgs e)
        {
            Dispatch(delegate
            {
                if (mini != null) { mini.EnsureVisible(); Save(); }
                if (ui == null || !ui.IsReady) return;
                UpdateSecondaryAvailability();
                if (state.DesktopMode && !forceWindowed && !ui.Desktop.IsAttached) { Rebuild(); ui.RememberGeometry(); Save(); ui.RefreshSettingsForTray(); return; }
                ui.PlaceOnScreen(false); ui.RememberGeometry(); Save(); ui.RefreshSettingsForTray();
            });
        }
        private bool UpdateSecondaryAvailability()
        {
            Native.MonitorArea selected = SecondaryStartup.Current(state.SecondaryDeviceName);
            bool nowAvailable = selected != null;
            bool selectedReturned = nowAvailable && !String.IsNullOrEmpty(state.SecondaryDeviceName) &&
                String.Equals(selected.DeviceName, state.SecondaryDeviceName, StringComparison.OrdinalIgnoreCase) &&
                !String.Equals(previousSelectedSecondaryName, selected.DeviceName, StringComparison.OrdinalIgnoreCase);
            bool restore = state.StartOnSecondaryOnly && nowAvailable && (!secondaryWasAvailable || selectedReturned);
            if (restore) SecondaryStartup.Restore(state, selected);
            secondaryWasAvailable = nowAvailable;
            previousSelectedSecondaryName = nowAvailable ? selected.DeviceName : null;
            return restore;
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
            Interlocked.Increment(ref settingsSaveGeneration);
            if (settingsSaveTimer != null) { settingsSaveTimer.Stop(); settingsSaveTimer = null; }
            TrayMenu closingMenu = trayMenu; trayMenu = null;
            if (closingMenu != null) closingMenu.Close();
            if (timer != null) { timer.Stop(); timer = null; }
            if (hotkeys != null) { hotkeys.Dispose(); hotkeys = null; }
            if (reminder != null) { ReminderPopup closing = reminder; reminder = null; closing.Dispose(); }
            if (mini != null) { MiniBubble closingMini = mini; mini = null; closingMini.Dispose(); }
            if (lanAdmin != null) { LanAdminWindow closingAdmin = lanAdmin; lanAdmin = null; closingAdmin.Dispose(); }
            if (lanService != null) { LanService closingLan = lanService; lanService = null; closingLan.Dispose(); }
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
