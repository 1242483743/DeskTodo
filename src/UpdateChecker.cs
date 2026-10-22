using System;
using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace DesktopTodo
{
    [DataContract]
    internal sealed class ReleaseInfo
    {
        [DataMember(Name = "tag_name")] internal string Tag = null;
        [DataMember(Name = "draft")] internal bool Draft = false;
        [DataMember(Name = "prerelease")] internal bool Prerelease = false;
        internal Version Version
        {
            get
            {
                Version version;
                return !Draft && !Prerelease && Regex.IsMatch(Tag ?? "", @"^v\d+\.\d+(\.\d+)?$") &&
                    System.Version.TryParse(Tag.Substring(1), out version)
                    ? new Version(version.Major, version.Minor, Math.Max(0, version.Build), 0) : null;
            }
        }
        internal string Page { get { return Version == null ? null : "https://github.com/1242483743/DeskTodo/releases/tag/" + Tag; } }
        internal static ReleaseInfo Read(Stream stream)
        {
            ReleaseInfo release = (ReleaseInfo)new DataContractJsonSerializer(typeof(ReleaseInfo)).ReadObject(stream);
            if (release == null || release.Version == null) throw new InvalidDataException("更新信息不是有效的正式版本。");
            return release;
        }
        internal static ReleaseInfo Fetch()
        {
            try { return FetchApi(); }
            catch (WebException e)
            {
                HttpWebResponse response = e.Response as HttpWebResponse;
                bool limited = response != null && ((int)response.StatusCode == 429 ||
                    (response.StatusCode == HttpStatusCode.Forbidden && response.Headers["X-RateLimit-Remaining"] == "0"));
                if (response != null) response.Close();
                if (!limited) throw;
                // GitHub's anonymous API quota is shared by all users behind a
                // public IP. Its public latest-release redirect needs no token.
                HttpWebRequest latest = (HttpWebRequest)WebRequest.Create("https://github.com/1242483743/DeskTodo/releases/latest");
                latest.Method = "HEAD"; latest.UserAgent = "DeskTodo-UpdateCheck";
                latest.Timeout = 12000; latest.ReadWriteTimeout = 12000;
                using (WebResponse page = latest.GetResponse()) return FromLatestUri(page.ResponseUri);
            }
        }
        internal static ReleaseInfo FromLatestUri(Uri uri)
        {
            const string prefix = "/1242483743/DeskTodo/releases/tag/";
            if (uri == null || !uri.IsAbsoluteUri || uri.Scheme != "https" || uri.Host != "github.com" || uri.Port != 443 ||
                uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
                !uri.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal)) throw new InvalidDataException("发布页地址无效。");
            ReleaseInfo release = new ReleaseInfo { Tag = uri.AbsolutePath.Substring(prefix.Length) };
            if (release.Version == null) throw new InvalidDataException("发布页版本无效。");
            return release;
        }
        private static ReleaseInfo FetchApi()
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create("https://api.github.com/repos/1242483743/DeskTodo/releases/latest");
            request.UserAgent = "DeskTodo-UpdateCheck"; request.Accept = "application/vnd.github+json";
            request.Headers["X-GitHub-Api-Version"] = "2022-11-28";
            request.Timeout = 12000; request.ReadWriteTimeout = 12000;
            using (WebResponse response = request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (MemoryStream body = new MemoryStream())
            {
                byte[] buffer = new byte[4096]; int count;
                while ((count = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (body.Length + count > 2 * 1024 * 1024) throw new InvalidDataException("更新信息过大。");
                    body.Write(buffer, 0, count);
                }
                body.Position = 0; return Read(body);
            }
        }
        internal static string FailureCode(Exception error)
        {
            WebException web = error as WebException;
            HttpWebResponse response = web == null ? null : web.Response as HttpWebResponse;
            return error.GetType().Name + (web == null ? "" : "/" + web.Status) +
                (response == null ? "" : "/HTTP " + (int)response.StatusCode);
        }
        internal static string FailureMessage(Exception error, bool english)
        {
            WebException web = error as WebException;
            string detail;
            if (web == null) detail = english ? "GitHub returned invalid release information." : "GitHub 返回的版本信息无效。";
            else if (web.Status == WebExceptionStatus.TrustFailure)
                detail = english ? "The HTTPS certificate could not be verified. Check the system clock and proxy certificate." : "HTTPS 证书验证失败，请检查系统时间和代理证书。";
            else if (web.Status == WebExceptionStatus.SecureChannelFailure)
                detail = english ? "A secure HTTPS connection could not be established. Check system updates and proxy settings." : "无法建立安全的 HTTPS 连接，请检查系统更新和代理设置。";
            else if (web.Status == WebExceptionStatus.Timeout)
                detail = english ? "The connection to GitHub timed out." : "连接 GitHub 超时。";
            else if (web.Status == WebExceptionStatus.NameResolutionFailure || web.Status == WebExceptionStatus.ProxyNameResolutionFailure)
                detail = english ? "The GitHub or proxy address could not be resolved. Check DNS and proxy settings." : "无法解析 GitHub 或代理地址，请检查 DNS 和代理设置。";
            else if (web.Status == WebExceptionStatus.ProtocolError)
                detail = english ? "GitHub could not provide the release (" + FailureCode(error) + ")." : "GitHub 暂时无法提供版本信息（" + FailureCode(error) + "）。";
            else detail = english ? "Cannot connect to GitHub. Check network and proxy settings." : "无法连接 GitHub，请检查网络和代理设置。";
            return detail + (english ? "\nYour local tasks are unaffected. Retry, or visit github.com/1242483743/DeskTodo/releases." :
                "\n本地待办不受影响。可重试，或手动访问 github.com/1242483743/DeskTodo/releases。");
        }
    }
    public sealed partial class TodoWindow
    {
        private bool updateChecking, featureWindowClosed;
        internal Func<ReleaseInfo> FetchRelease = ReleaseInfo.Fetch;
        private void CheckForUpdates()
        {
            if (updateChecking) return;
            updateChecking = true; updateButton.IsEnabled = false;
            SettingsMessage(L("正在检查更新…", "Checking for updates…"));
            Task.Run(delegate
            {
                ReleaseInfo release = null; Exception failure = null;
                try
                {
                    release = FetchRelease();
                    if (release == null || release.Version == null) throw new InvalidDataException("Invalid release");
                }
                catch (Exception error) { failure = error; }
                if (Window.Dispatcher.HasShutdownStarted) return;
                Window.Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (featureWindowClosed) return;
                    updateChecking = false; updateButton.IsEnabled = true;
                    SettingsMessage(failure == null ? L("更新检查完成", "Update check complete") : L("更新检查失败，可重试", "Update check failed; retry available"));
                    if (failure != null)
                    {
                        if (Desktop.Log != null) Desktop.Log("Update check: " + ReleaseInfo.FailureCode(failure));
                        ShowConfirmation(L("检查更新失败", "Update check failed"),
                            ReleaseInfo.FailureMessage(failure, State.Language == "en-US"), L("重试", "Retry"), CheckForUpdates);
                        return;
                    }
                    Version current = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                    if (release.Version <= current)
                    { ShowToast(L("当前版本 v" + current.Major + "." + current.Minor + " 无需更新。", "No newer stable version is available.")); return; }
                    ShowConfirmation(L("发现新版本 ", "Update available: ") + release.Tag,
                        L("打开官方 GitHub 发布页查看更新内容和安装包。不会自动下载、执行程序或关闭软件。", "Open the official GitHub release to review changes and downloads. Nothing is downloaded or installed automatically."),
                        L("前往发布页", "Open release"), delegate
                        {
                            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(release.Page) { UseShellExecute = true }); }
                            catch (Exception) { ShowNotice("无法打开浏览器", "请访问 github.com/1242483743/DeskTodo/releases 查看更新。"); }
                        });
                }));
            });
        }
    }
}
