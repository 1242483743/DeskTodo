using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using Microsoft.Win32;

namespace DesktopTodo
{
    public sealed class Storage
    {
        public readonly string DirectoryPath;
        public string DataPath { get { return Path.Combine(DirectoryPath, "tasks.json"); } }
        public string Warning;
        private bool protectBackup;
        public Storage(string directory) { DirectoryPath = Path.GetFullPath(directory); }
        public AppState Load()
        {
            if (!File.Exists(DataPath)) return new AppState();
            try { return Read(DataPath); }
            catch (Exception original)
            {
                // Never replace newer-format data with an empty or older backup.
                if (original is InvalidOperationException && original.Message.Contains("较新版本")) throw;
                string backup = DataPath + ".bak";
                if (File.Exists(backup))
                {
                    try
                    {
                        AppState recovered = Read(backup);
                        File.Copy(DataPath, DataPath + ".damaged-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"));
                        protectBackup = true;
                        Warning = "已从备份恢复事项，损坏的原文件已保留。";
                        return recovered;
                    }
                    catch (Exception) { }
                }
                throw new IOException("待办数据无法读取。为保护原数据，软件不会覆盖它。\n" + DataPath, original);
            }
        }
        public static AppState Read(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            {
                AppState state = (AppState)new DataContractJsonSerializer(typeof(AppState)).ReadObject(stream);
                if (state == null) throw new IOException("数据为空。");
                state.Validate();
                return state;
            }
        }
        public void Save(AppState state)
        {
            state.Validate();
            Directory.CreateDirectory(DirectoryPath);
            string temporary = DataPath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    new DataContractJsonSerializer(typeof(AppState)).WriteObject(stream, state);
                    stream.Flush(true);
                }
                if (File.Exists(DataPath))
                    File.Replace(temporary, DataPath, protectBackup ? null : DataPath + ".bak");
                else File.Move(temporary, DataPath);
                protectBackup = false;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        public void Export(AppState state, string destination)
        {
            using (FileStream stream = new FileStream(destination, FileMode.Create, FileAccess.Write))
                new DataContractJsonSerializer(typeof(AppState)).WriteObject(stream, state);
        }
        public void Log(string message)
        {
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                string path = Path.Combine(DirectoryPath, "desktop.log");
                if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024)
                    File.Move(path, Path.Combine(DirectoryPath, "desktop-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + ".log"));
                File.AppendAllText(path, DateTime.Now.ToString("s") + " " + message + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }
    }

    public static class Startup
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "DesktopTodo";
        public static string ExpectedCommand { get { return "\"" + System.Reflection.Assembly.GetExecutingAssembly().Location + "\" --startup"; } }
        public static string RegisteredCommand
        {
            get { using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey)) return key == null ? null : key.GetValue(ValueName) as string; }
        }
        public static bool IsEnabled { get { return String.Equals(RegisteredCommand, ExpectedCommand, StringComparison.OrdinalIgnoreCase); } }
        public static bool MigrateLegacyCommand()
        {
            if (!String.Equals(Path.GetFileName(System.Reflection.Assembly.GetExecutingAssembly().Location), "DeskTodo.exe", StringComparison.OrdinalIgnoreCase)) return false;
            string registered = RegisteredCommand;
            if (String.IsNullOrWhiteSpace(registered) || IsEnabled) return false;
            if (!NeedsCommandMigration(registered, File.Exists)) return false;
            SetEnabled(true);
            return true;
        }
        internal static bool NeedsCommandMigration(string registered, Func<string, bool> exists)
        {
            if (String.IsNullOrWhiteSpace(registered)) return false;
            string command = registered.Trim(), path;
            if (command[0] == '"')
            {
                int end = command.IndexOf('"', 1); if (end < 0) return false;
                path = command.Substring(1, end - 1);
            }
            else
            {
                int end = command.IndexOf(" --", StringComparison.Ordinal);
                path = end < 0 ? command : command.Substring(0, end);
            }
            string name = Path.GetFileName(path);
            return String.Equals(name, "桌面待办.exe", StringComparison.OrdinalIgnoreCase) ||
                (String.Equals(name, "DeskTodo.exe", StringComparison.OrdinalIgnoreCase) && !exists(path));
        }
        public static void SetEnabled(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (key == null) throw new IOException("无法访问当前用户的自启设置。");
                if (enabled) key.SetValue(ValueName, ExpectedCommand, RegistryValueKind.String);
                else key.DeleteValue(ValueName, false);
            }
        }
    }
}
