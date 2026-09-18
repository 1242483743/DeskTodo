using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DesktopTodo
{
    public sealed class TodoWindow
    {
        public readonly Window Window;
        public readonly AppState State;
        public readonly DesktopAttachment Desktop = new DesktopAttachment();
        public Action Changed, ExitRequested, HideRequested, ModeChanged, ExportRequested, ImportRequested, RebuildRequested;
        public Func<bool> GetStartup;
        public Action<bool> SetStartup;
        public bool IsSaved = true;
        public string SaveError;
        public readonly Stack<TodoItem> Removed = new Stack<TodoItem>();
        private readonly Stack<List<TodoItem>> undoGroups = new Stack<List<TodoItem>>();
        private string undoLabel = "已移除事项";
        private readonly DispatcherTimer undoTimer;
        private DateTime undoExpiresAtUtc;
        public readonly TextBox Input;
        private readonly TextBox editInput;
        private TodoItem editingItem;
        private TodoItem reminderEditingItem;
        private readonly TextBox itemReminderHours, itemReminderMinutes, itemReminderDailyTime, titleEditor, titleSettingInput;
        private Action pendingConfirmation;
        private bool closingEdit, suppressEditLostFocus;
        private bool dragging;
        private Native.Point dragStart;
        private Native.Rect dragRect, dragTarget;
        private IntPtr dragMonitor;
        private bool dragMoved, resizeMoved, previewAttempted;
        private GesturePreview movePreview;
        private double dragCardOpacity, dragThumbOpacity;
        private bool movePreviewHidCard;
        private bool resizing;
        private Native.Point resizeStart;
        private Native.Rect resizeRect, resizeTarget;
        private System.Drawing.Rectangle resizeWork;
        private double resizeScale;
        public bool IsReady;
        private bool forceWindowed;
        private bool attachmentFallback;
        private double contentScale = 1;
        internal double ContentScale { get { return contentScale; } }
        internal bool IsAttachmentFallback { get { return attachmentFallback; } }
        public T Control<T>(string name) where T : class { return Window.FindName(name) as T; }

        public TodoWindow(AppState state)
        {
            State = state;
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("DesktopTodo.MainWindow.xaml"))
                Window = (Window)XamlReader.Load(stream);
            Control<Image>("HeaderIcon").Source = UiAssets.InternalIcon;
            Control<Button>("ConfirmCloseButton").Content = UiGlyphs.Close();
            // WPF can create focus adorners inside control templates that are not known
            // until runtime. Clear them before keyboard focus is applied, including when
            // Alt turns keyboard cues on.
            Window.AddHandler(Keyboard.PreviewGotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(delegate(object sender, KeyboardFocusChangedEventArgs e)
            {
                Control control = e.NewFocus as Control;
                if (control != null) control.FocusVisualStyle = null;
            }), true);
            Input = Control<TextBox>("AddInput");
            editInput = Control<TextBox>("EditInput");
            titleEditor = Control<TextBox>("TitleEditor");
            titleSettingInput = Control<TextBox>("TitleSettingInput");
            itemReminderHours = Control<TextBox>("ItemReminderHours");
            itemReminderMinutes = Control<TextBox>("ItemReminderMinutes");
            itemReminderDailyTime = Control<TextBox>("ItemReminderDailyTime");
            undoTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            undoTimer.Tick += delegate { ExpireUndo(); };
            Window.Width = state.Width; Window.Height = state.Height;
            UpdateContentScale();
            ApplyTheme();
            Control<Button>("AddButton").Click += delegate { AddFromInput(); };
            Input.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Enter) { AddFromInput(); e.Handled = true; }
                if (e.Key == Key.Escape) { Input.Clear(); e.Handled = true; }
            };
            Window.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (OverlayOpen && e.Key == Key.Escape) { CloseOverlay(); e.Handled = true; return; }
                if (TitleEditing && e.Key == Key.Escape) { EndTitleEdit(false); e.Handled = true; return; }
                if (ReminderOpen && e.Key == Key.Escape) { CloseReminder(); e.Handled = true; return; }
                if (ReminderOpen && e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control) { SaveItemReminder(); e.Handled = true; return; }
                if (EditOpen && e.Key == Key.Escape) { CloseEdit(); e.Handled = true; return; }
                if (EditOpen && e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control) { SaveEdit(); e.Handled = true; return; }
                if (e.Key == Key.Escape && SettingsOpen) { CloseSettings(); e.Handled = true; return; }
                if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control && Removed.Count > 0 &&
                    (!(Keyboard.FocusedElement is TextBox) || (Keyboard.FocusedElement == Input && Input.Text.Length == 0))) { Undo(); e.Handled = true; }
            };
            Control<Button>("UndoButton").Click += delegate { Undo(); };
            Control<Button>("ClearCompletedButton").Click += delegate
            {
                int count = State.Completed.Count();
                if (count > 0) ShowConfirmation("清空已完成", "将清空 " + count + " 条已完成事项。清空后可以在主页点击“撤销”恢复整批事项。", "清空", ClearCompleted);
            };
            Control<Button>("LockButton").Click += delegate { State.Locked = !State.Locked; NotifyChanged(); RefreshStatus(); };
            Control<Button>("MinimizeButton").Click += delegate { if (HideRequested != null) HideRequested(); };
            Control<Button>("CloseButton").Click += delegate
            { ShowConfirmation("关闭 DeskTodo", "将保存当前修改并退出软件。提醒也会停止，确定要关闭吗？", "关闭", delegate { if (ExitRequested != null) ExitRequested(); }); };
            Control<Button>("MenuButton").Click += delegate { ShowMenu(); };
            Control<Button>("SettingsBack").Click += delegate { CloseSettings(); };
            Control<CheckBox>("DesktopToggle").Click += delegate { State.DesktopMode = Control<CheckBox>("DesktopToggle").IsChecked == true; NotifyChanged(); if (ModeChanged != null) ModeChanged(); };
            Control<CheckBox>("LockToggle").Click += delegate { State.Locked = Control<CheckBox>("LockToggle").IsChecked == true; NotifyChanged(); RefreshStatus(); RefreshSettings(); };
            Control<CheckBox>("ShowTrayToggle").Click += delegate
            { State.HideTray = Control<CheckBox>("ShowTrayToggle").IsChecked != true; NotifyChanged(); RefreshSettings(); };
            Control<CheckBox>("ShowMinimizeToggle").Click += delegate
            { State.ShowMinimizeButton = Control<CheckBox>("ShowMinimizeToggle").IsChecked == true; NotifyChanged(); RefreshStatus(); RefreshSettings(); };
            Control<CheckBox>("ShowCloseToggle").Click += delegate
            { State.ShowCloseButton = Control<CheckBox>("ShowCloseToggle").IsChecked == true; NotifyChanged(); RefreshStatus(); RefreshSettings(); };
            Control<Button>("TitleSaveButton").Click += delegate { SaveTitleFromSettings(); };
            titleSettingInput.TextChanged += delegate { Control<Button>("TitleSaveButton").IsEnabled = !State.Locked && !String.IsNullOrWhiteSpace(titleSettingInput.Text); };
            titleSettingInput.KeyDown += delegate(object sender, KeyEventArgs e)
            { if (e.Key == Key.Enter && !State.Locked) { SaveTitleFromSettings(); e.Handled = true; } };
            Control<CheckBox>("DarkToggle").Click += delegate { State.DarkMode = Control<CheckBox>("DarkToggle").IsChecked == true; ApplyTheme(); NotifyChanged(); Refresh(); };
            foreach (ThemePalette palette in ThemeCatalog.All)
            {
                ThemePalette selected = palette;
                RadioButton themeButton = Control<RadioButton>("Theme" + Char.ToUpperInvariant(palette.Id[0]) + palette.Id.Substring(1));
                themeButton.Tag = new SolidColorBrush((Color)ColorConverter.ConvertFromString(palette.Swatch));
                themeButton.Click += delegate
                { State.ThemeId = selected.Id; ApplyTheme(); NotifyChanged(); Refresh(); RefreshSettings(); };
            }
            Control<CheckBox>("StartupToggle").Click += delegate
            {
                try { if (SetStartup != null) SetStartup(Control<CheckBox>("StartupToggle").IsChecked == true); SettingsMessage("自启设置已更新"); }
                catch (Exception e) { ShowNotice("自启设置失败", e.Message); RefreshSettings(); }
            };
            Control<Button>("ReattachButton").Click += delegate { State.DesktopMode = true; NotifyChanged(); if (ModeChanged != null) ModeChanged(); };
            Control<Button>("ResetButton").Click += delegate
            {
                State.Locked = false;
                ApplyPositionPreset(State.DefaultCorner, State.DefaultScalePercent);
                Refresh(); RefreshSettings();
            };
            Control<Button>("PositionMenuButton").Click += delegate { OpenPositionOverlay(); };
            Control<Button>("ExportButton").Click += delegate { if (ExportRequested != null) ExportRequested(); };
            Control<Button>("ImportButton").Click += delegate { if (ImportRequested != null) ImportRequested(); };
            Control<Button>("RetryButton").Click += delegate { NotifyChanged(); ShowNotice(IsSaved ? "保存成功" : "保存失败", IsSaved ? "当前事项和设置已经保存。" : (SaveError ?? "请检查磁盘空间或文件权限。")); };
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
            { State.HideItemTimes = Control<CheckBox>("ShowTimesToggle").IsChecked != true; NotifyChanged(); Refresh(); RefreshSettings(); };
            Control<CheckBox>("ShowDateToggle").Click += delegate
            { State.ShowItemDates = Control<CheckBox>("ShowDateToggle").IsChecked == true; NotifyChanged(); Refresh(); RefreshSettings(); };
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
                Point addTop = addBar.TranslatePoint(new Point(0, 0), dragSurface);
                if (!IsTopDragPoint(e.GetPosition(dragSurface).Y, addTop.Y)) return;
                Native.GetPhysicalCursorPos(out dragStart); Native.PhysicalWindowRect(Desktop.Handle, out dragRect);
                dragTarget = dragRect;
                dragMonitor = Native.MonitorFor(dragRect).Handle;
                dragMoved = false; previewAttempted = false;
                dragging = true; dragSurface.CaptureMouse();
                e.Handled = true;
            };
            dragSurface.MouseMove += delegate
            {
                if (!dragging) return;
                Native.Point current; Native.GetPhysicalCursorPos(out current);
                if (!dragMoved && !PastDragThreshold(dragStart, current, Scale)) return;
                dragMoved = true;
                if (!previewAttempted) { previewAttempted = true; BeginMovePreview(dragRect); }
                dragTarget = MoveGeometry(dragRect, dragStart, current, Native.MonitorAt(current).Work);
                UpdateMovePreview(dragTarget);
            };
            dragSurface.MouseLeftButtonUp += delegate
            {
                if (!dragging) return;
                FinishMove(dragSurface, true);
            };
            dragSurface.LostMouseCapture += delegate { if (dragging) FinishMove(dragSurface, true); };
            Control<Thumb>("ResizeThumb").DragStarted += delegate
            {
                if (State.Locked || !IsReady) return;
                Native.GetPhysicalCursorPos(out resizeStart); Native.PhysicalWindowRect(Desktop.Handle, out resizeRect);
                resizeTarget = resizeRect;
                resizeMoved = false; previewAttempted = false;
                resizeScale = Scale;
                resizeWork = Native.MonitorFor(resizeRect).Work;
                Desktop.BeginResize();
                resizing = true;
            };
            Control<Thumb>("ResizeThumb").DragDelta += delegate(object sender, DragDeltaEventArgs e)
            {
                if (!resizing || State.Locked || !IsReady) return;
                Native.Point cursor; Native.GetPhysicalCursorPos(out cursor);
                if (!resizeMoved && !PastDragThreshold(resizeStart, cursor, resizeScale)) return;
                resizeMoved = true;
                if (!previewAttempted) { previewAttempted = true; BeginMovePreview(resizeRect); }
                resizeTarget = ResizeGeometry(resizeRect, resizeStart, cursor, resizeScale, resizeWork);
                UpdateMovePreview(resizeTarget);
            };
            Control<Thumb>("ResizeThumb").DragCompleted += delegate(object sender, DragCompletedEventArgs e) { FinishResize(!e.Canceled); };
            Window.SizeChanged += delegate { UpdateContentScale(); UpdateProgress(); UpdateCardClip(); };
            Control<Border>("ProgressTrack").SizeChanged += delegate { UpdateProgress(); };
            Window.Closed += delegate { IsReady = false; undoTimer.Stop(); DisposeMovePreview(); };
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
            UpdateCardClip();
            RefreshInputHint(); RefreshTitle(); Refresh();
        }

        internal void RefreshInputHint()
        {
            Control<TextBlock>("InputHint").ClearValue(UIElement.VisibilityProperty);
        }
        internal static bool ShouldShowInputHint(string text, bool focused) { return String.IsNullOrEmpty(text) && !focused; }
        internal static bool IsTopDragPoint(double pointerY, double inputTopY) { return pointerY >= 0 && pointerY < inputTopY; }

        private void UpdateCardClip()
        {
            Border card = Control<Border>("Card");
            if (card == null) return;
            double width = card.ActualWidth > 0 ? card.ActualWidth : Math.Max(0, Window.Width - 2);
            double height = card.ActualHeight > 0 ? card.ActualHeight : Math.Max(0, Window.Height - 2);
            card.Clip = new RectangleGeometry(new Rect(0, 0, width, height), 18, 18);
        }
        internal static Native.Rect ResizeGeometry(Native.Rect anchor, Native.Point start, Native.Point cursor, double scale, System.Drawing.Rectangle work)
        {
            int maxWidth = Math.Min((int)Math.Round(900 * scale), work.Right - anchor.Left);
            int maxHeight = Math.Min((int)Math.Round(1300 * scale), work.Bottom - anchor.Top);
            int minWidth = Math.Min((int)Math.Round(320 * scale), maxWidth);
            int minHeight = Math.Min((int)Math.Round(360 * scale), maxHeight);
            int width = Math.Max(minWidth, Math.Min(maxWidth, anchor.Right - anchor.Left + cursor.X - start.X));
            int height = Math.Max(minHeight, Math.Min(maxHeight, anchor.Bottom - anchor.Top + cursor.Y - start.Y));
            return new Native.Rect { Left = anchor.Left, Top = anchor.Top, Right = anchor.Left + width, Bottom = anchor.Top + height };
        }
        internal static Native.Rect MoveGeometry(Native.Rect anchor, Native.Point start, Native.Point cursor, System.Drawing.Rectangle work)
        {
            int width = anchor.Right - anchor.Left, height = anchor.Bottom - anchor.Top;
            int x = anchor.Left + cursor.X - start.X, y = anchor.Top + cursor.Y - start.Y;
            x = Math.Max(work.Left, Math.Min(work.Right - width, x));
            y = Math.Max(work.Top, Math.Min(work.Bottom - height, y));
            return new Native.Rect { Left = x, Top = y, Right = x + width, Bottom = y + height };
        }
        internal static bool PastDragThreshold(Native.Point start, Native.Point current, double scale)
        {
            return Math.Abs(current.X - start.X) >= Math.Max(3, SystemParameters.MinimumHorizontalDragDistance * scale) ||
                Math.Abs(current.Y - start.Y) >= Math.Max(3, SystemParameters.MinimumVerticalDragDistance * scale);
        }
        internal bool MovePreviewActive { get { return movePreview != null && Native.IsWindow(movePreview.Handle); } }
        internal IntPtr MovePreviewHandle { get { return movePreview == null ? IntPtr.Zero : movePreview.Handle; } }
        internal bool BeginMovePreview(Native.Rect rect)
        {
            try
            {
                BitmapSource snapshot = CaptureGestureSnapshot(rect);
                if (snapshot == null) return false;
                // Present the complete pixel surface first, then hide WPF content. There
                // is no invisible-preview / invisible-card gap on the first frame.
                movePreview = new GesturePreview(snapshot, rect);
                dragCardOpacity = Control<Border>("Card").Opacity;
                dragThumbOpacity = Control<Thumb>("ResizeThumb").Opacity;
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
        private void FinishMove(FrameworkElement header, bool commit)
        {
            if (!dragging) return;
            dragging = false;
            if (header.IsMouseCaptured) header.ReleaseMouseCapture();
            bool crossScreen = dragMonitor != Native.MonitorFor(dragTarget).Handle;
            if (commit && dragMoved && IsReady)
            {
                if (crossScreen && State.DesktopMode && !forceWindowed && !attachmentFallback)
                {
                    State.X = dragTarget.Left; State.Y = dragTarget.Top; State.PositionSet = true;
                    NotifyChanged();
                    Window.Hide();
                    DisposeMovePreview();
                    if (RebuildRequested != null) RebuildRequested();
                    return;
                }
                if (Desktop.IsAttached || !State.DesktopMode || forceWindowed || attachmentFallback)
                {
                    Desktop.Move(dragTarget.Left, dragTarget.Top, dragTarget.Right - dragTarget.Left, dragTarget.Bottom - dragTarget.Top);
                    RememberGeometry(); NotifyChanged();
                }
            }
            DisposeMovePreview();
        }
        private void FinishResize(bool commit)
        {
            if (!resizing) return;
            resizing = false;
            Desktop.EndResize();
            if (commit && resizeMoved && IsReady && (Desktop.IsAttached || !State.DesktopMode || forceWindowed || attachmentFallback))
            {
                Desktop.Move(resizeTarget.Left, resizeTarget.Top, resizeTarget.Right - resizeTarget.Left, resizeTarget.Bottom - resizeTarget.Top);
                RememberGeometry(); NotifyChanged();
            }
            DisposeMovePreview();
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
        private BitmapSource CaptureCardSnapshot()
        {
            Border card = Control<Border>("Card");
            if (card == null || card.ActualWidth <= 0 || card.ActualHeight <= 0) return null;
            Window.Dispatcher.Invoke(DispatcherPriority.Render, new Action(delegate { }));
            double scale = Scale;
            int width = Math.Max(1, (int)Math.Ceiling(card.ActualWidth * scale));
            int height = Math.Max(1, (int)Math.Ceiling(card.ActualHeight * scale));
            RenderTargetBitmap bitmap = new RenderTargetBitmap(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(card); bitmap.Freeze(); return bitmap;
        }
        internal bool ResizePreviewActive { get { return Control<Image>("ResizePreview").Visibility == Visibility.Visible; } }
        internal bool BeginResizePreview()
        {
            Border card = Control<Border>("Card");
            Image preview = Control<Image>("ResizePreview");
            if (card == null || preview == null || card.ActualWidth <= 0 || card.ActualHeight <= 0) return false;
            try
            {
                BitmapSource bitmap = CaptureCardSnapshot();
                if (bitmap == null) return false;
                preview.Source = bitmap;
                preview.Visibility = Visibility.Visible;
                card.Visibility = Visibility.Collapsed;
                return true;
            }
            catch
            {
                preview.Source = null;
                preview.Visibility = Visibility.Collapsed;
                card.Visibility = Visibility.Visible;
                return false;
            }
        }
        internal void EndResizePreview()
        {
            Border card = Control<Border>("Card");
            Image preview = Control<Image>("ResizePreview");
            card.Visibility = Visibility.Visible;
            preview.Visibility = Visibility.Collapsed;
            preview.Source = null;
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
            RefreshStatus();
        }
        public void PlaceOnScreen(bool reset)
        {
            double scale = Scale;
            int width = (int)Math.Round(State.Width * scale), height = (int)Math.Round(State.Height * scale);
            Native.Point cursor; Native.GetPhysicalCursorPos(out cursor);
            System.Drawing.Rectangle work = Native.MonitorAt(cursor).Work;
            int x = State.PositionSet && !reset ? (int)State.X : work.Right - width - 32;
            int y = State.PositionSet && !reset ? (int)State.Y : work.Top + 52;
            System.Drawing.Rectangle desired = new System.Drawing.Rectangle(x, y, width, height);
            Native.MonitorArea screen = Native.PhysicalMonitors().OrderByDescending(s =>
            {
                System.Drawing.Rectangle intersection = System.Drawing.Rectangle.Intersect(s.Work, desired);
                return (long)intersection.Width * intersection.Height;
            }).First();
            work = screen.Work;
            width = Math.Min(width, work.Width); height = Math.Min(height, work.Height);
            x = Math.Max(work.Left, Math.Min(work.Right - width, x));
            y = Math.Max(work.Top, Math.Min(work.Bottom - height, y));
            Desktop.Move(x, y, width, height);
        }
        public void RememberGeometry()
        {
            if (!IsReady || !Native.IsWindow(Desktop.Handle)) return;
            if (State.DesktopMode && !forceWindowed && !attachmentFallback && !Desktop.IsAttached) return;
            Native.Rect rect; Native.PhysicalWindowRect(Desktop.Handle, out rect);
            State.X = rect.Left; State.Y = rect.Top;
            State.Width = (rect.Right - rect.Left) / Scale; State.Height = (rect.Bottom - rect.Top) / Scale;
            State.PositionSet = true;
        }
        public void AddFromInput()
        {
            if (State.Add(Input.Text) == null) { Input.Focus(); return; }
            Input.Clear(); NotifyChanged(); Refresh(); Input.Focus();
        }
        public void Remove(TodoItem item)
        {
            if (item == null || item.Deleted) return;
            item.Deleted = true; Removed.Push(item); undoGroups.Push(new List<TodoItem> { item });
            undoLabel = "已移除事项"; StartUndoCountdown(); NotifyChanged(); Refresh();
        }
        internal void ClearCompleted()
        {
            List<TodoItem> items = State.Completed.ToList();
            if (items.Count == 0) return;
            foreach (TodoItem item in items) { item.Deleted = true; Removed.Push(item); }
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
            foreach (TodoItem item in items) item.Deleted = false;
            for (int i = 0; i < items.Count && Removed.Count > 0; i++) Removed.Pop();
            undoLabel = undoGroups.Count > 0 && undoGroups.Peek().Count > 1 ? "已清空 " + undoGroups.Peek().Count + " 条已完成事项" : "已移除事项";
            if (undoGroups.Count > 0) StartUndoCountdown();
            else { undoTimer.Stop(); undoExpiresAtUtc = DateTime.MinValue; }
            NotifyChanged(); Refresh();
        }
        private void StartUndoCountdown()
        {
            undoExpiresAtUtc = DateTime.UtcNow.AddSeconds(15);
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
        public void Refresh()
        {
            StackPanel pending = Control<StackPanel>("PendingList"), completed = Control<StackPanel>("CompletedList");
            pending.Children.Clear(); completed.Children.Clear();
            foreach (TodoItem item in State.Pending) pending.Children.Add(CreateRow(item));
            foreach (TodoItem item in State.Completed) completed.Children.Add(CreateRow(item));
            int count = State.Pending.Count(), done = State.Completed.Count();
            Control<TextBlock>("PendingCount").Text = count + " 件";
            Control<TextBlock>("CompletedHeader").Text = "已完成（" + done + "）";
            Control<FrameworkElement>("EmptyPanel").Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
            Control<TextBlock>("EmptyTitle").Text = done == 0 ? "把想做的事，先记下来" : "都完成了，歇一会儿吧";
            Control<TextBlock>("EmptySubtitle").Text = done == 0 ? "一件一件来，不必着急" : "新的事项，随时可以在上方添加";
            Control<FrameworkElement>("UndoPanel").Visibility = Removed.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateUndoMessage();
            Control<Button>("ClearCompletedButton").Visibility = done > 0 ? Visibility.Visible : Visibility.Collapsed;
            RefreshStatus(); UpdateProgress();
        }
        private Grid CreateRow(TodoItem item)
        {
            Grid row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            // Align the visible first-line glyphs with the checkbox/menu center at 19 DIP.
            // TextBlock's line box includes extra font ascent space above numeric glyphs.
            TextBlock text = new TextBlock { Text = item.Text, TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 22, Margin = new Thickness(0, 10, 0, 8) };
            if (item.IsCompleted) { text.Foreground = Brush("MutedBrush"); text.TextDecorations = TextDecorations.Strikethrough; }
            else { text.Cursor = Cursors.Hand; text.ToolTip = "双击编辑"; }
            CheckBox check = new CheckBox { IsChecked = item.IsCompleted, Style = Window.Resources["TaskCheck"] as Style };
            System.Windows.Automation.AutomationProperties.SetName(check, (item.IsCompleted ? "恢复未完成：" : "标记完成：") + item.Text);
            check.Click += delegate
            {
                State.Toggle(item);
                if (!item.IsCompleted && item.ReminderIntervalMinutes > 0) item.LastIntervalReminderAt = DateTime.UtcNow.Ticks;
                NotifyChanged(); Refresh();
            };
            ContextMenu menu = new ContextMenu { Style = Window.Resources["TaskMenu"] as Style, FontFamily = Window.FontFamily, FontSize = 13 };
            menu.Opened += delegate
            {
                foreach (string key in new[] { "CardBrush", "TextBrush", "LineBrush", "HoverBrush" }) menu.Resources[key] = Window.Resources[key];
            };
            MenuItem edit = new MenuItem { Header = "编辑事项", Style = Window.Resources["TaskMenuItem"] as Style }; edit.Click += delegate { Edit(item); };
            MenuItem copy = new MenuItem { Header = "复制事项", Style = Window.Resources["TaskMenuItem"] as Style };
            copy.Click += delegate
            {
                try { Clipboard.SetText(item.Text); }
                catch (Exception e) { ShowNotice("无法复制", "剪贴板暂时不可用：\n" + e.Message); }
            };
            MenuItem reminder = new MenuItem { Header = item.ReminderIntervalMinutes > 0 || item.DailyReminderMinutesPlusOne > 0 ? "设置提醒（已开启）" : "设置提醒", Style = Window.Resources["TaskMenuItem"] as Style };
            reminder.Click += delegate { OpenItemReminder(item); };
            MenuItem remove = new MenuItem { Header = "删除（可撤销）", Style = Window.Resources["TaskMenuItem"] as Style }; remove.Click += delegate { Remove(item); };
            menu.Items.Add(edit); menu.Items.Add(copy); menu.Items.Add(reminder); menu.Items.Add(remove);
            if (!item.IsCompleted) row.ContextMenu = menu;
            text.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) { EditFromTaskText(item, e.ClickCount); };
            row.Children.Add(check); Grid.SetColumn(text, 1); row.Children.Add(text);
            TextBlock time = new TextBlock
            {
                Text = ItemTime(item), FontSize = 9, Foreground = Brush("MutedBrush"), Margin = new Thickness(6, 12, 2, 0),
                VerticalAlignment = VerticalAlignment.Top, Visibility = State.HideItemTimes ? Visibility.Collapsed : Visibility.Visible,
                ToolTip = item.IsCompleted ? "完成时间" : "添加时间", Tag = "ItemTime"
            };
            Grid.SetColumn(time, 2); row.Children.Add(time);
            Button more = new Button { Style = Window.Resources["IconButton"] as Style, Content = item.IsCompleted ? UiGlyphs.Trash() : CreateDots(Orientation.Vertical), Width = 23, Height = 28, Margin = new Thickness(5, 5, -2, 0), VerticalAlignment = VerticalAlignment.Top, Opacity = 0.65, ToolTip = item.IsCompleted ? "删除（可撤销）" : "编辑、复制、提醒或删除", FocusVisualStyle = null };
            ToolTipService.SetPlacement(more, PlacementMode.Left);
            ToolTipService.SetHorizontalOffset(more, -8);
            System.Windows.Automation.AutomationProperties.SetName(more, (item.IsCompleted ? "删除（可撤销）：" : "编辑、复制、提醒或删除：") + item.Text);
            more.Click += delegate { if (item.IsCompleted) Remove(item); else { menu.PlacementTarget = more; menu.IsOpen = true; } };
            row.MouseEnter += delegate { more.Opacity = 1; }; row.MouseLeave += delegate { more.Opacity = 0.65; };
            Grid.SetColumn(more, 3); row.Children.Add(more);
            return row;
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
                    Width = 2.4,
                    Height = 2.4,
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
            button.Foreground = Brush(State.Locked ? "AccentBrush" : "MutedBrush");
            button.Background = Brushes.Transparent;
            Control<System.Windows.Shapes.Path>("LockShackle").Data = Geometry.Parse(State.Locked ? UiGlyphs.ClosedShackle : UiGlyphs.OpenShackle);
            button.ToolTip = State.Locked ? "已锁定，点击解锁" : "未锁定，点击锁定";
            System.Windows.Automation.AutomationProperties.SetName(button, State.Locked ? "已锁定，点击解锁" : "未锁定，点击锁定");
            TextBlock title = Control<TextBlock>("TitleLabel");
            title.Cursor = Cursors.Arrow;
            title.ToolTip = State.Locked ? "已锁定，解锁后可在设置中修改名称" : "可在设置中修改名称";
            Control<FrameworkElement>("DragHeader").Cursor = State.Locked ? Cursors.Arrow : Cursors.SizeAll;
            Control<Thumb>("ResizeThumb").Visibility = State.Locked ? Visibility.Hidden : Visibility.Visible;
            Control<Button>("MinimizeButton").Visibility = State.ShowMinimizeButton ? Visibility.Visible : Visibility.Collapsed;
            Control<Button>("CloseButton").Visibility = State.ShowCloseButton ? Visibility.Visible : Visibility.Collapsed;
            Control<TextBlock>("DateLabel").Text = DateTime.Now.ToString("M月d日  dddd", System.Globalization.CultureInfo.GetCultureInfo("zh-CN"));
            bool wantsDesktop = State.DesktopMode && !forceWindowed;
            string mode = Desktop.IsAttached ? "桌面模式" : attachmentFallback ? "等待桌面恢复" : wantsDesktop ? "桌面挂载失败" : "窗口模式";
            TextBlock status = Control<TextBlock>("StatusLabel");
            status.Text = (IsSaved ? "已自动保存" : "保存失败，请重试") + " · " + mode;
            status.ToolTip = SaveError ?? (attachmentFallback ? "Explorer 桌面尚未就绪；当前以普通窗口显示，恢复后会自动重新嵌入。" :
                wantsDesktop && !Desktop.IsAttached ? "点击设置 → 重新嵌入桌面；也可切换窗口模式。" : "数据只保存在本机；勾选即完成。");
            Control<System.Windows.Shapes.Ellipse>("StatusDot").Fill = IsSaved ? Desktop.IsAttached || !wantsDesktop || attachmentFallback ?
                new SolidColorBrush(Color.FromRgb(121, 168, 137)) : Brushes.DarkOrange : Brushes.IndianRed;
        }
        private void UpdateProgress()
        {
            int done = State.Completed.Count(), total = State.Pending.Count() + done;
            Control<TextBlock>("ProgressLabel").Text = total == 0 ? "" : done + "/" + total;
            Border track = Control<Border>("ProgressTrack");
            double available = Math.Max(0, track.ActualWidth > 0 ? track.ActualWidth :
                (Window.ActualWidth > 0 ? Window.ActualWidth : Window.Width) / contentScale - 60);
            Control<Border>("ProgressFill").Width = total == 0 ? 0 : available * done / total;
        }
        internal static double AdaptiveContentScale(double width, double height)
        {
            // Wide/tall cards should increase readability, not merely add blank area.
            // Height caps scaling so a wide but short card still has a useful task list.
            return Math.Round(Math.Max(1, Math.Min(1.8, Math.Min(width / 360, height / 400))), 3);
        }
        internal void UpdateContentScale()
        {
            double width = Window.ActualWidth > 0 ? Window.ActualWidth : Window.Width;
            double height = Window.ActualHeight > 0 ? Window.ActualHeight : Window.Height;
            double next = AdaptiveContentScale(width, height);
            Grid content = Control<Grid>("CardContent");
            if (content == null || (Math.Abs(next - contentScale) < 0.001 && content.LayoutTransform is ScaleTransform)) return;
            contentScale = next;
            content.LayoutTransform = new ScaleTransform(next, next);
        }
        public void ApplyTheme()
        {
            State.ThemeId = ThemeCatalog.Normalize(State.ThemeId);
            ThemePalette palette = ThemeCatalog.Get(State.ThemeId);
            string[] colors = State.DarkMode ? palette.Dark : palette.Light;
            for (int i = 0; i < ThemeCatalog.ResourceNames.Length; i++)
                Window.Resources[ThemeCatalog.ResourceNames[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
        }
        private void ShowMenu()
        {
            SettingsOpen = true;
        }
        public bool SettingsOpen
        {
            get { return Control<FrameworkElement>("SettingsPanel").Visibility == Visibility.Visible; }
            set
            {
                Control<FrameworkElement>("SettingsPanel").Visibility = value ? Visibility.Visible : Visibility.Collapsed;
                Control<FrameworkElement>("MainPanel").Visibility = value ? Visibility.Collapsed : Visibility.Visible;
                if (value) Control<FrameworkElement>("ReminderPanel").Visibility = Visibility.Collapsed;
                if (value) { RefreshSettings(); Control<Button>("SettingsBack").Focus(); }
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
            UpdateContentScale();
            Window.Dispatcher.BeginInvoke(new Action(delegate { editInput.Focus(); editInput.SelectAll(); }));
        }
        private void CloseEdit()
        {
            closingEdit = true;
            Control<FrameworkElement>("EditPanel").Visibility = Visibility.Collapsed;
            Control<FrameworkElement>("MainPanel").Visibility = Visibility.Visible;
            editingItem = null; Input.Focus(); UpdateContentScale();
            closingEdit = false; suppressEditLostFocus = false;
        }
        private void SaveEdit()
        {
            if (editingItem == null || String.IsNullOrWhiteSpace(editInput.Text)) return;
            editingItem.Text = editInput.Text.Trim();
            NotifyChanged(); Refresh(); CloseEdit();
        }
        private void RefreshSettings()
        {
            Control<CheckBox>("DesktopToggle").IsChecked = State.DesktopMode && !forceWindowed;
            Control<CheckBox>("DesktopToggle").IsEnabled = !forceWindowed;
            Control<CheckBox>("LockToggle").IsChecked = State.Locked;
            Control<CheckBox>("ShowTrayToggle").IsChecked = !State.HideTray;
            Control<CheckBox>("ShowMinimizeToggle").IsChecked = State.ShowMinimizeButton;
            Control<CheckBox>("ShowCloseToggle").IsChecked = State.ShowCloseButton;
            titleSettingInput.IsEnabled = !State.Locked;
            Control<Button>("TitleSaveButton").IsEnabled = !State.Locked && !String.IsNullOrWhiteSpace(titleSettingInput.Text);
            if (!titleSettingInput.IsKeyboardFocusWithin) titleSettingInput.Text = State.Title;
            Control<CheckBox>("DarkToggle").IsChecked = State.DarkMode;
            Control<CheckBox>("ShowTimesToggle").IsChecked = !State.HideItemTimes;
            Control<CheckBox>("ShowDateToggle").IsChecked = State.ShowItemDates;
            Control<CheckBox>("ShowDateToggle").IsEnabled = !State.HideItemTimes;
            foreach (ThemePalette palette in ThemeCatalog.All)
                Control<RadioButton>("Theme" + Char.ToUpperInvariant(palette.Id[0]) + palette.Id.Substring(1)).IsChecked = State.ThemeId == palette.Id;
            try { Control<CheckBox>("StartupToggle").IsChecked = GetStartup != null && GetStartup(); }
            catch { Control<CheckBox>("StartupToggle").IsChecked = false; }
        }
        public void SettingsMessage(string message) { Control<TextBlock>("SettingsStatus").Text = message; }
        internal void RefreshSettingsForTray() { RefreshSettings(); }
        internal bool OverlayOpen { get { return Control<FrameworkElement>("OverlayLayer").Visibility == Visibility.Visible; } }
        private void PrepareOverlaySurface(bool fullPage)
        {
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
            PrepareOverlaySurface(false);
            pendingConfirmation = action;
            Control<TextBlock>("ConfirmTitle").Text = title;
            Control<TextBlock>("ConfirmBody").Text = body;
            Control<Button>("ConfirmAcceptButton").Content = acceptLabel;
            Control<Button>("ConfirmCancelButton").Visibility = Visibility.Visible;
            Control<FrameworkElement>("PositionOverlay").Visibility = Visibility.Collapsed;
            Control<FrameworkElement>("ConfirmOverlay").Visibility = Visibility.Visible;
            Control<FrameworkElement>("OverlayLayer").Visibility = Visibility.Visible;
            Control<Button>("ConfirmCancelButton").Focus();
        }
        internal void ShowNotice(string title, string body)
        {
            PrepareOverlaySurface(false);
            pendingConfirmation = null;
            Control<TextBlock>("ConfirmTitle").Text = title;
            Control<TextBlock>("ConfirmBody").Text = body;
            Control<Button>("ConfirmAcceptButton").Content = "知道了";
            Control<Button>("ConfirmCancelButton").Visibility = Visibility.Collapsed;
            Control<FrameworkElement>("PositionOverlay").Visibility = Visibility.Collapsed;
            Control<FrameworkElement>("ConfirmOverlay").Visibility = Visibility.Visible;
            Control<FrameworkElement>("OverlayLayer").Visibility = Visibility.Visible;
            Control<Button>("ConfirmAcceptButton").Focus();
        }
        internal void CloseOverlay()
        {
            pendingConfirmation = null;
            Control<FrameworkElement>("OverlayLayer").Visibility = Visibility.Collapsed;
            Control<FrameworkElement>("ConfirmOverlay").Visibility = Visibility.Collapsed;
            Control<FrameworkElement>("PositionOverlay").Visibility = Visibility.Collapsed;
        }
        internal void OpenPositionOverlay()
        {
            PrepareOverlaySurface(true);
            SelectPositionOptions(State.DefaultCorner, State.DefaultScalePercent);
            Control<FrameworkElement>("ConfirmOverlay").Visibility = Visibility.Collapsed;
            Control<FrameworkElement>("PositionOverlay").Visibility = Visibility.Visible;
            Control<FrameworkElement>("OverlayLayer").Visibility = Visibility.Visible;
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
            double scale = Scale;
            int width = Math.Min(work.Width, (int)Math.Round(widthDip * scale));
            int height = Math.Min(work.Height, (int)Math.Round(heightDip * scale));
            int margin = (int)Math.Round(32 * scale);
            int x = corner == "TopLeft" || corner == "BottomLeft" ? work.Left + margin : work.Right - width - margin;
            int y = corner == "TopLeft" || corner == "TopRight" ? work.Top + margin : work.Bottom - height - margin;
            x = Math.Max(work.Left, Math.Min(work.Right - width, x)); y = Math.Max(work.Top, Math.Min(work.Bottom - height, y));
            Desktop.Move(x, y, width, height); RememberGeometry(); NotifyChanged();
        }
        internal void RefreshImportedState()
        {
            State.Validate(); ApplyTheme(); RefreshTitle(); Refresh(); RefreshSettings();
        }
        public void RestoreSession(TodoWindow previous)
        {
            Input.Text = previous.Input.Text;
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
            if (State.Locked || String.IsNullOrWhiteSpace(titleSettingInput.Text)) return;
            State.Title = titleSettingInput.Text.Trim();
            if (State.Title.Length > 12) State.Title = State.Title.Substring(0, 12);
            NotifyChanged(); RefreshTitle(); titleSettingInput.Text = State.Title; SettingsMessage("软件名称已更新");
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
            Control<TextBlock>("ReminderItemText").Text = item.Text;
            Control<CheckBox>("ItemIntervalToggle").IsChecked = item.ReminderIntervalMinutes > 0;
            Control<CheckBox>("ItemDailyToggle").IsChecked = item.DailyReminderMinutesPlusOne > 0;
            int interval = item.ReminderIntervalMinutes > 0 ? item.ReminderIntervalMinutes : 60;
            itemReminderHours.Text = (interval / 60).ToString(); itemReminderMinutes.Text = (interval % 60).ToString();
            int daily = item.DailyReminderMinutesPlusOne > 0 ? item.DailyReminderMinutesPlusOne - 1 : 20 * 60;
            itemReminderDailyTime.Text = (daily / 60).ToString("00") + ":" + (daily % 60).ToString("00");
            Control<TextBlock>("ReminderEditorMessage").Text = item.IsCompleted ? "已完成事项暂停提醒，恢复未完成后继续" : "两种提醒可以同时开启";
            Control<FrameworkElement>("MainPanel").Visibility = Visibility.Collapsed;
            Control<FrameworkElement>("SettingsPanel").Visibility = Visibility.Collapsed;
            Control<FrameworkElement>("EditPanel").Visibility = Visibility.Collapsed;
            Control<FrameworkElement>("ReminderPanel").Visibility = Visibility.Visible;
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
            reminderEditingItem = null; Input.Focus();
        }
        internal void SaveItemReminder()
        {
            if (reminderEditingItem == null) return;
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
            if (reminderEditingItem == null) return;
            reminderEditingItem.ReminderIntervalMinutes = 0; reminderEditingItem.DailyReminderMinutesPlusOne = 0;
            reminderEditingItem.LastIntervalReminderAt = 0; reminderEditingItem.LastDailyReminderDate = null; reminderEditingItem.ReminderSnoozeUntil = 0;
            NotifyChanged(); Refresh(); CloseReminder();
        }
    }
}
