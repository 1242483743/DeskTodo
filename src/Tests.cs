using System;
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
    internal static class Tests
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
                using (System.Drawing.Icon shellIcon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location))
                using (System.Drawing.Bitmap bitmap = shellIcon.ToBitmap())
                    Check(bitmap.GetPixel(0, 0).A == 0 && bitmap.GetPixel(bitmap.Width - 1, 0).A == 0 &&
                        bitmap.GetPixel(0, bitmap.Height - 1).A == 0 && bitmap.GetPixel(bitmap.Width - 1, bitmap.Height - 1).A == 0,
                        "Windows 实际提取的 EXE 图标四角透明，不带白色方形画布");
                Check(Native.GetAwarenessFromDpiAwarenessContext(Native.GetThreadDpiAwarenessContext()) == 2,
                    "进程使用 Per-Monitor DPI 感知，跨不同缩放比例屏幕不沿用主屏比例");
                AppState state = new AppState();
                Check(state.Locked && state.Title == "DeskTodo" && state.DefaultCorner == "TopRight" && state.DefaultScalePercent == 100,
                    "首次启动默认锁定、名称为 DeskTodo，并使用右上角 100% 偏好");
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
                string future = File.ReadAllText(newer.DataPath).Replace("\"Version\":1", "\"Version\":2"); File.WriteAllText(newer.DataPath, future);
                bool newerBlocked = false; try { newer.Load(); } catch (InvalidOperationException) { newerBlocked = true; }
                Check(newerBlocked && File.ReadAllText(newer.DataPath) == future, "新版本数据不会被旧备份降级覆盖");
                AppState extreme = new AppState { Width = double.PositiveInfinity, Height = 10, X = double.NaN }; extreme.Validate();
                Check(extreme.Width == 360 && extreme.Height == 360 && !extreme.PositionSet, "无效窗口尺寸和位置修正");
                AppState largestPreset = new AppState { Width = 900, Height = 1300, DefaultScalePercent = 250 }; largestPreset.Validate();
                Check(largestPreset.Width == 900 && largestPreset.Height == 1300 && largestPreset.DefaultScalePercent == 250,
                    "250% 默认比例及对应最大逻辑尺寸可持久保存");
                AppState importTarget = new AppState { X = -500, Y = 20, Width = 500, Height = 700, DesktopMode = true };
                importTarget.Add("导入前");
                AppState importSource = new AppState { Title = "导入清单", ThemeId = "lavender", DarkMode = true, HideItemTimes = true, ShowItemDates = true,
                    ShowMinimizeButton = true, ShowCloseButton = true, DefaultCorner = "TopRight", DefaultScalePercent = 150 };
                importSource.Add("导入后的事项"); Controller.ApplyImportedState(importTarget, importSource);
                Check(importTarget.Items.Count == 1 && importTarget.Items[0].Text == "导入后的事项" && importTarget.Title == "导入清单" && importTarget.ThemeId == "lavender" &&
                    importTarget.HideItemTimes && importTarget.ShowItemDates && importTarget.ShowMinimizeButton && importTarget.ShowCloseButton &&
                    importTarget.DefaultCorner == "TopRight" && importTarget.DefaultScalePercent == 150 &&
                    importTarget.X == -500 && importTarget.Y == 20 && importTarget.Width == 500 && importTarget.Height == 700 && importTarget.DesktopMode,
                    "导入备份替换清单与外观，同时保留当前电脑的安全窗口位置和桌面模式");
                AppState viewState = new AppState { DesktopMode = false, Locked = false };
                TodoWindow view = new TodoWindow(viewState);
                view.Changed = delegate { store.Save(viewState); view.IsSaved = true; };
                Check(ReferenceEquals(view.Control<Image>("HeaderIcon").Source, UiAssets.InternalIcon) && view.Window.Icon == null &&
                    UiAssets.InternalIcon.PixelWidth < 1254 && UiAssets.InternalIcon.PixelHeight < 1254,
                    "新 PNG 裁去透明留白后只加载到主界面内部，不替换窗口、程序或托盘图标");
                Check(view.Window.AllowsTransparency && view.Window.Background == Brushes.Transparent, "圆角使用带透明度的抗锯齿绘制，不使用像素窗口区域");
                Check(view.Window.Resources[SystemParameters.FocusVisualStyleKey] is Style, "窗口覆盖系统默认虚线焦点装饰");
                string[] focuslessNames = { "LockButton", "MinimizeButton", "CloseButton", "MenuButton", "AddButton", "AddInput", "TitleEditor", "TitleSettingInput", "TitleSaveButton", "UndoButton", "ClearCompletedButton", "CompletedSection", "SettingsBack", "DesktopToggle", "LockToggle", "ShowTrayToggle", "ShowMinimizeToggle", "ShowCloseToggle", "DarkToggle", "ShowTimesToggle", "ShowDateToggle", "StartupToggle", "ItemIntervalToggle", "ItemDailyToggle", "ItemReminderHours", "ItemReminderMinutes", "ItemReminderDailyTime", "HoursUpButton", "HoursDownButton", "MinutesUpButton", "MinutesDownButton", "DailyTimeUpButton", "DailyTimeDownButton", "ReminderClearButton", "ReminderCancelButton", "ReminderSaveButton", "ResetButton", "PositionMenuButton", "ReattachButton", "ExportButton", "ImportButton", "RetryButton", "HideButton", "ExitButton", "EditInput", "EditCancelButton", "EditSaveButton", "ConfirmCloseButton", "ConfirmCancelButton", "ConfirmAcceptButton", "PositionCloseButton", "PositionRestoreButton", "PositionSaveButton", "ResizeThumb" };
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
                lockButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(viewState.Locked && view.Control<System.Windows.Shapes.Path>("LockShackle").Data.ToString() == Geometry.Parse(UiGlyphs.ClosedShackle).ToString() && lockButton.Background == Brushes.Transparent &&
                    view.Control<Thumb>("ResizeThumb").Visibility == Visibility.Hidden, "锁定后显示闭锁图标、无外围框并禁用缩放");
                view.BeginTitleEdit();
                Check(!view.TitleEditing && view.Control<TextBlock>("TitleLabel").Cursor == Cursors.Arrow,
                    "主页标题不再提供原位编辑，整个顶部保持为拖动区域");
                Render(view, Path.Combine(directory, "locked.png"));
                lockButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(!viewState.Locked && view.Control<System.Windows.Shapes.Path>("LockShackle").Data.ToString() == Geometry.Parse(UiGlyphs.OpenShackle).ToString() && view.Control<Thumb>("ResizeThumb").Visibility == Visibility.Visible, "解除锁定恢复开锁图标和缩放手柄");
                Button addButton = view.Control<Button>("AddButton");
                Grid plus = addButton.Content as Grid;
                Border plusHorizontal = plus == null ? null : plus.Children[0] as Border;
                Border plusVertical = plus == null ? null : plus.Children[1] as Border;
                Check(plus != null && plusHorizontal != null && plusVertical != null && addButton.Width == 32 && addButton.Height == 32 && plus.Width == 18 && plus.Height == 18 &&
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
                Button rowMenu = row.Children.OfType<Button>().First();
                StackPanel rowDots = rowMenu.Content as StackPanel;
                Check(rowMenu.FocusVisualStyle == null && rowDots != null && rowDots.Orientation == Orientation.Vertical && rowDots.Children.Count == 3,
                    "事项菜单使用完整的垂直圆点且无焦点框");
                Check(ToolTipService.GetPlacement(rowMenu) == PlacementMode.Left && ToolTipService.GetHorizontalOffset(rowMenu) == -8 &&
                    rowMenu.ToolTip.ToString() == "编辑、复制、提醒或删除", "事项菜单提示固定显示在按钮左侧，不会被卡片右边缘截断");
                Check(row.ContextMenu.Items.Count == 4 && ((MenuItem)row.ContextMenu.Items[1]).Header.ToString() == "复制事项" &&
                    ((MenuItem)row.ContextMenu.Items[2]).Header.ToString() == "设置提醒",
                    "每条事项右键和右侧菜单都提供复制与独立提醒入口");
                Check(row.ContextMenu.FontSize == row.Children.OfType<TextBlock>().First().FontSize,
                    "未完成事项菜单字号与待办文字一致，不随大卡片重复放大");
                ((CheckBox)row.Children[0]).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
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
                FrameworkElement clear = view.Control<FrameworkElement>("ClearCompletedButton");
                Border divider = view.Control<Border>("CompletedDivider");
                double clearTop = clear.TranslatePoint(new Point(), divider).Y;
                Check(clearTop >= 9.5 && clear.ActualHeight >= 30, "清空按钮整个悬停范围与分隔线至少间隔 9.5 DIP，不再重合");
                Check(((System.Windows.Shapes.Path)((Grid)((Button)clear).Content).Children[0]).StrokeThickness == UiGlyphs.Stroke &&
                    view.Control<System.Windows.Shapes.Path>("LockShackle").StrokeThickness == UiGlyphs.Stroke,
                    "垃圾桶与开闭锁改为矢量图形，线宽统一为最小化的 1.8 DIP");
                row = (Grid)view.Control<StackPanel>("PendingList").Children[0];
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
                Check(view.UndoSecondsRemaining == 15 && view.Control<TextBlock>("UndoMessage").Text.EndsWith("15 秒"), "整批清空提示从 15 秒倒计时开始");
                view.AdvanceUndoForTest(2);
                Check(view.UndoSecondsRemaining <= 13 && view.Control<TextBlock>("UndoMessage").Text.EndsWith(view.UndoSecondsRemaining + " 秒"), "倒计时文字按剩余时间更新");
                Render(view, Path.Combine(directory, "undo-countdown.png"));
                view.Undo();
                Check(viewState.Completed.Count() == 2, "撤销一次恢复整批已清空事项");
                view.ClearCompleted(); view.AdvanceUndoForTest(14);
                DateTime undoDeadline = DateTime.UtcNow.AddSeconds(2);
                while (view.Removed.Count > 0 && DateTime.UtcNow < undoDeadline) { Pump(); System.Threading.Thread.Sleep(20); }
                Check(view.Removed.Count == 0 && view.Control<FrameworkElement>("UndoPanel").Visibility == Visibility.Collapsed && viewState.Completed.Count() == 0,
                    "真实 DispatcherTimer 在 15 秒期限归零后自动移除撤销提示与记录");
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
                Check(view.Control<StackPanel>("PendingList").Children.Count == 34, "长文本与大量事项构建");
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
                CheckBox visualToggle = view.Control<CheckBox>("DarkToggle"); visualToggle.ApplyTemplate();
                Grid toggleTrack = visualToggle.Template.FindName("Track", visualToggle) as Grid;
                System.Windows.Shapes.Ellipse toggleKnob = visualToggle.Template.FindName("Knob", visualToggle) as System.Windows.Shapes.Ellipse;
                Check(toggleTrack != null && toggleKnob != null && toggleTrack.Width == 36 && toggleTrack.Height == 20 && toggleTrack.VerticalAlignment == VerticalAlignment.Center &&
                    toggleKnob.Width == 16 && toggleKnob.Height == 16 && toggleKnob.VerticalAlignment == VerticalAlignment.Center,
                    "设置开关使用独立圆形滑块并在圆角轨道内完整居中");
                bool fakeStartup = false;
                view.GetStartup = delegate { return fakeStartup; }; view.SetStartup = delegate(bool value) { fakeStartup = value; };
                view.Control<CheckBox>("StartupToggle").IsChecked = true;
                view.Control<CheckBox>("StartupToggle").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(fakeStartup, "设置自启开关事件（模拟，不修改注册表）");
                Check(view.Control<TextBlock>("SettingsVersion").Text == "v1.22", "设置页最下方显示当前版本号");
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
                    view.Control<TextBlock>("CustomSectionLabel").Text == "自定义界面",
                    "设置分组使用基础功能与自定义界面名称");
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
                row = (Grid)view.Control<StackPanel>("PendingList").Children[0];
                Check(viewState.HideItemTimes && row.Children.OfType<TextBlock>().Single(textBlock => Equals(textBlock.Tag, "ItemTime")).Visibility == Visibility.Collapsed,
                    "设置可关闭每条事项的添加/完成时间");
                view.Control<CheckBox>("ShowTimesToggle").IsChecked = true;
                view.Control<CheckBox>("ShowTimesToggle").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(!viewState.HideItemTimes && view.Control<CheckBox>("ShowDateToggle").IsEnabled, "设置可重新开启事项时间及月日选项");
                Check(view.Control<CheckBox>("ShowDateToggle").IsChecked != true && !viewState.ShowItemDates, "显示月日默认关闭");
                view.Control<CheckBox>("ShowDateToggle").IsChecked = true;
                view.Control<CheckBox>("ShowDateToggle").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                row = (Grid)view.Control<StackPanel>("PendingList").Children[0];
                Check(viewState.ShowItemDates && row.Children.OfType<TextBlock>().Single(textBlock => Equals(textBlock.Tag, "ItemTime")).Text.Contains("/"),
                    "显示月日开启后，事项时间使用月/日和时分");
                Render(view, Path.Combine(directory, "settings.png"));
                view.Control<CheckBox>("DarkToggle").IsChecked = true;
                view.Control<CheckBox>("DarkToggle").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(viewState.DarkMode && ((SolidColorBrush)view.Window.Resources["CardBrush"]).Color == (Color)ColorConverter.ConvertFromString("#222B26"), "设置深色开关与主界面共用主题");
                view.Control<RadioButton>("ThemeOcean").IsChecked = true;
                view.Control<RadioButton>("ThemeOcean").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(viewState.ThemeId == "ocean" && ((SolidColorBrush)view.Window.Resources["AccentBrush"]).Color == (Color)ColorConverter.ConvertFromString("#70A3B9") &&
                    ThemeCatalog.All.Length == 12 && ThemeCatalog.All.All(palette => view.Window.FindName("Theme" + Char.ToUpperInvariant(palette.Id[0]) + palette.Id.Substring(1)) != null),
                    "原有五套加新增七套主题均提供完整深浅调色板和设置入口");
                UniformGrid themeGrid = view.Control<UniformGrid>("ThemeGrid");
                view.Window.UpdateLayout();
                Check(themeGrid.Columns == 3 && themeGrid.Rows == 4 && themeGrid.Children.Count == 12 &&
                    themeGrid.Children.Cast<FrameworkElement>().Select(child => child.ActualWidth).Max() -
                    themeGrid.Children.Cast<FrameworkElement>().Select(child => child.ActualWidth).Min() < 0.1,
                    "十二套主题固定为三列四行，三字名称不再挤乱列宽");
                Check(view.Window.FindName("IntervalReminderToggle") == null && view.Window.FindName("DailyReminderToggle") == null &&
                    view.Window.FindName("ReminderIntervalInput") == null, "全局设置彻底删除提醒控件，不再提供统一提醒");
                Check(view.Control<Button>("ResetButton").Content.ToString() == "默认位置大小" && view.Control<Button>("PositionMenuButton").HorizontalAlignment == HorizontalAlignment.Right,
                    "默认位置大小文字与最右侧三点设置入口分离，不再重合");
                view.Control<Button>("PositionMenuButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                view.Window.UpdateLayout();
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
                view.OpenPositionOverlay(); Render(view, Path.Combine(directory, "position-size-compact.png"));
                view.Control<ScrollViewer>("PositionOverlay").ScrollToEnd(); Render(view, Path.Combine(directory, "position-size-compact-bottom.png"));
                Check(view.Control<Button>("PositionSaveButton").Visibility == Visibility.Visible && view.Control<Button>("PositionSaveButton").ActualHeight >= 36 &&
                    view.Control<Button>("PositionRestoreButton").Visibility == Visibility.Visible && view.Control<Button>("PositionRestoreButton").ActualHeight >= 36,
                    "最小卡片的位置大小面板可滚动到底部，恢复默认和保存按钮完整可见");
                Check(view.Control<Border>("Card").Clip is RectangleGeometry && ((RectangleGeometry)view.Control<Border>("Card").Clip).RadiusX == 18 &&
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
                view.Control<TextBox>("TitleSettingInput").Text = "我的计划";
                Render(view, Path.Combine(directory, "title-setting.png"));
                view.Control<Button>("TitleSaveButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(viewState.Title == "我的计划" && view.Control<TextBlock>("TitleLabel").Text == "我的计划" && !view.TitleEditing,
                    "主页名称只在设置页编辑并保存，不占用顶部拖动区域");
                view.Control<TextBox>("TitleSettingInput").Text = "  ";
                view.Control<Button>("TitleSaveButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(viewState.Title == "我的计划", "设置中的空名称不会覆盖已有名称");
                view.Control<Button>("SettingsBack").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Render(view, Path.Combine(directory, "main-final.png"));
                view.Window.Show(); Pump(); view.InitializeNative(false, false, IntPtr.Zero); Pump();
                view.Input.Focus(); Pump();
                Check(view.Control<TextBlock>("InputHint").Visibility == Visibility.Collapsed,
                    "真实窗口中添加框获得焦点后占位文字隐藏，插入光标不会压住提示字");
                Render(view, Path.Combine(directory, "input-focused.png"));
                viewState.ShowMinimizeButton = true; viewState.ShowCloseButton = true; view.RefreshStatus(); view.Window.UpdateLayout();
                Border cardForButtons = view.Control<Border>("Card"); Button menuForButtons = view.Control<Button>("MenuButton");
                System.Windows.Point menuEdge = menuForButtons.TranslatePoint(new System.Windows.Point(menuForButtons.ActualWidth, 0), cardForButtons);
                Check(menuEdge.X <= cardForButtons.ActualWidth && view.Control<TextBlock>("TitleLabel").ActualWidth > 0,
                    "最小宽度同时显示锁定、最小化、关闭和设置图标时，标题与按钮均不越出卡片");
                Button minimizeButton = view.Control<Button>("MinimizeButton"), closeButton = view.Control<Button>("CloseButton");
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
                AppState alignState = new AppState { DesktopMode = false, ThemeId = "ocean", Width = 720, Height = 900 };
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
                bool previewStarted = view.BeginResizePreview(); Pump();
                Image resizePreview = view.Control<Image>("ResizePreview");
                Check(previewStarted && view.ResizePreviewActive && view.Control<Border>("Card").Visibility == Visibility.Collapsed &&
                    resizePreview.Source != null && ((BitmapSource)resizePreview.Source).IsFrozen && grip.IsVisible,
                    "缩放期间仅拉伸冻结的卡片快照，手柄保持捕获而完整控件树停止重排");
                Render(view, Path.Combine(directory, "resize-preview.png"));
                view.EndResizePreview(); Pump();
                Check(!view.ResizePreviewActive && view.Control<Border>("Card").Visibility == Visibility.Visible && resizePreview.Source == null,
                    "缩放结束恢复实时卡片并释放预览位图");
                view.Window.Width = 720; view.Window.Height = 1100; Pump();
                ScaleTransform largeTransform = view.Control<Grid>("CardContent").LayoutTransform as ScaleTransform;
                Check(view.ContentScale == 1.8 && largeTransform != null && largeTransform.ScaleX == 1.8 && largeTransform.ScaleY == 1.8,
                    "大卡片将文字、图标、按钮与间距一起放大到 180%，不只扩大空白区域");
                Render(view, Path.Combine(directory, "large.png"));
                Check(view.Control<Border>("ProgressFill").ActualWidth <= view.Control<Border>("ProgressTrack").ActualWidth + 1,
                    "自适应放大后进度条按内容宽度计算，不溢出轨道");
                view.Control<Button>("MenuButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
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
        private static void Pump()
        {
            DispatcherFrame frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(delegate { frame.Continue = false; }));
            Dispatcher.PushFrame(frame);
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
            Check(size.Right - size.Left == 1125 && size.Bottom == work.Bottom && size.Left == anchor.Left, "缩放最大尺寸支持 250% 预设并遵守屏幕边界，左上角固定");
            Native.Rect moved = TodoWindow.MoveGeometry(anchor, start, new Native.Point { X = -2000, Y = 1700 }, work);
            Check(moved.Left == work.Left && moved.Top == work.Bottom - (anchor.Bottom - anchor.Top) &&
                moved.Right - moved.Left == anchor.Right - anchor.Left && moved.Bottom - moved.Top == anchor.Bottom - anchor.Top,
                "拖动跨负坐标屏幕时按目标屏工作区限位且尺寸不变");
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
        private static bool SameRect(Native.Rect left, Native.Rect right)
        { return left.Left == right.Left && left.Top == right.Top && left.Right == right.Right && left.Bottom == right.Bottom; }
        private static void CheckNativeModes(Application app, string fixture)
        {
            // Use our own HWND as an Explorer-like parent. Do not modify real Explorer.
            Window host = new Window { Width = 1000, Height = 900, Left = 0, Top = 0, WindowStyle = WindowStyle.None, ShowInTaskbar = false };
            host.Show(); Native.TestSurface = new WindowInteropHelper(host).Handle;
            AppState state = new AppState { DesktopMode = false, X = 80, Y = 90, PositionSet = true };
            TodoItem item = state.Add("模式切换保留事项");
            Controller controller = new Controller(app, new Storage(Path.Combine(fixture, "mode-test")), state, false);
            controller.Start(); Pump();
            TodoWindow ui = controller.CurrentUi;
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
            TrayMenu exitMenu = controller.OpenTrayMenu(); Pump(); Pump();
            exitMenu.Buttons[4].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump();
            Check(!Native.IsWindow(ui.Desktop.Handle) && !controller.TrayVisible, "可见托盘菜单退出会保存数据并彻底关闭程序，不重复 Close");
            controller.Dispose(); controller.Exit();
            Native.TestSurface = IntPtr.Zero; replacementHost.Close();
        }
        private static void Render(TodoWindow view, string path)
        {
            FrameworkElement root = (FrameworkElement)view.Window.Content;
            root.Measure(new Size(view.Window.Width, view.Window.Height));
            root.Arrange(new Rect(0, 0, view.Window.Width, view.Window.Height)); root.UpdateLayout();
            RenderTargetBitmap image = new RenderTargetBitmap((int)(view.Window.Width * 1.5), (int)(view.Window.Height * 1.5), 144, 144, PixelFormats.Pbgra32);
            image.Render(root);
            PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using (FileStream stream = File.Create(path)) encoder.Save(stream);
        }
        private static void Render(Window window, string path)
        {
            FrameworkElement root = (FrameworkElement)window.Content;
            root.Measure(new Size(window.Width, 600));
            double height = Math.Max(260, root.DesiredSize.Height);
            root.Arrange(new Rect(0, 0, window.Width, height)); root.UpdateLayout();
            RenderTargetBitmap image = new RenderTargetBitmap((int)(window.Width * 1.5), (int)(height * 1.5), 144, 144, PixelFormats.Pbgra32);
            image.Render(root); PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using (FileStream stream = File.Create(path)) encoder.Save(stream);
        }
    }
}
