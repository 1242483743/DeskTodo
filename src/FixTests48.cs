using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;

namespace DesktopTodo
{
    internal static partial class Tests
    {
        private static void CheckFixes48(Application app, string fixture, string directory)
        {
            Check(ServicePointManager.SecurityProtocol == SecurityProtocolType.SystemDefault,
                "实际 EXE 进程使用系统安全 TLS 协议，而非旧版 SSL3/TLS1.0 默认值");
            TodoWindow view = new TodoWindow(new AppState { Language = "zh-CN", DesktopMode = false, Locked = false });
            try
            {
                view.Window.Show(); view.InitializeNative(true, false, IntPtr.Zero); view.SettingsOpen = true;
                foreach (bool dark in new[] { false, true })
                foreach (int[] size in new[] { new[] { 320, 360 }, new[] { 440, 636 }, new[] { 640, 900 } })
                {
                    view.State.DarkMode = dark; view.ApplyTheme(); view.Window.Width = size[0]; view.Window.Height = size[1];
                    view.OpenSettingsCategory("ManagementLabel"); AdvanceFrames(230);
                    Button update = view.Control<Button>("CheckUpdatesButton"), exit = view.Control<Button>("ExitButton");
                    StackPanel panel = (StackPanel)exit.Parent;
                    FrameworkElement[] rows = panel.Children.Cast<FrameworkElement>().ToArray();
                    for (int i = 1; i < rows.Length; i++)
                    {
                        Rect before = rows[i - 1].TransformToAncestor(panel).TransformBounds(new Rect(rows[i - 1].RenderSize));
                        Rect after = rows[i].TransformToAncestor(panel).TransformBounds(new Rect(rows[i].RenderSize));
                        Check(after.Top - before.Bottom >= 7.9, "管理按钮实际布局保留间距，" + size[0] + "/" + dark + "/" + i);
                    }
                    Check(rows.Last() == exit && panel.Children.IndexOf(update) < panel.Children.IndexOf(exit), "退出软件放在管理区最后，检查更新位于其上方");
                    ScrollViewer scroll = VisualDescendants<ScrollViewer>(view.Window).First(s => s.IsVisible && IsVisualChild(exit, s));
                    scroll.ScrollToEnd(); Pump();
                    Rect bounds = exit.TransformToAncestor(scroll).TransformBounds(new Rect(exit.RenderSize));
                    Check(bounds.Bottom <= scroll.ActualHeight + 1 && bounds.Top >= -1, "最小管理窗口也可完整滚动到退出按钮");
                    Render(view, Path.Combine(directory, "v148-management-" + size[0] + "-" + dark + ".png"), dark ? 168 : 120);
                }
                view.Window.Width = 440; view.Window.Height = 636; view.State.DarkMode = false; view.ApplyTheme();
                int calls = 0; string logged = null;
                view.Desktop.Log = delegate(string message) { logged = message; };
                view.FetchRelease = delegate { Interlocked.Increment(ref calls); throw new WebException("fixture", WebExceptionStatus.Timeout); };
                Button check = view.Control<Button>("CheckUpdatesButton");
                check.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); check.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(PumpUntil(delegate { return check.IsEnabled && view.Control<TextBlock>("ConfirmTitle").Text == "检查更新失败"; }, 5000), "真实更新按钮的超时路径可恢复按钮状态");
                Check(calls == 1 && logged.Contains("Timeout") && view.Control<TextBlock>("ConfirmBody").Text.Contains("超时") &&
                    (string)view.Control<Button>("ConfirmAcceptButton").Content == "重试", "更新检查防重复、区分超时、记录非敏感诊断并提供重试");
                AdvanceFrames(230); Render(view, Path.Combine(directory, "v148-update-failure.png"));
                view.FetchRelease = delegate { Interlocked.Increment(ref calls); return new ReleaseInfo { Tag = "v99.0" }; };
                view.Control<Button>("ConfirmAcceptButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(PumpUntil(delegate { return check.IsEnabled && view.Control<TextBlock>("ConfirmTitle").Text.Contains("v99.0"); }, 5000), "从错误弹框点击重试可发现新版，未自动打开网页或下载");
                view.CloseOverlay();
                view.FetchRelease = delegate { return new ReleaseInfo { Tag = "v1.46" }; };
                check.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(PumpUntil(delegate { return check.IsEnabled && view.ToastText.Contains("无需更新"); }, 5000), "本地版本较新时用非阻塞短提示说明无需更新，不提示降级");
                view.CloseOverlay();
                Check(ReleaseInfo.FailureMessage(new WebException("x", WebExceptionStatus.TrustFailure), false).Contains("证书") &&
                    ReleaseInfo.FailureMessage(new WebException("x", WebExceptionStatus.NameResolutionFailure), false).Contains("DNS") &&
                    ReleaseInfo.FailureMessage(new InvalidDataException(), true).Contains("invalid"), "证书、DNS 和无效版本分别显示原因，不绕过证书验证");
                view.SettingsOpen = false; AdvanceFrames(240);
                Native.Rect origin; Native.PhysicalWindowRect(view.Desktop.Handle, out origin);
                Native.Point start = new Native.Point { X = origin.Left + 60, Y = origin.Top + 40 };
                System.Diagnostics.Stopwatch capture = System.Diagnostics.Stopwatch.StartNew();
                view.BeginWindowMove(start); view.ContinueWindowMove(new Native.Point { X = start.X + 9, Y = start.Y + 9 });
                report.AppendLine("MEASURE: 初次固定画面捕获及交接 " + capture.Elapsed.TotalMilliseconds.ToString("F2") + " ms（非屏幕 FPS）");
                IntPtr preview = view.MovePreviewHandle; int previousFrames = view.WindowMoveFrameUpdates;
                int previousPositions = view.MovePreviewPositionUpdates;
                for (int i = 0; i < 500; i++) view.QueueWindowMove(new Native.Point { X = start.X + 10 + i % 25, Y = start.Y + 10 });
                Check(view.WindowMoveFrameUpdates == previousFrames, "高频鼠标输入不逐条移动原生窗口");
                AdvanceFrames(80);
                Check(view.WindowMoveFrameUpdates == previousFrames + 1 && view.MovePreviewPositionUpdates == previousPositions + 1,
                    "500 次输入合并为一帧，只采用最后指针位置");
                bool stable = true;
                for (int frame = 0; frame < 35; frame++)
                {
                    view.QueueWindowMove(new Native.Point { X = start.X + 25 + frame % 20, Y = start.Y + 15 + frame % 10 }); AdvanceFrames(20);
                    Native.Rect live; Native.PhysicalWindowRect(view.Desktop.Handle, out live);
                    stable &= view.MovePreviewHandle == preview && Native.IsWindowVisible(preview) && SameRect(live, origin) &&
                        GestureWindowClip.PixelsSuppressed(view.Desktop.Handle) && view.MovePreviewSurfaceBuilds == 1;
                }
                Check(stable, "连续逐帧拖动复用同一可见表面，原窗口不移动、不反复截图、不恢复旧画面");
                int positions = view.MovePreviewPositionUpdates;
                for (int i = 0; i < 50; i++) view.ContinueWindowMove(new Native.Point { X = start.X + 39, Y = start.Y + 19 });
                Check(view.MovePreviewPositionUpdates <= positions + 1, "相同指针位置不重复 SetWindowPos");
                view.QueueWindowMove(new Native.Point { X = start.X + 180, Y = start.Y + 180 });
                view.FinishWindowMoveForTest(false); AdvanceFrames(120);
                Native.Rect restored; Native.PhysicalWindowRect(view.Desktop.Handle, out restored);
                Check(!Native.IsWindow(preview) && SameRect(origin, restored) && !view.GeometryGestureActive && !view.State.PositionSet &&
                    !GestureWindowClip.PixelsSuppressed(view.Desktop.Handle), "取消时丢弃待处理帧，恢复完整实时窗口，不保存草稿");
                System.Drawing.Rectangle work = Native.MonitorFor(origin).Work;
                int dx = origin.Left > work.Left + 40 ? -30 : 30, dy = origin.Top > work.Top + 40 ? -30 : 30;
                view.BeginWindowMove(start); view.QueueWindowMove(new Native.Point { X = start.X + dx / 2, Y = start.Y + dy / 2 });
                Native.Point release = new Native.Point { X = start.X + dx, Y = start.Y + dy };
                double originScale = Native.MonitorScale(Native.MonitorFor(origin).Handle);
                Native.MonitorArea releaseMonitor = Native.MonitorAt(release);
                Native.Rect expected = TodoWindow.DpiMoveGeometry(origin, start, release, originScale,
                    Native.MonitorScale(releaseMonitor.Handle), (origin.Right - origin.Left) / originScale,
                    (origin.Bottom - origin.Top) / originScale, releaseMonitor.Work, true);
                view.FinishWindowMoveForTest(true, release); AdvanceFrames(100);
                Native.PhysicalWindowRect(view.Desktop.Handle, out restored);
                report.AppendLine("MEASURE: 快速松手 origin=" + origin.Left + "," + origin.Top + "," + (origin.Right-origin.Left) + "," + (origin.Bottom-origin.Top) +
                    " delta=" + dx + "," + dy + " result=" + restored.Left + "," + restored.Top + "," + (restored.Right-restored.Left) + "," + (restored.Bottom-restored.Top) +
                    " saved=" + view.State.PositionSet + " active=" + view.MovePreviewActive + " ex=" + Native.GetWindowLongPtr(view.Desktop.Handle, Native.GWL_EXSTYLE));
                Check(view.State.PositionSet && SameRect(restored, expected) &&
                    !view.MovePreviewActive && (Native.GetWindowLongPtr(view.Desktop.Handle, Native.GWL_EXSTYLE).ToInt64() & 8) == 0,
                    "下一帧前快速松手仍提交最后指针，最终原窗口不遗留置顶状态");
            }
            finally { view.Window.Close(); }
            CheckMaintenanceGesture48(app, fixture);
            TodoWindow closing = new TodoWindow(new AppState { Language = "zh-CN", DesktopMode = false });
            using (ManualResetEvent entered = new ManualResetEvent(false))
            using (ManualResetEvent complete = new ManualResetEvent(false))
            using (ManualResetEvent release = new ManualResetEvent(false))
            {
                closing.FetchRelease = delegate { entered.Set(); release.WaitOne(); complete.Set(); return new ReleaseInfo { Tag = "v99.0" }; };
                closing.Window.Show(); closing.Control<Button>("CheckUpdatesButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(entered.WaitOne(3000), "更新检查在后台线程执行"); closing.Window.Close(); release.Set();
                Check(complete.WaitOne(3000), "更新过程中关闭窗口不阻塞后台结束"); AdvanceFrames(150);
                Check(!closing.Window.IsVisible, "关闭后更新结果不重新 Show 已关闭的 WPF 窗口");
            }
        }
        private static bool IsVisualChild(DependencyObject child, DependencyObject parent)
        { for (DependencyObject node = child; node != null; node = VisualTreeHelper.GetParent(node)) if (node == parent) return true; return false; }
        private static void CheckMaintenanceGesture48(Application app, string fixture)
        {
            Window host = new Window { Width = 1000, Height = 900, ShowInTaskbar = false };
            Window replacement = new Window { Width = 1000, Height = 900, ShowInTaskbar = false };
            Controller controller = null; IntPtr previousSurface = Native.TestSurface;
            try
            {
                host.Show(); replacement.Show(); Native.TestSurface = new WindowInteropHelper(host).Handle;
                controller = new Controller(app, new Storage(Path.Combine(fixture, "v148-maintenance")),
                    new AppState { Language = "zh-CN", DesktopMode = true, Locked = false, HideTray = true }, false);
                controller.Start(); TodoWindow view = controller.CurrentUi; AdvanceFrames(180);
                Native.Rect rect; Native.PhysicalWindowRect(view.Desktop.Handle, out rect);
                Native.Point start = new Native.Point { X = rect.Left + 60, Y = rect.Top + 45 };
                view.BeginWindowMove(start); view.ContinueWindowMove(new Native.Point { X = start.X + 15, Y = start.Y + 10 });
                IntPtr preview = view.MovePreviewHandle; Native.TestSurface = new WindowInteropHelper(replacement).Handle;
                for (int i = 0; i < 4; i++) { controller.Tick(); AdvanceFrames(20); }
                Check(controller.CurrentUi == view && view.MovePreviewHandle == preview && Native.IsWindowVisible(preview),
                    "桌面宿主变化时维护轮询不会在拖动中重建或抢回窗口");
                view.FinishWindowMoveForTest(false); Pump(); controller.Tick(); Pump();
                Check(controller.CurrentUi != view && controller.CurrentUi.Desktop.Parent == Native.TestSurface,
                    "拖动结束后补做桌面宿主恢复，不永久跳过维护");
            }
            finally { if (controller != null) controller.Dispose(); Native.TestSurface = previousSurface; replacement.Close(); host.Close(); }
        }
        internal static int CheckLiveUpdate48(string directory)
        {
            Directory.CreateDirectory(directory);
            new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            TodoWindow view = new TodoWindow(new AppState { Language = "zh-CN", DesktopMode = false });
            try
            {
                view.Window.Show(); view.SettingsOpen = true; view.OpenSettingsCategory("ManagementLabel"); AdvanceFrames(230);
                Button button = view.Control<Button>("CheckUpdatesButton");
                button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                bool ended = PumpUntil(delegate { return button.IsEnabled; }, 40000);
                string title = view.Control<TextBlock>("ConfirmTitle").Text;
                Check(ended && (view.ToastText.Contains("无需更新") || title.StartsWith("发现新版本")), "真实 EXE 内点击检查更新，经 HTTPS 获取 GitHub 正式版本：" + view.ToastText + title);
                AdvanceFrames(230); Render(view, Path.Combine(directory, "v148-update-live.png"));
                report.AppendLine("TLS=" + ServicePointManager.SecurityProtocol);
                File.WriteAllText(Path.Combine(directory, "live-update-report.txt"), report.ToString(), Encoding.UTF8); return 0;
            }
            catch (Exception error)
            { File.WriteAllText(Path.Combine(directory, "live-update-report.txt"), report + error.ToString(), Encoding.UTF8); return 1; }
            finally { view.Window.Close(); }
        }
    }
}
