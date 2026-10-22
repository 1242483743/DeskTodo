using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DesktopTodo
{
    public sealed partial class TodoWindow
    {
        public readonly Window Window;
        public readonly AppState State;
        public readonly DesktopAttachment Desktop = new DesktopAttachment();
        public Action Changed, ExitRequested, HideRequested, ModeChanged, ExportRequested, ImportRequested, RebuildRequested, MiniRequested, LanAdminRequested, SecondaryPreferenceChanged;
        public Action SettingsChanged;
        public bool IsSavePending;
        public Func<bool> GetStartup;
        public Func<string, string, string> ConfigureShortcutsRequested;
        public Action<bool> SetStartup;
        public bool IsSaved = true;
        public string SaveError;
        public readonly Stack<TodoItem> Removed = new Stack<TodoItem>();
        private readonly Stack<List<TodoItem>> undoGroups = new Stack<List<TodoItem>>();
        private string undoLabel = "已移除事项";
        private readonly DispatcherTimer undoTimer;
        private readonly DispatcherTimer noticeTimer;
        private DateTime noticeExpiresAtUtc;
        private readonly DispatcherTimer statusTimer;
        private DateTime statusExpiresAtUtc;
        private string statusMessage;
        private DateTime undoExpiresAtUtc;
        public readonly TextBox Input;
        private readonly TextBox editInput;
        private TodoItem editingItem;
        private TodoItem reminderEditingItem;
        private readonly TextBox itemReminderHours, itemReminderMinutes, itemReminderDailyTime, titleEditor, titleSettingInput;
        private Action pendingConfirmation;
        private bool closingEdit, suppressEditLostFocus;
        private bool dragging;
        private readonly UiFrameQueue windowMoveFrames;
        internal bool GeometryGestureActive { get { return dragging || resizing; } }
        internal int WindowMoveFrameUpdates { get { return windowMoveFrames.FrameUpdates; } }
        private Native.Point dragStart;
        private Native.Rect dragRect, dragTarget;
        private IntPtr dragMonitor;
        private double dragScale, dragWidth, dragHeight;
        private Native.MonitorArea[] dragDisplays;
        private readonly Dictionary<IntPtr, double> dragDisplayScales = new Dictionary<IntPtr, double>();
        private bool dragMoved, resizeMoved, previewAttempted;
        private GesturePreview movePreview;
        private GestureWindowClip moveSourceClip;
        private double dragCardOpacity, dragThumbOpacity;
        private bool movePreviewHidCard;
        private bool resizing;
        private Native.Point resizeStart;
        private Native.Rect resizeRect, resizeTarget;
        private System.Drawing.Rectangle resizeWork;
        private double resizeScale;
        private string resizeDirection = "BottomRight";
        private bool suppressSettingsEvents;
        private List<Native.MonitorArea> secondaryMonitorCache;
        private string secondarySelectorSignature;
        private bool startupKnown, cachedStartup;
        private string appliedTheme;
        private Grid settingsCategoryLayer;
        private Border settingsCategorySurface;
        private ScrollViewer settingsCategoryScroll;
        private TextBlock settingsCategoryTitle;
        private readonly Dictionary<string, StackPanel> settingsCategoryContents = new Dictionary<string, StackPanel>();
        private readonly Dictionary<string, Button> settingsCategoryButtons = new Dictionary<string, Button>();
        private string currentSettingsCategory;
        private int settingsCategoryAnimationVersion;
        private const double TaskDragStartDistance = 5;
        internal static bool PastTaskDragStart(Point start, Point current)
        { return Math.Abs(current.X - start.X) >= TaskDragStartDistance || Math.Abs(current.Y - start.Y) >= TaskDragStartDistance; }
        private sealed class CachedRow { internal TodoItem Item; internal string Signature; internal Grid Row; }
        private readonly Dictionary<string, CachedRow> rowCache = new Dictionary<string, CachedRow>();
        private sealed class CachedHeading { internal string Signature; internal Grid Heading; }
        private readonly Dictionary<string, CachedHeading> headingCache = new Dictionary<string, CachedHeading>();
        private string rowAppearance = "";
        private TodoGroup editingGroup;
        private DispatcherTimer groupNameTimer;
        public bool IsReady;
        private bool forceWindowed;
        private bool attachmentFallback;
        private double contentScale = 1;
        private double displayCompensation = 1;
        private double LogicalScreenScale { get { return Scale * displayCompensation; } }
        private static readonly Brush SavedStatusBrush = CreateSavedStatusBrush();
        private static Brush CreateSavedStatusBrush()
        { SolidColorBrush brush = new SolidColorBrush(Color.FromRgb(121, 168, 137)); brush.Freeze(); return brush; }
        internal double ContentScale { get { return contentScale; } }
        internal bool IsAttachmentFallback { get { return attachmentFallback; } }
        public T Control<T>(string name) where T : class { return Window.FindName(name) as T; }

        public TodoWindow(AppState state)
        {
            State = state;
            windowMoveFrames = new UiFrameQueue(delegate(Point point)
            { ContinueWindowMove(new Native.Point { X = (int)point.X, Y = (int)point.Y }, true); });
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("DesktopTodo.MainWindow.xaml"))
            using (StreamReader reader = new StreamReader(stream))
                Window = (Window)XamlReader.Parse(reader.ReadToEnd().Replace("assembly=DeskTodo", "assembly=" + Assembly.GetExecutingAssembly().GetName().Name));
            UiInteraction.DisableHoverHints(Window);
            UiDrag.ReorderZone(Control<ScrollViewer>("PendingScroll"), "DeskTodo.Group");
            UiDrag.ReorderZone(Control<ScrollViewer>("PendingScroll"), "DeskTodo.Task");
            UiDrag.ReorderZone(Control<ScrollViewer>("GroupScroll"), "DeskTodo.Group");
            UiDrag.ReorderZone(Control<ScrollViewer>("GroupScroll"), "DeskTodo.ManagedTask");
            UiGlyphs.CenterLabelInk(Control<TextBlock>("CompletedHeader"));
            Control<Image>("HeaderIcon").Source = UiAssets.InternalIcon;
            Control<Image>("LanguageWelcomeIcon").Source = UiAssets.InternalIcon;
            Control<Button>("ConfirmCloseButton").Content = UiGlyphs.Close();
            Control<Button>("GroupNameClose").Content = UiGlyphs.Close();
            Control<Button>("ClearCompletedButton").Content = UiGlyphs.Trash();
            // WPF can create focus adorners inside control templates that are not known
            // until runtime. Clear them before keyboard focus is applied, including when
            // Alt turns keyboard cues on.
            Window.AddHandler(Keyboard.PreviewGotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(delegate(object sender, KeyboardFocusChangedEventArgs e)
            {
                Control control = e.NewFocus as Control;
                if (control != null) control.FocusVisualStyle = null;
            }), true);
            Input = Control<TextBox>("AddInput");
            Control<Button>("SaveHotkeysButton").Click += delegate { SaveShortcuts(); };
            editInput = Control<TextBox>("EditInput");
            titleEditor = Control<TextBox>("TitleEditor");
            titleSettingInput = Control<TextBox>("TitleSettingInput");
            itemReminderHours = Control<TextBox>("ItemReminderHours");
            itemReminderMinutes = Control<TextBox>("ItemReminderMinutes");
            itemReminderDailyTime = Control<TextBox>("ItemReminderDailyTime");
            undoTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            undoTimer.Tick += delegate { ExpireUndo(); };
            noticeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            noticeTimer.Tick += delegate { UpdateNoticeCountdown(); };
            statusTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            statusTimer.Tick += delegate { UpdateSettingsMessage(); };
            Window.Width = state.Width; Window.Height = state.Height;
            UpdateContentScale();
            ApplyTheme();
            Control<Button>("AddButton").Click += delegate { AddFromInput(); };
            Input.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Enter)
                {
                    if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
                    {
                        int caret = Input.CaretIndex;
                        Input.Text = Input.Text.Insert(caret, Environment.NewLine);
                        Input.CaretIndex = caret + Environment.NewLine.Length;
                    }
                    else AddFromInput();
                    e.Handled = true;
                }
                if (e.Key == Key.Escape) { Input.Clear(); e.Handled = true; }
            };
            Window.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (dragging && e.Key == Key.Escape) { FinishMove(Control<FrameworkElement>("Card"), false); e.Handled = true; return; }
                if (UiDrag.IsActive && e.Key == Key.Escape) { UiDrag.Cancel(Window); e.Handled = true; return; }
                if (OverlayOpen && e.Key == Key.Escape) { CloseOverlay(); e.Handled = true; return; }
                if (SettingsCategoryOpen && e.Key == Key.Escape) { CloseSettingsCategory(); e.Handled = true; return; }
                if (TitleEditing && e.Key == Key.Escape) { EndTitleEdit(false); e.Handled = true; return; }
                if (ReminderOpen && e.Key == Key.Escape) { CloseReminder(); e.Handled = true; return; }
                if (ReminderOpen && e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control) { SaveItemReminder(); e.Handled = true; return; }
                if (EditOpen && e.Key == Key.Escape) { CloseEdit(); e.Handled = true; return; }
                if (EditOpen && e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control) { SaveEdit(); e.Handled = true; return; }
                if (e.Key == Key.Escape && GroupOpen) { CloseGroups(); e.Handled = true; return; }
                if (e.Key == Key.Escape && SettingsOpen) { CloseSettings(); e.Handled = true; return; }
                if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control && Removed.Count > 0 &&
                    (!(Keyboard.FocusedElement is TextBox) || (Keyboard.FocusedElement == Input && Input.Text.Length == 0))) { Undo(); e.Handled = true; }
            };
            Control<Button>("UndoButton").Click += delegate { Undo(); };
            Control<Button>("ClearCompletedButton").Click += delegate
            {
                int count = VisibleCompleted().Count();
                if (count > 0) ShowConfirmation(L("清空已完成", "Clear completed"),
                    L("将移除当前范围内的 " + count + " 条已完成事项", "Remove all " + count + " completed tasks in the current scope") +
                    (count > completedVisibleLimit ? L("（包括尚未加载的记录）", " (including unloaded records)") : "") +
                    L("。可点击“撤销”，或在“最近删除”中恢复。", ". Undo or restore from Recently deleted."), L("清空", "Clear"), ClearCompleted);
            };
            Control<Button>("LockButton").Click += delegate { State.Locked = !State.Locked; NotifyChanged(); RefreshStatus(); };
            Control<Button>("CompactButton").Click += delegate { if (MiniRequested != null) MiniRequested(); };
            Control<Button>("MinimizeButton").Click += delegate { if (HideRequested != null) HideRequested(); };
            Control<Button>("CloseButton").Click += delegate
            { ShowConfirmation("关闭 DeskTodo", "将保存当前修改并退出软件。提醒也会停止，确定要关闭吗？", "关闭", delegate { if (ExitRequested != null) ExitRequested(); }); };
            Control<Button>("MenuButton").Click += delegate { ShowMenu(); };
            Control<Button>("SettingsBack").Click += delegate { CloseSettings(); };
            Control<CheckBox>("DesktopToggle").Click += delegate { State.DesktopMode = Control<CheckBox>("DesktopToggle").IsChecked == true; NotifyChanged(); if (ModeChanged != null) ModeChanged(); };
            Control<CheckBox>("LockToggle").Click += delegate { State.Locked = Control<CheckBox>("LockToggle").IsChecked == true; NotifySettingsChanged(); RefreshStatus(); RefreshSettings(); };
            Control<CheckBox>("ShowTrayToggle").Click += delegate
            { State.HideTray = Control<CheckBox>("ShowTrayToggle").IsChecked != true; NotifySettingsChanged(); RefreshSettings(); };
            Control<CheckBox>("ShowMinimizeToggle").Click += delegate
            { State.ShowMinimizeButton = Control<CheckBox>("ShowMinimizeToggle").IsChecked == true; NotifySettingsChanged(); RefreshStatus(); RefreshSettings(); };
            Control<CheckBox>("ShowCloseToggle").Click += delegate
            { State.ShowCloseButton = Control<CheckBox>("ShowCloseToggle").IsChecked == true; NotifySettingsChanged(); RefreshStatus(); RefreshSettings(); };
            Control<CheckBox>("ShowCompactToggle").Click += delegate
            { State.ShowCompactButton = Control<CheckBox>("ShowCompactToggle").IsChecked == true; NotifySettingsChanged(); RefreshStatus(); RefreshSettings(); };
            Control<Button>("TitleSaveButton").Click += delegate { SaveTitleFromSettings(); };
            titleSettingInput.TextChanged += delegate { Control<Button>("TitleSaveButton").IsEnabled = !String.IsNullOrWhiteSpace(titleSettingInput.Text); };
            titleSettingInput.KeyDown += delegate(object sender, KeyEventArgs e)
            { if (e.Key == Key.Enter) { SaveTitleFromSettings(); e.Handled = true; } };
            Control<CheckBox>("DarkToggle").Click += delegate { State.DarkMode = Control<CheckBox>("DarkToggle").IsChecked == true; ApplyTheme(); NotifySettingsChanged(); RefreshSettings(); };
            foreach (ThemePalette palette in ThemeCatalog.All)
            {
                ThemePalette selected = palette;
                RadioButton themeButton = Control<RadioButton>("Theme" + Char.ToUpperInvariant(palette.Id[0]) + palette.Id.Substring(1));
                themeButton.Tag = new SolidColorBrush((Color)ColorConverter.ConvertFromString(palette.Swatch));
                themeButton.Click += delegate
                { State.ThemeId = selected.Id; ApplyTheme(); NotifySettingsChanged(); RefreshSettings(); };
            }
            Control<CheckBox>("StartupToggle").Click += delegate
            {
                try { if (SetStartup != null) SetStartup(Control<CheckBox>("StartupToggle").IsChecked == true); RefreshSettings(true); SettingsMessage("自启设置已更新"); }
                catch (Exception e) { ShowNotice("自启设置失败", e.Message); RefreshSettings(true); }
            };
            Control<CheckBox>("SecondaryStartupToggle").Click += delegate
            {
                bool enabled = Control<CheckBox>("SecondaryStartupToggle").IsChecked == true;
                try
                {
                    if (enabled)
                    {
                        RememberGeometry();
                        if (SetStartup != null && (GetStartup == null || !GetStartup())) SetStartup(true);
                    }
                    State.StartOnSecondaryOnly = enabled;
                    NotifySettingsChanged(); if (SecondaryPreferenceChanged != null) SecondaryPreferenceChanged(); RefreshSettings(true);
                    SettingsMessage(enabled ? L("已开启副屏自启", "Secondary-only startup enabled") : L("已关闭副屏限制", "Secondary-only restriction disabled"));
                }
                catch (Exception e) { ShowNotice(L("自启设置失败", "Startup setting failed"), e.Message); RefreshSettings(); }
            };
            Control<ComboBox>("SecondaryDisplaySelect").SelectionChanged += delegate
            {
                if (suppressSettingsEvents) return;
                ComboBoxItem choice = Control<ComboBox>("SecondaryDisplaySelect").SelectedItem as ComboBoxItem;
                string name = choice == null ? null : choice.Tag as string;
                if (String.IsNullOrEmpty(name) || String.Equals(name, State.SecondaryDeviceName, StringComparison.OrdinalIgnoreCase)) return;
                State.SecondaryDeviceName = name; State.SecondaryPositionSet = false;
                RememberGeometry(); NotifyChanged(); if (SecondaryPreferenceChanged != null) SecondaryPreferenceChanged(); RefreshSettings();
            };
            Control<Button>("ResetButton").Click += delegate
            {
                State.Locked = false;
                ApplyPositionPreset(State.DefaultCorner, State.DefaultScalePercent);
                Refresh(); RefreshSettings();
            };
            Control<Button>("PositionMenuButton").Click += delegate { OpenPositionOverlay(); };
            Control<Button>("ExportButton").Click += delegate { if (ExportRequested != null) ExportRequested(); };
            Control<Button>("ImportButton").Click += delegate { if (ImportRequested != null) ImportRequested(); };
            Control<Button>("HideButton").Click += delegate { CloseSettings(); if (HideRequested != null) HideRequested(); };
            Control<Button>("ExitButton").Click += delegate { if (ExitRequested != null) ExitRequested(); };
            Control<Button>("EditCancelButton").Click += delegate { CloseEdit(); };
            Control<Button>("EditSaveButton").Click += delegate { SaveEdit(); };
            editInput.LostKeyboardFocus += delegate
            {
                if (suppressEditLostFocus) { suppressEditLostFocus = false; return; }
                if (EditOpen && !closingEdit && !String.IsNullOrWhiteSpace(editInput.Text)) SaveEdit();
            };
            Control<Button>("ReminderCancelButton").Click += delegate { CloseReminder(); };
            Control<Button>("ReminderSaveButton").Click += delegate { SaveItemReminder(); };
            Control<Button>("ReminderClearButton").Click += delegate { ClearItemReminder(); };
            Control<CheckBox>("ItemIntervalToggle").Click += delegate { RefreshItemReminderVisibility(); };
            Control<CheckBox>("ItemDailyToggle").Click += delegate { RefreshItemReminderVisibility(); };
            Control<Button>("HoursUpButton").Click += delegate { StepNumber(itemReminderHours, 1, 0, 168); };
            Control<Button>("HoursDownButton").Click += delegate { StepNumber(itemReminderHours, -1, 0, 168); };
            Control<Button>("MinutesUpButton").Click += delegate { StepNumber(itemReminderMinutes, 1, 0, 59); };
            Control<Button>("MinutesDownButton").Click += delegate { StepNumber(itemReminderMinutes, -1, 0, 59); };
            Control<Button>("DailyTimeUpButton").Click += delegate { StepDailyTime(1); };
            Control<Button>("DailyTimeDownButton").Click += delegate { StepDailyTime(-1); };
            Control<Button>("ConfirmCloseButton").Click += delegate { CloseOverlay(); };
            Control<Button>("ConfirmCancelButton").Click += delegate { CloseOverlay(); };
            Control<Button>("ConfirmAcceptButton").Click += delegate { Action action = pendingConfirmation; CloseOverlay(); if (action != null) action(); };
            Control<Button>("PositionCloseButton").Click += delegate { CloseOverlay(); };
            Control<Button>("PositionRestoreButton").Click += delegate { SelectPositionOptions("TopRight", 100); };
            Control<Button>("PositionSaveButton").Click += delegate { SavePositionOptions(); };
            foreach (string cornerName in new[] { "CornerTopLeft", "CornerTopRight", "CornerBottomLeft", "CornerBottomRight" })
                Control<RadioButton>(cornerName).Checked += delegate { RefreshPositionMarkers(); };
            Control<CheckBox>("ShowTimesToggle").Click += delegate
            { State.HideItemTimes = Control<CheckBox>("ShowTimesToggle").IsChecked != true; NotifySettingsChanged(); RefreshTaskTimes(); RefreshSettings(); };
            Control<CheckBox>("ShowDateToggle").Click += delegate
            { State.ShowItemDates = Control<CheckBox>("ShowDateToggle").IsChecked == true; NotifySettingsChanged(); RefreshTaskTimes(); RefreshSettings(); };
            Control<CheckBox>("DisableGroupsToggle").Click += delegate
            { State.DisableGroups = Control<CheckBox>("DisableGroupsToggle").IsChecked == true; NotifySettingsChanged(); Refresh(); RefreshSettings(); };
            Control<Button>("SortButton").Click += delegate { ToggleSortDirection(); };
            Control<Button>("GroupButton").Click += delegate { ShowGroupMenu(); };
            Control<Button>("AddGroupButton").Click += delegate { ShowGroupMenu(); };
            Control<Button>("GroupBack").Click += delegate { CloseGroups(); };
            Control<Button>("GroupCreateButton").Click += delegate { CreateGroup(); };
            Control<Button>("GroupNameClose").Click += delegate { CloseOverlay(); };
            Control<Button>("GroupNameCancel").Click += delegate { CloseOverlay(); };
            Control<Button>("GroupNameAccept").Click += delegate { AcceptGroupName(); };
            Control<TextBox>("GroupNameInput").TextChanged += delegate
            {
                if (editingGroup == null) return;
                if (groupNameTimer == null)
                {
                    groupNameTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                    groupNameTimer.Tick += delegate { groupNameTimer.Stop(); SaveGroupNameDraft(); };
                }
                groupNameTimer.Stop(); groupNameTimer.Start();
            };
            Control<TextBox>("GroupNameInput").LostKeyboardFocus += delegate { if (editingGroup != null) SaveGroupNameDraft(); };
            Control<TextBox>("GroupNameInput").KeyDown += delegate(object sender, KeyEventArgs e)
            { if (e.Key == Key.Enter) { AcceptGroupName(); e.Handled = true; } };
            Control<Grid>("OverlayLayer").MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            { if (e.OriginalSource == sender && Control<FrameworkElement>("GroupNameOverlay").Visibility == Visibility.Visible) CloseOverlay(); };
            Control<TextBox>("GroupCreateInput").KeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { CreateGroup(); e.Handled = true; } };
            Control<Button>("ChooseChineseButton").Click += delegate { SelectLanguage("zh-CN", true); };
            Control<Button>("ChooseEnglishButton").Click += delegate { SelectLanguage("en-US", true); };
            Control<ComboBox>("LanguageSelect").SelectionChanged += delegate
            {
                if (suppressSettingsEvents) return;
                ComboBoxItem choice = Control<ComboBox>("LanguageSelect").SelectedItem as ComboBoxItem;
                if (choice != null) SelectLanguage(choice.Tag as string, false);
            };
            Control<CheckBox>("MiniAlwaysOnTopToggle").Click += delegate
            { State.MiniAlwaysOnTop = Control<CheckBox>("MiniAlwaysOnTopToggle").IsChecked == true; NotifySettingsChanged(); RefreshSettings(); };
            Control<CheckBox>("LanSharingToggle").Click += delegate
            { State.LanSharingEnabled = Control<CheckBox>("LanSharingToggle").IsChecked == true; NotifyChanged(); RefreshSettings(); };
            Control<TextBox>("LanDisplayNameInput").LostKeyboardFocus += delegate { SaveLanDisplayName(); };
            Control<TextBox>("LanDisplayNameInput").KeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { SaveLanDisplayName(); e.Handled = true; } };
            Control<Button>("LanRegenerateButton").Click += delegate
            {
                ShowConfirmation("更换管理口令", "更换后，已连接的管理员需要输入新口令才能继续管理。", "更换", delegate
                {
                    State.LanAccessCode = Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant(); NotifyChanged(); RefreshSettings();
                });
            };
            Control<Button>("LanAdminButton").Click += delegate { if (LanAdminRequested != null) LanAdminRequested(); };
            Control<Slider>("MiniOpacitySlider").ValueChanged += delegate
            {
                if (suppressSettingsEvents) return;
                State.MiniIconOpacityPercent = (int)Math.Round(Control<Slider>("MiniOpacitySlider").Value);
                NotifySettingsChanged(); Control<TextBlock>("MiniOpacityValue").Text = State.MiniIconOpacityPercent + "%";
            };
            Control<Slider>("WindowOpacitySlider").ValueChanged += delegate
            {
                if (suppressSettingsEvents) return;
                State.WindowOpacityPercent = (int)Math.Round(Control<Slider>("WindowOpacitySlider").Value);
                ApplyCardOpacity(); NotifySettingsChanged(); Control<TextBlock>("WindowOpacityValue").Text = State.WindowOpacityPercent + "%";
            };
            titleEditor.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Enter) { EndTitleEdit(true); e.Handled = true; }
                else if (e.Key == Key.Escape) { EndTitleEdit(false); e.Handled = true; }
            };
            titleEditor.LostKeyboardFocus += delegate { if (TitleEditing) EndTitleEdit(true); };
            editInput.TextChanged += delegate
            {
                Control<TextBlock>("EditCount").Text = editInput.Text.Length + " / 500";
                Control<Button>("EditSaveButton").IsEnabled = !String.IsNullOrWhiteSpace(editInput.Text);
            };
            FrameworkElement dragSurface = Control<FrameworkElement>("Card");
            FrameworkElement addBar = Control<FrameworkElement>("AddBar");
            dragSurface.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (State.Locked || IsButtonSource(e.OriginalSource as DependencyObject) || !IsReady) return;
                FrameworkElement inputBar = SearchOpen ? Control<FrameworkElement>("SearchBar") : addBar;
                Point addTop = inputBar.TranslatePoint(new Point(0, 0), dragSurface);
                if (!IsTopDragPoint(e.GetPosition(dragSurface).Y, addTop.Y)) return;
                Native.Point cursor; Native.GetPhysicalCursorPos(out cursor);
                BeginWindowMove(cursor); dragSurface.CaptureMouse();
                e.Handled = true;
            };
            dragSurface.MouseMove += delegate(object sender, MouseEventArgs e)
            {
                // Ignore hover/synthetic capture messages and queued motion after
                // button release; only a held-left-button gesture moves a window.
                if (!dragging || e.LeftButton != MouseButtonState.Pressed) return;
                Native.Point current; Native.GetPhysicalCursorPos(out current);
                QueueWindowMove(current);
            };
            dragSurface.MouseLeftButtonUp += delegate
            {
                if (!dragging) return;
                FinishMove(dragSurface, true);
            };
            dragSurface.LostMouseCapture += delegate { if (dragging) FinishMove(dragSurface, true); };
            foreach (string resizeName in new[] { "ResizeTop", "ResizeBottom", "ResizeLeft", "ResizeRight", "ResizeTopLeft", "ResizeTopRight", "ResizeBottomLeft", "ResizeThumb" })
            {
                Thumb handle = Control<Thumb>(resizeName);
                handle.DragStarted += delegate(object sender, DragStartedEventArgs e) { BeginLiveResize((string)((Thumb)sender).Tag); };
                handle.DragDelta += delegate(object sender, DragDeltaEventArgs e) { ContinueLiveResize(); };
                handle.DragCompleted += delegate(object sender, DragCompletedEventArgs e) { FinishResize(!e.Canceled); };
            }
            Window.SizeChanged += delegate { UpdateContentScale(); UpdateProgress(); UpdateCardClip(); };
            Control<Grid>("CardContent").SizeChanged += delegate { UpdateCardClip(); };
            Control<Border>("ProgressTrack").SizeChanged += delegate { UpdateProgress(); };
            Control<Grid>("TaskArea").SizeChanged += delegate { SizeCompletedArea(); };
            Control<ScrollViewer>("PendingScroll").SizeChanged += delegate { UpdateEmptyPlaceholder(); };
            Control<Expander>("CompletedSection").Expanded += delegate { SizeCompletedArea(); RefreshCompletedRows(); };
            Control<Expander>("CompletedSection").Collapsed += delegate { completedVisibleLimit = CompletedPageSize; Control<StackPanel>("CompletedList").Children.Clear(); PruneRowCache(); };
            Window.Closed += delegate { if (editingGroup != null) SaveGroupNameDraft(false); if (groupNameTimer != null) groupNameTimer.Stop(); IsReady = false; dragging = false; windowMoveFrames.Cancel(); undoTimer.Stop(); noticeTimer.Stop(); statusTimer.Stop(); UiDrag.Cancel(Window); DisposeMovePreview(); };
            Window.Deactivated += delegate { UiDrag.Cancel(Window); };
            Window.PreviewMouseDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (Desktop.IsAttached) Native.SetFocus(Desktop.Handle);
                if (!EditOpen) return;
                DependencyObject source = e.OriginalSource as DependencyObject;
                if (IsInside(source, editInput)) return;
                if (IsInside(source, Control<Button>("EditCancelButton"))) { suppressEditLostFocus = true; return; }
                if (IsInside(source, Control<Button>("EditSaveButton"))) return;
                if (!String.IsNullOrWhiteSpace(editInput.Text)) { SaveEdit(); e.Handled = true; }
            };
            Version version = Assembly.GetExecutingAssembly().GetName().Version;
            Control<TextBlock>("SettingsVersion").Text = "v" + version.Major + "." + version.Minor;
            BuildSettingsCategories();
            BuildProductivityFeatures();
            UpdateCardClip();
            ApplyLanguage();
            RefreshInputHint(); RefreshTitle(); Refresh();
            if (String.IsNullOrWhiteSpace(State.Language)) ShowLanguageWelcome();
        }

        internal void RefreshInputHint()
        {
            Control<TextBlock>("InputHint").ClearValue(UIElement.VisibilityProperty);
        }
        private bool IsEnglish { get { return String.Equals(State.Language, "en-US", StringComparison.OrdinalIgnoreCase); } }
        private string L(string chinese, string english) { return IsEnglish ? english : chinese; }

        private void SetToggleText(string name, string chinese, string english, string chineseSubtitle, string englishSubtitle)
        {
            CheckBox toggle = Control<CheckBox>(name);
            if (toggle == null) return;
            StackPanel content = toggle.Content as StackPanel;
            if (content == null)
            {
                toggle.Content = L(chinese, english);
                return;
            }
            if (content.Children.Count > 0) ((TextBlock)content.Children[0]).Text = L(chinese, english);
            if (content.Children.Count > 1) ((TextBlock)content.Children[1]).Text = L(chineseSubtitle, englishSubtitle);
        }

        private void ApplyLanguage()
        {
            Control<TextBlock>("InputHint").Text = L("添加一件要做的事…", "Add a task…");
            Control<TextBlock>("PendingSectionLabel").Text = L("未完成", "Pending");
            Control<Button>("SortButton").Content = L("排序", "Sort");
            Control<TextBlock>("SettingsTitle").Text = L("设置", "Settings");
            Control<TextBlock>("SettingsSubtitle").Text = L("让这张卡片更适合你", "Make this card work for you");
            Control<TextBlock>("HotkeyLabel").Text = L("快捷键（留空关闭）", "Keyboard shortcuts (blank to disable)");
            Control<TextBlock>("ShowHotkeyLabel").Text = L("显示主窗口", "Show main window");
            Control<TextBlock>("MiniHotkeyLabel").Text = L("悬浮图标 / 恢复", "Floating icon / restore");
            Control<Button>("SaveHotkeysButton").Content = L("保存快捷键", "Save shortcuts");
            Control<TextBlock>("HotkeyStatus").Text = L("默认 Ctrl+Alt+D 显示，Ctrl+Alt+M 悬浮/恢复", "Defaults: Ctrl+Alt+D shows; Ctrl+Alt+M floats/restores");
            Control<Button>("SettingsBack").Content = L("返回", "Back");
            Control<TextBlock>("BasicSectionLabel").Text = L("基础功能", "Basics");
            Control<TextBlock>("CustomSectionLabel").Text = L("自定义界面", "Appearance");
            SetToggleText("DesktopToggle", "嵌入桌面", "Pin to desktop", "随桌面显示，不挡住其他软件", "Stay on the desktop behind other apps");
            Control<CheckBox>("LockToggle").Content = L("锁定位置和大小", "Lock position and size");
            Control<CheckBox>("StartupToggle").Content = L("开机自启", "Start with Windows");
            SetToggleText("SecondaryStartupToggle", "仅连接副屏时自启", "Start only with secondary display",
                "自动在该副屏恢复上次位置；手动启动不受限", "Restore its last position; manual launch is always allowed");
            Control<TextBlock>("SecondaryDisplayLabel").Text = L("目标副屏", "Target secondary display");
            Control<CheckBox>("ShowTrayToggle").Content = L("显示托盘图标", "Show tray icon");
            Control<CheckBox>("ShowMinimizeToggle").Content = L("显示最小化按钮", "Show minimize button");
            Control<CheckBox>("ShowCloseToggle").Content = L("显示关闭按钮", "Show close button");
            Control<CheckBox>("ShowCompactToggle").Content = L("显示悬浮图标", "Show mini icon button");
            SetToggleText("ShowTimesToggle", "显示事项时间", "Show task times", "未完成显示添加时间，已完成显示完成时间", "Show created or completed time");
            SetToggleText("ShowDateToggle", "显示月日", "Show month and day", "在事项时间前同时显示月/日", "Include month/day before the time");
            SetToggleText("DisableGroupsToggle", "关闭分组功能", "Turn off groups", "隐藏分组入口与标题；已有分组不会删除", "Hide group controls and headings; existing groups are kept");
            Control<CheckBox>("MiniAlwaysOnTopToggle").Content = L("悬浮图标始终置顶", "Keep mini icon on top");
            Control<TextBlock>("MiniOpacityLabel").Text = L("悬浮图标透明度", "Mini icon opacity");
            Control<TextBlock>("LanguageLabel").Text = L("界面语言", "Language");
            Control<TextBlock>("SoftwareNameLabel").Text = L("软件名称", "App name");
            Control<TextBlock>("ThemeLabel").Text = L("主题颜色", "Theme color");
            Control<CheckBox>("DarkToggle").Content = L("深色外观", "Dark appearance");
            Control<TextBlock>("WindowOpacityLabel").Text = L("界面透明度", "Interface opacity");
            Control<TextBlock>("LanSectionLabel").Text = L("局域网管理", "LAN management");
            SetToggleText("LanSharingToggle", "允许局域网管理", "Allow LAN management", "仅同一局域网、持有管理口令的管理员可访问", "Only administrators on this LAN with the access code can connect");
            Control<TextBlock>("LanNameLabel").Text = L("局域网显示名称", "LAN display name");
            Control<TextBlock>("LanCodeLabel").Text = L("管理口令（只发给可信任的管理员）", "Access code (share only with trusted administrators)");
            Control<Button>("LanRegenerateButton").Content = L("更换口令", "New code");
            Control<Button>("LanAdminButton").Content = L("打开局域网管理员面板", "Open LAN administrator panel");
            Control<TextBlock>("ManagementLabel").Text = L("管理", "Manage");
            Control<Button>("TitleSaveButton").Content = L("保存名称", "Save name");
            Control<Button>("ResetButton").Content = L("默认位置大小", "Default position and size");
            Control<Button>("HideButton").Content = L("隐藏到托盘", "Hide to tray");
            Control<Button>("ExportButton").Content = L("导出备份", "Export backup");
            Control<Button>("ImportButton").Content = L("导入备份", "Import backup");
            Control<Button>("ExitButton").Content = L("退出软件", "Exit DeskTodo");
            Control<TextBlock>("SettingsStatus").Text = State.LanSharingEnabled ? L("设置自动保存 · 已开启局域网共享", "Settings save automatically · LAN sharing enabled") : L("设置自动保存 · 数据只在本机", "Settings save automatically · local data only");
            Control<TextBlock>("EditTitle").Text = L("编辑事项", "Edit task");
            Control<TextBlock>("EditSubtitle").Text = L("修改内容后保存，最多 500 字", "Edit and save, up to 500 characters");
            Control<TextBlock>("EditHelp").Text = L("点击其他位置保存 · Esc 取消 · Ctrl+Enter 保存", "Click outside to save · Esc cancels · Ctrl+Enter saves");
            Control<Button>("EditCancelButton").Content = L("取消", "Cancel");
            Control<Button>("EditSaveButton").Content = L("保存", "Save");
            Control<TextBlock>("ReminderTitle").Text = L("事项提醒", "Task reminder");
            Control<TextBlock>("ReminderSubtitle").Text = L("只提醒这一条未完成事项", "Remind only for this pending task");
            Control<TextBlock>("PositionTitle").Text = L("默认位置与大小", "Default position and size");
            Control<TextBlock>("PositionLabel").Text = L("默认位置", "Default position");
            Control<TextBlock>("PositionScaleLabel").Text = L("默认比例", "Default scale");
            Control<Button>("PositionRestoreButton").Content = L("恢复默认", "Restore defaults");
            Control<Button>("PositionSaveButton").Content = L("保存", "Save");
            Control<TextBlock>("GroupTitle").Text = L("待办分组", "Task groups");
            Control<TextBlock>("GroupSubtitle").Text = L("拖动事项到其他组；拖动右侧把手调整分组顺序", "Drag tasks between groups; use handles to reorder groups");
            Control<Button>("GroupBack").Content = L("返回", "Back");
            Control<Button>("GroupCreateButton").Content = L("新建分组", "Create group");
            Control<TextBlock>("GroupStatus").Text = L("事项删除可撤销；删除分组不会删除其中的待办", "Task deletion is undoable; removing a group keeps its tasks");
            string[] names = { "forest", "ocean", "coral", "sun", "mint", "sky" };
            string[] english = { "Forest", "Ocean", "Coral", "Sun", "Mint", "Sky" };
            for (int i = 0; i < names.Length; i++)
            {
                RadioButton theme = Control<RadioButton>("Theme" + Char.ToUpperInvariant(names[i][0]) + names[i].Substring(1));
                if (theme != null) theme.Content = IsEnglish ? english[i] : ThemeCatalog.Get(names[i]).Name;
            }
            UpdateSettingsCategoryLabels();
            RefreshStatus();
        }

        private void ShowLanguageWelcome()
        {
            Control<FrameworkElement>("MainPanel").Visibility = Visibility.Collapsed;
            Control<FrameworkElement>("SettingsPanel").Visibility = Visibility.Collapsed;
            Control<FrameworkElement>("LanguageWelcomePanel").Visibility = Visibility.Visible;
            AnimateIn(Control<FrameworkElement>("LanguageWelcomePanel"));
        }

        private void SelectLanguage(string language, bool firstRun)
        {
            if (language != "en-US") language = "zh-CN";
            State.Language = language;
            Control<FrameworkElement>("LanguageWelcomePanel").Visibility = Visibility.Collapsed;
            if (firstRun) Control<FrameworkElement>("MainPanel").Visibility = Visibility.Visible;
            ApplyLanguage(); NotifyChanged(); Refresh(); RefreshSettings();
            if (firstRun) AnimateIn(Control<FrameworkElement>("MainPanel"));
        }

        private void AnimateIn(FrameworkElement element)
        {
            if (element == null) return;
            if (!Window.IsVisible || !SystemParameters.ClientAreaAnimation)
            {
                UiAnimation.Release(element);
                element.BeginAnimation(UIElement.OpacityProperty, null);
                element.Opacity = 1;
                element.RenderTransform = null;
                return;
            }
            UiAnimation.CacheFor(element, 230);
            element.Opacity = 0;
            TranslateTransform translate = new TranslateTransform(0, 8);
            element.RenderTransform = translate;
            element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(190)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
        private void SizeCompletedArea()
        {
            Grid area = Control<Grid>("TaskArea"); ScrollViewer history = Control<ScrollViewer>("CompletedScroll");
            if (area == null || history == null) return;
            // Keep the collapsed header anchored above the footer. Expanded history
            // gets its own bounded scroll region instead of displacing pending work.
            // Reserve the measured tool/search area, the completed header, and
            // one pending row. A hard-coded reserve overflowed small search cards.
            Expander completed = Control<Expander>("CompletedSection");
            ToggleButton header = completed.Template == null ? null : completed.Template.FindName("HeaderToggle", completed) as ToggleButton;
            double headerHeight = header != null && header.ActualHeight > 0 ? header.ActualHeight : 48;
            double reserved = area.RowDefinitions[0].ActualHeight + headerHeight + Control<Grid>("CompletedDock").Margin.Top + 40;
            history.MaxHeight = Math.Max(0, Math.Min(area.ActualHeight * .42, area.ActualHeight - reserved));
            UpdateEmptyPlaceholder();
        }
        private void UpdateEmptyPlaceholder()
        {
            StackPanel empty = Control<StackPanel>("EmptyPanel"); ScrollViewer scroll = Control<ScrollViewer>("PendingScroll");
            if (empty == null || scroll == null || scroll.ActualHeight <= 0) return;
            bool compact = scroll.ActualHeight < 180, icon = scroll.ActualHeight >= 165;
            empty.Margin = compact ? new Thickness(0, 8, 0, 8) : new Thickness(0, 32, 0, 30);
            empty.Children[0].Visibility = icon ? Visibility.Visible : Visibility.Collapsed;
            Control<TextBlock>("EmptyTitle").Margin = icon ? new Thickness(0, 15, 0, 6) : new Thickness(0, 0, 0, 6);
            Control<TextBlock>("EmptySubtitle").Visibility = scroll.ActualHeight >= 70 ? Visibility.Visible : Visibility.Collapsed;
        }
        internal static bool ShouldShowInputHint(string text, bool focused) { return String.IsNullOrEmpty(text) && !focused; }
        internal static bool IsTopDragPoint(double pointerY, double inputTopY) { return pointerY >= 0 && pointerY < inputTopY; }

        private void UpdateCardClip()
        {
            Border card = Control<Border>("Card");
            if (card == null) return;
            Grid shadow = Control<Grid>("CardShadow");
            if (shadow != null)
            {
                double shadowWidth = shadow.ActualWidth > 0 ? shadow.ActualWidth : Window.Width;
                double shadowHeight = shadow.ActualHeight > 0 ? shadow.ActualHeight : Window.Height;
                Thickness gutter = card.Margin;
                CombinedGeometry outside = new CombinedGeometry(GeometryCombineMode.Exclude,
                    new RectangleGeometry(new Rect(0, 0, shadowWidth, shadowHeight)),
                    new RectangleGeometry(new Rect(gutter.Left, gutter.Top,
                        Math.Max(0, shadowWidth - gutter.Left - gutter.Right), Math.Max(0, shadowHeight - gutter.Top - gutter.Bottom)), 18, 18));
                outside.Freeze(); shadow.Clip = outside;
            }
            // Clipping the painted Border at its own edge cuts off the partially
            // transparent antialiasing pixels. Clip only its inset contents.
            card.Clip = null;
            Grid content = Control<Grid>("CardContent");
            if (content == null) return;
            double width = content.ActualWidth > 0 ? content.ActualWidth : Math.Max(0, Window.Width - 4);
            double height = content.ActualHeight > 0 ? content.ActualHeight : Math.Max(0, Window.Height - 4);
            // The painted card is not scaled; keep its content clip at the same
            // visual radius even when the inner UI uses a LayoutTransform.
            content.Clip = new RectangleGeometry(new Rect(0, 0, width, height), 17 / contentScale, 17 / contentScale);
            UpdateSettingsCategoryClip();
        }
        internal static Native.Rect ResizeGeometry(Native.Rect anchor, Native.Point start, Native.Point cursor, double scale, System.Drawing.Rectangle work)
        {
            int maxWidth = Math.Min((int)Math.Round(900 * scale), work.Right - anchor.Left);
            int maxHeight = Math.Min((int)Math.Round(1300 * scale), work.Bottom - anchor.Top);
            int minWidth = Math.Min((int)Math.Round(320 * scale), maxWidth);
            int minHeight = Math.Min((int)Math.Round(360 * scale), maxHeight);
            int width = Math.Max(minWidth, Math.Min(maxWidth, anchor.Right - anchor.Left + cursor.X - start.X));
            int height = Math.Max(minHeight, Math.Min(maxHeight, anchor.Bottom - anchor.Top + cursor.Y - start.Y));
            width = Math.Min(width, Math.Max(minWidth, (int)Math.Round(height * 0.8)));
            return new Native.Rect { Left = anchor.Left, Top = anchor.Top, Right = anchor.Left + width, Bottom = anchor.Top + height };
        }
        internal static Native.Rect ResizeGeometry(Native.Rect anchor, Native.Point start, Native.Point cursor, double scale, System.Drawing.Rectangle work, string direction)
        {
            int dx = cursor.X - start.X, dy = cursor.Y - start.Y;
            int minWidth = Math.Min((int)Math.Round(320 * scale), work.Width);
            int minHeight = Math.Min((int)Math.Round(360 * scale), work.Height);
            int maxWidth = Math.Min((int)Math.Round(900 * scale), work.Width);
            int maxHeight = Math.Min((int)Math.Round(1300 * scale), work.Height);
            int left = anchor.Left, top = anchor.Top, right = anchor.Right, bottom = anchor.Bottom;
            bool movesLeft = direction.IndexOf("Left", StringComparison.Ordinal) >= 0;
            bool movesRight = direction.IndexOf("Right", StringComparison.Ordinal) >= 0;
            bool movesTop = direction.IndexOf("Top", StringComparison.Ordinal) >= 0;
            bool movesBottom = direction.IndexOf("Bottom", StringComparison.Ordinal) >= 0;
            if (movesLeft) left = Math.Max(work.Left, Math.Max(right - maxWidth, Math.Min(right - minWidth, anchor.Left + dx)));
            if (movesRight) right = Math.Min(work.Right, Math.Min(left + maxWidth, Math.Max(left + minWidth, anchor.Right + dx)));
            if (movesTop) top = Math.Max(work.Top, Math.Max(bottom - maxHeight, Math.Min(bottom - minHeight, anchor.Top + dy)));
            if (movesBottom) bottom = Math.Min(work.Bottom, Math.Min(top + maxHeight, Math.Max(top + minHeight, anchor.Bottom + dy)));
            int readableWidth = Math.Max(minWidth, (int)Math.Round((bottom - top) * 0.8));
            if (right - left > readableWidth)
            {
                if (movesLeft && !movesRight) left = right - readableWidth;
                else right = left + readableWidth;
            }
            return new Native.Rect { Left = left, Top = top, Right = right, Bottom = bottom };
        }

        private void BeginLiveResize(string direction)
        {
            if (State.Locked || !IsReady) return;
            resizeDirection = String.IsNullOrEmpty(direction) ? "BottomRight" : direction;
            Native.GetPhysicalCursorPos(out resizeStart); Native.PhysicalWindowRect(Desktop.Handle, out resizeRect);
            resizeTarget = resizeRect; resizeMoved = false; resizeScale = LogicalScreenScale;
            resizeWork = Native.MonitorFor(resizeRect).Work;
            Desktop.BeginResize(); resizing = true;
        }

        private void ContinueLiveResize()
        {
            if (!resizing || State.Locked || !IsReady) return;
            Native.Point cursor; Native.GetPhysicalCursorPos(out cursor);
            if (!resizeMoved && !PastDragThreshold(resizeStart, cursor, resizeScale)) return;
            resizeMoved = true;
            Native.Rect next = ResizeGeometry(resizeRect, resizeStart, cursor, resizeScale, resizeWork, resizeDirection);
            if (next.Left == resizeTarget.Left && next.Top == resizeTarget.Top && next.Right == resizeTarget.Right && next.Bottom == resizeTarget.Bottom) return;
            resizeTarget = next;
            // Resize the real WPF surface on every pointer update. Text, spacing and DPI
            // therefore reflow live instead of displaying a stretched bitmap preview.
            Desktop.Move(next.Left, next.Top, next.Right - next.Left, next.Bottom - next.Top);
        }
        internal static Native.Rect MoveGeometry(Native.Rect anchor, Native.Point start, Native.Point cursor, System.Drawing.Rectangle work)
        {
            int width = anchor.Right - anchor.Left, height = anchor.Bottom - anchor.Top;
            int x = anchor.Left + cursor.X - start.X, y = anchor.Top + cursor.Y - start.Y;
            x = Math.Max(work.Left, Math.Min(work.Right - width, x));
            y = Math.Max(work.Top, Math.Min(work.Bottom - height, y));
            return new Native.Rect { Left = x, Top = y, Right = x + width, Bottom = y + height };
        }
        internal static Native.Rect MoveAcrossMonitorsGeometry(Native.Rect anchor, Native.Point start, Native.Point cursor, IEnumerable<System.Drawing.Rectangle> works)
        {
            List<System.Drawing.Rectangle> screens = works == null ? new List<System.Drawing.Rectangle>() : works.ToList();
            if (screens.Count == 0) return anchor;
            System.Drawing.Rectangle desktop = screens[0];
            for (int i = 1; i < screens.Count; i++) desktop = System.Drawing.Rectangle.Union(desktop, screens[i]);
            int width = anchor.Right - anchor.Left, height = anchor.Bottom - anchor.Top;
            int x = anchor.Left + cursor.X - start.X, y = anchor.Top + cursor.Y - start.Y;
            x = Math.Max(desktop.Left, Math.Min(desktop.Right - width, x));
            y = Math.Max(desktop.Top, Math.Min(desktop.Bottom - height, y));
            return new Native.Rect { Left = x, Top = y, Right = x + width, Bottom = y + height };
        }
        internal static Native.Rect ConstrainMoveToWork(Native.Rect rect, System.Drawing.Rectangle work)
        {
            int width = Math.Min(rect.Right - rect.Left, work.Width), height = Math.Min(rect.Bottom - rect.Top, work.Height);
            int x = Math.Max(work.Left, Math.Min(work.Right - width, rect.Left));
            int y = Math.Max(work.Top, Math.Min(work.Bottom - height, rect.Top));
            return new Native.Rect { Left = x, Top = y, Right = x + width, Bottom = y + height };
        }
        internal static Native.Rect DpiMoveGeometry(Native.Rect anchor, Native.Point start, Native.Point cursor,
            double sourceScale, double targetScale, double widthDip, double heightDip, System.Drawing.Rectangle work, bool constrain)
        {
            // Keep DIP size and the pointer's logical grab offset, not a fixed
            // pixel rectangle. Always derive from the gesture origin (no drift).
            int width = Math.Max(1, (int)Math.Round(widthDip * targetScale));
            int height = Math.Max(1, (int)Math.Round(heightDip * targetScale));
            double fit = constrain ? Math.Min(1, Math.Min(work.Width / (double)width, work.Height / (double)height)) : 1;
            width = Math.Max(1, (int)Math.Round(width * fit));
            height = Math.Max(1, (int)Math.Round(height * fit));
            int x = cursor.X - (int)Math.Round((start.X - anchor.Left) * targetScale / sourceScale * fit);
            int y = cursor.Y - (int)Math.Round((start.Y - anchor.Top) * targetScale / sourceScale * fit);
            if (constrain)
            {
                x = Math.Max(work.Left, Math.Min(work.Right - width, x));
                y = Math.Max(work.Top, Math.Min(work.Bottom - height, y));
            }
            return new Native.Rect { Left = x, Top = y, Right = x + width, Bottom = y + height };
        }
        private Native.MonitorArea DragDisplayAt(Native.Point point)
        {
            // Cached at mouse-down: no display enumeration or DPI query per frame.
            Native.MonitorArea closest = dragDisplays[0]; long distance = Int64.MaxValue;
            foreach (Native.MonitorArea m in dragDisplays)
            {
                long dx = Math.Max(m.Bounds.Left - (long)point.X, Math.Max(0, (long)point.X - m.Bounds.Right + 1));
                long dy = Math.Max(m.Bounds.Top - (long)point.Y, Math.Max(0, (long)point.Y - m.Bounds.Bottom + 1));
                long next = dx * dx + dy * dy;
                if (next < distance) { distance = next; closest = m; }
                if (next == 0) break;
            }
            return closest;
        }
        private Native.Rect DragGeometry(Native.Point cursor, Native.MonitorArea monitor, bool constrain)
        {
            double targetScale;
            if (!dragDisplayScales.TryGetValue(monitor.Handle, out targetScale)) targetScale = Native.MonitorScale(monitor.Handle);
            return DpiMoveGeometry(dragRect, dragStart, cursor, dragScale, targetScale, dragWidth, dragHeight, monitor.Work, constrain);
        }
        internal static bool PastDragThreshold(Native.Point start, Native.Point current, double scale)
        {
            return Math.Abs(current.X - start.X) >= Math.Max(3, SystemParameters.MinimumHorizontalDragDistance * scale) ||
                Math.Abs(current.Y - start.Y) >= Math.Max(3, SystemParameters.MinimumVerticalDragDistance * scale);
        }
        internal bool MovePreviewActive { get { return movePreview != null && Native.IsWindow(movePreview.Handle); } }
        internal void BeginWindowMove(Native.Point cursor)
        {
            windowMoveFrames.Cancel();
            dragStart = cursor; Native.PhysicalWindowRect(Desktop.Handle, out dragRect);
            dragTarget = dragRect; dragMonitor = Native.MonitorFor(dragRect).Handle;
            dragDisplays = Native.PhysicalMonitors().ToArray();
            if (dragDisplays.Length == 0) dragDisplays = new[] { Native.MonitorFor(dragRect) };
            dragDisplayScales.Clear();
            foreach (Native.MonitorArea monitor in dragDisplays) dragDisplayScales[monitor.Handle] = Native.MonitorScale(monitor.Handle);
            dragScale = Native.MonitorScale(dragMonitor);
            dragWidth = State.Width; dragHeight = State.Height;
            Native.MonitorArea origin = Native.MonitorFor(dragRect);
            // A manually resized window may not yet have updated saved geometry.
            // A work-area fit must not overwrite the user's preferred DIP size.
            double fit = Math.Min(1, Math.Min(origin.Work.Width / (State.Width * dragScale), origin.Work.Height / (State.Height * dragScale)));
            if (Math.Abs(dragWidth * dragScale * fit - (dragRect.Right - dragRect.Left)) > 2 ||
                Math.Abs(dragHeight * dragScale * fit - (dragRect.Bottom - dragRect.Top)) > 2)
            { dragWidth = (dragRect.Right - dragRect.Left) / dragScale; dragHeight = (dragRect.Bottom - dragRect.Top) / dragScale; }
            dragMoved = false; previewAttempted = false; dragging = true;
        }
        internal void ContinueWindowMove(Native.Point current, bool refreshCursorAfterCapture = false)
        {
            if (!dragging || State.Locked || !IsReady) return;
            if (!dragMoved && !PastDragThreshold(dragStart, current, dragScale)) return;
            dragMoved = true;
            // Moving a per-pixel-transparent WPF HWND also drives WPF's native
            // position/DPI/layout pipeline. Keep that surface stationary in BOTH
            // modes: translate one immutable native surface for the entire gesture.
            if (!previewAttempted)
            {
                previewAttempted = true; BeginMovePreview(dragRect);
                // Capture may take longer on high DPI displays. Never move using
                // the pointer sampled before that work.
                if (refreshCursorAfterCapture) Native.GetPhysicalCursorPos(out current);
            }
            dragTarget = DragGeometry(current, DragDisplayAt(current), false);
            if (MovePreviewActive) UpdateMovePreview(dragTarget);
            else Desktop.Move(dragTarget.Left, dragTarget.Top, dragTarget.Right - dragTarget.Left, dragTarget.Bottom - dragTarget.Top);
        }
        internal void QueueWindowMove(Native.Point current)
        {
            if (dragging && !State.Locked && IsReady) windowMoveFrames.Queue(new Point(current.X, current.Y));
        }
        internal void FinishWindowMoveForTest(bool commit) { FinishMove(Control<FrameworkElement>("Card"), commit); }
        internal void FinishWindowMoveForTest(bool commit, Native.Point cursor) { FinishMove(Control<FrameworkElement>("Card"), commit, cursor); }
        internal IntPtr MovePreviewHandle { get { return movePreview == null ? IntPtr.Zero : movePreview.Handle; } }
        internal int MovePreviewSurfaceBuilds { get { return movePreview == null ? 0 : movePreview.SurfaceBuilds; } }
        internal int MovePreviewPositionUpdates { get { return movePreview == null ? 0 : movePreview.PositionUpdates; } }
        internal bool BeginMovePreview(Native.Rect rect)
        {
            try
            {
                BitmapSource snapshot = CaptureGestureSnapshot(rect);
                if (snapshot == null) return false;
                // Build the complete surface while hidden. Native clipping removes
                // the live HWND synchronously, rather than leaving its previous
                // opacity/shadow frame behind until WPF renders again.
                movePreview = new GesturePreview(snapshot, rect, false);
                dragCardOpacity = Control<Border>("Card").Opacity;
                dragThumbOpacity = Control<Thumb>("ResizeThumb").Opacity;
                moveSourceClip = new GestureWindowClip(Desktop.Handle);
                movePreview.Show();
                movePreviewHidCard = true;
                Control<Border>("Card").Opacity = 0;
                Control<Thumb>("ResizeThumb").Opacity = 0;
                return true;
            }
            catch (Exception e)
            {
                if (Desktop.Log != null) Desktop.Log("Gesture preview: " + e.Message);
                DisposeMovePreview();
                return false;
            }
        }
        internal void UpdateMovePreview(Native.Rect rect)
        {
            if (!MovePreviewActive) return;
            movePreview.Update(rect);
        }
        private void FinishMove(FrameworkElement header, bool commit, Native.Point? testCursor = null)
        {
            if (!dragging) return;
            bool pendingMove = windowMoveFrames.IsQueued;
            windowMoveFrames.Cancel();
            Native.Point releaseCursor; Native.GetPhysicalCursorPos(out releaseCursor);
            if (testCursor.HasValue) releaseCursor = testCursor.Value;
            // A quick press/move/release may happen before the next render event.
            // Commit that final point, but Escape must discard all queued movement.
            if (commit && !dragMoved && pendingMove) ContinueWindowMove(releaseCursor);
            dragging = false;
            if (header.IsMouseCaptured) header.ReleaseMouseCapture();
            Native.MonitorArea targetMonitor = Native.MonitorAt(releaseCursor);
            // Re-sample on release; the last mouse-move can precede the final point.
            // Cancel never resizes the proxy or reads a draft into persistent state.
            if (commit && dragMoved) dragTarget = DragGeometry(releaseCursor, targetMonitor, true);
            UpdateMovePreview(dragTarget);
            bool crossScreen = dragMonitor != targetMonitor.Handle;
            if (!commit && dragMoved && !MovePreviewActive && IsReady)
                Desktop.Move(dragRect.Left, dragRect.Top, dragRect.Right - dragRect.Left, dragRect.Bottom - dragRect.Top);
            if (commit && dragMoved && IsReady)
            {
                if (crossScreen && State.DesktopMode && !forceWindowed && !attachmentFallback)
                {
                    State.X = dragTarget.Left; State.Y = dragTarget.Top; State.PositionSet = true;
                    State.Width = dragWidth; State.Height = dragHeight;
                    SecondaryStartup.Capture(State, targetMonitor, dragTarget, true);
                    if (!targetMonitor.IsPrimary) { State.SecondaryWidth = dragWidth; State.SecondaryHeight = dragHeight; }
                    NotifyChanged();
                    Window.Hide();
                    DisposeMovePreview();
                    if (RebuildRequested != null) RebuildRequested();
                    return;
                }
                if (Desktop.IsAttached || !State.DesktopMode || forceWindowed || attachmentFallback)
                {
                    Desktop.Move(dragTarget.Left, dragTarget.Top, dragTarget.Right - dragTarget.Left, dragTarget.Bottom - dragTarget.Top);
                    State.X = dragTarget.Left; State.Y = dragTarget.Top;
                    State.Width = dragWidth; State.Height = dragHeight;
                    State.PositionSet = true; SecondaryStartup.Capture(State, targetMonitor, dragTarget, true);
                    if (!targetMonitor.IsPrimary) { State.SecondaryWidth = dragWidth; State.SecondaryHeight = dragHeight; }
                    NotifyChanged();
                }
            }
            DisposeMovePreview();
        }
        private void FinishResize(bool commit)
        {
            if (!resizing) return;
            resizing = false;
            Desktop.EndResize();
            if (!commit && resizeMoved && IsReady)
                Desktop.Move(resizeRect.Left, resizeRect.Top, resizeRect.Right - resizeRect.Left, resizeRect.Bottom - resizeRect.Top);
            else if (commit && resizeMoved && IsReady && (Desktop.IsAttached || !State.DesktopMode || forceWindowed || attachmentFallback))
            {
                RememberGeometry(false); NotifyChanged();
            }
        }
        internal void DisposeMovePreview()
        {
            Border card = Control<Border>("Card");
            Thumb thumb = Control<Thumb>("ResizeThumb");
            if (movePreviewHidCard)
            {
                if (card != null) card.Opacity = dragCardOpacity;
                if (thumb != null) thumb.Opacity = dragThumbOpacity;
            }
            if (moveSourceClip != null) { moveSourceClip.Dispose(); moveSourceClip = null; }
            bool defer = movePreviewHidCard;
            movePreviewHidCard = false;
            GesturePreview previous = movePreview; movePreview = null;
            if (previous != null)
            {
                // Keep the last bitmap until the restored/new WPF card has had its render
                // pass. RebuildRequested runs at Normal priority before this cleanup.
                if (defer && !Window.Dispatcher.HasShutdownStarted)
                    Window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(delegate
                    {
                        // The restored WPF card has rendered at its final position. Wait
                        // for DWM to present that frame before removing the frozen preview,
                        // preventing a one-frame desktop flash at the hand-off.
                        GesturePreview.FlushComposition();
                        previous.Dispose();
                    }));
                else previous.Dispose();
            }
        }
        private BitmapSource CaptureGestureSnapshot(Native.Rect rect)
        {
            FrameworkElement root = Window.Content as FrameworkElement;
            if (root == null || root.ActualWidth <= 0 || root.ActualHeight <= 0) return null;
            int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0) return null;
            RenderTargetBitmap bitmap = new RenderTargetBitmap(width, height, 96 * width / root.ActualWidth, 96 * height / root.ActualHeight, PixelFormats.Pbgra32);
            bitmap.Render(root); bitmap.Freeze(); return bitmap;
        }
        private static bool IsButtonSource(DependencyObject source)
        {
            while (source != null)
            {
                if (source is ButtonBase || source is TextBoxBase) return true;
                source = source is Visual || source is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
            }
            return false;
        }
        private static bool IsInside(DependencyObject source, DependencyObject ancestor)
        {
            while (source != null)
            {
                if (source == ancestor) return true;
                source = source is Visual || source is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
            }
            return false;
        }
        public double Scale
        {
            get { PresentationSource source = PresentationSource.FromVisual(Window); return source == null || source.CompositionTarget == null ? 1 : source.CompositionTarget.TransformToDevice.M11; }
        }
        public void InitializeNative(bool forceWindowed, bool attachmentFallback, IntPtr desktopSurface)
        {
            this.forceWindowed = forceWindowed;
            this.attachmentFallback = attachmentFallback;
            Desktop.Initialize(Window, false);
            if (State.DesktopMode && !forceWindowed && !attachmentFallback)
            {
                if (!Desktop.Attach(desktopSurface))
                {
                    this.attachmentFallback = true;
                    Window.ShowInTaskbar = true;
                }
            }
            IsReady = true;
            PlaceOnScreen(false);
            UpdateDisplayCompensation();
            RefreshStatus();
        }
        public void PlaceOnScreen(bool reset)
        {
            Native.Point cursor; Native.GetPhysicalCursorPos(out cursor);
            Native.MonitorArea screen = State.PositionSet && !reset ? Native.MonitorAt(new Native.Point { X = (int)State.X + 1, Y = (int)State.Y + 1 }) : Native.MonitorAt(cursor);
            double scale = Native.MonitorScale(screen.Handle);
            int width = (int)Math.Round(State.Width * scale), height = (int)Math.Round(State.Height * scale);
            System.Drawing.Rectangle work = screen.Work;
            int x = State.PositionSet && !reset ? (int)State.X : work.Right - width - 32;
            int y = State.PositionSet && !reset ? (int)State.Y : work.Top + 52;
            double fit = Math.Min(1, Math.Min(work.Width / (double)width, work.Height / (double)height));
            width = Math.Max(1, (int)Math.Round(width * fit)); height = Math.Max(1, (int)Math.Round(height * fit));
            x = Math.Max(work.Left, Math.Min(work.Right - width, x));
            y = Math.Max(work.Top, Math.Min(work.Bottom - height, y));
            Desktop.Move(x, y, width, height);
            UpdateDisplayCompensation();
        }
        public void RememberGeometry(bool preserveWorkFit = true)
        {
            if (!IsReady || !Native.IsWindow(Desktop.Handle)) return;
            if (State.DesktopMode && !forceWindowed && !attachmentFallback && !Desktop.IsAttached) return;
            Native.Rect rect; Native.PhysicalWindowRect(Desktop.Handle, out rect);
            State.X = rect.Left; State.Y = rect.Top;
            Native.MonitorArea monitor = Native.MonitorFor(rect);
            double scale = Native.MonitorScale(monitor.Handle);
            double fit = Math.Min(1, Math.Min(monitor.Work.Width / (State.Width * scale), monitor.Work.Height / (State.Height * scale)));
            bool keepPreferred = preserveWorkFit && fit < 1 &&
                Math.Abs(State.Width * scale * fit - (rect.Right - rect.Left)) <= 2 &&
                Math.Abs(State.Height * scale * fit - (rect.Bottom - rect.Top)) <= 2;
            if (!keepPreferred) { State.Width = (rect.Right - rect.Left) / scale; State.Height = (rect.Bottom - rect.Top) / scale; }
            State.PositionSet = true;
            SecondaryStartup.Capture(State, monitor, rect);
            if (keepPreferred && !monitor.IsPrimary && String.Equals(State.SecondaryDeviceName, monitor.DeviceName, StringComparison.OrdinalIgnoreCase))
            { State.SecondaryWidth = State.Width; State.SecondaryHeight = State.Height; }
        }
        public void AddFromInput()
        {
            string groupId = State.DisableGroups || String.IsNullOrWhiteSpace(State.ActiveGroupId) || State.ActiveGroupId == "__ungrouped__" ? null : State.ActiveGroupId;
            if (State.Add(Input.Text, groupId) == null) { Input.Focus(); return; }
            Input.Clear(); NotifyChanged(); Refresh(); Input.Focus();
        }
        public void Remove(TodoItem item)
        {
            if (item == null || item.Deleted) return;
            item.Deleted = true; item.DeletedAt = DateTime.UtcNow.Ticks; Removed.Push(item); undoGroups.Push(new List<TodoItem> { item });
            undoLabel = "已移除事项"; StartUndoCountdown(); NotifyChanged(); Refresh();
        }
        internal void ClearCompleted()
        {
            List<TodoItem> items = VisibleCompleted().ToList();
            if (items.Count == 0) return;
            foreach (TodoItem item in items) { item.Deleted = true; item.DeletedAt = DateTime.UtcNow.Ticks; Removed.Push(item); }
            undoGroups.Push(items); undoLabel = "已清空 " + items.Count + " 条已完成事项";
            StartUndoCountdown(); NotifyChanged(); Refresh();
        }
        internal void ClearUndoHistory()
        {
            undoTimer.Stop(); undoExpiresAtUtc = DateTime.MinValue;
            Removed.Clear(); undoGroups.Clear(); undoLabel = "已移除事项"; Refresh();
        }
        public void Undo()
        {
            if (undoExpiresAtUtc != DateTime.MinValue && DateTime.UtcNow >= undoExpiresAtUtc) { ExpireUndo(); return; }
            if (undoGroups.Count == 0) return;
            List<TodoItem> items = undoGroups.Pop();
            foreach (TodoItem item in items) State.Restore(item);
            for (int i = 0; i < items.Count && Removed.Count > 0; i++) Removed.Pop();
            undoLabel = undoGroups.Count > 0 && undoGroups.Peek().Count > 1 ? "已清空 " + undoGroups.Peek().Count + " 条已完成事项" : "已移除事项";
            if (undoGroups.Count > 0) StartUndoCountdown();
            else { undoTimer.Stop(); undoExpiresAtUtc = DateTime.MinValue; }
            NotifyChanged(); Refresh();
        }
        private void StartUndoCountdown()
        {
            undoExpiresAtUtc = DateTime.UtcNow.AddSeconds(10);
            undoTimer.Stop(); undoTimer.Interval = TimeSpan.FromMilliseconds(250); undoTimer.Start();
        }
        private void ExpireUndo()
        {
            if (undoExpiresAtUtc != DateTime.MinValue && DateTime.UtcNow < undoExpiresAtUtc)
            {
                UpdateUndoMessage(); return;
            }
            undoTimer.Stop(); undoExpiresAtUtc = DateTime.MinValue;
            Removed.Clear(); undoGroups.Clear(); undoLabel = "已移除事项"; Refresh();
        }
        internal void ExpireUndoForTest() { undoExpiresAtUtc = DateTime.UtcNow.AddMilliseconds(-1); ExpireUndo(); }
        internal int UndoSecondsRemaining { get { return UndoSecondsAt(DateTime.UtcNow); } }
        private int UndoSecondsAt(DateTime now)
        { return undoExpiresAtUtc == DateTime.MinValue ? 0 : Math.Max(0, (int)Math.Ceiling((undoExpiresAtUtc - now).TotalSeconds)); }
        private void UpdateUndoMessage()
        { Control<TextBlock>("UndoMessage").Text = undoLabel + " · " + UndoSecondsRemaining + " 秒"; }
        internal void AdvanceUndoForTest(int seconds)
        { undoExpiresAtUtc = undoExpiresAtUtc.AddSeconds(-seconds); ExpireUndo(); }
        public void NotifyChanged() { IsSaved = false; if (Changed != null) Changed(); }
        private void NotifySettingsChanged()
        { IsSaved = false; if (SettingsChanged != null) SettingsChanged(); else if (Changed != null) Changed(); }
        public void Refresh()
        {
            UiDrag.Cancel(Window);
            StackPanel pending = Control<StackPanel>("PendingList"), completed = Control<StackPanel>("CompletedList");
            List<UIElement> nextPending = new List<UIElement>(), nextCompleted = new List<UIElement>();
            compactSearchLayout = CompactSearch;
            UpdateRowAppearance();
            foreach (string key in headingCache.Keys.Where(key => key != "__ungrouped__" && !State.Groups.Any(g => g.Id == key)).ToArray()) headingCache.Remove(key);
            List<TodoItem> visiblePending = VisiblePending().ToList();
            if (State.DisableGroups || State.Groups.Count == 0)
                foreach (TodoItem item in visiblePending) nextPending.Add(CachedTaskRow(item));
            else
            {
                ILookup<string, TodoItem> members = visiblePending.ToLookup(item => item.GroupId ?? "");
                foreach (TodoGroup group in State.OrderedGroups.Where(g => g.Pinned)) AppendPendingGroup(pending, members[group.Id].ToList(), group.Id, group.Name, nextPending);
                AppendPendingGroup(pending, members[""].ToList(), null, L("未分组", "Ungrouped"), nextPending);
                foreach (TodoGroup group in State.OrderedGroups.Where(g => !g.Pinned)) AppendPendingGroup(pending, members[group.Id].ToList(), group.Id, group.Name, nextPending);
            }
            if (compactSearchLayout && searchQuery.Length > 0)
            {
                List<TodoItem> matchingDone = VisibleCompleted().ToList();
                if (matchingDone.Count > 0)
                {
                    TextBlock heading = new TextBlock { Text = L("已完成", "Completed") + "（" + matchingDone.Count + "）",
                        FontSize = 10.5, Margin = new Thickness(0, 10, 0, 4) };
                    heading.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); nextPending.Add(heading);
                    AppendCompletedPage(nextPending, matchingDone);
                }
            }
            else if (!compactSearchLayout && Control<Expander>("CompletedSection").IsExpanded)
                AppendCompletedPage(nextCompleted, VisibleCompleted().ToList());
            // Completed rows can move between the two lists at the responsive
            // breakpoint. Detach from the old parent before attaching the new one.
            if (compactSearchLayout) { ReconcileRows(completed, nextCompleted); ReconcileRows(pending, nextPending); }
            else { ReconcileRows(pending, nextPending); ReconcileRows(completed, nextCompleted); }
            PruneRowCache();
            Control<Grid>("CompletedDock").Visibility = compactSearchLayout ? Visibility.Collapsed : Visibility.Visible;
            Control<TextBlock>("PendingSectionLabel").Text = compactSearchLayout ? L("搜索结果", "Search results") : L("未完成", "Pending");
            int count = visiblePending.Count, done = VisibleCompleted().Count();
            int shownCount = compactSearchLayout && searchQuery.Length > 0 ? count + done : count;
            Control<TextBlock>("PendingCount").Text = IsEnglish ? shownCount.ToString() : shownCount + " 件";
            Control<Button>("SortButton").Content = State.PendingSort == "Descending" ? L("↓ 降序", "↓ Newest") : L("↑ 升序", "↑ Oldest");
            Control<Button>("GroupButton").Content = ActiveGroupName();
            Button addGroup = Control<Button>("AddGroupButton");
            addGroup.Visibility = State.DisableGroups ? Visibility.Collapsed : Visibility.Visible;
            Control<TextBlock>("AddGroupLabel").Text = ActiveGroupName();
            addGroup.ToolTip = L("新待办添加到：", "Add new task to: ") + ActiveGroupName();
            Control<TextBlock>("CompletedHeader").Text = IsEnglish ? "Completed (" + done + ")" : "已完成（" + done + "）";
            Control<FrameworkElement>("EmptyPanel").Visibility = count == 0 && (!compactSearchLayout || done == 0 || searchQuery.Length == 0) ? Visibility.Visible : Visibility.Collapsed;
            UpdateEmptyPlaceholder();
            Control<TextBlock>("EmptyTitle").Text = done == 0 ? L("把想做的事，先记下来", "Write down what you want to do") : L("都完成了，歇一会儿吧", "All done — take a break");
            Control<TextBlock>("EmptySubtitle").Text = done == 0 ? L("一件一件来，不必着急", "One thing at a time") : L("新的事项，随时可以在上方添加", "Add another task whenever you like");
            Control<FrameworkElement>("UndoPanel").Visibility = Removed.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateUndoMessage();
            Control<Button>("ClearCompletedButton").Visibility = done > 0 ? Visibility.Visible : Visibility.Collapsed;
            RefreshSearchResults();
            RefreshStatus(); UpdateProgress();
        }

        private void RefreshCompletedRows()
        {
            Refresh();
        }
        private static void ReconcileRows(StackPanel panel, IList<UIElement> desired)
        {
            HashSet<UIElement> keep = new HashSet<UIElement>(desired);
            for (int i = panel.Children.Count - 1; i >= 0; i--)
                if (!keep.Contains(panel.Children[i])) panel.Children.RemoveAt(i);
            for (int i = 0; i < desired.Count; i++)
            {
                UIElement row = desired[i];
                if (i < panel.Children.Count && panel.Children[i] == row) continue;
                int previous = panel.Children.IndexOf(row);
                if (previous >= 0) panel.Children.RemoveAt(previous);
                panel.Children.Insert(i, row);
            }
        }
        private void UpdateRowAppearance()
        {
            string next = State.Language + ":" + State.DisableGroups + ":" + CompactSearch +
                ":" + String.Join("|", State.OrderedGroups.Select(g => g.Id + ":" + g.Name + ":" + g.Pinned));
            if (next == rowAppearance) return;
            rowCache.Clear(); rowAppearance = next;
        }
        private void PruneRowCache()
        {
            HashSet<string> retained = new HashSet<string>(Control<StackPanel>("PendingList").Children.OfType<Grid>()
                .Concat(Control<StackPanel>("CompletedList").Children.OfType<Grid>()).Select(row => row.Tag as TodoItem)
                .Where(item => item != null).Select(item => item.Id));
            foreach (string id in rowCache.Keys.Where(id => !retained.Contains(id)).ToArray()) rowCache.Remove(id);
        }
        private Grid CachedTaskRow(TodoItem item)
        {
            string signature = item.Text + "\0" + item.CompletedAt + ":" + item.CreatedAt + ":" + item.Pinned + ":" + item.GroupId + ":" + item.ReminderIntervalMinutes + ":" + item.DailyReminderMinutesPlusOne + ":" + item.ReminderManaged;
            CachedRow cached;
            if (!rowCache.TryGetValue(item.Id, out cached) || cached.Signature != signature || cached.Item != item)
            { cached = new CachedRow { Item = item, Signature = signature, Row = CreateRow(item) }; rowCache[item.Id] = cached; }
            UpdateTaskTime(cached); cached.Row.RenderTransform = Transform.Identity; cached.Row.Opacity = 1; return cached.Row;
        }
        private void UpdateTaskTime(CachedRow cached)
        {
            TextBlock time = cached.Row.Children.OfType<TextBlock>().FirstOrDefault(t => Object.Equals(t.Tag, "ItemTime"));
            if (time == null) return;
            time.Visibility = State.HideItemTimes ? Visibility.Collapsed : Visibility.Visible;
            time.Text = ItemTime(cached.Item);
        }
        private void RefreshTaskTimes() { foreach (CachedRow cached in rowCache.Values) UpdateTaskTime(cached); }

        internal void ToggleSortDirection()
        {
            State.PendingSort = State.PendingSort == "Ascending" ? "Descending" : "Ascending";
            NotifyChanged(); Refresh();
        }

        private IEnumerable<TodoItem> VisiblePending() { return State.Pending.Where(MatchesSearch); }
        private IEnumerable<TodoItem> VisibleCompleted() { return State.Completed.Where(MatchesSearch); }
        private void AppendPendingGroup(StackPanel panel, IList<TodoItem> all, string groupId, string name, List<UIElement> desired)
        {
            List<TodoItem> items = all.ToList();
            string collapseKey = groupId ?? "__ungrouped__";
            bool collapsed = searchQuery.Length > 0 ? searchCollapsedGroups.Contains(collapseKey) : State.CollapsedGroupIds.Contains(collapseKey);
            if (searchQuery.Length > 0 && items.Count == 0) return;
            TodoGroup currentGroup = State.Groups.FirstOrDefault(g => g.Id == groupId);
            string signature = name + ":" + items.Count + ":" + collapsed + ":" + CompactSearch + ":" + (searchQuery.Length > 0) + ":" + (currentGroup != null && currentGroup.Pinned) + ":" + State.Language;
            CachedHeading cached;
            if (headingCache.TryGetValue(collapseKey, out cached) && cached.Signature == signature)
            {
                cached.Heading.Margin = new Thickness(0, desired.Count == 0 ? 0 : 7, 0, 2);
                desired.Add(cached.Heading);
                if (!collapsed) foreach (TodoItem item in items) desired.Add(CachedTaskRow(item));
                return;
            }
            Grid heading = new Grid { Height = 26, Margin = new Thickness(0, desired.Count == 0 ? 0 : 7, 0, 2), Background = Brushes.Transparent,
                UseLayoutRounding = false, SnapsToDevicePixels = false };
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
            heading.ColumnDefinitions.Add(new ColumnDefinition());
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Button fold = new Button { Style = Window.Resources["IconButton"] as Style, Width = 22, Height = 22,
                Padding = new Thickness(2), Content = UiGlyphs.Chevron(collapsed),
                Foreground = Brush("MutedBrush"), ToolTip = collapsed ? L("展开分组", "Expand group") : L("折叠分组", "Collapse group"),
                FocusVisualStyle = null };
            fold.VerticalAlignment = VerticalAlignment.Center;
            fold.SetResourceReference(Button.ForegroundProperty, "MutedBrush");
            System.Windows.Automation.AutomationProperties.SetName(fold, collapsed ? L("展开分组", "Expand group") : L("折叠分组", "Collapse group"));
            fold.Click += delegate
            {
                if (searchQuery.Length > 0)
                {
                    if (collapsed) searchCollapsedGroups.Remove(collapseKey); else searchCollapsedGroups.Add(collapseKey);
                    Refresh(); return;
                }
                if (collapsed) State.CollapsedGroupIds.Remove(collapseKey);
                else State.CollapsedGroupIds.Add(collapseKey);
                NotifyChanged(); Refresh();
            };
            TextBlock label = new TextBlock { Text = name, FontSize = 10.5, FontWeight = FontWeights.Medium, Foreground = Brush("MutedBrush"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            UiGlyphs.CenterLabelInk(label);
            label.SetResourceReference(TextBlock.ForegroundProperty, currentGroup != null && currentGroup.Pinned ? "AccentBrush" : "MutedBrush");
            heading.ContextMenu = GroupContextMenu(groupId);
            UiDrag.GroupBoundary(heading, "DeskTodo.Group");
            UiDrag.Target(heading, "DeskTodo.Task",
                delegate(object moving, bool after) { MoveTask(moving as TodoItem, groupId); },
                null, true, delegate(object moving, bool after) { return GroupTaskSlot(panel, heading, (TodoItem)moving); });
            if (groupId != null && !CompactSearch)
            {
                heading.Cursor = Cursors.SizeAll;
                UiDrag.Source(heading, groupId, "DeskTodo.Group", heading, true);
                UiDrag.Target(heading, "DeskTodo.Group", delegate(object moving, bool after) { MoveGroup(moving as string, groupId, after); },
                    delegate(object moving) { return CanMoveGroup(moving as string, groupId); });
            }
            TextBlock count = new TextBlock { Text = items.Count.ToString(), FontSize = 10, Foreground = Brush("MutedBrush"), VerticalAlignment = VerticalAlignment.Center };
            count.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            UiGlyphs.CenterLabelInk(count);
            heading.Children.Add(fold); Grid.SetColumn(label, 1); heading.Children.Add(label);
            Grid.SetColumn(count, 2); heading.Children.Add(count);
            headingCache[collapseKey] = new CachedHeading { Signature = signature, Heading = heading };
            desired.Add(heading);
            if (!collapsed) foreach (TodoItem item in items) desired.Add(CachedTaskRow(item));
        }
        private string ActiveGroupName()
        {
            if (String.IsNullOrWhiteSpace(State.ActiveGroupId)) return L("未分组", "Ungrouped");
            if (State.ActiveGroupId == "__ungrouped__") return L("未分组", "Ungrouped");
            TodoGroup group = State.Groups.FirstOrDefault(g => g.Id == State.ActiveGroupId);
            return group == null ? L("未分组", "Ungrouped") : group.Name;
        }
        private string GroupNameError(string value, TodoGroup except)
        {
            value = (value ?? "").Trim();
            if (value.Length == 0) return L("分组名称不能为空", "Group name cannot be empty");
            if (value.Length > 20) return L("分组名称最多 20 个字", "Group name must be at most 20 characters");
            if (State.Groups.Any(g => g != except && String.Equals(g.Name, value, StringComparison.CurrentCultureIgnoreCase)))
                return L("已经有同名分组", "A group with this name already exists");
            return null;
        }
        internal string RenameGroup(TodoGroup group, string value, bool refresh = true)
        {
            if (group == null || !State.Groups.Contains(group)) return L("分组已不存在", "The group no longer exists");
            string error = GroupNameError(value, group); if (error != null) return error;
            value = value.Trim(); if (group.Name == value) return null;
            group.Name = value; NotifyChanged();
            if (refresh) Refresh();
            return null;
        }
        private void WireGroupAutoName(TextBox name, TodoGroup group)
        {
            DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            Action save = delegate
            {
                timer.Stop(); if (!State.Groups.Contains(group)) return;
                string error = RenameGroup(group, name.Text, false);
                Control<TextBlock>("GroupStatus").Text = error ?? L("名称已自动保存", "Name saved automatically");
            };
            timer.Tick += delegate { save(); };
            name.TextChanged += delegate { timer.Stop(); timer.Start(); };
            name.LostKeyboardFocus += delegate { save(); if (String.Equals(name.Text.Trim(), group.Name, StringComparison.Ordinal)) name.Text = group.Name; };
            name.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { save(); e.Handled = true; } };
            name.Unloaded += delegate { save(); timer.Stop(); };
        }
        internal void SetGroupPinned(TodoGroup group, bool pinned)
        {
            if (group == null || !State.Groups.Contains(group) || group.Pinned == pinned) return;
            group.Pinned = pinned; NotifyChanged(); Refresh(); if (GroupOpen) RefreshGroups();
        }
        private void ConfirmDeleteGroup(TodoGroup group)
        {
            ShowConfirmation(L("删除分组", "Delete group"), L("删除“", "Delete \"") + group.Name +
                L("”后，其中的待办将移到未分组，不会删除事项。", "\"? Its tasks will become ungrouped and will not be deleted."), L("删除", "Delete"), delegate
            {
                if (!State.Groups.Contains(group)) return;
                foreach (TodoItem item in State.Items.Where(i => i.GroupId == group.Id)) item.GroupId = null;
                State.Groups.Remove(group); State.CollapsedGroupIds.Remove(group.Id);
                if (State.ActiveGroupId == group.Id) State.ActiveGroupId = null;
                NotifyChanged(); Refresh(); if (GroupOpen) RefreshGroups();
            });
        }
        private ContextMenu GroupContextMenu(string groupId)
        {
            ContextMenu menu = new ContextMenu { Style = Window.Resources["TaskMenu"] as Style, FontFamily = Window.FontFamily, FontSize = 12.5 };
            ConfigureMenuScale(menu);
            menu.Opened += delegate { foreach (string key in new[] { "CardBrush", "TextBrush", "LineBrush", "HoverBrush" }) menu.Resources[key] = Window.Resources[key]; };
            TodoGroup group = State.Groups.FirstOrDefault(g => g.Id == groupId);
            Action<string, Action> add = delegate(string title, Action action)
            {
                MenuItem item = new MenuItem { Header = title, Style = Window.Resources["TaskMenuItem"] as Style };
                item.Click += delegate { action(); }; menu.Items.Add(item);
            };
            if (group != null)
            {
                add(L("重命名分组", "Rename group"), delegate { OpenGroupName(group); });
                add(group.Pinned ? L("取消置顶分组", "Unpin group") : L("置顶分组", "Pin group"), delegate { SetGroupPinned(group, !group.Pinned); });
            }
            else add(L("创建分组…", "Create group…"), delegate { OpenGroupName(); });
            add(L("管理分组", "Manage groups"), delegate { OpenGroups(); });
            if (group != null) { menu.Items.Add(new Separator()); add(L("删除分组…", "Delete group…"), delegate { ConfirmDeleteGroup(group); }); }
            return menu;
        }
        internal void OpenGroupName(TodoGroup group = null)
        {
            if (State.DisableGroups) return;
            CloseOverlay(); CloseSettingsCategory(false); PrepareOverlaySurface(false);
            noticeTimer.Stop(); noticeExpiresAtUtc = DateTime.MinValue;
            Control<FrameworkElement>("ConfirmOverlay").Visibility = Visibility.Collapsed;
            Control<FrameworkElement>("PositionOverlay").Visibility = Visibility.Collapsed;
            TextBox name = Control<TextBox>("GroupNameInput"); name.Text = group == null ? "" : group.Name;
            editingGroup = group;
            Control<TextBlock>("GroupNameTitle").Text = group == null ? L("创建分组", "Create group") : L("重命名分组", "Rename group");
            System.Windows.Automation.AutomationProperties.SetName(Control<Button>("GroupNameClose"), L("关闭", "Close"));
            Control<TextBlock>("GroupNameMessage").Text = group == null ? L("最多 20 个字", "Up to 20 characters") : L("改名自动保存，点击外侧或按 Esc 关闭", "Changes save automatically; click outside or Esc to close");
            Control<Button>("GroupNameCancel").Content = group == null ? L("取消", "Cancel") : L("关闭", "Close");
            Control<Button>("GroupNameAccept").Content = L("创建", "Create");
            Control<Button>("GroupNameAccept").Visibility = group == null ? Visibility.Visible : Visibility.Collapsed;
            Control<FrameworkElement>("GroupNameOverlay").Visibility = Visibility.Visible;
            Control<FrameworkElement>("OverlayLayer").Visibility = Visibility.Visible;
            AnimateIn(Control<FrameworkElement>("OverlaySurface")); name.Focus(); name.SelectAll();
        }
        private bool SaveGroupNameDraft(bool refresh = true)
        {
            if (groupNameTimer != null) groupNameTimer.Stop();
            if (editingGroup == null) return true;
            string error = RenameGroup(editingGroup, Control<TextBox>("GroupNameInput").Text, refresh);
            Control<TextBlock>("GroupNameMessage").Text = error ?? L("名称已自动保存", "Name saved automatically");
            return error == null;
        }
        private void AcceptGroupName()
        {
            if (editingGroup != null) { if (SaveGroupNameDraft()) CloseOverlay(); return; }
            string value = (Control<TextBox>("GroupNameInput").Text ?? "").Trim(), error = GroupNameError(value, null);
            if (error != null) { Control<TextBlock>("GroupNameMessage").Text = error; return; }
            TodoGroup group = new TodoGroup { Name = value, SortOrder = State.Groups.Count };
            State.Groups.Add(group); State.ActiveGroupId = group.Id; NotifyChanged(); CloseOverlay(); Refresh(); if (GroupOpen) RefreshGroups();
        }
        private void ShowGroupMenu()
        {
            if (State.DisableGroups) return;
            ContextMenu menu = new ContextMenu { Style = Window.Resources["TaskMenu"] as Style, FontFamily = Window.FontFamily, FontSize = 12.5 };
            ConfigureMenuScale(menu);
            menu.Opened += delegate { foreach (string key in new[] { "CardBrush", "TextBrush", "LineBrush", "HoverBrush" }) menu.Resources[key] = Window.Resources[key]; };
            AddGroupChoice(menu, null, L("未分组", "Ungrouped"));
            foreach (TodoGroup group in State.OrderedGroups) AddGroupChoice(menu, group.Id, group.Name);
            menu.Items.Add(new Separator());
            MenuItem manage = new MenuItem { Header = L("创建分组…", "Create group…"), Style = Window.Resources["TaskMenuItem"] as Style };
            manage.Click += delegate { OpenGroupName(); }; menu.Items.Add(manage);
            Button button = Control<Button>("AddGroupButton");
            button.ContextMenu = menu;
            menu.PlacementTarget = button; menu.Placement = PlacementMode.Bottom; menu.IsOpen = true;
        }
        private void AddGroupChoice(ContextMenu menu, string groupId, string label)
        {
            bool selected = String.Equals(State.ActiveGroupId ?? "", groupId ?? "", StringComparison.Ordinal);
            MenuItem choice = new MenuItem { Header = (selected ? "✓  " : "    ") + label, Style = Window.Resources["TaskMenuItem"] as Style };
            choice.Click += delegate { State.ActiveGroupId = groupId; NotifyChanged(); Refresh(); }; menu.Items.Add(choice);
        }
        private void ConfigureMenuScale(ContextMenu menu)
        {
            menu.Opened += delegate
            {
                PresentationSource source = PresentationSource.FromVisual(menu);
                double popupScale = source == null || source.CompositionTarget == null ? Scale : source.CompositionTarget.TransformToDevice.M11;
                // Popups are separate HWNDs; the desktop surface's compensation
                // and the card's adaptive scale do not automatically reach them.
                double factor = contentScale * LogicalScreenScale / popupScale;
                menu.LayoutTransform = new ScaleTransform(factor, factor);
            };
        }
        internal bool GroupOpen { get { return Control<FrameworkElement>("GroupPanel").Visibility == Visibility.Visible; } }
        internal void OpenGroups()
        {
            if (State.DisableGroups) return;
            Control<FrameworkElement>("MainPanel").Visibility = Visibility.Collapsed;
            Control<FrameworkElement>("SettingsPanel").Visibility = Visibility.Collapsed;
            Control<FrameworkElement>("GroupPanel").Visibility = Visibility.Visible;
            RefreshGroups(); AnimateIn(Control<FrameworkElement>("GroupPanel"));
        }
        private void CloseGroups()
        {
            Control<FrameworkElement>("GroupPanel").Visibility = Visibility.Collapsed;
            Control<FrameworkElement>("MainPanel").Visibility = Visibility.Visible;
            AnimateIn(Control<FrameworkElement>("MainPanel")); Refresh(); Input.Focus();
        }
        internal void CreateGroup()
        {
            if (State.DisableGroups) return;
            TextBox input = Control<TextBox>("GroupCreateInput"); string name = (input.Text ?? "").Trim();
            if (name.Length == 0) { input.Focus(); return; }
            string error = GroupNameError(name, null);
            if (error != null) { Control<TextBlock>("GroupStatus").Text = error; return; }
            TodoGroup group = new TodoGroup { Name = name, SortOrder = State.Groups.Count };
            State.Groups.Add(group); State.ActiveGroupId = group.Id; input.Clear(); NotifyChanged(); RefreshGroups();
        }
        private void RefreshGroups()
        {
            StackPanel list = Control<StackPanel>("GroupList"); list.Children.Clear();
            ILookup<string, TodoItem> members = State.Pending.ToLookup(t => t.GroupId ?? "");
            foreach (TodoGroup group in State.OrderedGroups.Where(g => g.Pinned)) AddGroupManagementRow(list, group.Id, group.Name, true, members[group.Id].ToList());
            AddGroupManagementRow(list, null, L("未分组", "Ungrouped"), false, members[""].ToList());
            foreach (TodoGroup group in State.OrderedGroups.Where(g => !g.Pinned)) AddGroupManagementRow(list, group.Id, group.Name, true, members[group.Id].ToList());
        }
        internal bool CanMoveGroup(string movingId, string targetId)
        {
            TodoGroup moving = State.Groups.FirstOrDefault(g => g.Id == movingId), target = State.Groups.FirstOrDefault(g => g.Id == targetId);
            return moving != null && target != null && moving != target && moving.Pinned == target.Pinned;
        }
        internal void MoveGroup(string movingId, string targetId, bool after)
        {
            List<TodoGroup> order = State.OrderedGroups.ToList();
            TodoGroup moving = order.FirstOrDefault(g => g.Id == movingId), target = order.FirstOrDefault(g => g.Id == targetId);
            if (!CanMoveGroup(movingId, targetId)) return;
            TodoGroup[] previous = order.ToArray();
            order.Remove(moving); order.Insert(order.IndexOf(target) + (after ? 1 : 0), moving);
            if (order.SequenceEqual(previous)) return;
            for (int i = 0; i < order.Count; i++) order[i].SortOrder = i;
            NotifyChanged(); Refresh(); if (GroupOpen) RefreshGroups();
        }
        internal void MoveManagedTask(TodoItem item, string groupId, TodoItem target = null, bool after = true)
        {
            if (State.DisableGroups) return;
            MoveTask(item, groupId, target, after);
        }
        private void MoveTask(TodoItem item, string groupId, TodoItem target = null, bool after = true)
        {
            if (item == null || item.Deleted || item.IsCompleted) return;
            if (State.DisableGroups) groupId = item.GroupId;
            if (groupId != null && !State.Groups.Any(g => g.Id == groupId)) return;
            if (target != null && (target == item || target.IsCompleted || target.Deleted || target.Pinned != item.Pinned)) return;
            List<TodoItem> order = State.Pending.ToList(), previous = order.ToList();
            string previousGroup = item.GroupId; order.Remove(item);
            item.GroupId = groupId;
            int index = target == null ? order.FindLastIndex(t => String.Equals(t.GroupId ?? "", groupId ?? "", StringComparison.Ordinal) && t.Pinned == item.Pinned) + 1
                : order.IndexOf(target) + (after ? 1 : 0);
            order.Insert(Math.Max(0, Math.Min(index, order.Count)), item);
            if (previousGroup == item.GroupId && order.SequenceEqual(previous)) return;
            ApplyVisibleManualOrder(order); NotifyChanged(); Refresh(); if (GroupOpen) RefreshGroups();
        }
        private void AddGroupManagementRow(StackPanel list, string groupId, string label, bool editable, List<TodoItem> members)
        {
            Grid row = new Grid { Background = Brushes.Transparent };
            StackPanel body = new StackPanel();
            Border surface = new Border { Child = body, Background = Brush("InputBrush"), CornerRadius = new CornerRadius(9), Margin = new Thickness(0, 0, 0, 10), Padding = new Thickness(4) };
            body.Children.Add(row);
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bool selected = String.Equals(State.ActiveGroupId ?? "", groupId ?? "", StringComparison.Ordinal);
            System.Windows.Shapes.Ellipse choiceDot = new System.Windows.Shapes.Ellipse { Width = 14, Height = 14,
                Stroke = Brush("AccentBrush"), StrokeThickness = 1.5, Fill = selected ? Brush("AccentBrush") : Brushes.Transparent };
            Button select = new Button { Content = choiceDot, Style = Window.Resources["IconButton"] as Style, Width = 30, Height = 40, Foreground = Brush("AccentBrush"), ToolTip = L("选择此分组", "Select this group") };
            TextBox name = new TextBox { Text = label, IsReadOnly = !editable, Background = Brushes.Transparent, Foreground = Brush("TextBrush"), BorderThickness = new Thickness(0), Padding = new Thickness(4, 0, 4, 0), VerticalContentAlignment = VerticalAlignment.Center, MaxLength = 20, FocusVisualStyle = null };
            select.Click += delegate { State.ActiveGroupId = groupId; NotifyChanged(); RefreshGroups(); };
            row.Children.Add(select); Grid.SetColumn(name, 1); row.Children.Add(name);
            if (editable)
            {
                TodoGroup group = State.Groups.First(g => g.Id == groupId);
                name.Foreground = group.Pinned ? Brush("AccentBrush") : Brush("TextBrush");
                WireGroupAutoName(name, group);
                Button remove = new Button { Content = L("删除", "Delete"), Style = Window.Resources["IconButton"] as Style, Width = 46, Height = 40, FontSize = 10 };
                remove.Click += delegate { ConfirmDeleteGroup(group); };
                Grid.SetColumn(remove, 3); row.Children.Add(remove);
                surface.ContextMenu = GroupContextMenu(groupId);
                TextBlock grip = new TextBlock { Text = "⠿", FontSize = 16, Foreground = Brush("MutedBrush"), Width = 24,
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.SizeAll, ToolTip = "拖动调整分组顺序" };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); Grid.SetColumn(grip, 4); row.Children.Add(grip);
                UiDrag.Source(grip, groupId, "DeskTodo.Group", surface);
                UiDrag.Target(surface, "DeskTodo.Group", delegate(object moving, bool after) { MoveGroup(moving as string, groupId, after); },
                    delegate(object moving) { return CanMoveGroup(moving as string, groupId); });
            }
            UiDrag.Target(surface, "DeskTodo.ManagedTask", delegate(object moving, bool after) { MoveManagedTask(moving as TodoItem, groupId); },
                null, true, delegate(object moving, bool after) { return ManagedTaskSlot(body, (TodoItem)moving); });
            if (members.Count == 0) body.Children.Add(new TextBlock { Text = L("暂无未完成待办 · 可拖入事项", "No pending tasks · drop tasks here"),
                FontSize = 10, Foreground = Brush("MutedBrush"), Margin = new Thickness(30, 2, 8, 8) });
            foreach (TodoItem item in members)
            {
                Grid task = new Grid { Background = Brushes.Transparent, Margin = new Thickness(8, 0, 4, 0), Tag = item, MinHeight = 34 };
                task.ColumnDefinitions.Add(new ColumnDefinition()); task.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                task.Children.Add(new TextBlock { Text = item.Text, FontSize = 12.5, TextWrapping = TextWrapping.Wrap,
                    Foreground = Brush("TextBrush"), Margin = new Thickness(18, 7, 6, 7), Cursor = Cursors.SizeAll });
                Button delete = new Button { Content = UiGlyphs.Trash(), Style = Window.Resources["IconButton"] as Style,
                    Width = 28, Height = 28, ToolTip = L("删除（可撤销）", "Delete (undoable)"), VerticalAlignment = VerticalAlignment.Center };
                delete.Click += delegate { Remove(item); RefreshGroups(); };
                Grid.SetColumn(delete, 1); task.Children.Add(delete); body.Children.Add(task);
                UiDrag.Source(task, item, "DeskTodo.ManagedTask");
                UiDrag.Target(task, "DeskTodo.ManagedTask", delegate(object moving, bool after) { MoveManagedTask(moving as TodoItem, groupId, item, after); },
                    delegate(object moving) { TodoItem source = moving as TodoItem; return source != null && source != item && source.Pinned == item.Pinned; });
            }
            list.Children.Add(surface);
        }
        private Grid CreateRow(TodoItem item)
        {
            Grid row = new Grid { Background = Brushes.Transparent, Tag = item };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            // Align the visible first-line glyphs with the checkbox/menu center at 19 DIP.
            // TextBlock's line box includes extra font ascent space above numeric glyphs.
            TextBlock text = new TextBlock { Text = item.Text, TextWrapping = TextWrapping.Wrap, FontSize = 12.5, LineHeight = 22, Margin = new Thickness(0, 10, 0, 8) };
            text.SetResourceReference(TextBlock.ForegroundProperty, item.IsCompleted ? "MutedBrush" : "TextBrush");
            if (item.IsCompleted) { text.TextDecorations = TextDecorations.Strikethrough; }
            else { text.Cursor = Cursors.Hand; text.ToolTip = L("双击编辑", "Double-click to edit"); if (item.Pinned) text.FontWeight = FontWeights.SemiBold; }
            CheckBox check = new CheckBox { IsChecked = item.IsCompleted, Style = Window.Resources["TaskCheck"] as Style };
            System.Windows.Automation.AutomationProperties.SetName(check, (item.IsCompleted ? "恢复未完成：" : "标记完成：") + item.Text);
            check.Click += delegate
            {
                State.Toggle(item);
                if (!item.IsCompleted && item.ReminderIntervalMinutes > 0) item.LastIntervalReminderAt = DateTime.UtcNow.Ticks;
                NotifyChanged(); Refresh();
            };
            ContextMenu menu = new ContextMenu { Style = Window.Resources["TaskMenu"] as Style, FontFamily = Window.FontFamily, FontSize = 12.5 };
            menu.Opened += delegate
            {
                foreach (string key in new[] { "CardBrush", "TextBrush", "LineBrush", "HoverBrush" }) menu.Resources[key] = Window.Resources[key];
            };
            ConfigureMenuScale(menu);
            MenuItem pin = new MenuItem { Header = item.Pinned ? L("取消置顶", "Unpin") : L("置顶事项", "Pin task"), Style = Window.Resources["TaskMenuItem"] as Style };
            pin.Click += delegate { item.Pinned = !item.Pinned; NotifyChanged(); Refresh(); };
            MenuItem edit = new MenuItem { Header = L("编辑事项", "Edit task"), Style = Window.Resources["TaskMenuItem"] as Style }; edit.Click += delegate { Edit(item); };
            MenuItem copy = new MenuItem { Header = L("复制事项", "Copy task"), Style = Window.Resources["TaskMenuItem"] as Style };
            copy.Click += delegate
            {
                try { Clipboard.SetText(item.Text); }
                catch (Exception e) { ShowNotice("无法复制", "剪贴板暂时不可用：\n" + e.Message); }
            };
            MenuItem reminder = new MenuItem { Header = item.ReminderIntervalMinutes > 0 || item.DailyReminderMinutesPlusOne > 0 ? L("设置提醒（已开启）", "Reminder (on)") : L("设置提醒", "Set reminder"), Style = Window.Resources["TaskMenuItem"] as Style };
            reminder.Click += delegate { OpenItemReminder(item); };
            MenuItem remove = new MenuItem { Header = L("删除（可撤销）", "Delete (undoable)"), Style = Window.Resources["TaskMenuItem"] as Style }; remove.Click += delegate { Remove(item); };
            menu.Items.Add(pin); menu.Items.Add(edit); menu.Items.Add(copy);
            if (!State.DisableGroups)
            {
                MenuItem moveGroup = new MenuItem { Header = L("移动到分组", "Move to group"), Style = Window.Resources["TaskSubmenuItem"] as Style };
                AddMoveGroupChoice(moveGroup, item, null, L("未分组", "Ungrouped"));
                foreach (TodoGroup group in State.OrderedGroups) AddMoveGroupChoice(moveGroup, item, group.Id, group.Name);
                menu.Items.Add(moveGroup);
            }
            menu.Items.Add(reminder); menu.Items.Add(remove);
            if (!item.IsCompleted) row.ContextMenu = menu;
            text.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) { EditFromTaskText(item, e.ClickCount); };
            row.Children.Add(check); Grid.SetColumn(text, 1); row.Children.Add(text);
            TextBlock time = new TextBlock
            {
                Text = ItemTime(item), FontSize = 9, Foreground = Brush("MutedBrush"), Margin = new Thickness(6, 12, 2, 0),
                VerticalAlignment = VerticalAlignment.Top, Visibility = State.HideItemTimes ? Visibility.Collapsed : Visibility.Visible,
                ToolTip = item.IsCompleted ? "完成时间" : "添加时间", Tag = "ItemTime"
            };
            Grid.SetColumn(time, 3); row.Children.Add(time);
            time.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            Button more = new Button { Style = Window.Resources["IconButton"] as Style, Content = item.IsCompleted ? UiGlyphs.Trash() : CreateDots(Orientation.Vertical), Width = 23, Height = 28, Margin = new Thickness(5, 5, 0, 0), VerticalAlignment = VerticalAlignment.Top, Opacity = 0.65, ToolTip = item.IsCompleted ? L("删除（可撤销）", "Delete (undoable)") : State.DisableGroups ? L("编辑、复制、提醒或删除", "Edit, copy, remind or delete") : L("编辑、复制、分组、提醒或删除", "Edit, copy, group, remind or delete"), FocusVisualStyle = null };
            ToolTipService.SetPlacement(more, PlacementMode.Left);
            ToolTipService.SetHorizontalOffset(more, -8);
            System.Windows.Automation.AutomationProperties.SetName(more, (item.IsCompleted ? "删除（可撤销）：" : "编辑、复制、提醒或删除：") + item.Text);
            more.Click += delegate { if (item.IsCompleted) Remove(item); else { menu.PlacementTarget = more; menu.Placement = PlacementMode.Left; menu.IsOpen = true; } };
            row.MouseEnter += delegate { more.Opacity = 1; }; row.MouseLeave += delegate { more.Opacity = 0.65; };
            Grid.SetColumn(more, 4); row.Children.Add(more);
            if (!item.IsCompleted && !CompactSearch)
            {
                UiDrag.Source(row, item, "DeskTodo.Task");
                UiDrag.Target(row, "DeskTodo.Task", delegate(object moving, bool after) { MoveTask(moving as TodoItem, item.GroupId, item, after); },
                    delegate(object moving) { return CanReorderTaskBeside(moving as TodoItem, item); });
            }
            return row;
        }
        private UiDrag.Session testTaskDrag;
        private StackPanel testTaskPanel;
        internal void BeginTaskDragForTest(Grid row, Point pointer)
        {
            testTaskPanel = VisualTreeHelper.GetParent(row) as StackPanel;
            testTaskDrag = UiDrag.BeginForTest(row, testTaskPanel.TranslatePoint(pointer, (FrameworkElement)Window.Content));
        }
        internal void QueueTaskDragForTest(Point pointer)
        { if (testTaskDrag != null) testTaskDrag.Queue(testTaskPanel.TranslatePoint(pointer, (FrameworkElement)Window.Content)); }
        internal int TaskDragFrameUpdates { get { return testTaskDrag == null ? 0 : testTaskDrag.FrameUpdates; } }
        internal UiDrag.Session TaskDragForTest { get { return testTaskDrag; } }
        internal void EndTaskDragForTest(Point pointer, bool commit)
        {
            if (testTaskDrag == null) return;
            if (commit) testTaskDrag.Flush(testTaskPanel.TranslatePoint(pointer, (FrameworkElement)Window.Content));
            testTaskDrag.Complete(commit); testTaskDrag = null; testTaskPanel = null;
        }
        internal bool CanReorderTaskBeside(TodoItem moving, TodoItem candidate)
        { return moving != null && candidate != null && moving != candidate && moving.Pinned == candidate.Pinned; }
        private UiDrag.Slot GroupTaskSlot(StackPanel panel, FrameworkElement heading, TodoItem moving)
        {
            int first = panel.Children.IndexOf(heading) + 1, end = first;
            while (end < panel.Children.Count && !UiDrag.IsBoundary(panel.Children[end] as FrameworkElement, "DeskTodo.Group")) end++;
            int last = -1;
            for (int i = first; i < end; i++)
            {
                TodoItem item = ((FrameworkElement)panel.Children[i]).Tag as TodoItem;
                if (item != null && item != moving && item.Pinned == moving.Pinned) last = i;
            }
            return new UiDrag.Slot(panel, last >= 0 ? last + 1 : moving.Pinned ? first : end);
        }
        private UiDrag.Slot ManagedTaskSlot(StackPanel body, TodoItem moving)
        {
            int last = -1;
            for (int i = 1; i < body.Children.Count; i++)
            {
                TodoItem item = ((FrameworkElement)body.Children[i]).Tag as TodoItem;
                if (item != null && item != moving && item.Pinned == moving.Pinned) last = i;
            }
            return new UiDrag.Slot(body, last >= 0 ? last + 1 : moving.Pinned ? 1 : body.Children.Count);
        }
        internal void ApplyVisibleManualOrder(IList<TodoItem> visibleOrder)
        {
            if (visibleOrder == null || visibleOrder.Count == 0) return;
            HashSet<string> visibleIds = new HashSet<string>(visibleOrder.Select(item => item.Id));
            List<TodoItem> all = State.Pending.ToList();
            int visibleIndex = 0;
            for (int i = 0; i < all.Count && visibleIndex < visibleOrder.Count; i++)
                if (visibleIds.Contains(all[i].Id)) all[i] = visibleOrder[visibleIndex++];
            for (int i = 0; i < all.Count; i++) all[i].SortOrder = i;
            State.PendingSort = "Manual";
        }
        private void AddMoveGroupChoice(MenuItem parent, TodoItem item, string groupId, string label)
        {
            MenuItem choice = new MenuItem { Header = (String.Equals(item.GroupId ?? "", groupId ?? "", StringComparison.Ordinal) ? "✓  " : "    ") + label, Style = Window.Resources["TaskMenuItem"] as Style };
            choice.Click += delegate { item.GroupId = groupId; NotifyChanged(); Refresh(); };
            parent.Items.Add(choice);
        }
        private string ItemTime(TodoItem item)
        {
            long ticks = item.IsCompleted ? item.CompletedAt : item.CreatedAt;
            if (ticks <= 0 || ticks > DateTime.MaxValue.Ticks) return "";
            try { return new DateTime(ticks, DateTimeKind.Utc).ToLocalTime().ToString(State.ShowItemDates ? "M/d HH:mm" : "HH:mm"); }
            catch { return ""; }
        }
        internal void EditFromTaskText(TodoItem item, int clickCount)
        {
            if (item != null && !item.IsCompleted && clickCount >= 2) OpenEdit(item);
        }
        private FrameworkElement CreateDots(Orientation orientation)
        {
            StackPanel dots = new StackPanel
            {
                Orientation = orientation,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };
            for (int i = 0; i < 3; i++)
                dots.Children.Add(new System.Windows.Shapes.Ellipse
                {
                    Width = 1.8,
                    Height = 1.8,
                    Fill = Brush("MutedBrush"),
                    Margin = orientation == Orientation.Vertical ? new Thickness(0, 1.1, 0, 1.1) : new Thickness(1.1, 0, 1.1, 0),
                    SnapsToDevicePixels = false
                });
            return dots;
        }
        private Brush Brush(string name) { return (Brush)Window.Resources[name]; }
        public void RefreshStatus()
        {
            Button button = Control<Button>("LockButton");
            // Lock state is communicated by the shackle shape, not a darker color.
            button.Foreground = Brush("MutedBrush");
            button.Background = Brushes.Transparent;
            Control<System.Windows.Shapes.Path>("LockShackle").Data = State.Locked ? UiGlyphs.ClosedShackleGeometry : UiGlyphs.OpenShackleGeometry;
            button.ToolTip = State.Locked ? "已锁定，点击解锁" : "未锁定，点击锁定";
            System.Windows.Automation.AutomationProperties.SetName(button, State.Locked ? "已锁定，点击解锁" : "未锁定，点击锁定");
            TextBlock title = Control<TextBlock>("TitleLabel");
            title.Cursor = Cursors.Arrow;
            title.ToolTip = L("可在设置中修改名称", "Edit the name in Settings");
            Control<FrameworkElement>("DragHeader").Cursor = State.Locked ? Cursors.Arrow : Cursors.SizeAll;
            Control<FrameworkElement>("ResizeHandles").Visibility = State.Locked ? Visibility.Hidden : Visibility.Visible;
            Control<Button>("MinimizeButton").Visibility = State.ShowMinimizeButton ? Visibility.Visible : Visibility.Collapsed;
            Control<Button>("CloseButton").Visibility = State.ShowCloseButton ? Visibility.Visible : Visibility.Collapsed;
            Control<Button>("CompactButton").Visibility = State.ShowCompactButton ? Visibility.Visible : Visibility.Collapsed;
            Control<TextBlock>("DateLabel").Text = DateTime.Now.ToString(IsEnglish ? "MMM d  dddd" : "M月d日  dddd", System.Globalization.CultureInfo.GetCultureInfo(IsEnglish ? "en-US" : "zh-CN"));
            bool wantsDesktop = State.DesktopMode && !forceWindowed;
            string mode = Desktop.IsAttached ? L("桌面模式", "Desktop") : attachmentFallback ? L("等待桌面恢复", "Waiting for desktop") : wantsDesktop ? L("桌面挂载失败", "Desktop unavailable") : L("窗口模式", "Window");
            TextBlock status = Control<TextBlock>("StatusLabel");
            string clock = DateTime.Now.ToString("HH:mm");
            status.Text = (IsSavePending ? L("正在保存", "Saving") : IsSaved ? L("已自动保存", "Saved") + " " + clock : L("保存失败", "Save failed")) + " · " + mode;
            status.ToolTip = SaveError ?? (attachmentFallback ? "Explorer 桌面尚未就绪；当前以普通窗口显示，恢复后会自动重新嵌入。" :
                wantsDesktop && !Desktop.IsAttached ? "正在等待 Explorer 桌面恢复；也可在设置中关闭后重新开启“嵌入桌面”。" : "数据只保存在本机；勾选即完成。");
            Control<System.Windows.Shapes.Ellipse>("StatusDot").Fill = IsSavePending ? Brush("MutedBrush") : IsSaved ? Desktop.IsAttached || !wantsDesktop || attachmentFallback ?
                SavedStatusBrush : Brushes.DarkOrange : Brushes.IndianRed;
        }
        private void UpdateProgress()
        {
            // Counting does not need the sorted enumerables used to display rows.
            int done = 0, total = 0;
            foreach (TodoItem item in State.Items)
            { if (item.Deleted) continue; total++; if (item.IsCompleted) done++; }
            Control<TextBlock>("ProgressLabel").Text = total == 0 ? "" : done + "/" + total;
            Border track = Control<Border>("ProgressTrack");
            double available = Math.Max(0, track.ActualWidth > 0 ? track.ActualWidth :
                (Window.ActualWidth > 0 ? Window.ActualWidth : Window.Width) / displayCompensation / contentScale - 60);
            Control<Border>("ProgressFill").Width = total == 0 ? 0 : available * done / total;
        }
        internal static double AdaptiveContentScale(double width, double height)
        {
            // Wide/tall cards should increase readability, not merely add blank area.
            // Preserve at least the default card's logical height while enlarging.
            // Scaling a short landscape monitor's card down to 400 logical DIP
            // left only a few rows beneath the header, input and group headings.
            return Math.Round(Math.Max(1, Math.Min(1.8, Math.Min(width / 360, height / 520))), 3);
        }
        internal void UpdateContentScale()
        {
            UpdateDisplayCompensation();
            double width = (Window.ActualWidth > 0 ? Window.ActualWidth : Window.Width) / displayCompensation;
            double height = (Window.ActualHeight > 0 ? Window.ActualHeight : Window.Height) / displayCompensation;
            // The shadow gutter is not content space. Account for it before
            // scaling, otherwise short/wide windows lose usable list height.
            Thickness gutter = Control<Border>("Card").Margin;
            double next = AdaptiveContentScale(width - gutter.Left - gutter.Right, height - gutter.Top - gutter.Bottom);
            Grid content = Control<Grid>("CardContent");
            if (content == null || (Math.Abs(next - contentScale) < 0.001 && content.LayoutTransform is ScaleTransform)) return;
            contentScale = next;
            content.LayoutTransform = new ScaleTransform(next, next);
            if (settingsCategorySurface != null) settingsCategorySurface.LayoutTransform = new ScaleTransform(next, next);
            if (toastSurface != null) { toastSurface.LayoutTransform = new ScaleTransform(next, next); toastSurface.Margin = new Thickness(18, 0, 18, 44 * next); }
            if (settingsCategorySurface != null) SizeSettingsCategory();
            UpdateCardClip();
        }
        private void UpdateDisplayCompensation()
        {
            if (!IsReady || !Native.IsWindow(Desktop.Handle)) return;
            // A desktop child inherits the host's DPI (often the primary display),
            // even when physically on another monitor. Scale the complete vector
            // surface, including corners, gutters and resize grips, exactly once.
            Native.Rect rect; Native.PhysicalWindowRect(Desktop.Handle, out rect);
            double next = Desktop.IsAttached ? Native.MonitorScale(Native.MonitorFor(rect).Handle) / Scale : 1;
            if (Math.Abs(next - displayCompensation) < .0001) return;
            displayCompensation = next;
            Control<Grid>("DpiSurface").LayoutTransform = new ScaleTransform(next, next);
        }
        public void ApplyTheme()
        {
            State.ThemeId = ThemeCatalog.Normalize(State.ThemeId);
            string signature = State.ThemeId + ":" + State.DarkMode;
            if (signature == appliedTheme) { ApplyCardOpacity(); return; }
            appliedTheme = signature;
            ThemePalette palette = ThemeCatalog.Get(State.ThemeId);
            string[] colors = State.DarkMode ? palette.Dark : palette.Light;
            for (int i = 0; i < ThemeCatalog.ResourceNames.Length; i++)
                Window.Resources[ThemeCatalog.ResourceNames[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
            Window.Resources["AccentTextBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(ThemeCatalog.AccentText(State.ThemeId, State.DarkMode)));
            Window.Resources["CheckLineBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(State.DarkMode ? "#777770" : "#8F8F89"));
            Color outline = (Color)ColorConverter.ConvertFromString(State.DarkMode ? "#505050" : "#DADAD8");
            SolidColorBrush inputOutline = new SolidColorBrush(outline); inputOutline.Freeze(); Window.Resources["InputLineBrush"] = inputOutline;
            ApplyCardOpacity();
        }
        private void ApplyCardOpacity()
        {
            // A layered WPF top-level window may ignore Window.Opacity after it is
            // reparented into Explorer's desktop icon surface. Fade the rendered card
            // instead so the same setting works in both desktop and windowed modes.
            Window.Opacity = 1;
            if (!movePreviewHidCard) Control<Border>("Card").Opacity = State.WindowOpacityPercent / 100.0;
        }
        private void ShowMenu()
        {
            SettingsOpen = true;
        }
        private bool SettingsCategoryOpen { get { return settingsCategoryLayer != null && settingsCategoryLayer.Visibility == Visibility.Visible; } }
        internal bool CategoryDialogOpen { get { return SettingsCategoryOpen; } }
        internal Grid SettingsCategoryBackdrop { get { return settingsCategoryLayer; } }
        internal ScrollViewer SettingsCategoryScroll { get { return settingsCategoryScroll; } }
        private void BuildSettingsCategories()
        {
            ScrollViewer overview = Control<ScrollViewer>("SettingsScroll");
            StackPanel original = overview.Content as StackPanel;
            if (original == null) return;
            string[] keys = { "BasicSectionLabel", "CustomSectionLabel", "ThemeLabel", "LanSectionLabel", "ManagementLabel" };
            UIElement[] controls = original.Children.Cast<UIElement>().ToArray();
            original.Children.Clear();
            string section = null;
            foreach (UIElement child in controls)
            {
                FrameworkElement element = child as FrameworkElement;
                if (element != null && keys.Contains(element.Name))
                {
                    section = element.Name;
                    settingsCategoryContents[section] = new StackPanel();
                    continue;
                }
                if (section != null) settingsCategoryContents[section].Children.Add(child);
            }
            foreach (string key in keys)
            {
                string selected = key;
                Button entry = new Button { Height = 58, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Margin = new Thickness(0, 0, 0, 10), Padding = new Thickness(15, 7, 12, 7),
                    Style = Window.Resources["SettingsCategoryButton"] as Style, Cursor = Cursors.Hand };
                Grid row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                TextBlock label = new TextBlock { FontSize = 13.5, FontWeight = FontWeights.Medium,
                    VerticalAlignment = VerticalAlignment.Center };
                label.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
                UiGlyphs.CenterLabelInk(label);
                row.Children.Add(label);
                FrameworkElement arrow = UiGlyphs.Chevron(true);
                arrow.VerticalAlignment = VerticalAlignment.Center;
                ((System.Windows.Shapes.Path)((Grid)arrow).Children[0]).SetResourceReference(System.Windows.Shapes.Path.StrokeProperty, "MutedBrush");
                Grid.SetColumn(arrow, 1); row.Children.Add(arrow);
                entry.Content = row; entry.Click += delegate { OpenSettingsCategory(selected); };
                settingsCategoryButtons[key] = entry; original.Children.Add(entry);
            }
            SolidColorBrush shade = new SolidColorBrush(Color.FromArgb(0x66, 0x0B, 0x17, 0x20));
            settingsCategoryLayer = new Grid { Visibility = Visibility.Collapsed, Margin = new Thickness(-1), Background = shade,
                SnapsToDevicePixels = false };
            Panel.SetZIndex(settingsCategoryLayer, 10);
            settingsCategoryLayer.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            { if (e.OriginalSource == settingsCategoryLayer) CloseSettingsCategory(); e.Handled = true; };
            settingsCategorySurface = new Border { CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1),
                Padding = new Thickness(17), HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center };
            settingsCategorySurface.SetResourceReference(Border.BackgroundProperty, "CardBrush");
            settingsCategorySurface.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
            settingsCategorySurface.LayoutTransform = new ScaleTransform(contentScale, contentScale);
            settingsCategoryLayer.Children.Add(settingsCategorySurface);
            Grid shell = new Grid(); shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            shell.RowDefinitions.Add(new RowDefinition()); settingsCategorySurface.Child = shell;
            Grid header = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            settingsCategoryTitle = new TextBlock { FontSize = 18, FontWeight = FontWeights.Medium,
                VerticalAlignment = VerticalAlignment.Center };
            settingsCategoryTitle.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            header.Children.Add(settingsCategoryTitle);
            Button close = new Button { Content = UiGlyphs.Close(), Width = 32, Height = 32,
                Style = Window.Resources["IconButton"] as Style, ToolTip = "返回设置总览" };
            close.Click += delegate { CloseSettingsCategory(); };
            Grid.SetColumn(close, 1); header.Children.Add(close); shell.Children.Add(header);
            settingsCategoryScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 4, 0) };
            Grid.SetRow(settingsCategoryScroll, 1); shell.Children.Add(settingsCategoryScroll);
            // Keep one unscaled backdrop inside the card's opacity group. Only
            // its dialog content scales. This removes overlapping rim patches
            // and their antialiasing seams at fractional DPI/content scales.
            Border card = Control<Border>("Card");
            Grid content = Control<Grid>("CardContent");
            card.Child = null;
            Grid layers = new Grid(); layers.Children.Add(content); layers.Children.Add(settingsCategoryLayer);
            card.Child = layers;
            settingsCategoryLayer.SizeChanged += delegate { UpdateSettingsCategoryClip(); SizeSettingsCategory(); };
            SizeSettingsCategory(); UpdateSettingsCategoryLabels();
        }
        private void SizeSettingsCategory()
        {
            if (settingsCategorySurface == null) return;
            double width = (settingsCategoryLayer.ActualWidth > 0 ? settingsCategoryLayer.ActualWidth : State.Width - 2) / contentScale;
            double height = (settingsCategoryLayer.ActualHeight > 0 ? settingsCategoryLayer.ActualHeight : State.Height - 2) / contentScale;
            settingsCategorySurface.Width = Math.Min(520, Math.Max(220, width - 32));
            settingsCategorySurface.Height = Double.NaN;
            settingsCategorySurface.MaxHeight = Math.Min(580, Math.Max(220, height - 32));
        }
        private void UpdateSettingsCategoryClip()
        {
            if (settingsCategoryLayer == null || settingsCategoryLayer.ActualWidth <= 0 || settingsCategoryLayer.ActualHeight <= 0) return;
            settingsCategoryLayer.Clip = new RectangleGeometry(new Rect(0, 0,
                settingsCategoryLayer.ActualWidth, settingsCategoryLayer.ActualHeight), 18, 18);
        }
        private void UpdateSettingsCategoryLabels()
        {
            string[] keys = { "BasicSectionLabel", "CustomSectionLabel", "ThemeLabel", "LanSectionLabel", "ManagementLabel" };
            foreach (string key in keys)
            {
                Button entry;
                if (!settingsCategoryButtons.TryGetValue(key, out entry)) continue;
                ((TextBlock)((Grid)entry.Content).Children[0]).Text = Control<TextBlock>(key).Text;
            }
            if (currentSettingsCategory != null) settingsCategoryTitle.Text = Control<TextBlock>(currentSettingsCategory).Text;
        }
        internal void OpenSettingsCategory(string key)
        {
            RefreshProductivitySettings();
            if (featureReady && key == "ThemeLabel") key = "CustomSectionLabel";
            StackPanel content;
            if (!settingsCategoryContents.TryGetValue(key, out content)) return;
            ++settingsCategoryAnimationVersion;
            if (currentSettingsCategory != null) settingsCategoryScroll.Content = null;
            currentSettingsCategory = key; settingsCategoryScroll.Content = content;
            settingsCategoryTitle.Text = Control<TextBlock>(key).Text;
            SizeSettingsCategory();
            bool wasHidden = settingsCategoryLayer.Visibility != Visibility.Visible;
            double currentOpacity = wasHidden ? 0 : settingsCategoryLayer.Opacity;
            settingsCategoryLayer.BeginAnimation(UIElement.OpacityProperty, null);
            bool motion = Window.IsVisible && SystemParameters.ClientAreaAnimation;
            settingsCategoryLayer.Opacity = motion ? currentOpacity : 1;
            settingsCategoryLayer.Visibility = Visibility.Visible;
            TranslateTransform slide = settingsCategorySurface.RenderTransform as TranslateTransform;
            if (slide == null) { slide = new TranslateTransform(); settingsCategorySurface.RenderTransform = slide; }
            slide.BeginAnimation(TranslateTransform.YProperty, null);
            slide.Y = wasHidden && motion ? 8 : slide.Y;
            if (motion)
            {
                UiAnimation.CacheFor(settingsCategorySurface, 250);
                settingsCategoryLayer.Opacity = 1;
                settingsCategoryLayer.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(currentOpacity, 1, TimeSpan.FromMilliseconds(180))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                slide.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(slide.Y, 0, TimeSpan.FromMilliseconds(210))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
            else slide.Y = 0;
            settingsCategoryScroll.ScrollToHome();
        }
        private void CloseSettingsCategory(bool animate = true)
        {
            if (!SettingsCategoryOpen) return;
            int version = ++settingsCategoryAnimationVersion;
            if (!animate || !Window.IsVisible || !SystemParameters.ClientAreaAnimation) { FinishSettingsCategoryClose(version); return; }
            UiAnimation.CacheFor(settingsCategorySurface, 190);
            double currentOpacity = settingsCategoryLayer.Opacity;
            DoubleAnimation fade = new DoubleAnimation(currentOpacity, 0, TimeSpan.FromMilliseconds(150))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            fade.Completed += delegate { if (version == settingsCategoryAnimationVersion) FinishSettingsCategoryClose(version); };
            settingsCategoryLayer.BeginAnimation(UIElement.OpacityProperty, fade);
            TranslateTransform slide = settingsCategorySurface.RenderTransform as TranslateTransform;
            if (slide != null) slide.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(slide.Y, 8, TimeSpan.FromMilliseconds(150)));
        }
        private void FinishSettingsCategoryClose(int version)
        {
            if (version != settingsCategoryAnimationVersion) return;
            UiAnimation.Release(settingsCategorySurface);
            settingsCategoryLayer.BeginAnimation(UIElement.OpacityProperty, null);
            settingsCategoryLayer.Opacity = 1;
            settingsCategoryLayer.Visibility = Visibility.Collapsed;
            TranslateTransform slide = settingsCategorySurface.RenderTransform as TranslateTransform;
            if (slide != null) { slide.BeginAnimation(TranslateTransform.YProperty, null); slide.Y = 0; }
            settingsCategoryScroll.Content = null; currentSettingsCategory = null;
        }
        public bool SettingsOpen
        {
            get { return Control<FrameworkElement>("SettingsPanel").Visibility == Visibility.Visible; }
            set
            {
                Control<FrameworkElement>("SettingsPanel").Visibility = value ? Visibility.Visible : Visibility.Collapsed;
                Control<FrameworkElement>("MainPanel").Visibility = value ? Visibility.Collapsed : Visibility.Visible;
                Control<FrameworkElement>("GroupPanel").Visibility = Visibility.Collapsed;
                if (value) Control<FrameworkElement>("ReminderPanel").Visibility = Visibility.Collapsed;
                if (value) { RefreshSettings(true); AnimateIn(Control<FrameworkElement>("SettingsPanel")); Control<Button>("SettingsBack").Focus(); }
                else { CloseSettingsCategory(false); AnimateIn(Control<FrameworkElement>("MainPanel")); }
            }
        }
        private void CloseSettings() { SettingsOpen = false; Input.Focus(); }
        public bool EditOpen { get { return Control<FrameworkElement>("EditPanel").Visibility == Visibility.Visible; } }
        internal void OpenEdit(TodoItem item)
        {
            if (item == null || item.Deleted) return;
            editingItem = item;
            editInput.Text = item.Text;
            Control<FrameworkElement>("MainPanel").Visibility = Visibility.Collapsed;
            Control<FrameworkElement>("SettingsPanel").Visibility = Visibility.Collapsed;
            Control<FrameworkElement>("ReminderPanel").Visibility = Visibility.Collapsed;
            Control<FrameworkElement>("EditPanel").Visibility = Visibility.Visible;
            AnimateIn(Control<FrameworkElement>("EditPanel"));
            UpdateContentScale();
            Window.Dispatcher.BeginInvoke(new Action(delegate { editInput.Focus(); editInput.SelectAll(); }));
        }
        private void CloseEdit()
        {
            closingEdit = true;
            Control<FrameworkElement>("EditPanel").Visibility = Visibility.Collapsed;
            Control<FrameworkElement>("MainPanel").Visibility = Visibility.Visible;
            AnimateIn(Control<FrameworkElement>("MainPanel"));
            editingItem = null; Input.Focus(); UpdateContentScale();
            closingEdit = false; suppressEditLostFocus = false;
        }
        private void SaveEdit()
        {
            if (editingItem == null || String.IsNullOrWhiteSpace(editInput.Text)) return;
            editingItem.Text = editInput.Text.Trim();
            NotifyChanged(); Refresh(); CloseEdit();
        }
        private void RefreshSettings(bool refreshExternal = false)
        {
            suppressSettingsEvents = true;
            try
            {
                Control<CheckBox>("DesktopToggle").IsChecked = State.DesktopMode && !forceWindowed;
                Control<CheckBox>("DesktopToggle").IsEnabled = !forceWindowed;
                Control<CheckBox>("LockToggle").IsChecked = State.Locked;
                if (!Control<TextBox>("ShowHotkeyInput").IsKeyboardFocusWithin) Control<TextBox>("ShowHotkeyInput").Text = State.ShowHotkey;
                if (!Control<TextBox>("MiniHotkeyInput").IsKeyboardFocusWithin) Control<TextBox>("MiniHotkeyInput").Text = State.MiniHotkey;
                Control<CheckBox>("ShowTrayToggle").IsChecked = !State.HideTray;
                Control<CheckBox>("ShowMinimizeToggle").IsChecked = State.ShowMinimizeButton;
                Control<CheckBox>("ShowCloseToggle").IsChecked = State.ShowCloseButton;
                Control<CheckBox>("ShowCompactToggle").IsChecked = State.ShowCompactButton;
                Control<CheckBox>("MiniAlwaysOnTopToggle").IsChecked = State.MiniAlwaysOnTop;
                titleSettingInput.IsEnabled = true;
                Control<Button>("TitleSaveButton").IsEnabled = !String.IsNullOrWhiteSpace(titleSettingInput.Text);
                if (!titleSettingInput.IsKeyboardFocusWithin) titleSettingInput.Text = State.Title;
                Control<CheckBox>("DarkToggle").IsChecked = State.DarkMode;
                Control<FrameworkElement>("WindowOpacityPanel").Visibility = Visibility.Visible;
                Control<Slider>("WindowOpacitySlider").Value = State.WindowOpacityPercent;
                Control<TextBlock>("WindowOpacityValue").Text = State.WindowOpacityPercent + "%";
                Control<Slider>("MiniOpacitySlider").Value = State.MiniIconOpacityPercent;
                Control<TextBlock>("MiniOpacityValue").Text = State.MiniIconOpacityPercent + "%";
                Control<ComboBox>("LanguageSelect").SelectedIndex = IsEnglish ? 1 : 0;
                Control<CheckBox>("ShowTimesToggle").IsChecked = !State.HideItemTimes;
                Control<CheckBox>("ShowDateToggle").IsChecked = State.ShowItemDates;
                Control<CheckBox>("ShowDateToggle").IsEnabled = !State.HideItemTimes;
                Control<CheckBox>("DisableGroupsToggle").IsChecked = State.DisableGroups;
                Control<CheckBox>("LanSharingToggle").IsChecked = State.LanSharingEnabled;
                TextBox lanName = Control<TextBox>("LanDisplayNameInput");
                if (!lanName.IsKeyboardFocusWithin) lanName.Text = State.LanDisplayName;
                Control<TextBox>("LanAccessCodeInput").Text = State.LanAccessCode;
                Control<TextBlock>("LanStatus").Text = State.LanSharingEnabled ? L("已开启；首次启用时请允许 Windows 专用网络访问", "Enabled; allow private network access if Windows asks") : L("共享默认关闭，待办仍只保存在本机", "Sharing is off; tasks remain local");
                Control<TextBlock>("SettingsStatus").Text = State.LanSharingEnabled ? L("设置自动保存 · 已开启局域网共享", "Settings save automatically · LAN sharing enabled") : L("设置自动保存 · 数据只在本机", "Settings save automatically · local data only");
                foreach (ThemePalette palette in ThemeCatalog.All)
                    Control<RadioButton>("Theme" + Char.ToUpperInvariant(palette.Id[0]) + palette.Id.Substring(1)).IsChecked = State.ThemeId == palette.Id;
                if (refreshExternal || !startupKnown)
                {
                    try { cachedStartup = GetStartup != null && GetStartup(); }
                    catch { cachedStartup = false; }
                    startupKnown = true;
                }
                Control<CheckBox>("StartupToggle").IsChecked = cachedStartup;
                if (refreshExternal) secondaryMonitorCache = null;
                RefreshSecondarySettings();
                RefreshProductivitySettings();
            }
            finally { suppressSettingsEvents = false; }
        }
        private void RefreshSecondarySettings()
        {
            Control<CheckBox>("SecondaryStartupToggle").IsChecked = State.StartOnSecondaryOnly;
            ComboBox selector = Control<ComboBox>("SecondaryDisplaySelect");
            List<Native.MonitorArea> monitors = secondaryMonitorCache ?? (secondaryMonitorCache = SecondaryStartup.AvailableMonitors().Where(m => !m.IsPrimary)
                .OrderBy(m => m.Bounds.Left).ThenBy(m => m.Bounds.Top).ToList());
            Native.MonitorArea selected = SecondaryStartup.Select(monitors, State.SecondaryDeviceName);
            string signature = State.Language + ":" + State.SecondaryDeviceName + ":" + String.Join("|", monitors.Select(m => m.DeviceName + ":" + m.Bounds));
            if (signature != secondarySelectorSignature)
            {
            secondarySelectorSignature = signature; selector.Items.Clear();
            int selectedIndex = -1;
            for (int i = 0; i < monitors.Count; i++)
            {
                Native.MonitorArea monitor = monitors[i];
                string label = String.IsNullOrEmpty(monitor.DeviceName) ? L("副屏 ", "Display ") + (i + 1) : monitor.DeviceName.Replace("\\\\.\\", "");
                selector.Items.Add(new ComboBoxItem { Content = label + " · " + monitor.Bounds.Width + "×" + monitor.Bounds.Height,
                    Tag = monitor.DeviceName });
                if (selected == monitor) selectedIndex = i;
            }
            if (selectedIndex < 0 && !String.IsNullOrEmpty(State.SecondaryDeviceName))
            {
                selectedIndex = selector.Items.Count;
                selector.Items.Add(new ComboBoxItem { Content = L("上次选择的副屏（未连接）", "Selected display (disconnected)"),
                    Tag = State.SecondaryDeviceName });
            }
            if (selectedIndex < 0 && selector.Items.Count == 0)
            {
                selectedIndex = 0;
                selector.Items.Add(new ComboBoxItem { Content = L("暂无副屏", "No secondary display"), Tag = "" });
            }
            selector.SelectedIndex = selectedIndex;
            }
            selector.IsEnabled = monitors.Count > 0;
            bool autoStart = Control<CheckBox>("StartupToggle").IsChecked == true;
            Control<TextBlock>("SecondaryStartupStatus").Text = !State.StartOnSecondaryOnly
                ? L("默认关闭；开启后会同时打开“开机自启”", "Off by default; enabling also turns on Windows startup")
                : !autoStart
                    ? L("副屏条件已开启；请同时打开“开机自启”", "Secondary condition is on; also enable Windows startup")
                    : selected == null
                        ? L("开机时等待所选副屏连接；手动启动仍可用", "Waits for the selected display at login; manual launch still works")
                        : L("下次开机在此副屏恢复上次位置", "Next login restores the last position on this display");
        }
        public void SettingsMessage(string message)
        {
            statusMessage = message;
            statusExpiresAtUtc = DateTime.UtcNow.AddSeconds(10);
            UpdateSettingsMessage(); statusTimer.Start();
        }
        internal void SaveShortcuts()
        {
            string showKey = Control<TextBox>("ShowHotkeyInput").Text.Trim(), miniKey = Control<TextBox>("MiniHotkeyInput").Text.Trim();
            uint modifiers, key;
            if (!GlobalHotkeys.Parse(showKey, out modifiers, out key) || !GlobalHotkeys.Parse(miniKey, out modifiers, out key))
            { Control<TextBlock>("HotkeyStatus").Text = L("快捷键格式无效，例如 Ctrl+Alt+D；留空关闭", "Invalid shortcut; try Ctrl+Alt+D, or leave blank to disable."); return; }
            string error = ConfigureShortcutsRequested == null ? null : ConfigureShortcutsRequested(showKey, miniKey);
            if (error != null) { Control<TextBlock>("HotkeyStatus").Text = error; return; }
            State.ShowHotkey = showKey; State.MiniHotkey = miniKey; NotifyChanged();
            Control<TextBlock>("HotkeyStatus").Text = L("快捷键已保存", "Shortcuts saved");
        }
        private void UpdateSettingsMessage()
        {
            int seconds = Math.Max(0, (int)Math.Ceiling((statusExpiresAtUtc - DateTime.UtcNow).TotalSeconds));
            if (seconds == 0)
            {
                statusTimer.Stop(); statusMessage = null;
                Control<TextBlock>("SettingsStatus").Text = State.LanSharingEnabled ?
                    L("设置自动保存 · 已开启局域网共享", "Settings save automatically · LAN sharing enabled") :
                    L("设置自动保存 · 数据只在本机", "Settings save automatically · local data only");
                return;
            }
            Control<TextBlock>("SettingsStatus").Text = statusMessage + " · " + seconds + "s";
        }
        internal void AdvanceSettingsMessageForTest(int seconds)
        { statusExpiresAtUtc = statusExpiresAtUtc.AddSeconds(-seconds); UpdateSettingsMessage(); }
        internal void RefreshSettingsForTray() { RefreshSettings(true); }
        internal bool OverlayOpen { get { return Control<FrameworkElement>("OverlayLayer").Visibility == Visibility.Visible; } }
        private void PrepareOverlaySurface(bool fullPage)
        {
            if (editingGroup != null) SaveGroupNameDraft(); editingGroup = null;
            if (groupNameTimer != null) groupNameTimer.Stop();
            Control<FrameworkElement>("GroupNameOverlay").Visibility = Visibility.Collapsed;
            Grid layer = Control<Grid>("OverlayLayer");
            Border surface = Control<Border>("OverlaySurface");
            if (fullPage)
            {
                layer.Background = Brushes.Transparent;
                surface.HorizontalAlignment = HorizontalAlignment.Stretch;
                surface.VerticalAlignment = VerticalAlignment.Stretch;
                surface.MaxWidth = Double.PositiveInfinity; surface.MinWidth = 0; surface.MaxHeight = Double.PositiveInfinity;
                surface.Margin = new Thickness(0); surface.Padding = new Thickness(22, 20, 22, 16);
                surface.BorderThickness = new Thickness(0); surface.CornerRadius = new CornerRadius(18);
            }
            else
            {
                layer.Background = new SolidColorBrush(Color.FromArgb(0x66, 0x0B, 0x17, 0x20));
                surface.HorizontalAlignment = HorizontalAlignment.Center;
                surface.VerticalAlignment = VerticalAlignment.Center;
                surface.MaxWidth = 330; surface.MinWidth = 270; surface.MaxHeight = 440;
                surface.Margin = new Thickness(14); surface.Padding = new Thickness(20);
                surface.BorderThickness = new Thickness(1); surface.CornerRadius = new CornerRadius(16);
            }
        }
        internal void ShowConfirmation(string title, string body, string acceptLabel, Action action)
        {
            HideToast();
            CloseSettingsCategory(false);
            noticeTimer.Stop(); noticeExpiresAtUtc = DateTime.MinValue;
            PrepareOverlaySurface(false);
            pendingConfirmation = action;
            Control<TextBlock>("ConfirmTitle").Text = title;
            Control<TextBlock>("ConfirmBody").Text = body;
            Control<Button>("ConfirmAcceptButton").Content = acceptLabel;
            Control<Button>("ConfirmCancelButton").Visibility = Visibility.Visible;
            Control<FrameworkElement>("PositionOverlay").Visibility = Visibility.Collapsed;
            Control<FrameworkElement>("ConfirmOverlay").Visibility = Visibility.Visible;
            Control<FrameworkElement>("OverlayLayer").Visibility = Visibility.Visible;
            AnimateIn(Control<FrameworkElement>("OverlaySurface"));
            Control<Button>("ConfirmCancelButton").Focus();
        }
        internal void ShowNotice(string title, string body)
        {
            HideToast();
            CloseSettingsCategory(false);
            PrepareOverlaySurface(false);
            pendingConfirmation = null;
            Control<TextBlock>("ConfirmTitle").Text = title;
            Control<TextBlock>("ConfirmBody").Text = body;
            noticeExpiresAtUtc = DateTime.UtcNow.AddSeconds(10);
            UpdateNoticeCountdown();
            noticeTimer.Start();
            Control<Button>("ConfirmCancelButton").Visibility = Visibility.Collapsed;
            Control<FrameworkElement>("PositionOverlay").Visibility = Visibility.Collapsed;
            Control<FrameworkElement>("ConfirmOverlay").Visibility = Visibility.Visible;
            Control<FrameworkElement>("OverlayLayer").Visibility = Visibility.Visible;
            AnimateIn(Control<FrameworkElement>("OverlaySurface"));
            Control<Button>("ConfirmAcceptButton").Focus();
        }
        private void UpdateNoticeCountdown()
        {
            int seconds = Math.Max(0, (int)Math.Ceiling((noticeExpiresAtUtc - DateTime.UtcNow).TotalSeconds));
            if (seconds == 0) { CloseOverlay(); return; }
            Control<Button>("ConfirmAcceptButton").Content = L("知道了", "Got it") + " · " + seconds + "s";
        }
        internal void AdvanceNoticeForTest(int seconds)
        { noticeExpiresAtUtc = noticeExpiresAtUtc.AddSeconds(-seconds); UpdateNoticeCountdown(); }
        internal void CloseOverlay()
        {
            if (editingGroup != null) SaveGroupNameDraft(); editingGroup = null;
            if (groupNameTimer != null) groupNameTimer.Stop();
            Control<FrameworkElement>("GroupNameOverlay").Visibility = Visibility.Collapsed;
            noticeTimer.Stop(); noticeExpiresAtUtc = DateTime.MinValue;
            pendingConfirmation = null;
            Control<FrameworkElement>("OverlayLayer").Visibility = Visibility.Collapsed;
            Control<FrameworkElement>("ConfirmOverlay").Visibility = Visibility.Collapsed;
            Control<FrameworkElement>("PositionOverlay").Visibility = Visibility.Collapsed;
        }
        internal void OpenPositionOverlay()
        {
            CloseSettingsCategory(false);
            noticeTimer.Stop(); noticeExpiresAtUtc = DateTime.MinValue;
            PrepareOverlaySurface(true);
            SelectPositionOptions(State.DefaultCorner, State.DefaultScalePercent);
            Control<FrameworkElement>("ConfirmOverlay").Visibility = Visibility.Collapsed;
            Control<FrameworkElement>("PositionOverlay").Visibility = Visibility.Visible;
            Control<FrameworkElement>("OverlayLayer").Visibility = Visibility.Visible;
            AnimateIn(Control<FrameworkElement>("PositionOverlay"));
            Control<Button>("PositionCloseButton").Focus();
        }
        private void SelectPositionOptions(string corner, int scale)
        {
            Control<RadioButton>("CornerTopLeft").IsChecked = corner == "TopLeft";
            Control<RadioButton>("CornerTopRight").IsChecked = corner == "TopRight";
            Control<RadioButton>("CornerBottomLeft").IsChecked = corner == "BottomLeft";
            Control<RadioButton>("CornerBottomRight").IsChecked = corner != "TopLeft" && corner != "TopRight" && corner != "BottomLeft";
            Control<RadioButton>("Scale75").IsChecked = scale == 75;
            Control<RadioButton>("Scale100").IsChecked = scale == 100;
            Control<RadioButton>("Scale125").IsChecked = scale == 125;
            Control<RadioButton>("Scale150").IsChecked = scale == 150;
            Control<RadioButton>("Scale175").IsChecked = scale == 175;
            Control<RadioButton>("Scale200").IsChecked = scale == 200;
            Control<RadioButton>("Scale225").IsChecked = scale == 225;
            Control<RadioButton>("Scale250").IsChecked = scale == 250;
            RefreshPositionMarkers();
        }
        private void RefreshPositionMarkers()
        {
            bool topLeft = Control<RadioButton>("CornerTopLeft").IsChecked == true;
            bool topRight = Control<RadioButton>("CornerTopRight").IsChecked == true;
            bool bottomLeft = Control<RadioButton>("CornerBottomLeft").IsChecked == true;
            SetPositionMarker("PositionMarkerTopLeft", topLeft);
            SetPositionMarker("PositionMarkerTopRight", topRight);
            SetPositionMarker("PositionMarkerBottomLeft", bottomLeft);
            SetPositionMarker("PositionMarkerBottomRight", !topLeft && !topRight && !bottomLeft);
        }
        private void SetPositionMarker(string name, bool selected)
        {
            System.Windows.Shapes.Ellipse marker = Control<System.Windows.Shapes.Ellipse>(name);
            marker.Fill = selected ? Brush("AccentBrush") : Brushes.White;
            marker.Stroke = Brush("AccentBrush");
        }
        private void SavePositionOptions()
        {
            string corner = Control<RadioButton>("CornerTopLeft").IsChecked == true ? "TopLeft" :
                Control<RadioButton>("CornerTopRight").IsChecked == true ? "TopRight" :
                Control<RadioButton>("CornerBottomLeft").IsChecked == true ? "BottomLeft" : "BottomRight";
            int percent = Control<RadioButton>("Scale75").IsChecked == true ? 75 :
                Control<RadioButton>("Scale125").IsChecked == true ? 125 :
                Control<RadioButton>("Scale150").IsChecked == true ? 150 :
                Control<RadioButton>("Scale175").IsChecked == true ? 175 :
                Control<RadioButton>("Scale200").IsChecked == true ? 200 :
                Control<RadioButton>("Scale225").IsChecked == true ? 225 :
                Control<RadioButton>("Scale250").IsChecked == true ? 250 : 100;
            State.DefaultCorner = corner; State.DefaultScalePercent = percent;
            NotifyChanged(); CloseOverlay(); RefreshSettings();
        }
        internal void ApplyPositionPreset(string corner, int percent)
        {
            double widthDip = Math.Max(320, Math.Min(900, 360 * percent / 100.0));
            double heightDip = Math.Max(360, Math.Min(1300, 520 * percent / 100.0));
            State.Width = widthDip; State.Height = heightDip;
            if (!IsReady || !Native.IsWindow(Desktop.Handle))
            { Window.Width = widthDip; Window.Height = heightDip; State.PositionSet = false; NotifyChanged(); return; }
            Native.Rect current; Native.PhysicalWindowRect(Desktop.Handle, out current);
            System.Drawing.Rectangle work = Native.MonitorFor(current).Work;
            double scale = LogicalScreenScale;
            double fit = Math.Min(1, Math.Min(work.Width / (widthDip * scale), work.Height / (heightDip * scale)));
            int width = Math.Max(1, (int)Math.Round(widthDip * scale * fit));
            int height = Math.Max(1, (int)Math.Round(heightDip * scale * fit));
            int margin = (int)Math.Round(32 * scale);
            int x = corner == "TopLeft" || corner == "BottomLeft" ? work.Left + margin : work.Right - width - margin;
            int y = corner == "TopLeft" || corner == "TopRight" ? work.Top + margin : work.Bottom - height - margin;
            x = Math.Max(work.Left, Math.Min(work.Right - width, x)); y = Math.Max(work.Top, Math.Min(work.Bottom - height, y));
            Desktop.Move(x, y, width, height); RememberGeometry(); NotifyChanged();
        }
        internal void RefreshImportedState()
        {
            completedVisibleLimit = CompletedPageSize;
            State.Validate(); ApplyTheme(); RefreshTitle(); Refresh(); RefreshSettings();
        }
        public void RestoreSession(TodoWindow previous)
        {
            Input.Text = previous.Input.Text;
            if (previous.SearchOpen)
            {
                SetSearchOpen(true); completedBeforeSearch = previous.completedBeforeSearch;
                Control<TextBox>("SearchInput").Text = previous.Control<TextBox>("SearchInput").Text;
                ApplySearch();
            }
            foreach (TodoItem item in previous.Removed.Reverse()) Removed.Push(item);
            foreach (List<TodoItem> group in previous.undoGroups.Reverse()) undoGroups.Push(new List<TodoItem>(group));
            undoLabel = previous.undoLabel;
            if (undoGroups.Count > 0)
            {
                undoExpiresAtUtc = previous.undoExpiresAtUtc;
                TimeSpan remaining = undoExpiresAtUtc - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero) ExpireUndo();
                else { undoTimer.Interval = TimeSpan.FromMilliseconds(250); undoTimer.Start(); }
            }
            completedVisibleLimit = previous.completedVisibleLimit;
            Control<Expander>("CompletedSection").IsExpanded = previous.Control<Expander>("CompletedSection").IsExpanded;
            Refresh();
            if (previous.EditOpen && previous.editingItem != null)
            {
                TodoItem item = State.Items.FirstOrDefault(candidate => candidate.Id == previous.editingItem.Id);
                if (item != null) { OpenEdit(item); editInput.Text = previous.editInput.Text; UpdateContentScale(); }
            }
            else if (previous.ReminderOpen && previous.reminderEditingItem != null)
            {
                TodoItem item = State.Items.FirstOrDefault(candidate => candidate.Id == previous.reminderEditingItem.Id);
                if (item != null)
                {
                    OpenItemReminder(item);
                    Control<CheckBox>("ItemIntervalToggle").IsChecked = previous.Control<CheckBox>("ItemIntervalToggle").IsChecked;
                    Control<CheckBox>("ItemDailyToggle").IsChecked = previous.Control<CheckBox>("ItemDailyToggle").IsChecked;
                    itemReminderHours.Text = previous.itemReminderHours.Text; itemReminderMinutes.Text = previous.itemReminderMinutes.Text;
                    itemReminderDailyTime.Text = previous.itemReminderDailyTime.Text; RefreshItemReminderVisibility();
                }
            }
            else if (previous.GroupOpen) OpenGroups();
            else
            {
                SettingsOpen = previous.SettingsOpen;
                if (!SettingsOpen && previous.TitleEditing) { BeginTitleEdit(); titleEditor.Text = previous.titleEditor.Text; }
            }
        }
        private void Edit(TodoItem item)
        {
            OpenEdit(item);
        }
        internal bool TitleEditing { get { return Control<FrameworkElement>("TitleEditSurface").Visibility == Visibility.Visible; } }
        private void RefreshTitle()
        {
            State.Title = String.IsNullOrWhiteSpace(State.Title) || State.Title.Trim() == "待办" ? "DeskTodo" : State.Title.Trim();
            Control<TextBlock>("TitleLabel").Text = State.Title;
        }
        private void SaveTitleFromSettings()
        {
            if (String.IsNullOrWhiteSpace(titleSettingInput.Text)) return;
            State.Title = titleSettingInput.Text.Trim();
            if (State.Title.Length > 12) State.Title = State.Title.Substring(0, 12);
            NotifyChanged(); RefreshTitle(); titleSettingInput.Text = State.Title; SettingsMessage("软件名称已更新");
        }
        private void SaveLanDisplayName()
        {
            TextBox input = Control<TextBox>("LanDisplayNameInput");
            string value = (input.Text ?? "").Trim();
            if (value.Length == 0) { input.Text = State.LanDisplayName; return; }
            if (value.Length > 24) value = value.Substring(0, 24);
            if (value == State.LanDisplayName) return;
            State.LanDisplayName = value; NotifyChanged(); RefreshSettings();
        }
        internal void BeginTitleEdit()
        {
            // Header editing was intentionally removed: the whole top band is now a
            // predictable drag target. The name is edited only from Settings.
        }
        internal void EndTitleEdit(bool save)
        {
            if (!TitleEditing) return;
            Control<FrameworkElement>("TitleEditSurface").Visibility = Visibility.Collapsed;
            Control<FrameworkElement>("TitleLabel").Visibility = Visibility.Visible;
            if (save && !String.IsNullOrWhiteSpace(titleEditor.Text))
            {
                State.Title = titleEditor.Text.Trim();
                if (State.Title.Length > 12) State.Title = State.Title.Substring(0, 12);
                NotifyChanged();
            }
            RefreshTitle();
        }
        internal bool ReminderOpen { get { return Control<FrameworkElement>("ReminderPanel").Visibility == Visibility.Visible; } }
        internal void OpenItemReminder(TodoItem item)
        {
            if (item == null || item.Deleted) return;
            reminderEditingItem = item;
            foreach (string control in new[] { "ItemIntervalToggle", "ItemDailyToggle", "ItemReminderHours", "ItemReminderMinutes", "ItemReminderDailyTime", "HoursUpButton", "HoursDownButton", "MinutesUpButton", "MinutesDownButton", "DailyTimeUpButton", "DailyTimeDownButton", "ReminderClearButton", "ReminderSaveButton" })
                Control<UIElement>(control).IsEnabled = !item.ReminderManaged;
            Control<TextBlock>("ReminderItemText").Text = item.Text;
            Control<CheckBox>("ItemIntervalToggle").IsChecked = item.ReminderIntervalMinutes > 0;
            Control<CheckBox>("ItemDailyToggle").IsChecked = item.DailyReminderMinutesPlusOne > 0;
            int interval = item.ReminderIntervalMinutes > 0 ? item.ReminderIntervalMinutes : 60;
            itemReminderHours.Text = (interval / 60).ToString(); itemReminderMinutes.Text = (interval % 60).ToString();
            int daily = item.DailyReminderMinutesPlusOne > 0 ? item.DailyReminderMinutesPlusOne - 1 : 20 * 60;
            itemReminderDailyTime.Text = (daily / 60).ToString("00") + ":" + (daily % 60).ToString("00");
            Control<TextBlock>("ReminderEditorMessage").Text = item.IsCompleted ? "已完成事项暂停提醒，恢复未完成后继续" : "两种提醒可以同时开启";
            if (item.ReminderManaged) Control<TextBlock>("ReminderEditorMessage").Text = "此提醒由管理员管理，完成待办后自动停止；如需修改请联系管理员";
            Control<FrameworkElement>("MainPanel").Visibility = Visibility.Collapsed;
            Control<FrameworkElement>("SettingsPanel").Visibility = Visibility.Collapsed;
            Control<FrameworkElement>("EditPanel").Visibility = Visibility.Collapsed;
            Control<FrameworkElement>("ReminderPanel").Visibility = Visibility.Visible;
            AnimateIn(Control<FrameworkElement>("ReminderPanel"));
            RefreshItemReminderVisibility();
        }
        private void RefreshItemReminderVisibility()
        {
            Control<FrameworkElement>("ItemIntervalPanel").Visibility = Control<CheckBox>("ItemIntervalToggle").IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            Control<FrameworkElement>("ItemDailyPanel").Visibility = Control<CheckBox>("ItemDailyToggle").IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }
        private static int NumberOrZero(TextBox input)
        {
            int value;
            return Int32.TryParse((input.Text ?? "").Trim(), out value) ? value : 0;
        }
        private static void StepNumber(TextBox input, int change, int minimum, int maximum)
        {
            int value = NumberOrZero(input);
            input.Text = Math.Max(minimum, Math.Min(maximum, value + change)).ToString();
            input.CaretIndex = input.Text.Length;
        }
        private void StepDailyTime(int change)
        {
            DateTime parsed;
            if (!DateTime.TryParseExact((itemReminderDailyTime.Text ?? "").Trim(), new[] { "H:mm", "HH:mm" },
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out parsed)) parsed = DateTime.Today.AddHours(20);
            int minutes = (parsed.Hour * 60 + parsed.Minute + change + 1440) % 1440;
            itemReminderDailyTime.Text = (minutes / 60).ToString("00") + ":" + (minutes % 60).ToString("00");
            itemReminderDailyTime.CaretIndex = itemReminderDailyTime.Text.Length;
        }
        private void CloseReminder()
        {
            Control<FrameworkElement>("ReminderPanel").Visibility = Visibility.Collapsed;
            Control<FrameworkElement>("MainPanel").Visibility = Visibility.Visible;
            AnimateIn(Control<FrameworkElement>("MainPanel"));
            reminderEditingItem = null; Input.Focus();
        }
        internal void SaveItemReminder()
        {
            if (reminderEditingItem == null || reminderEditingItem.ReminderManaged) return;
            int interval = 0, daily = 0, hours = 0, minutes = 0;
            if (Control<CheckBox>("ItemIntervalToggle").IsChecked == true)
            {
                string hoursText = itemReminderHours.Text.Trim(), minutesText = itemReminderMinutes.Text.Trim();
                if ((hoursText.Length > 0 && !Int32.TryParse(hoursText, out hours)) || (minutesText.Length > 0 && !Int32.TryParse(minutesText, out minutes)) ||
                    hours < 0 || hours > 168 || minutes < 0 || minutes > 59 || hours * 60 + minutes < 1 || hours * 60 + minutes > 10080)
                { Control<TextBlock>("ReminderEditorMessage").Text = "间隔须为 1 分钟至 168 小时，分钟填写 0–59"; return; }
                interval = hours * 60 + minutes;
            }
            if (Control<CheckBox>("ItemDailyToggle").IsChecked == true)
            {
                DateTime parsed;
                if (!DateTime.TryParseExact(itemReminderDailyTime.Text.Trim(), new[] { "H:mm", "HH:mm" },
                    System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out parsed))
                { Control<TextBlock>("ReminderEditorMessage").Text = "每天的时间请按 20:30 这样的格式输入"; return; }
                daily = parsed.Hour * 60 + parsed.Minute + 1;
            }
            if (interval != reminderEditingItem.ReminderIntervalMinutes || (interval > 0 && reminderEditingItem.LastIntervalReminderAt == 0))
                reminderEditingItem.LastIntervalReminderAt = interval > 0 ? DateTime.UtcNow.Ticks : 0;
            if (daily != reminderEditingItem.DailyReminderMinutesPlusOne) reminderEditingItem.LastDailyReminderDate = null;
            reminderEditingItem.ReminderIntervalMinutes = interval; reminderEditingItem.DailyReminderMinutesPlusOne = daily;
            reminderEditingItem.ReminderSnoozeUntil = 0;
            NotifyChanged(); Refresh(); CloseReminder();
        }
        private void ClearItemReminder()
        {
            if (reminderEditingItem == null || reminderEditingItem.ReminderManaged) return;
            reminderEditingItem.ReminderIntervalMinutes = 0; reminderEditingItem.DailyReminderMinutesPlusOne = 0;
            reminderEditingItem.LastIntervalReminderAt = 0; reminderEditingItem.LastDailyReminderDate = null; reminderEditingItem.ReminderSnoozeUntil = 0;
            NotifyChanged(); Refresh(); CloseReminder();
        }
    }
}
