using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;

namespace DesktopTodo
{
    internal static partial class Tests
    {
        private static void Click49(TodoWindow view, string name)
        { view.Control<Button>(name).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); }
        private static void CheckImprovements49(Application app, string fixture, string directory)
        {
            CheckHistoryPaging49(directory);
            Window host = new Window { Width = 1000, Height = 900, ShowInTaskbar = false };
            IntPtr oldSurface = Native.TestSurface; Controller controller = null;
            var entered = new ManualResetEvent(false); var release = new ManualResetEvent(false); var complete = new ManualResetEvent(false);
            try
            {
                host.Show(); Native.TestSurface = new WindowInteropHelper(host).Handle;
                AppState state = new AppState { Language = "zh-CN", DesktopMode = false, HideTray = true };
                TodoGroup group = new TodoGroup { Name = "快照测试" }; state.Groups.Add(group); state.CollapsedGroupIds.Add(group.Id);
                TodoItem item = state.Add("原始内容", group.Id), done = state.Add("已完成记录"); state.Toggle(done);
                AppState copy = state.CopyForSave(12345);
                item.Text = "界面修改"; group.Name = "新的组名"; state.CollapsedGroupIds.Clear();
                Check(copy.Items[0].Text == "原始内容" && copy.Groups[0].Name == "快照测试" && copy.CollapsedGroupIds.Count == 1 &&
                    copy.LastSavedAt == 12345 && state.LastSavedAt == 0 && !Object.ReferenceEquals(copy.Items[0], item),
                    "后台快照深复制事项、分组和折叠列表，后续界面修改不能污染快照");
                Storage store = new Storage(Path.Combine(fixture, "v149-save")); store.Save(state);
                byte[] original = File.ReadAllBytes(store.DataPath);
                controller = new Controller(app, store, state, false); controller.Start(); TodoWindow view = controller.CurrentUi; Pump();
                int worker = 0, main = Thread.CurrentThread.ManagedThreadId;
                controller.BackgroundSnapshotObserverForTest = delegate(AppState draft)
                {
                    worker = Thread.CurrentThread.ManagedThreadId; entered.Set();
                    if (!release.WaitOne(5000)) throw new TimeoutException("QA serializer wait");
                    complete.Set();
                };
                view.Input.Text = "点击添加立即反馈"; Click49(view, "AddButton");
                Check(state.Pending.Any(t => t.Text == "点击添加立即反馈") && view.IsSavePending && original.SequenceEqual(File.ReadAllBytes(store.DataPath)),
                    "点击添加立即更新界面与保存中状态，不在点击处理器中写盘");
                controller.BeginSettingsSave();
                Check(entered.WaitOne(2000) && worker != main, "普通待办的 JSON 序列化路径实际进入后台线程");
                bool heartbeat = false;
                app.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(delegate { heartbeat = true; })); Pump();
                item.Text = "后台忙碌时的新内容"; view.NotifyChanged();
                state.Toggle(done); view.NotifyChanged(); state.Toggle(done); view.NotifyChanged();
                Check(heartbeat && !complete.WaitOne(0) && view.IsSavePending,
                    "后台序列化阻塞时界面仍处理输入，连续编辑和完成不会等待旧写入");
                release.Set(); Check(complete.WaitOne(2000), "后台序列化可正常继续");
                controller.BackgroundSnapshotObserverForTest = null;
                Check(PumpUntil(delegate { return view.IsSaved && !view.IsSavePending; }, 5000) &&
                    Storage.Read(store.DataPath).Items.Single(t => t.Id == item.Id).Text == item.Text && Storage.Read(store.DataPath).Completed.Single().Id == done.Id,
                    "过期工作不能覆盖新数据，最终保存最新编辑和完成状态");
                entered.Reset(); release.Reset(); complete.Reset();
                controller.BackgroundSnapshotObserverForTest = delegate(AppState draft)
                {
                    if (draft.Items.First(t => t.Id == item.Id).Text != "故意延迟的旧快照") return;
                    entered.Set(); if (!release.WaitOne(5000)) throw new TimeoutException("QA old snapshot wait"); complete.Set();
                };
                item.Text = "故意延迟的旧快照"; view.NotifyChanged(); controller.BeginSettingsSave();
                Check(entered.WaitOne(2000), "乱序回归：旧快照在序列化前被独立阻塞");
                item.Text = "先落盘的新快照"; view.NotifyChanged(); controller.BeginSettingsSave();
                Check(PumpUntil(delegate { return view.IsSaved && !view.IsSavePending; }, 4000), "新快照不等待旧序列化任务，能够先完成保存");
                release.Set(); Check(complete.WaitOne(2000), "较旧的序列化任务随后继续"); AdvanceFrames(150);
                Check(Storage.Read(store.DataPath).Items.First(t => t.Id == item.Id).Text == "先落盘的新快照" && view.IsSaved,
                    "故意乱序完成的旧快照不能覆盖已落盘的新修订，也不能破坏保存状态");
                controller.BackgroundSnapshotObserverForTest = null;
                int writes = 0;
                controller.SettingsSnapshotWriterForTest = delegate(byte[] bytes) { store.SaveSnapshot(bytes); Interlocked.Increment(ref writes); };
                var burstWatch = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < 70; i++) { item.Text = "持续修改 " + i; view.NotifyChanged(); AdvanceFrames(20); }
                report.AppendLine("MEASURE: 连续修改耗时 " + burstWatch.ElapsedMilliseconds + "ms，写入 " + writes + "，状态 " + view.IsSavePending + "/" + view.SaveError);
                Check(writes > 0 && writes <= Math.Ceiling(burstWatch.ElapsedMilliseconds / 100.0) + 2,
                    "持续操作超过一秒仍有中途落盘，写入次数受实际时间与合并间隔约束");
                Check(PumpUntil(delegate { return view.IsSaved && !view.IsSavePending; }, 4000) && Storage.Read(store.DataPath).Items.First(t => t.Id == item.Id).Text == "持续修改 69",
                    "连续操作结束后保存最后一次输入");
                report.AppendLine("MEASURE: 70 次连续普通修改合并为 " + writes + " 次后台写入（非 FPS）");
                controller.SettingsSnapshotWriterForTest = delegate(byte[] bytes) { throw new IOException("QA simulated failure"); };
                item.Text = "失败后仍保留"; view.NotifyChanged(); controller.BeginSettingsSave();
                Check(PumpUntil(delegate { return !view.IsSavePending; }, 3000) && !view.IsSaved && view.SaveError != null &&
                    Storage.Read(store.DataPath).Items.First(t => t.Id == item.Id).Text == "持续修改 69", "普通待办写盘失败保留旧文件，显示错误而非虚假成功");
                controller.SettingsSnapshotWriterForTest = null;
                typeof(Controller).GetField("lastSaveAttempt", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, DateTime.UtcNow.AddSeconds(-11));
                controller.Tick();
                Check(view.IsSavePending && PumpUntil(delegate { return view.IsSaved && !view.IsSavePending; }, 3000), "保存失败的维护重试也走后台路径并恢复成功状态");
                item.Text = "隐藏前最后编辑"; view.NotifyChanged(); controller.Hide();
                Check(view.IsSaved && !view.IsSavePending && Storage.Read(store.DataPath).Items.First(t => t.Id == item.Id).Text == item.Text,
                    "隐藏前强制刷新普通修改，未落盘的合并请求不会丢失");
                controller.Show(); view = controller.CurrentUi;
                state.LanSharingEnabled = true;
                MethodInfo request = typeof(Controller).GetMethod("HandleLanRequest", BindingFlags.Instance | BindingFlags.NonPublic);
                LanResponse response = (LanResponse)request.Invoke(controller, new object[] { new LanRequest { Operation = "add", AccessCode = state.LanAccessCode, Text = "远程确认前已落盘" } });
                Check(response.Success && !view.IsSavePending && Storage.Read(store.DataPath).Pending.Any(t => t.Text == "远程确认前已落盘"),
                    "远程修改仍在确实落盘后才报告成功，不因本机异步化变成虚假确认");
                state.LanSharingEnabled = false;
                view.SettingsOpen = true; view.OpenSettingsCategory("ManagementLabel"); AdvanceFrames(230);
                var focus = System.Windows.Input.Keyboard.FocusedElement;
                view.ShowToast("普通操作完成"); AdvanceFrames(180);
                Check(!view.OverlayOpen && !view.Control<Border>("ToastSurface").IsHitTestVisible && Object.ReferenceEquals(focus, System.Windows.Input.Keyboard.FocusedElement),
                    "成功短提示不打开遮罩、不抢焦点、不拦截鼠标操作");
                Border toast = view.Control<Border>("ToastSurface");
                Check(Object.ReferenceEquals(toast.Parent, view.SettingsCategoryBackdrop.Parent) && Panel.GetZIndex(toast) > Panel.GetZIndex(view.SettingsCategoryBackdrop),
                    "短提示位于分类遮罩上方的同级容器，不会被背景压暗");
                Render(view, Path.Combine(directory, "v149-toast.png"));
                view.Window.Width = 320; view.Window.Height = 360; view.State.DarkMode = true; view.ApplyTheme();
                view.ShowToast("Backup exported successfully · 操作成功，可继续编辑"); AdvanceFrames(230);
                FrameworkElement toastParent = (FrameworkElement)toast.Parent;
                Rect toastBounds = toast.TransformToAncestor(toastParent).TransformBounds(new Rect(toast.RenderSize));
                Check(toastBounds.Left >= -1 && toastBounds.Top >= -1 && toastBounds.Right <= toastParent.ActualWidth + 1 &&
                    toastBounds.Bottom <= toastParent.ActualHeight + 1, "最小窗口深色主题的中英文短提示完整位于卡片内");
                Render(view, Path.Combine(directory, "v149-toast-min-dark.png"));
                view.Window.Width = 440; view.Window.Height = 636; view.State.DarkMode = false; view.ApplyTheme();
                view.ShowToast("最新结果"); Check(view.ToastText == "最新结果", "连续提示替换同一条，不叠加多个提示框");
                view.ExpireToastForTest(); Check(view.ToastText.Length == 0, "短提示到期后收起且不需要点击确认");
                view.ShowToast("会收起"); view.ShowConfirmation("重要操作", "仍需确认", "确认", delegate { });
                Check(view.ToastText.Length == 0 && view.OverlayOpen, "重要确认仍为显式确认，短提示不盖住确认框"); view.CloseOverlay();
                CheckPurge49(controller, state, store, directory);
                CheckImport49(controller, state, store, fixture);
                controller.Hide();
            }
            finally
            {
                release.Set(); if (controller != null) { controller.BackgroundSnapshotObserverForTest = null; controller.Dispose(); }
                Native.TestSurface = oldSurface; host.Close();
            }
        }
        private static void CheckHistoryPaging49(string directory)
        {
            AppState state = new AppState { Language = "zh-CN", DesktopMode = false };
            for (int i = 0; i < 125; i++) { TodoItem item = state.Add("历史分页 " + i); state.Toggle(item); }
            TodoWindow view = new TodoWindow(state);
            try
            {
                view.Window.Show(); view.Control<Expander>("CompletedSection").IsExpanded = true; AdvanceFrames(230);
                StackPanel rows = view.Control<StackPanel>("CompletedList");
                Check(rows.Children.OfType<Grid>().Count() == 50 && state.Completed.Count() == 125, "已完成首批仅创建五十条控件，全部历史数据完整保留");
                Click49(view, "CompletedLoadMore"); Check(rows.Children.OfType<Grid>().Count() == 100, "已完成第二批按需加载到一百条");
                Click49(view, "CompletedLoadMore");
                Check(rows.Children.OfType<Grid>().Count() == 125 && view.Control<Button>("CompletedLoadMore").Parent == null &&
                    rows.Children.OfType<Grid>().Select(row => ((TodoItem)row.Tag).Id).Distinct().Count() == 125,
                    "历史尾页完整可达、没有重复或遗漏，加载完后移除加载按钮");
                view.Control<Expander>("CompletedSection").IsExpanded = false; view.Control<Expander>("CompletedSection").IsExpanded = true;
                Check(rows.Children.OfType<Grid>().Count() == 50, "折叠释放历史行，重新展开从首批开始");
                view.SetSearchOpen(true); view.Control<TextBox>("SearchInput").Text = "历史分页 0"; view.ApplySearch();
                Check(rows.Children.OfType<Grid>().Count() == 1 && ((TodoItem)rows.Children.OfType<Grid>().Single().Tag).Text == "历史分页 0",
                    "搜索覆盖尚未加载的历史记录，不局限于已显示的首批");
                view.Control<TextBox>("SearchInput").Text = "历史分页"; view.ApplySearch(); view.Window.Width = 320; view.Window.Height = 360; AdvanceFrames(230);
                StackPanel compact = view.Control<StackPanel>("PendingList");
                Check(compact.Children.OfType<Grid>().Count(row => row.Tag is TodoItem) == 50 && view.Control<Button>("CompletedLoadMore").Parent == compact,
                    "最小窗口的合并搜索结果同样分批加载，加载按钮位于可滚动结果区");
                Click49(view, "CompletedLoadMore");
                Check(compact.Children.OfType<Grid>().Count(row => row.Tag is TodoItem) == 100, "最小窗口也能加载后续历史搜索结果");
                view.Window.Width = 440; view.Window.Height = 636; AdvanceFrames(230);
                Check(view.Control<Button>("CompletedLoadMore").Parent == rows && rows.Children.OfType<Grid>().Count() == 100,
                    "大小窗口切换安全转移加载按钮和已加载行，不重复挂接控件");
                view.SetSearchOpen(false); Click49(view, "ClearCompletedButton");
                Check(view.Control<TextBlock>("ConfirmBody").Text.Contains("125") && view.Control<TextBlock>("ConfirmBody").Text.Contains("尚未加载"),
                    "清空确认明确包含未加载记录并使用完整范围计数");
                Click49(view, "ConfirmCancelButton"); Check(state.Completed.Count() == 125, "取消清空不会删除任何历史");
                view.Control<Expander>("CompletedSection").IsExpanded = true; AdvanceFrames(230);
                ScrollViewer emptyScroll = view.Control<ScrollViewer>("PendingScroll"); emptyScroll.ScrollToHome(); Pump();
                TextBlock emptyTitle = view.Control<TextBlock>("EmptyTitle");
                Rect emptyBounds = emptyTitle.TransformToAncestor(emptyScroll).TransformBounds(new Rect(emptyTitle.RenderSize));
                Check(emptyBounds.Top >= -1 && emptyBounds.Bottom <= emptyScroll.ActualHeight + 1,
                    "展开大量历史后空状态主文字仍完整可见，不被装饰图标挤出视口");
                Render(view, Path.Combine(directory, "v149-history-page.png"));
                view.ClearCompleted(); Check(state.Completed.Count() == 0 && state.RecentlyDeleted.Count() == 125, "确认清空处理整个范围，不漏掉未加载事项");
                view.Undo(); Check(state.Completed.Count() == 125 && rows.Children.OfType<Grid>().Count() <= 100, "批量撤销恢复所有历史，界面仍受分页上限控制");
            }
            finally { view.Window.Close(); }
        }
        private static void CheckPurge49(Controller controller, AppState state, Storage store, string directory)
        {
            TodoWindow view = controller.CurrentUi;
            TodoItem deleted = state.Add("手动清理的已删除事项"); view.Remove(deleted); controller.Hide(); controller.Show();
            view.SettingsOpen = true; view.OpenRecentlyDeleted(); AdvanceFrames(230); Click49(view, "PurgeDeletedButton");
            Click49(view, "ConfirmCancelButton");
            Check(state.Items.Contains(deleted) && Directory.GetFiles(store.DirectoryPath, "before-purge-*.json").Length == 0,
                "取消最近删除清理不会删除记录，也不会生成无用备份");
            view.OpenRecentlyDeleted(); Click49(view, "PurgeDeletedButton");
            TodoItem later = state.Add("确认打开之后才删除的事项"); later.Deleted = true; later.DeletedAt = DateTime.UtcNow.Ticks; view.NotifyChanged();
            int active = state.Items.Count(t => !t.Deleted);
            Click49(view, "ConfirmAcceptButton");
            string safety = Directory.GetFiles(store.DirectoryPath, "before-purge-*.json").Single();
            Check(!state.Items.Contains(deleted) && state.Items.Contains(later) && state.Items.Count(t => !t.Deleted) == active &&
                Storage.Read(safety).RecentlyDeleted.Any(t => t.Id == deleted.Id) && !Storage.Read(store.DataPath).Items.Any(t => t.Id == deleted.Id),
                "手动清理先保存有效完整备份并落盘，仅移除确认时选中的已删除记录");
            Render(view, Path.Combine(directory, "v149-recently-deleted.png"));
            view.OpenRecentlyDeleted(); Click49(view, "PurgeDeletedButton");
            using (FileStream held = File.Open(store.DataPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Click49(view, "ConfirmAcceptButton");
                Check(state.Items.Contains(later) && view.OverlayOpen && view.Control<TextBlock>("ConfirmTitle").Text == "清理未完成",
                    "模拟磁盘拒绝替换时恢复清理前记录，不显示虚假清理成功");
            }
            view.CloseOverlay(); controller.Hide(); controller.Show();
            Check(Storage.Read(store.DataPath).Items.Any(t => t.Id == later.Id), "清理失败后恢复的记录可重新正常保存");
        }
        private static void CheckImport49(Controller controller, AppState state, Storage store, string fixture)
        {
            TodoWindow view = controller.CurrentUi;
            MethodInfo import = typeof(Controller).GetMethod("ImportFromPath", BindingFlags.Instance | BindingFlags.NonPublic);
            Storage incoming = new Storage(Path.Combine(fixture, "v149-import"));
            AppState replacement = new AppState { Language = "zh-CN", DesktopMode = false }; replacement.Add("导入的新记录"); incoming.Save(replacement);
            string oldId = state.Items.First().Id;
            import.Invoke(controller, new object[] { incoming.DataPath });
            using (FileStream held = File.Open(store.DataPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Click49(view, "ConfirmAcceptButton");
                Check(state.Items.Any(t => t.Id == oldId) && state.Items.All(t => t.Text != "导入的新记录") &&
                    view.Control<TextBlock>("ConfirmTitle").Text == "导入未完成", "导入落盘失败时恢复原清单，不先宣称导入成功");
            }
            view.CloseOverlay(); controller.Hide(); controller.Show();
            import.Invoke(controller, new object[] { incoming.DataPath }); Click49(view, "ConfirmAcceptButton");
            Check(state.Pending.Single().Text == "导入的新记录" && Storage.Read(store.DataPath).Pending.Single().Text == "导入的新记录" &&
                view.ToastText.Contains("已恢复") && !view.OverlayOpen && Directory.GetFiles(store.DirectoryPath, "before-import-*.json").Length >= 2,
                "成功导入仍先备份并确认落盘，以非阻塞提示报告完成");
        }
    }
}
