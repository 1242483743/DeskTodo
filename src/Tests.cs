using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Windows.Input;
using System.Runtime.InteropServices;

namespace DesktopTodo
{
    internal static partial class Tests
    {
        private static int passed;
        private static readonly StringBuilder report = new StringBuilder();
        private static void Check(bool condition, string name)
        { if (!condition) throw new Exception("FAIL: " + name); passed++; report.AppendLine("PASS: " + name); }
        public static int Run(string directory)
        {
            Directory.CreateDirectory(directory);
            string fixture = Path.Combine(directory, "run-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(fixture);
            try
            {
                Application app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                CheckLanRouting50();
                CheckImprovements49(app, fixture, directory);
                CheckFixes48(app, fixture, directory);
                CheckProductivity47(fixture, directory);
                CheckSettingsResponsiveness46(app, fixture, directory);
                CheckMixedDpi45(directory);
                CheckWindowMove44(directory);
                CheckAppearance42(directory);
                CheckInkAlignment43(directory);
                using (System.Drawing.Icon shellIcon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location))
                using (System.Drawing.Bitmap bitmap = shellIcon.ToBitmap())
                    Check(bitmap.GetPixel(0, 0).A == 0 && bitmap.GetPixel(bitmap.Width - 1, 0).A == 0 &&
                        bitmap.GetPixel(0, bitmap.Height - 1).A == 0 && bitmap.GetPixel(bitmap.Width - 1, bitmap.Height - 1).A == 0,
                        "Windows 实际提取的 EXE 图标四角透明，不带白色方形画布");
                Check(Native.GetAwarenessFromDpiAwarenessContext(Native.GetThreadDpiAwarenessContext()) == 2,
                    "进程使用 Per-Monitor DPI 感知，跨不同缩放比例屏幕不沿用主屏比例");
                AppState state = new AppState();
                Check(state.Locked && state.Title == "DeskTodo" && state.DefaultCorner == "TopRight" && state.DefaultScalePercent == 100 && !state.StartOnSecondaryOnly,
                    "首次启动默认锁定、名称为 DeskTodo，并使用右上角 100% 偏好");
                Check(state.Version == 4 && state.PendingSort == "Ascending" && state.WindowOpacityPercent == 100 && !state.ShowCompactButton && !state.DisableGroups &&
                    state.MiniAlwaysOnTop && state.MiniIconOpacityPercent == 92 && String.IsNullOrEmpty(state.Language),
                    "新版默认按时间升序、界面完全不透明且不显示悬浮入口，并保留首次语言选择");
                AppState migratedV1 = new AppState { Version = 1, Language = null, MiniAlwaysOnTop = false,
                    WindowOpacityPercent = 0, MiniIconOpacityPercent = 0, MiniX = 900, MiniY = 500, MiniEdge = "Right" };
                migratedV1.Validate();
                Check(migratedV1.Version == 4 && migratedV1.Language == "zh-CN" && migratedV1.MiniAlwaysOnTop &&
                    migratedV1.WindowOpacityPercent == 100 && migratedV1.MiniIconOpacityPercent == 92 &&
                    Double.IsNaN(migratedV1.MiniX) && Double.IsNaN(migratedV1.MiniY) && migratedV1.MiniEdge == null,
                    "旧数据无损迁移到 v4，并清理不同缩放屏幕下失效的悬浮窗坐标");
                AppState ordered = new AppState { Language = "zh-CN" };
                TodoItem orderA = ordered.Add("A"), orderB = ordered.Add("B"), orderC = ordered.Add("C");
                orderA.CreatedAt = 30; orderB.CreatedAt = 10; orderC.CreatedAt = 20;
                ordered.PendingSort = "Ascending";
                Check(String.Join("", ordered.Pending.Select(item => item.Text)) == "BCA", "待办可按添加时间升序排列");
                ordered.PendingSort = "Descending";
                Check(String.Join("", ordered.Pending.Select(item => item.Text)) == "ACB", "待办可按添加时间降序排列");
                orderC.Pinned = true;
                Check(ordered.Pending.First() == orderC, "置顶事项在时间排序中仍固定显示在最前");
                orderC.Pinned = false; orderA.SortOrder = 2; orderB.SortOrder = 0; orderC.SortOrder = 1; ordered.PendingSort = "Manual"; ordered.Validate();
                Check(ordered.PendingSort == "Manual" && String.Join("", ordered.Pending.Select(item => item.Text)) == "BCA", "拖拽后的自定义顺序可保存并恢复");
                AppState groupedState = new AppState { Language = "zh-CN" };
                TodoGroup workGroup = new TodoGroup { Name = "工作", SortOrder = 0 };
                groupedState.Groups.Add(workGroup);
                TodoItem groupedItem = groupedState.Add("分组事项", workGroup.Id);
                groupedState.Validate();
                Check(groupedState.Groups.Count == 1 && groupedItem.GroupId == workGroup.Id && groupedState.Pending.Single().GroupId == workGroup.Id,
                    "分组及事项归属写入新版数据模型并通过校验");
                groupedState.Groups.Clear(); groupedState.Validate();
                Check(groupedItem.GroupId == null, "删除或缺失分组时保留待办并自动转为未分组");
                LanSnapshot simulatedSnapshot = new LanSnapshot { Revision = 7, Title = "远程清单", Tasks = new List<LanTaskInfo> { new LanTaskInfo { Id = "one", Text = "远程事项" } } };
                using (LanService simulatedLan = new LanService(
                    delegate { return new LanPeer { InstanceId = "test", DisplayName = "测试用户", MachineName = "QA", Version = "1.25" }; },
                    delegate(LanRequest request) { return request.AccessCode == "SAFE1234" ? new LanResponse { Success = true, Snapshot = simulatedSnapshot } : LanResponse.Fail("管理口令不正确。"); }, null, 0))
                {
                    LanPeer localPeer = new LanPeer { Address = "127.0.0.1", Port = simulatedLan.Port, InstanceId = "test", DisplayName = "测试用户" };
                    Check(!LanService.Send(localPeer, new LanRequest { Operation = "snapshot", AccessCode = "WRONG" }).Success &&
                        LanService.Send(localPeer, new LanRequest { Operation = "snapshot", AccessCode = "SAFE1234" }).Snapshot.Tasks.Single().Text == "远程事项",
                        "局域网命令使用管理口令鉴权，并能在 TCP 回环中完整读取待办快照");
                }
                string adminPrefsDir = Path.Combine(fixture, "lan-prefs");
                LanAdminPreferences adminPrefs = LanAdminPreferences.Load(adminPrefsDir);
                adminPrefs.Aliases["test-user"] = "设计组";
                adminPrefs.Codes["test-user"] = "SAFE1234";
                adminPrefs.HiddenIds.Add("hidden-user"); adminPrefs.Save();
                LanAdminPreferences reopenedPrefs = LanAdminPreferences.Load(adminPrefsDir);
                Check(reopenedPrefs.CodeFor("test-user") == "SAFE1234" && reopenedPrefs.IsHidden("hidden-user") &&
                    reopenedPrefs.NameFor(new LanPeer { InstanceId = "test-user", DisplayName = "原名" }) == "设计组" &&
                    !Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(adminPrefsDir, "lan-admin-preferences.dat"))).Contains("SAFE1234"),
                    "管理员重用已验证口令与用户别名；本机偏好加密保存，不进入明文备份");
                reopenedPrefs.Codes["integration-user"] = "SAFE1234"; reopenedPrefs.Save();
                object lanGate = new object();
                LanSnapshot liveTasks = new LanSnapshot { Revision = 1, Tasks = new List<LanTaskInfo> {
                    new LanTaskInfo { Id = "initial", Text = "最初的事项", CreatedAt = DateTime.UtcNow.Ticks } } };
                using (LanService liveLan = new LanService(
                    delegate { return new LanPeer { InstanceId = "integration-user", DisplayName = "联机测试", MachineName = "QA" }; },
                    delegate(LanRequest request)
                    {
                        if (request.AccessCode != "SAFE1234") return LanResponse.Fail("管理口令不正确。");
                        lock (lanGate)
                        {
                            if (request.Operation == "add")
                            {
                                liveTasks.Tasks.Add(new LanTaskInfo { Id = "new", Text = request.Text,
                                    GroupId = request.GroupId, CreatedAt = DateTime.UtcNow.Ticks }); liveTasks.Revision++;
                            }
                            else if (request.Operation == "update")
                            {
                                LanTaskInfo target = liveTasks.Tasks.FirstOrDefault(item => item.Id == request.ItemId);
                                if (target == null) return LanResponse.Fail("事项不存在");
                                target.Text = request.Text; target.GroupId = request.GroupId;
                                target.Completed = request.Completed;
                                target.CompletedAt = target.Completed ? DateTime.UtcNow.Ticks : 0; liveTasks.Revision++;
                            }
                            return new LanResponse { Success = true, Snapshot = LanJson.Read<LanSnapshot>(LanJson.Write(liveTasks)) };
                        }
                    }, null, 0))
                using (LanAdminWindow liveAdmin = new LanAdminWindow(new AppState { Language = "zh-CN" }, adminPrefsDir))
                {
                    liveAdmin.LoadPeersForTest(new[] { new LanPeer { InstanceId = "integration-user",
                        DisplayName = "联机测试", MachineName = "QA", Address = "127.0.0.1", Port = liveLan.Port } });
                    liveAdmin.UserList.SelectedIndex = 0;
                    Check(PumpUntil(delegate { return liveAdmin.PendingRows.Children.OfType<Grid>().Count(item => item.Tag is LanTaskInfo) == 1 &&
                        liveAdmin.StatusText.Text.StartsWith("已连接"); }, 3000),
                        "选择已授权用户后自动使用记住的口令，通过真实 TCP 读取其待办");
                    liveAdmin.AddTextInput.Text = "管理员新增事项";
                    liveAdmin.AddTaskButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    Check(PumpUntil(delegate { return liveAdmin.PendingRows.Children.OfType<Grid>().Count(item => item.Tag is LanTaskInfo) == 2; }, 3000),
                        "管理员页面的主页式添加框通过 TCP 实际新增用户待办");
                    Grid addedRow = liveAdmin.PendingRows.Children.OfType<Grid>().First(item => item.Tag is LanTaskInfo &&
                        ((LanTaskInfo)item.Tag).Text == "管理员新增事项");
                    CheckBox remoteCheck = addedRow.Children.OfType<CheckBox>().Single(); remoteCheck.IsChecked = true;
                    remoteCheck.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    Check(PumpUntil(delegate { lock (lanGate) return liveTasks.Tasks.Single(item => item.Id == "new").Completed; }, 3000),
                        "管理员勾选事项通过 TCP 实际同步完成状态");
                }
                Check(state.Add("   ") == null && state.Items.Count == 0, "空事项不会添加");
                TodoItem first = state.Add("  整理今天的工作清单  ");
                Check(first.Text == "整理今天的工作清单" && state.Pending.Count() == 1, "添加和去除首尾空白");
                TodoItem second = state.Add("午休后出去走走 🌿");
                Check(first.Id != second.Id, "事项具有唯一 ID");
                state.Toggle(first);
                Check(state.Pending.Count() == 1 && state.Completed.Count() == 1, "完成事项移入已完成");
                state.Toggle(first);
                Check(!first.IsCompleted && state.Pending.Count() == 2, "取消完成返回待办");
                bool rejected = false;
                try { state.Add(new string('字', 501)); } catch (ArgumentException) { rejected = true; }
                Check(rejected, "拒绝超过 500 字的事项");
                Storage store = new Storage(fixture);
                store.Save(state);
                AppState loaded = store.Load();
                Check(loaded.Items[1].Text == second.Text && loaded.Items.Count == 2, "JSON 保存、中文和 emoji 恢复");
                state.ThemeId = "ocean"; state.Title = "我的计划";
                first.ReminderIntervalMinutes = 90; first.DailyReminderMinutesPlusOne = 20 * 60 + 31;
                first.LastIntervalReminderAt = DateTime.UtcNow.Ticks; store.Save(state);
                loaded = store.Load();
                Check(loaded.ThemeId == "ocean" && loaded.Title == "我的计划" && loaded.Items[0].ReminderIntervalMinutes == 90 &&
                    loaded.Items[0].DailyReminderMinutesPlusOne == 1231 && loaded.Items[1].ReminderIntervalMinutes == 0,
                    "自定义标题和独立事项提醒持久保存，不影响其他事项");
                state.DisableGroups = true; store.Save(state); loaded = store.Load();
                Check(loaded.DisableGroups && loaded.Items.Count == 2, "关闭分组设置持久保存，不影响待办数据");
                state.DisableGroups = false; store.Save(state);
                string legacy = File.ReadAllText(store.DataPath).Replace("\"Title\":\"我的计划\",", "");
                File.WriteAllText(store.DataPath, legacy); loaded = store.Load();
                Check(loaded.Title == "DeskTodo" && !loaded.HideItemTimes && loaded.DefaultCorner == "TopRight" && loaded.DefaultScalePercent == 100,
                    "旧版数据缺少新设置时迁移为 DeskTodo、默认显示时间并使用右上和 100%，不丢失事项");
                state.ThemeId = "forest";
                TodoItem schedule = new TodoItem { Text = "需要提醒的事项", ReminderIntervalMinutes = 90 };
                DateTime utc = new DateTime(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc); schedule.LastIntervalReminderAt = utc.AddMinutes(-90).Ticks;
                ReminderDue intervalDue = ReminderScheduler.Due(schedule, utc.ToLocalTime(), utc);
                Check(intervalDue.Interval && intervalDue.Any && !ReminderScheduler.Due(schedule, utc.ToLocalTime(), utc.AddSeconds(-1)).Any,
                    "小时与分钟合计的间隔精确到一分钟，未到期不触发");
                TodoItem independent = new TodoItem { Text = "另一事项", ReminderIntervalMinutes = 120, LastIntervalReminderAt = utc.AddMinutes(-90).Ticks };
                Check(!ReminderScheduler.Due(independent, utc.ToLocalTime(), utc).Any, "不同事项独立调度，不共用提醒时间");
                ReminderScheduler.MarkShown(schedule, intervalDue, utc.ToLocalTime(), utc);
                Check(!ReminderScheduler.Due(schedule, utc.ToLocalTime(), utc.AddMinutes(89)).Any &&
                    ReminderScheduler.Due(schedule, utc.ToLocalTime(), utc.AddMinutes(90)).Interval, "弹窗后从本次时间计算下一次间隔提醒");
                schedule.ReminderIntervalMinutes = 0; schedule.DailyReminderMinutesPlusOne = 20 * 60 + 31;
                ReminderDue dailyDue = ReminderScheduler.Due(schedule, new DateTime(2026, 9, 17, 20, 31, 0), utc);
                ReminderScheduler.MarkShown(schedule, dailyDue, new DateTime(2026, 9, 17, 20, 31, 0), utc);
                Check(dailyDue.Daily && !ReminderScheduler.Due(schedule, new DateTime(2026, 9, 17, 22, 0, 0), utc.AddHours(1)).Daily,
                    "每天指定时间只提醒一次");
                schedule.ReminderSnoozeUntil = utc.AddMinutes(10).Ticks;
                Check(!ReminderScheduler.Due(schedule, utc.ToLocalTime(), utc.AddMinutes(9)).Any && ReminderScheduler.Due(schedule, utc.ToLocalTime(), utc.AddMinutes(10)).Snoozed,
                    "稍后提醒在十分钟内抑制重复弹窗并按时恢复");
                schedule.CompletedAt = utc.Ticks;
                Check(!ReminderScheduler.Due(schedule, utc.ToLocalTime(), utc.AddMinutes(10)).Any, "完成事项停止提醒，包括已延后的提醒");
                schedule.CompletedAt = 0; schedule.Deleted = true;
                Check(!ReminderScheduler.Due(schedule, utc.ToLocalTime(), utc.AddMinutes(10)).Any, "删除事项停止提醒");
                schedule.Deleted = false; schedule.ReminderSnoozeUntil = 0; schedule.DailyReminderMinutesPlusOne = 1; schedule.LastDailyReminderDate = null;
                Check(ReminderScheduler.Due(schedule, new DateTime(2026, 9, 18, 0, 0, 0), utc).Daily, "每天提醒支持午夜 00:00");
                state.Toggle(first); store.Save(state);
                Check(File.Exists(store.DataPath + ".bak") && store.Load().Completed.Count() == 1, "原子保存及上一个版本备份");
                state.Locked = true; state.DarkMode = true; state.X = -700; state.Y = 100; state.PositionSet = true;
                state.HideItemTimes = true; state.ShowItemDates = true; state.ShowMinimizeButton = true; state.ShowCloseButton = true;
                state.DefaultCorner = "TopLeft"; state.DefaultScalePercent = 125; store.Save(state);
                loaded = store.Load();
                Check(loaded.Locked && loaded.DarkMode && loaded.X == -700 && loaded.HideItemTimes && loaded.ShowItemDates && loaded.ShowMinimizeButton && loaded.ShowCloseButton &&
                    loaded.DefaultCorner == "TopLeft" && loaded.DefaultScalePercent == 125,
                    "外观、界面按钮、时间开关、默认位置比例及负坐标持久保存");
                state.HideTray = true; store.Save(state);
                Check(store.Load().HideTray, "隐藏托盘偏好随数据持久保存");
                state.HideTray = false; store.Save(state);
                File.WriteAllText(store.DataPath, "broken fixture");
                loaded = store.Load();
                Check(loaded.Completed.Count() == 1 && !String.IsNullOrEmpty(store.Warning), "损坏数据从备份恢复");
                store.Save(loaded);
                Check(Storage.Read(store.DataPath + ".bak").Items.Count == 2 && Directory.GetFiles(fixture, "*.damaged-*").Length == 1, "恢复不会毁坏备份，原损坏文件保留");
                string invalidDir = Path.Combine(fixture, "invalid"); Directory.CreateDirectory(invalidDir);
                File.WriteAllText(Path.Combine(invalidDir, "tasks.json"), "original invalid content");
                bool blocked = false;
                try { new Storage(invalidDir).Load(); } catch (IOException) { blocked = true; }
                Check(blocked && File.ReadAllText(Path.Combine(invalidDir, "tasks.json")) == "original invalid content", "无法恢复时停止，不覆盖用户数据");
                string newerDir = Path.Combine(fixture, "newer"); Storage newer = new Storage(newerDir); newer.Save(new AppState()); newer.Save(new AppState());
                string future = File.ReadAllText(newer.DataPath).Replace("\"Version\":4", "\"Version\":5"); File.WriteAllText(newer.DataPath, future);
                bool newerBlocked = false; try { newer.Load(); } catch (InvalidOperationException) { newerBlocked = true; }
                Check(newerBlocked && File.ReadAllText(newer.DataPath) == future, "新版本数据不会被旧备份降级覆盖");
                AppState extreme = new AppState { Width = double.PositiveInfinity, Height = 10, X = double.NaN }; extreme.Validate();
                Check(extreme.Width == 320 && extreme.Height == 360 && !extreme.PositionSet, "无效窗口尺寸和位置修正");
                AppState overlyWide = new AppState { Width = 720, Height = 520 }; overlyWide.Validate();
                Check(overlyWide.Width == 416 && overlyWide.Height == 520,
                    "过宽的旧窗口在启动时收敛为可阅读的卡片比例");
                AppState largestPreset = new AppState { Width = 900, Height = 1300, DefaultScalePercent = 250 }; largestPreset.Validate();
                Check(largestPreset.Width == 900 && largestPreset.Height == 1300 && largestPreset.DefaultScalePercent == 250,
                    "250% 默认比例及对应最大逻辑尺寸可持久保存");
                AppState importTarget = new AppState { X = -500, Y = 20, Width = 500, Height = 700, DesktopMode = true,
                    StartOnSecondaryOnly = true, SecondaryDeviceName = "DISPLAY2", SecondaryPositionSet = true,
                    SecondaryOffsetX = 44, SecondaryOffsetY = 55, SecondaryWidth = 500, SecondaryHeight = 700 };
                importTarget.Add("导入前");
                AppState importSource = new AppState { Title = "导入清单", ThemeId = "coral", DarkMode = true, HideItemTimes = true, ShowItemDates = true,
                    ShowMinimizeButton = true, ShowCloseButton = true, DefaultCorner = "TopRight", DefaultScalePercent = 150 };
                importSource.Add("导入后的事项"); Controller.ApplyImportedState(importTarget, importSource);
                Check(importTarget.Items.Count == 1 && importTarget.Items[0].Text == "导入后的事项" && importTarget.Title == "导入清单" && importTarget.ThemeId == "coral" &&
                    importTarget.HideItemTimes && importTarget.ShowItemDates && importTarget.ShowMinimizeButton && importTarget.ShowCloseButton &&
                    importTarget.DefaultCorner == "TopRight" && importTarget.DefaultScalePercent == 150 &&
                    importTarget.X == -500 && importTarget.Y == 20 && importTarget.Width == 500 && importTarget.Height == 700 && importTarget.DesktopMode &&
                    importTarget.StartOnSecondaryOnly && importTarget.SecondaryDeviceName == "DISPLAY2" && importTarget.SecondaryOffsetX == 44,
                    "导入备份替换清单与外观，同时保留当前电脑的安全窗口位置和桌面模式");
                AppState firstRunState = new AppState { DesktopMode = false, Locked = false, Language = null };
                TodoWindow firstRunView = new TodoWindow(firstRunState);
                Check(firstRunView.Control<FrameworkElement>("LanguageWelcomePanel").Visibility == Visibility.Visible &&
                    firstRunView.Control<FrameworkElement>("MainPanel").Visibility == Visibility.Collapsed,
                    "首次使用先显示中英文语言选择，不提前露出主页");
                firstRunView.Window.Show(); Pump(); System.Threading.Thread.Sleep(220); Pump();
                Render(firstRunView, Path.Combine(directory, "language-first-run.png"));
                firstRunView.Control<Button>("ChooseEnglishButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(firstRunState.Language == "en-US" && firstRunView.Control<FrameworkElement>("LanguageWelcomePanel").Visibility == Visibility.Collapsed &&
                    firstRunView.Control<TextBlock>("PendingSectionLabel").Text == "Pending" && firstRunView.Control<TextBlock>("SettingsTitle").Text == "Settings",
                    "首次选择 English 后立即切换主页和设置语言并保存选择");
                firstRunView.Window.Close();
                AppState viewState = new AppState { DesktopMode = false, Locked = false, Language = "zh-CN" };
                TodoWindow view = new TodoWindow(viewState);
                view.Changed = delegate { store.Save(viewState); view.IsSaved = true; };
                Check(ReferenceEquals(view.Control<Image>("HeaderIcon").Source, UiAssets.InternalIcon) && view.Window.Icon == null &&
                    UiAssets.InternalIcon.PixelWidth < 1254 && UiAssets.InternalIcon.PixelHeight < 1254,
                    "新 PNG 裁去透明留白后只加载到主界面内部，不替换窗口、程序或托盘图标");
                Check(view.Window.AllowsTransparency && view.Window.Background == Brushes.Transparent, "圆角使用带透明度的抗锯齿绘制，不使用像素窗口区域");
                Border mainCard = view.Control<Border>("Card");
                Grid cardContent = view.Control<Grid>("CardContent");
                Check(mainCard.Clip == null && cardContent.Clip is RectangleGeometry &&
                    ((RectangleGeometry)cardContent.Clip).RadiusX == 17,
                    "只裁剪卡片内层，外框不再被二次圆角裁剪截断抗锯齿边缘");
                int[] cornerSamples = CornerAntialiasSamples(view);
                Check(cornerSamples.All(count => count >= 2),
                    "卡片四个圆角在原生分辨率下均包含平滑过渡像素");
                Check(view.Window.Resources[SystemParameters.FocusVisualStyleKey] is Style, "窗口覆盖系统默认虚线焦点装饰");
                string[] focuslessNames = { "LockButton", "CompactButton", "MinimizeButton", "CloseButton", "MenuButton", "GroupButton", "SortButton", "AddButton", "AddInput", "TitleEditor", "TitleSettingInput", "TitleSaveButton", "UndoButton", "ClearCompletedButton", "CompletedSection", "SettingsBack", "DesktopToggle", "LockToggle", "ShowTrayToggle", "ShowMinimizeToggle", "ShowCloseToggle", "ShowCompactToggle", "MiniAlwaysOnTopToggle", "MiniOpacitySlider", "WindowOpacitySlider", "LanguageSelect", "DarkToggle", "ShowTimesToggle", "ShowDateToggle", "StartupToggle", "SecondaryStartupToggle", "SecondaryDisplaySelect", "LanSharingToggle", "LanDisplayNameInput", "LanAccessCodeInput", "LanRegenerateButton", "LanAdminButton", "GroupBack", "GroupCreateInput", "GroupCreateButton", "ItemIntervalToggle", "ItemDailyToggle", "ItemReminderHours", "ItemReminderMinutes", "ItemReminderDailyTime", "HoursUpButton", "HoursDownButton", "MinutesUpButton", "MinutesDownButton", "DailyTimeUpButton", "DailyTimeDownButton", "ReminderClearButton", "ReminderCancelButton", "ReminderSaveButton", "ResetButton", "PositionMenuButton", "ExportButton", "ImportButton", "HideButton", "ExitButton", "EditInput", "EditCancelButton", "EditSaveButton", "ConfirmCloseButton", "ConfirmCancelButton", "ConfirmAcceptButton", "PositionCloseButton", "PositionRestoreButton", "PositionSaveButton", "ResizeTop", "ResizeBottom", "ResizeLeft", "ResizeRight", "ResizeTopLeft", "ResizeTopRight", "ResizeBottomLeft", "ResizeThumb" };
                Check(focuslessNames.All(name => view.Control<Control>(name) != null && view.Control<Control>(name).FocusVisualStyle == null),
                    "所有命名交互控件均禁用 Alt/键盘虚线焦点框");
                Check(!view.Control<Expander>("CompletedSection").IsExpanded, "已完成默认折叠");
                Button lockButton = view.Control<Button>("LockButton");
                Check(view.Control<System.Windows.Shapes.Path>("LockShackle").Data.ToString() == Geometry.Parse(UiGlyphs.OpenShackle).ToString() && lockButton.Background == Brushes.Transparent, "未锁定显示开锁图标且无外围框");
                Check(!lockButton.IsTabStop && lockButton.FocusVisualStyle == null, "锁按钮不接收 Tab/Alt 焦点方框");
                Check(view.Control<Button>("MinimizeButton").Visibility == Visibility.Collapsed && view.Control<Button>("CloseButton").Visibility == Visibility.Collapsed,
                    "最小化和关闭图标默认不显示");
                Check(TodoWindow.IsTopDragPoint(0, 90) && TodoWindow.IsTopDragPoint(89.9, 90) && !TodoWindow.IsTopDragPoint(90, 90),
                    "从卡片顶边到输入条上方整块区域均属于拖动区，输入条及以下不触发拖动");
                Check(TodoWindow.ShouldShowInputHint("", false) && !TodoWindow.ShouldShowInputHint("", true) && !TodoWindow.ShouldShowInputHint("有内容", false),
                    "输入框获得焦点时立即隐藏占位文字，光标不再与提示字重合");
                Check(view.Input.AcceptsReturn && view.Input.TextWrapping == TextWrapping.Wrap && view.Input.MaxHeight == 76,
                    "新增事项框支持 Ctrl+Enter 换行、自动折行及受控高度");
                viewState.LastSavedAt = new DateTime(2026, 9, 23, 6, 7, 0, DateTimeKind.Utc).Ticks;
                view.IsSaved = true; view.RefreshStatus();
                string expectedSavedTime = DateTime.Now.ToString("HH:mm");
                Check(view.Control<TextBlock>("StatusLabel").Text.Contains(expectedSavedTime), "自动保存状态旁显示实时小时和分钟，而不是上次保存时间");
                Brush unlockedLockColor = lockButton.Foreground;
                lockButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(viewState.Locked && view.Control<System.Windows.Shapes.Path>("LockShackle").Data.ToString() == Geometry.Parse(UiGlyphs.ClosedShackle).ToString() && lockButton.Background == Brushes.Transparent &&
                    view.Control<FrameworkElement>("ResizeHandles").Visibility == Visibility.Hidden && Equals(lockButton.Foreground, unlockedLockColor),
                    "锁定后显示闭锁图标、无外围框、不改变颜色并禁用缩放");
                view.BeginTitleEdit();
                Check(!view.TitleEditing && view.Control<TextBlock>("TitleLabel").Cursor == Cursors.Arrow,
                    "主页标题不再提供原位编辑，整个顶部保持为拖动区域");
                Render(view, Path.Combine(directory, "locked.png"));
                lockButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(!viewState.Locked && view.Control<System.Windows.Shapes.Path>("LockShackle").Data.ToString() == Geometry.Parse(UiGlyphs.OpenShackle).ToString() && view.Control<FrameworkElement>("ResizeHandles").Visibility == Visibility.Visible, "解除锁定恢复开锁图标和缩放手柄");
                Button addButton = view.Control<Button>("AddButton");
                Grid plus = addButton.Content as Grid;
                Border plusHorizontal = plus == null ? null : plus.Children[0] as Border;
                Border plusVertical = plus == null ? null : plus.Children[1] as Border;
                Check(plus != null && plusHorizontal != null && plusVertical != null && addButton.Width == 30 && addButton.Height == 30 && plus.Width == 18 && plus.Height == 18 &&
                    plusHorizontal.Width == 14 && plusHorizontal.Height == 1.8 && plusVertical.Width == 1.8 && plusVertical.Height == 14 &&
                    plusHorizontal.HorizontalAlignment == HorizontalAlignment.Center && plusHorizontal.VerticalAlignment == VerticalAlignment.Center &&
                    plusVertical.HorizontalAlignment == HorizontalAlignment.Center && plusVertical.VerticalAlignment == VerticalAlignment.Center,
                    "加号使用几何线条并在按钮内水平垂直居中");
                StackPanel menuDots = view.Control<Button>("MenuButton").Content as StackPanel;
                Check(menuDots != null && menuDots.Orientation == Orientation.Horizontal && menuDots.Children.Count == 3 &&
                    menuDots.Children.Cast<FrameworkElement>().All(dot => dot.Width == 2.5 && dot.Height == 2.5 && dot.VerticalAlignment == VerticalAlignment.Stretch),
                    "顶部省略号使用完整的居中圆点，不依赖字体基线或负边距");
                int beforeEmptyAdd = viewState.Items.Count;
                view.Input.Text = " "; addButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(viewState.Items.Count == beforeEmptyAdd && addButton.IsEnabled && !addButton.Focusable && !addButton.IsTabStop && addButton.FocusVisualStyle == null &&
                    ((SolidColorBrush)addButton.Background).Color == ((SolidColorBrush)view.Window.Resources["AccentBrush"]).Color,
                    "空输入点击不添加、按钮保持绿色且不产生 Alt 焦点框");
                view.Input.Text = "给自己安排一点专注时间"; view.AddFromInput();
                Check(viewState.Pending.Count() == 1 && view.Input.Text == "" && view.Control<StackPanel>("PendingList").Children.Count == 1, "添加按钮逻辑及输入清空");
                TodoItem editing = viewState.Pending.First();
                int windowsBeforeEdit = app.Windows.Count;
                view.OpenEdit(editing);
                Check(view.EditOpen && view.Control<FrameworkElement>("MainPanel").Visibility == Visibility.Collapsed && app.Windows.Count == windowsBeforeEdit, "编辑事项在卡片内部打开，不创建系统弹窗");
                TextBox editInput = view.Control<TextBox>("EditInput");
                editInput.Text = "  更新后的事项内容  ";
                Check(view.Control<TextBlock>("EditCount").Text == "12 / 500" && view.Control<Button>("EditSaveButton").IsEnabled, "编辑字数和保存状态即时更新");
                Render(view, Path.Combine(directory, "edit.png"));
                view.Control<Button>("EditSaveButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(!view.EditOpen && editing.Text == "更新后的事项内容", "卡片内编辑保存并去除首尾空白");
                view.OpenEdit(editing); editInput.Text = "不应保存";
                view.Control<Button>("EditCancelButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(!view.EditOpen && editing.Text == "更新后的事项内容", "取消编辑不修改事项");
                view.OpenEdit(editing); editInput.Text = "点击空白处自动保存";
                view.Control<FrameworkElement>("EditPanel").RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseDownEvent });
                Check(!view.EditOpen && editing.Text == "点击空白处自动保存", "编辑事项时点击输入框外立即保存并返回主页");
                Grid row = (Grid)view.Control<StackPanel>("PendingList").Children[0];
                Check(((CheckBox)row.Children[0]).Content == null && row.Children.OfType<TextBlock>().Count() == 2 &&
                    row.Children.OfType<TextBlock>().Single(textBlock => Equals(textBlock.Tag, "ItemTime")).Visibility == Visibility.Visible,
                    "事项文字与完成勾选框分离，点击文字不会误完成");
                string createdTime = row.Children.OfType<TextBlock>().Single(textBlock => Equals(textBlock.Tag, "ItemTime")).Text;
                Check(createdTime.Length == 5 && createdTime[2] == ':', "未完成事项在菜单左侧显示本地添加时间 HH:mm");
                view.EditFromTaskText(editing, 1);
                Check(!view.EditOpen && !editing.IsCompleted, "单击未完成事项文字不改变完成状态");
                view.EditFromTaskText(editing, 2);
                Check(view.EditOpen && !editing.IsCompleted, "双击未完成事项文字进入编辑且不改变完成状态");
                view.Control<Button>("EditCancelButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Button rowMenu = row.Children.OfType<Button>().Single(button => button.Width == 23);
                StackPanel rowDots = rowMenu.Content as StackPanel;
                Check(rowMenu.FocusVisualStyle == null && rowDots != null && rowDots.Orientation == Orientation.Vertical && rowDots.Children.Count == 3,
                    "事项菜单使用完整的垂直圆点且无焦点框");
                Check(ToolTipService.GetPlacement(rowMenu) == PlacementMode.Left && ToolTipService.GetHorizontalOffset(rowMenu) == -8 &&
                    rowMenu.ToolTip.ToString() == "编辑、复制、分组、提醒或删除", "事项菜单提示固定显示在按钮左侧，不会被卡片右边缘截断");
                Check(row.ContextMenu.Items.Count == 6 && ((MenuItem)row.ContextMenu.Items[2]).Header.ToString() == "复制事项" &&
                    ((MenuItem)row.ContextMenu.Items[3]).Header.ToString() == "移动到分组" && ((MenuItem)row.ContextMenu.Items[4]).Header.ToString() == "设置提醒",
                    "每条事项右键和右侧菜单都提供置顶、复制、分组与独立提醒入口");
                Check(row.ContextMenu.FontSize == row.Children.OfType<TextBlock>().First().FontSize,
                    "未完成事项菜单字号与待办文字一致，不随大卡片重复放大");
                ((CheckBox)row.Children[0]).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(viewState.Completed.Count() == 1 && view.Control<StackPanel>("CompletedList").Children.Count == 0,
                    "已完成折叠时不创建隐藏事项控件，保留事项数据和数量");
                view.Control<Expander>("CompletedSection").IsExpanded = true;
                Check(viewState.Completed.Count() == 1 && view.Control<StackPanel>("CompletedList").Children.Count == 1, "勾选事件、状态变化和列表迁移");
                row = (Grid)view.Control<StackPanel>("CompletedList").Children[0];
                string completedTime = row.Children.OfType<TextBlock>().Single(textBlock => Equals(textBlock.Tag, "ItemTime")).Text;
                Check(completedTime.Length == 5 && completedTime[2] == ':', "已完成事项在菜单左侧显示本地完成时间 HH:mm");
                Button completedDelete = row.Children.OfType<Button>().Single();
                Check(row.ContextMenu == null && completedDelete.Content is Grid && completedDelete.ToolTip.ToString() == "删除（可撤销）",
                    "已完成事项不再显示菜单，右侧是简洁的删除图标");
                completedDelete.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(editing.Deleted && viewState.Completed.Count() == 0, "已完成右侧删除键直接软删除，不改变其他事项");
                view.Undo(); row = (Grid)view.Control<StackPanel>("CompletedList").Children[0];
                Check(!editing.Deleted && editing.IsCompleted, "撤销单条已完成删除保留原完成状态和时间");
                ((CheckBox)row.Children[0]).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                row = (Grid)view.Control<StackPanel>("PendingList").Children[0];
                Check(viewState.Pending.Count() == 1 && row.Children.OfType<TextBlock>().Single(textBlock => Equals(textBlock.Tag, "ItemTime")).Text == createdTime,
                    "已完成列表取消勾选后恢复显示原添加时间");
                TodoItem removed = viewState.Items[0]; view.Remove(removed);
                Check(viewState.Pending.Count() == 0 && removed.Deleted && Storage.Read(store.DataPath).Items[0].Deleted, "删除为软删除，落盘保留");
                view.Undo();
                Check(!removed.Deleted && viewState.Pending.Count() == 1, "撤销删除并重新保存");
                Render(view, Path.Combine(directory, "empty.png"));
                viewState.Items.Clear(); view.Refresh(); Render(view, Path.Combine(directory, "empty.png"));
                viewState.Add("整理今天的工作清单");
                viewState.Add("给项目写一个简短的进度总结");
                viewState.Add("傍晚去散步，顺便买点水果");
                TodoItem done = viewState.Add("喝一杯水"); viewState.Toggle(done);
                done = viewState.Add("把桌面收拾干净"); viewState.Toggle(done);
                view.Refresh(); Render(view, Path.Combine(directory, "main.png"));
                viewState.PendingSort = "Ascending"; view.Refresh();
                row = (Grid)view.Control<StackPanel>("PendingList").Children[0];
                Check(view.Control<Button>("SortButton").Content.ToString() == "↑ 升序" &&
                    !row.Children.OfType<Button>().Any(button => button.Width == 18),
                    "默认升序按钮文字明确，事项整行可拖动且不增加突兀的拖动把手");
                System.Windows.Documents.AdornerLayer dragLayer = System.Windows.Documents.AdornerLayer.GetAdornerLayer(view.Control<StackPanel>("PendingList"));
                Check(dragLayer != null,
                    "待办列表有可用的浮层，直接拖动可呈现圆角预览");
                Check(!TodoWindow.PastTaskDragStart(new Point(0, 0), new Point(4.9, 0)) &&
                    TodoWindow.PastTaskDragStart(new Point(0, 0), new Point(5, 0)) &&
                    TodoWindow.PastTaskDragStart(new Point(0, 0), new Point(0, -5)),
                    "待办按下后移动 5 DIP 就直接开始拖动，不再等待长按计时器");
                view.Control<Button>("SortButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(viewState.PendingSort == "Descending" && view.Control<Button>("SortButton").Content.ToString() == "↓ 降序",
                    "点击排序按钮直接切换降序，不再弹出选择框");
                view.Control<Button>("SortButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(viewState.PendingSort == "Ascending" && view.Control<Button>("SortButton").Content.ToString() == "↑ 升序",
                    "再次点击排序按钮直接恢复升序");
                List<TodoItem> dragOrder = viewState.Pending.Reverse().ToList();
                view.ApplyVisibleManualOrder(dragOrder); view.Refresh();
                Check(viewState.PendingSort == "Manual" && viewState.Pending.SequenceEqual(dragOrder) &&
                    view.Control<Button>("SortButton").Content.ToString() == "↑ 升序",
                    "拖动排序可保存顺序，排序按钮不显示自定第三状态");
                viewState.PendingSort = "Ascending"; view.Refresh();
                view.OpenGroups();
                view.Control<TextBox>("GroupCreateInput").Text = "项目 A";
                view.Control<Button>("GroupCreateButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(view.GroupOpen && viewState.Groups.Count == 1 && viewState.ActiveGroupId == viewState.Groups[0].Id &&
                    view.Control<StackPanel>("GroupList").Children.Count == 2,
                    "分组页可新建并立即作为添加目标，同时保留未分组入口");
                Check(view.Control<StackPanel>("GroupList").Children.OfType<Border>().Count() == 2 &&
                    view.Control<StackPanel>("GroupList").Children.OfType<Border>().All(surface => surface.CornerRadius.TopLeft == 9),
                    "分组选项与软件卡片一致使用完整圆角，不再显示方形背景");
                Render(view, Path.Combine(directory, "groups.png"));
                view.Control<Button>("GroupBack").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                StackPanel ungroupedPending = view.Control<StackPanel>("PendingList");
                Check(ungroupedPending.Children.OfType<Grid>().Count(grid => grid.Tag == null) == viewState.Groups.Count + 1 &&
                    ungroupedPending.Children.OfType<Grid>().Count(grid => grid.Tag is TodoItem) == viewState.Pending.Count(),
                    "启用分组后保留未分组和空分组标题，主页可以直接拖入空分组");
                Render(view, Path.Combine(directory, "ungrouped-no-heading.png"));
                view.Input.Text = "自动进入项目 A"; view.AddFromInput();
                StackPanel groupedPending = view.Control<StackPanel>("PendingList");
                Check(viewState.Items.Last().GroupId == viewState.Groups[0].Id &&
                    groupedPending.Children.OfType<Grid>().Count(grid => grid.Tag is TodoItem) == viewState.Pending.Count() &&
                    groupedPending.Children.OfType<Grid>().Count(grid => grid.Tag == null) >= 2 &&
                    view.Control<TextBlock>("AddGroupLabel").Text.StartsWith("项目 A"),
                    "新事项归入选定分组，未完成区同时展示各分组及其事项");
                StackPanel foldList = view.Control<StackPanel>("PendingList");
                Grid foldHeading = foldList.Children.OfType<Grid>().First(grid => grid.Tag == null &&
                    grid.Children.OfType<TextBlock>().Any(label => label.Text == "项目 A"));
                Button foldButton = foldHeading.Children.OfType<Button>().Single();
                foldButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(viewState.CollapsedGroupIds.Contains(viewState.Groups[0].Id) &&
                    view.Control<StackPanel>("PendingList").Children.OfType<Grid>().Count(grid => grid.Tag is TodoItem) == viewState.Pending.Count() - 1,
                    "分组标题中的折叠按钮只隐藏该组事项，不改变待办数据");
                Render(view, Path.Combine(directory, "groups-collapsed.png"));
                Storage foldStore = new Storage(Path.Combine(fixture, "fold-state")); foldStore.Save(viewState);
                Check(foldStore.Load().CollapsedGroupIds.Contains(viewState.Groups[0].Id), "分组折叠状态重启后保持");
                foldHeading = view.Control<StackPanel>("PendingList").Children.OfType<Grid>().First(grid => grid.Tag == null &&
                    grid.Children.OfType<TextBlock>().Any(label => label.Text == "项目 A"));
                foldHeading.Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(!viewState.CollapsedGroupIds.Contains(viewState.Groups[0].Id) &&
                    view.Control<StackPanel>("PendingList").Children.OfType<Grid>().Count(grid => grid.Tag is TodoItem) == viewState.Pending.Count(),
                    "再次点击分组箭头展开原有事项");
                Button addGroupButton = view.Control<Button>("AddGroupButton");
                addGroupButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(addGroupButton.ContextMenu != null && addGroupButton.ContextMenu.Items.Count == 4 &&
                    ((MenuItem)addGroupButton.ContextMenu.Items[0]).Header.ToString().Contains("未分组") &&
                    ((MenuItem)addGroupButton.ContextMenu.Items[3]).Header.ToString().Contains("创建"),
                    "分组入口直接提供选择分组和创建分组");
                Check(Grid.GetColumn(addGroupButton) == 1 && Grid.GetColumn(view.Control<Button>("AddButton")) == 2 &&
                    !view.Control<TextBlock>("AddGroupLabel").Text.Contains("⌄"),
                    "分组小框移到加号左侧，平时不显示向下箭头");
                addGroupButton.ContextMenu.IsOpen = false;
                viewState.ActiveGroupId = null; view.Refresh();
                Check(view.Control<StackPanel>("PendingList").Children.OfType<Grid>().Count(grid => grid.Tag is TodoItem) == viewState.Pending.Count() &&
                    view.Control<TextBlock>("AddGroupLabel").Text.StartsWith("未分组"), "切换添加目标不会隐藏其他分组的未完成事项");
                CheckBox disableGroups = view.Control<CheckBox>("DisableGroupsToggle");
                disableGroups.IsChecked = true; disableGroups.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(viewState.DisableGroups && view.Control<Button>("AddGroupButton").Visibility == Visibility.Collapsed &&
                    view.Control<StackPanel>("PendingList").Children.OfType<Grid>().All(grid => grid.Tag is TodoItem) &&
                    viewState.Groups.Count == 1 && viewState.Items.Any(item => item.GroupId == viewState.Groups[0].Id),
                    "关闭分组功能后隐藏分组入口和标题，但保留已有分组及事项归属");
                TodoItem oldGroupedItem = viewState.Pending.First(item => item.GroupId == viewState.Groups[0].Id);
                TodoItem oldUngroupedItem = viewState.Pending.First(item => item.GroupId == null);
                Check(view.CanReorderTaskBeside(oldGroupedItem, oldUngroupedItem),
                    "关闭分组功能后拖动可跨越旧分组边界");
                List<TodoItem> flatOrder = viewState.Pending.ToList();
                flatOrder.Remove(oldGroupedItem); flatOrder.Insert(0, oldGroupedItem);
                view.ApplyVisibleManualOrder(flatOrder); view.Refresh();
                Check(viewState.Pending.First() == oldGroupedItem && oldGroupedItem.GroupId == viewState.Groups[0].Id,
                    "关闭分组功能后跨旧分组排序可保存，且不抹去原分组归属");
                Grid flatRow = view.Control<StackPanel>("PendingList").Children.OfType<Grid>().First(grid => grid.Tag is TodoItem);
                Check(!flatRow.ContextMenu.Items.OfType<MenuItem>().Any(item => item.Header != null && item.Header.ToString() == "移动到分组"),
                    "关闭分组功能后事项菜单不再提供分组操作");
                view.Input.Text = "关闭分组时新增"; view.AddFromInput();
                Check(viewState.Items.Last().GroupId == null, "关闭分组功能后新事项默认不分组");
                Render(view, Path.Combine(directory, "groups-disabled.png"));
                disableGroups.IsChecked = false; disableGroups.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(!viewState.DisableGroups && view.Control<Button>("AddGroupButton").Visibility == Visibility.Visible &&
                    view.Control<StackPanel>("PendingList").Children.OfType<Grid>().Any(grid => grid.Tag == null) &&
                    viewState.Items.Any(item => item.GroupId == viewState.Groups[0].Id),
                    "重新开启分组后恢复分组入口与标题，原有归属不变");
                Check(view.CanReorderTaskBeside(oldGroupedItem, oldUngroupedItem),
                    "重新开启分组后主页拖动可以将同置顶层级的待办移到其他分组");
                FrameworkElement clear = view.Control<FrameworkElement>("ClearCompletedButton");
                Border divider = view.Control<Border>("CompletedDivider");
                double clearTop = clear.TranslatePoint(new Point(), divider).Y;
                Check(clearTop >= 9.5 && clear.ActualHeight >= 30, "清空按钮整个悬停范围与分隔线至少间隔 9.5 DIP，不再重合");
                Check(((System.Windows.Shapes.Path)((Grid)((Button)clear).Content).Children[0]).StrokeThickness == UiGlyphs.Stroke &&
                    view.Control<System.Windows.Shapes.Path>("LockShackle").StrokeThickness == UiGlyphs.Stroke,
                    "垃圾桶与开闭锁改为矢量图形，线宽统一为最小化的 1.8 DIP");
                row = view.Control<StackPanel>("PendingList").Children.OfType<Grid>().First(grid => grid.Tag is TodoItem);
                TextBlock pendingText = row.Children.OfType<TextBlock>().First();
                Check(pendingText.TextDecorations == null || pendingText.TextDecorations.Count == 0, "未完成事项无删除线");
                row = (Grid)view.Control<StackPanel>("CompletedList").Children[0];
                Check(row.Children.OfType<TextBlock>().First().TextDecorations.Count == 1, "已完成事项显示删除线");
                view.Control<Button>("ClearCompletedButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(view.OverlayOpen && view.Control<TextBlock>("ConfirmTitle").Text == "清空已完成" && viewState.Completed.Count() == 2,
                    "清空已完成先在卡片内显示二次确认，不立即修改清单");
                Render(view, Path.Combine(directory, "clear-completed-confirm.png"));
                view.Control<Button>("ConfirmAcceptButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(viewState.Completed.Count() == 0 && view.Control<TextBlock>("UndoMessage").Text.Contains("2 条"), "确认后一次清空全部已完成事项并显示整批撤销");
                Check(view.UndoSecondsRemaining == 10 && view.Control<TextBlock>("UndoMessage").Text.EndsWith("10 秒"), "整批清空提示从 10 秒倒计时开始");
                view.AdvanceUndoForTest(2);
                Check(view.UndoSecondsRemaining <= 8 && view.Control<TextBlock>("UndoMessage").Text.EndsWith(view.UndoSecondsRemaining + " 秒"), "倒计时文字按剩余时间更新");
                Render(view, Path.Combine(directory, "undo-countdown.png"));
                view.Undo();
                Check(viewState.Completed.Count() == 2, "撤销一次恢复整批已清空事项");
                view.ClearCompleted(); view.AdvanceUndoForTest(9);
                DateTime undoDeadline = DateTime.UtcNow.AddSeconds(2);
                while (view.Removed.Count > 0 && DateTime.UtcNow < undoDeadline) { Pump(); System.Threading.Thread.Sleep(20); }
                Check(view.Removed.Count == 0 && view.Control<FrameworkElement>("UndoPanel").Visibility == Visibility.Collapsed && viewState.Completed.Count() == 0,
                    "真实 DispatcherTimer 在 10 秒期限归零后自动移除撤销提示与记录");
                view.ShowNotice("操作提示", "自动关闭测试");
                Check(view.OverlayOpen && view.Control<Button>("ConfirmAcceptButton").Content.ToString().EndsWith("10s"),
                    "普通提示弹框显示 10 秒倒计时");
                view.AdvanceNoticeForTest(11);
                Check(!view.OverlayOpen, "普通提示到期自动关闭，确认操作不会被自动执行");
                foreach (TodoItem item in viewState.Items.Where(item => item.Deleted)) item.Deleted = false;
                view.Refresh();
                view.Control<Expander>("CompletedSection").IsExpanded = true;
                Render(view, Path.Combine(directory, "completed.png"));
                viewState.DarkMode = true; view.ApplyTheme(); view.Refresh(); Render(view, Path.Combine(directory, "dark.png"));
                viewState.DarkMode = false; view.ApplyTheme();
                view.Window.Width = 320; view.Window.Height = 360;
                viewState.Add("这是一条比较长的待办事项，用于确认最小窗口尺寸下文字可以正常换行，不会盖住勾选框或者编辑按钮。");
                for (int i = 0; i < 30; i++) viewState.Add("滚动列表测试事项 " + (i + 1));
                view.Refresh(); Render(view, Path.Combine(directory, "compact.png"));
                Check(view.Control<StackPanel>("PendingList").Children.OfType<Grid>().Count(grid => grid.Tag is TodoItem) == viewState.Pending.Count(), "长文本与大量事项构建");
                Border compactAddBar = view.Control<Border>("AddBar");
                Button compactGroupButton = view.Control<Button>("AddGroupButton");
                Point groupRight = compactGroupButton.TranslatePoint(new Point(compactGroupButton.ActualWidth, 0), compactAddBar);
                Check(view.Input.ActualWidth >= 110 && compactGroupButton.ActualWidth >= 72 &&
                    groupRight.X <= compactAddBar.ActualWidth + 0.1 &&
                    view.Control<TextBlock>("AddGroupLabel").TextTrimming == TextTrimming.CharacterEllipsis,
                    "最小宽度时添加框、加号和分组入口完整显示，长分组名使用省略号");
                Check(Startup.ExpectedCommand.StartsWith("\"") && Startup.ExpectedCommand.Contains("\" --startup"),
                    "自启命令路径带引号并标记自启模式，启动后默认锁定（未修改真实自启设置）");
                Check(Startup.NeedsCommandMigration("\"D:\\Old Folder\\DeskTodo.exe\" --startup", delegate { return false; }) &&
                    Startup.NeedsCommandMigration("\"D:\\Old Folder\\桌面待办.exe\" --startup", delegate { return true; }) &&
                    !Startup.NeedsCommandMigration("\"D:\\Existing\\DeskTodo.exe\" --startup", delegate { return true; }) &&
                    !Startup.NeedsCommandMigration("\"D:\\OtherApp.exe\" --startup", delegate { return false; }) &&
                    !Startup.NeedsCommandMigration("\"D:\\Unclosed\\DeskTodo.exe", delegate { return false; }),
                    "目录改名后仅迁移失效的 DeskTodo 或旧中文自启路径，不覆盖有效或其他应用命令");
                view.Window.Width = 360; view.Window.Height = 520;
                view.Control<Button>("MenuButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(view.SettingsOpen && view.Control<FrameworkElement>("MainPanel").Visibility == Visibility.Collapsed, "设置在卡片内打开，不使用系统菜单");
                Pump(); System.Threading.Thread.Sleep(230); Pump();
                Check(!view.CategoryDialogOpen && view.Control<ScrollViewer>("SettingsScroll").Content is StackPanel &&
                    ((StackPanel)view.Control<ScrollViewer>("SettingsScroll").Content).Children.Count == 4,
                    "设置首页合并为四个分类入口，外观与主题不再分散");
                Render(view, Path.Combine(directory, "settings-overview.png"));
                foreach (string category in new[] { "BasicSectionLabel", "CustomSectionLabel", "ThemeLabel", "LanSectionLabel", "ManagementLabel" })
                {
                    view.OpenSettingsCategory(category);
                    view.Window.UpdateLayout();
                    Render(view, Path.Combine(directory, "settings-category-" + category + ".png"));
                    view.SettingsCategoryScroll.ScrollToEnd(); view.Window.UpdateLayout();
                    Render(view, Path.Combine(directory, "settings-category-" + category + "-bottom.png"));
                }
                view.OpenSettingsCategory("BasicSectionLabel");
                view.ExpandAdvancedSettings();
                view.Window.Width = 320; view.Window.Height = 360; Pump();
                foreach (string category in new[] { "CustomSectionLabel", "LanSectionLabel", "ManagementLabel" })
                {
                    view.OpenSettingsCategory(category); view.Window.UpdateLayout();
                    if (category == "ManagementLabel")
                    {
                        view.OpenSettingsCategory("BasicSectionLabel"); view.ExpandAdvancedSettings(); view.Window.UpdateLayout();
                        Check(view.Control<Button>("ResetButton").ActualWidth >= 160 &&
                            view.Control<Button>("PositionMenuButton").ActualWidth >= 33,
                            "最窄卡片的默认位置大小文字和三点入口都保留足够空间");
                        view.OpenSettingsCategory(category); view.Window.UpdateLayout();
                    }
                    Render(view, Path.Combine(directory, "settings-min-" + category + ".png"));
                    view.SettingsCategoryScroll.ScrollToEnd(); view.Window.UpdateLayout();
                    Render(view, Path.Combine(directory, "settings-min-" + category + "-bottom.png"));
                }
                view.Window.Width = 360; view.Window.Height = 520; Pump();
                view.OpenSettingsCategory("BasicSectionLabel"); view.Window.UpdateLayout();
                Check(view.CategoryDialogOpen && view.SettingsCategoryScroll.Content is StackPanel &&
                    ((StackPanel)view.SettingsCategoryScroll.Content).Children.Contains(view.Control<CheckBox>("DesktopToggle")),
                    "基础功能在卡片内弹框显示并可滚动");
                Render(view, Path.Combine(directory, "settings-category-basics.png"));
                view.OpenSettingsCategory("CustomSectionLabel");
                Check(((StackPanel)view.SettingsCategoryScroll.Content).Children.Contains(view.Control<ComboBox>("TimeDisplaySelect")),
                    "外观分类切换后保留合并的时间选项及已设置值");
                view.SettingsCategoryBackdrop.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                    { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
                Check(!view.CategoryDialogOpen && view.SettingsOpen, "点击设置弹框外部只关闭弹框，仍留在设置首页");
                CheckBox visualToggle = view.Control<CheckBox>("DarkToggle"); visualToggle.ApplyTemplate();
                Grid toggleTrack = visualToggle.Template.FindName("Track", visualToggle) as Grid;
                System.Windows.Shapes.Ellipse toggleKnob = visualToggle.Template.FindName("Knob", visualToggle) as System.Windows.Shapes.Ellipse;
                Check(toggleTrack != null && toggleKnob != null && toggleTrack.Width == 32 && toggleTrack.Height == 18 && toggleTrack.VerticalAlignment == VerticalAlignment.Center &&
                    toggleKnob.Width == 14 && toggleKnob.Height == 14 && toggleKnob.VerticalAlignment == VerticalAlignment.Center,
                    "设置开关使用独立圆形滑块并在圆角轨道内完整居中");
                bool fakeStartup = false;
                view.GetStartup = delegate { return fakeStartup; }; view.SetStartup = delegate(bool value) { fakeStartup = value; };
                view.Control<CheckBox>("StartupToggle").IsChecked = true;
                view.Control<CheckBox>("StartupToggle").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(fakeStartup, "设置自启开关事件（模拟，不修改注册表）");
                Version releaseVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                Check(view.Control<TextBlock>("SettingsVersion").Text == "v" + releaseVersion.Major + "." + releaseVersion.Minor, "设置页最下方显示当前版本号：" + view.Control<TextBlock>("SettingsVersion").Text);
                view.SettingsMessage("设置已保存");
                Check(view.Control<TextBlock>("SettingsStatus").Text.EndsWith("10s"), "设置页临时提示从 10 秒开始倒计时");
                view.AdvanceSettingsMessageForTest(11);
                Check(!view.Control<TextBlock>("SettingsStatus").Text.Contains("设置已保存"), "设置页提示到期恢复常驻状态文字");
                ComboBox languageChoice = view.Control<ComboBox>("LanguageSelect"); languageChoice.ApplyTemplate();
                Check(languageChoice.Style == view.Window.Resources["ModernComboBox"] && languageChoice.Template.FindName("DropDownToggle", languageChoice) != null,
                    "界面语言使用与整张卡片一致的圆角自定义下拉框，不再显示系统默认样式");
                Check(view.Control<CheckBox>("ShowCompactToggle").IsChecked != true && view.Control<Button>("CompactButton").Visibility == Visibility.Collapsed,
                    "显示悬浮图标默认关闭，主页不出现无去向的悬浮入口");
                view.Control<CheckBox>("ShowCompactToggle").IsChecked = true;
                view.Control<CheckBox>("ShowCompactToggle").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(viewState.ShowCompactButton && view.Control<Button>("CompactButton").Visibility == Visibility.Visible,
                    "设置开启显示悬浮图标后，主页入口立即出现");
                Check(view.Control<CheckBox>("LanSharingToggle").IsChecked != true && !String.IsNullOrWhiteSpace(view.Control<TextBox>("LanAccessCodeInput").Text) &&
                    !String.IsNullOrWhiteSpace(view.Control<TextBox>("LanDisplayNameInput").Text),
                    "局域网共享默认关闭，同时预生成显示名称和独立管理口令");
                view.OpenSettingsCategory("ManagementLabel"); view.Window.UpdateLayout();
                Check(view.Window.FindName("ReattachButton") == null && view.Window.FindName("RetryButton") == null &&
                    Equals(view.Control<Button>("ExitButton").Foreground, view.Window.Resources["TextBrush"]),
                    "设置移除重复的重新嵌入和重试保存，退出软件保持正常可用视觉");
                CheckBox miniTopmost = view.Control<CheckBox>("MiniAlwaysOnTopToggle");
                miniTopmost.IsChecked = false; miniTopmost.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                view.Control<Slider>("MiniOpacitySlider").Value = 75;
                Check(!viewState.MiniAlwaysOnTop && viewState.MiniIconOpacityPercent == 75,
                    "设置可分别调整悬浮图标置顶和透明度");
                miniTopmost.IsChecked = true; miniTopmost.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                view.Control<Slider>("MiniOpacitySlider").Value = 92;
                view.Control<CheckBox>("ShowTrayToggle").IsChecked = false;
                view.Control<CheckBox>("ShowTrayToggle").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(viewState.HideTray, "设置新增托盘显隐开关，关闭后保存隐藏偏好");
                view.Control<CheckBox>("ShowTrayToggle").IsChecked = true;
                view.Control<CheckBox>("ShowTrayToggle").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(!viewState.HideTray, "设置可重新显示托盘");
                bool clickedHideTray = false;
                TrayMenu trayMenu = new TrayMenu(view, delegate { }, delegate { clickedHideTray = true; }, delegate { }, delegate { }, delegate { });
                Check(trayMenu.Buttons[1].Content.ToString() == "隐藏托盘" && trayMenu.Buttons[2].Content.ToString() == "嵌入桌面" &&
                    ReferenceEquals(trayMenu.Window.Resources["CardBrush"], view.Window.Resources["CardBrush"]) && trayMenu.Buttons.All(button => button.FocusVisualStyle == null),
                    "托盘菜单使用软件当前主题和无虚线焦点框的 WPF UI，窗口模式显示嵌入桌面");
                Render(trayMenu.Window, Path.Combine(directory, "tray-menu.png"));
                trayMenu.Buttons[1].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Pump();
                Check(clickedHideTray, "托盘隐藏菜单正确执行操作");
                viewState.DesktopMode = true;
                TrayMenu embeddedMenu = new TrayMenu(view, null, null, null, null, null);
                Check(embeddedMenu.Buttons[2].Content.ToString() == "取消嵌入桌面", "托盘嵌入选项随当前模式互换文字，不再使用固定重新嵌入");
                viewState.DarkMode = true; view.ApplyTheme();
                TrayMenu darkTray = new TrayMenu(view, null, null, null, null, null); Render(darkTray.Window, Path.Combine(directory, "tray-menu-dark.png"));
                Check(ReferenceEquals(darkTray.Window.Resources["CardBrush"], view.Window.Resources["CardBrush"]), "深色托盘菜单跟随主题，不显示旧式白色系统菜单");
                embeddedMenu.Close(); darkTray.Close(); viewState.DarkMode = false; viewState.DesktopMode = false; view.ApplyTheme();
                Native.Point menuAt = TrayMenu.Placement(new Native.Point { X = -1, Y = 2550 }, new System.Drawing.Rectangle(-1600, 0, 1600, 2500), 216, 250);
                Check(menuAt.X >= -1600 && menuAt.X + 216 <= 0 && menuAt.Y + 250 <= 2500, "托盘菜单在负坐标副屏工作区内完整显示");
                bool trayOnEveryMonitor = true;
                foreach (Native.MonitorArea monitor in Native.PhysicalMonitors())
                {
                    TrayMenu monitorMenu = new TrayMenu(view, null, null, null, null, null);
                    monitorMenu.ShowAt(new Native.Point { X = monitor.Work.Right - 25, Y = monitor.Work.Bottom - 25 });
                    Pump(); Pump();
                    Native.Rect actualMenu;
                    Native.PhysicalWindowRect(new WindowInteropHelper(monitorMenu.Window).Handle, out actualMenu);
                    trayOnEveryMonitor &= monitorMenu.Window.IsVisible && actualMenu.Left >= monitor.Work.Left && actualMenu.Top >= monitor.Work.Top &&
                        actualMenu.Right <= monitor.Work.Right && actualMenu.Bottom <= monitor.Work.Bottom;
                    monitorMenu.Close();
                }
                Check(trayOnEveryMonitor, "在每块真实显示器的任务栏附近打开托盘菜单，混合 DPI 下完整落在工作区内");
                TrayMenu reentrantMenu = new TrayMenu(view, null, null, null, null, null);
                reentrantMenu.Window.Closing += delegate { reentrantMenu.Close(); };
                reentrantMenu.ShowAt(new Native.Point { X = 200, Y = 300 }); Pump();
                reentrantMenu.Close(); reentrantMenu.Close(); Pump();
                Check(!reentrantMenu.Window.IsVisible, "托盘关闭期间再次关闭、关闭后再次关闭都不会抛出 Window 生命周期异常");
                Check(view.Control<TextBlock>("BasicSectionLabel").Text == "基础功能" &&
                    view.Control<TextBlock>("CustomSectionLabel").Text == "外观",
                    "设置分组使用基础功能与合并后的外观名称");
                view.Control<CheckBox>("ShowMinimizeToggle").IsChecked = true;
                view.Control<CheckBox>("ShowMinimizeToggle").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                view.Control<CheckBox>("ShowCloseToggle").IsChecked = true;
                view.Control<CheckBox>("ShowCloseToggle").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(viewState.ShowMinimizeButton && viewState.ShowCloseButton && view.Control<Button>("MinimizeButton").Visibility == Visibility.Visible &&
                    view.Control<Button>("CloseButton").Visibility == Visibility.Visible,
                    "设置可分别显示最小化和关闭图标，两个选项默认关闭");
                view.Control<CheckBox>("ShowMinimizeToggle").IsChecked = false;
                view.Control<CheckBox>("ShowMinimizeToggle").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                view.Control<CheckBox>("ShowCloseToggle").IsChecked = false;
                view.Control<CheckBox>("ShowCloseToggle").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(view.Control<CheckBox>("ShowTimesToggle").IsChecked == true, "事项时间默认开启");
                view.Control<CheckBox>("ShowTimesToggle").IsChecked = false;
                view.Control<CheckBox>("ShowTimesToggle").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                row = view.Control<StackPanel>("PendingList").Children.OfType<Grid>().First(grid => grid.Tag is TodoItem);
                Check(viewState.HideItemTimes && row.Children.OfType<TextBlock>().Single(textBlock => Equals(textBlock.Tag, "ItemTime")).Visibility == Visibility.Collapsed,
                    "设置可关闭每条事项的添加/完成时间");
                view.Control<CheckBox>("ShowTimesToggle").IsChecked = true;
                view.Control<CheckBox>("ShowTimesToggle").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(!viewState.HideItemTimes && view.Control<CheckBox>("ShowDateToggle").IsEnabled, "设置可重新开启事项时间及月日选项");
                Check(view.Control<CheckBox>("ShowDateToggle").IsChecked != true && !viewState.ShowItemDates, "显示月日默认关闭");
                view.Control<CheckBox>("ShowDateToggle").IsChecked = true;
                view.Control<CheckBox>("ShowDateToggle").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                row = view.Control<StackPanel>("PendingList").Children.OfType<Grid>().First(grid => grid.Tag is TodoItem);
                Check(viewState.ShowItemDates && row.Children.OfType<TextBlock>().Single(textBlock => Equals(textBlock.Tag, "ItemTime")).Text.Contains("/"),
                    "显示月日开启后，事项时间使用月/日和时分");
                Render(view, Path.Combine(directory, "settings.png"));
                Check(view.Control<FrameworkElement>("WindowOpacityPanel").Visibility == Visibility.Visible &&
                    view.Control<Slider>("WindowOpacitySlider").Value == 100 && view.Control<Slider>("WindowOpacitySlider").Minimum == 30,
                    "界面透明度默认完全不透明且最低可调到 30%");
                Panel appearanceSettings = System.Windows.Media.VisualTreeHelper.GetParent(view.Control<CheckBox>("DarkToggle")) as Panel;
                Check(appearanceSettings != null && appearanceSettings.Children.IndexOf(view.Control<FrameworkElement>("WindowOpacityPanel")) ==
                    appearanceSettings.Children.IndexOf(view.Control<CheckBox>("DarkToggle")) + 1,
                    "界面透明度紧接在深色外观选项下方");
                view.Control<CheckBox>("DarkToggle").IsChecked = true;
                view.Control<CheckBox>("DarkToggle").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(viewState.DarkMode && ((SolidColorBrush)view.Window.Resources["CardBrush"]).Color == (Color)ColorConverter.ConvertFromString(ThemeCatalog.Get("forest").Dark[0]) &&
                    view.Control<FrameworkElement>("WindowOpacityPanel").Visibility == Visibility.Visible, "深浅外观共用始终可见的界面透明度调节");
                view.Control<Slider>("WindowOpacitySlider").Value = 30;
                Check(viewState.WindowOpacityPercent == 30 && Math.Abs(view.Control<Border>("Card").Opacity - 0.3) < 0.001 && view.Window.Opacity == 1,
                    "界面透明度作用于实际卡片像素，桌面挂载和普通窗口均能达到 30%");
                int alpha30 = CardCenterAlpha(view);
                view.Control<Slider>("WindowOpacitySlider").Value = 100;
                int alpha100 = CardCenterAlpha(view);
                Check(alpha30 >= 60 && alpha30 <= 105 && alpha100 >= 235,
                    "渲染像素的透明度随滑块从 30% 恢复到 100%，而非仅更新设置数值");
                view.Control<RadioButton>("ThemeOcean").IsChecked = true;
                view.OpenSettingsCategory("ThemeLabel"); view.Window.UpdateLayout();
                view.Control<RadioButton>("ThemeOcean").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(viewState.ThemeId == "ocean" && ((SolidColorBrush)view.Window.Resources["AccentBrush"]).Color == (Color)ColorConverter.ConvertFromString(ThemeCatalog.Get("ocean").Dark[5]) &&
                    ThemeCatalog.All.Length == 7 && ThemeCatalog.All.All(palette => view.Window.FindName("Theme" + Char.ToUpperInvariant(palette.Id[0]) + palette.Id.Substring(1)) != null),
                    "石墨及六套低饱和主题均提供完整深浅调色板和设置入口");
                UniformGrid themeGrid = view.Control<UniformGrid>("ThemeGrid");
                view.Window.UpdateLayout();
                Check(themeGrid.Columns == 3 && themeGrid.Rows == 2 && themeGrid.Children.Count == 6 &&
                    themeGrid.Children.Cast<FrameworkElement>().Select(child => child.ActualWidth).Max() -
                    themeGrid.Children.Cast<FrameworkElement>().Select(child => child.ActualWidth).Min() < 0.1,
                    "六套主题固定为三列两行，名称不挤乱列宽");
                view.OpenSettingsCategory("CustomSectionLabel");
                view.SettingsCategoryScroll.ScrollToEnd(); view.Window.UpdateLayout();
                Render(view, Path.Combine(directory, "settings-language-opacity.png"));
                view.OpenSettingsCategory("LanSectionLabel"); view.Window.UpdateLayout();
                Render(view, Path.Combine(directory, "settings-lan.png"));
                Check(view.Window.FindName("IntervalReminderToggle") == null && view.Window.FindName("DailyReminderToggle") == null &&
                    view.Window.FindName("ReminderIntervalInput") == null, "全局设置彻底删除提醒控件，不再提供统一提醒");
                Check(view.Control<Button>("ResetButton").Content.ToString() == "默认位置大小" && view.Control<Button>("PositionMenuButton").HorizontalAlignment == HorizontalAlignment.Right,
                    "默认位置大小文字与最右侧三点设置入口分离，不再重合");
                view.Control<Button>("PositionMenuButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                view.Window.UpdateLayout(); System.Threading.Thread.Sleep(220); Pump();
                Check(view.OverlayOpen && view.Control<FrameworkElement>("PositionOverlay").Visibility == Visibility.Visible &&
                    view.Control<RadioButton>("CornerTopRight").IsChecked == true && view.Control<RadioButton>("Scale100").IsChecked == true,
                    "只有默认位置大小右侧三点入口打开偏好面板");
                Border positionSurface = view.Control<Border>("OverlaySurface");
                Check(positionSurface.HorizontalAlignment == HorizontalAlignment.Stretch && positionSurface.VerticalAlignment == VerticalAlignment.Stretch &&
                    positionSurface.Margin == new Thickness(0) && positionSurface.BorderThickness == new Thickness(0) &&
                    view.Control<Grid>("OverlayLayer").Background == Brushes.Transparent,
                    "默认位置与大小作为整张卡片的全屏页面显示，不保留灰色背景和居中弹窗边框");
                Border positionMonitor = view.Control<Border>("PositionMonitor"), positionWallpaper = view.Control<Border>("PositionWallpaper");
                Check(positionMonitor.ActualWidth <= 200.1 && positionMonitor.ActualHeight >= 119 && positionWallpaper.Clip is RectangleGeometry &&
                    positionMonitor.CornerRadius.TopLeft == 16 && positionWallpaper.CornerRadius.TopLeft == 16 &&
                    ((RectangleGeometry)positionWallpaper.Clip).RadiusX == 16 && view.Control<System.Windows.Shapes.Ellipse>("PositionMarkerTopLeft").Margin.Left == 14 &&
                    view.Control<System.Windows.Shapes.Ellipse>("PositionMarkerBottomRight").Margin.Right == 14,
                    "电脑屏幕与壁纸使用统一圆角，四个定位圆点进一步向内完整留白");
                Grid closeGlyph = view.Control<Button>("PositionCloseButton").Content as Grid;
                System.Windows.Shapes.Path closePath = closeGlyph == null ? null : closeGlyph.Children.OfType<System.Windows.Shapes.Path>().FirstOrDefault();
                Check(closeGlyph != null && closeGlyph.Width == 18 && closeGlyph.Height == 18 && closePath != null &&
                    closePath.HorizontalAlignment == HorizontalAlignment.Center && closePath.VerticalAlignment == VerticalAlignment.Center,
                    "默认位置页关闭键使用固定矢量叉号并在按钮中水平垂直居中");
                Check(new[] { "Scale75", "Scale100", "Scale125", "Scale150", "Scale175", "Scale200", "Scale225", "Scale250" }
                    .All(name => view.Control<RadioButton>(name) != null), "默认比例提供 75% 到 250% 的八档选择");
                view.Control<RadioButton>("CornerTopLeft").IsChecked = true;
                Check(view.Control<System.Windows.Shapes.Ellipse>("PositionMarkerTopLeft").Fill == view.Window.Resources["AccentBrush"] &&
                    view.Control<System.Windows.Shapes.Ellipse>("PositionMarkerBottomRight").Fill == Brushes.White,
                    "点击左上、右上、左下或右下时，对应屏幕圆点实时变为主题色");
                Render(view, Path.Combine(directory, "position-size.png"));
                Button restorePosition = view.Control<Button>("PositionRestoreButton"), savePosition = view.Control<Button>("PositionSaveButton");
                System.Windows.Point restoreOrigin = restorePosition.TranslatePoint(new System.Windows.Point(0, 0), view.Control<ScrollViewer>("PositionOverlay"));
                System.Windows.Point saveOrigin = savePosition.TranslatePoint(new System.Windows.Point(0, 0), view.Control<ScrollViewer>("PositionOverlay"));
                Check(Math.Abs(restoreOrigin.Y - saveOrigin.Y) < 0.1 && restorePosition.ActualHeight == savePosition.ActualHeight &&
                    restorePosition.Margin == new Thickness(0) && savePosition.Margin == new Thickness(0),
                    "恢复默认和保存按钮使用相同高度、边距并处在同一水平线上");
                view.Control<RadioButton>("Scale125").IsChecked = true;
                view.Control<Button>("PositionSaveButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(!view.OverlayOpen && viewState.DefaultCorner == "TopLeft" && viewState.DefaultScalePercent == 125 &&
                    viewState.Width == 360 && viewState.Height == 520, "偏好面板保存只更新默认偏好，不立即移动或缩放卡片");
                view.Control<Button>("ResetButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(viewState.Width == 450 && viewState.Height == 650 && !view.OverlayOpen,
                    "点击默认位置大小主体区域直接应用已保存的位置和比例，不打开偏好面板");
                view.OpenPositionOverlay(); view.Control<RadioButton>("Scale250").IsChecked = true;
                view.Control<Button>("PositionSaveButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                view.Control<Button>("ResetButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(viewState.DefaultScalePercent == 250 && viewState.Width == 900 && viewState.Height == 1300,
                    "250% 默认比例可保存，并在点击主体区域后应用到最大卡片尺寸");
                view.Window.Width = 360; view.Window.Height = 520; viewState.Width = 360; viewState.Height = 520;
                view.OpenPositionOverlay(); view.Control<Button>("PositionRestoreButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(view.Control<RadioButton>("CornerTopRight").IsChecked == true && view.Control<RadioButton>("Scale100").IsChecked == true,
                    "位置大小面板的恢复默认选择右上和 100%");
                view.Control<Button>("PositionCloseButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Render(view, Path.Combine(directory, "settings-dark.png"));
                view.Control<ScrollViewer>("SettingsScroll").ScrollToEnd(); view.Control<ScrollViewer>("SettingsScroll").UpdateLayout();
                view.Window.Width = 320; view.Window.Height = 360;
                Render(view, Path.Combine(directory, "settings-compact.png"));
                view.OpenPositionOverlay(); System.Threading.Thread.Sleep(220); Pump(); Render(view, Path.Combine(directory, "position-size-compact.png"));
                view.Control<ScrollViewer>("PositionOverlay").ScrollToEnd(); Render(view, Path.Combine(directory, "position-size-compact-bottom.png"));
                ScrollViewer positionViewport = view.Control<ScrollViewer>("PositionOverlay");
                Check(view.Control<Button>("PositionSaveButton").Visibility == Visibility.Visible && view.Control<Button>("PositionSaveButton").ActualHeight >= 33 &&
                    view.Control<Button>("PositionRestoreButton").Visibility == Visibility.Visible && view.Control<Button>("PositionRestoreButton").ActualHeight >= 33 &&
                    new[] { "PositionSaveButton", "PositionRestoreButton" }.All(name => {
                        Button button = view.Control<Button>(name);
                        Rect bounds = button.TransformToAncestor(positionViewport).TransformBounds(new Rect(button.RenderSize));
                        return bounds.Top >= 0 && bounds.Bottom <= positionViewport.ActualHeight + .5; }),
                    "最小卡片的位置大小面板可滚动到底部，恢复默认和保存按钮完整可见：" + String.Join(";", new[] { "PositionSaveButton", "PositionRestoreButton" }.Select(name => {
                        Button button = view.Control<Button>(name);
                        return name + " height=" + button.ActualHeight + " bounds=" + button.TransformToAncestor(positionViewport).TransformBounds(new Rect(button.RenderSize)) + " viewport=" + positionViewport.ActualHeight; })));
                Check(view.Control<Border>("Card").Clip == null && view.Control<Grid>("CardContent").Clip is RectangleGeometry &&
                    ((RectangleGeometry)view.Control<Grid>("CardContent").Clip).RadiusX == 17 &&
                    view.Control<FrameworkElement>("OverlayLayer").Margin.Left == -1,
                    "设置遮罩始终裁剪在抗锯齿圆角内并覆盖边线，不出现直角白边");
                view.CloseOverlay();
                view.Control<Button>("SettingsBack").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(!view.SettingsOpen, "设置返回待办");
                view.Window.Width = 360; view.Window.Height = 520;
                TodoItem reminded = viewState.Pending.First(), notReminded = viewState.Pending.Skip(1).First();
                view.OpenItemReminder(reminded);
                Check(view.ReminderOpen && view.Control<TextBlock>("ReminderItemText").Text == reminded.Text && !view.EditOpen && !view.SettingsOpen,
                    "独立提醒在卡片内部编辑，并明确显示当前事项");
                view.Control<CheckBox>("ItemIntervalToggle").IsChecked = true;
                view.Control<CheckBox>("ItemIntervalToggle").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                view.Control<CheckBox>("ItemDailyToggle").IsChecked = true;
                view.Control<CheckBox>("ItemDailyToggle").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                view.Control<TextBox>("ItemReminderHours").Text = "1";
                view.Control<TextBox>("ItemReminderMinutes").Text = "30";
                view.Control<TextBox>("ItemReminderDailyTime").Text = "20:30";
                view.Control<Button>("HoursUpButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                view.Control<Button>("HoursDownButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                view.Control<Button>("MinutesUpButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                view.Control<Button>("MinutesDownButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                view.Control<Button>("DailyTimeUpButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                view.Control<Button>("DailyTimeDownButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(view.Control<TextBox>("ItemReminderHours").Text == "1" && view.Control<TextBox>("ItemReminderMinutes").Text == "30" &&
                    view.Control<TextBox>("ItemReminderDailyTime").Text == "20:30", "提醒输入框右侧上下箭头可加减小时、分钟和每日时间");
                Render(view, Path.Combine(directory, "item-reminder.png"));
                Check(new[] { "ItemReminderHours", "ItemReminderMinutes", "ItemReminderDailyTime" }.All(name =>
                {
                    TextBox input = view.Control<TextBox>(name); input.ApplyTemplate();
                    ScrollViewer host = input.Template.FindName("PART_ContentHost", input) as ScrollViewer;
                    return host != null && host.ActualHeight >= input.FontSize * 1.6;
                }), "提醒单行输入的文字视口保留完整行高，数字不被重复内边距裁切");
                viewState.DarkMode = false; view.ApplyTheme(); Render(view, Path.Combine(directory, "item-reminder-light.png"));
                viewState.DarkMode = true; view.ApplyTheme();
                view.SaveItemReminder();
                Check(!view.ReminderOpen && reminded.ReminderIntervalMinutes == 90 && reminded.DailyReminderMinutesPlusOne == 1231 &&
                    reminded.LastIntervalReminderAt > 0 && notReminded.ReminderIntervalMinutes == 0 && notReminded.DailyReminderMinutesPlusOne == 0,
                    "保存小时、分钟和每日时间只修改这一条事项");
                view.OpenItemReminder(reminded); view.Control<TextBox>("ItemReminderHours").Text = ""; view.Control<TextBox>("ItemReminderMinutes").Text = "30";
                view.SaveItemReminder();
                Check(!view.ReminderOpen && reminded.ReminderIntervalMinutes == 30, "小时留空、只填写分钟时可以保存提醒");
                view.OpenItemReminder(reminded); view.Control<TextBox>("ItemReminderHours").Text = "1"; view.Control<TextBox>("ItemReminderMinutes").Text = "30"; view.SaveItemReminder();
                Check(reminded.ReminderIntervalMinutes == 90, "分钟提醒保存后仍可改回小时和分钟组合");
                view.OpenItemReminder(reminded); view.Control<TextBox>("ItemReminderMinutes").Text = "60"; view.SaveItemReminder();
                Check(view.ReminderOpen && reminded.ReminderIntervalMinutes == 90, "无效分钟值留在编辑页，不覆盖已有提醒");
                view.Control<TextBox>("ItemReminderMinutes").Text = "0"; view.Control<TextBox>("ItemReminderDailyTime").Text = "25:00"; view.SaveItemReminder();
                Check(view.ReminderOpen && reminded.ReminderIntervalMinutes == 90 && reminded.DailyReminderMinutesPlusOne == 1231,
                    "无效每日时间不会部分保存间隔规则");
                view.Control<Button>("ReminderCancelButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                view.OpenItemReminder(reminded);
                Check(view.Control<TextBox>("ItemReminderMinutes").Text == "30" && view.Control<TextBox>("ItemReminderDailyTime").Text == "20:30",
                    "取消提醒编辑丢弃草稿，再次打开恢复保存值");
                view.Window.Width = 320; view.Window.Height = 360; Render(view, Path.Combine(directory, "item-reminder-compact.png"));
                Check(view.Control<TextBox>("ItemReminderHours").ActualWidth >= 48 && view.Control<TextBox>("ItemReminderMinutes").ActualWidth >= 48 &&
                    view.Control<TextBox>("ItemReminderDailyTime").ActualWidth >= 72, "最小卡片的提醒输入采用弹性列宽，小时、分钟和时间不被裁切");
                view.Control<ScrollViewer>("ItemReminderScroll").ScrollToEnd(); Render(view, Path.Combine(directory, "item-reminder-compact-bottom.png"));
                Check(view.Control<Button>("ReminderSaveButton").ActualWidth >= 78 && view.Control<Button>("ReminderClearButton").ActualWidth >= 76,
                    "最小提醒页底部清除、取消、保存按钮完整且固定可见");
                view.Window.Width = 360; view.Window.Height = 520;
                view.Control<Button>("ReminderClearButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(!view.ReminderOpen && reminded.ReminderIntervalMinutes == 0 && reminded.DailyReminderMinutesPlusOne == 0 &&
                    reminded.ReminderSnoozeUntil == 0, "清除提醒同时关闭此事项两种规则和稍后提醒");
                view.Control<Button>("MenuButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                viewState.Locked = true; view.RefreshStatus(); view.RefreshSettingsForTray();
                Check(view.Control<TextBox>("TitleSettingInput").IsEnabled && view.Control<Button>("TitleSaveButton").IsEnabled,
                    "锁定位置和大小后，设置中的软件名称仍可编辑");
                view.Control<TextBox>("TitleSettingInput").Text = "我的计划";
                Render(view, Path.Combine(directory, "title-setting.png"));
                view.Control<Button>("TitleSaveButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(viewState.Title == "我的计划" && view.Control<TextBlock>("TitleLabel").Text == "我的计划" && !view.TitleEditing,
                    "主页名称只在设置页编辑并保存，不占用顶部拖动区域");
                view.Control<TextBox>("TitleSettingInput").Text = "  ";
                view.Control<Button>("TitleSaveButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(viewState.Title == "我的计划", "设置中的空名称不会覆盖已有名称");
                viewState.Locked = false; view.RefreshStatus();
                view.Control<Button>("SettingsBack").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Render(view, Path.Combine(directory, "main-final.png"));
                view.Window.Show(); Pump(); view.InitializeNative(false, false, IntPtr.Zero); Pump();
                StackPanel visibleDragPanel = view.Control<StackPanel>("PendingList");
                Grid visibleDragRow = visibleDragPanel.Children.OfType<Grid>().First(grid => grid.Tag is TodoItem);
                Point visibleDragTop = visibleDragRow.TranslatePoint(new Point(0, 0), visibleDragPanel);
                TaskDragAdorner visibleDragPreview = new TaskDragAdorner(visibleDragPanel, visibleDragRow, visibleDragTop.X, visibleDragTop.Y);
                System.Windows.Documents.AdornerLayer visibleDragLayer = System.Windows.Documents.AdornerLayer.GetAdornerLayer(visibleDragPanel);
                visibleDragLayer.Add(visibleDragPreview); visibleDragPreview.MoveTo(visibleDragTop.Y + 16); Pump();
                visibleDragRow.Opacity = 0;
                Render(view, Path.Combine(directory, "drag-preview.png"));
                visibleDragRow.Opacity = 1;
                Check(visibleDragPreview.Effect == null && TaskDragAdorner.HorizontalContentInset >= 8,
                    "拖动浮层无投影且内容距离左右圆角描边至少 8 DIP");
                int previewStride = visibleDragPreview.Snapshot.PixelWidth * 4;
                byte[] previewPixels = new byte[previewStride * visibleDragPreview.Snapshot.PixelHeight];
                visibleDragPreview.Snapshot.CopyPixels(previewPixels, previewStride, 0);
                PngBitmapEncoder raster = new PngBitmapEncoder(); raster.Frames.Add(BitmapFrame.Create(visibleDragPreview.Snapshot));
                using (FileStream rasterFile = File.Create(Path.Combine(directory, "v139-drag-raster.png"))) raster.Save(rasterFile);
                RenderTargetBitmap drawnPreview = new RenderTargetBitmap(Math.Max(1, (int)visibleDragPanel.ActualWidth),
                    Math.Max(1, (int)visibleDragPanel.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                drawnPreview.Render(visibleDragPreview);
                byte[] drawnPixels = new byte[drawnPreview.PixelWidth * drawnPreview.PixelHeight * 4];
                drawnPreview.CopyPixels(drawnPixels, drawnPreview.PixelWidth * 4, 0);
                Check(previewPixels.Where((value, index) => index % 4 == 3 && value > 0).Count() > 100 &&
                    drawnPixels.Where((value, index) => index % 4 == 3 && value > 0).Count() > 100,
                    "可见窗口中直接拖动的快照和浮层均绘制了事项内容：snapshot=" + previewPixels.Where((value, index) => index % 4 == 3 && value > 0).Count() +
                    ";draw=" + drawnPixels.Where((value, index) => index % 4 == 3 && value > 0).Count() + ";size=" + visibleDragRow.RenderSize +
                    ";bitmap=" + visibleDragPreview.Snapshot.PixelWidth + "x" + visibleDragPreview.Snapshot.PixelHeight);
                visibleDragLayer.Remove(visibleDragPreview);
                view.Input.Focus(); Pump();
                Check(view.Control<TextBlock>("InputHint").Visibility == Visibility.Collapsed,
                    "真实窗口中添加框获得焦点后占位文字隐藏，插入光标不会压住提示字");
                Render(view, Path.Combine(directory, "input-focused.png"));
                viewState.ShowCompactButton = true; viewState.ShowMinimizeButton = true; viewState.ShowCloseButton = true; view.RefreshStatus(); view.Window.UpdateLayout();
                Border cardForButtons = view.Control<Border>("Card"); Button menuForButtons = view.Control<Button>("MenuButton");
                System.Windows.Point menuEdge = menuForButtons.TranslatePoint(new System.Windows.Point(menuForButtons.ActualWidth, 0), cardForButtons);
                Check(menuEdge.X <= cardForButtons.ActualWidth && view.Control<TextBlock>("TitleLabel").ActualWidth > 0,
                    "最小宽度同时显示锁定、最小化、关闭和设置图标时，标题与按钮均不越出卡片");
                Button minimizeButton = view.Control<Button>("MinimizeButton"), closeButton = view.Control<Button>("CloseButton");
                double compactX = view.Control<Button>("CompactButton").TranslatePoint(new Point(), cardForButtons).X;
                double menuX = menuForButtons.TranslatePoint(new Point(), cardForButtons).X;
                double minimizeX = minimizeButton.TranslatePoint(new Point(), cardForButtons).X;
                double closeX = closeButton.TranslatePoint(new Point(), cardForButtons).X;
                Check(compactX < minimizeX && minimizeX < closeX && closeX < menuX,
                    "可选按钮位于设置左侧，更多按钮始终保持最右侧固定锚点");
                FrameworkElement minimizeGlyph = view.Control<FrameworkElement>("MinimizeGlyph"), closeGlyphHeader = view.Control<FrameworkElement>("CloseGlyph");
                Point minimizeCenter = minimizeGlyph.TranslatePoint(new Point(minimizeGlyph.ActualWidth / 2, minimizeGlyph.ActualHeight / 2), minimizeButton);
                Point closeCenterHeader = closeGlyphHeader.TranslatePoint(new Point(closeGlyphHeader.ActualWidth / 2, closeGlyphHeader.ActualHeight / 2), closeButton);
                Check(Math.Abs(minimizeCenter.X - minimizeButton.ActualWidth / 2) < 0.1 && Math.Abs(minimizeCenter.Y - minimizeButton.ActualHeight / 2) < 0.1 &&
                    Math.Abs(closeCenterHeader.X - closeButton.ActualWidth / 2) < 0.1 && Math.Abs(closeCenterHeader.Y - closeButton.ActualHeight / 2) < 0.1,
                    "最小化横线与关闭叉号均在各自按钮内水平垂直居中");
                Render(view, Path.Combine(directory, "header-buttons.png"));
                viewState.ShowMinimizeButton = false; viewState.ShowCloseButton = false; view.RefreshStatus();
                view.OpenEdit(viewState.Pending.First());
                Check(view.Window.FindName("EditExpandButton") == null, "编辑页仅保留取消和保存，删除展开编辑及其窗口放大逻辑");
                view.Control<Button>("EditCancelButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                AppState alignState = new AppState { DesktopMode = false, ThemeId = "ocean", Width = 720, Height = 900, Language = "zh-CN" };
                alignState.Add("123"); alignState.Add("单行事项文字"); alignState.Add("多行事项\n123\n第三行");
                TodoWindow alignView = new TodoWindow(alignState);
                Render(alignView, Path.Combine(directory, "task-alignment.png"));
                Grid alignRow = (Grid)alignView.Control<StackPanel>("PendingList").Children[0];
                Check(alignRow.Children.OfType<TextBlock>().First().Margin.Top == 10 && alignRow.Children.OfType<Button>().First().Margin.Top + 14 == 19,
                    "单行数字和多行事项的首行使用与勾选框、菜单一致的视觉中心");
                alignView.OpenEdit(alignState.Pending.First()); Render(alignView, Path.Combine(directory, "edit-no-expand.png")); alignView.Window.Close();
                ReminderPopup popup = new ReminderPopup(viewState, reminded);
                Check(popup.Window.WindowStyle == WindowStyle.None && popup.Window.AllowsTransparency && popup.Window.FindName("ReminderItems") != null,
                    "提醒使用与主体一致的自定义卡片弹窗而非旧式系统框");
                Check(ReferenceEquals(((Image)popup.Window.FindName("ReminderIcon")).Source, UiAssets.InternalIcon),
                    "提醒弹框左上角与软件主界面共用新的内部 PNG 图标");
                Check(((TextBlock)popup.Window.FindName("ReminderItems")).Text == reminded.Text, "提醒弹窗只展示触发提醒的那一条事项");
                Render(popup.Window, Path.Combine(directory, "reminder.png"));
                Border reminderSurface = (Border)popup.Window.FindName("ReminderItemSurface");
                TextBlock reminderText = (TextBlock)popup.Window.FindName("ReminderItems");
                double surfaceCenter = reminderSurface.TranslatePoint(new Point(0, reminderSurface.ActualHeight / 2), popup.Window).Y;
                double textCenter = reminderText.TranslatePoint(new Point(0, reminderText.ActualHeight / 2), popup.Window).Y;
                Check(Math.Abs(surfaceCenter - textCenter) < 0.6, "单行提醒事项在圆角内容框内精确上下居中");
                popup.Dispose();
                AppState miniState = new AppState { DesktopMode = false, Language = "zh-CN", ThemeId = "ocean",
                    MiniAlwaysOnTop = false, MiniIconOpacityPercent = 75 };
                using (MiniBubble bubble = new MiniBubble(miniState, delegate { }, delegate { }))
                {
                    bubble.Show(); Pump(); System.Threading.Thread.Sleep(240); Pump();
                    Border bubbleSurface = bubble.Surface.Content as Border;
                    Grid bubbleContent = bubbleSurface == null ? null : bubbleSurface.Child as Grid;
                    Image bubbleIcon = bubbleContent == null ? null : bubbleContent.Children.OfType<Image>().FirstOrDefault();
                    Check(bubble.Surface.IsVisible, "悬浮小图标可显示为独立窗口");
                    Check(!bubble.Surface.Topmost, "悬浮小图标遵循关闭始终置顶设置");
                    Check(Math.Abs(bubble.Surface.Width - 62) < 1 && Math.Abs(bubble.Surface.Height - 62) < 1,
                        "悬浮小图标保持 62×62 DIP 紧凑尺寸（实际 " + bubble.Surface.Width + "×" + bubble.Surface.Height + "）");
                    Check(bubbleIcon != null && ReferenceEquals(bubbleIcon.Source, UiAssets.FloatingIcon) && bubbleSurface.Effect == null,
                        "悬浮小图标使用高清透明资源，不带额外方框或投影");
                    Check(bubbleSurface.ToolTip == null, "悬浮图标不再弹出遮挡桌面的操作提示");
                    Check(bubbleIcon.Width >= 60 && UiAssets.FloatingIcon.PixelWidth >= 512,
                        "悬浮图标在小窗口内填充显示，并使用至少 512 像素的源图");
                    Check(Math.Abs(bubble.Surface.Opacity - 0.75) < 0.04,
                        "悬浮小图标使用独立透明度设置（实际 " + bubble.Surface.Opacity.ToString("0.00") + "）");
                    Render(bubble.Surface, Path.Combine(directory, "mini-bubble.png"));
                }
                using (LanAdminWindow adminWindow = new LanAdminWindow(viewState, adminPrefsDir))
                {
                    Check(adminWindow.Window.ShowInTaskbar && adminWindow.Window.MinWidth >= 680 && adminWindow.Window.MinHeight >= 460,
                        "局域网管理员使用可在任务栏找回的独立大窗口，并设置可操作的最小尺寸");
                    Render(adminWindow.Window, Path.Combine(directory, "lan-admin.png"), adminWindow.Window.Height);
                    FrameworkElement adminRoot = (FrameworkElement)adminWindow.Window.Content;
                    adminRoot.Measure(new Size(780, 420)); adminRoot.Arrange(new Rect(0, 0, 780, 420)); adminRoot.UpdateLayout();
                    double connectBottom = adminWindow.ConnectButton.TranslatePoint(new Point(0, adminWindow.ConnectButton.ActualHeight), adminRoot).Y;
                    double statusTop = adminWindow.StatusText.TranslatePoint(new Point(), adminRoot).Y;
                    Check(connectBottom + 4 <= statusTop && adminWindow.ConnectButton.ActualHeight >= 35,
                        "局域网管理窗口在较短高度下完整显示连接按钮，不与状态行重叠");
                    adminWindow.EditCompletedCheckBox.ApplyTemplate();
                    Border adminCheckBox = adminWindow.EditCompletedCheckBox.Template.FindName("Box", adminWindow.EditCompletedCheckBox) as Border;
                    Check(adminCheckBox != null && adminCheckBox.CornerRadius.TopLeft == 5 && adminCheckBox.Width == 18,
                        "局域网已完成选项使用与应用一致的圆角勾选框");
                    adminWindow.LoadPeersForTest(new[] {
                        new LanPeer { InstanceId = "test-user", DisplayName = "很长很长的原始名称用来验证自动换行", MachineName = "WORKSTATION-123" },
                        new LanPeer { InstanceId = "long-user", DisplayName = "一个很长很长的局域网用户名用来检查左侧列表自动换行不被截断",
                            MachineName = "VERY-LONG-WORKSTATION-NAME" },
                        new LanPeer { InstanceId = "hidden-user", DisplayName = "暂时隐藏", MachineName = "QA" }
                    }, true);
                    adminWindow.UserList.SelectedItem = adminWindow.UserList.Items.Cast<LanPeer>().Single(item => item.InstanceId == "test-user");
                    Check(adminWindow.UserList.Items.Count == 2 && adminWindow.HiddenUserList.Items.Count == 1 &&
                        adminWindow.CodeInput.Text == "SAFE1234" &&
                        adminWindow.UserList.Items.Cast<LanPeer>().Any(item => item.DisplayTitle == "设计组"),
                        "扫描结果遵守本机重命名/隐藏偏好，已验证口令自动填入");
                    Check(adminWindow.UserList.ContextMenu != null && adminWindow.HiddenUserList.ContextMenu != null,
                        "用户列表首次右键即可打开操作菜单，隐藏用户提供恢复入口");
                    adminWindow.ApplySnapshotForTest(new LanSnapshot { Revision = 9,
                        Groups = new List<LanGroupInfo> { new LanGroupInfo { Id = "work", Name = "项目" } },
                        Tasks = new List<LanTaskInfo> {
                            new LanTaskInfo { Id = "a", Text = "远程待办内容", GroupId = "work", CreatedAt = DateTime.UtcNow.Ticks },
                            new LanTaskInfo { Id = "b", Text = "普通待办", CreatedAt = DateTime.UtcNow.Ticks }
                        } });
                    Check(adminWindow.PendingRows.Children.OfType<Grid>().Count(remoteRow => remoteRow.Tag is LanTaskInfo) == 2 &&
                        adminWindow.PendingRows.Children.OfType<Grid>().Any(remoteRow => remoteRow.Tag == null),
                        "管理员右侧直接以主页式分组、勾选框和事项菜单展示用户待办");
                    Render(adminWindow.Window, Path.Combine(directory, "lan-admin-populated.png"), adminWindow.Window.Height);
                    Grid addGrid = adminWindow.AddTextInput.Parent as Grid;
                    TextBlock hint = addGrid.Children.OfType<TextBlock>().Single();
                    FrameworkElement adminPlusGlyph = adminWindow.AddTaskButton.Content as FrameworkElement;
                    double inputCenter = adminWindow.AddTextInput.TranslatePoint(new Point(0, adminWindow.AddTextInput.ActualHeight / 2), addGrid).Y;
                    double hintCenter = hint.TranslatePoint(new Point(0, hint.ActualHeight / 2), addGrid).Y;
                    double buttonCenter = adminWindow.AddTaskButton.TranslatePoint(new Point(0, adminWindow.AddTaskButton.ActualHeight / 2), addGrid).Y;
                    Check(adminPlusGlyph is Grid && Math.Abs(hintCenter - inputCenter) <= 1 && Math.Abs(buttonCenter - inputCenter) <= 1,
                        "局域网新增提示、输入框和几何加号在同一水平中线，不依赖全角字形基线");
                    adminWindow.AddTextInput.Text = "第一行\n第二行\n第三行";
                    Render(adminWindow.Window, Path.Combine(directory, "review-admin-multiline.png"), adminWindow.Window.Height);
                    Check(adminWindow.AddTextInput.ActualHeight <= 70 && adminWindow.AddTextInput.AcceptsReturn,
                        "局域网新增多行文字保持完整输入和受控高度，不挤掉列表");
                    adminWindow.AddTextInput.Clear();
                    adminWindow.Window.Width = 680; adminWindow.Window.Height = 460;
                    Render(adminWindow.Window, Path.Combine(directory, "lan-admin-min.png"), 421, 648);
                    adminWindow.HiddenToggleButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump();
                    Render(adminWindow.Window, Path.Combine(directory, "lan-admin-min-hidden.png"), 421, 648);
                    adminWindow.OpenEditForTest(new LanTaskInfo { Id = "a", Text = "远程待办内容", GroupId = "work" });
                    Render(adminWindow.Window, Path.Combine(directory, "lan-admin-min-editor.png"), 421, 648);
                    double saveBottom = adminWindow.EditSaveButton.TranslatePoint(
                        new Point(0, adminWindow.EditSaveButton.ActualHeight), adminWindow.EditLayer).Y;
                    Check(adminWindow.EditSaveButton.ActualHeight >= 35 &&
                        saveBottom <= adminWindow.EditLayer.ActualHeight - 4,
                        "局域网管理员最小窗口的编辑弹层完整显示保存按钮");
                }
                ReminderPopup longPopup = new ReminderPopup(viewState, new TodoItem { Text = String.Join("\n", Enumerable.Repeat("长内容事项提醒，确保内容可滚动且底部按钮不会被挤走。", 15)) });
                Render(longPopup.Window, Path.Combine(directory, "reminder-long.png")); longPopup.Dispose();
                FrameworkElement root = (FrameworkElement)view.Window.Content;
                Thumb grip = view.Control<Thumb>("ResizeThumb");
                System.Windows.Point gripPoint = grip.TranslatePoint(new System.Windows.Point(grip.ActualWidth / 2, grip.ActualHeight / 2), root);
                DependencyObject hit = root.InputHitTest(gripPoint) as DependencyObject;
                while (hit != null && hit != grip) hit = VisualTreeHelper.GetParent(hit);
                Check(hit == grip && Math.Abs(grip.ActualWidth - 26) < 1, "缩放手柄在父布局内可点击，点击范围约 26 DIP");
                Expander completedSection = view.Control<Expander>("CompletedSection"); completedSection.ApplyTemplate();
                ToggleButton completedToggle = completedSection.Template.FindName("HeaderToggle", completedSection) as ToggleButton;
                if (completedToggle != null) completedToggle.ApplyTemplate();
                System.Windows.Shapes.Path completedArrow = completedToggle == null ? null : completedToggle.Template.FindName("Arrow", completedToggle) as System.Windows.Shapes.Path;
                Check(completedToggle != null && completedToggle.FocusVisualStyle == null && completedArrow != null && completedArrow.Data != null && completedArrow.Margin.Top == 0 && completedArrow.Margin.Bottom == 0,
                    "已完成折叠箭头使用居中矢量图且无焦点框");
                FrameworkElement dragHeader = view.Control<FrameworkElement>("DragHeader");
                Native.Rect heldBefore; Native.PhysicalWindowRect(view.Desktop.Handle, out heldBefore);
                dragHeader.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
                Pump(); Native.Rect heldAfter; Native.PhysicalWindowRect(view.Desktop.Handle, out heldAfter);
                Check(!view.MovePreviewActive && view.Control<Border>("Card").Opacity == 1 && SameRect(heldBefore, heldAfter),
                    "标题左键按下但尚未移动时不创建预览、不隐藏卡片、不改变窗口尺寸");
                dragHeader.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
                Pump();
                Native.Rect moveRect; Native.GetWindowRect(new WindowInteropHelper(view.Window).Handle, out moveRect);
                bool movePreviewStarted = view.BeginMovePreview(moveRect); Pump();
                IntPtr movePreview = view.MovePreviewHandle;
                long moveStyle = Native.GetWindowLongPtr(movePreview, Native.GWL_STYLE).ToInt64();
                long moveExStyle = Native.GetWindowLongPtr(movePreview, Native.GWL_EXSTYLE).ToInt64();
                Check(movePreviewStarted && view.MovePreviewActive && movePreview != new WindowInteropHelper(view.Window).Handle &&
                    Native.GetParent(movePreview) == IntPtr.Zero && (moveStyle & Native.WS_CHILD) == 0 &&
                    (moveExStyle & Native.WS_EX_NOACTIVATE) != 0 && (moveExStyle & Native.WS_EX_TRANSPARENT) != 0 &&
                    view.Control<Border>("Card").Opacity == 0 && grip.Opacity == 0,
                    "拖动和缩放预览是本进程独立顶层窗口，不在移动中操作 Explorer 子窗口");
                Check(HwndSource.FromHwnd(movePreview) == null && Native.ClassName(movePreview) == "DesktopTodo.PhysicalGesturePreview",
                    "预览是原生固定像素 HWND，不是会自动缩放的第二个 WPF Window");
                Native.Rect previewTarget = new Native.Rect { Left = moveRect.Left + 17, Top = moveRect.Top + 13, Right = moveRect.Right + 41, Bottom = moveRect.Bottom + 29 };
                view.UpdateMovePreview(previewTarget); Pump();
                Native.Rect previewActual; Native.GetWindowRect(movePreview, out previewActual);
                Check(previewActual.Left == previewTarget.Left && previewActual.Top == previewTarget.Top && previewActual.Right == previewTarget.Right && previewActual.Bottom == previewTarget.Bottom,
                    "独立预览按屏幕像素即时移动和缩放");
                view.DisposeMovePreview(); Pump();
                Check(!view.MovePreviewActive && !Native.IsWindow(movePreview) && view.Control<Border>("Card").Opacity == 1 && grip.Opacity == 1,
                    "预览结束后释放 HWND 并恢复实时卡片");
                Native.Rect allEdgeAnchor = new Native.Rect { Left = 100, Top = 100, Right = 500, Bottom = 620 };
                System.Drawing.Rectangle allEdgeWork = new System.Drawing.Rectangle(0, 0, 1200, 1000);
                Native.Rect fromLeft = TodoWindow.ResizeGeometry(allEdgeAnchor, new Native.Point { X = 100, Y = 100 }, new Native.Point { X = 60, Y = 100 }, 1, allEdgeWork, "Left");
                Native.Rect fromTopRight = TodoWindow.ResizeGeometry(allEdgeAnchor, new Native.Point { X = 500, Y = 100 }, new Native.Point { X = 540, Y = 70 }, 1, allEdgeWork, "TopRight");
                Check(fromLeft.Left == 84 && fromLeft.Right == 500 && fromTopRight.Top == 70 && fromTopRight.Right == 540 &&
                    view.Window.FindName("ResizeTop") != null && view.Window.FindName("ResizeBottomLeft") != null,
                    "任意边和四角均可实时调整窗口，过宽时收敛到可阅读比例");
                bool invisibleResizeChrome = true;
                foreach (string handleName in new[] { "ResizeTop", "ResizeBottom", "ResizeLeft", "ResizeRight", "ResizeTopLeft", "ResizeTopRight", "ResizeBottomLeft" })
                {
                    Thumb edge = view.Control<Thumb>(handleName); edge.ApplyTemplate();
                    Border edgeSurface = VisualTreeHelper.GetChildrenCount(edge) > 0 ? VisualTreeHelper.GetChild(edge, 0) as Border : null;
                    invisibleResizeChrome &= edgeSurface != null && edgeSurface.Background == Brushes.Transparent;
                }
                Check(invisibleResizeChrome, "七个边缘缩放热区使用透明模板，四角不再出现白色方块");
                view.Window.Width = 720; view.Window.Height = 1100; Pump();
                ScaleTransform largeTransform = view.Control<Grid>("CardContent").LayoutTransform as ScaleTransform;
                Check(view.ContentScale == 1.8 && largeTransform != null && largeTransform.ScaleX == 1.8 && largeTransform.ScaleY == 1.8,
                    "大卡片将文字、图标、按钮与间距一起放大到 180%，不只扩大空白区域");
                Render(view, Path.Combine(directory, "large.png"));
                Check(view.Control<Border>("ProgressFill").ActualWidth <= view.Control<Border>("ProgressTrack").ActualWidth + 1,
                    "自适应放大后进度条按内容宽度计算，不溢出轨道");
                view.Control<Button>("MenuButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Pump(); System.Threading.Thread.Sleep(230); Pump();
                Render(view, Path.Combine(directory, "settings-large.png"));
                view.Control<Button>("SettingsBack").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                view.OpenEdit(viewState.Pending.First()); Render(view, Path.Combine(directory, "edit-large.png"));
                view.Control<Button>("EditCancelButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                view.OpenItemReminder(reminded);
                view.Control<CheckBox>("ItemIntervalToggle").IsChecked = true; view.Control<CheckBox>("ItemIntervalToggle").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                view.Control<CheckBox>("ItemDailyToggle").IsChecked = true; view.Control<CheckBox>("ItemDailyToggle").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Render(view, Path.Combine(directory, "item-reminder-large.png"));
                view.Control<Button>("ReminderCancelButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                view.Window.Width = 360; view.Window.Height = 520; Pump();
                Check(view.ContentScale == 1 && TodoWindow.AdaptiveContentScale(320, 360) == 1 && TodoWindow.AdaptiveContentScale(720, 360) == 1,
                    "默认和短卡片不缩小字号，恢复小窗口时不会裁切大控件");
                view.Window.Close();
                AppState motionState = new AppState { Language = "zh-CN", DesktopMode = false, ThemeId = "ocean" };
                TodoWindow motionView = new TodoWindow(motionState);
                motionView.Window.Show(); Pump();
                motionView.SettingsOpen = true; Pump(); System.Threading.Thread.Sleep(230); Pump();
                motionView.OpenSettingsCategory("BasicSectionLabel"); Pump();
                Check(motionView.CategoryDialogOpen && motionView.SettingsCategoryBackdrop.HasAnimatedProperties,
                    "可见设置分类小窗口进入时使用透明度过渡");
                System.Threading.Thread.Sleep(230); Pump();
                string motionScreenshot = Path.Combine(directory, "settings-motion-open.png");
                Render(motionView, motionScreenshot);
                using (System.Drawing.Bitmap corners = new System.Drawing.Bitmap(motionScreenshot))
                {
                    int width = corners.Width, height = corners.Height;
                    int inset = (int)Math.Round(8 * corners.HorizontalResolution / 96.0);
                    int[][] samples = { new[] { 24, 2 }, new[] { 2, 24 }, new[] { width - 25, 2 },
                        new[] { width - 3, 24 }, new[] { 24, height - 3 }, new[] { 2, height - 25 },
                        new[] { width - 25, height - 3 }, new[] { width - 3, height - 25 } };
                    Check(samples.All(point => { int x = point[0] < width / 2 ? point[0] + inset : point[0] - inset;
                        int y = point[1] < height / 2 ? point[1] + inset : point[1] - inset;
                        System.Drawing.Color pixel = corners.GetPixel(x, y);
                        return pixel.A > 180 && pixel.R < 200 && pixel.G < 200 && pixel.B < 200; }),
                        "分类弹框遮罩覆盖卡片四角，不露出亮白色弧边");
                }
                CheckBox animatedToggle = motionView.Control<CheckBox>("DarkToggle");
                motionView.OpenSettingsCategory("CustomSectionLabel"); Pump();
                animatedToggle.ApplyTemplate();
                System.Windows.Shapes.Ellipse animatedKnob = animatedToggle.Template.FindName("Knob", animatedToggle) as System.Windows.Shapes.Ellipse;
                Border animatedTrack = animatedToggle.Template.FindName("ActiveTrack", animatedToggle) as Border;
                animatedToggle.IsChecked = true; Pump(); System.Threading.Thread.Sleep(220); Pump();
                Check(animatedKnob != null && animatedTrack != null &&
                    Math.Abs(((TranslateTransform)animatedKnob.RenderTransform).X - 14) < 0.5 && animatedTrack.Opacity > 0.95,
                    "设置开关打开时滑块平移并渐显主题色轨道");
                animatedToggle.IsChecked = false; Pump(); System.Threading.Thread.Sleep(220); Pump();
                Check(Math.Abs(((TranslateTransform)animatedKnob.RenderTransform).X) < 0.5 && animatedTrack.Opacity < 0.05,
                    "设置开关关闭时滑块与轨道反向平滑过渡");
                motionView.SettingsCategoryBackdrop.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                    { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
                Check(motionView.CategoryDialogOpen, "可见分类小窗口退出时先播放动画");
                Check(PumpUntil(delegate { return !motionView.CategoryDialogOpen; }, 800) && motionView.SettingsOpen,
                    "设置分类小窗口退出动画完成后返回总览");
                motionView.OpenSettingsCategory("BasicSectionLabel"); Pump();
                motionView.SettingsCategoryBackdrop.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                    { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
                motionView.OpenSettingsCategory("ThemeLabel"); Pump(); System.Threading.Thread.Sleep(230); Pump();
                Check(motionView.CategoryDialogOpen && motionView.SettingsCategoryScroll.Content is StackPanel &&
                    ((StackPanel)motionView.SettingsCategoryScroll.Content).Children.Contains(motionView.Control<UniformGrid>("ThemeGrid")),
                    "关闭动画未结束就打开另一分类时不会被旧动画误关掉");
                motionView.Window.Width = 720; motionView.Window.Height = 1100; Pump();
                string largeMotionScreenshot = Path.Combine(directory, "settings-motion-large.png");
                Render(motionView, largeMotionScreenshot);
                Check(motionView.ContentScale == 1.8 && motionView.CategoryDialogOpen &&
                    NoBrightCornerSeams(largeMotionScreenshot),
                    "放大后的卡片四角无白边，分类弹框与遮罩完整显示");
                motionView.Window.Close();
                CheckReviewLayouts(directory);
                CheckFeishu36(directory, fixture);
                CheckFeishu37(directory, fixture);
                CheckFeishu38(directory, fixture);
                CheckUnifiedDrag39(directory, fixture);
                CheckFeishu40(directory, fixture);
                CheckSecondaryStartup41(directory, fixture);
                CheckResizeMath();
                CheckPhysicalPreview();
                CheckNativeModes(app, fixture);
                report.AppendLine("\nAll " + passed + " checks passed. QA fixtures: " + fixture);
                File.WriteAllText(Path.Combine(directory, "test-report.txt"), report.ToString(), Encoding.UTF8);
                return 0;
            }
            catch (Exception e)
            {
                report.AppendLine(e.ToString());
                File.WriteAllText(Path.Combine(directory, "test-report.txt"), report.ToString(), Encoding.UTF8);
                return 1;
            }
        }
        private static void CheckAppearance42(string directory)
        {
            AppState appearance = new AppState { Language = "zh-CN", DesktopMode = false, Locked = false,
                ThemeId = "graphite", Width = 440, Height = 636, PendingSort = "Manual" };
            TodoGroup work = new TodoGroup { Name = "工作", SortOrder = 0 };
            TodoGroup life = new TodoGroup { Name = "生活", SortOrder = 1 };
            appearance.Groups.Add(work); appearance.Groups.Add(life);
            TodoItem item = appearance.Add("整理今天的工作安排", work.Id);
            appearance.Add("留一点时间读书", life.Id);
            appearance.Toggle(appearance.Add("把桌面整理干净", life.Id));
            appearance.Validate();
            TodoWindow ui = new TodoWindow(appearance);
            ui.IsSaved = true;
            ui.Changed = delegate { ui.IsSaved = true; };
            try
            {
                ui.Window.Show(); AdvanceFrames(250);
                Check(ui.Window.FontSize == 12.5 && ui.Window.FontFamily.Source.Contains("Segoe UI Variable") &&
                    ui.Control<Border>("Card").Margin == new Thickness(8) && ui.Control<Border>("Card").Effect == null,
                    "正式版采用批准的字体与独立阴影，不把阴影效果加到文本和动画子树");
                Check(ThemeCatalog.Normalize("graphite") == "graphite" && new AppState().ThemeId == "forest" &&
                    ThemeCatalog.Normalize("unknown-old-theme") == "forest", "新增石墨主题不改变旧配色 ID、首次默认或旧数据回退规则");
                foreach (ThemePalette palette in ThemeCatalog.All)
                foreach (bool dark in new[] { false, true })
                {
                    ui.SettingsOpen = true; AdvanceFrames(230); ui.OpenSettingsCategory("ThemeLabel"); AdvanceFrames(230);
                    RadioButton choice = ui.Control<RadioButton>("Theme" + Char.ToUpperInvariant(palette.Id[0]) + palette.Id.Substring(1));
                    choice.IsChecked = true; choice.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    CheckBox mode = ui.Control<CheckBox>("DarkToggle"); mode.IsChecked = dark;
                    mode.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    string[] colors = dark ? palette.Dark : palette.Light;
                    string accentText = ThemeCatalog.AccentText(palette.Id, dark);
                    Check(appearance.ThemeId == palette.Id && appearance.DarkMode == dark &&
                        ((SolidColorBrush)ui.Window.Resources["CardBrush"]).Color == (Color)ColorConverter.ConvertFromString(colors[0]),
                        palette.Name + (dark ? "深色" : "浅色") + "通过真实主题和深色开关切换，无预览专用逻辑");
                    Check(ThemeCatalog.Contrast(colors[0], colors[1]) >= 7 && ThemeCatalog.Contrast(colors[0], colors[2]) >= 4.5 &&
                        ThemeCatalog.Contrast(colors[5], accentText) >= 4.5, palette.Name + (dark ? "深色" : "浅色") + "正文、辅助文字和主按钮对比度达标");
                    Check(((SolidColorBrush)ui.Control<Button>("EditSaveButton").Foreground).Color == (Color)ColorConverter.ConvertFromString(accentText),
                        "主按钮文字跟随 " + palette.Id + (dark ? " dark" : " light") + " 的强调色亮度");
                    ResourceDictionary admin = LanAdminWindowLegacy.AdminResources(colors);
                    Check(((SolidColorBrush)admin["AdminAccentText"]).Color == (Color)ColorConverter.ConvertFromString(accentText),
                        "管理员按钮和勾选标记使用同一可读前景色");
                    using (ReminderPopup reminder = new ReminderPopup(appearance, item))
                    {
                        Check(((SolidColorBrush)reminder.Window.FindName("DismissButton").As<Button>().Foreground).Color ==
                            (Color)ColorConverter.ConvertFromString(accentText), "提醒弹框深浅主题前景与主界面一致");
                        if (palette.Id == "graphite") Render(reminder.Window, Path.Combine(directory, "v142-reminder-" + (dark ? "dark" : "light") + ".png"));
                    }
                    AdvanceFrames(260);
                    Render(ui, Path.Combine(directory, "v142-theme-" + palette.Id + (dark ? "-dark" : "-light") + ".png"));
                    ui.SettingsOpen = false; AdvanceFrames(230);
                    Render(ui, Path.Combine(directory, "v142-home-" + palette.Id + (dark ? "-dark" : "-light") + ".png"));
                }
                appearance.ThemeId = "graphite"; appearance.DarkMode = false; ui.ApplyTheme();
                ui.OpenEdit(item); AdvanceFrames(230); Render(ui, Path.Combine(directory, "v142-edit.png"));
                ui.Control<Button>("EditCancelButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                ui.Window.Width = 360; ui.Window.Height = 520; ui.RefreshImportedState(); AdvanceFrames(230);
                Render(ui, Path.Combine(directory, "v142-default-size.png"));
                Check(!ui.Control<TextBlock>("StatusLabel").Text.Contains("预览"), "正式版保留真实保存状态，未混入演示数据提示");
            }
            finally { ui.Window.Close(); }
        }
        private static void CheckSettingsResponsiveness46(Application app, string fixture, string directory)
        {
            Window host = new Window { Width = 1000, Height = 900, WindowStyle = WindowStyle.None, ShowInTaskbar = false };
            IntPtr oldSurface = Native.TestSurface;
            Controller controller = null;
            var started = new System.Threading.ManualResetEvent(false);
            var release = new System.Threading.ManualResetEvent(false);
            var finished = new System.Threading.ManualResetEvent(false);
            try
            {
                host.Show(); Native.TestSurface = new WindowInteropHelper(host).Handle;
                AppState state = new AppState { Language = "zh-CN", DesktopMode = false, HideTray = true, Width = 640, Height = 800 };
                for (int i = 0; i < 100; i++) state.Add("设置流畅度独立测试 " + i);
                Storage store = new Storage(Path.Combine(fixture, "settings-save")); store.Save(state);
                byte[] initialFile = File.ReadAllBytes(store.DataPath);
                controller = new Controller(app, store, state, false); controller.Start(); Pump();
                TodoWindow ui = controller.CurrentUi;
                int startupReads = 0;
                ui.GetStartup = delegate { startupReads++; return false; };
                ui.SettingsOpen = true; ui.OpenSettingsCategory("CustomSectionLabel"); Pump();
                Grid[] rows = ui.Control<StackPanel>("PendingList").Children.OfType<Grid>().ToArray();
                object selectorItem = ui.Control<ComboBox>("SecondaryDisplaySelect").Items[0];
                Action<string, bool> toggle = delegate(string name, bool value)
                { CheckBox check = ui.Control<CheckBox>(name); check.IsChecked = value; check.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); };
                toggle("ShowTimesToggle", false); ui.Window.UpdateLayout();
                Check(rows.SequenceEqual(ui.Control<StackPanel>("PendingList").Children.OfType<Grid>()) &&
                    rows.SelectMany(r => r.Children.OfType<TextBlock>()).Where(t => Object.Equals(t.Tag, "ItemTime")).All(t => t.Visibility == Visibility.Collapsed),
                    "时间显示开关原地更新全部 100 条事项，不拆卸或重建待办控件");
                toggle("ShowTimesToggle", true); toggle("ShowDateToggle", true);
                Check(rows.SelectMany(r => r.Children.OfType<TextBlock>()).Where(t => Object.Equals(t.Tag, "ItemTime")).All(t => t.Text.Contains("/")),
                    "日期显示开关原地更新时间文字，保留所有待办行实例");
                toggle("DarkToggle", true); ui.Window.UpdateLayout();
                Check(rows.SequenceEqual(ui.Control<StackPanel>("PendingList").Children.OfType<Grid>()),
                    "切换深色主题保留全部待办控件，通过动态资源刷新文字和颜色");
                Check(rows.SelectMany(r => r.Children.OfType<TextBlock>()).All(t => Object.ReferenceEquals(t.Foreground,
                    ui.Window.Resources[Object.Equals(t.Tag, "ItemTime") ? "MutedBrush" : "TextBrush"])),
                    "保留的待办文字和时间画刷实际跟随新的深色主题，不停留在旧颜色");
                object accent = ui.Window.Resources["AccentBrush"];
                for (int i = 70; i <= 90; i++) ui.Control<Slider>("WindowOpacitySlider").Value = i;
                Check(Object.ReferenceEquals(accent, ui.Window.Resources["AccentBrush"]) && Math.Abs(ui.Control<Border>("Card").Opacity - .90) < .001,
                    "透明度滑块只更新卡片透明度，不重复创建主题画刷");
                Check(startupReads == 1 && Object.ReferenceEquals(selectorItem, ui.Control<ComboBox>("SecondaryDisplaySelect").Items[0]),
                    "普通设置点击和连续滑块操作不重复读自启注册表或重建副屏选项");
                Check(ui.IsSavePending && !ui.IsSaved && initialFile.SequenceEqual(File.ReadAllBytes(store.DataPath)),
                    "连续设置修改立即显示保存中，合并窗口内不在界面线程写盘");
                Check(ui.Control<TextBlock>("StatusLabel").Text.Contains("正在保存") &&
                    Object.ReferenceEquals(ui.Control<System.Windows.Shapes.Ellipse>("StatusDot").Fill, ui.Window.Resources["MutedBrush"]),
                    "正常后台保存使用中性状态提示，不闪烁红色保存失败标志");
                controller.Tick();
                Check(ui.IsSavePending && initialFile.SequenceEqual(File.ReadAllBytes(store.DataPath)),
                    "两秒维护计时器不会抢在合并保存前执行同步写盘");
                int writes = 0, workerThread = 0, uiThread = System.Threading.Thread.CurrentThread.ManagedThreadId;
                controller.SettingsSnapshotWriterForTest = delegate(byte[] bytes)
                {
                    workerThread = System.Threading.Thread.CurrentThread.ManagedThreadId;
                    started.Set();
                    if (!release.WaitOne(5000)) throw new TimeoutException("QA stalled writer timed out");
                    store.SaveSnapshot(bytes); System.Threading.Interlocked.Increment(ref writes); finished.Set();
                };
                controller.BeginSettingsSave();
                Check(started.WaitOne(2000) && workerThread != uiThread, "设置文件写入实际运行于后台线程");
                typeof(Controller).GetField("lastSaveAttempt", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .SetValue(controller, DateTime.UtcNow.AddSeconds(-11));
                controller.Tick();
                Check(ui.IsSavePending && !finished.WaitOne(0), "超时维护检查也不在界面线程等待尚未完成的慢磁盘写入");
                bool heartbeat = false;
                app.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(delegate { heartbeat = true; })); Pump();
                Check(heartbeat && !finished.WaitOne(0), "模拟慢磁盘期间界面仍能处理输入，不等待写盘完成");
                toggle("LockToggle", false); release.Set();
                Check(finished.WaitOne(2000), "后台原子写入能够完成独立快照"); Pump();
                Check(ui.IsSavePending && !ui.IsSaved && Storage.Read(store.DataPath).Locked,
                    "旧快照完成不把更新后的设置误标为已保存，后续修改不会污染旧快照");
                controller.BeginSettingsSave();
                Check(PumpUntil(delegate { return !ui.IsSavePending; }, 3000) && ui.IsSaved && !Storage.Read(store.DataPath).Locked &&
                    Storage.Read(store.DataPath).DataRevision == state.DataRevision && writes == 2,
                    "新快照随后保存最新设置与修订号，连续 20 次滑块修改只写入一次快照");
                toggle("ShowMinimizeToggle", true); controller.Hide(); Pump();
                Check(!ui.IsSavePending && ui.IsSaved && Storage.Read(store.DataPath).ShowMinimizeButton,
                    "隐藏及退出共用的直接保存路径刷新尚未落盘的设置并取消合并计时器");
                controller.Show(); Pump(); ui = controller.CurrentUi;
                controller.SettingsSnapshotWriterForTest = delegate(byte[] bytes) { throw new IOException("QA simulated disk failure"); };
                toggle("ShowCloseToggle", true); controller.BeginSettingsSave();
                Check(PumpUntil(delegate { return !ui.IsSavePending; }, 3000) && !ui.IsSaved && ui.SaveError != null && !Storage.Read(store.DataPath).ShowCloseButton,
                    "后台写盘失败显示保存失败并保留原文件，不显示虚假成功");
                controller.SettingsSnapshotWriterForTest = null; controller.Hide(); Pump();
                Check(ui.IsSaved && ui.SaveError == null && Storage.Read(store.DataPath).ShowCloseButton,
                    "保存失败后直接重试写入最新设置，恢复成功状态");
                controller.Show(); Pump(); ui = controller.CurrentUi;
                int beforeBurst = writes;
                controller.SettingsSnapshotWriterForTest = delegate(byte[] bytes) { store.SaveSnapshot(bytes); System.Threading.Interlocked.Increment(ref writes); };
                for (int i = 80; i < 95; i++) ui.Control<Slider>("WindowOpacitySlider").Value = i;
                Check(PumpUntil(delegate { return !ui.IsSavePending; }, 3000) && writes == beforeBurst + 1 && Storage.Read(store.DataPath).WindowOpacityPercent == 94,
                    "真实合并计时器自动落盘，只保存连续拖动的最终数值");
                started.Reset(); release.Reset(); finished.Reset();
                controller.SettingsSnapshotWriterForTest = delegate(byte[] bytes)
                {
                    started.Set(); if (!release.WaitOne(5000)) throw new TimeoutException("QA recreate writer timeout");
                    store.SaveSnapshot(bytes); finished.Set();
                };
                toggle("LockToggle", true); controller.BeginSettingsSave();
                Check(started.WaitOne(2000), "窗口重建测试启动独立后台写入");
                typeof(Controller).GetMethod("Rebuild", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(controller, null);
                ui = controller.CurrentUi; Pump();
                Check(ui.IsSavePending && !ui.IsSaved, "宿主或窗口重建保留正在保存状态，不中断后台保存");
                release.Set();
                Check(PumpUntil(delegate { return !ui.IsSavePending; }, 3000) && ui.IsSaved && Storage.Read(store.DataPath).Locked,
                    "窗口重建后后台完成通知正确更新新窗口与文件");
                started.Reset(); release.Reset(); finished.Reset();
                int raceWrites = 0;
                controller.SettingsSnapshotWriterForTest = delegate(byte[] bytes)
                {
                    started.Set(); if (!release.WaitOne(5000)) throw new TimeoutException("QA direct save writer timeout");
                    store.SaveSnapshot(bytes); System.Threading.Interlocked.Increment(ref raceWrites); finished.Set();
                };
                toggle("LockToggle", false); controller.BeginSettingsSave();
                Check(started.WaitOne(2000), "同步保存竞态测试先阻塞旧后台快照");
                toggle("LockToggle", true); controller.BeginSettingsSave();
                System.Threading.ThreadPool.QueueUserWorkItem(delegate { System.Threading.Thread.Sleep(30); release.Set(); });
                controller.Hide(); AdvanceFrames(150);
                Check(ui.IsSaved && !ui.IsSavePending && Storage.Read(store.DataPath).Locked && raceWrites == 1,
                    "直接保存使排队中的旧快照失效，旧后台写入不能覆盖最终锁定状态");
                controller.Show(); Pump(); ui = controller.CurrentUi;
                controller.SettingsSnapshotWriterForTest = null;
                ui.SettingsOpen = true; ui.OpenSettingsCategory("ThemeLabel"); AdvanceFrames(300);
                Render(ui, Path.Combine(directory, "v146-settings-dark.png"));
                toggle("DarkToggle", false); AdvanceFrames(300); Render(ui, Path.Combine(directory, "v146-settings-light.png"));
                ui.SettingsOpen = false; AdvanceFrames(300); Render(ui, Path.Combine(directory, "v146-main.png"));
                controller.Hide();
            }
            finally
            {
                release.Set();
                if (controller != null) controller.Dispose();
                Native.TestSurface = oldSurface; host.Close();
                // Worker wait handles remain valid until all queued workers finish.
            }
        }
        private static void CheckMixedDpi45(string directory)
        {
            Native.Rect anchor = new Native.Rect { Left = 400, Top = 20, Right = 1200, Bottom = 1020 };
            Native.Point grab = new Native.Point { X = 500, Y = 80 };
            System.Drawing.Rectangle portrait = new System.Drawing.Rectangle(-1600, 0, 1600, 2476);
            System.Drawing.Rectangle landscape = new System.Drawing.Rectangle(0, 0, 1920, 1020);
            Native.Point pointer = new Native.Point { X = -1000, Y = 180 };
            Native.Rect moved = TodoWindow.DpiMoveGeometry(anchor, grab, pointer, 1.25, 1.75, 640, 800, portrait, true);
            Check(moved.Right - moved.Left == 1120 && moved.Bottom - moved.Top == 1400 && moved.Left == -1140 && moved.Top == 96,
                "125%→175%跨屏保持640×800 DIP及逻辑抓取点，像素尺寸统一变为1120×1400");
            Native.Rect returned = TodoWindow.DpiMoveGeometry(moved, pointer, grab, 1.75, 1.25, 640, 800, landscape, true);
            Check(SameRect(anchor, returned), "175%→125%返回原屏不改变逻辑尺寸、比例与抓取位置");
            foreach (double scale in new[] { 1.0, 1.25, 1.5, 1.75, 2.0, 2.5 })
            {
                Native.Rect free = TodoWindow.DpiMoveGeometry(anchor, grab, pointer, 1.25, scale, 640, 800, portrait, false);
                Check(free.Right - free.Left == (int)Math.Round(640 * scale) && free.Bottom - free.Top == (int)Math.Round(800 * scale),
                    "100%–250%混合DPI拖动保持逻辑宽高，目标比例=" + scale);
            }
            Native.Rect fit = TodoWindow.DpiMoveGeometry(anchor, grab, new Native.Point { X = 500, Y = 400 },
                1.25, 1.25, 900, 1300, landscape, true);
            Check(fit.Bottom - fit.Top == landscape.Height && Math.Abs((fit.Right - fit.Left) / (double)(fit.Bottom - fit.Top) - 900.0 / 1300) < .002,
                "小屏工作区按完整宽高比例收纳，不只压低高度造成横向变形");
            Native.MonitorArea[] monitors = Native.PhysicalMonitors().ToArray();
            foreach (Native.MonitorArea monitor in monitors)
                report.AppendLine("DISPLAY: " + monitor.DeviceName + " work=" + monitor.Work + " scale=" + Native.MonitorScale(monitor.Handle));
            if (monitors.Length < 2) { report.AppendLine("SKIP: 实机跨屏验证需要至少两块显示器；纯几何混合DPI验证已执行。"); return; }
            foreach (bool embedded in new[] { false, true })
            {
                AppState state = new AppState { Language = "zh-CN", DesktopMode = embedded, Locked = false, Width = 440, Height = 636,
                    PositionSet = true, X = monitors[0].Work.Left + 80, Y = monitors[0].Work.Top + 80 };
                state.Add("跨屏字号与布局测试");
                Window host = new Window { Width = 800, Height = 900, WindowStyle = WindowStyle.None, ShowInTaskbar = false };
                TodoWindow view = new TodoWindow(state);
                try
                {
                    host.Show(); view.Window.Show(); view.InitializeNative(!embedded, false, new WindowInteropHelper(host).Handle); AdvanceFrames(250);
                    // The isolated host is our own window, never Explorer's desktop.
                    for (int round = 0; round < 3; round++)
                    foreach (Native.MonitorArea target in monitors.Reverse())
                    {
                        Native.Rect before; Native.PhysicalWindowRect(view.Desktop.Handle, out before);
                        report.AppendLine("ORIGIN45: embedded=" + embedded + " native=" + (before.Right-before.Left) + "x" + (before.Bottom-before.Top) +
                            " dip=" + view.Window.ActualWidth + "x" + view.Window.ActualHeight + " state=" + state.Width + "x" + state.Height +
                            " dpi=" + PresentationSource.FromVisual(view.Window).CompositionTarget.TransformToDevice.M11);
                        Native.Point start = new Native.Point { X = before.Left + 60, Y = before.Top + 45 };
                        Native.Point end = new Native.Point { X = target.Work.Left + 200, Y = target.Work.Top + 140 };
                        view.BeginWindowMove(start); view.ContinueWindowMove(end);
                        bool rebuild = false; view.RebuildRequested = delegate { rebuild = true; };
                        view.FinishWindowMoveForTest(true, end); AdvanceFrames(200);
                        if (rebuild)
                        {
                            view.Window.Close(); view = new TodoWindow(state); view.Window.Show();
                            view.InitializeNative(false, false, new WindowInteropHelper(host).Handle); AdvanceFrames(220);
                        }
                        Native.Rect actual; Native.PhysicalWindowRect(view.Desktop.Handle, out actual);
                        double dpi = PresentationSource.FromVisual(view.Window).CompositionTarget.TransformToDevice.M11;
                        report.AppendLine("DPI45: embedded=" + embedded + " screen=" + target.DeviceName + " native=" +
                            (actual.Right - actual.Left) + "x" + (actual.Bottom - actual.Top) + " WPF=" +
                            view.Window.ActualWidth + "x" + view.Window.ActualHeight + " dpi=" + dpi + " content=" + view.ContentScale);
                        Check(Math.Abs(state.Width - 440) < 1 && Math.Abs(state.Height - 636) < 1,
                            "实机连续往返切屏不累积尺寸误差，embedded=" + embedded + " round=" + round + " " + target.DeviceName);
                        Check(Math.Abs(view.Control<Grid>("DpiSurface").ActualWidth - 440) <= 2 && Math.Abs(view.Control<Grid>("DpiSurface").ActualHeight - 636) <= 2 && Math.Abs(view.ContentScale - 1.178) < .003,
                            "实机目标屏布局维持440×636逻辑尺寸，embedded=" + embedded + " " + target.DeviceName);
                        if (round == 0)
                        {
                            string imageName = "v145-" + (embedded ? "desktop" : "window") + "-" + target.DeviceName.Replace("\\", "").Replace(".", "");
                            RenderLaidOutWindow(view.Window, Path.Combine(directory, imageName + ".png"), dpi * 96);
                            Grid task = view.Control<StackPanel>("PendingList").Children.OfType<Grid>().First(row => row.Tag is TodoItem);
                            ContextMenu menu = task.ContextMenu; menu.PlacementTarget = task; menu.IsOpen = true; AdvanceFrames(100);
                            double popupDpi = PresentationSource.FromVisual(menu).CompositionTarget.TransformToDevice.M11;
                            ScaleTransform menuTransform = menu.LayoutTransform as ScaleTransform;
                            Check(menuTransform != null && Math.Abs(menuTransform.ScaleX * popupDpi - view.ContentScale * Native.MonitorScale(target.Handle)) < .003,
                                "独立事项菜单的字号与卡片同屏同步缩放，embedded=" + embedded + " " + target.DeviceName);
                            menu.IsOpen = false;
                            view.SettingsOpen = true; view.OpenSettingsCategory("BasicSectionLabel"); AdvanceFrames(230);
                            RenderLaidOutWindow(view.Window, Path.Combine(directory, imageName + "-settings.png"), dpi * 96);
                            view.SettingsOpen = false; AdvanceFrames(220);
                            view.RememberGeometry();
                            Check(Math.Abs(state.Width - 440) < 1 && Math.Abs(state.Height - 636) < 1,
                                "嵌入宿主DPI不会污染实际屏幕的尺寸保存，embedded=" + embedded + " " + target.DeviceName);
                        }
                    }
                }
                finally { view.Window.Close(); host.Close(); }
            }
            AppState large = new AppState { Language = "zh-CN", DesktopMode = false, Locked = false, Width = 900, Height = 1300,
                PositionSet = true, X = monitors[0].Work.Left + 10, Y = monitors[0].Work.Top + 10 };
            TodoWindow largeView = new TodoWindow(large);
            try
            {
                largeView.Window.Show(); largeView.InitializeNative(true, false, IntPtr.Zero); AdvanceFrames(220);
                foreach (Native.MonitorArea target in monitors.Reverse())
                {
                    Native.Rect before; Native.PhysicalWindowRect(largeView.Desktop.Handle, out before);
                    Native.Point start = new Native.Point { X = before.Left + 60, Y = before.Top + 45 };
                    Native.Point end = new Native.Point { X = target.Work.Left + 200, Y = target.Work.Top + 140 };
                    largeView.BeginWindowMove(start); largeView.ContinueWindowMove(end); largeView.FinishWindowMoveForTest(true, end); AdvanceFrames(220);
                    largeView.RememberGeometry();
                    Native.Rect rect; Native.PhysicalWindowRect(largeView.Desktop.Handle, out rect);
                    Check(large.Width == 900 && large.Height == 1300 && Math.Abs((rect.Right - rect.Left) / (double)(rect.Bottom - rect.Top) - 900.0 / 1300) < .002,
                        "大卡片跨到短屏后保存/隐藏不覆盖偏好尺寸，返回大屏自动恢复，" + target.DeviceName);
                    largeView.Window.Hide(); largeView.Window.Show(); largeView.PlaceOnScreen(false); AdvanceFrames(120);
                    Native.Rect restored; Native.PhysicalWindowRect(largeView.Desktop.Handle, out restored);
                    Check(SameRect(rect, restored), "跨屏收纳后隐藏与显示位置尺寸不跳变，" + target.DeviceName);
                }
            }
            finally { largeView.Window.Close(); }
        }
        private static void CheckWindowMove44(string directory)
        {
            AppState state = new AppState { Language = "zh-CN", DesktopMode = false, Locked = false, ThemeId = "graphite" };
            state.Add("独立移动回归测试");
            TodoWindow view = new TodoWindow(state);
            try
            {
                view.Window.Show(); view.InitializeNative(true, false, IntPtr.Zero); AdvanceFrames(240);
                Check(VisualDescendants<SoftCardShadow>(view.Window).Count() == 1 &&
                    !VisualDescendants<FrameworkElement>(view.Window).Any(element => element.Effect is System.Windows.Media.Effects.DropShadowEffect),
                    "主体柔和圆角阴影用轻量矢量绘制，快照不再执行整窗模糊");
                Native.Rect original; Native.PhysicalWindowRect(view.Desktop.Handle, out original);
                Native.Point start = new Native.Point { X = original.Left + 60, Y = original.Top + 45 };
                view.BeginWindowMove(start); view.ContinueWindowMove(new Native.Point { X = start.X + 1, Y = start.Y + 1 });
                Native.Rect below; Native.PhysicalWindowRect(view.Desktop.Handle, out below);
                Check(SameRect(original, below) && !view.MovePreviewActive, "窗口按下及未越过拖动阈值时不捕获快照或移动");
                view.ContinueWindowMove(new Native.Point { X = start.X - 12, Y = start.Y + 9 });
                Native.Rect moved; Native.PhysicalWindowRect(view.MovePreviewHandle, out moved);
                Check(moved.Left == original.Left - 12 && moved.Top == original.Top + 9 && view.MovePreviewActive &&
                    GestureWindowClip.PixelsSuppressed(view.Desktop.Handle),
                    "普通窗口与桌面模式一致：首次越过阈值建立一份固定画面的原生预览，原 HWND 不反复移动");
                IntPtr retainedPreview = view.MovePreviewHandle;
                System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < 100; i++) view.ContinueWindowMove(new Native.Point { X = start.X - 12 - i % 2, Y = start.Y + 9 });
                report.AppendLine("MEASURE: 普通窗口100次移动更新 " + watch.Elapsed.TotalMilliseconds.ToString("F2") + " ms（不是屏幕FPS）");
                Check(view.MovePreviewHandle == retainedPreview && view.MovePreviewSurfaceBuilds == 1 && Native.IsWindowVisible(retainedPreview),
                    "连续窗口移动复用同一份可见代理，不重复截图或重建像素表面");
                view.FinishWindowMoveForTest(false); Pump(); Native.PhysicalWindowRect(view.Desktop.Handle, out moved);
                Check(SameRect(original, moved) && !state.PositionSet, "取消直接移动后恢复原位置，不保存草稿位置");
                view.BeginWindowMove(start); state.Locked = true;
                view.ContinueWindowMove(new Native.Point { X = start.X - 25, Y = start.Y + 20 });
                view.FinishWindowMoveForTest(false); Native.PhysicalWindowRect(view.Desktop.Handle, out moved);
                Check(SameRect(original, moved), "锁定时移动路径不改变窗口位置");
                state.Locked = false; view.BeginWindowMove(start);
                view.ContinueWindowMove(new Native.Point { X = start.X - 15, Y = start.Y + 10 });
                view.FinishWindowMoveForTest(true); Pump(); Native.PhysicalWindowRect(view.Desktop.Handle, out moved);
                Check(state.PositionSet && state.X == moved.Left && state.Y == moved.Top, "普通窗口松手后保存真实最终位置");
                Render(view, Path.Combine(directory, "v144-move-restored.png"));
            }
            finally { view.Window.Close(); }
            Window host = new Window { Width = 1200, Height = 1000, WindowStyle = WindowStyle.None, ShowInTaskbar = false };
            TodoWindow attached = new TodoWindow(new AppState { Language = "zh-CN", DesktopMode = true, Locked = false, ThemeId = "graphite" });
            try
            {
                host.Show(); attached.Window.Show(); attached.InitializeNative(false, false, new WindowInteropHelper(host).Handle); AdvanceFrames(240);
                Native.Rect original; Native.PhysicalWindowRect(attached.Desktop.Handle, out original);
                Native.Point start = new Native.Point { X = original.Left + 60, Y = original.Top + 45 };
                Border card = attached.Control<Border>("Card");
                attached.BeginWindowMove(start); card.CaptureMouse();
                attached.ContinueWindowMove(new Native.Point { X = start.X - 15, Y = start.Y + 10 }); Pump();
                Check(attached.Desktop.IsAttached && attached.MovePreviewActive && attached.Control<Border>("Card").Opacity == 0 &&
                    attached.Control<Grid>("CardShadow").Opacity == 0, "嵌入桌面仍用独立代理移动，实时卡片与阴影一起隐藏，无双重阴影残留");
                IntPtr preview = attached.MovePreviewHandle; Native.Rect previewRect; Native.PhysicalWindowRect(preview, out previewRect);
                Native.Rect sourceRect; Native.PhysicalWindowRect(attached.Desktop.Handle, out sourceRect);
                Native.Point endPoint = new Native.Point { X = start.X - 15, Y = start.Y + 10 };
                Native.MonitorArea endDisplay = Native.MonitorAt(endPoint);
                Native.Rect expectedPreview = TodoWindow.DpiMoveGeometry(original, start, endPoint,
                    Native.MonitorScale(Native.MonitorFor(original).Handle), Native.MonitorScale(endDisplay.Handle),
                    attached.State.Width, attached.State.Height, endDisplay.Work, false);
                report.AppendLine("MEASURE: 嵌入拖动 origin=" + original.Left + "," + original.Top + "," + (original.Right-original.Left) + "," + (original.Bottom-original.Top) +
                    " preview=" + previewRect.Left + "," + previewRect.Top + "," + (previewRect.Right-previewRect.Left) + "," + (previewRect.Bottom-previewRect.Top) +
                    " expected=" + expectedPreview.Left + "," + expectedPreview.Top + "," + (expectedPreview.Right-expectedPreview.Left) + "," + (expectedPreview.Bottom-expectedPreview.Top) +
                    " source=" + sourceRect.Left + "," + sourceRect.Top + "," + (sourceRect.Right-sourceRect.Left) + "," + (sourceRect.Bottom-sourceRect.Top));
                Check(SameRect(previewRect, expectedPreview) && SameRect(sourceRect, original),
                    "嵌入桌面移动只更新顶层代理，不重绘 Explorer 子窗口");
                Check(GestureWindowClip.PixelsSuppressed(attached.Desktop.Handle),
                    "原生像素在交接时立即隐藏，不等待 WPF 下一帧，避免半透明卡片与阴影叠闪");
                Check(card.IsMouseCaptured, "原生暂时隐藏像素不丢失拖动鼠标捕获");
                attached.FinishWindowMoveForTest(false); Pump();
                Check(!Native.IsWindow(preview) && attached.Control<Border>("Card").Opacity == 1 && attached.Control<Grid>("CardShadow").Opacity == 1,
                    "嵌入模式取消后释放代理并恢复一份卡片和阴影");
                Check(!GestureWindowClip.PixelsSuppressed(attached.Desktop.Handle), "结束移动恢复原生可见区域，圆角仍由 WPF 抗锯齿绘制");
                attached.State.WindowOpacityPercent = 70; attached.RefreshImportedState(); AdvanceFrames(220);
                attached.BeginWindowMove(start); attached.ContinueWindowMove(new Native.Point { X = start.X - 16, Y = start.Y + 10 });
                IntPtr firstPreview = attached.MovePreviewHandle; attached.FinishWindowMoveForTest(false);
                attached.BeginWindowMove(start); attached.ContinueWindowMove(new Native.Point { X = start.X - 17, Y = start.Y + 10 });
                IntPtr secondPreview = attached.MovePreviewHandle; Pump();
                Check(!Native.IsWindow(firstPreview) && Native.IsWindow(secondPreview) && GestureWindowClip.PixelsSuppressed(attached.Desktop.Handle),
                    "快速连续拖动时旧代理清理不恢复新手势的隐藏区域，不留下两份预览");
                attached.FinishWindowMoveForTest(false); Pump();
                Check(!Native.IsWindow(secondPreview) && !GestureWindowClip.PixelsSuppressed(attached.Desktop.Handle) && Math.Abs(card.Opacity - .7) < .001,
                    "连续拖动结束保留用户70%透明度，恢复像素可见性且不残留代理");
            }
            finally { attached.Window.Close(); host.Close(); }
        }
        private static void CheckInkAlignment43(string directory)
        {
            AppState state = new AppState { Language = "zh-CN", DesktopMode = false, ThemeId = "graphite", Width = 440, Height = 636 };
            TodoWindow view = new TodoWindow(state);
            try
            {
                view.Window.Show(); view.SettingsOpen = true; AdvanceFrames(260);
                foreach (string language in new[] { "zh-CN", "en-US" })
                foreach (bool dark in new[] { false, true })
                foreach (int[] size in new[] { new[] { 320, 360 }, new[] { 360, 520 }, new[] { 480, 740 }, new[] { 720, 1000 } })
                {
                    state.Language = language; state.DarkMode = dark;
                    view.Window.Width = size[0]; view.Window.Height = size[1]; view.RefreshImportedState(); view.SettingsOpen = true;
                    AdvanceFrames(260);
                    StackPanel overview = (StackPanel)view.Control<ScrollViewer>("SettingsScroll").Content;
                    Button[] entries = overview.Children.OfType<Button>().ToArray();
                    Check(entries.Length == 4 && entries.All(entry => {
                        Grid row = (Grid)entry.Content; TextBlock label = row.Children.OfType<TextBlock>().Single();
                        Rect text = label.TransformToAncestor(entry).TransformBounds(UiGlyphs.TextInk(label));
                        Grid icon = row.Children.OfType<Grid>().Single(); System.Windows.Shapes.Path arrow = icon.Children.OfType<System.Windows.Shapes.Path>().Single();
                        Rect glyph = arrow.TransformToAncestor(entry).TransformBounds(arrow.Data.GetRenderBounds(new Pen(arrow.Stroke, arrow.StrokeThickness)
                            { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }));
                        return Math.Abs(text.Y + text.Height / 2 - entry.ActualHeight / 2) < .65 &&
                            Math.Abs(glyph.Y + glyph.Height / 2 - entry.ActualHeight / 2) < .65 && text.Left < 20;
                    }), language + (dark ? " dark " : " light ") + size[0] + "x" + size[1] + " 分类实际文字与几何箭头均垂直居中，并保留左对齐");
                    foreach (int dpi in new[] { 96, 120, 168, 192 })
                        Check(entries.All(entry => CategoryTextPixelsCentered(entry, dpi)),
                            language + (dark ? " dark " : " light ") + size[0] + "x" + size[1] + " " + dpi + "dpi 分类文字实际渲染像素上下居中");
                    if (language == "zh-CN" && size[0] == 480)
                        Render(view, Path.Combine(directory, "v143-settings-" + (dark ? "dark" : "light") + ".png"), 192);
                }
                state.Language = "zh-CN"; state.DarkMode = false; view.Window.Width = 440; view.Window.Height = 636;
                view.RefreshImportedState(); view.OpenSettingsCategory("ThemeLabel"); AdvanceFrames(260);
                RadioButton theme = view.Control<RadioButton>("ThemeGraphite");
                Check(VisualDescendants<InkContentPresenter>(theme).Any(), "主题按钮使用统一字形居中呈现器");
                foreach (string styleName in new[] { "SettingsCategoryButton", "ActionButton", "PrimaryButton" })
                {
                    Button feedback = new Button { Content = "保存", Width = 150, Style = view.Window.Resources[styleName] as Style };
                    feedback.ApplyTemplate();
                    Trigger hover = feedback.Template.Triggers.OfType<Trigger>().Single(trigger => trigger.Property == UIElement.IsMouseOverProperty);
                    System.Windows.Media.Animation.BeginStoryboard enter = hover.EnterActions.OfType<System.Windows.Media.Animation.BeginStoryboard>().Single();
                    System.Windows.Media.Animation.BeginStoryboard leave = hover.ExitActions.OfType<System.Windows.Media.Animation.BeginStoryboard>().Single();
                    Check(enter.Storyboard.Children.Cast<System.Windows.Media.Animation.DoubleAnimation>().All(animation =>
                        animation.Duration.TimeSpan.TotalMilliseconds == 120 && System.Windows.Media.Animation.Storyboard.GetTargetName(animation) == "HoverSurface") &&
                        leave.Storyboard.Children.Cast<System.Windows.Media.Animation.DoubleAnimation>().All(animation => animation.To == 0) &&
                        feedback.Template.Triggers.OfType<Trigger>().Any(trigger => trigger.Property == UIElement.IsKeyboardFocusedProperty),
                        styleName + " 背景短淡入淡出，不缩放文字，并保留键盘焦点反馈");
                }
                foreach (string title in new[] { "保存", "取消", "Save", "Cancel", "保存名称" })
                {
                    Button button = new Button { Content = title, Width = 150, Style = view.Window.Resources["PrimaryButton"] as Style };
                    Border host = new Border { Child = button, Width = 150, Height = 34 };
                    // Use a real loaded window so the same Loaded/text-change path runs.
                    Window probe = new Window { Width = 190, Height = 100, WindowStyle = WindowStyle.None,
                        ShowInTaskbar = false, Content = host, FontFamily = view.Window.FontFamily, FontSize = view.Window.FontSize };
                    foreach (object key in new[] { "TextBrush", "AccentBrush", "AccentTextBrush" }) probe.Resources[key] = view.Window.Resources[key];
                    try
                    {
                        probe.Show(); AdvanceFrames(60); button.ApplyTemplate();
                        TextBlock label = VisualDescendants<TextBlock>(button).Single();
                        Rect ink = label.TransformToAncestor(button).TransformBounds(UiGlyphs.TextInk(label));
                        Check(Math.Abs(ink.Y + ink.Height / 2 - button.ActualHeight / 2) < .65,
                            title + " 按钮按实际文字笔画居中，而非仅令文字行框居中");
                        button.Content = title == "保存" ? "Apply" : "保存"; AdvanceFrames(60);
                        label = VisualDescendants<TextBlock>(button).Single(); ink = label.TransformToAncestor(button).TransformBounds(UiGlyphs.TextInk(label));
                        Check(Math.Abs(ink.Y + ink.Height / 2 - button.ActualHeight / 2) < .65, "按钮内容更换后重新按字形居中");
                    }
                    finally { probe.Close(); }
                }
                TextBlock rename = new TextBlock { Text = "基础功能", Width = 180, FontFamily = view.Window.FontFamily, FontSize = 13.5,
                    FontWeight = FontWeights.Medium, Foreground = Brushes.Black };
                Window textProbe = new Window { Width = 210, Height = 90, WindowStyle = WindowStyle.None, ShowInTaskbar = false, Content = rename };
                UiGlyphs.CenterLabelInk(rename); UiGlyphs.CenterLabelInk(rename);
                try
                {
                    textProbe.Show(); AdvanceFrames(60); rename.Text = "Settings"; AdvanceFrames(60);
                    Rect ink = rename.RenderTransform.TransformBounds(UiGlyphs.TextInk(rename));
                    Check(Math.Abs(ink.Y + ink.Height / 2 - rename.ActualHeight / 2) < .65, "语言切换且控件大小不变时也重新居中，重复注册不会累计偏移");
                }
                finally { textProbe.Close(); }
                view.SettingsOpen = false;
                state.Add("整理今天的工作安排"); state.Add("留一点时间读书");
                view.RefreshImportedState(); AdvanceFrames(260); Render(view, Path.Combine(directory, "v143-main.png"));
                view.OpenEdit(state.Pending.First()); AdvanceFrames(260); Render(view, Path.Combine(directory, "v143-edit.png"));
            }
            finally { view.Window.Close(); }
        }
        private static bool CategoryTextPixelsCentered(Button entry, int dpi)
        {
            TextBlock label = ((Grid)entry.Content).Children.OfType<TextBlock>().Single();
            Rect ink = label.TransformToAncestor(entry).TransformBounds(UiGlyphs.TextInk(label));
            double scale = dpi / 96.0;
            RenderTargetBitmap image = new RenderTargetBitmap((int)Math.Ceiling(entry.ActualWidth * scale),
                (int)Math.Ceiling(entry.ActualHeight * scale), dpi, dpi, PixelFormats.Pbgra32);
            DrawingVisual isolated = new DrawingVisual();
            using (DrawingContext drawing = isolated.RenderOpen())
            {
                // A relative viewbox normalizes the child's layout offset; an
                // absolute viewbox would capture only the first row at y = 0.
                VisualBrush brush = new VisualBrush(entry) { Stretch = Stretch.Fill };
                drawing.DrawRectangle(brush, null, new Rect(0, 0, entry.ActualWidth, entry.ActualHeight));
            }
            image.Render(isolated);
            byte[] pixels = new byte[image.PixelWidth * image.PixelHeight * 4]; image.CopyPixels(pixels, image.PixelWidth * 4, 0);
            Color text = ((SolidColorBrush)label.Foreground).Color;
            int top = image.PixelHeight, bottom = -1;
            int left = Math.Max(0, (int)Math.Floor(ink.Left * scale) - 1), right = Math.Min(image.PixelWidth, (int)Math.Ceiling(ink.Right * scale) + 1);
            for (int y = 0; y < image.PixelHeight; y++) for (int x = left; x < right; x++)
            {
                int p = (y * image.PixelWidth + x) * 4;
                if (pixels[p + 3] > 230 && Math.Abs(pixels[p] - text.B) < 45 && Math.Abs(pixels[p + 1] - text.G) < 45 && Math.Abs(pixels[p + 2] - text.R) < 45)
                { top = Math.Min(top, y); bottom = Math.Max(bottom, y); }
            }
            bool centered = bottom >= top && Math.Abs((top + bottom + 1) / 2.0 - entry.ActualHeight * scale / 2) <= 1.4;
            if (!centered) throw new InvalidOperationException("Pixel ink: " + label.Text + " dpi=" + dpi + " top=" + top + " bottom=" + bottom + " height=" + entry.ActualHeight + " ink=" + ink);
            return centered;
        }
        private static void Pump()
        {
            DispatcherFrame frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(delegate { frame.Continue = false; }));
            Dispatcher.PushFrame(frame);
        }
        private static bool PumpUntil(Func<bool> condition, int timeoutMs)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                Pump(); if (condition()) return true;
                System.Threading.Thread.Sleep(15);
            }
            Pump(); return condition();
        }
        private static void CheckResizeMath()
        {
            Native.Rect anchor = new Native.Rect { Left = -1450, Top = 200, Right = -1000, Bottom = 850 };
            Native.Point start = new Native.Point { X = -1010, Y = 840 };
            System.Drawing.Rectangle work = new System.Drawing.Rectangle(-1600, 0, 1600, 1200);
            Native.Rect size = TodoWindow.ResizeGeometry(anchor, start, new Native.Point { X = -910, Y = 900 }, 1.25, work);
            Check(size.Left == -1450 && size.Top == 200 && size.Right - size.Left == 550 && size.Bottom - size.Top == 710, "缩放使用固定屏幕起点，125% 和负坐标不漂移");
            Native.Rect repeat = TodoWindow.ResizeGeometry(anchor, start, new Native.Point { X = -910, Y = 900 }, 1.25, work);
            Check(size.Right == repeat.Right && size.Bottom == repeat.Bottom, "重复鼠标位置不会累积缩放误差");
            size = TodoWindow.ResizeGeometry(anchor, start, new Native.Point { X = -3000, Y = -1000 }, 1.25, work);
            Check(size.Right - size.Left == 400 && size.Bottom - size.Top == 450, "缩放最小尺寸限制");
            size = TodoWindow.ResizeGeometry(anchor, start, new Native.Point { X = 10000, Y = 10000 }, 1.25, work);
            Check(size.Right - size.Left == 800 && size.Bottom == work.Bottom && size.Left == anchor.Left, "缩放最大尺寸遵守屏幕边界和可阅读宽高比，左上角固定");
            Native.Rect moved = TodoWindow.MoveGeometry(anchor, start, new Native.Point { X = -2000, Y = 1700 }, work);
            Check(moved.Left == work.Left && moved.Top == work.Bottom - (anchor.Bottom - anchor.Top) &&
                moved.Right - moved.Left == anchor.Right - anchor.Left && moved.Bottom - moved.Top == anchor.Bottom - anchor.Top,
                "拖动跨负坐标屏幕时按目标屏工作区限位且尺寸不变");
            Native.Rect crossingAnchor = new Native.Rect { Left = -500, Top = 120, Right = -100, Bottom = 720 };
            Native.Rect crossing = TodoWindow.MoveAcrossMonitorsGeometry(crossingAnchor,
                new Native.Point { X = -300, Y = 300 }, new Native.Point { X = 100, Y = 300 },
                new[] { new System.Drawing.Rectangle(-1600, 0, 1600, 1200), new System.Drawing.Rectangle(0, 0, 1920, 1080) });
            Check(crossing.Left == -100 && crossing.Right == 300,
                "主窗口跨屏拖动时允许平滑跨越屏幕接缝，不在指针过界瞬间跳动");
            Native.Rect landed = TodoWindow.ConstrainMoveToWork(crossing, new System.Drawing.Rectangle(0, 0, 1920, 1080));
            Check(landed.Left == 0 && landed.Top == 120 && landed.Right == 400 && landed.Bottom == 720,
                "跨屏拖动松手后按目标屏幕工作区完整收纳窗口");
            Native.Rect mini = new Native.Rect { Left = 1870, Top = 400, Right = 1932, Bottom = 462 };
            System.Drawing.Rectangle miniWork = new System.Drawing.Rectangle(0, 0, 1920, 1040);
            Check(MiniBubble.NearestEdge(mini, miniWork) == "Right", "悬浮图标靠近屏幕右边缘时识别为贴边");
            Native.Rect hiddenMini = MiniBubble.EdgeRect(mini, miniWork, "Right", true);
            Native.Rect shownMini = MiniBubble.EdgeRect(hiddenMini, miniWork, "Right", false);
            Check(hiddenMini.Left == 1889 && hiddenMini.Right == 1951 && shownMini.Left == 1858 && shownMini.Right == 1920,
                "悬浮图标贴边后只隐藏一半，鼠标经过可完整滑回屏幕内");
            Check(MiniBubble.IsCursorInside(shownMini, new Native.Point { X = 1900, Y = 430 }) &&
                !MiniBubble.IsCursorInside(shownMini, new Native.Point { X = 1800, Y = 430 }),
                "悬浮图标延迟收起时以物理鼠标坐标复核，悬停不会反复弹出缩回");
            Native.Rect recoveredMini = MiniBubble.ConstrainRect(new Native.Rect { Left = 9000, Top = -5000, Right = 9062, Bottom = -4938 }, miniWork);
            Check(recoveredMini.Left == 1858 && recoveredMini.Top == 0,
                "悬浮图标保存位置落在已移除屏幕时会自动回收到当前屏幕");
            Check(!TodoWindow.PastDragThreshold(new Native.Point { X = 100, Y = 200 }, new Native.Point { X = 100, Y = 200 }, 1.75) &&
                !TodoWindow.PastDragThreshold(new Native.Point { X = 100, Y = 200 }, new Native.Point { X = 102, Y = 202 }, 1.75) &&
                TodoWindow.PastDragThreshold(new Native.Point { X = 100, Y = 200 }, new Native.Point { X = 120, Y = 200 }, 1.75),
                "仅按住或细小抖动不启用预览，超过拖动阈值才切换画面");
        }
        private static void CheckPhysicalPreview()
        {
            byte[] pixels = new byte[16 * 16 * 4];
            for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = 92; pixels[i + 1] = 121; pixels[i + 2] = 59; pixels[i + 3] = 255; }
            BitmapSource bitmap = BitmapSource.Create(16, 16, 96, 96, PixelFormats.Pbgra32, null, pixels, 64); bitmap.Freeze();
            bool initialExact = true, stableAfterDpi = true, movedExact = true, contextRestored = true;
            int monitorCount = 0;
            foreach (Native.MonitorArea monitor in Native.PhysicalMonitors())
            {
                monitorCount++;
                Native.Rect target = new Native.Rect { Left = monitor.Work.Left + 25, Top = monitor.Work.Top + 35,
                    Right = monitor.Work.Left + 225, Bottom = monitor.Work.Top + 335 };
                IntPtr previous = Native.SetThreadDpiAwarenessContext(new IntPtr(-1));
                GesturePreview preview;
                try
                {
                    preview = new GesturePreview(bitmap, target);
                    contextRestored &= Native.GetAwarenessFromDpiAwarenessContext(Native.GetThreadDpiAwarenessContext()) == 0;
                    Native.Rect actual; Native.PhysicalWindowRect(preview.Handle, out actual);
                    initialExact &= SameRect(actual, target);
                }
                finally { if (previous != IntPtr.Zero) Native.SetThreadDpiAwarenessContext(previous); }
                using (preview)
                {
                    Pump(); Native.Rect actual; Native.PhysicalWindowRect(preview.Handle, out actual);
                    initialExact &= SameRect(actual, target);
                    Native.Rect suggestion = new Native.Rect { Left = target.Left, Top = target.Top, Right = target.Right + 80, Bottom = target.Bottom + 120 };
                    IntPtr memory = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Native.Rect)));
                    try
                    {
                        Marshal.StructureToPtr(suggestion, memory, false);
                        Native.SendMessage(preview.Handle, 0x02E0, new IntPtr(168 | (168 << 16)), memory);
                    }
                    finally { Marshal.FreeHGlobal(memory); }
                    Pump(); Native.PhysicalWindowRect(preview.Handle, out actual); stableAfterDpi &= SameRect(actual, target);
                    for (int i = 0; i < 80; i++)
                    {
                        Native.Rect next = new Native.Rect { Left = target.Left + i, Top = target.Top + i,
                            Right = target.Right + i + i % 7, Bottom = target.Bottom + i + i % 9 };
                        preview.Update(next); Native.PhysicalWindowRect(preview.Handle, out actual); movedExact &= SameRect(actual, next);
                    }
                }
            }
            Check(monitorCount > 0 && initialExact, "在每块真实显示器上创建预览：首帧和布局空闲后均保持准确物理尺寸");
            Check(stableAfterDpi, "预览收到 175% DPI 建议矩形时仍保持原像素大小，不先变大再缩小");
            Check(movedExact, "每块显示器上 80 次原生预览移动/缩放：位置和大小始终精确");
            Check(contextRestored, "原生预览临时使用物理 DPI 上下文后恢复调用方上下文，不改变 WPF 缩放状态");
        }
        private static void CheckReviewLayouts(string directory)
        {
            AppState layoutState = new AppState { Language = "zh-CN", DesktopMode = false, Locked = false,
                ThemeId = "ocean", Width = 480, Height = 600, DisableGroups = false, LanSharingEnabled = true };
            TodoGroup work = new TodoGroup { Id = "qa-work", Name = "工作" };
            TodoGroup life = new TodoGroup { Id = "qa-life", Name = "生活" };
            layoutState.Groups.Add(work); layoutState.Groups.Add(life);
            for (int i = 0; i < 12; i++) layoutState.Add("事项 " + (i + 1), i < 6 ? work.Id : life.Id);
            TodoWindow layoutView = new TodoWindow(layoutState);
            try
            {
                Render(layoutView, Path.Combine(directory, "review-short-monitor.png"));
                ScrollViewer list = layoutView.Control<ScrollViewer>("TaskScroll");
                // The name of the list viewport is deliberately located through
                // its content, rather than assuming a test-only layout copy.
                FrameworkElement ancestor = layoutView.Control<StackPanel>("PendingList");
                while (ancestor != null && !(ancestor is ScrollViewer)) ancestor = VisualTreeHelper.GetParent(ancestor) as FrameworkElement;
                list = ancestor as ScrollViewer;
                Check(Math.Abs(layoutView.ContentScale - TodoWindow.AdaptiveContentScale(464, 584)) < 0.002 && list != null && list.ViewportHeight > 200,
                    "横屏短卡片保留至少默认逻辑高度，列表不再被过度放大挤占");
                Geometry shackle = layoutView.Control<System.Windows.Shapes.Path>("LockShackle").Data;
                Brush statusDot = layoutView.Control<System.Windows.Shapes.Ellipse>("StatusDot").Fill;
                for (int i = 0; i < 100; i++) layoutView.RefreshStatus();
                Check(ReferenceEquals(shackle, layoutView.Control<System.Windows.Shapes.Path>("LockShackle").Data) &&
                    ReferenceEquals(statusDot, layoutView.Control<System.Windows.Shapes.Ellipse>("StatusDot").Fill),
                    "周期状态更新复用冻结图形和画刷，不反复分配锁图标及状态色");
                layoutView.SettingsOpen = true;
                double[][] sizes = { new[] { 320.0, 360.0, 96.0 }, new[] { 480.0, 600.0, 120.0 },
                    new[] { 640.0, 800.0, 168.0 }, new[] { 720.0, 1100.0, 192.0 } };
                bool cornersOkay = true, inputsOkay = true;
                foreach (double[] size in sizes)
                {
                    layoutView.Window.Width = size[0]; layoutView.Window.Height = size[1];
                    foreach (string category in new[] { "BasicSectionLabel", "CustomSectionLabel", "ThemeLabel", "LanSectionLabel", "ManagementLabel" })
                    {
                        layoutView.OpenSettingsCategory(category);
                        string path = Path.Combine(directory, "review-" + category + "-" + size[2] + "dpi.png");
                        Render(layoutView, path, size[2]);
                        cornersOkay &= NoBrightCornerSeams(path);
                        if (category == "LanSectionLabel")
                        {
                            TextBox input = layoutView.Control<TextBox>("LanDisplayNameInput"); input.ApplyTemplate();
                            FrameworkElement host = input.Template.FindName("PART_ContentHost", input) as FrameworkElement;
                            Point hostTop = host.TranslatePoint(new Point(), input);
                            inputsOkay &= hostTop.Y >= 3 && hostTop.Y + host.ActualHeight <= input.ActualHeight - 3 &&
                                layoutView.SettingsCategoryScroll.HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled;
                            if (size[2] == 168)
                            {
                                layoutView.SettingsCategoryScroll.ScrollToEnd();
                                Render(layoutView, Path.Combine(directory, "review-lan-bottom-168dpi.png"), size[2]);
                            }
                        }
                    }
                }
                Check(cornersOkay, "五个分类在 100%、125%、175%、200% 渲染比例下四角均无白弧接缝");
                Check(inputsOkay, "局域网名称输入文字留在边框内，底部描边不被文字宿主覆盖");
                layoutState.WindowOpacityPercent = 60; layoutView.ApplyTheme(); layoutView.OpenSettingsCategory("BasicSectionLabel");
                int alpha = CardCenterAlpha(layoutView);
                Check(alpha >= 145 && alpha <= 160, "分类弹框与主体属于同一透明度组，半透明时不产生双层叠加");
                layoutState.DarkMode = true; layoutState.WindowOpacityPercent = 100; layoutView.ApplyTheme();
                Render(layoutView, Path.Combine(directory, "review-category-dark.png"), 168);
            }
            finally { layoutView.Window.Close(); }
            AppState history = new AppState { Language = "zh-CN", DesktopMode = false };
            for (int i = 0; i < 1000; i++) { TodoItem item = history.Add("历史事项 " + i); history.Toggle(item); }
            TodoWindow historyView = new TodoWindow(history);
            try
            {
                Check(historyView.Control<StackPanel>("CompletedList").Children.Count == 0 && history.Completed.Count() == 1000,
                    "1000 条已完成历史折叠时零隐藏行控件，原数据完整保留");
                historyView.Control<Expander>("CompletedSection").IsExpanded = true;
                Check(historyView.Control<StackPanel>("CompletedList").Children.OfType<Grid>().Count() == 50 && historyView.Control<Button>("CompletedLoadMore").Parent != null,
                    "展开已完成首批只创建五十条历史行，后续仍可加载而不删除原数据");
                historyView.Control<Expander>("CompletedSection").IsExpanded = false;
                Check(historyView.Control<StackPanel>("CompletedList").Children.Count == 0,
                    "重新折叠已完成释放历史行的视觉引用");
            }
            finally { historyView.Window.Close(); }
        }

        private static void CheckFeishu36(string directory, string fixture)
        {
            uint modifiers, virtualKey;
            Check(GlobalHotkeys.Parse("Ctrl+Alt+D", out modifiers, out virtualKey) && modifiers == 3 && virtualKey == 68 &&
                GlobalHotkeys.Parse("", out modifiers, out virtualKey) && virtualKey == 0 && !GlobalHotkeys.Parse("D", out modifiers, out virtualKey),
                "快捷键支持组合键与留空关闭，拒绝会拦截普通输入的单字母");
            int showEvents = 0, miniEvents = 0;
            using (GlobalHotkeys shortcuts = new GlobalHotkeys(delegate { showEvents++; }, delegate { miniEvents++; }))
            {
                Check(shortcuts.Configure("Ctrl+Alt+D", "Ctrl+Alt+D") != null && shortcuts.Configure("", "") == null,
                    "相同快捷键在注册前拒绝，关闭两项功能可以保存");
                Native.SendMessage(shortcuts.Handle, 0x0312, new IntPtr(1), IntPtr.Zero);
                Native.SendMessage(shortcuts.Handle, 0x0312, new IntPtr(2), IntPtr.Zero);
                Check(showEvents == 1 && miniEvents == 1, "稳定独立消息窗口将显示与悬浮恢复快捷键分发到不同功能");
                using (GlobalHotkeys conflict = new GlobalHotkeys(delegate { }, delegate { }))
                {
                    string reserved = null, occupied = null;
                    // Briefly reserve uncommon function-key combinations on our own
                    // message windows. Never steal an existing registration or send
                    // keyboard input to the user's applications.
                    foreach (int number in Enumerable.Range(13, 12))
                    {
                        string candidate = "Ctrl+Alt+Shift+F" + number;
                        if (reserved == null && shortcuts.Configure(candidate, "") == null) { reserved = candidate; continue; }
                        if (candidate != reserved && conflict.Configure(candidate, "") == null) { occupied = candidate; break; }
                    }
                    Check(reserved != null && occupied != null, "独立 QA 消息窗口实际注册两组空闲全局快捷键");
                    Check(shortcuts.Configure(reserved, occupied) != null && conflict.Configure(reserved, "") != null,
                        "第二组快捷键注册冲突时撤销部分注册，并实际恢复原快捷键");
                    Check(shortcuts.Configure("", "") == null && conflict.Configure(reserved, "") == null,
                        "清空快捷键实际释放系统注册，不只更新设置文字");
                }
            }
            AppState state = new AppState { Language = "zh-CN", DesktopMode = false, ThemeId = "ocean", Width = 480, Height = 640 };
            TodoGroup work = new TodoGroup { Id = "group36-work", Name = "工作项目", SortOrder = 0 }, life = new TodoGroup { Id = "group36-life", Name = "生活计划", SortOrder = 1 };
            state.Groups.Add(work); state.Groups.Add(life);
            TodoItem a = state.Add("第一项工作\n包含多行说明", work.Id), b = state.Add("拖动这条待办到其他分组", life.Id);
            for (int i = 0; i < 8; i++) state.Add("工作安排 " + (i + 1), work.Id);
            TodoWindow view = new TodoWindow(state);
            try
            {
                Grid originalRow = view.Control<StackPanel>("PendingList").Children.OfType<Grid>().First(g => g.Tag == a);
                for (int i = 0; i < 10; i++) view.Refresh();
                Check(ReferenceEquals(originalRow, view.Control<StackPanel>("PendingList").Children.OfType<Grid>().First(g => g.Tag == a)),
                    "重复刷新复用内容未改变的待办行，不重复构建整套菜单和控件");
                a.Text = "内容已更新"; view.Refresh();
                Check(!ReferenceEquals(originalRow, view.Control<StackPanel>("PendingList").Children.OfType<Grid>().First(g => g.Tag == a)),
                    "文字变化立即重建对应行，缓存不显示旧内容");
                view.MoveGroup(life.Id, work.Id, false);
                Check(state.Groups.OrderBy(g => g.SortOrder).First() == life, "主页分组可拖动排序并保存新顺序");
                Grid heading = view.Control<StackPanel>("PendingList").Children.OfType<Grid>().First(g => g.Tag == null);
                Render(view, Path.Combine(directory, "v136-group-home.png"));
                TextBlock headingText = heading.Children.OfType<TextBlock>().First(); Button headingArrow = heading.Children.OfType<Button>().Single();
                Rect headingInk = UiGlyphs.TextInk(headingText);
                Check(Math.Abs(headingText.TranslatePoint(new Point(0, headingInk.Top + headingInk.Height / 2), heading).Y -
                    headingArrow.TranslatePoint(new Point(0, headingArrow.ActualHeight / 2), heading).Y) < 1,
                    "分组标题行文本与展开箭头共用垂直中线");
                view.OpenGroups(); Render(view, Path.Combine(directory, "v136-group-management.png"));
                Border groupCard = view.Control<StackPanel>("GroupList").Children.OfType<Border>().Last();
                Check(groupCard.Child is StackPanel && ((StackPanel)groupCard.Child).Children.Count > 1 && groupCard.AllowDrop,
                    "分组管理显示组内未完成待办，并提供跨组拖放目标");
                Grid managementHeader = ((StackPanel)groupCard.Child).Children.OfType<Grid>().First();
                Check(managementHeader.Children.OfType<Button>().First().Content is System.Windows.Shapes.Ellipse,
                    "分组选择使用与界面协调的几何圆点，不再使用字形对勾");
                view.MoveManagedTask(b, work.Id);
                Check(b.GroupId == work.Id && state.PendingSort == "Manual", "管理页待办拖到另一组真实修改归属，组内显示同步更新");
                view.Remove(b); view.Control<Button>("UndoButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(!b.Deleted && b.GroupId == work.Id, "管理页快速删除仍可撤销，并恢复正确分组");
                view.Control<TextBox>("ShowHotkeyInput").Text = "Ctrl+Alt+K"; view.Control<TextBox>("MiniHotkeyInput").Text = "Ctrl+Alt+J";
                view.SaveShortcuts();
                Storage testStore = new Storage(Path.Combine(fixture, "v136-data")); testStore.Save(state);
                AppState reopened = testStore.Load();
                Check(reopened.ShowHotkey == "Ctrl+Alt+K" && reopened.MiniHotkey == "Ctrl+Alt+J" && reopened.Groups.OrderBy(g => g.SortOrder).First().Id == life.Id,
                    "快捷键与分组拖动顺序经实际 JSON 保存、读取后保持");
                state.LanSharingEnabled = true; view.RefreshImportedState();
                view.SettingsOpen = true; view.OpenSettingsCategory("LanSectionLabel");
                Render(view, Path.Combine(directory, "v136-lan-input.png"), 168);
                TextBox lanName = view.Control<TextBox>("LanDisplayNameInput"); lanName.ApplyTemplate();
                Border outline = lanName.Template.FindName("InputSurface", lanName) as Border;
                Check(outline != null && outline.Margin.Bottom >= .5 && outline.BorderThickness.Bottom >= 1.2 && outline.BorderBrush != null,
                    "局域网名称框边线向内留出抗锯齿空间并提高对比，底边不贴裁切边缘");
                view.OpenSettingsCategory("BasicSectionLabel"); view.ExpandAdvancedSettings(); Render(view, Path.Combine(directory, "v136-shortcuts-top.png"));
                view.SettingsCategoryScroll.ScrollToEnd(); Render(view, Path.Combine(directory, "v136-shortcuts-bottom.png"));
                Button shortcutSave = view.Control<Button>("SaveHotkeysButton");
                Point shortcutPosition = shortcutSave.TranslatePoint(new Point(), view.SettingsCategoryScroll);
                Check(shortcutSave.Visibility == Visibility.Visible && shortcutSave.ActualHeight > 0 && shortcutPosition.Y >= 0 && shortcutPosition.Y + shortcutSave.ActualHeight <= view.SettingsCategoryScroll.ActualHeight,
                    "默认卡片中快捷键设置可滚动到完整可见的保存按钮");
                state.DarkMode = true; view.ApplyTheme(); view.Refresh();
                Render(view, Path.Combine(directory, "v136-shortcuts-dark.png"));
                state.DarkMode = false;
            }
            finally { view.Window.Close(); }

            LanTaskInfo task = new LanTaskInfo { Id = "v136-task", Text = "管理员为这条事项设置提醒", GroupId = "work", SortOrder = 1 };
            LanRequest request = LanAdminWindow.ReminderRequest(task, true, "", "5", true, "00:00", true);
            Check(request.ReminderIntervalMinutes == 5 && request.DailyReminderMinutesPlusOne == 1 && request.ReminderManaged,
                "管理员提醒输入小时留空、只填分钟可保存，并支持每天午夜");
            bool rejected = false;
            try { LanAdminWindow.ReminderRequest(task, true, "0", "0", false, "", false); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "管理员提醒拒绝零分钟间隔，不生成无效规则");
            LanSnapshot data = new LanSnapshot { Revision = 7, PendingSort = "Manual", Groups = new List<LanGroupInfo> {
                new LanGroupInfo { Id = "work", Name = "非常长的项目分组名称检查滚动文字是否完整" } }, Tasks = new List<LanTaskInfo> {
                task, new LanTaskInfo { Id = "v136-other", Text = "另一条待办", GroupId = "work", SortOrder = 0 } } };
            Check(LanAdminWindow.OrderedPending(data).First().Id == "v136-other", "管理员页面按客户端手动顺序显示，不强制恢复时间排序");
            string prefsDirectory = Path.Combine(fixture, "v136-admin");
            LanAdminPreferences prefs = LanAdminPreferences.Load(prefsDirectory); prefs.WindowWidth = 810; prefs.WindowHeight = 860;
            prefs.DefaultReminderManaged = true; prefs.Save();
            double expectedWindowWidth = 0, expectedWindowHeight = 0;
            using (LanAdminWindow admin = new LanAdminWindow(state, prefsDirectory))
            {
                Check(admin.Window.Width == Math.Min(810, SystemParameters.WorkArea.Width) && admin.Window.Height == Math.Min(860, SystemParameters.WorkArea.Height) &&
                    admin.Preferences.DefaultReminderManaged, "管理员窗口从加密偏好恢复上次尺寸，提醒保护开关记忆保留");
                admin.LoadPeersForTest(new[] { new LanPeer { InstanceId = "v136-peer", DisplayName = "界面测试", MachineName = "QA" } }, true);
                admin.UserList.SelectedIndex = 0; admin.CodeInput.Text = "QA123456"; admin.ApplySnapshotForTest(data);
                admin.AddGroupBox.SelectedIndex = 1;
                admin.Window.Show(); Pump();
                Render(admin.Window, Path.Combine(directory, "v136-admin.png"), 821, 778);
                MarqueeLabel marquee = VisualDescendants<MarqueeLabel>(admin.AddGroupBox).FirstOrDefault();
                Check(marquee != null && marquee.Text == data.Groups[0].Name && marquee.ActualWidth > 20 &&
                    ((TextBlock)marquee.Child).Text == data.Groups[0].Name,
                    "长分组选择框实际显示完整绑定文字并创建可见滚动视口：" + (marquee == null ? "missing" : marquee.Text + " width=" + marquee.ActualWidth + " child=" + ((TextBlock)marquee.Child).Text));
                Check(((TextBlock)marquee.Child).RenderTransform.HasAnimatedProperties,
                    "长分组名称在实际可见窗口中启动滚动动画，不只提供静态裁切文字");
                Grid row = admin.PendingRows.Children.OfType<Grid>().First(g => g.Tag is LanTaskInfo);
                Check(row.AllowDrop && row.ContextMenu.Style != null && row.ContextMenu.Items.OfType<MenuItem>().Any(m => m.Header.ToString().StartsWith("设置提醒")),
                    "管理员待办支持拖动目标，右键菜单使用统一圆角样式并提供提醒入口");
                row.ContextMenu.ApplyTemplate();
                MenuItem firstMenu = row.ContextMenu.Items.OfType<MenuItem>().First(); firstMenu.ApplyTemplate();
                Check(firstMenu.Template != null && (firstMenu.Template.FindName("Surface", firstMenu) as Border) != null,
                    "管理员菜单项实际使用自定义圆角模板，而非系统菜单外观");
                RenderMenu(row.ContextMenu, Path.Combine(directory, "v136-admin-context-menu.png"));
                Check(admin.Window.Resources[SystemParameters.FocusVisualStyleKey] is Style, "管理员窗口全局去掉虚线焦点框，保留样式内键盘高亮");
                Window reminder = admin.OpenReminder(task); Pump();
                Render(reminder, Path.Combine(directory, "v136-admin-reminder.png"));
                Grid reminderRoot = (Grid)((Border)reminder.Content).Child;
                StackPanel reminderBody = (StackPanel)((ScrollViewer)reminderRoot.Children[0]).Content;
                Check(reminderBody.Children.OfType<CheckBox>().Last().IsChecked == true, "新提醒弹框实际从记忆偏好初始化不可取消开关");
                reminderBody.Children.OfType<CheckBox>().First().IsChecked = true;
                StackPanel actions = (StackPanel)reminderRoot.Children[1]; Button saveReminder = actions.Children.OfType<Button>().Last();
                TextBlock reminderMessage = reminderBody.Children.OfType<TextBlock>().Last();
                admin.SendForTest = delegate(LanPeer peer, LanRequest command) { return LanResponse.Fail("测试口令错误，请重新连接"); };
                saveReminder.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(PumpUntil(delegate { return reminderMessage.Text == "测试口令错误，请重新连接" && saveReminder.IsEnabled; }, 3000) && reminder.IsVisible,
                    "提醒保存失败在弹框内反馈并恢复保存按钮，不只在被遮挡的主窗口显示错误");
                admin.SendForTest = delegate(LanPeer peer, LanRequest command) { request = command; data.Revision++; return new LanResponse { Success = true, Snapshot = data }; };
                saveReminder.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(PumpUntil(delegate { return request.AccessCode == "QA123456" && !reminder.IsVisible; }, 3000) && request.Operation == "reminder" && request.ReminderManaged,
                    "管理员弹框保存通过统一异步同步路径发送已鉴权的提醒命令");
                if (reminder.IsVisible) reminder.Close();
                admin.Window.Width = 770; admin.Window.Height = 790; Pump();
                // Windows rounds a visible HWND to physical pixels at fractional DPI.
                // Verify that rounding stays within one pixel, then remember its real
                // logical size rather than requiring an impossible exact DIP size.
                Matrix device = PresentationSource.FromVisual(admin.Window).CompositionTarget.TransformToDevice;
                expectedWindowWidth = admin.Window.Width; expectedWindowHeight = admin.Window.Height;
                Check(Math.Abs(expectedWindowWidth - 770) <= 1 / device.M11 && Math.Abs(expectedWindowHeight - 790) <= 1 / device.M22,
                    "可见管理员窗口调整尺寸只发生一个物理像素以内的 DPI 取整");
                admin.Window.Close();
            }
            prefs = LanAdminPreferences.Load(prefsDirectory);
            Check(prefs.WindowWidth == expectedWindowWidth && prefs.WindowHeight == expectedWindowHeight,
                "管理员关闭窗口时实际保存当前尺寸供下次使用：" + prefs.WindowWidth + " × " + prefs.WindowHeight);
            using (LanAdminWindow reopened = new LanAdminWindow(state, prefsDirectory))
            {
                Check(reopened.Window.Width == Math.Min(expectedWindowWidth, SystemParameters.WorkArea.Width) &&
                    reopened.Window.Height == Math.Min(expectedWindowHeight, SystemParameters.WorkArea.Height),
                    "重新创建管理员窗口实际恢复保存尺寸，不仅检查偏好文件");
                reopened.Window.Close();
            }
            AppState darkAppearance = new AppState { DarkMode = true };
            using (LanAdminWindow darkAdmin = new LanAdminWindow(darkAppearance, Path.Combine(fixture, "v136-admin-dark")))
            {
                darkAdmin.LoadPeersForTest(new[] { new LanPeer { InstanceId = "v136-dark-peer", DisplayName = "深色测试", MachineName = "QA" } }, true);
                darkAdmin.UserList.SelectedIndex = 0; darkAdmin.ApplySnapshotForTest(data);
                Window darkReminder = darkAdmin.OpenReminder(task); Pump();
                darkReminder.Width = darkReminder.MinWidth; darkReminder.Height = darkReminder.MinHeight; Pump();
                RenderLaidOutWindow(darkReminder, Path.Combine(directory, "v136-admin-reminder-dark-min.png"));
                SolidColorBrush expectedText = (SolidColorBrush)darkAdmin.Window.Resources["AdminText"];
                Check(VisualDescendants<CheckBox>(darkReminder).All(c => ((SolidColorBrush)c.Foreground).Color == expectedText.Color),
                    "深色提醒的开关文字使用主题文字色，不出现默认黑字");
                Grid darkRoot = (Grid)((Border)darkReminder.Content).Child; StackPanel darkActions = (StackPanel)darkRoot.Children[1];
                Check(darkActions.ActualHeight > 0 && darkActions.TranslatePoint(new Point(0, darkActions.ActualHeight), darkRoot).Y <= darkRoot.ActualHeight + 1,
                    "最小管理员提醒窗口的取消和保存固定在可见范围内：" + darkRoot.ActualWidth + " × " + darkRoot.ActualHeight);
                StackPanel darkBody = (StackPanel)((ScrollViewer)darkRoot.Children[0]).Content;
                Grid darkInterval = darkBody.Children.OfType<Grid>().First();
                Check(darkInterval.TranslatePoint(new Point(darkInterval.ActualWidth, 0), darkRoot).X <= darkRoot.ActualWidth + 1,
                    "最小提醒窗口的小时分钟输入区域不超出客户区");
                Grid darkRow = darkAdmin.PendingRows.Children.OfType<Grid>().First(g => g.Tag is LanTaskInfo);
                RenderMenu(darkRow.ContextMenu, Path.Combine(directory, "v136-admin-context-menu-dark.png"));
                darkReminder.Close(); darkAdmin.Window.Close();
            }
        }
        private static void CheckFeishu37(string directory, string fixture)
        {
            AppState state = new AppState { Language = "zh-CN", DesktopMode = false, ThemeId = "ocean", Width = 480, Height = 720 };
            TodoGroup work = new TodoGroup { Id = "work37", Name = "工作项目", SortOrder = 0 }, life = new TodoGroup { Id = "life37", Name = "生活计划", SortOrder = 1 };
            state.Groups.Add(work); state.Groups.Add(life);
            TodoItem a = state.Add("工作事项一", work.Id), b = state.Add("工作事项二", work.Id), c = state.Add("生活事项", life.Id);
            TodoItem done = state.Add("已完成的测试事项"); state.Toggle(done);
            TodoWindow view = new TodoWindow(state);
            try
            {
                view.Window.Show(); Pump();
                FrameworkElement root = (FrameworkElement)view.Window.Content;
                StackPanel rows = view.Control<StackPanel>("PendingList");
                Grid first = rows.Children.OfType<Grid>().First(g => g.Tag == null && UiDrag.IsSource(g)), last = rows.Children.OfType<Grid>().Last(g => g.Tag == null && UiDrag.IsSource(g));
                Button fold = first.Children.OfType<Button>().Single(); TextBlock label = first.Children.OfType<TextBlock>().First();
                Rect ink = UiGlyphs.TextInk(label); Grid icon = (Grid)fold.Content; System.Windows.Shapes.Path chevron = icon.Children.OfType<System.Windows.Shapes.Path>().Single();
                double inkCenter = label.TranslatePoint(new Point(0, ink.Top + ink.Height / 2), first).Y;
                double iconCenter = chevron.TranslatePoint(new Point(8, 8), first).Y, buttonCenter = fold.TranslatePoint(new Point(0, fold.ActualHeight / 2), first).Y;
                RenderDragWindow(view.Window, Path.Combine(directory, "v137-home-centred.png"));
                Check(Math.Abs(inkCenter - iconCenter) < .7 && Math.Abs(iconCenter - buttonCenter) < .7 && icon.RenderTransform.Value.IsIdentity,
                    "分组文字的实际字形、箭头笔画及按钮外框共用中线，无任意下移补丁：" + inkCenter + "/" + iconCenter + "/" + buttonCenter);
                ToolTipEventArgs hint = (ToolTipEventArgs)Activator.CreateInstance(typeof(ToolTipEventArgs), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                    null, new object[] { true }, null); hint.RoutedEvent = ToolTipService.ToolTipOpeningEvent; fold.RaiseEvent(hint);
                Check(hint.Handled && !String.IsNullOrEmpty(System.Windows.Automation.AutomationProperties.GetName(fold)),
                    "动态分组按钮实际拦截直接路由的悬停提示，同时保留无障碍名称");
                Expander completed = view.Control<Expander>("CompletedSection"); completed.ApplyTemplate();
                ToggleButton toggle = completed.Template.FindName("HeaderToggle", completed) as ToggleButton; toggle.ApplyTemplate();
                System.Windows.Shapes.Path completedArrow = toggle.Template.FindName("Arrow", toggle) as System.Windows.Shapes.Path;
                TextBlock completedText = view.Control<TextBlock>("CompletedHeader"); Rect completedInk = UiGlyphs.TextInk(completedText);
                Check(Math.Abs(completedArrow.TranslatePoint(new Point(8, 8), root).Y - completedText.TranslatePoint(new Point(0, completedInk.Top + completedInk.Height / 2), root).Y) < .8,
                    "主页已完成箭头与实际中文笔画上下居中");
                RenderLaidOutWindow(view.Window, Path.Combine(directory, "v137-home-centred.png"));
                Point start = first.TranslatePoint(new Point(first.ActualWidth / 2, 13), root);
                Point destination = last.TranslatePoint(new Point(last.ActualWidth / 2, last.ActualHeight - 2), root);
                using (UiDrag.Session drag = UiDrag.BeginForTest(first, start))
                {
                    drag.Update(destination); Pump();
                    Check(drag.HasGhost && !drag.Marker.IsEmpty && drag.AnimatedElements > 0 && first.Opacity < .2,
                        "主页分组使用真实浮动快照、插入标记和相邻行让位动画：ghost=" + drag.HasGhost + ";marker=" + drag.Marker + ";motion=" + drag.AnimatedElements);
                    DateTime frameAt = DateTime.UtcNow.AddMilliseconds(90); PumpUntil(delegate { return DateTime.UtcNow >= frameAt; }, 700);
                    Check(drag.PreviewDisplacement > 1, "让位动画真实随渲染时钟推进，不只是创建了动画对象");
                    RenderDragWindow(view.Window, Path.Combine(directory, "v137-group-drag.png"));
                    drag.Complete(false);
                }
                Check(state.Groups.OrderBy(g => g.SortOrder).First() == work && first.Opacity == 1 && !UiDrag.IsActive,
                    "取消分组拖动恢复透明度、变换和原顺序，不写入预览位置");
                Pump();
                start = first.TranslatePoint(new Point(first.ActualWidth / 2, 13), root);
                destination = last.TranslatePoint(new Point(last.ActualWidth / 2, last.ActualHeight - 2), root);
                using (UiDrag.Session drag = UiDrag.BeginForTest(first, start))
                {
                    drag.Update(destination);
                    Check(!drag.Marker.IsEmpty, "取消后再次拖动仍可定位插入目标：start=" + start + ";end=" + destination + ";marker=" + drag.Marker);
                    drag.Complete(true);
                }
                Check(state.Groups.OrderBy(g => g.SortOrder).First() == life, "分组松手才提交排序，移动整组而不改变事项归属");
                Pump(); first = rows.Children.OfType<Grid>().First(g => g.Tag == null && UiDrag.IsSource(g));
                using (UiDrag.Session drag = UiDrag.BeginForTest(first, first.TranslatePoint(new Point(50, 12), root)))
                { drag.Update(new Point(-40, -40)); Check(drag.Marker.IsEmpty, "拖到窗口外不保留误导性的插入目标"); drag.Complete(true); }
                Check(state.Groups.OrderBy(g => g.SortOrder).First() == life && !UiDrag.IsActive, "无效位置松手不改组顺序并清理拖动状态");
                view.OpenGroups(); AdvanceFrames(240);
                StackPanel groups = view.Control<StackPanel>("GroupList");
                Border workCard = groups.Children.OfType<Border>().Last(), lifeCard = groups.Children.OfType<Border>().Skip(1).First();
                Grid workHeader = ((StackPanel)workCard.Child).Children.OfType<Grid>().First();
                TextBlock grip = workHeader.Children.OfType<TextBlock>().Single();
                using (UiDrag.Session drag = UiDrag.BeginForTest(grip, grip.TranslatePoint(new Point(5, 10), root)))
                {
                    drag.Update(lifeCard.TranslatePoint(new Point(80, 4), root));
                    Check(drag.HasGhost && drag.AnimatedElements > 0, "分组管理拖动的是整张分组卡片快照，其他分组平滑让位");
                    AdvanceFrames(90);
                    RenderDragWindow(view.Window, Path.Combine(directory, "v137-management-group-drag.png"));
                    drag.Complete(false);
                }
                Grid sourceTask = ((StackPanel)workCard.Child).Children.OfType<Grid>().First(g => g.Tag == a);
                Grid targetTask = ((StackPanel)lifeCard.Child).Children.OfType<Grid>().First(g => g.Tag == c);
                using (UiDrag.Session drag = UiDrag.BeginForTest(sourceTask, sourceTask.TranslatePoint(new Point(30, 15), root)))
                {
                    drag.Update(targetTask.TranslatePoint(new Point(40, 4), root));
                    Check(drag.HasGhost && !drag.Marker.IsEmpty && drag.AnimatedElements > 0, "管理页跨组事项拖动也有浮动快照、目标动画和准确的插入位置：ghost=" + drag.HasGhost + ";marker=" + drag.Marker + ";motion=" + drag.AnimatedElements + ";source=" + sourceTask.TranslatePoint(new Point(), root) + ";target=" + targetTask.TranslatePoint(new Point(40, 4), root));
                    AdvanceFrames(90);
                    RenderDragWindow(view.Window, Path.Combine(directory, "v137-management-task-drag.png")); drag.Complete(true);
                }
                Check(a.GroupId == life.Id && a.SortOrder < c.SortOrder && b.GroupId == work.Id, "管理页拖到某条事项上可指定前后顺序，未移动的归属保留");
                Pump();
                groups = view.Control<StackPanel>("GroupList"); lifeCard = groups.Children.OfType<Border>().Skip(1).First();
                sourceTask = ((StackPanel)lifeCard.Child).Children.OfType<Grid>().First(g => g.Tag == a);
                targetTask = ((StackPanel)lifeCard.Child).Children.OfType<Grid>().First(g => g.Tag == c);
                using (UiDrag.Session drag = UiDrag.BeginForTest(sourceTask, sourceTask.TranslatePoint(new Point(30, 15), root)))
                { drag.Update(targetTask.TranslatePoint(new Point(40, targetTask.ActualHeight - 3), root)); Check(drag.AnimatedElements > 0, "管理页同组待办拖动有相邻行让位动画"); drag.Complete(true); }
                Check(a.SortOrder > c.SortOrder, "管理页同组事项排序真实保存，不只显示动画");
                new Storage(Path.Combine(fixture, "v137-order")).Save(state);
                AppState reopened = new Storage(Path.Combine(fixture, "v137-order")).Load();
                Check(reopened.Items.Single(t => t.Id == a.Id).GroupId == life.Id && reopened.Pending.First(t => t.GroupId == life.Id).Id == c.Id,
                    "新增管理页排序与跨组归属经实际备份文件读写后仍正确");
                a.Pinned = true; string originalGroup = a.GroupId; int originalSort = a.SortOrder;
                view.MoveManagedTask(a, work.Id, b, false);
                Check(a.GroupId == originalGroup && a.SortOrder == originalSort, "管理页新排序入口也拒绝跨置顶层级插入，原事项不丢失"); a.Pinned = false;
                for (int i = 0; i < 28; i++) state.Add("滚动拖动测试 " + i, life.Id);
                view.OpenGroups(); AdvanceFrames(240); groups = view.Control<StackPanel>("GroupList");
                ScrollViewer groupScroll = VisualDescendants<ScrollViewer>(view.Control<FrameworkElement>("GroupPanel")).First(s => s.Content == groups);
                lifeCard = groups.Children.OfType<Border>().Skip(1).First();
                sourceTask = ((StackPanel)lifeCard.Child).Children.OfType<Grid>().First(g => g.Tag == c);
                using (UiDrag.Session drag = UiDrag.BeginForTest(sourceTask, sourceTask.TranslatePoint(new Point(30, 15), root)))
                {
                    drag.Update(groupScroll.TranslatePoint(new Point(groupScroll.ActualWidth / 2, groupScroll.ActualHeight - 8), root)); AdvanceFrames(160);
                    Check(groupScroll.VerticalOffset > 0 && drag.HasGhost, "长列表拖到边缘实际自动滚动，浮动反馈持续显示"); drag.Complete(false);
                }
                double stoppedAt = groupScroll.VerticalOffset; AdvanceFrames(110);
                Check(Math.Abs(groupScroll.VerticalOffset - stoppedAt) < .1 && c.GroupId == life.Id && !UiDrag.IsActive,
                    "取消拖动停止自动滚动时钟、恢复视觉且不修改跨组归属");
            }
            finally { view.Window.Close(); }

            LanTaskInfo remoteA = new LanTaskInfo { Id = "a37", Text = "管理员测试事项一", GroupId = "remote-work", SortOrder = 0 },
                remoteB = new LanTaskInfo { Id = "b37", Text = "管理员测试事项二", GroupId = "remote-work", SortOrder = 1 };
            LanSnapshot data = new LanSnapshot { Revision = 15, PendingSort = "Manual", Groups = new List<LanGroupInfo> {
                new LanGroupInfo { Id = "remote-work", Name = "工作" }, new LanGroupInfo { Id = "remote-life", Name = "生活" } }, Tasks = new List<LanTaskInfo> {
                    remoteA, remoteB, new LanTaskInfo { Id = "c37", Text = "另一组事项", GroupId = "remote-life", SortOrder = 2 },
                    new LanTaskInfo { Id = "d37", Text = "完成事项", Completed = true, CompletedAt = DateTime.UtcNow.Ticks } } };
            using (LanAdminWindow admin = new LanAdminWindow(state, Path.Combine(fixture, "v137-admin")))
            {
                LanPeer peer = new LanPeer { InstanceId = "peer37", DisplayName = "Administrator", MachineName = "QA" };
                admin.LoadPeersForTest(new[] { peer }, true); admin.UserList.SelectedIndex = 0; admin.CodeInput.Text = "QA123456"; admin.ApplySnapshotForTest(data);
                admin.Window.Show(); Pump(); FrameworkElement root = (FrameworkElement)admin.Window.Content;
                Window rename = admin.OpenRename(peer); Pump();
                TextBox renameInput = VisualDescendants<TextBox>(rename).Single(); renameInput.ApplyTemplate();
                ScrollViewer host = renameInput.Template.FindName("PART_ContentHost", renameInput) as ScrollViewer;
                Check(host.VerticalAlignment == VerticalAlignment.Center && Math.Abs(host.TranslatePoint(new Point(0, host.ActualHeight / 2), renameInput).Y - renameInput.ActualHeight / 2) < .6,
                    "重命名框的真实文字宿主垂直居中，不再贴上边缘");
                RenderDragWindow(rename, Path.Combine(directory, "v137-rename.png")); rename.Close();
                ContextMenuEventArgs menuOpening = (ContextMenuEventArgs)Activator.CreateInstance(typeof(ContextMenuEventArgs),
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null, new object[] { admin.UserList, true }, null);
                admin.UserList.RaiseEvent(menuOpening);
                ContextMenu userMenu = admin.UserList.ContextMenu; RenderMenu(userMenu, Path.Combine(directory, "v137-user-menu.png"));
                Separator separator = userMenu.Items.OfType<Separator>().Single(); separator.ApplyTemplate();
                Border divider = VisualDescendants<Border>(separator).Single();
                Check(separator.Margin.Left == 0 && separator.Margin.Right == 0 && divider.Margin.Left == divider.Margin.Right && divider.ActualWidth > 80,
                    "管理员用户菜单分隔线左右等距，移除系统遗留图标槽缩进：" + separator.Margin + ";line=" + divider.ActualWidth);
                StackPanel completedLabel = (StackPanel)admin.CompletedToggle.Content;
                System.Windows.Shapes.Path arrow = VisualDescendants<System.Windows.Shapes.Path>(completedLabel).Single();
                Check(arrow.Data.ToString() == System.Windows.Media.Geometry.Parse(UiGlyphs.ChevronRight).ToString(), "管理员已完成折叠箭头与主页采用同一几何路径");
                admin.CompletedToggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump();
                completedLabel = (StackPanel)admin.CompletedToggle.Content; arrow = VisualDescendants<System.Windows.Shapes.Path>(completedLabel).Single();
                Check(arrow.Data.ToString() == System.Windows.Media.Geometry.Parse(UiGlyphs.ChevronDown).ToString(), "管理员已完成展开箭头与主页采用同一几何路径");
                Grid source = admin.PendingRows.Children.OfType<Grid>().First(g => g.Tag == remoteA), target = admin.PendingRows.Children.OfType<Grid>().First(g => g.Tag == remoteB);
                LanRequest sent = null;
                admin.SendForTest = delegate(LanPeer p, LanRequest request) { sent = request; data.Revision++; return new LanResponse { Success = true, Snapshot = data }; };
                using (UiDrag.Session drag = UiDrag.BeginForTest(source, source.TranslatePoint(new Point(60, 15), root)))
                {
                    drag.Update(target.TranslatePoint(new Point(60, target.ActualHeight - 2), root));
                    Check(drag.HasGhost && drag.AnimatedElements > 0 && !drag.Marker.IsEmpty && sent == null,
                        "管理员待办拖动显示快照、动画及插入标记，预览阶段不发网络写命令");
                    AdvanceFrames(90);
                    Check(GlyphDrawings(drag.DrawingSnapshot).Any() &&
                        GlyphSignature(drag.DrawingSnapshot) == GlyphSignature(UiDrag.CaptureDrawing(source)),
                        "管理员清单也使用原生字形快照，拖动不改变字体、字号和字符宽度");
                    RenderDragWindow(admin.Window, Path.Combine(directory, "v137-admin-task-drag.png")); drag.Complete(true);
                }
                Check(PumpUntil(delegate { return sent != null && admin.StatusText.Text.StartsWith("修改已同步"); }, 3000) &&
                    sent.Operation == "reorder" && sent.ItemId == remoteA.Id && sent.TargetId == remoteB.Id && sent.After && sent.ExpectedRevisionPlusOne == 16,
                    "管理员拖动松手只发送一次带修订保护的准确排序命令");
                source = admin.PendingRows.Children.OfType<Grid>().First(g => g.Tag == null && UiDrag.IsSource(g)); target = admin.PendingRows.Children.OfType<Grid>().Last(g => g.Tag == null && UiDrag.IsSource(g));
                using (UiDrag.Session drag = UiDrag.BeginForTest(source, source.TranslatePoint(new Point(60, 12), root)))
                { drag.Update(target.TranslatePoint(new Point(60, target.ActualHeight - 2), root)); Check(drag.HasGhost && drag.AnimatedElements > 0, "管理员分组标题拖动也移动整组并显示让位动画"); AdvanceFrames(90); RenderDragWindow(admin.Window, Path.Combine(directory, "v137-admin-group-drag.png")); drag.Complete(false); }
                admin.OpenEditForTest(remoteA); Pump();
                Check(((SolidColorBrush)admin.EditLayer.Background).Color == ((SolidColorBrush)admin.Window.Resources["AdminCard"]).Color && admin.EditTextInput.ActualHeight > 180 && admin.EditTextInput.ActualWidth > 250,
                    "管理员编辑不再使用暗色遮罩，默认窗口编辑框扩大至可用区域");
                RenderLaidOutWindow(admin.Window, Path.Combine(directory, "v137-admin-edit.png"));
                admin.Window.Width = 680; admin.Window.Height = 460; AdvanceFrames(230);
                Check(admin.EditTextInput.ActualHeight >= 40 && admin.EditSaveButton.TranslatePoint(new Point(0, admin.EditSaveButton.ActualHeight), admin.EditLayer).Y <= admin.EditLayer.ActualHeight,
                    "扩大后的编辑框在最小管理员窗口仍完整显示输入和保存按钮");
                RenderLaidOutWindow(admin.Window, Path.Combine(directory, "v137-admin-edit-min.png"));
                Check(!admin.PendingRows.IsVisible && admin.UserList.IsVisible, "编辑只隐藏右侧原清单，左侧用户列表保持正常且无背景文字穿透");
                Window reminder = admin.OpenReminder(remoteA); Pump();
                Grid reminderRoot = (Grid)((Border)reminder.Content).Child; StackPanel body = (StackPanel)((ScrollViewer)reminderRoot.Children[0]).Content;
                Check(body.Children.OfType<CheckBox>().Count() == 3 && body.Children.OfType<CheckBox>().All(t => t.Style == reminder.Resources["AdminSwitch"]),
                    "管理员提醒以三个主题开关替代任务完成式勾选框，操作逻辑与主页一致");
                body.Children.OfType<CheckBox>().First().IsChecked = true;
                Grid interval = body.Children.OfType<Grid>().Single(g => g.Name == "IntervalInputs");
                TextBox[] inputs = VisualDescendants<TextBox>(interval).ToArray(); inputs[0].Text = ""; inputs[1].Text = "5"; AdvanceFrames(230);
                Check(interval.Visibility == Visibility.Visible && inputs[0].Text == "" && inputs[1].Text == "5", "管理员提醒开启后显示小时分钟输入，小时为空的草稿不强行补零");
                Check(body.Children.OfType<TextBlock>().Any(t => t.Text.Contains("每隔 5 分钟")), "管理员提醒显示清晰的当前规则摘要");
                Check(VisualDescendants<System.Windows.Shapes.Ellipse>(body.Children.OfType<CheckBox>().First()).Single().RenderTransform.Value.OffsetX > 15.5,
                    "提醒开关实际动画到开启端，视觉状态与规则一致");
                RenderDragWindow(reminder, Path.Combine(directory, "v137-admin-reminder-on.png"));
                reminder.Width = reminder.MinWidth; reminder.Height = reminder.MinHeight; Pump();
                ScrollViewer reminderScroll = (ScrollViewer)reminderRoot.Children[0]; reminderScroll.ScrollToEnd(); AdvanceFrames(230);
                TextBlock minuteUnit = VisualDescendants<TextBlock>(interval).Single(t => t.Text == "分钟");
                Check(minuteUnit.TranslatePoint(new Point(minuteUnit.ActualWidth, 0), reminderScroll).X <= reminderScroll.ViewportWidth + .5,
                    "最小提醒窗口的分钟文字完整处于滚动视口内，不藏在滚动条后：right=" + minuteUnit.TranslatePoint(new Point(minuteUnit.ActualWidth, 0), reminderScroll).X + ";viewport=" + reminderScroll.ViewportWidth);
                RenderDragWindow(reminder, Path.Combine(directory, "v137-admin-reminder-min.png"));
                StackPanel actions = (StackPanel)reminderRoot.Children[1];
                Check(actions.Children.Count == 3 && actions.TranslatePoint(new Point(actions.ActualWidth, actions.ActualHeight), reminderRoot).X <= reminderRoot.ActualWidth + 1,
                    "管理员提醒的清除、取消、保存三个操作在最小窗口均不裁切");
                sent = null; inputs[1].Text = "0";
                actions.Children.OfType<Button>().Last().RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump();
                Check(sent == null && reminder.IsVisible && actions.Children.OfType<Button>().Last().IsEnabled && body.Children.OfType<TextBlock>().Last().Text.Contains("1 分钟"),
                    "零间隔在弹框内明确报错，保留输入且不发送无效网络写入");
                inputs[1].Text = "5";
                sent = null; actions.Children.OfType<Button>().Last().RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(PumpUntil(delegate { return !reminder.IsVisible; }, 3000) && sent != null && sent.ReminderIntervalMinutes == 5,
                    "新版管理员提醒真实异步保存仅分钟规则，不只是修改展示");
                VisualDescendants<Button>(admin.EditLayer).First(button => button.Content as string == "取消").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump();
                Check(admin.PendingRows.IsVisible && admin.EditLayer.Visibility == Visibility.Collapsed, "退出编辑恢复清单和操作，不留下隐藏控件");
                admin.Window.Width = 760; admin.Window.Height = 820; Pump();
                source = admin.PendingRows.Children.OfType<Grid>().First(g => g.Tag is LanTaskInfo);
                LanSnapshot newest = new LanSnapshot { Revision = data.Revision + 4, DisableGroups = false, Groups = data.Groups, Tasks = data.Tasks, PendingSort = "Manual" };
                LanSnapshot late = new LanSnapshot { Revision = data.Revision + 2, DisableGroups = true, Groups = data.Groups, Tasks = data.Tasks, PendingSort = "Manual" };
                using (UiDrag.Session drag = UiDrag.BeginForTest(source, source.TranslatePoint(new Point(60, 15), root)))
                {
                    admin.ApplySnapshotForTest(newest); admin.ApplySnapshotForTest(late); admin.ApplyDeferredSnapshot();
                    Check(admin.PendingRows.Children.Contains(source), "网络快照返回时不拆掉正在拖动的实际行控件"); drag.Complete(false);
                }
                admin.ApplyDeferredSnapshot();
                Check(admin.AddGroupBox.Visibility == Visibility.Visible, "拖动结束采用暂存的最高修订，乱序网络响应不能回退界面");
                admin.OpenEditForTest(remoteA); admin.EditTextInput.Text = "未保存的管理员草稿";
                LanSnapshot changed = new LanSnapshot { Revision = newest.Revision + 1, Groups = data.Groups, Tasks = data.Tasks, PendingSort = "Manual" };
                admin.ApplySnapshotForTest(changed); Pump();
                Check(admin.EditTextInput.Text == "未保存的管理员草稿" && VisualDescendants<ComboBox>(admin.EditLayer).Single().SelectedItem is LanGroupInfo,
                    "自动刷新保留管理员编辑草稿与分组选择，不刷新成空白");
                sent = null; admin.SendForTest = delegate(LanPeer p, LanRequest request) { sent = request; return LanResponse.Fail("数据已变化，请重新打开编辑"); };
                admin.EditSaveButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(PumpUntil(delegate { return sent != null && admin.StatusText.Text.Contains("重新打开编辑"); }, 3000) && sent.ExpectedRevisionPlusOne == newest.Revision + 1 &&
                    admin.EditLayer.Visibility == Visibility.Visible && admin.EditTextInput.Text == "未保存的管理员草稿",
                    "管理员编辑提交使用打开时修订，冲突后保留草稿而非静默覆盖新数据");
                LanSnapshot disabled = new LanSnapshot { Revision = changed.Revision + 1, DisableGroups = true, Groups = data.Groups, Tasks = data.Tasks, PendingSort = "Manual" };
                admin.ApplySnapshotForTest(disabled); Pump();
                Check(admin.AddGroupBox.Visibility == Visibility.Collapsed && admin.PendingRows.Children.OfType<Grid>().All(g => g.Tag is LanTaskInfo) &&
                    admin.PendingRows.Children.OfType<Grid>().First().ContextMenu.Items.OfType<MenuItem>().All(m => m.Header.ToString() != "移动到分组"),
                    "客户端关闭分组时管理员也隐藏分组入口，避免旧归属约束与界面相矛盾");
                admin.ApplySnapshotForTest(data);
                Check(admin.AddGroupBox.Visibility == Visibility.Collapsed, "延迟返回的旧修订不会覆盖新快照，防止拖动结束后倒退");
            }
            using (LanAdminWindow dark = new LanAdminWindow(new AppState { DarkMode = true }, Path.Combine(fixture, "v137-dark")))
            {
                LanTaskInfo task = new LanTaskInfo { Id = "dark37", Text = "深色下同时开启两种提醒", ReminderIntervalMinutes = 65,
                    DailyReminderMinutesPlusOne = 1440, ReminderManaged = true };
                Window reminder = dark.OpenReminder(task); AdvanceFrames(230);
                Grid root = (Grid)((Border)reminder.Content).Child;
                StackPanel body = (StackPanel)((ScrollViewer)root.Children[0]).Content;
                Check(body.Children.OfType<CheckBox>().All(cbox => cbox.IsChecked == true && VisualDescendants<System.Windows.Shapes.Ellipse>(cbox).Single().RenderTransform.Value.OffsetX > 15.5),
                    "深色提醒初始开启状态真实落在右端，三种开关与已有规则一致");
                FrameworkElement dailySpin = body.Children.OfType<Grid>().Single(g => g.Name != "IntervalInputs");
                TextBox daily = VisualDescendants<TextBox>(dailySpin).Single();
                VisualDescendants<Button>(dailySpin).First().RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(daily.Text == "00:00" && body.Children.OfType<TextBlock>().Any(t => t.Text.Contains("每天 00:00")),
                    "每天时间加减跨午夜正确循环，规则摘要同步而不出现 24:00");
                reminder.Width = reminder.MinWidth; reminder.Height = reminder.MinHeight; AdvanceFrames(230);
                ((ScrollViewer)root.Children[0]).ScrollToEnd(); AdvanceFrames(80);
                foreach (int dpi in new[] { 96, 120, 168, 192 }) RenderLaidOutWindow(reminder, Path.Combine(directory, "v137-reminder-dark-" + dpi + "dpi.png"), dpi);
                StackPanel actions = (StackPanel)root.Children[1];
                Check(actions.TranslatePoint(new Point(actions.ActualWidth, actions.ActualHeight), root).X <= root.ActualWidth + 1 &&
                    actions.TranslatePoint(new Point(actions.ActualWidth, actions.ActualHeight), root).Y <= root.ActualHeight + 1,
                    "深色两种提醒同时开启时正文可滚动，最小尺寸底部三按钮完整显示");
                reminder.Close();
            }
        }
        private static void CheckFeishu38(string directory, string fixture)
        {
            AppState state = new AppState { Language = "zh-CN", DesktopMode = false, ThemeId = "ocean", Width = 640, Height = 1040 };
            TodoGroup work = new TodoGroup { Id = "work38", Name = "工作", SortOrder = 0 }, samples = new TodoGroup { Id = "samples38", Name = "DQ112", SortOrder = 1 };
            state.Groups.Add(work); state.Groups.Add(samples);
            state.Add("供电底座问题点", work.Id); state.Add("挡风夹项目推进", work.Id); state.Add("扭簧、销钉样品", samples.Id);
            for (int i = 0; i < 7; i++) state.Toggle(state.Add("历史事项 " + i));
            TodoWindow view = new TodoWindow(state); int changes = 0; view.Changed = delegate { changes++; };
            try
            {
                view.Window.Show(); AdvanceFrames(260);
                FrameworkElement root = (FrameworkElement)view.Window.Content;
                Grid dock = view.Control<Grid>("CompletedDock"), area = view.Control<Grid>("TaskArea");
                Border progress = view.Control<Border>("ProgressTrack");
                ScrollViewer pending = view.Control<ScrollViewer>("PendingScroll"), history = view.Control<ScrollViewer>("CompletedScroll");
                Expander completed = view.Control<Expander>("CompletedSection");
                Func<FrameworkElement, Rect> rect = delegate(FrameworkElement element) { return element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize)); };
                RenderLaidOutWindow(view.Window, Path.Combine(directory, "v138-home-bottom.png"));
                Check(Math.Abs(rect(dock).Bottom - rect(area).Bottom) <= view.ContentScale + .5 && rect(dock).Bottom <= rect(progress).Top &&
                    rect(progress).Top - rect(dock).Bottom <= 20 * view.ContentScale,
                    "已完成固定在主页底部、进度条上方，不再紧跟最后一条未完成：dock=" + rect(dock) + ";area=" + rect(area) + ";progress=" + rect(progress) + ";scale=" + view.ContentScale);
                Check(!VisualDescendants<Expander>(pending).Contains(completed) && view.Control<StackPanel>("CompletedList").Children.Count == 0,
                    "折叠历史不构建旧行；未完成滚动区与已完成底栏分离");
                RenderLaidOutWindow(view.Window, Path.Combine(directory, "v138-home-bottom.png"));
                view.OpenGroups(); AdvanceFrames(260);
                StackPanel list = view.Control<StackPanel>("GroupList"); ScrollViewer scroll = view.Control<ScrollViewer>("GroupScroll");
                Border source = list.Children.OfType<Border>().Skip(1).First(), other = list.Children.OfType<Border>().Last();
                TextBlock grip = ((StackPanel)source.Child).Children.OfType<Grid>().First().Children.OfType<TextBlock>().Single();
                double initialTop = rect(source).Top;
                Point tail = scroll.TranslatePoint(new Point(scroll.ActualWidth / 2, scroll.ActualHeight - 36), root);
                Check(tail.Y > rect(other).Bottom + 10, "复现截图：两组下面存在可拖放的空白区域");
                using (UiDrag.Session drag = UiDrag.BeginForTest(grip, grip.TranslatePoint(new Point(4, 10), root)))
                {
                    drag.Update(other.TranslatePoint(new Point(80, other.ActualHeight - 3), root)); AdvanceFrames(220);
                    double displaced = rect(other).Top; Rect marker = drag.Marker;
                    drag.Update(tail); AdvanceFrames(90);
                    Check(!drag.Marker.IsEmpty && Math.Abs(rect(other).Top - displaced) < 1 && Math.Abs(rect(other).Top - initialTop) < 1.5,
                        "拖入底部空白区保留末尾插入位置与让位，不回弹、不留下原分组高度的大空洞");
                    for (int i = 0; i < 60; i++) drag.Update(new Point(tail.X + i % 3, tail.Y - i % 7));
                    Check(drag.Marker == marker && source.Opacity == 0 && changes == 0, "末尾空白区连续移动不跳落点，预览不写入排序");
                    RenderDragWindow(view.Window, Path.Combine(directory, "v138-group-tail-preview.png")); drag.Complete(false);
                }
                Check(state.Groups.OrderBy(g => g.SortOrder).First() == work && source.Opacity == 1 && other.RenderTransform.Value.IsIdentity && other.CacheMode == null && !UiDrag.IsActive,
                    "取消末尾拖动恢复组顺序、透明度、位移及短期缓存");
                using (UiDrag.Session drag = UiDrag.BeginForTest(grip, grip.TranslatePoint(new Point(4, 10), root)))
                {
                    for (int i = 0; i < 1000; i++) drag.Queue(new Point(tail.X, tail.Y - i % 4));
                    Check(drag.HasQueuedFrame && drag.FrameUpdates == 0, "1000 个鼠标样本只排队一个渲染回调，不逐次重绘");
                    AdvanceFrames(60);
                    Check(!drag.HasQueuedFrame && drag.FrameUpdates == 1 && !drag.Marker.IsEmpty, "合并到一次实际渲染帧且采用最新指针位置");
                    drag.Queue(new Point(-40, -40)); drag.Flush(tail);
                    Check(!drag.HasQueuedFrame && !drag.Marker.IsEmpty && drag.FrameUpdates == 1, "松手同步采用最终位置，未渲染的旧样本不能覆盖提交");
                    drag.Complete(true);
                    Check(!drag.HasQueuedFrame && !UiDrag.IsActive, "提交后卸载指针渲染订阅，不留下后台重绘");
                }
                Check(state.Groups.OrderBy(g => g.SortOrder).Last() == work && changes == 1,
                    "从第一组拖到空白底部实际移动到最后，且只提交一次");
                Storage store = new Storage(Path.Combine(fixture, "v138-order")); store.Save(state);
                Check(store.Load().Groups.OrderBy(g => g.SortOrder).Last().Id == work.Id, "底部落点排序写入文件，重开仍在最后");
                AdvanceFrames(40); list = view.Control<StackPanel>("GroupList"); source = list.Children.OfType<Border>().Last();
                grip = ((StackPanel)source.Child).Children.OfType<Grid>().First().Children.OfType<TextBlock>().Single();
                Point outside = scroll.TranslatePoint(new Point(scroll.ActualWidth / 2, scroll.ActualHeight + 6), root);
                using (UiDrag.Session drag = UiDrag.BeginForTest(grip, grip.TranslatePoint(new Point(4, 10), root)))
                { drag.Update(outside); Check(drag.Marker.IsEmpty, "列表外的说明栏不冒充底部落点"); drag.Complete(true); }
                Check(changes == 1, "无效位置松手不保存、不重排");
                Border ungrouped = list.Children.OfType<Border>().First(), first = list.Children.OfType<Border>().Skip(1).First();
                Point gap = new Point(rect(first).Left + 40, (rect(ungrouped).Bottom + rect(first).Top) / 2);
                using (UiDrag.Session drag = UiDrag.BeginForTest(grip, grip.TranslatePoint(new Point(4, 10), root)))
                { drag.Update(gap); Check(!drag.Marker.IsEmpty, "分组卡片之间的间隙也能放置，不要求必须压在卡片上"); drag.Complete(true); }
                Check(state.Groups.OrderBy(g => g.SortOrder).First() == work && changes == 2 && list.Children.OfType<Border>().Count() == 3,
                    "间隙松手正确放到前面，未分组始终固定，不重复生成组");
                view.Control<Button>("GroupBack").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); AdvanceFrames(260);
                Grid heading = view.Control<StackPanel>("PendingList").Children.OfType<Grid>().First(g => g.Tag == null && UiDrag.IsSource(g));
                Point homeTail = pending.TranslatePoint(new Point(pending.ActualWidth / 2, pending.ActualHeight - 36), root);
                using (UiDrag.Session drag = UiDrag.BeginForTest(heading, heading.TranslatePoint(new Point(80, 13), root)))
                { drag.Update(homeTail); Check(!drag.Marker.IsEmpty, "主页分组的底部空白区同样支持放到最后"); drag.Complete(true); }
                Check(state.Groups.OrderBy(g => g.SortOrder).Last() == work, "主页整组末尾拖动保持组内事项归属");
                AdvanceFrames(50);
                StackPanel homeRows = view.Control<StackPanel>("PendingList");
                Grid taskRow = homeRows.Children.OfType<Grid>().First(g => g.Tag is TodoItem && ((TodoItem)g.Tag).GroupId == work.Id);
                Grid beside = homeRows.Children.OfType<Grid>().Last(g => g.Tag is TodoItem && ((TodoItem)g.Tag).GroupId == work.Id);
                TodoItem movingTask = (TodoItem)taskRow.Tag, besideTask = (TodoItem)beside.Tag;
                int sourceIndex = homeRows.Children.IndexOf(taskRow), beforeTaskChange = changes; string sortBefore = state.PendingSort;
                Point taskPress = taskRow.TranslatePoint(new Point(35, 15), homeRows), taskEnd = beside.TranslatePoint(new Point(35, beside.ActualHeight - 2), homeRows);
                view.BeginTaskDragForTest(taskRow, taskPress);
                UiDrag.Session taskGhost = view.TaskDragForTest;
                Check(taskGhost.Snapshot.PixelWidth >= taskRow.ActualWidth * VisualTreeHelper.GetDpi(taskRow).DpiScaleX * view.ContentScale - 1,
                    "放大卡片的事项快照按祖先缩放和实际 DPI 捕获，不将小位图拉伸成模糊文字");
                for (int i = 0; i < 500; i++) view.QueueTaskDragForTest(taskEnd);
                Check(view.TaskDragFrameUpdates == 0 && changes == beforeTaskChange, "主页事项拖动同样合并鼠标样本，排队时不重建/保存清单");
                AdvanceFrames(60);
                Check(view.TaskDragFrameUpdates == 1 && homeRows.Children.IndexOf(taskRow) > sourceIndex && taskRow.Opacity == 0,
                    "主页事项的一次渲染帧真实推进让位，原行隐藏");
                RenderDragWindow(view.Window, Path.Combine(directory, "v138-task-frame-preview.png"));
                view.EndTaskDragForTest(taskEnd, false); Pump();
                Check(homeRows.Children.IndexOf(taskRow) == sourceIndex && taskRow.Opacity == 1 &&
                    homeRows.Children.Cast<UIElement>().All(e => e.RenderTransform.Value.IsIdentity) && state.PendingSort == sortBefore && changes == beforeTaskChange,
                    "主页取消事项拖动恢复原视觉顺序/变换，数据和排序模式不变");
                taskPress = taskRow.TranslatePoint(new Point(35, 15), homeRows); taskEnd = beside.TranslatePoint(new Point(35, beside.ActualHeight - 2), homeRows);
                view.BeginTaskDragForTest(taskRow, taskPress); view.QueueTaskDragForTest(new Point(0, 0)); view.EndTaskDragForTest(taskEnd, true);
                Check(state.PendingSort == "Manual" && movingTask.SortOrder > besideTask.SortOrder && changes == beforeTaskChange + 1 && movingTask.GroupId == work.Id,
                    "主页松手立即采用终点，未渲染的旧样本不影响真实事项排序与归属");
                for (int i = 0; i < 45; i++) state.Add("长列表待办 " + i, work.Id);
                for (int i = 0; i < 120; i++) state.Toggle(state.Add("更多历史 " + i));
                view.Refresh(); completed.IsExpanded = true; AdvanceFrames(260);
                Check(history.ScrollableHeight > 0 && pending.ScrollableHeight > 0 && history.ActualHeight <= area.ActualHeight * .43 &&
                    pending.ActualHeight > 60 && rect(pending).Bottom <= rect(dock).Top + 1,
                    "大量未完成和历史分别滚动，展开历史有高度上限，不遮挡待办");
                double dockBottom = rect(dock).Bottom; pending.ScrollToEnd(); Pump();
                Check(Math.Abs(rect(dock).Bottom - dockBottom) < 1 && history.VerticalOffset == 0, "滚动未完成列表不带走已完成底栏，也不改变历史滚动位置");
                history.ScrollToEnd(); Pump(); Check(history.VerticalOffset > 0, "展开历史可独立查看最后一条");
                RenderLaidOutWindow(view.Window, Path.Combine(directory, "v138-history-expanded.png"));
                completed.IsExpanded = false; view.Remove(state.Completed.First()); Pump();
                Check(view.Control<Grid>("UndoPanel").IsVisible && rect(dock).Bottom <= rect(view.Control<Grid>("UndoPanel")).Top + 1,
                    "清除/删除后的撤销提示位于底栏下方，不叠在已完成按钮上"); view.Undo();
                view.Window.Width = 320; view.Window.Height = 360; AdvanceFrames(260); completed.IsExpanded = true; Pump();
                Check(rect(dock).Bottom <= rect(progress).Top + 1 && rect(pending).Bottom <= rect(dock).Top + 1 &&
                    rect(dock).Top >= rect(view.Control<TextBlock>("PendingSectionLabel")).Bottom,
                    "320×360 最小窗口展开历史也不覆盖输入区、未完成标题和进度条");
                RenderLaidOutWindow(view.Window, Path.Combine(directory, "v138-home-min.png"));
                view.Window.Width = 900; view.Window.Height = 1300; AdvanceFrames(260); completed.IsExpanded = false;
                state.DarkMode = true; state.Language = "en-US"; view.RefreshImportedState(); AdvanceFrames(260);
                Check(Math.Abs(rect(dock).Bottom - rect(area).Bottom) <= view.ContentScale + .5 && rect(dock).Bottom <= rect(progress).Top,
                    "放大、深色和英文模式仍保持已完成底部锚点");
                RenderLaidOutWindow(view.Window, Path.Combine(directory, "v138-home-dark-large.png"), 168);
                state.DarkMode = false; state.Language = "zh-CN"; view.RefreshImportedState(); view.SettingsOpen = true; AdvanceFrames(260);
                view.OpenSettingsCategory("BasicSectionLabel"); AdvanceFrames(55);
                Border surface = view.SettingsCategoryBackdrop.Children.OfType<Border>().Single();
                BitmapCache cache = surface.CacheMode as BitmapCache;
                Check(cache != null && cache.RenderAtScale >= view.ContentScale - .01 && surface.RenderTransform.HasAnimatedProperties,
                    "复杂分类弹框在位移动画中按实际缩放短期缓存，保留动画且不使用低分辨率缓存");
                AdvanceFrames(280); Check(surface.CacheMode == null && Math.Abs(surface.RenderTransform.Value.OffsetY) < .1,
                    "入场后释放位图缓存并停在准确的布局位置，开关不在永久整页缓存里重绘");
                CheckBox toggle = view.Control<CheckBox>("DesktopToggle"); toggle.ApplyTemplate();
                System.Windows.Shapes.Ellipse knob = (System.Windows.Shapes.Ellipse)toggle.Template.FindName("Knob", toggle);
                toggle.IsChecked = false; AdvanceFrames(220); toggle.IsChecked = true; AdvanceFrames(60);
                Check(knob.RenderTransform.Value.OffsetX > 0 && knob.RenderTransform.Value.OffsetX < 14 && toggle.CacheMode == null,
                    "开关仍有真实中间帧，用小型位移和透明度动画，不缓存整个页面");
                AdvanceFrames(220); Check(Math.Abs(knob.RenderTransform.Value.OffsetX - 14) < .1, "开关动画准确到达开启端点");
                view.SettingsCategoryBackdrop.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
                AdvanceFrames(30); Check(surface.CacheMode is BitmapCache, "分类弹框退出也使用短期缓存");
                view.OpenSettingsCategory("ThemeLabel"); AdvanceFrames(300);
                Check(view.CategoryDialogOpen && surface.CacheMode == null, "退出中切换分类不会被旧动画关闭，完成后不残留缓存");
                BitmapCache previousCache = new BitmapCache(); surface.CacheMode = previousCache; UiAnimation.CacheFor(surface, 60); AdvanceFrames(100);
                Check(surface.CacheMode == previousCache, "短期缓存尊重并恢复控件原有的缓存配置"); surface.CacheMode = null;
                UiAnimation.CacheFor(surface, 500); view.SettingsOpen = false; AdvanceFrames(40);
                Check(surface.CacheMode == null, "弹框隐藏或卸载立即释放临时缓存，不等待后台计时器");
                // Empty-state bottom placement is independent of whether there are rows.
                state.Items.Clear(); view.ClearUndoHistory(); view.Refresh(); completed.IsExpanded = false; Pump();
                Check(Math.Abs(rect(dock).Bottom - rect(area).Bottom) <= view.ContentScale + .5 && view.Control<StackPanel>("EmptyPanel").IsVisible,
                    "零条待办也保留底部已完成入口，空状态不将按钮顶回中间");
            }
            finally { view.Window.Close(); }
        }
        private static IEnumerable<GlyphRunDrawing> GlyphDrawings(Drawing drawing)
        {
            GlyphRunDrawing glyph = drawing as GlyphRunDrawing;
            if (glyph != null) yield return glyph;
            DrawingGroup group = drawing as DrawingGroup;
            if (group != null) foreach (Drawing child in group.Children)
                foreach (GlyphRunDrawing run in GlyphDrawings(child)) yield return run;
        }
        private static string GlyphSignature(Drawing drawing)
        {
            return String.Join("|", GlyphDrawings(drawing).Select(g => g.GlyphRun.GlyphTypeface.FontUri + ":" +
                g.GlyphRun.FontRenderingEmSize.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ":" +
                String.Join(",", g.GlyphRun.GlyphIndices) + ":" + String.Join(",", g.GlyphRun.AdvanceWidths)));
        }
        private static void CheckUnifiedDrag39(string directory, string fixture)
        {
            AppState state = new AppState { Language = "zh-CN", DesktopMode = false, Width = 540, Height = 900 };
            TodoGroup work = new TodoGroup { Id = "work39", Name = "工作", SortOrder = 0 },
                life = new TodoGroup { Id = "life39", Name = "生活", SortOrder = 1 },
                empty = new TodoGroup { Id = "empty39", Name = "空分组", SortOrder = 2 };
            state.Groups.Add(work); state.Groups.Add(life); state.Groups.Add(empty);
            TodoItem a = state.Add("拖动字体 Abc 123", work.Id), b = state.Add("第二条工作待办", work.Id),
                c = state.Add("生活待办", life.Id), pin = state.Add("置顶不丢失", work.Id); pin.Pinned = true;
            TodoWindow view = new TodoWindow(state); int changes = 0;
            view.Changed = delegate { changes++; view.IsSaved = true; };
            try
            {
                view.Window.Show(); AdvanceFrames(240);
                FrameworkElement root = (FrameworkElement)view.Window.Content;
                StackPanel rows = view.Control<StackPanel>("PendingList");
                Func<TodoItem, Grid> row = delegate(TodoItem item) { return rows.Children.OfType<Grid>().Single(g => g.Tag == item); };
                Func<string, Grid> heading = delegate(string name) { return rows.Children.OfType<Grid>().Single(g => g.Tag == null && g.Children.OfType<TextBlock>().Any(t => t.Text == name)); };
                Grid source = row(a), target = row(c); UIElement[] originalRows = rows.Children.Cast<UIElement>().ToArray();
                string originalSort = state.PendingSort, originalGlyphs = GlyphSignature(UiDrag.CaptureDrawing(source));
                double originalWidth = source.TransformToAncestor(root).TransformBounds(new Rect(source.RenderSize)).Width;
                Point end = target.TranslatePoint(new Point(45, 2), root);
                using (UiDrag.Session drag = UiDrag.BeginForTest(source, source.TranslatePoint(new Point(40, 15), root)))
                {
                    drag.Update(end); AdvanceFrames(210);
                    Check(rows.Children.IndexOf(source) < rows.Children.IndexOf(target) &&
                        rows.Children.IndexOf(source) > rows.Children.IndexOf(heading("生活")) && !drag.Marker.IsEmpty,
                        "主页拖到另一组事项上方：真实布局在目标组让位，浮层和插入线同时显示");
                    Check(a.GroupId == work.Id && state.PendingSort == originalSort && changes == 0,
                        "跨组预览不提前改归属、顺序或保存数据");
                    Check(drag.DrawingSnapshot.IsFrozen && GlyphDrawings(drag.DrawingSnapshot).Any() &&
                        GlyphSignature(drag.DrawingSnapshot) == originalGlyphs &&
                        Math.Abs(drag.SnapshotDisplaySize.Width - originalWidth) < .01,
                        "主页拖动使用冻结原字形：字体、字号、字符宽度及整行宽度与拖动前完全相同");
                    RenderDragWindow(view.Window, Path.Combine(directory, "v139-home-cross-group.png"));
                    for (int i = 0; i < 30; i++) drag.Update(end);
                    Check(rows.Children.IndexOf(source) < rows.Children.IndexOf(target), "停在目标插槽不来回换位");
                    drag.Complete(false);
                }
                Check(rows.Children.Cast<UIElement>().SequenceEqual(originalRows) && a.GroupId == work.Id && changes == 0 &&
                    source.Opacity == 1 && rows.Children.Cast<UIElement>().All(e => e.RenderTransform.Value.IsIdentity),
                    "取消跨组拖动恢复精确原视觉序列、透明度和变换，归属完全不变");
                using (UiDrag.Session drag = UiDrag.BeginForTest(source, source.TranslatePoint(new Point(40, 15), root)))
                {
                    drag.Queue(new Point(-20, -20)); drag.Flush(target.TranslatePoint(new Point(45, 2), root)); drag.Complete(true);
                }
                Check(a.GroupId == life.Id && a.SortOrder < c.SortOrder && changes == 1 && !a.IsCompleted && pin.Pinned,
                    "主页松手同步保存目标分组与前后顺序一次，不改变完成状态或置顶");
                AdvanceFrames(40); source = row(a); target = row(c);
                using (UiDrag.Session drag = UiDrag.BeginForTest(source, source.TranslatePoint(new Point(40, 15), root)))
                { drag.Update(target.TranslatePoint(new Point(45, target.ActualHeight - 2), root)); drag.Complete(true); }
                Check(a.SortOrder > c.SortOrder && changes == 2, "主页同组下半行落点保存到目标后方");
                state.CollapsedGroupIds.Add(empty.Id); view.Refresh(); Pump(); source = row(a);
                using (UiDrag.Session drag = UiDrag.BeginForTest(source, source.TranslatePoint(new Point(40, 15), root)))
                { Grid h = heading("空分组"); drag.Update(h.TranslatePoint(new Point(60, 13), root)); Check(!drag.Marker.IsEmpty, "空分组或折叠标题均可直接接收待办"); drag.Complete(true); }
                Check(a.GroupId == empty.Id && state.CollapsedGroupIds.Contains(empty.Id) && !rows.Children.OfType<Grid>().Any(g => g.Tag == a),
                    "拖入折叠空组后正确更新归属和数量，并保持原折叠偏好");
                state.CollapsedGroupIds.Clear(); view.Refresh(); Pump(); source = row(a);
                using (UiDrag.Session drag = UiDrag.BeginForTest(source, source.TranslatePoint(new Point(40, 15), root)))
                { Grid h = heading("未分组"); drag.Update(h.TranslatePoint(new Point(60, 13), root)); drag.Complete(true); }
                Check(a.GroupId == null && changes == 4, "主页也可以拖到没有事项的未分组入口");
                string pinGroup = pin.GroupId; source = row(pin); target = row(c);
                using (UiDrag.Session drag = UiDrag.BeginForTest(source, source.TranslatePoint(new Point(40, 15), root)))
                { drag.Update(new Point(-15, -15)); drag.Complete(true); }
                Check(pin.GroupId == pinGroup && pin.Pinned && changes == 4, "置顶事项在窗口外松手不保存、不误改归属");
                state.DisableGroups = true; view.Refresh(); Pump(); source = row(c); target = row(b);
                string hiddenGroup = c.GroupId;
                using (UiDrag.Session drag = UiDrag.BeginForTest(source, source.TranslatePoint(new Point(40, 15), root)))
                { drag.Update(target.TranslatePoint(new Point(45, 2), root)); drag.Complete(true); }
                Check(c.GroupId == hiddenGroup && c.SortOrder < b.SortOrder && state.PendingSort == "Manual",
                    "关闭分组后仍能自由排序，但不抹去待办原有分组");
                Storage store = new Storage(Path.Combine(fixture, "v139-order")); store.Save(state);
                AppState reopened = store.Load();
                Check(reopened.Items.Single(t => t.Id == a.Id).GroupId == null && reopened.Items.Single(t => t.Id == c.Id).GroupId == hiddenGroup &&
                    reopened.Items.Single(t => t.Id == c.Id).SortOrder < reopened.Items.Single(t => t.Id == b.Id).SortOrder,
                    "主页跨组、未分组及关闭分组排序经真实 JSON 保存读取保持");
                state.DisableGroups = false; view.Refresh(); view.OpenGroups(); AdvanceFrames(240);
                StackPanel groups = view.Control<StackPanel>("GroupList");
                Border workCard = groups.Children.OfType<Border>().Skip(1).First(), lifeCard = groups.Children.OfType<Border>().Skip(2).First();
                StackPanel sourceBody = (StackPanel)workCard.Child, targetBody = (StackPanel)lifeCard.Child;
                Grid managed = sourceBody.Children.OfType<Grid>().Single(g => g.Tag == b), destination = targetBody.Children.OfType<Grid>().Single(g => g.Tag == c);
                UIElement[] fromOrder = sourceBody.Children.Cast<UIElement>().ToArray(), toOrder = targetBody.Children.Cast<UIElement>().ToArray();
                string managedGlyphs = GlyphSignature(UiDrag.CaptureDrawing(managed)); int before = changes;
                using (UiDrag.Session drag = UiDrag.BeginForTest(managed, managed.TranslatePoint(new Point(40, 15), root)))
                {
                    drag.Update(destination.TranslatePoint(new Point(45, 2), root)); AdvanceFrames(210);
                    Check(!sourceBody.Children.Contains(managed) && targetBody.Children.Contains(managed) && b.GroupId == work.Id,
                        "分组管理同主页：源组立即收回空位、目标组扩展让位，数据到松手才改");
                    Check(GlyphSignature(drag.DrawingSnapshot) == managedGlyphs, "分组管理拖动保留原字形和字号，不产生位图压缩文字");
                    RenderDragWindow(view.Window, Path.Combine(directory, "v139-management-cross-group.png")); drag.Complete(false);
                }
                Check(sourceBody.Children.Cast<UIElement>().SequenceEqual(fromOrder) && targetBody.Children.Cast<UIElement>().SequenceEqual(toOrder) &&
                    changes == before && managed.Opacity == 1, "跨卡片取消后两边的子控件序列精确恢复，没有空洞和遗留占位");
                view.Control<Button>("GroupBack").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); view.SettingsOpen = true;
                view.OpenSettingsCategory("BasicSectionLabel"); AdvanceFrames(240); view.SettingsCategoryScroll.ScrollToEnd(); Pump();
                RenderLaidOutWindow(view.Window, Path.Combine(directory, "v139-input-shortcuts.png"), 168);
                CheckInputOutlines39(view, directory);
            }
            finally { view.Window.Close(); }
        }
        private static void CheckSecondaryStartup41(string directory, string fixture)
        {
            List<Native.MonitorArea> realMonitors = SecondaryStartup.AvailableMonitors();
            Check(realMonitors.Count >= 1 && realMonitors.Count(m => m.IsPrimary) == 1 &&
                realMonitors.All(m => m.Work.Width > 0 && m.Work.Height > 0 && !String.IsNullOrEmpty(m.DeviceName)),
                "实际 Windows 显示器能读取主副屏标志、设备名及有效工作区");
            Native.MonitorArea primary = new Native.MonitorArea { DeviceName = "DISPLAY1", IsPrimary = true,
                Bounds = new System.Drawing.Rectangle(0, 0, 1920, 1080), Work = new System.Drawing.Rectangle(0, 0, 1920, 1040) };
            Native.MonitorArea left = new Native.MonitorArea { DeviceName = "DISPLAY2", IsPrimary = false,
                Bounds = new System.Drawing.Rectangle(-1600, 0, 1600, 900), Work = new System.Drawing.Rectangle(-1600, 0, 1600, 860) };
            Native.MonitorArea right = new Native.MonitorArea { DeviceName = "DISPLAY3", IsPrimary = false,
                Bounds = new System.Drawing.Rectangle(1920, 0, 1280, 1024), Work = new System.Drawing.Rectangle(1920, 0, 1280, 984) };
            Check(SecondaryStartup.Select(new[] { primary }, null) == null &&
                SecondaryStartup.Select(new[] { primary, left, right }, "DISPLAY3") == right &&
                SecondaryStartup.Select(new[] { primary, left, right }, "MISSING") == null &&
                SecondaryStartup.Select(new[] { primary, left }, "MISSING") == left,
                "仅扩展副屏满足自启条件；多副屏严格匹配所选设备，单副屏改名可回退");
            AppState state = new AppState { Language = "zh-CN", SecondaryDeviceName = "DISPLAY2", StartOnSecondaryOnly = true };
            Native.Rect placed = new Native.Rect { Left = -1500, Top = 90, Right = -1100, Bottom = 690 };
            SecondaryStartup.Capture(state, left, placed);
            Check(state.SecondaryPositionSet && state.SecondaryOffsetX == 100 && state.SecondaryOffsetY == 90 &&
                state.SecondaryWidth == 400 && state.SecondaryHeight == 600, "副屏单独保存相对工作区位置与窗口尺寸");
            SecondaryStartup.Capture(state, primary, new Native.Rect { Left = 200, Top = 200, Right = 600, Bottom = 800 });
            SecondaryStartup.Capture(state, right, new Native.Rect { Left = 2000, Top = 100, Right = 2400, Bottom = 700 });
            Check(state.SecondaryOffsetX == 100 && state.SecondaryOffsetY == 90 && state.SecondaryDeviceName == "DISPLAY2",
                "拔掉副屏落到主屏或操作另一副屏，不覆盖原副屏最后位置");
            Native.MonitorArea relocated = new Native.MonitorArea { DeviceName = "DISPLAY2", IsPrimary = false,
                Bounds = new System.Drawing.Rectangle(-2000, 0, 1600, 900), Work = new System.Drawing.Rectangle(-2000, 0, 1600, 860) };
            SecondaryStartup.Restore(state, relocated);
            Check(state.X == -1900 && state.Y == 90 && state.Width == 400 && state.Height == 600,
                "副屏换到不同桌面坐标后，按屏幕相对位置恢复，而非沿用旧绝对坐标");
            Native.MonitorArea smaller = new Native.MonitorArea { DeviceName = "DISPLAY2", IsPrimary = false,
                Bounds = new System.Drawing.Rectangle(-1000, 0, 1000, 500), Work = new System.Drawing.Rectangle(-1000, 0, 1000, 480) };
            state.SecondaryOffsetX = 900; state.SecondaryOffsetY = 800;
            SecondaryStartup.Restore(state, smaller);
            Check(state.X == -320 && state.Y == 0 && state.Width == 400 && state.Height == 600 && state.SecondaryOffsetX == 900 && state.SecondaryOffsetY == 800,
                "副屏分辨率变小时窗口等比收纳保持可见，原偏好尺寸与坐标仍保留以待屏幕恢复");
            Storage store = new Storage(Path.Combine(fixture, "v141-secondary")); store.Save(state);
            AppState loaded = store.Load();
            Check(loaded.StartOnSecondaryOnly && loaded.SecondaryDeviceName == "DISPLAY2" && loaded.SecondaryPositionSet &&
                loaded.SecondaryOffsetX == 900 && loaded.SecondaryOffsetY == 800,
                "副屏条件、目标设备和最后位置写入数据文件并可重启恢复");
            SecondaryStartup.Capture(loaded, right, new Native.Rect { Left = 2000, Top = 80, Right = 2400, Bottom = 680 }, true);
            Check(loaded.SecondaryDeviceName == "DISPLAY3" && loaded.SecondaryOffsetX == 80 && loaded.SecondaryOffsetY == 80,
                "用户主动拖到另一块副屏时自动更新目标屏和最后位置");

            string waitFolder = Path.Combine(fixture, "v141-wait"); Directory.CreateDirectory(waitFolder);
            Func<Native.MonitorArea> disconnected = delegate { return null; };
            System.Threading.Tasks.Task<bool> manual = System.Threading.Tasks.Task.Factory.StartNew(delegate
            { return SecondaryStartup.WaitForDisplay(waitFolder, disconnected, 20); });
            Check(!manual.Wait(80), "未连接副屏时开机启动等待，不提前显示窗口");
            File.WriteAllText(Path.Combine(waitFolder, "show.request"), "manual");
            Check(manual.Wait(1000) && manual.Result, "手动再次启动可越过等待，仍能打开设置");
            System.Threading.Tasks.Task<bool> exiting = System.Threading.Tasks.Task.Factory.StartNew(delegate
            { return SecondaryStartup.WaitForDisplay(waitFolder, disconnected, 20); });
            File.WriteAllText(Path.Combine(waitFolder, "exit.request"), "exit");
            Check(exiting.Wait(1000) && !exiting.Result, "等待期间安装器的退出请求可结束后台进程");
            int connected = 0;
            System.Threading.Tasks.Task<bool> hotplug = System.Threading.Tasks.Task.Factory.StartNew(delegate
            { return SecondaryStartup.WaitForDisplay(waitFolder, delegate { return System.Threading.Interlocked.CompareExchange(ref connected, 0, 0) == 1 ? left : null; }, 20); });
            Check(!hotplug.Wait(80), "副屏尚未连接时保持等待");
            System.Threading.Interlocked.Exchange(ref connected, 1);
            Check(hotplug.Wait(1000) && hotplug.Result, "副屏在登录后连接也会触发启动");
            int probe = 0;
            Check(SecondaryStartup.WaitForDisplay(waitFolder, delegate
                {
                    if (System.Threading.Interlocked.Increment(ref probe) == 1)
                        throw new System.ComponentModel.Win32Exception("display reconfiguration");
                    return left;
                }, 20) && probe >= 2,
                "驱动热插拔瞬间读取工作区失败时继续重试，不退出自启进程");

            bool fakeStartup = false;
            TodoWindow view = new TodoWindow(new AppState { Language = "zh-CN", DesktopMode = false });
            view.GetStartup = delegate { return fakeStartup; }; view.SetStartup = delegate(bool enabled) { fakeStartup = enabled; };
            try
            {
                view.Window.Show(); Pump(); view.SettingsOpen = true; view.OpenSettingsCategory("BasicSectionLabel"); Pump();
                view.ExpandAdvancedSettings(); view.Window.UpdateLayout();
                Check(!view.State.StartOnSecondaryOnly && view.Control<CheckBox>("SecondaryStartupToggle").IsChecked == false &&
                    view.Control<ComboBox>("SecondaryDisplaySelect").IsVisible,
                    "基础功能高级设置提供默认关闭的副屏自启开关和目标副屏选择");
                view.Control<CheckBox>("SecondaryStartupToggle").IsChecked = true;
                view.Control<CheckBox>("SecondaryStartupToggle").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(fakeStartup && view.State.StartOnSecondaryOnly,
                    "开启副屏条件时同步启用开机自启（隔离模拟，不修改真实注册表）");
                view.Control<CheckBox>("SecondaryStartupToggle").IsChecked = false;
                view.Control<CheckBox>("SecondaryStartupToggle").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(!view.State.StartOnSecondaryOnly && fakeStartup,
                    "关闭副屏限制不擅自关闭用户既有的开机自启");
                AdvanceFrames(260);
                RenderLaidOutWindow(view.Window, Path.Combine(directory, "v141-secondary-settings.png"), 160);
            }
            finally { view.Window.Close(); }
        }

        private static void CheckFeishu40(string directory, string fixture)
        {
            AppState state = new AppState { Language = "zh-CN", DesktopMode = false, ThemeId = "ocean", Width = 480, Height = 740 };
            TodoGroup work = new TodoGroup { Name = "工作", SortOrder = 0 }, life = new TodoGroup { Name = "生活", SortOrder = 1 };
            state.Groups.Add(work); state.Groups.Add(life);
            TodoItem task = state.Add("分组不会改变事项内容与时间", work.Id), done = state.Add("已完成事项", work.Id); state.Toggle(done);
            Storage store = new Storage(Path.Combine(fixture, "v140-groups")); int changes = 0;
            TodoWindow view = new TodoWindow(state); view.Changed = delegate { changes++; store.Save(state); view.IsSaved = true; };
            try
            {
                view.Window.Show(); AdvanceFrames(250);
                StackPanel pending = view.Control<StackPanel>("PendingList");
                Func<string, Grid> heading = delegate(string label) { return pending.Children.OfType<Grid>().First(g => g.Tag == null && g.Children.OfType<TextBlock>().Any(t => t.Text == label)); };
                Grid original = heading(work.Name);
                for (int i = 0; i < 12; i++) view.Refresh();
                Check(ReferenceEquals(original, heading(work.Name)), "分组刷新复用未变化的标题和菜单，减少重复创建与排版");
                view.Control<Button>("AddGroupButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump();
                ContextMenu choices = view.Control<Button>("AddGroupButton").ContextMenu;
                MenuItem create = choices.Items.OfType<MenuItem>().Last(); choices.IsOpen = false;
                Check(create.Header.ToString() == "创建分组…", "主页添加分组下拉入口改为创建分组，不再跳转大管理页");
                create.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); AdvanceFrames(230);
                Check(view.OverlayOpen && !view.GroupOpen && view.Control<FrameworkElement>("GroupNameOverlay").IsVisible &&
                    view.Control<Border>("OverlaySurface").ActualWidth <= 330.1 && view.Control<FrameworkElement>("MainPanel").Visibility == Visibility.Visible,
                    "创建分组打开同风格的小弹窗，保留主界面上下文");
                view.Control<TextBox>("GroupNameInput").Text = "工作"; view.Control<Button>("GroupNameAccept").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(state.Groups.Count == 2 && view.OverlayOpen && view.Control<TextBlock>("GroupNameMessage").Text.Contains("同名"), "创建同名分组不写入，弹窗内说明错误");
                view.Control<TextBox>("GroupNameInput").Text = " "; view.Control<Button>("GroupNameAccept").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(state.Groups.Count == 2 && view.Control<TextBlock>("GroupNameMessage").Text.Contains("不能为空"), "空白名称不能创建分组");
                view.Control<TextBox>("GroupNameInput").Text = " 临时项目 "; view.Control<Button>("GroupNameAccept").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump();
                TodoGroup added = state.Groups.Single(g => g.Name == "临时项目");
                Check(!view.OverlayOpen && state.ActiveGroupId == added.Id && changes == 1, "创建后去除首尾空白、选中新组并只保存一次");
                view.OpenGroupName(); view.Control<TextBox>("GroupNameInput").Text = "不会创建"; view.Control<Button>("GroupNameCancel").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(state.Groups.Count == 3 && changes == 1, "取消创建不会留下空组或误保存");
                ContextMenu actions = heading(work.Name).ContextMenu;
                Check(actions.Items.OfType<MenuItem>().Select(m => m.Header.ToString()).SequenceEqual(new[] { "重命名分组", "置顶分组", "管理分组", "删除分组…" }),
                    "主页分组标题右键菜单包含重命名、置顶、管理和安全删除");
                actions.Items.OfType<MenuItem>().First().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                view.Control<TextBox>("GroupNameInput").Text = "工作安排";
                Check(PumpUntil(delegate { return work.Name == "工作安排"; }, 1200), "右键重命名在输入停止后自动保存，不需要保存按钮");
                view.Control<TextBox>("GroupNameInput").Text = "即时改名"; view.CloseOverlay();
                Check(work.Name == "即时改名" && !view.OverlayOpen && store.Load().Groups.Single(g => g.Id == work.Id).Name == "即时改名",
                    "关闭重命名弹窗立即保存最后输入，真实 JSON 重新读取保持");
                heading(work.Name).ContextMenu.Items.OfType<MenuItem>().Single(m => m.Header.ToString() == "管理分组").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); AdvanceFrames(230);
                StackPanel cards = view.Control<StackPanel>("GroupList");
                Func<TodoGroup, TextBox> field = delegate(TodoGroup g) { return VisualDescendants<TextBox>(cards).Single(t => t.Text == g.Name); };
                Check(view.GroupOpen && !VisualDescendants<Button>(cards).Any(b => Convert.ToString(b.Content) == "保存"), "分组管理中去除逐组保存按钮，名称直接编辑自动保存");
                TextBox name = field(work); name.Text = "管理页自动改名";
                Check(PumpUntil(delegate { return work.Name == name.Text; }, 1200), "管理页改名停顿 250 毫秒自动提交，不重建编辑框或丢失焦点");
                int before = changes; name.Text = life.Name; AdvanceFrames(350);
                Check(work.Name == "管理页自动改名" && changes == before && view.Control<TextBlock>("GroupStatus").Text.Contains("同名"), "管理页同名输入不会覆盖现有分组或产生额外写入");
                name.Text = "回车立即保存"; name.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(name), Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
                Check(work.Name == "回车立即保存", "分组管理按回车立即提交最后输入");
                view.Control<Button>("GroupBack").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); AdvanceFrames(230);
                heading(work.Name).ContextMenu.Items.OfType<MenuItem>().Single(m => m.Header.ToString() == "置顶分组").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Check(work.Pinned && pending.Children.OfType<Grid>().First(g => g.Tag == null) == heading(work.Name) &&
                    store.Load().Groups.Single(g => g.Id == work.Id).Pinned, "置顶分组排在未分组及普通组之前，置顶状态持久化");
                before = changes; view.MoveGroup(work.Id, life.Id, true);
                Check(changes == before && !view.CanMoveGroup(work.Id, life.Id), "置顶组与普通组不可通过拖动混排，不会悄悄取消置顶");
                view.SetGroupPinned(life, true); view.MoveGroup(life.Id, work.Id, false);
                Check(state.OrderedGroups.First() == life && state.OrderedGroups.Skip(1).First() == work, "多个置顶分组之间仍可自由拖动排序");
                view.SetGroupPinned(life, false);
                heading(work.Name).ContextMenu.Items.OfType<MenuItem>().Single(m => m.Header.ToString() == "取消置顶分组").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Check(!work.Pinned && heading("未分组") == pending.Children.OfType<Grid>().First(g => g.Tag == null) && task.GroupId == work.Id && done.GroupId == work.Id,
                    "取消置顶恢复普通分组区域，不改变待办归属、完成状态与时间");
                foreach (bool dark in new[] { false, true })
                {
                    state.DarkMode = dark; view.ApplyTheme(); view.OpenGroupName(work); AdvanceFrames(230);
                    RenderLaidOutWindow(view.Window, Path.Combine(directory, "v140-group-rename-" + (dark ? "dark" : "light") + ".png"), 120);
                    view.CloseOverlay();
                }
                state.DarkMode = false; view.ApplyTheme(); view.Control<ComboBox>("LanguageSelect").SelectedIndex = 1; view.OpenGroupName(); AdvanceFrames(230);
                Check(view.Control<TextBlock>("GroupNameTitle").Text == "Create group" && view.Control<Button>("GroupNameAccept").Content.ToString() == "Create", "新建与重命名弹窗支持中英文");
                RenderLaidOutWindow(view.Window, Path.Combine(directory, "v140-group-create-english.png"), 168); view.CloseOverlay();
                view.Window.Width = 400; view.Window.Height = 450; view.OpenGroupName(); AdvanceFrames(230); view.Window.UpdateLayout();
                Border smallSurface = view.Control<Border>("OverlaySurface"); Button smallCreate = view.Control<Button>("GroupNameAccept");
                Check(smallSurface.ActualHeight <= ((FrameworkElement)view.Window.Content).ActualHeight - 28 &&
                    smallCreate.TransformToAncestor(smallSurface).TransformBounds(new Rect(smallCreate.RenderSize)).Bottom <= smallSurface.ActualHeight,
                    "最小卡片的新建分组弹窗与操作按钮完整显示，不发生底部裁切");
                RenderLaidOutWindow(view.Window, Path.Combine(directory, "v140-group-create-small.png"), 96); view.CloseOverlay(); view.Window.Width = 480; view.Window.Height = 740;
                view.Control<ComboBox>("LanguageSelect").SelectedIndex = 0; view.Refresh();
                state.CollapsedGroupIds.Add(work.Id); view.Refresh();
                heading(work.Name).ContextMenu.Items.OfType<MenuItem>().Single(m => m.Header.ToString() == "删除分组…").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Check(state.Groups.Contains(work) && view.OverlayOpen, "右键删除分组先二次确认，不直接删除");
                view.Control<Button>("ConfirmCancelButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(state.Groups.Contains(work) && task.GroupId == work.Id, "取消删除分组保持事项归属");
                heading(work.Name).ContextMenu.Items.OfType<MenuItem>().Single(m => m.Header.ToString() == "删除分组…").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                view.Control<Button>("ConfirmAcceptButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(!state.Groups.Contains(work) && task.GroupId == null && done.GroupId == null && !state.CollapsedGroupIds.Contains(work.Id) && !task.Deleted && done.IsCompleted,
                    "确认删除只移除分组，未完成和已完成事项移到未分组且保留数据");
            }
            finally { view.Window.Close(); }

            CheckAdminTick40(directory);
            LanPeer peer = new LanPeer { InstanceId = "v140-peer", DisplayName = "隔离扫描测试", MachineName = "QA" };
            using (LanAdminWindow admin = new LanAdminWindow(state, Path.Combine(fixture, "v140-admin")))
            {
                admin.LoadPeersForTest(new[] { peer }, true); admin.UserList.SelectedIndex = 0; admin.CodeInput.Text = "TEST1234";
                LanSnapshot data = new LanSnapshot { Revision = 1 }; data.Tasks.Add(new LanTaskInfo { Id = "v140-task", Text = "网络失败保留此事项", CreatedAt = DateTime.UtcNow.Ticks });
                admin.ApplySnapshotForTest(data); admin.Window.Show(); AdvanceFrames(230);
                Grid originalRemote = admin.PendingRows.Children.OfType<Grid>().Single(row => row.Tag is LanTaskInfo);
                LanSnapshot equivalent = LanJson.Read<LanSnapshot>(LanJson.Write(data)); equivalent.Revision = 2;
                admin.ApplySnapshotForTest(equivalent); Pump();
                Check(ReferenceEquals(originalRemote, admin.PendingRows.Children.OfType<Grid>().Single(row => row.Tag is LanTaskInfo)) &&
                    ReferenceEquals(originalRemote.Tag, equivalent.Tasks[0]), "管理员新快照复用内容相同的行，同时更新当前对象标记，避免重建所有菜单");
                equivalent.Revision = 3; equivalent.Tasks[0].Text = "网络失败保留更新后的事项"; admin.ApplySnapshotForTest(equivalent); Pump();
                Check(!ReferenceEquals(originalRemote, admin.PendingRows.Children.OfType<Grid>().Single(row => row.Tag is LanTaskInfo)) &&
                    VisualDescendants<TextBlock>(admin.PendingRows).Any(t => t.Text == equivalent.Tasks[0].Text), "远程内容变化立即失效行缓存，不显示旧文字");
                data = equivalent;
                List<string> logs = new List<string>(); admin.Log = delegate(string message) { lock (logs) logs.Add(message); };
                admin.DiscoverForTest = delegate { throw new System.Net.Sockets.SocketException(10013); };
                for (int i = 0; i < 3; i++)
                {
                    admin.Discover(); Check(PumpUntil(delegate { return !admin.IsWorking; }, 1800), "UDP 创建异常第 " + (i + 1) + " 次被后台边界捕获，扫描恢复可重试状态");
                    Check(admin.StatusText.Text.Contains("扫描失败") && admin.UserList.Items.Count == 1 && admin.UserList.SelectedItem == peer &&
                        admin.PendingRows.Children.OfType<Grid>().Any(row => (row.Tag as LanTaskInfo) == data.Tasks[0]), "扫描失败不退出进程，也不清空已有用户和待办列表");
                }
                Check(logs.Count == 3 && logs.All(message => message.Contains("SocketException")), "网络错误完整记录异常类型与堆栈，不只显示泛化提示");
                admin.DiscoverForTest = delegate { return new List<LanPeer> { peer, new LanPeer { InstanceId = "v140-second", DisplayName = "第二用户", MachineName = "QA2" } }; };
                admin.Discover(); Check(PumpUntil(delegate { return !admin.IsWorking; }, 1800) && admin.UserList.Items.Count == 2 && admin.UserList.SelectedItem == peer,
                    "失败后重新扫描成功，能连接多个用户并保持原用户选择");
                admin.AddTextInput.Text = "失败不丢失新增草稿";
                admin.SendForTest = delegate { throw new System.Net.Sockets.SocketException(10054); };
                admin.AddTaskButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(PumpUntil(delegate { return !admin.IsWorking; }, 1800) && admin.StatusText.Text.Contains("连接失败") && admin.AddTextInput.Text == "失败不丢失新增草稿",
                    "后台命令连接异常也被捕获，保留输入并允许重试");
                admin.SendForTest = delegate { return null; }; admin.AddTaskButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(PumpUntil(delegate { return !admin.IsWorking; }, 1800) && admin.StatusText.Text.Contains("有效数据"), "空响应在界面内提示，不触发空引用崩溃");
                CheckBox remoteCheck = admin.PendingRows.Children.OfType<Grid>().Single(row => row.Tag is LanTaskInfo).Children.OfType<CheckBox>().Single();
                remoteCheck.IsChecked = true; remoteCheck.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(PumpUntil(delegate { return !admin.IsWorking; }, 1800) && remoteCheck.IsChecked == false && !data.Tasks[0].Completed,
                    "远程完成操作失败时复用行恢复真实勾选状态，不留下虚假完成显示");
                admin.SendForTest = delegate { data.Revision++; return new LanResponse { Success = true, Snapshot = data }; };
                admin.AddTaskButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(PumpUntil(delegate { return !admin.IsWorking; }, 1800) && admin.AddTextInput.Text == "" && admin.StatusText.Text.Contains("修改已同步"), "连接错误或空响应后可正常重试发送，工作状态不会卡住");
                using (System.Threading.ManualResetEvent started = new System.Threading.ManualResetEvent(false))
                using (System.Threading.ManualResetEvent release = new System.Threading.ManualResetEvent(false))
                using (System.Threading.ManualResetEvent finished = new System.Threading.ManualResetEvent(false))
                {
                    admin.DiscoverForTest = delegate { started.Set(); release.WaitOne(1800); finished.Set(); throw new System.Net.Sockets.SocketException(10013); };
                    admin.Log = delegate { throw new IOException("模拟日志不可写"); }; admin.Discover();
                    Check(started.WaitOne(1000), "关闭窗口竞态测试已进入后台扫描"); admin.Window.Close(); release.Set();
                    Check(finished.WaitOne(1500), "扫描期间关闭管理员窗口，后台扫描正常结束"); AdvanceFrames(200);
                }
            }
            using (LanAdminWindow reopened = new LanAdminWindow(state, Path.Combine(fixture, "v140-admin")))
            {
                reopened.LoadPeersForTest(new[] { peer }, true); reopened.DiscoverForTest = delegate { return new List<LanPeer> { peer }; };
                reopened.Show(); reopened.Discover();
                Check(PumpUntil(delegate { return !reopened.IsWorking; }, 1800) && reopened.Window.IsVisible && reopened.UserList.Items.Count == 1,
                    "扫描失败、日志失败及窗口关闭后仍可重新打开管理员并正常扫描");
            }
        }
        private static void CheckAdminTick40(string directory)
        {
            foreach (bool dark in new[] { false, true })
            foreach (double scale in new[] { 1.0, 1.25, 1.75, 2.0 })
            {
                ThemePalette palette = ThemeCatalog.Get("ocean"); ResourceDictionary resources = LanAdminWindowLegacy.AdminResources(dark ? palette.Dark : palette.Light);
                CheckBox check = new CheckBox { Content = "已完成", IsChecked = true, Width = 140, Height = 36, FontSize = 13,
                    FontFamily = new FontFamily("Microsoft YaHei UI"), Style = resources["AdminCheck"] as Style };
                check.Resources.MergedDictionaries.Add(resources); check.Measure(new Size(140, 36)); check.Arrange(new Rect(0, 0, 140, 36)); check.UpdateLayout(); check.ApplyTemplate();
                Border box = (Border)check.Template.FindName("Box", check); System.Windows.Shapes.Path tick = (System.Windows.Shapes.Path)check.Template.FindName("Tick", check);
                Color tickColor = (Color)ColorConverter.ConvertFromString(ThemeCatalog.AccentText("ocean", dark));
                Check(((SolidColorBrush)tick.Stroke).Color == tickColor, "管理员勾号使用当前强调色对应的可读前景色");
                Rect ink = tick.TransformToAncestor(box).TransformBounds(tick.RenderedGeometry.GetRenderBounds(new Pen(Brushes.White, tick.StrokeThickness)
                    { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }));
                double centerX = ink.X + ink.Width / 2, centerY = ink.Y + ink.Height / 2;
                RenderTargetBitmap image = new RenderTargetBitmap((int)Math.Ceiling(140 * scale), (int)Math.Ceiling(36 * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32); image.Render(check);
                byte[] pixels = new byte[image.PixelWidth * image.PixelHeight * 4]; image.CopyPixels(pixels, image.PixelWidth * 4, 0);
                Point origin = box.TranslatePoint(new Point(), check); int left = image.PixelWidth, right = -1, top = image.PixelHeight, bottom = -1;
                for (int y = (int)Math.Floor(origin.Y * scale); y < (int)Math.Ceiling((origin.Y + box.ActualHeight) * scale); y++)
                for (int x = (int)Math.Floor(origin.X * scale); x < (int)Math.Ceiling((origin.X + box.ActualWidth) * scale); x++)
                {
                    int p = (y * image.PixelWidth + x) * 4;
                    if (pixels[p + 3] > 245 && Math.Abs(pixels[p] - tickColor.B) < 15 &&
                        Math.Abs(pixels[p + 1] - tickColor.G) < 15 && Math.Abs(pixels[p + 2] - tickColor.R) < 15)
                    { left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y); }
                }
                Check(Math.Abs(centerX - box.ActualWidth / 2) < .65 && Math.Abs(centerY - box.ActualHeight / 2) < .65 && right >= left &&
                    Math.Abs((left + right + 1) / 2.0 - (origin.X + box.ActualWidth / 2) * scale) <= 1.2 &&
                    Math.Abs((top + bottom + 1) / 2.0 - (origin.Y + box.ActualHeight / 2) * scale) <= 1.2,
                    "管理员" + (dark ? "深色" : "浅色") + " " + scale * 100 + "% 勾选框真实笔画和像素范围均上下左右居中");
                if (scale == 1 || scale == 1.75)
                {
                    PngBitmapEncoder png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
                    using (FileStream file = File.Create(Path.Combine(directory, "v140-admin-tick-" + (dark ? "dark-" : "light-") + scale * 100 + ".png"))) png.Save(file);
                }
            }
        }
        private static void CheckInputOutlines39(TodoWindow view, string directory)
        {
            string[] names = { "ShowHotkeyInput", "MiniHotkeyInput", "TitleSettingInput", "LanDisplayNameInput",
                "LanAccessCodeInput", "GroupCreateInput", "GroupNameInput", "ItemReminderHours", "ItemReminderMinutes", "ItemReminderDailyTime" };
            Check(names.All(name => view.Control<TextBox>(name).Style == view.Window.Resources["SettingInput"]),
                "快捷键、名称、口令、新分组、小时分钟每日时间共用同一完整输入框样式");
            foreach (bool dark in new[] { false, true })
            foreach (bool adminStyle in new[] { false, true })
            foreach (double scale in new[] { .75, 1, 1.25, 1.5, 1.75, 2, 2.25, 2.5 })
            {
                view.State.DarkMode = dark; view.ApplyTheme();
                TextBox box = new TextBox { Width = 260.4, Text = "Ctrl+Alt+D", TextAlignment = TextAlignment.Left,
                    Style = view.Window.Resources["SettingInput"] as Style };
                if (adminStyle)
                {
                    string[] colors = new[] { "CardBrush", "TextBrush", "MutedBrush", "LineBrush", "InputBrush", "AccentBrush" }
                        .Select(key => ((SolidColorBrush)view.Window.Resources[key]).Color.ToString()).ToArray();
                    ResourceDictionary resources = LanAdminWindowLegacy.AdminResources(colors);
                    box.Resources.MergedDictionaries.Add(resources); box.Style = resources["AdminInput"] as Style;
                    box.Background = (Brush)view.Window.Resources["CardBrush"]; box.Foreground = (Brush)view.Window.Resources["TextBrush"];
                    box.BorderBrush = (Brush)view.Window.Resources["InputLineBrush"]; box.Padding = new Thickness(9, 4, 9, 4);
                }
                foreach (object key in new[] { "CardBrush", "TextBrush", "LineBrush", "InputLineBrush", "AccentBrush" })
                    box.Resources[key] = view.Window.Resources[key];
                box.Measure(new Size(260.4, 36)); box.Arrange(new Rect(0, 0, 260.4, 36)); box.UpdateLayout(); box.ApplyTemplate();
                InputOutline outline = box.Template.FindName(adminStyle ? "Surface" : "InputSurface", box) as InputOutline;
                double dpi = 96 * scale;
                RenderTargetBitmap image = new RenderTargetBitmap((int)Math.Ceiling(box.ActualWidth * scale), (int)Math.Ceiling(box.ActualHeight * scale), dpi, dpi, PixelFormats.Pbgra32);
                image.Render(box);
                byte[] pixels = new byte[image.PixelWidth * image.PixelHeight * 4]; image.CopyPixels(pixels, image.PixelWidth * 4, 0);
                Color background = ((SolidColorBrush)view.Window.Resources["CardBrush"]).Color;
                int first = (int)(image.PixelWidth * .2), last = (int)(image.PixelWidth * .8), depth = (int)Math.Ceiling(7 * scale), top = 0, bottom = 0;
                for (int x = first; x < last; x++)
                {
                    bool topInk = false, bottomInk = false;
                    for (int y = 0; y < depth; y++)
                    {
                        int[] positions = { (y * image.PixelWidth + x) * 4, ((image.PixelHeight - 1 - y) * image.PixelWidth + x) * 4 };
                        for (int edge = 0; edge < 2; edge++)
                        {
                            int p = positions[edge];
                            bool ink = pixels[p + 3] > 220 && Math.Abs(pixels[p] - background.B) + Math.Abs(pixels[p + 1] - background.G) + Math.Abs(pixels[p + 2] - background.R) > 30;
                            if (edge == 0) topInk |= ink; else bottomInk |= ink;
                        }
                    }
                    if (topInk) top++; if (bottomInk) bottom++;
                }
                Check(outline != null && outline.InkBounds.Bottom + outline.BorderThickness.Bottom / 2 < outline.ActualHeight &&
                    top == last - first && bottom == last - first,
                    (adminStyle ? "管理员" : "主页设置") + (dark ? "深色" : "浅色") + " " + scale * 100 + "% 输入框：上下边线每一列均完整，不只检查控件尺寸：top=" + top + ";bottom=" + bottom);
                if (!dark && !adminStyle && (scale == 1 || scale == 1.75 || scale == 2.5))
                {
                    PngBitmapEncoder png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
                    using (FileStream file = File.Create(Path.Combine(directory, "v139-outline-" + scale * 100 + ".png"))) png.Save(file);
                }
            }
            view.State.DarkMode = false; view.ApplyTheme();
        }
        private static void RenderDragWindow(Window window, string path)
        {
            window.UpdateLayout();
            RenderTargetBitmap bitmap = new RenderTargetBitmap((int)System.Math.Ceiling(window.ActualWidth * 1.5), (int)System.Math.Ceiling(window.ActualHeight * 1.5), 144, 144, PixelFormats.Pbgra32);
            bitmap.Render(window); PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (FileStream stream = File.Create(path)) encoder.Save(stream);
        }
        private static void AdvanceFrames(int milliseconds)
        {
            DateTime frameAt = DateTime.UtcNow.AddMilliseconds(milliseconds);
            PumpUntil(delegate { return DateTime.UtcNow >= frameAt; }, milliseconds + 700);
        }
        private static IEnumerable<T> VisualDescendants<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child is T) yield return (T)child;
                foreach (T descendant in VisualDescendants<T>(child)) yield return descendant;
            }
        }
        private static void RenderMenu(ContextMenu menu, string path)
        {
            menu.Measure(new Size(250, Double.PositiveInfinity)); menu.Arrange(new Rect(0, 0, 250, menu.DesiredSize.Height)); menu.UpdateLayout();
            RenderTargetBitmap bitmap = new RenderTargetBitmap(375, Math.Max(1, (int)Math.Ceiling(menu.ActualHeight * 1.5)), 144, 144, PixelFormats.Pbgra32);
            bitmap.Render(menu); PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (FileStream stream = File.Create(path)) encoder.Save(stream);
        }
        private static void RenderLaidOutWindow(Window window, string path, double dpi = 144)
        {
            // Capture actual WPF client layout after a real HWND resize; forcing the
            // root to a smaller arrange rect can otherwise crop an unchanged window.
            FrameworkElement root = (FrameworkElement)window.Content; root.UpdateLayout();
            double width = root.ActualWidth + root.Margin.Left + root.Margin.Right;
            double height = root.ActualHeight + root.Margin.Top + root.Margin.Bottom;
            RenderTargetBitmap raw = new RenderTargetBitmap((int)Math.Ceiling(width * dpi / 96), (int)Math.Ceiling(height * dpi / 96), dpi, dpi, PixelFormats.Pbgra32); raw.Render(root);
            DrawingVisual visual = new DrawingVisual();
            using (DrawingContext drawing = visual.RenderOpen())
            { drawing.DrawRectangle(window.Background, null, new Rect(0, 0, width, height)); drawing.DrawImage(raw, new Rect(0, 0, width, height)); }
            RenderTargetBitmap image = new RenderTargetBitmap(raw.PixelWidth, raw.PixelHeight, dpi, dpi, PixelFormats.Pbgra32); image.Render(visual);
            PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using (FileStream stream = File.Create(path)) encoder.Save(stream);
        }
        private static bool SameRect(Native.Rect left, Native.Rect right)
        { return left.Left == right.Left && left.Top == right.Top && left.Right == right.Right && left.Bottom == right.Bottom; }
        private static void CheckNativeModes(Application app, string fixture)
        {
            // Use our own HWND as an Explorer-like parent. Do not modify real Explorer.
            Window host = new Window { Width = 1000, Height = 900, Left = 0, Top = 0, WindowStyle = WindowStyle.None, ShowInTaskbar = false };
            host.Show(); Native.TestSurface = new WindowInteropHelper(host).Handle;
            AppState state = new AppState { DesktopMode = false, X = 80, Y = 90, PositionSet = true, ShowCompactButton = true };
            TodoItem item = state.Add("模式切换保留事项");
            Controller controller = new Controller(app, new Storage(Path.Combine(fixture, "mode-test")), state, false);
            controller.Start(); Pump();
            TodoWindow ui = controller.CurrentUi;
            state.LanSharingEnabled = true;
            System.Reflection.MethodInfo handler = typeof(Controller).GetMethod("HandleLanRequest",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            state.LanAccessCode = "QA123456";
            Func<LanRequest, LanResponse> read = request => (LanResponse)handler.Invoke(controller, new object[] { request });
            LanResponse fullRead = read(new LanRequest { Operation = "snapshot", AccessCode = state.LanAccessCode });
            LanResponse noChange = read(new LanRequest { Operation = "snapshot", AccessCode = state.LanAccessCode,
                KnownRevisionPlusOne = state.DataRevision + 1 });
            Check(fullRead.Success && fullRead.Snapshot != null && noChange.Success && noChange.Snapshot == null,
                "局域网完整读取保持兼容，修订号未改变时只返回轻量成功响应");
            Check(!read(new LanRequest { Operation = "snapshot", AccessCode = "WRONG",
                KnownRevisionPlusOne = state.DataRevision + 1 }).Success,
                "轻量局域网刷新仍先验证管理口令，不泄漏未授权状态");
            state.DataRevision++;
            Check(read(new LanRequest { Operation = "snapshot", AccessCode = state.LanAccessCode,
                KnownRevisionPlusOne = state.DataRevision }).Snapshot != null,
                "数据修订号改变后立即返回完整快照，不丢失同步更新");
            Check(!LanJson.Write(new LanRequest { Operation = "snapshot" }).Contains("KnownRevisionPlusOne") &&
                LanJson.Read<LanRequest>("{\"Operation\":\"snapshot\"}").KnownRevisionPlusOne == 0,
                "旧版局域网请求继续使用完整快照，新字段缺失时安全兼容");
            TodoGroup remoteWork = new TodoGroup { Id = "remote-work", Name = "远程工作", SortOrder = 0 };
            TodoGroup remoteLife = new TodoGroup { Id = "remote-life", Name = "远程生活", SortOrder = 1 };
            state.Groups.Add(remoteWork); state.Groups.Add(remoteLife);
            TodoItem remoteA = state.Add("管理员提醒测试", remoteWork.Id), remoteB = state.Add("远程排序目标", remoteLife.Id);
            using (LanService commands = new LanService(delegate { return new LanPeer { InstanceId = "v136-command-fixture" }; }, read, null, 0))
            {
            LanPeer commandPeer = new LanPeer { Address = "127.0.0.1", Port = commands.Port };
            Func<LanRequest, LanResponse> overTcp = delegate(LanRequest command)
            {
                System.Threading.Tasks.Task<LanResponse> transfer = System.Threading.Tasks.Task.Factory.StartNew(delegate { return LanService.Send(commandPeer, command); });
                if (!PumpUntil(delegate { return transfer.IsCompleted; }, 5000)) throw new TimeoutException("本机命令回环测试超时");
                return transfer.Result;
            };
            LanResponse remindResponse = overTcp(new LanRequest { Operation = "reminder", AccessCode = state.LanAccessCode, ItemId = remoteA.Id,
                ReminderIntervalMinutes = 5, DailyReminderMinutesPlusOne = 1, ReminderManaged = true });
            Check(remindResponse.Success && remoteA.ReminderIntervalMinutes == 5 && remoteA.DailyReminderMinutesPlusOne == 1 && remoteA.ReminderManaged,
                "管理员独立提醒经真实 TCP 回环写入客户端，支持分钟与每天午夜并保存不可取消标志");
            ui.OpenItemReminder(remoteA); ui.Control<TextBox>("ItemReminderMinutes").Text = "0";
            ui.Control<Button>("ReminderClearButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); ui.SaveItemReminder();
            Check(remoteA.ReminderIntervalMinutes == 5 && !ui.Control<Button>("ReminderSaveButton").IsEnabled && !ui.Control<Button>("ReminderClearButton").IsEnabled,
                "用户端按钮和执行逻辑双重阻止自行修改或清除管理员保护提醒");
            ui.Control<Button>("ReminderCancelButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Check(!read(new LanRequest { Operation = "reminder", AccessCode = "WRONG", ItemId = remoteA.Id, ReminderIntervalMinutes = 0 }).Success && remoteA.ReminderManaged,
                "错误管理口令不能删除保护提醒");
            Check(!read(new LanRequest { Operation = "reminder", AccessCode = state.LanAccessCode, ItemId = remoteA.Id, ReminderIntervalMinutes = 10081 }).Success && remoteA.ReminderIntervalMinutes == 5,
                "服务器拒绝非法提醒时间，原有规则不受影响");
            long oldRevision = state.DataRevision;
            Check(overTcp(new LanRequest { Operation = "reorder", AccessCode = state.LanAccessCode, ItemId = remoteA.Id, TargetId = remoteB.Id,
                After = true, ExpectedRevisionPlusOne = oldRevision + 1 }).Success && remoteA.GroupId == remoteLife.Id && state.PendingSort == "Manual" && remoteA.SortOrder > remoteB.SortOrder,
                "远程拖动经真实 TCP 修改客户端分组与手动顺序，不只是管理员视觉排序");
            Check(!read(new LanRequest { Operation = "reorder", AccessCode = state.LanAccessCode, ItemId = remoteA.Id, TargetId = remoteB.Id,
                ExpectedRevisionPlusOne = oldRevision + 1 }).Success && remoteA.SortOrder > remoteB.SortOrder,
                "并发修订冲突拒绝旧拖动结果，保护其他用户新修改");
            Check(overTcp(new LanRequest { Operation = "reorder-group", AccessCode = state.LanAccessCode, ItemId = remoteLife.Id, TargetId = remoteWork.Id }).Success && remoteLife.SortOrder < remoteWork.SortOrder,
                "管理员分组拖动顺序同步到用户主页");
            ui.SetGroupPinned(remoteWork, true);
            LanSnapshot pinnedSnapshot = overTcp(new LanRequest { Operation = "snapshot", AccessCode = state.LanAccessCode }).Snapshot;
            Check(pinnedSnapshot.Groups.First().Id == remoteWork.Id && pinnedSnapshot.Groups.First().Pinned,
                "分组置顶状态和置顶优先顺序通过真实 TCP 快照同步到管理员");
            int workOrder = remoteWork.SortOrder, lifeOrder = remoteLife.SortOrder;
            Check(!overTcp(new LanRequest { Operation = "reorder-group", AccessCode = state.LanAccessCode, ItemId = remoteWork.Id, TargetId = remoteLife.Id,
                ExpectedRevisionPlusOne = state.DataRevision + 1 }).Success && remoteWork.SortOrder == workOrder && remoteLife.SortOrder == lifeOrder && remoteWork.Pinned,
                "服务端拒绝把置顶组拖入普通组层，保护排序及置顶偏好");
            ui.SetGroupPinned(remoteWork, false);
            long emptyRevision = state.DataRevision;
            Check(overTcp(new LanRequest { Operation = "reorder", AccessCode = state.LanAccessCode, ItemId = remoteA.Id,
                GroupId = remoteWork.Id, ExpectedRevisionPlusOne = emptyRevision + 1 }).Success && remoteA.GroupId == remoteWork.Id,
                "远程拖到空分组标题通过真实 TCP 保存归属，无需伪造目标事项");
            Check(!read(new LanRequest { Operation = "reorder", AccessCode = state.LanAccessCode, ItemId = remoteA.Id,
                GroupId = null, ExpectedRevisionPlusOne = emptyRevision + 1 }).Success && remoteA.GroupId == remoteWork.Id,
                "空分组拖入同样拒绝过期修订，防止覆盖并发操作");
            Check(!read(new LanRequest { Operation = "reorder", AccessCode = state.LanAccessCode, ItemId = remoteA.Id,
                GroupId = "missing39", ExpectedRevisionPlusOne = state.DataRevision + 1 }).Success && remoteA.GroupId == remoteWork.Id,
                "空组拖放的目标被删除时明确拒绝，不悄悄移到未分组");
            state.DisableGroups = true;
            Check(!read(new LanRequest { Operation = "reorder", AccessCode = state.LanAccessCode, ItemId = remoteA.Id,
                GroupId = null, ExpectedRevisionPlusOne = state.DataRevision + 1 }).Success && remoteA.GroupId == remoteWork.Id,
                "关闭分组后管理员不能借空组落点改掉隐藏归属");
            state.DisableGroups = false;
            state.Toggle(remoteA);
            Check(!ReminderScheduler.Due(remoteA, DateTime.Now.AddDays(1), DateTime.UtcNow.AddDays(1)).Any,
                "完成待办立即停止管理员保护提醒，不需用户取消");
            LanTaskInfo remoteSnapshot = overTcp(new LanRequest { Operation = "snapshot", AccessCode = state.LanAccessCode }).Snapshot.Tasks.First(t => t.Id == remoteA.Id);
            Check(remoteSnapshot.ReminderManaged && remoteSnapshot.ReminderIntervalMinutes == 5 && remoteSnapshot.SortOrder == remoteA.SortOrder,
                "局域网快照携带提醒规则与排序信息，重新连接可正确恢复");
            }
            state.Items.Remove(remoteA); state.Items.Remove(remoteB); state.Groups.Remove(remoteWork); state.Groups.Remove(remoteLife);
            state.LanSharingEnabled = false;
            ui.Input.Text = "尚未提交的输入"; ui.Control<Expander>("CompletedSection").IsExpanded = true;
            ui.Remove(item); ui.SettingsOpen = true;
            // Session restoration, not wall-clock expiry, is the subject of this test.
            ui.AdvanceUndoForTest(-120);
            Native.Rect original; Native.GetWindowRect(ui.Desktop.Handle, out original);
            bool allModes = true, allPosition = true, allSession = true;
            for (int i = 0; i < 12; i++)
            {
                state.DesktopMode = !state.DesktopMode;
                TodoWindow previous = controller.CurrentUi;
                controller.SwitchMode(); Pump(); ui = controller.CurrentUi;
                Native.Rect rect; Native.GetWindowRect(ui.Desktop.Handle, out rect);
                allModes &= ui != previous && Native.IsWindowVisible(ui.Desktop.Handle) && ui.Desktop.IsAttached == state.DesktopMode && ui.Window.ShowInTaskbar != state.DesktopMode;
                allPosition &= rect.Left == original.Left && rect.Top == original.Top && rect.Right == original.Right && rect.Bottom == original.Bottom;
                allSession &= ui.Input.Text == "尚未提交的输入" && ui.Removed.Count == 1 && ui.SettingsOpen && ui.Control<Expander>("CompletedSection").IsExpanded;
            }
            Check(allModes, "12 次桌面/窗口模式切换：重建 HWND、可见及正确任务栏状态");
            Check(allPosition, "12 次模式切换保留屏幕位置和尺寸");
            Check(allSession, "模式切换保留未提交输入、撤销栈、折叠及设置页面");
            state.DesktopMode = true; controller.SwitchMode(); Pump(); ui = controller.CurrentUi;
            bool anchored = true;
            int finalWidth = 0, finalHeight = 0;
            ui.Desktop.BeginResize();
            for (int i = 0; i < 80; i++)
            {
                int width = original.Right - original.Left + i % 15;
                int height = original.Bottom - original.Top + i % 12;
                finalWidth = width; finalHeight = height;
                ui.Desktop.Move(original.Left, original.Top, width, height);
                Native.Rect rect; Native.GetWindowRect(ui.Desktop.Handle, out rect);
                anchored &= rect.Left == original.Left && rect.Top == original.Top && rect.Right - rect.Left == width && rect.Bottom - rect.Top == height;
            }
            ui.Desktop.EndResize();
            Pump();
            bool layoutMatches = Math.Abs(ui.Window.ActualWidth * ui.Scale - finalWidth) < 2 && Math.Abs(ui.Window.ActualHeight * ui.Scale - finalHeight) < 2;
            Check(anchored, "80 次嵌入窗口缩放始终保留左上角及准确尺寸");
            Check(layoutMatches, "原生缩放同步 WPF 内容布局，无双重尺寸写入");
            Native.Rect expected; Native.GetWindowRect(ui.Desktop.Handle, out expected);
            Native.SetWindowPos(ui.Desktop.Handle, IntPtr.Zero, 5, 6, 25, 26, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE); Pump();
            Native.Rect pinned; Native.GetWindowRect(ui.Desktop.Handle, out pinned);
            Check(pinned.Left == expected.Left && pinned.Top == expected.Top && pinned.Right == expected.Right && pinned.Bottom == expected.Bottom, "父客户坐标防护阻止意外重定位和尺寸回写");
            ui.RememberGeometry(); controller.Hide(); controller.Show(); Pump();
            Native.GetWindowRect(ui.Desktop.Handle, out pinned);
            Check(Native.IsWindowVisible(ui.Desktop.Handle) && pinned.Left == expected.Left && pinned.Top == expected.Top, "嵌入模式隐藏后重新显示不丢失窗口");
            ui.Undo(); Check(!item.Deleted && state.Pending.Count() == 1, "模式切换后的撤销仍有效");
            controller.HideTray(); Pump();
            Check(state.HideTray && !controller.TrayVisible && ui.Window.IsVisible && ui.Control<CheckBox>("ShowTrayToggle").IsChecked == false,
                "隐藏托盘实际移除图标但保留可见卡片和设置恢复入口");
            controller.Hide(); Pump();
            Check(!state.HideTray && controller.TrayVisible && !ui.Window.IsVisible,
                "隐藏托盘后再最小化会自动恢复托盘，不会同时失去两个入口");
            controller.Show(); Pump();
            ui.Window.Close(); Pump();
            Check(!ui.Window.IsVisible && Native.IsWindow(ui.Desktop.Handle), "主窗口关闭取消后延后隐藏，不在 Closing 事件内修改 Visibility");
            controller.Show(); Pump();
            for (int actionIndex = 0; actionIndex < 4; actionIndex++)
            {
                TrayMenu actualMenu = controller.OpenTrayMenu(); Pump(); Pump();
                bool menuClosed = false; actualMenu.Window.Closed += delegate { menuClosed = true; };
                actualMenu.Buttons[actionIndex].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Pump(); Pump(); ui = controller.CurrentUi;
                Check(menuClosed && ui.Window.IsVisible && Native.IsWindow(ui.Desktop.Handle),
                    "实际可见托盘菜单动作 " + actionIndex + "：先完全关闭菜单再执行，不触发生命周期异常");
                if (actionIndex == 1) Check(!controller.TrayVisible && state.HideTray, "从可见托盘菜单隐藏图标确实生效");
                if (actionIndex == 3) Check(ui.SettingsOpen, "从可见托盘菜单打开设置确实生效");
            }
            TrayMenu replacedMenu = controller.OpenTrayMenu(); Pump();
            TrayMenu replacementMenu = controller.OpenTrayMenu(); Pump(); Pump();
            Check(!replacedMenu.Window.IsVisible && replacementMenu.Window.IsVisible, "重复打开托盘菜单只留下最新的一份，不重复关闭旧 Window");
            replacementMenu.Close(); replacementMenu.Close(); replacementMenu.ShowAt(new Native.Point { X = 100, Y = 100 }); Pump();
            Check(!replacementMenu.Window.IsVisible, "关闭后的托盘菜单不再 Show 或 EnsureHandle");
            state.HideTray = false;
            state.DesktopMode = true; controller.SwitchMode(); Pump(); ui = controller.CurrentUi;
            bool beforeToggle = state.DesktopMode;
            controller.ToggleDesktopFromTray(); Pump(); ui = controller.CurrentUi;
            Check(state.DesktopMode != beforeToggle && ui.Window.IsVisible && !ui.Desktop.IsAttached,
                "托盘可实际取消嵌入桌面，并重建成可见普通窗口");
            controller.ToggleDesktopFromTray(); Pump(); ui = controller.CurrentUi;
            Check(state.DesktopMode == beforeToggle && ui.Desktop.IsAttached && ui.Window.IsVisible,
                "托盘再次点击恢复嵌入，保留清单且窗口不丢失");
            Window replacementHost = new Window { Width = 1000, Height = 900, Left = 0, Top = 0, WindowStyle = WindowStyle.None, ShowInTaskbar = false };
            replacementHost.Show(); IntPtr replacementHandle = new WindowInteropHelper(replacementHost).Handle;
            Native.TestSurface = replacementHandle;
            TodoWindow beforeHostReplacement = controller.CurrentUi;
            double savedX = state.X, savedY = state.Y;
            controller.Tick(); Pump(); ui = controller.CurrentUi;
            Check(ui != beforeHostReplacement && ui.Desktop.IsAttached && ui.Desktop.Parent == replacementHandle &&
                state.X == savedX && state.Y == savedY && ui.RebuildRequested != null,
                "Explorer 桌面宿主更换后按已保存坐标重建，不读取失效父窗口并可继续跨屏重建");
            host.Close();
            Native.TestSurface = new IntPtr(-1);
            state.DesktopMode = true; controller.SwitchMode(); Pump(); TodoWindow fallbackUi = controller.CurrentUi;
            Check(state.DesktopMode && fallbackUi.IsAttachmentFallback && !fallbackUi.Desktop.IsAttached && fallbackUi.Window.ShowInTaskbar &&
                fallbackUi.Control<TextBlock>("StatusLabel").Text.Contains("等待桌面恢复"),
                "Explorer 尚未重建桌面宿主时显示可操作的任务栏窗口，不闪退或隐身");
            Native.TestSurface = replacementHandle; controller.Tick(); Pump(); TodoWindow recoveredUi = controller.CurrentUi;
            Check(recoveredUi != fallbackUi && recoveredUi.Desktop.IsAttached && recoveredUi.Desktop.Parent == replacementHandle &&
                !recoveredUi.Window.ShowInTaskbar && state.DesktopMode,
                "Explorer 桌面宿主恢复后自动从临时窗口重新嵌入桌面");
            Native.TestSurface = new IntPtr(1); state.DesktopMode = true; controller.SwitchMode(); Pump();
            Check(state.DesktopMode && controller.CurrentUi.IsAttachmentFallback && !controller.CurrentUi.Desktop.IsAttached &&
                Native.IsWindowVisible(controller.CurrentUi.Desktop.Handle) && controller.CurrentUi.Window.ShowInTaskbar,
                "桌面句柄失效时保留嵌入偏好并回退可见窗口，不需重启");
            Native.TestSurface = replacementHandle; controller.SwitchMode(); Pump(); ui = controller.CurrentUi;
            ui.OpenItemReminder(item);
            ui.Control<CheckBox>("ItemIntervalToggle").IsChecked = true; ui.Control<CheckBox>("ItemIntervalToggle").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            ui.Control<TextBox>("ItemReminderHours").Text = "0"; ui.Control<TextBox>("ItemReminderMinutes").Text = "45";
            controller.SwitchMode(); Pump(); ui = controller.CurrentUi;
            Check(ui.ReminderOpen && ui.Control<CheckBox>("ItemIntervalToggle").IsChecked == true &&
                ui.Control<TextBox>("ItemReminderHours").Text == "0" && ui.Control<TextBox>("ItemReminderMinutes").Text == "45",
                "宿主重建保留独立提醒页及未保存的小时、分钟草稿");
            ui.Control<Button>("ReminderCancelButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            state.Title = "工作清单"; controller.SwitchMode(); Pump(); ui = controller.CurrentUi;
            Check(ui.Control<TextBlock>("TitleLabel").Text == "工作清单", "宿主重建保留自定义主页标题");
            state.ReminderIntervalHours = 1; state.LastIntervalReminderAt = DateTime.UtcNow.AddHours(-2).Ticks;
            controller.Tick(); Pump();
            Check(!app.Windows.Cast<Window>().Any(window => window.IsVisible && window.Title == "DeskTodo 提醒"),
                "旧版全局提醒即使到期也不再调度");
            TodoItem other = state.Add("第二条独立提醒事项");
            item.ReminderIntervalMinutes = 1; item.LastIntervalReminderAt = DateTime.UtcNow.AddMinutes(-2).Ticks;
            other.ReminderIntervalMinutes = 1; other.LastIntervalReminderAt = DateTime.UtcNow.AddMinutes(-2).Ticks;
            controller.Tick(); Pump();
            Window shown = app.Windows.Cast<Window>().FirstOrDefault(window => window.IsVisible && window.Title == "DeskTodo 提醒");
            Check(shown != null && ((TextBlock)shown.FindName("ReminderItems")).Text == item.Text &&
                app.Windows.Cast<Window>().Count(window => window.IsVisible && window.Title == "DeskTodo 提醒") == 1,
                "控制器实际到期弹窗只展示第一条事项，同一时间不堆叠弹窗");
            ((Button)shown.FindName("DismissButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            controller.Tick(); Pump();
            shown = app.Windows.Cast<Window>().FirstOrDefault(window => window.IsVisible && window.Title == "DeskTodo 提醒");
            Check(shown != null && ((TextBlock)shown.FindName("ReminderItems")).Text == other.Text,
                "第一条关闭后轮到其他到期事项，提醒互不覆盖");
            state.Toggle(other); controller.Tick(); Pump();
            Check(!app.Windows.Cast<Window>().Any(window => window.IsVisible && window.Title == "DeskTodo 提醒"),
                "事项完成后正在显示的对应提醒也自动关闭");
            ui = controller.CurrentUi;
            state.WindowOpacityPercent = 40; ui.ApplyTheme();
            ui.Control<Button>("CompactButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump();
            Window shownBubble = app.Windows.Cast<Window>().FirstOrDefault(window => window.IsVisible && Math.Abs(window.Width - 62) < 1 && Math.Abs(window.Height - 62) < 1);
            Check(state.MiniMode && !state.DesktopMode && !controller.CurrentUi.Window.IsVisible && shownBubble != null,
                "缩小按钮会退出桌面嵌入并切换为可拖动悬浮图标");
            controller.Show(); Pump(); ui = controller.CurrentUi;
            Check(!state.MiniMode && ui.Window.IsVisible && ui.Window.Opacity == 1 &&
                Math.Abs(ui.Control<Border>("Card").Opacity - 0.4) < 0.001 &&
                !app.Windows.Cast<Window>().Any(window => window.IsVisible && Math.Abs(window.Width - 62) < 1 && Math.Abs(window.Height - 62) < 1),
                "悬浮图标恢复不叠加透明动画闪帧，并保留用户设置的卡片透明度");
            TrayMenu exitMenu = controller.OpenTrayMenu(); Pump(); Pump();
            exitMenu.Buttons[4].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump();
            for (int attempt = 0; attempt < 10 && Native.IsWindow(ui.Desktop.Handle); attempt++)
            { System.Threading.Thread.Sleep(10); Pump(); }
            Check(!Native.IsWindow(ui.Desktop.Handle) && !controller.TrayVisible, "可见托盘菜单退出会保存数据并彻底关闭程序，不重复 Close");
            controller.Dispose(); controller.Exit();
            Native.TestSurface = IntPtr.Zero; replacementHost.Close();
        }
        private static int CardCenterAlpha(TodoWindow view)
        {
            FrameworkElement root = (FrameworkElement)view.Window.Content;
            root.Measure(new Size(view.Window.Width, view.Window.Height));
            root.Arrange(new Rect(0, 0, view.Window.Width, view.Window.Height)); root.UpdateLayout();
            int width = Math.Max(1, (int)view.Window.Width), height = Math.Max(1, (int)view.Window.Height);
            RenderTargetBitmap image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            image.Render(root);
            byte[] pixel = new byte[4];
            image.CopyPixels(new Int32Rect(width / 2, height / 2, 1, 1), pixel, 4, 0);
            return pixel[3];
        }
        private static int[] CornerAntialiasSamples(TodoWindow view)
        {
            FrameworkElement root = (FrameworkElement)view.Window.Content;
            int width = (int)view.Window.Width, height = (int)view.Window.Height;
            root.Measure(new Size(width, height));
            root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
            RenderTargetBitmap image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            image.Render(root);
            byte[] pixels = new byte[width * height * 4];
            image.CopyPixels(pixels, width * 4, 0);
            int[] samples = new int[4];
            for (int y = 0; y < 24; y++) for (int x = 0; x < 24; x++)
            {
                int[] points = { x, y, width - 1 - x, y, x, height - 1 - y, width - 1 - x, height - 1 - y };
                for (int corner = 0; corner < 4; corner++)
                {
                    int alpha = pixels[(points[corner * 2 + 1] * width + points[corner * 2]) * 4 + 3];
                    if (alpha > 0 && alpha < 255) samples[corner]++;
                }
            }
            return samples;
        }
        private static bool NoBrightCornerSeams(string path)
        {
            using (System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap(path))
            {
                double scale = bitmap.HorizontalResolution / 96.0;
                int inset = (int)Math.Round(8 * scale);
                int extent = Math.Min((int)Math.Ceiling(26 * scale), Math.Min(bitmap.Width, bitmap.Height));
                int edgeStrip = (int)Math.Ceiling(8 * scale);
                for (int y = 0; y < extent; y++) for (int x = 0; x < extent; x++)
                {
                    // Inspect only the card perimeter. A legitimate white dialog
                    // can be just 16 DIP from its edge at the minimum window size.
                    if (x >= edgeStrip && y >= edgeStrip) continue;
                    foreach (System.Drawing.Point corner in new[] {
                        new System.Drawing.Point(x, y), new System.Drawing.Point(bitmap.Width - 1 - x, y),
                        new System.Drawing.Point(x, bitmap.Height - 1 - y),
                        new System.Drawing.Point(bitmap.Width - 1 - x, bitmap.Height - 1 - y) })
                    {
                        int sampleX = corner.X < bitmap.Width / 2 ? corner.X + inset : corner.X - inset;
                        int sampleY = corner.Y < bitmap.Height / 2 ? corner.Y + inset : corner.Y - inset;
                        System.Drawing.Color pixel = bitmap.GetPixel(sampleX, sampleY);
                        if (pixel.A > 180 && pixel.R > 225 && pixel.G > 225 && pixel.B > 225) return false;
                    }
                }
                return true;
            }
        }
        private static void Render(TodoWindow view, string path, double dpi = 144)
        {
            FrameworkElement root = (FrameworkElement)view.Window.Content;
            root.Measure(new Size(view.Window.Width, view.Window.Height));
            root.Arrange(new Rect(0, 0, view.Window.Width, view.Window.Height)); root.UpdateLayout();
            RenderTargetBitmap image = new RenderTargetBitmap((int)Math.Ceiling(view.Window.Width * dpi / 96),
                (int)Math.Ceiling(view.Window.Height * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
            image.Render(root);
            PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using (FileStream stream = File.Create(path)) encoder.Save(stream);
        }
        private static void Render(Window window, string path, double? renderHeight = null, double? renderWidth = null)
        {
            FrameworkElement root = (FrameworkElement)window.Content;
            double width = renderWidth ?? window.Width;
            root.Measure(new Size(width, renderHeight ?? 600));
            double height = renderHeight ?? Math.Max(260, root.DesiredSize.Height);
            root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
            RenderTargetBitmap raw = new RenderTargetBitmap((int)(width * 1.5), (int)(height * 1.5), 144, 144, PixelFormats.Pbgra32);
            raw.Render(root);
            DrawingVisual background = new DrawingVisual();
            using (DrawingContext drawing = background.RenderOpen())
            {
                drawing.DrawRectangle(window.Background ?? Brushes.Transparent, null, new Rect(0, 0, width, height));
                drawing.DrawImage(raw, new Rect(0, 0, width, height));
            }
            RenderTargetBitmap image = new RenderTargetBitmap((int)(width * 1.5), (int)(height * 1.5), 144, 144, PixelFormats.Pbgra32);
            image.Render(background);
            PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using (FileStream stream = File.Create(path)) encoder.Save(stream);
        }
    }
}
