using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace DeskTodoSetup
{
    internal static class SetupProgram
    {
        internal static string SelfPath { get { return Assembly.GetExecutingAssembly().Location; } }
        internal static string Version { get { System.Version v = Assembly.GetExecutingAssembly().GetName().Version; return v.Major + "." + v.Minor; } }
        [STAThread] public static int Main(string[] args)
        {
            try
            {
                AppContext.SetSwitch("Switch.System.Windows.DoNotScaleForDpiChanges", false);
                int test = Array.IndexOf(args, "--self-test");
                if (test >= 0) return SetupTests.Run(args.Length > test + 1 ? args[test + 1] : Path.Combine(Path.GetTempPath(), "DeskTodo-setup-qa"));
                InstallPaths paths = InstallPaths.ForCurrentUser();
                string selected = Value(args, "--install-dir");
                if (selected != null) paths.Directory = Path.GetFullPath(selected);
                if (Array.IndexOf(args, "--apply") >= 0)
                {
                    paths.StartMenu = Value(args, "--start-menu-dir"); paths.Desktop = Value(args, "--desktop-dir");
                    paths.Data = Value(args, "--data-dir"); paths.UserSid = Value(args, "--user-sid");
                    // Elevated worker operates on the original user's data/registry,
                    // including when UAC is approved using a different admin account.
                    new SecurityIdentifier(paths.UserSid);
                    InstallService worker = new InstallService(paths, new UserRegistration(paths));
                    worker.AddStartMenu = Value(args, "--start-menu") == "1";
                    worker.AddDesktop = Value(args, "--desktop") == "1";
                    string result = Value(args, "--result");
                    try { if (Array.IndexOf(args, "--uninstall") >= 0) worker.Uninstall(); else worker.Install(); File.WriteAllText(result, "OK", Encoding.UTF8); return 0; }
                    catch (Exception e) { File.WriteAllText(result, e.Message, Encoding.UTF8); return 1; }
                }
                bool uninstall = Array.IndexOf(args, "--uninstall") >= 0 || String.Equals(Path.GetFileName(SelfPath), "Uninstall.exe", StringComparison.OrdinalIgnoreCase);
                if (uninstall && selected == null) paths.Directory = Path.GetDirectoryName(SelfPath);
                if (uninstall && InstallPaths.Same(Path.GetDirectoryName(SelfPath), paths.Directory))
                {
                    // Run outside the installed directory so the uninstaller can remove
                    // only the fixed program files, including its original executable.
                    string temp = Path.Combine(Path.GetTempPath(), "DeskTodo-Uninstall-" + Guid.NewGuid().ToString("N"));
                    System.IO.Directory.CreateDirectory(temp);
                    string copy = Path.Combine(temp, "Uninstall.exe"); File.Copy(SelfPath, copy);
                    Process.Start(new ProcessStartInfo(copy, "--uninstall --install-dir " + Quote(paths.Directory)) { UseShellExecute = true }); return 0;
                }
                using (Mutex mutex = new Mutex(false, "Local\\DeskTodo-Setup-" + Environment.UserName))
                {
                    bool acquired; try { acquired = mutex.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
                    if (!acquired) { MessageBox.Show("另一项 DeskTodo 安装或卸载正在进行，请稍后再试。", "DeskTodo"); return 1; }
                    try
                    {
                        Application app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                        SetupUi ui = new SetupUi(new InstallService(paths, new UserRegistration(paths)), uninstall);
                        app.Run(ui.Window); return 0;
                    }
                    finally { mutex.ReleaseMutex(); }
                }
            }
            catch (Exception e) { MessageBox.Show(e.Message, "DeskTodo 安装未完成", MessageBoxButton.OK, MessageBoxImage.Warning); return 1; }
        }
        internal static string Value(string[] args, string key) { int at = Array.IndexOf(args, key); return at >= 0 && at + 1 < args.Length ? args[at + 1] : null; }
        internal static string Quote(string value) { return "\"" + value.Replace("\"", "").TrimEnd('\\') + "\""; }
        internal static byte[] Resource(string name)
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("DeskTodo.Setup." + name))
            { if (stream == null) throw new IOException("安装包文件不完整：" + name); using (MemoryStream result = new MemoryStream()) { stream.CopyTo(result); return result.ToArray(); } }
        }
    }

    internal sealed class InstallPaths
    {
        internal string Directory, StartMenu, Desktop, Data;
        internal string UserSid;
        internal bool Test;
        internal string App { get { return Path.Combine(Directory, "DeskTodo.exe"); } }
        internal string ShellIcon { get { return Path.Combine(Directory, InstallService.IconFileName); } }
        internal string Uninstaller { get { return Path.Combine(Directory, "Uninstall.exe"); } }
        internal string MenuLink { get { return Path.Combine(StartMenu, "DeskTodo.lnk"); } }
        internal string DesktopLink { get { return Path.Combine(Desktop, "DeskTodo.lnk"); } }
        internal static InstallPaths ForCurrentUser()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return new InstallPaths { Directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "DeskTodo"),
                StartMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "DeskTodo"),
                Desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Data = Path.Combine(local, "DesktopTodo"), UserSid = WindowsIdentity.GetCurrent().User.Value };
        }
        internal static bool Same(string first, string second)
        { return String.Equals(Path.GetFullPath(first).TrimEnd('\\'), Path.GetFullPath(second).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase); }
        internal void Validate()
        {
            if (String.IsNullOrWhiteSpace(Directory) || !Path.IsPathRooted(Directory) || Directory.StartsWith(@"\\")) throw new IOException("请选择本机磁盘上的绝对安装路径。");
            Directory = Path.GetFullPath(Directory).TrimEnd('\\');
            if (Directory.Contains("\"") || Directory.IndexOfAny(Path.GetInvalidPathChars()) >= 0) throw new IOException("安装路径包含无效字符。");
            if (Same(Directory, Data) || Same(Directory, Path.GetPathRoot(Directory)) || Under(Data, Directory) || Under(Directory, Data)) throw new IOException("不能把数据目录、其父目录或磁盘根目录用作安装目录。");
            if (!Test)
            {
                foreach (string protectedPath in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Desktop, StartMenu })
                    if (!String.IsNullOrEmpty(protectedPath) && Same(Directory, protectedPath)) throw new IOException("请选择独立的 DeskTodo 子文件夹，不要直接安装到系统或个人目录。");
                if (Under(Directory, Environment.GetFolderPath(Environment.SpecialFolder.Windows))) throw new IOException("不能安装到 Windows 系统目录。");
                for (DirectoryInfo at = new DirectoryInfo(Directory); at != null; at = at.Parent)
                    if (at.Exists && (at.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("安装目录不能经过符号链接或目录联接，请选择普通文件夹。");
            }
            foreach (string name in InstallService.FileNames)
                if (!Same(Path.GetDirectoryName(Path.Combine(Directory, name)), Directory)) throw new IOException("程序文件路径无效。");
        }
        internal static bool Under(string path, string parent) { return !String.IsNullOrEmpty(parent) && Path.GetFullPath(path).StartsWith(Path.GetFullPath(parent).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase); }
    }
    internal sealed class RegistrationSnapshot
    {
        internal bool Exists;
        internal Dictionary<string, object> Values = new Dictionary<string, object>();
        internal string Startup;
    }
    internal interface IRegistration
    {
        RegistrationSnapshot Capture();
        void Register();
        void Remove();
        void Restore(RegistrationSnapshot snapshot);
    }
    internal sealed class UserRegistration : IRegistration
    {
        private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\DeskTodo";
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private readonly InstallPaths paths;
        internal UserRegistration(InstallPaths paths) { this.paths = paths; }
        private RegistryKey UserRoot { get { return Registry.Users.OpenSubKey(paths.UserSid, true); } }
        public RegistrationSnapshot Capture()
        {
            RegistrationSnapshot snapshot = new RegistrationSnapshot();
            using (RegistryKey root = UserRoot)
            using (RegistryKey key = root.OpenSubKey(UninstallKey))
            { if (key != null) { snapshot.Exists = true; foreach (string name in key.GetValueNames()) snapshot.Values[name] = key.GetValue(name); } }
            using (RegistryKey root = UserRoot)
            using (RegistryKey key = root.OpenSubKey(RunKey)) snapshot.Startup = key == null ? null : key.GetValue("DesktopTodo") as string;
            return snapshot;
        }
        public void Register()
        {
            using (RegistryKey root = UserRoot)
            using (RegistryKey key = root.CreateSubKey(UninstallKey))
            {
                key.SetValue("DisplayName", "DeskTodo"); key.SetValue("DisplayVersion", SetupProgram.Version);
                key.SetValue("Publisher", "DeskTodo"); key.SetValue("InstallLocation", paths.Directory);
                key.SetValue("DisplayIcon", paths.ShellIcon); key.SetValue("UninstallString", "\"" + paths.Uninstaller + "\" --uninstall");
                key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                key.SetValue("NoModify", 1, RegistryValueKind.DWord); key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                key.SetValue("EstimatedSize", (int)(InstallService.FileNames.Sum(name => new FileInfo(Path.Combine(paths.Directory, name)).Length) / 1024), RegistryValueKind.DWord);
            }
            // Keep an existing DeskTodo auto-start enabled when installing/upgrading,
            // but never enable it for a user who previously disabled it.
            string old = Capture().Startup;
            if (IsDeskTodoCommand(old)) using (RegistryKey root = UserRoot) using (RegistryKey key = root.CreateSubKey(RunKey)) key.SetValue("DesktopTodo", "\"" + paths.App + "\" --startup");
        }
        private static bool IsDeskTodoCommand(string command)
        {
            string executable = CommandPath(command);
            return String.Equals(Path.GetFileName(executable), "DeskTodo.exe", StringComparison.OrdinalIgnoreCase) || String.Equals(Path.GetFileName(executable), "桌面待办.exe", StringComparison.OrdinalIgnoreCase);
        }
        internal static string CommandPath(string command)
        {
            if (String.IsNullOrWhiteSpace(command)) return "";
            command = command.Trim(); int end = command[0] == '"' ? command.IndexOf('"', 1) : command.IndexOf(" --", StringComparison.Ordinal);
            return command[0] == '"' ? (end > 0 ? command.Substring(1, end - 1) : "") : (end > 0 ? command.Substring(0, end) : command);
        }
        public void Remove()
        {
            RegistrationSnapshot snapshot = Capture(); object location;
            if (snapshot.Values.TryGetValue("InstallLocation", out location) && InstallPaths.Same(location.ToString(), paths.Directory)) using (RegistryKey root = UserRoot) root.DeleteSubKeyTree(UninstallKey, false);
            string command = CommandPath(snapshot.Startup);
            if (!String.IsNullOrEmpty(command) && InstallPaths.Same(command, paths.App)) using (RegistryKey root = UserRoot) using (RegistryKey key = root.OpenSubKey(RunKey, true)) { if (key != null) key.DeleteValue("DesktopTodo", false); }
        }
        public void Restore(RegistrationSnapshot snapshot)
        {
            using (RegistryKey root = UserRoot)
            {
                root.DeleteSubKeyTree(UninstallKey, false);
                if (snapshot.Exists) using (RegistryKey key = root.CreateSubKey(UninstallKey)) foreach (KeyValuePair<string, object> value in snapshot.Values) key.SetValue(value.Key, value.Value);
                using (RegistryKey key = root.CreateSubKey(RunKey)) { if (snapshot.Startup == null) key.DeleteValue("DesktopTodo", false); else key.SetValue("DesktopTodo", snapshot.Startup); }
            }
        }
    }

    internal sealed class InstallService
    {
        // A new path gives the shell a fresh icon-cache key on upgrade. Do not
        // clear global caches or restart Explorer to refresh our shortcuts.
        internal static readonly string IconFileName = "DeskTodo-v" + SetupProgram.Version + ".ico";
        internal static readonly string[] FileNames = { "DeskTodo.exe", "DeskTodo.exe.config", "使用说明.md", "Uninstall.exe", IconFileName };
        internal readonly InstallPaths Paths;
        private readonly IRegistration registration;
        internal bool FailAfterFilesForTest;
        internal bool AddStartMenu = true, AddDesktop;
        internal InstallService(InstallPaths paths, IRegistration registration) { Paths = paths; this.registration = registration; }
        internal static Task RunOnSta(Action operation)
        {
            // Windows shortcut COM objects require an apartment suitable for shell
            // operations. Keep them on one dedicated STA instead of a pool MTA.
            TaskCompletionSource<bool> completion = new TaskCompletionSource<bool>();
            Thread worker = new Thread(delegate()
            { try { operation(); completion.SetResult(true); } catch (Exception e) { completion.SetException(e); } });
            worker.IsBackground = true; worker.SetApartmentState(ApartmentState.STA); worker.Start(); return completion.Task;
        }
        private Dictionary<string, byte[]> SnapshotFiles()
        {
            Dictionary<string, byte[]> files = new Dictionary<string, byte[]>();
            foreach (string path in FileNames.Select(name => Path.Combine(Paths.Directory, name)).Concat(new[] { Paths.MenuLink, Paths.DesktopLink })) files[path] = File.Exists(path) ? File.ReadAllBytes(path) : null;
            return files;
        }
        private void Rollback(Dictionary<string, byte[]> files, RegistrationSnapshot snapshot)
        {
            foreach (KeyValuePair<string, byte[]> file in files)
                if (file.Value == null) { if (File.Exists(file.Key)) File.Delete(file.Key); }
                else { Directory.CreateDirectory(Path.GetDirectoryName(file.Key)); File.WriteAllBytes(file.Key, file.Value); }
            registration.Restore(snapshot);
        }
        internal void Install()
        {
            Paths.Validate(); StopRunningInstance();
            // Read and verify embedded payloads before touching existing installation.
            byte[][] payload = { SetupProgram.Resource("App"), SetupProgram.Resource("Config"), SetupProgram.Resource("Readme"), File.ReadAllBytes(SetupProgram.SelfPath), SetupProgram.Resource("Icon") };
            if (payload[0].Length < 2 || payload[0][0] != 'M' || payload[0][1] != 'Z') throw new IOException("安装包中的程序无效。");
            Dictionary<string, byte[]> previous = SnapshotFiles(); RegistrationSnapshot snapshot = registration.Capture();
            try
            {
                Directory.CreateDirectory(Paths.Directory);
                for (int i = 0; i < FileNames.Length; i++) WriteAtomic(Path.Combine(Paths.Directory, FileNames[i]), payload[i]);
                if (FailAfterFilesForTest) throw new IOException("模拟安装写入失败");
                string previousApp = Paths.App; object oldLocation;
                if (snapshot.Values.TryGetValue("InstallLocation", out oldLocation)) previousApp = Path.Combine(oldLocation.ToString(), "DeskTodo.exe");
                SetShortcut(Paths.MenuLink, AddStartMenu, previousApp);
                SetShortcut(Paths.DesktopLink, AddDesktop, previousApp);
                registration.Register();
                NotifyShell(Paths.App);
            }
            catch { Rollback(previous, snapshot); throw; }
        }
        private void SetShortcut(string link, bool enabled, string previousApp)
        {
            if (enabled) CreateShortcut(link, Paths.App, Paths.ShellIcon);
            else if (IsOwnShortcut(link, Paths.App) || IsOwnShortcut(link, previousApp)) File.Delete(link);
        }
        internal string WorkerArguments(bool uninstall, string result)
        {
            return "--apply " + (uninstall ? "--uninstall " : "") + "--install-dir " + SetupProgram.Quote(Paths.Directory) +
                " --start-menu-dir " + SetupProgram.Quote(Paths.StartMenu) + " --desktop-dir " + SetupProgram.Quote(Paths.Desktop) +
                " --data-dir " + SetupProgram.Quote(Paths.Data) + " --user-sid " + Paths.UserSid +
                " --start-menu " + (AddStartMenu ? "1" : "0") + " --desktop " + (AddDesktop ? "1" : "0") + " --result " + SetupProgram.Quote(result);
        }
        internal async Task Apply(bool uninstall)
        {
            try { await RunOnSta(delegate { if (uninstall) Uninstall(); else Install(); }); return; }
            catch (UnauthorizedAccessException)
            {
                if (Paths.Test) throw;
            }
            // Elevate only the file-operation worker, never the wizard or app.
            string result = Path.Combine(Path.GetTempPath(), "DeskTodo-Install-" + Guid.NewGuid().ToString("N") + ".result");
            try
            {
                using (Process worker = Process.Start(new ProcessStartInfo(SetupProgram.SelfPath, WorkerArguments(uninstall, result)) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden }))
                {
                    await Task.Run(delegate { worker.WaitForExit(); });
                    string response = File.Exists(result) ? File.ReadAllText(result, Encoding.UTF8) : "安装工作进程未返回结果，请重试。";
                    if (worker.ExitCode != 0 || response != "OK") throw new IOException(response);
                }
            }
            finally { if (File.Exists(result)) File.Delete(result); }
        }
        internal void SetDesktopShortcut(bool enabled)
        {
            Paths.Validate();
            if (enabled) CreateShortcut(Paths.DesktopLink, Paths.App, Paths.ShellIcon);
            else if (IsOwnShortcut(Paths.DesktopLink, Paths.App)) File.Delete(Paths.DesktopLink);
        }
        internal void Uninstall()
        {
            Paths.Validate(); StopRunningInstance();
            Dictionary<string, byte[]> previous = SnapshotFiles(); RegistrationSnapshot snapshot = registration.Capture();
            try
            {
                foreach (string name in FileNames) { string path = Path.Combine(Paths.Directory, name); if (File.Exists(path)) File.Delete(path); }
                foreach (string shortcut in new[] { Paths.MenuLink, Paths.DesktopLink }) if (IsOwnShortcut(shortcut, Paths.App)) File.Delete(shortcut);
                registration.Remove();
            }
            catch { Rollback(previous, snapshot); throw; }
            RemoveEmptyDirectory(Paths.Directory); RemoveEmptyDirectory(Paths.StartMenu);
            // Never delete data, logs, backups, unrelated files, or whole directory trees.
        }
        private static void RemoveEmptyDirectory(string path)
        { if (Directory.Exists(path) && Directory.GetFileSystemEntries(path).Length == 0) Directory.Delete(path, false); }
        private static void WriteAtomic(string path, byte[] bytes)
        {
            string staged = path + ".install-" + Guid.NewGuid().ToString("N");
            try { File.WriteAllBytes(staged, bytes); if (File.Exists(path)) File.Replace(staged, path, null); else File.Move(staged, path); }
            finally { if (File.Exists(staged)) File.Delete(staged); }
        }
        private void StopRunningInstance()
        {
            if (Paths.Test) return;
            string name;
            using (SHA256 hash = SHA256.Create()) name = "Local\\DesktopTodo-" + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(Paths.Data).ToUpperInvariant()))).Replace("-", "");
            using (Mutex mutex = new Mutex(false, name))
            {
                bool acquired; try { acquired = mutex.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
                if (!acquired)
                {
                    Directory.CreateDirectory(Paths.Data); File.WriteAllText(Path.Combine(Paths.Data, "exit.request"), DateTime.UtcNow.ToString("o"));
                    try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(12)); } catch (AbandonedMutexException) { acquired = true; }
                    if (!acquired) throw new IOException("DeskTodo 尚未退出。请先保存并关闭软件，再重试；当前程序文件没有被覆盖。");
                }
                mutex.ReleaseMutex();
            }
        }
        internal static void CreateShortcut(string path, string target, string iconPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)); object shell = null, link = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true));
                link = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                SetCom(link, "TargetPath", target); SetCom(link, "WorkingDirectory", Path.GetDirectoryName(target)); SetCom(link, "IconLocation", iconPath + ",0"); SetCom(link, "Description", "DeskTodo 桌面待办");
                link.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
                NotifyShell(path);
            }
            finally { if (link != null) Marshal.FinalReleaseComObject(link); if (shell != null) Marshal.FinalReleaseComObject(shell); }
        }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern void SHChangeNotify(uint eventId, uint flags, [MarshalAs(UnmanagedType.LPWStr)] string path, IntPtr second);
        private static void NotifyShell(string path) { SHChangeNotify(0x2000, 5, path, IntPtr.Zero); }
        private static void SetCom(object obj, string property, object value) { obj.GetType().InvokeMember(property, BindingFlags.SetProperty, null, obj, new[] { value }); }
        internal static bool IsOwnShortcut(string path, string target)
        {
            if (!File.Exists(path)) return false; object shell = null, link = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true)); link = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                string actual = link.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, link, null) as string;
                return !String.IsNullOrEmpty(actual) && InstallPaths.Same(actual, target);
            }
            catch { return false; }
            finally { if (link != null) Marshal.FinalReleaseComObject(link); if (shell != null) Marshal.FinalReleaseComObject(shell); }
        }
    }

    internal sealed class SetupUi
    {
        internal readonly Window Window;
        private readonly InstallService service;
        private readonly bool uninstall;
        private bool busy, finished;
        private int page;
        internal T Control<T>(string name) where T : class { return Window.FindName(name) as T; }
        internal SetupUi(InstallService service, bool uninstall)
        {
            this.service = service; this.uninstall = uninstall;
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("DeskTodo.Setup.Window")) Window = (Window)XamlReader.Load(stream);
            using (MemoryStream stream = new MemoryStream(SetupProgram.Resource("Icon")))
            {
                IconBitmapDecoder decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                BitmapFrame image = decoder.Frames.OrderByDescending(frame => frame.PixelWidth).First(); image.Freeze(); Control<Image>("SetupIcon").Source = image;
            }
            using (System.Drawing.Icon icon = System.Drawing.Icon.ExtractAssociatedIcon(SetupProgram.SelfPath)) Window.Icon = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            Control<TextBox>("InstallPath").Text = service.Paths.Directory;
            Control<TextBlock>("Subtitle").Text = "桌面上的简约待办 · v" + SetupProgram.Version;
            Control<TextBlock>("DataNote").Visibility = Visibility.Collapsed;
            if (uninstall)
            {
                Window.Title = "DeskTodo 卸载"; Control<TextBlock>("Heading").Text = "卸载 DeskTodo";
                Control<TextBlock>("PathLabel").Text = "卸载位置";
                Control<TextBlock>("Description").Text = "确认卸载？只删除程序文件、开始菜单入口和本软件的桌面快捷方式。";
                Control<TextBlock>("DataNote").Text = "你的待办和备份会保留在：\n" + service.Paths.Data;
                Control<TextBlock>("DataNote").Visibility = Visibility.Visible;
                Control<Button>("Primary").Content = "卸载";
                Control<TextBox>("InstallPath").IsReadOnly = true; Control<Button>("Browse").Visibility = Visibility.Collapsed;
            }
            Control<Button>("Browse").Click += delegate
            {
                using (System.Windows.Forms.FolderBrowserDialog browser = new System.Windows.Forms.FolderBrowserDialog { Description = "选择 DeskTodo 安装文件夹", SelectedPath = Control<TextBox>("InstallPath").Text, ShowNewFolderButton = true })
                    if (browser.ShowDialog() == System.Windows.Forms.DialogResult.OK) Control<TextBox>("InstallPath").Text = browser.SelectedPath;
            };
            Control<Button>("Back").Click += delegate { if (!busy && !finished) SetPage(0); };
            Control<Button>("Cancel").Click += delegate { if (!busy) Window.Close(); };
            Control<Button>("Primary").Click += async delegate { await Primary(); };
            Window.Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) { e.Cancel = busy; };
        }
        private async Task Primary()
        {
            if (busy) return;
            if (finished)
            {
                try
                {
                    if (!uninstall)
                    {
                        if (Control<CheckBox>("LaunchApp").IsChecked == true) Process.Start(new ProcessStartInfo(service.Paths.App) { UseShellExecute = true, WorkingDirectory = service.Paths.Directory });
                    }
                    Window.Close();
                }
                catch (Exception e) { Control<TextBlock>("Status").Text = "无法完成：" + e.Message + "。可取消勾选启动或快捷方式后重试。"; }
                return;
            }
            if (!uninstall && page == 0)
            {
                try { service.Paths.Directory = Control<TextBox>("InstallPath").Text.Trim(); service.Paths.Validate(); SetPage(1); }
                catch (Exception e) { Control<TextBlock>("Status").Text = e.Message; }
                return;
            }
            service.AddStartMenu = Control<CheckBox>("StartMenuShortcut").IsChecked == true;
            service.AddDesktop = Control<CheckBox>("DesktopShortcut").IsChecked == true;
            busy = true; Control<Button>("Primary").IsEnabled = false; Control<Button>("Cancel").IsEnabled = false;
            Control<Button>("Back").IsEnabled = false; Control<StackPanel>("ShortcutPage").IsEnabled = false;
            Control<ProgressBar>("Progress").Visibility = Visibility.Visible;
            Control<TextBlock>("Status").Text = uninstall ? "正在安全卸载，待办数据不会删除…" : "正在安装；如软件已运行，将先保存并退出…";
            try
            {
                await service.Apply(uninstall);
                ShowSuccess();
            }
            catch (Exception e) { Control<TextBlock>("Status").Text = "未完成：" + e.Message + "。已有安装已保留，可重试。"; }
            finally { busy = false; Control<Button>("Primary").IsEnabled = true; Control<Button>("Cancel").IsEnabled = !finished; Control<Button>("Back").IsEnabled = true; Control<StackPanel>("ShortcutPage").IsEnabled = true; Control<ProgressBar>("Progress").Visibility = Visibility.Collapsed; }
        }
        private void SetPage(int next)
        {
            page = next;
            Control<StackPanel>("LocationPage").Visibility = page == 0 ? Visibility.Visible : Visibility.Collapsed;
            Control<StackPanel>("ShortcutPage").Visibility = page == 1 ? Visibility.Visible : Visibility.Collapsed;
            Control<Button>("Back").Visibility = page == 1 ? Visibility.Visible : Visibility.Collapsed;
            Control<Button>("Primary").Content = page == 0 ? "下一步" : "安装";
            Control<TextBlock>("Status").Text = "";
        }
        internal void ShowSuccess()
        {
            finished = true; Control<TextBlock>("Heading").Text = uninstall ? "已卸载 DeskTodo" : "安装完成";
            Control<TextBlock>("Description").Text = uninstall ? "程序已移除，你的待办和备份仍然保留。重新安装后可继续使用。" : "DeskTodo 已准备就绪。";
            Control<StackPanel>("LocationPage").Visibility = Visibility.Collapsed; Control<StackPanel>("ShortcutPage").Visibility = Visibility.Collapsed;
            Control<Button>("Back").Visibility = Visibility.Collapsed;
            Control<TextBlock>("Status").Text = uninstall ? "卸载完成" : "DeskTodo v" + SetupProgram.Version + " 已安装";
            Control<Button>("Primary").Content = "完成"; Control<Button>("Cancel").Visibility = Visibility.Collapsed;
            if (!uninstall) Control<StackPanel>("FinishOptions").Visibility = Visibility.Visible;
        }
    }

    internal static class SetupTests
    {
        private sealed class FakeRegistration : IRegistration
        {
            internal bool Registered;
            public RegistrationSnapshot Capture() { return new RegistrationSnapshot { Exists = Registered }; }
            public void Register() { Registered = true; }
            public void Remove() { Registered = false; }
            public void Restore(RegistrationSnapshot snapshot) { Registered = snapshot.Exists; }
        }
        private static int passed;
        private static readonly StringBuilder report = new StringBuilder();
        private static void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); report.AppendLine("PASS: " + name); passed++; }
        internal static int Run(string output)
        {
            Directory.CreateDirectory(output); string fixture = Path.Combine(output, "setup-run-" + Guid.NewGuid().ToString("N"));
            try
            {
                Application app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(app.Dispatcher));
                InstallPaths paths = new InstallPaths { Test = true, Directory = Path.Combine(fixture, "install"), StartMenu = Path.Combine(fixture, "start-menu"), Desktop = Path.Combine(fixture, "desktop"), Data = Path.Combine(fixture, "data"), UserSid = WindowsIdentity.GetCurrent().User.Value };
                Directory.CreateDirectory(paths.Data); string data = Path.Combine(paths.Data, "tasks.json"); File.WriteAllText(data, "existing todo data");
                FakeRegistration registration = new FakeRegistration(); InstallService service = new InstallService(paths, registration);
                InstallPaths defaultPaths = InstallPaths.ForCurrentUser(); defaultPaths.Test = true;
                SetupUi defaultUi = new SetupUi(new InstallService(defaultPaths, new FakeRegistration()), false);
                Render(defaultUi.Window, Path.Combine(output, "setup-default.png")); defaultUi.Window.Close();
                SetupUi ui = new SetupUi(service, false); Render(ui.Window, Path.Combine(output, "setup.png"));
                Check(InstallPaths.Same(InstallPaths.ForCurrentUser().Directory, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "DeskTodo")), "默认安装到 64 位 Program Files 的 DeskTodo 文件夹");
                Check(ui.Control<StackPanel>("LocationPage").Visibility == Visibility.Visible && ui.Control<Button>("Primary").Content.ToString() == "下一步" &&
                    !ui.Control<TextBox>("InstallPath").IsReadOnly && ui.Control<TextBlock>("Description").Text == "", "第一页可编辑安装位置，删除原默认添加开始菜单的说明行");
                Check(ui.Control<CheckBox>("StartMenuShortcut").IsChecked == true && ui.Control<CheckBox>("DesktopShortcut").IsChecked != true, "快捷方式选项默认开始菜单选中、桌面未选中，均可自选");
                Check(ui.Control<TextBlock>("DataNote").Visibility == Visibility.Collapsed && ui.Control<TextBlock>("DataNote").Text == "", "安装界面不显示已有待办、主题和提醒会保留的说明行");
                using (MemoryStream iconStream = new MemoryStream(SetupProgram.Resource("Icon")))
                {
                    IconBitmapDecoder iconDecoder = new IconBitmapDecoder(iconStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    BitmapSource shown = ui.Control<Image>("SetupIcon").Source as BitmapSource;
                    Check(iconDecoder.Frames.Count == 10 && shown != null && shown.PixelWidth == 256,
                        "安装界面图标使用指定透明清单图案的最大帧，不再使用软件内列表图标");
                    byte[] pixels = new byte[shown.PixelWidth * shown.PixelHeight * 4];
                    new FormatConvertedBitmap(shown, PixelFormats.Bgra32, null, 0).CopyPixels(pixels, shown.PixelWidth * 4, 0);
                    Check(new[] { 3, (shown.PixelWidth - 1) * 4 + 3, (shown.PixelHeight - 1) * shown.PixelWidth * 4 + 3, pixels.Length - 1 }.All(index => pixels[index] == 0),
                        "安装界面图标四角透明，没有方形白底");
                }
                Check(SetupProgram.Resource("App")[0] == 'M' && SetupProgram.Resource("Config").Length > 100 && SetupProgram.Resource("Readme").Length > 100, "单文件安装包包含程序、配置和使用说明");
                string chosen = Path.Combine(fixture, "Custom DeskTodo 中文");
                ui.Control<TextBox>("InstallPath").Text = chosen;
                ui.Control<Button>("Primary").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Check(ui.Control<StackPanel>("ShortcutPage").Visibility == Visibility.Visible && ui.Control<Button>("Primary").Content.ToString() == "安装" && !Directory.Exists(chosen), "下一页选择快捷方式；尚未点击安装时不写程序文件");
                Render(ui.Window, Path.Combine(output, "setup-options.png"));
                ui.Control<Button>("Back").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Check(ui.Control<StackPanel>("LocationPage").Visibility == Visibility.Visible && ui.Control<TextBox>("InstallPath").Text == chosen, "上一步保留自选安装路径");
                ui.Control<TextBox>("InstallPath").Text = paths.Data;
                ui.Control<Button>("Primary").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Check(ui.Control<StackPanel>("ShortcutPage").Visibility != Visibility.Visible && ui.Control<TextBlock>("Status").Text.Contains("数据目录"), "数据目录无效时阻止进入安装页并在页内提示");
                ui.Control<TextBox>("InstallPath").Text = chosen;
                ui.Control<Button>("Primary").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Check(paths.Directory == chosen, "自定义含空格和中文的路径确实传给安装服务");
                ui.Control<Button>("Primary").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                DateTime installedDeadline = DateTime.UtcNow.AddSeconds(12);
                while (ui.Control<StackPanel>("FinishOptions").Visibility != Visibility.Visible && DateTime.UtcNow < installedDeadline)
                {
                    System.Windows.Threading.DispatcherFrame frame = new System.Windows.Threading.DispatcherFrame();
                    app.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(delegate { frame.Continue = false; }));
                    System.Windows.Threading.Dispatcher.PushFrame(frame); Thread.Sleep(20);
                }
                Check(ui.Control<StackPanel>("FinishOptions").Visibility == Visibility.Visible, "实际安装按钮异步执行文件和 COM 快捷方式操作后进入完成页：" + ui.Control<TextBlock>("Status").Text);
                Check(InstallService.FileNames.All(name => File.Exists(Path.Combine(paths.Directory, name))), "安装到独立程序目录，包含卸载入口");
                Check(File.ReadAllBytes(paths.App).SequenceEqual(SetupProgram.Resource("App")), "安装后的程序逐字节匹配安装包载荷");
                Check(File.ReadAllBytes(paths.ShellIcon).SequenceEqual(SetupProgram.Resource("Icon")), "安装专用的版本化透明 ICO 逐字节匹配载荷");
                Check(InstallService.IsOwnShortcut(paths.MenuLink, paths.App), "默认生成正确指向程序的开始菜单快捷方式（测试目录）");
                Check(ShortcutIcon(paths.MenuLink) == paths.ShellIcon + ",0", "开始菜单快捷方式明确引用本版透明图标，不沿用 EXE 的旧缓存键");
                CheckShellIcon(paths.MenuLink, "开始菜单");
                Check(!File.Exists(paths.DesktopLink) && registration.Registered, "未选择桌面快捷方式时不创建，安装注册接口已执行（模拟注册表）");
                Check(File.ReadAllText(data) == "existing todo data", "安装保留已有待办数据");
                ui.ShowSuccess(); Render(ui.Window, Path.Combine(output, "setup-finished.png"));
                Check(ui.Control<StackPanel>("FinishOptions").Visibility == Visibility.Visible && ui.Control<StackPanel>("ShortcutPage").Visibility == Visibility.Collapsed && ui.Control<Button>("Primary").Content.ToString() == "完成", "完成页面只保留启动选项，快捷方式已在第二页处理");
                service.SetDesktopShortcut(true); Check(InstallService.IsOwnShortcut(paths.DesktopLink, paths.App), "选中后创建桌面快捷方式（测试目录）");
                Check(ShortcutIcon(paths.DesktopLink) == paths.ShellIcon + ",0", "桌面快捷方式明确引用本版透明图标");
                CheckShellIcon(paths.DesktopLink, "桌面");
                service.SetDesktopShortcut(false); Check(!File.Exists(paths.DesktopLink), "取消选择可移除自己的桌面快捷方式");
                service.SetDesktopShortcut(true); service.AddDesktop = true; string unrelated = Path.Combine(paths.Directory, "用户保留文件.txt"); File.WriteAllText(unrelated, "keep me"); service.Install();
                Check(File.ReadAllText(data) == "existing todo data" && File.ReadAllText(unrelated) == "keep me" && InstallService.IsOwnShortcut(paths.DesktopLink, paths.App), "升级保留数据、无关文件和已有桌面快捷方式");
                byte[] previous = Encoding.UTF8.GetBytes("previous exe fixture"); File.WriteAllBytes(paths.App, previous);
                byte[] previousIcon = Encoding.UTF8.GetBytes("previous icon fixture"); File.WriteAllBytes(paths.ShellIcon, previousIcon); service.FailAfterFilesForTest = true;
                bool failed = false; try { service.Install(); } catch (IOException) { failed = true; }
                Check(failed && File.ReadAllBytes(paths.App).SequenceEqual(previous) && registration.Registered, "模拟安装失败后恢复旧程序和原注册状态");
                Check(File.ReadAllBytes(paths.ShellIcon).SequenceEqual(previousIcon), "模拟安装失败后恢复旧图标，不留下半更新资源");
                service.FailAfterFilesForTest = false; service.Install();
                SetupUi uninstall = new SetupUi(service, true); Render(uninstall.Window, Path.Combine(output, "uninstall.png"));
                Check(uninstall.Control<TextBlock>("Description").Text.Contains("确认卸载") && uninstall.Control<TextBlock>("DataNote").Text.Contains(paths.Data), "卸载先确认并明确说明保留数据位置");
                service.Uninstall();
                Check(InstallService.FileNames.All(name => !File.Exists(Path.Combine(paths.Directory, name))) && !File.Exists(paths.MenuLink) && !File.Exists(paths.DesktopLink) && !registration.Registered, "卸载仅移除已知程序文件、自己的快捷方式及注册入口");
                Check(File.ReadAllText(data) == "existing todo data" && File.ReadAllText(unrelated) == "keep me", "卸载绝不递归删除目录，待办和无关文件仍保留");
                bool unsafeBlocked = false; try { new InstallPaths { Test = true, Directory = paths.Data, Data = paths.Data }.Validate(); } catch (IOException) { unsafeBlocked = true; }
                Check(unsafeBlocked, "数据目录不能作为安装或卸载目标");
                Check(UserRegistration.CommandPath("\"C:\\Programs with spaces\\DeskTodo.exe\" --startup") == "C:\\Programs with spaces\\DeskTodo.exe", "含空格自启路径正确解析");
                for (int choice = 0; choice < 4; choice++)
                {
                    string combo = Path.Combine(fixture, "choice-" + choice);
                    InstallPaths comboPaths = new InstallPaths { Test = true, Directory = Path.Combine(combo, "app"), StartMenu = Path.Combine(combo, "menu"), Desktop = Path.Combine(combo, "desktop"), Data = paths.Data, UserSid = paths.UserSid };
                    InstallService comboService = new InstallService(comboPaths, new FakeRegistration()) { AddStartMenu = (choice & 1) != 0, AddDesktop = (choice & 2) != 0 };
                    comboService.Install();
                    Check(File.Exists(comboPaths.MenuLink) == comboService.AddStartMenu && File.Exists(comboPaths.DesktopLink) == comboService.AddDesktop,
                        "快捷方式勾选组合 " + choice + " 精确生效（实际文件和 COM，测试目录）");
                    comboService.AddStartMenu = false; comboService.AddDesktop = false; comboService.Install();
                    Check(!File.Exists(comboPaths.MenuLink) && !File.Exists(comboPaths.DesktopLink), "升级取消勾选组合 " + choice + " 时移除本程序原快捷方式");
                    comboService.Uninstall();
                }
                string workerArgs = service.WorkerArguments(false, Path.Combine(fixture, "result"));
                Check(workerArgs.Contains("--install-dir " + SetupProgram.Quote(paths.Directory)) && workerArgs.Contains("--data-dir " + SetupProgram.Quote(paths.Data)) && workerArgs.Contains("--user-sid " + paths.UserSid), "提权工作进程保留原用户标识和目录，向导和软件不随之提权（参数检查）");
                Check(service.WorkerArguments(true, Path.Combine(fixture, "result")).Contains("--uninstall"), "自定义路径卸载可向工作进程传递明确目标");
                InstallPaths custom = InstallPaths.ForCurrentUser(); custom.Directory = Path.Combine(fixture, "real-validation"); custom.Validate();
                Check(custom.Directory.EndsWith("real-validation"), "生产路径验证允许用户选择本机独立自定义目录");
                bool rootBlocked = false; custom.Directory = Path.GetPathRoot(fixture); try { custom.Validate(); } catch (IOException) { rootBlocked = true; }
                Check(rootBlocked, "生产路径验证禁止磁盘根目录");
                report.AppendLine("All " + passed + " installer checks passed. Fixture: " + fixture);
                File.WriteAllText(Path.Combine(output, "setup-test-report.txt"), report.ToString()); app.Shutdown(); return 0;
            }
            catch (Exception e) { report.AppendLine(e.ToString()); File.WriteAllText(Path.Combine(output, "setup-test-report.txt"), report.ToString()); return 1; }
        }
        private static string ShortcutIcon(string path)
        {
            object shell = null, link = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true));
                link = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                return link.GetType().InvokeMember("IconLocation", BindingFlags.GetProperty, null, link, null) as string;
            }
            finally { if (link != null) Marshal.FinalReleaseComObject(link); if (shell != null) Marshal.FinalReleaseComObject(shell); }
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ShellFileInfo
        {
            internal IntPtr Icon;
            internal int IconIndex;
            internal uint Attributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] internal string DisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] internal string TypeName;
        }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(string path, uint attributes, out ShellFileInfo info, uint size, uint flags);
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
        private static void CheckShellIcon(string path, string name)
        {
            ShellFileInfo info;
            IntPtr result = SHGetFileInfo(path, 0, out info, (uint)Marshal.SizeOf(typeof(ShellFileInfo)), 0x100);
            if (result == IntPtr.Zero || info.Icon == IntPtr.Zero) throw new IOException("Windows Shell 无法提取快捷方式图标。");
            try
            {
                using (System.Drawing.Icon icon = System.Drawing.Icon.FromHandle(info.Icon))
                using (System.Drawing.Bitmap bitmap = icon.ToBitmap())
                {
                    Check(bitmap.GetPixel(0, 0).A == 0 && bitmap.GetPixel(bitmap.Width - 1, 0).A == 0 &&
                        bitmap.GetPixel(0, bitmap.Height - 1).A == 0 && bitmap.GetPixel(bitmap.Width - 1, bitmap.Height - 1).A == 0,
                        name + "实际 Windows Shell 提取的图标四角透明，没有白色方形画布");
                    int painted = 0;
                    for (int x = 0; x < bitmap.Width; x++) if (bitmap.GetPixel(x, bitmap.Height / 2).A >= 128) painted++;
                    Check(painted >= bitmap.Width * .9, name + "实际 Windows Shell 图标图案占比至少 90%，不再偏小");
                    bitmap.Save(Path.Combine(Path.GetDirectoryName(path), "shell-icon-preview.png"), System.Drawing.Imaging.ImageFormat.Png);
                }
            }
            finally { DestroyIcon(info.Icon); }
        }
        private static void Render(Window window, string path)
        {
            window.ShowInTaskbar = false; window.Show(); window.UpdateLayout();
            FrameworkElement root = (FrameworkElement)window.Content; double width = root.ActualWidth + root.Margin.Left + root.Margin.Right;
            double height = root.ActualHeight + root.Margin.Top + root.Margin.Bottom;
            RenderTargetBitmap image = new RenderTargetBitmap((int)Math.Ceiling(width * 1.5), (int)Math.Ceiling(height * 1.5), 144, 144, PixelFormats.Pbgra32);
            DrawingVisual background = new DrawingVisual(); using (DrawingContext drawing = background.RenderOpen()) drawing.DrawRectangle(window.Background, null, new Rect(0, 0, width, height));
            image.Render(background); image.Render(root);
            PngBitmapEncoder png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image)); using (FileStream stream = File.Create(path)) png.Save(stream);
            window.Hide();
        }
    }
}
