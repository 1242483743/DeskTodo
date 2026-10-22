using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace DesktopTodo
{
    internal static partial class Tests
    {
        private static void CheckProductivity47(string fixture, string directory)
        {
            AppState state = new AppState { Language = "zh-CN", DesktopMode = false, Locked = false, Width = 440, Height = 636 };
            TodoGroup group = new TodoGroup { Name = "生产计划" }; state.Groups.Add(group);
            TodoItem alpha = state.Add("跟进 Alpha 样品", group.Id), beta = state.Add("整理返修数据");
            TodoItem done = state.Add("完成 alpha BOM", group.Id); state.Toggle(done);
            state.CollapsedGroupIds.Add(group.Id);
            TodoWindow view = new TodoWindow(state);
            try
            {
                view.Window.Show(); AdvanceFrames(230);
                foreach (int[] size in new[] { new[] { 320, 360 }, new[] { 440, 636 }, new[] { 640, 900 } })
                {
                    view.Window.Width = size[0]; view.Window.Height = size[1]; view.SettingsOpen = false; AdvanceFrames(230);
                    Button menu = view.Control<Button>("MenuButton");
                    Rect main = menu.TransformToAncestor((Visual)view.Window.Content).TransformBounds(new Rect(menu.RenderSize));
                    Render(view, Path.Combine(directory, "v147-home-" + size[0] + ".png"));
                    view.SettingsOpen = true; AdvanceFrames(230);
                    Button back = view.Control<Button>("SettingsBack");
                    Rect settings = back.TransformToAncestor((Visual)view.Window.Content).TransformBounds(new Rect(back.RenderSize));
                    Check(Math.Abs(main.Right - settings.Right) < 1 && Math.Abs(main.Width - settings.Width) < 1 &&
                        Math.Abs(main.Top + main.Height / 2 - settings.Top - settings.Height / 2) < 1,
                        size[0] + " 宽主页更多与设置返回的点击区域、左右中心和垂直中心一致");
                    Render(view, Path.Combine(directory, "v147-settings-" + size[0] + ".png"));
                }
                view.Window.Width = 440; view.Window.Height = 636; view.SettingsOpen = false; AdvanceFrames(230);
                view.SetSearchOpen(true); view.Control<TextBox>("SearchInput").Text = "ALPHA"; view.ApplySearch(); Pump();
                Grid[] rows = view.Control<StackPanel>("PendingList").Children.OfType<Grid>().Where(r => r.Tag is TodoItem).ToArray();
                Check(rows.Length == 1 && rows[0].Tag == alpha && state.CollapsedGroupIds.Contains(group.Id),
                    "搜索大小写不敏感、查出折叠组事项但不改写原折叠偏好");
                Check(view.Control<StackPanel>("CompletedList").Children.OfType<Grid>().Any(r => r.Tag == done),
                    "搜索同时呈现已完成匹配事项");
                view.Window.Width = 320; view.Window.Height = 360; AdvanceFrames(230);
                FrameworkElement dock = view.Control<FrameworkElement>("CompletedDock");
                Render(view, Path.Combine(directory, "v147-search-min.png"));
                Check(dock.Visibility == Visibility.Collapsed && view.Control<Border>("AddBar").Visibility == Visibility.Collapsed &&
                    view.Control<StackPanel>("PendingList").Children.OfType<Grid>().Any(r => r.Tag == done),
                    "最小窗口搜索使用统一可滚动结果区，已完成匹配不会因高度不足被隐藏");
                ScrollViewer results = view.Control<ScrollViewer>("PendingScroll"); results.ScrollToEnd(); Pump();
                Grid doneResult = view.Control<StackPanel>("PendingList").Children.OfType<Grid>().Single(r => r.Tag == done);
                Rect resultBounds = doneResult.TransformToAncestor(results).TransformBounds(new Rect(doneResult.RenderSize));
                Check(resultBounds.Top >= -1 && resultBounds.Bottom <= results.ActualHeight + 1,
                    "最小卡片实际滚动到底后，已完成搜索结果整行完整进入视口");
                Render(view, Path.Combine(directory, "v147-search-min-completed.png"));
                view.Window.Width = 440; view.Window.Height = 636; AdvanceFrames(230);
                Check(dock.Visibility == Visibility.Visible && view.Control<StackPanel>("CompletedList").Children.OfType<Grid>().Any(r => r.Tag == done),
                    "搜索跨大小窗口时结果行安全切换容器，恢复正常底部已完成区");
                Grid searchHeading = view.Control<StackPanel>("PendingList").Children.OfType<Grid>().First(r => !(r.Tag is TodoItem));
                searchHeading.Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(state.CollapsedGroupIds.Count == 1 && state.CollapsedGroupIds.Contains(group.Id) &&
                    !view.Control<StackPanel>("PendingList").Children.OfType<Grid>().Any(r => r.Tag is TodoItem),
                    "搜索中临时折叠分组不改写主页折叠状态，退出可恢复原偏好");
                view.Control<TextBox>("SearchInput").Text = "生产计划"; view.ApplySearch();
                Check(view.Control<StackPanel>("PendingList").Children.OfType<Grid>().Any(r => r.Tag == alpha), "可按分组名称搜索事项");
                view.Control<TextBox>("SearchInput").Text = "不存在的关键词"; view.ApplySearch();
                Check(view.Control<StackPanel>("PendingList").Children.Count == 0 && view.Control<TextBlock>("EmptyTitle").Text.Contains("匹配"),
                    "零搜索结果不显示空分组或错误的全部完成文案");
                view.SetSearchOpen(false);
                Check(!view.SearchOpen && !view.Control<Expander>("CompletedSection").IsExpanded && state.Pending.Count() == 2,
                    "退出搜索恢复原已完成展开状态，不改变任何事项");
                view.SettingsOpen = true; view.OpenSettingsCategory("CustomSectionLabel"); AdvanceFrames(240);
                ComboBox times = view.Control<ComboBox>("TimeDisplaySelect");
                int changes = 0; view.SettingsChanged = delegate { changes++; };
                times.SelectedIndex = 0; Check(state.HideItemTimes && changes == 1, "合并的时间选项关闭显示，只发出一次保存通知");
                times.SelectedIndex = 2; Check(!state.HideItemTimes && state.ShowItemDates && changes == 2, "一个选项即可切换月日和时间");
                Render(view, Path.Combine(directory, "v147-appearance.png"));
                view.OpenSettingsCategory("BasicSectionLabel"); AdvanceFrames(240);
                Check(!view.Control<TextBox>("ShowHotkeyInput").IsVisible, "低频快捷键默认收在高级设置，不挤占基础页面");
                view.ExpandAdvancedSettings(); view.Window.UpdateLayout();
                Check(view.Control<TextBox>("ShowHotkeyInput").IsVisible, "高级设置可重新展开快捷键，原功能未删除");
                long completedAt = done.CompletedAt;
                view.Remove(done); view.ClearUndoHistory();
                Check(state.RecentlyDeleted.Single() == done && done.DeletedAt > 0, "清理短时撤销记录不会永久删除事项");
                string dataDirectory = Path.Combine(fixture, "v147-history"); Storage store = new Storage(dataDirectory);
                store.Save(state); AppState reloaded = store.Load();
                TodoItem deleted = reloaded.RecentlyDeleted.Single(); reloaded.Restore(deleted);
                Check(!deleted.Deleted && deleted.DeletedAt == 0 && deleted.CompletedAt == completedAt && deleted.GroupId == group.Id,
                    "重启后仍可恢复已删除事项，保留分组、原完成状态及完成时间");
                view.OpenRecentlyDeleted(); AdvanceFrames(240); Render(view, Path.Combine(directory, "v147-recently-deleted.png"));
                beta.Text = "修改后的新内容"; store.Save(state);
                string history = store.HistoryFiles().Single();
                Check(Storage.Read(history).Items.Single(i => i.Id == beta.Id).Text == "整理返修数据", "每日历史备份保留当天第一次修改前的数据");
                beta.Text = "再次修改"; store.Save(state);
                Check(Storage.Read(history).Items.Single(i => i.Id == beta.Id).Text == "整理返修数据", "同日后续保存不会覆盖当日历史快照");
                for (int i = 1; i <= 31; i++) File.Copy(store.DataPath,
                    Path.Combine(store.HistoryPath, "tasks-" + DateTime.Now.Date.AddDays(-i).ToString("yyyy-MM-dd") + ".json"));
                string manualExport = Path.Combine(store.HistoryPath, "manual-export.json"); store.Export(state, manualExport);
                new Storage(dataDirectory).Save(state);
                Check(store.HistoryFiles().Length == 30 && File.Exists(manualExport),
                    "历史轮换只保留最近三十份应用快照，不删除用户另存的导出文件");
                string damaged = Path.Combine(fixture, "v147-damaged"); Directory.CreateDirectory(damaged);
                File.WriteAllText(Path.Combine(damaged, "tasks.json"), "broken", Encoding.UTF8);
                Storage bad = new Storage(damaged); bad.Save(new AppState());
                Check(bad.HistoryFiles().Length == 0 && Storage.Read(bad.DataPath) != null && !String.IsNullOrEmpty(bad.HistoryWarning),
                    "损坏文件不会混入有效历史；备份失败不阻断当前清单保存，并保留警告");
                foreach (string tag in new[] { "v1.47", "v1.48.2" })
                {
                    using (MemoryStream input = new MemoryStream(Encoding.UTF8.GetBytes("{\"tag_name\":\"" + tag + "\",\"draft\":false,\"prerelease\":false}")))
                    { ReleaseInfo release = ReleaseInfo.Read(input); Check(release.Page.EndsWith(tag) && release.Version.Build >= 0, "更新检查解析正式版本 " + tag); }
                }
                bool rejected = false;
                try { using (MemoryStream input = new MemoryStream(Encoding.UTF8.GetBytes("{\"tag_name\":\"https://evil.example\"}"))) ReleaseInfo.Read(input); }
                catch (InvalidDataException) { rejected = true; }
                Check(rejected, "更新检查拒绝异常版本与外部地址，不执行远程程序");
                bool rejectedPreview = false;
                try { using (MemoryStream input = new MemoryStream(Encoding.UTF8.GetBytes("{\"tag_name\":\"v99.0\",\"prerelease\":true}"))) ReleaseInfo.Read(input); }
                catch (InvalidDataException) { rejectedPreview = true; }
                Check(rejectedPreview, "正式版更新入口不会引导安装测试版本");
                Check(ReleaseInfo.FromLatestUri(new Uri("https://github.com/1242483743/DeskTodo/releases/tag/v1.47")).Version == new Version(1, 47, 0, 0),
                    "API 限流时可从官方 latest 发布页重定向读取正式版本");
                bool rejectedRedirect = false;
                try { ReleaseInfo.FromLatestUri(new Uri("https://github.com.evil.example/1242483743/DeskTodo/releases/tag/v1.47")); }
                catch (InvalidDataException) { rejectedRedirect = true; }
                Check(rejectedRedirect, "更新限流回退仍严格限制官方仓库和 HTTPS 地址");

                state.Language = "en-US"; view.RefreshImportedState(); view.SettingsOpen = true; view.OpenSettingsCategory("CustomSectionLabel"); AdvanceFrames(230);
                Check(times.Items[2].ToString() == "Month/day and time" && times.SelectedIndex == 2,
                    "切换语言不会清空时间显示偏好或触发额外设置写入");
                Render(view, Path.Combine(directory, "v147-appearance-english.png"));
            }
            finally { view.Window.Close(); }

            AppState many = new AppState { Language = "zh-CN", DesktopMode = false, DisableGroups = true, Width = 440, Height = 636 };
            for (int i = 0; i < 1000; i++) many.Add("批量事项 " + i);
            TodoWindow manyView = new TodoWindow(many);
            try
            {
                manyView.Window.Show(); AdvanceFrames(230);
                StackPanel panel = manyView.Control<StackPanel>("PendingList");
                UIElement[] originals = panel.Children.Cast<UIElement>().ToArray(); int unloads = 0;
                foreach (FrameworkElement row in originals) row.Unloaded += delegate { unloads++; };
                System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < 10; i++) manyView.Refresh();
                watch.Stop(); Pump();
                Check(panel.Children.Cast<UIElement>().SequenceEqual(originals) && unloads == 0,
                    "一千条待办重复刷新不拆卸原控件、不触发字形卸载与重建");
                report.AppendLine("METRIC: 1000 tasks, 10 cached refreshes = " + watch.ElapsedMilliseconds + "ms (not FPS).");
                manyView.Control<TextBox>("SearchInput").Text = "批量事项 999"; manyView.ApplySearch();
                Check(panel.Children.Count == 1 && ((TodoItem)((Grid)panel.Children[0]).Tag).Text == "批量事项 999",
                    "一千条事项中搜索不漏掉列表尾部的数据");
                manyView.Control<TextBox>("SearchInput").Clear(); manyView.ApplySearch();
                Check(panel.Children.Count == 1000, "退出筛选后全部一千条事项及原排序仍可用");
                foreach (TodoItem item in many.Items.Take(120)) { item.Deleted = true; item.DeletedAt = DateTime.UtcNow.Ticks; }
                manyView.SettingsOpen = true; manyView.OpenRecentlyDeleted(); AdvanceFrames(230);
                StackPanel deletedPage = (StackPanel)manyView.SettingsCategoryScroll.Content;
                StackPanel deletedRows = deletedPage.Children.OfType<StackPanel>().Single();
                Button more = deletedPage.Children.OfType<Button>().Single();
                Check(deletedRows.Children.Count == 50 && more.IsVisible, "大量最近删除记录首屏只创建五十行，避免一次性生成全部控件");
                more.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); more.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(deletedRows.Children.Count == 120 && more.Visibility == Visibility.Collapsed, "加载更多可查看全部最近删除记录，不遗漏尾部事项");
            }
            finally { manyView.Window.Close(); }
        }
    }
}
