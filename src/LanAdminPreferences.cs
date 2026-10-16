using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;

namespace DesktopTodo
{
    // Administrator-side preferences are deliberately separate from tasks.json and
    // exported backups. Access codes are encrypted for the current Windows user.
    [DataContract]
    internal sealed class LanAdminPreferences
    {
        [DataMember] public Dictionary<string, string> Aliases = new Dictionary<string, string>();
        [DataMember] public Dictionary<string, string> Codes = new Dictionary<string, string>();
        [DataMember] public List<string> HiddenIds = new List<string>();
        [DataMember] public bool DefaultReminderManaged;
        [DataMember] public double WindowWidth = 760;
        [DataMember] public double WindowHeight = 820;
        private string path;

        internal static LanAdminPreferences Load(string directory)
        {
            string file = String.IsNullOrWhiteSpace(directory) ? null : Path.Combine(directory, "lan-admin-preferences.dat");
            foreach (string candidate in new[] { file, file == null ? null : file + ".bak" })
            {
                if (candidate == null || !File.Exists(candidate)) continue;
                try
                {
                    byte[] plain = ProtectedData.Unprotect(File.ReadAllBytes(candidate), null, DataProtectionScope.CurrentUser);
                    LanAdminPreferences saved = LanJson.Read<LanAdminPreferences>(Encoding.UTF8.GetString(plain));
                    if (saved == null) continue;
                    saved.Aliases = saved.Aliases ?? new Dictionary<string, string>();
                    saved.Codes = saved.Codes ?? new Dictionary<string, string>();
                    saved.HiddenIds = saved.HiddenIds ?? new List<string>();
                    if (Double.IsNaN(saved.WindowWidth) || Double.IsInfinity(saved.WindowWidth) || saved.WindowWidth < 680) saved.WindowWidth = 760;
                    if (Double.IsNaN(saved.WindowHeight) || Double.IsInfinity(saved.WindowHeight) || saved.WindowHeight < 460) saved.WindowHeight = 820;
                    saved.path = file; return saved;
                }
                catch (Exception) { /* A corrupt or foreign-user file must not block task access. */ }
            }
            return new LanAdminPreferences { path = file };
        }

        internal string CodeFor(string instanceId)
        {
            string code;
            return !String.IsNullOrWhiteSpace(instanceId) && Codes.TryGetValue(instanceId, out code) ? code : "";
        }

        internal string NameFor(LanPeer peer)
        {
            string alias;
            return peer != null && Aliases.TryGetValue(peer.InstanceId ?? "", out alias) && !String.IsNullOrWhiteSpace(alias)
                ? alias : peer == null ? "" : peer.DisplayName;
        }

        internal bool IsHidden(string instanceId)
        { return !String.IsNullOrWhiteSpace(instanceId) && HiddenIds.Contains(instanceId); }

        internal void Save()
        {
            if (path == null) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            byte[] encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(LanJson.Write(this)), null, DataProtectionScope.CurrentUser);
            string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(encrypted, 0, encrypted.Length); stream.Flush(true); }
                if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
