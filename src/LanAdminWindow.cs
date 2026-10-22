using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Controls.Primitives;

namespace DesktopTodo
{
    internal sealed class LanAdminWindow : IDisposable
    {
        private readonly Window window;
        private readonly ListBox users, hiddenUsers;
        private readonly TextBox code, addText, editText;
        private readonly ComboBox addGroup, editGroup;
        private readonly CheckBox editCompleted;
        private readonly TextBlock status, remoteTitle, pendingCount, completedHeader;
        private readonly Button connect, hiddenToggle, addButton, completedToggle;
        private readonly StackPanel pendingRows, completedRows;
        private readonly Grid editLayer;
        private readonly Border editCard;
        private readonly Button editSaveButton;
        private readonly FrameworkElement[] browseSections;
        private readonly DispatcherTimer refreshTimer;
        private readonly LanAdminPreferences preferences;
        private readonly Brush card, text, muted, line, input, accent, accentText;
        private List<LanPeer> peers = new List<LanPeer>();
        private LanPeer selectedPeer;
        private LanTaskInfo editingTask;
        private LanSnapshot snapshot;
        private long revision = -1;
        private long editingRevision = -1;
        private bool disposed, working, completedExpanded, refreshingPeerLists, testDataMode;
        private Window reminderDialog;
        private LanSnapshot deferredSnapshot;
        private sealed class CachedRemoteRow { internal string Signature; internal FrameworkElement Row; }
        private readonly Dictionary<string, CachedRemoteRow> rowCache = new Dictionary<string, CachedRemoteRow>();
        private string rowAppearance;
        internal Func<LanPeer, LanRequest, LanResponse> SendForTest;
        internal Func<List<LanPeer>> DiscoverForTest;
        internal Action<string> Log;
        internal bool IsWorking { get { return working; } }

        internal Window Window { get { return window; } }
        internal Button ConnectButton { get { return connect; } }
        internal TextBlock StatusText { get { return status; } }
        internal ListBox UserList { get { return users; } }
        internal ListBox HiddenUserList { get { return hiddenUsers; } }
        internal Button HiddenToggleButton { get { return hiddenToggle; } }
        internal TextBox CodeInput { get { return code; } }
        internal StackPanel PendingRows { get { return pendingRows; } }
        internal TextBox AddTextInput { get { return addText; } }
        internal Button AddTaskButton { get { return addButton; } }
        internal ComboBox AddGroupBox { get { return addGroup; } }
        internal CheckBox EditCompletedCheckBox { get { return editCompleted; } }
        internal Grid EditLayer { get { return editLayer; } }
        internal Button EditSaveButton { get { return editSaveButton; } }
        internal TextBox EditTextInput { get { return editText; } }
        internal Button CompletedToggle { get { return completedToggle; } }
        internal LanAdminPreferences Preferences { get { return preferences; } }

        internal LanAdminWindow(AppState appearance, string dataDirectory = null)
        {
            preferences = LanAdminPreferences.Load(dataDirectory);
            ThemePalette palette = ThemeCatalog.Get(appearance.ThemeId);
            string[] colors = appearance.DarkMode ? palette.Dark : palette.Light;
            card = Solid(colors[0]); text = Solid(colors[1]); muted = Solid(colors[2]);
            line = Solid(colors[3]); input = Solid(colors[4]); accent = Solid(colors[5]);
            accentText = Solid(ThemeCatalog.AccentForeground(colors[5]));
            Rect workArea = SystemParameters.WorkArea;
            window = new Window { Title = "DeskTodo 局域网管理", Width = Math.Max(680, Math.Min(preferences.WindowWidth, workArea.Width)), Height = Math.Max(460, Math.Min(preferences.WindowHeight, workArea.Height)),
                MinWidth = 680, MinHeight = 460, WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = card, Foreground = text, FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI"),
                FontSize = 13, ShowInTaskbar = true, Icon = UiAssets.ApplicationIcon };
            window.Resources.MergedDictionaries.Add(LanAdminWindowLegacy.AdminResources(colors));
            window.Resources.Add(SystemParameters.FocusVisualStyleKey, new Style(typeof(Control)));
            window.AddHandler(Keyboard.PreviewGotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(delegate(object sender, KeyboardFocusChangedEventArgs e)
            { Control focused = e.NewFocus as Control; if (focused != null) focused.FocusVisualStyle = null; }), true);
            window.Resources.MergedDictionaries.Add(MenuResources());
            UiInteraction.DisableHoverHints(window);
            Grid root = new Grid { Margin = new Thickness(22), Background = card };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.Children.Add(new TextBlock { Text = "局域网待办管理", FontSize = 23, FontWeight = FontWeights.SemiBold, Foreground = text });

            Grid body = new Grid { Margin = new Thickness(0, 17, 0, 12) }; Grid.SetRow(body, 1);
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(244) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            body.ColumnDefinitions.Add(new ColumnDefinition());
            root.Children.Add(body);

            Grid left = new Grid(); Grid.SetColumn(left, 0); body.Children.Add(left);
            left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            left.RowDefinitions.Add(new RowDefinition());
            left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Button discover = Button("扫描局域网用户", accent, accentText);
            discover.Margin = new Thickness(0); discover.Click += delegate { Discover(); }; left.Children.Add(discover);
            ScrollViewer peerScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(0, 8, 0, 4) };
            Grid.SetRow(peerScroll, 1); left.Children.Add(peerScroll);
            StackPanel peerStack = new StackPanel { Margin = new Thickness(0, 0, 6, 0) }; peerScroll.Content = peerStack;
            users = UserListBox(); users.Height = 160; users.MinHeight = 55;
            users.SelectionChanged += delegate { if (!refreshingPeerLists) SelectPeer(users.SelectedItem as LanPeer); };
            users.ContextMenu = NewMenu();
            users.ContextMenuOpening += UserMenuOpening;
            users.AddHandler(UIElement.PreviewMouseRightButtonDownEvent, new MouseButtonEventHandler(SelectRightClickedUser), true);
            peerStack.Children.Add(users);
            hiddenToggle = Button("隐藏的用户（0）", input, text); hiddenToggle.Margin = new Thickness(0, 4, 0, 0);
            hiddenToggle.Click += delegate
            {
                bool show = hiddenUsers.Visibility != Visibility.Visible;
                hiddenUsers.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                if (show) peerScroll.Dispatcher.BeginInvoke(new Action(delegate { peerScroll.ScrollToEnd(); }));
            };
            peerStack.Children.Add(hiddenToggle);
            hiddenUsers = UserListBox(); hiddenUsers.MaxHeight = 84; hiddenUsers.Margin = new Thickness(0, 5, 0, 4);
            hiddenUsers.Visibility = Visibility.Collapsed;
            hiddenUsers.MouseDoubleClick += delegate
            {
                LanPeer peer = hiddenUsers.SelectedItem as LanPeer;
                if (peer != null) RestoreUser(peer);
            };
            hiddenUsers.AddHandler(UIElement.PreviewMouseRightButtonDownEvent, new MouseButtonEventHandler(SelectRightClickedUser), true);
            ContextMenu hiddenMenu = NewMenu();
            MenuItem restore = new MenuItem { Header = "重新显示" };
            restore.Click += delegate { LanPeer peer = hiddenUsers.SelectedItem as LanPeer; if (peer != null) RestoreUser(peer); };
            hiddenMenu.Items.Add(restore); hiddenUsers.ContextMenu = hiddenMenu;
            peerStack.Children.Add(hiddenUsers);
            peerScroll.SizeChanged += delegate
            {
                users.Height = Math.Max(96, peerScroll.ActualHeight - hiddenToggle.ActualHeight - 8);
            };
            TextBlock codeLabel = new TextBlock { Text = "管理口令", Foreground = muted, Margin = new Thickness(0, 7, 0, 5) };
            Grid.SetRow(codeLabel, 2); left.Children.Add(codeLabel);
            code = InputBox(36); code.CharacterCasing = CharacterCasing.Upper; code.MaxLength = 16;
            code.Padding = new Thickness(9, 0, 9, 0); code.VerticalContentAlignment = VerticalAlignment.Center;
            code.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { RefreshRemote(true); e.Handled = true; } };
            Grid.SetRow(code, 3); left.Children.Add(code);
            connect = Button("连接并读取", input, text); connect.Margin = new Thickness(0, 8, 0, 0);
            connect.Click += delegate { RefreshRemote(true); }; Grid.SetRow(connect, 4); left.Children.Add(connect);

            Grid right = new Grid { ClipToBounds = true }; Grid.SetColumn(right, 2); body.Children.Add(right);
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            right.RowDefinitions.Add(new RowDefinition());
            remoteTitle = new TextBlock { Text = "选择用户后查看待办", FontSize = 17, FontWeight = FontWeights.SemiBold,
                Foreground = text, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(1, 2, 0, 10) };
            right.Children.Add(remoteTitle);
            Border addBar = new Border { Background = input, CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 5, 5, 5), Margin = new Thickness(0, 0, 0, 12) };
            Grid.SetRow(addBar, 1); right.Children.Add(addBar);
            Grid addGrid = new Grid(); addGrid.ColumnDefinitions.Add(new ColumnDefinition());
            addGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(116) });
            addGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) }); addBar.Child = addGrid;
            addText = new TextBox { Background = Brushes.Transparent, Foreground = text, BorderThickness = new Thickness(0),
                Padding = new Thickness(5, 0, 5, 0), VerticalContentAlignment = VerticalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center, MaxLength = 500, MinHeight = 36, MaxHeight = 70,
                TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                FocusVisualStyle = null, ToolTip = "输入待办，Enter 添加；Ctrl+Enter 换行" };
            addText.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key != Key.Enter) return;
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) return;
                AddTask(); e.Handled = true;
            };
            addGrid.Children.Add(addText);
            TextBlock addHint = new TextBlock { Text = "添加一件要做的事…", Foreground = muted,
                Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false };
            addGrid.Children.Add(addHint);
            addText.TextChanged += delegate { addHint.Visibility = String.IsNullOrEmpty(addText.Text) && !addText.IsKeyboardFocused
                ? Visibility.Visible : Visibility.Collapsed; };
            addText.GotKeyboardFocus += delegate { addHint.Visibility = Visibility.Collapsed; };
            addText.LostKeyboardFocus += delegate { addHint.Visibility = String.IsNullOrEmpty(addText.Text) ? Visibility.Visible : Visibility.Collapsed; };
            addGroup = GroupBox(); addGroup.Margin = new Thickness(3, 2, 5, 2); Grid.SetColumn(addGroup, 1); addGrid.Children.Add(addGroup);
            addButton = Button("", accent, accentText); addButton.Content = UiGlyphs.Plus();
            addButton.Padding = new Thickness(0); addButton.VerticalAlignment = VerticalAlignment.Center;
            addButton.MinWidth = 0; addButton.Width = 36; addButton.Height = 36;
            addButton.FontSize = 20; addButton.Margin = new Thickness(0); addButton.Click += delegate { AddTask(); };
            Grid.SetColumn(addButton, 2); addGrid.Children.Add(addButton);

            ScrollViewer taskScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Background = Brushes.Transparent };
            UiDrag.ReorderZone(taskScroll, "DeskTodo.RemoteGroup");
            UiDrag.ReorderZone(taskScroll, "DeskTodo.RemoteTask");
            Grid.SetRow(taskScroll, 2); right.Children.Add(taskScroll);
            StackPanel taskBody = new StackPanel { Margin = new Thickness(1, 0, 5, 4) }; taskScroll.Content = taskBody;
            Grid pendingHeading = new Grid { Height = 26, Margin = new Thickness(0, 0, 0, 7) };
            pendingHeading.ColumnDefinitions.Add(new ColumnDefinition()); pendingHeading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            pendingHeading.Children.Add(new TextBlock { Text = "未完成", Foreground = muted, FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            pendingCount = new TextBlock { Foreground = muted, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(pendingCount, 1); pendingHeading.Children.Add(pendingCount); taskBody.Children.Add(pendingHeading);
            pendingRows = new StackPanel(); taskBody.Children.Add(pendingRows);
            taskBody.Children.Add(new Border { Height = 1, Background = line, Margin = new Thickness(0, 14, 0, 8) });
            completedToggle = Button("", Brushes.Transparent, muted); completedToggle.Margin = new Thickness(0);
            completedToggle.UseLayoutRounding = false;
            completedToggle.Padding = new Thickness(0); completedToggle.MinWidth = 0;
            completedToggle.HorizontalContentAlignment = HorizontalAlignment.Left;
            completedToggle.Template = (ControlTemplate)XamlReader.Parse(
                "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'>" +
                "<Border Background='{TemplateBinding Background}' Padding='{TemplateBinding Padding}' CornerRadius='7'>" +
                "<ContentPresenter HorizontalAlignment='Left' VerticalAlignment='Center'/></Border></ControlTemplate>");
            completedHeader = new TextBlock { Foreground = muted, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            UiGlyphs.CenterLabelInk(completedHeader);
            System.Windows.Automation.AutomationProperties.SetName(completedToggle, "展开或收起已完成");
            completedToggle.Click += delegate { completedExpanded = !completedExpanded; RenderRows(); };
            taskBody.Children.Add(completedToggle);
            completedRows = new StackPanel { Visibility = Visibility.Collapsed }; taskBody.Children.Add(completedRows);
            browseSections = new FrameworkElement[] { remoteTitle, addBar, taskScroll };

            // Replace the right-hand content with an editor. A same-theme surface
            // hides stray underlying letters without dimming the surrounding UI.
            editLayer = new Grid { Background = card, Visibility = Visibility.Collapsed };
            Grid.SetRow(editLayer, 0); Grid.SetRowSpan(editLayer, 3); Panel.SetZIndex(editLayer, 5);
            right.Children.Add(editLayer);
            editCard = new Border { Background = card, BorderBrush = line, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14), Padding = new Thickness(14), MaxWidth = 720,
                HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 8, 4, 8) };
            editLayer.Children.Add(editCard);
            Grid editBody = new Grid(); editCard.Child = editBody;
            editBody.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            editBody.RowDefinitions.Add(new RowDefinition());
            editBody.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            editBody.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            editBody.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            editBody.Children.Add(new TextBlock { Text = "编辑事项", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
            editText = InputBox(Double.NaN); editText.MinHeight = 40; editText.AcceptsReturn = true; editText.TextWrapping = TextWrapping.Wrap;
            editText.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; editText.MaxLength = 500;
            editText.VerticalContentAlignment = VerticalAlignment.Top; editText.Padding = new Thickness(10);
            Grid.SetRow(editText, 1); editBody.Children.Add(editText);
            editGroup = GroupBox(); editGroup.Margin = new Thickness(0, 9, 0, 0); Grid.SetRow(editGroup, 2); editBody.Children.Add(editGroup);
            editCompleted = new CheckBox { Content = "已完成", Foreground = text, Margin = new Thickness(0, 10, 0, 0),
                Style = window.Resources["AdminCheck"] as Style }; Grid.SetRow(editCompleted, 3); editBody.Children.Add(editCompleted);
            StackPanel editActions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0) };
            Button cancel = Button("取消", input, text);
            editSaveButton = Button("保存", accent, accentText);
            cancel.Click += delegate { CloseEdit(); }; editSaveButton.Click += delegate { SaveEdit(); };
            editActions.Children.Add(cancel); editActions.Children.Add(editSaveButton); Grid.SetRow(editActions, 4); editBody.Children.Add(editActions);
            editLayer.SizeChanged += delegate
            {
                editCard.Height = Math.Max(0, Math.Min(560, editLayer.ActualHeight - 16));
            };
            editLayer.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            { if (e.OriginalSource == editLayer) { SaveEdit(); e.Handled = true; } };
            window.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            { if (e.Key == Key.Escape && editLayer.Visibility == Visibility.Visible) { CloseEdit(); e.Handled = true; } };

            status = new TextBlock { Text = "先扫描局域网内已开启共享的 DeskTodo", Foreground = muted,
                TextWrapping = TextWrapping.Wrap, FontSize = 11 };
            Grid.SetRow(status, 2); root.Children.Add(status);
            window.Content = root;
            window.Closed += delegate { Dispose(); };
            window.Closing += delegate
            {
                // RestoreBounds can lag behind a normal-window resize until the next
                // native layout message. Use the current requested size in normal mode;
                // only use restore bounds when the window is maximized or minimized.
                Rect savedBounds = window.RestoreBounds;
                bool useRestore = window.WindowState != WindowState.Normal && !savedBounds.IsEmpty;
                preferences.WindowWidth = useRestore ? savedBounds.Width : window.Width;
                preferences.WindowHeight = useRestore ? savedBounds.Height : window.Height;
                try { preferences.Save(); } catch (Exception error) { status.Text = "窗口大小保存失败：" + error.Message; }
            };
            refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            refreshTimer.Tick += delegate
            {
                if (UiDrag.IsActive) return;
                ApplyDeferredSnapshot();
                if (!testDataMode && selectedPeer != null && snapshot != null) RefreshRemote(false);
            };
            refreshTimer.Start();
        }

        private static Brush Solid(string value) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)); }
        private ResourceDictionary MenuResources()
        {
            ResourceDictionary resources = (ResourceDictionary)XamlReader.Parse(@"<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' xmlns:local='clr-namespace:DesktopTodo;assembly=DeskTodo'>
              <Style TargetType='ContextMenu'><Setter Property='FocusVisualStyle' Value='{x:Null}'/><Setter Property='HasDropShadow' Value='False'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ContextMenu'><Border Background='{DynamicResource MenuCard}' BorderBrush='{DynamicResource MenuLine}' BorderThickness='1' CornerRadius='10' Padding='5'><ItemsPresenter/></Border></ControlTemplate></Setter.Value></Setter></Style>
              <Style TargetType='Separator'><Setter Property='OverridesDefaultStyle' Value='True'/><Setter Property='Margin' Value='0'/><Setter Property='Padding' Value='0'/><Setter Property='HorizontalAlignment' Value='Stretch'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Separator'><Border Height='1' Background='{DynamicResource MenuLine}' Margin='9,5'/></ControlTemplate></Setter.Value></Setter></Style>
              <Style TargetType='MenuItem'><Setter Property='Foreground' Value='{DynamicResource MenuText}'/><Setter Property='FontSize' Value='13'/><Setter Property='FocusVisualStyle' Value='{x:Null}'/><Setter Property='Cursor' Value='Hand'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='MenuItem'><Grid><Border x:Name='Surface' Background='Transparent' CornerRadius='6' Padding='14,9'><Grid><Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions><local:InkContentPresenter ContentSource='Header'/><Path x:Name='Arrow' Grid.Column='1' Data='M 5,3 L 9,7 L 5,11' Stroke='{DynamicResource MenuMuted}' Margin='14,0,0,0' VerticalAlignment='Center' Visibility='Collapsed'/></Grid></Border><Popup x:Name='PART_Popup' IsOpen='{Binding IsSubmenuOpen, RelativeSource={RelativeSource TemplatedParent}}' Placement='Right' AllowsTransparency='True' Focusable='False' PopupAnimation='Fade'><Border Background='{DynamicResource MenuCard}' BorderBrush='{DynamicResource MenuLine}' BorderThickness='1' CornerRadius='9' Padding='5' Margin='3,0,0,0'><StackPanel IsItemsHost='True' MinWidth='130'/></Border></Popup></Grid><ControlTemplate.Triggers><Trigger Property='HasItems' Value='True'><Setter TargetName='Arrow' Property='Visibility' Value='Visible'/></Trigger><Trigger Property='IsHighlighted' Value='True'><Setter TargetName='Surface' Property='Background' Value='{DynamicResource MenuHover}'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter TargetName='Surface' Property='Opacity' Value='.45'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
            </ResourceDictionary>".Replace("assembly=DeskTodo", "assembly=" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Name));
            resources["MenuCard"] = card; resources["MenuText"] = text; resources["MenuLine"] = line;
            resources["MenuHover"] = input; resources["MenuMuted"] = muted;
            // MenuBase assigns a keyed separator style during container preparation.
            // An implicit Separator style alone leaves the system's icon-gutter margin.
            resources[MenuItem.SeparatorStyleKey] = resources[typeof(Separator)];
            return resources;
        }
        private ContextMenu NewMenu()
        {
            ContextMenu menu = new ContextMenu { Background = card, Foreground = text, BorderBrush = line,
                FontFamily = window.FontFamily, FontSize = 13, Style = window.Resources[typeof(ContextMenu)] as Style };
            menu.Resources.MergedDictionaries.Add(window.Resources.MergedDictionaries.Last());
            return menu;
        }
        internal static LanRequest ReminderRequest(LanTaskInfo task, bool intervalOn, string hoursText, string minutesText,
            bool dailyOn, string dailyText, bool managed)
        {
            int hours = 0, minutes = 0, daily = 0;
            if (intervalOn && ((!String.IsNullOrWhiteSpace(hoursText) && !Int32.TryParse(hoursText, out hours)) ||
                (!String.IsNullOrWhiteSpace(minutesText) && !Int32.TryParse(minutesText, out minutes)) ||
                hours < 0 || hours > 168 || minutes < 0 || minutes > 59 || hours * 60 + minutes < 1 || hours * 60 + minutes > 10080))
                throw new ArgumentException("间隔须为 1 分钟至 168 小时；小时可留空，分钟填写 0–59。");
            if (dailyOn)
            {
                DateTime parsed;
                if (!DateTime.TryParseExact((dailyText ?? "").Trim(), new[] { "H:mm", "HH:mm" }, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out parsed)) throw new ArgumentException("每天时间请填写 24 小时制，例如 20:30。");
                daily = parsed.Hour * 60 + parsed.Minute + 1;
            }
            return new LanRequest { Operation = "reminder", ItemId = task.Id, ReminderIntervalMinutes = intervalOn ? hours * 60 + minutes : 0,
                DailyReminderMinutesPlusOne = daily, ReminderManaged = managed };
        }
        internal Window OpenReminder(LanTaskInfo task)
        {
            if (reminderDialog != null) { reminderDialog.Activate(); return reminderDialog; }
            long openedRevision = revision;
            Window dialog = new Window { Title = "事项提醒", Width = 440, Height = 560, MinWidth = 370, MinHeight = 430,
                Owner = window.IsVisible ? window : null, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
                Foreground = text, FontFamily = window.FontFamily, FontSize = 13, ShowInTaskbar = false, Icon = UiAssets.ApplicationIcon,
                UseLayoutRounding = true };
            dialog.Resources.MergedDictionaries.Add(window.Resources.MergedDictionaries[0]);
            UiInteraction.DisableHoverHints(dialog);
            Grid root = new Grid { Margin = new Thickness(22) };
            root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            dialog.Content = new Border { Background = card, BorderBrush = line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(15), Child = root };
            StackPanel body = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, FocusVisualStyle = null });
            DockPanel heading = new DockPanel();
            Button close = Button("", input, muted); close.Content = UiGlyphs.Close(); close.Width = close.Height = 30; close.MinWidth = 0; close.Padding = new Thickness(0);
            System.Windows.Automation.AutomationProperties.SetName(close, "关闭事项提醒");
            DockPanel.SetDock(close, Dock.Right); heading.Children.Add(close);
            heading.Children.Add(new TextBlock { Text = "事项提醒", FontSize = 23, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            heading.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) { if (!close.IsMouseOver) { try { dialog.DragMove(); } catch (InvalidOperationException) { } } };
            body.Children.Add(heading);
            body.Children.Add(new TextBlock { Text = task.Text, TextWrapping = TextWrapping.Wrap, Foreground = muted, Margin = new Thickness(0, 10, 0, 17), MaxHeight = 72 });
            CheckBox interval = ReminderSwitch("间隔提醒", "每隔指定时长提醒一次", task.ReminderIntervalMinutes > 0);
            body.Children.Add(interval);
            int initial = task.ReminderIntervalMinutes > 0 ? task.ReminderIntervalMinutes : 60;
            TextBox hours = InputBox(36), minutes = InputBox(36), daily = InputBox(36);
            hours.Text = (initial / 60).ToString(); minutes.Text = (initial % 60).ToString();
            hours.MaxLength = 3; minutes.MaxLength = 2; daily.MaxLength = 5;
            Grid intervalGrid = new Grid { Name = "IntervalInputs", Margin = new Thickness(0, 9, 0, 16) };
            intervalGrid.ColumnDefinitions.Add(new ColumnDefinition()); intervalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) }); intervalGrid.ColumnDefinitions.Add(new ColumnDefinition());
            FrameworkElement hourSpin = SpinInput(hours, "小时", 168), minuteSpin = SpinInput(minutes, "分钟", 59);
            intervalGrid.Children.Add(hourSpin); Grid.SetColumn(minuteSpin, 2); intervalGrid.Children.Add(minuteSpin); body.Children.Add(intervalGrid);
            CheckBox dailyOn = ReminderSwitch("每天定时提醒", "每天在指定时间提醒一次", task.DailyReminderMinutesPlusOne > 0); body.Children.Add(dailyOn);
            int at = task.DailyReminderMinutesPlusOne > 0 ? task.DailyReminderMinutesPlusOne - 1 : 1200;
            daily.Text = (at / 60).ToString("00") + ":" + (at % 60).ToString("00");
            FrameworkElement dailySpin = SpinInput(daily, "24 小时制", -1); dailySpin.Margin = new Thickness(0, 9, 0, 16); body.Children.Add(dailySpin);
            bool hasRules = task.ReminderIntervalMinutes > 0 || task.DailyReminderMinutesPlusOne > 0;
            CheckBox managed = ReminderSwitch("由管理员管理", "用户不能修改或关闭；完成事项后自动停止", hasRules ? task.ReminderManaged : preferences.DefaultReminderManaged);
            managed.Margin = new Thickness(0, 15, 0, 10); body.Children.Add(managed);
            TextBlock summary = new TextBlock { Foreground = muted, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 8) }; body.Children.Add(summary);
            TextBlock message = new TextBlock { Text = "小时可留空，只填分钟即可。两种提醒可以同时开启。", TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = muted }; body.Children.Add(message);
            Action refresh = delegate
            {
                intervalGrid.Visibility = interval.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
                dailySpin.Visibility = dailyOn.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
                managed.IsEnabled = interval.IsChecked == true || dailyOn.IsChecked == true;
                int h = 0, m = 0; Int32.TryParse(hours.Text, out h); Int32.TryParse(minutes.Text, out m);
                string intervalText = interval.IsChecked == true ? "每隔 " + (h > 0 ? h + " 小时 " : "") + m + " 分钟" : "";
                summary.Text = (intervalText + (dailyOn.IsChecked == true ? (intervalText.Length > 0 ? "；" : "") + "每天 " + daily.Text : "")).Trim();
                if (summary.Text.Length == 0) summary.Text = "此事项的提醒已关闭";
            };
            interval.Checked += delegate { refresh(); }; interval.Unchecked += delegate { refresh(); };
            dailyOn.Checked += delegate { refresh(); }; dailyOn.Unchecked += delegate { refresh(); };
            hours.TextChanged += delegate { refresh(); }; minutes.TextChanged += delegate { refresh(); }; daily.TextChanged += delegate { refresh(); }; refresh();
            StackPanel actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 15, 0, 0) }; Grid.SetRow(actions, 1); root.Children.Add(actions);
            Button clear = Button("清除提醒", input, text), cancel = Button("取消", input, text), save = Button("保存", accent, accentText);
            cancel.Click += delegate { dialog.Close(); };
            close.Click += delegate { if (save.IsEnabled) dialog.Close(); };
            clear.Click += delegate { interval.IsChecked = false; dailyOn.IsChecked = false; save.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); };
            save.Click += delegate
            {
                try
                {
                    LanRequest request = ReminderRequest(task, interval.IsChecked == true, hours.Text, minutes.Text, dailyOn.IsChecked == true, daily.Text,
                        managed.IsChecked == true && (interval.IsChecked == true || dailyOn.IsChecked == true));
                    request.ExpectedRevisionPlusOne = openedRevision + 1;
                    save.IsEnabled = cancel.IsEnabled = clear.IsEnabled = close.IsEnabled = body.IsEnabled = false; message.Text = "正在同步提醒…";
                    SendRequest(request, delegate
                    {
                        preferences.DefaultReminderManaged = managed.IsChecked == true;
                        try { preferences.Save(); } catch (Exception error) { status.Text = "提醒已同步，但偏好保存失败：" + error.Message; }
                        if (reminderDialog == dialog) dialog.Close();
                    }, delegate(string error) { save.IsEnabled = cancel.IsEnabled = clear.IsEnabled = close.IsEnabled = body.IsEnabled = true; refresh(); message.Text = error; });
                }
                catch (ArgumentException error) { message.Text = error.Message; }
            };
            actions.Children.Add(clear); actions.Children.Add(cancel); actions.Children.Add(save);
            dialog.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) { if (save.IsEnabled) dialog.Close(); e.Handled = true; }
                else if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0 && save.IsEnabled)
                { save.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); e.Handled = true; }
            };
            dialog.AddHandler(Keyboard.PreviewGotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(delegate(object sender, KeyboardFocusChangedEventArgs e)
            { Control focused = e.NewFocus as Control; if (focused != null) focused.FocusVisualStyle = null; }), true);
            dialog.Closed += delegate { reminderDialog = null; };
            reminderDialog = dialog; dialog.Show(); return dialog;
        }
        private CheckBox ReminderSwitch(string title, string description, bool enabled)
        {
            StackPanel caption = new StackPanel(); caption.Children.Add(new TextBlock { Text = title, Foreground = text });
            caption.Children.Add(new TextBlock { Text = description, Foreground = muted, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
            CheckBox toggle = new CheckBox { Content = caption, IsChecked = enabled, Style = window.Resources["AdminSwitch"] as Style, Margin = new Thickness(0, 4, 0, 4) };
            System.Windows.Automation.AutomationProperties.SetName(toggle, title); return toggle;
        }
        private FrameworkElement SpinInput(TextBox box, string unit, int maximum)
        {
            Grid row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(box); StackPanel arrows = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(arrows, 1); row.Children.Add(arrows);
            foreach (int delta in new[] { 1, -1 })
            {
                int step = delta; Button arrow = Button(step > 0 ? "▴" : "▾", input, muted); arrow.MinWidth = 0; arrow.Width = 24; arrow.Height = 18; arrow.Margin = new Thickness(0); arrow.Padding = new Thickness(0);
                arrow.Click += delegate
                {
                    if (maximum >= 0) { int value; Int32.TryParse(box.Text, out value); box.Text = Math.Max(0, Math.Min(maximum, value + step)).ToString(); }
                    else { DateTime time; if (!DateTime.TryParse(box.Text, out time)) time = DateTime.Today.AddHours(20); box.Text = time.AddMinutes(step).ToString("HH:mm"); }
                };
                arrows.Children.Add(arrow);
            }
            TextBlock suffix = new TextBlock { Text = unit, Foreground = muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 0, 0) }; Grid.SetColumn(suffix, 2); row.Children.Add(suffix); return row;
        }
        private Button Button(string label, Brush background, Brush foreground)
        { return new Button { Content = label, Height = 36, MinWidth = 78, Padding = new Thickness(11, 5, 11, 5),
            Margin = new Thickness(5, 0, 0, 0), Background = background, Foreground = foreground,
            BorderThickness = new Thickness(0), Cursor = Cursors.Hand, FocusVisualStyle = null,
            Style = window.Resources["AdminButton"] as Style }; }
        private TextBox InputBox(double height)
        { return new TextBox { Height = height, Padding = new Thickness(9, 4, 9, 4), Background = input,
            VerticalContentAlignment = VerticalAlignment.Center, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            Foreground = text, BorderBrush = line, Style = window.Resources["AdminInput"] as Style,
            FocusVisualStyle = null }; }
        private ComboBox GroupBox()
        {
            FrameworkElementFactory label = new FrameworkElementFactory(typeof(MarqueeLabel));
            label.SetBinding(MarqueeLabel.TextProperty, new Binding("DisplayName"));
            ComboBox combo = new ComboBox { Height = 34, Background = card, Foreground = text, BorderBrush = line,
                Style = window.Resources["AdminCombo"] as Style, ItemTemplate = new DataTemplate { VisualTree = label } };
            combo.SelectionChanged += delegate { LanGroupInfo selected = combo.SelectedItem as LanGroupInfo; combo.ToolTip = selected == null ? "分组" : selected.Name; };
            return combo;
        }
        private ListBox UserListBox()
        {
            ListBox list = new ListBox { Background = input, Foreground = text, BorderBrush = line,
                Padding = new Thickness(5), HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Style = window.Resources["AdminList"] as Style };
            ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
            FrameworkElementFactory label = new FrameworkElementFactory(typeof(TextBlock));
            label.SetBinding(TextBlock.TextProperty, new Binding("DisplayTitle"));
            label.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            label.SetValue(TextBlock.MarginProperty, new Thickness(0, 0, 0, 1));
            FrameworkElementFactory machine = new FrameworkElementFactory(typeof(TextBlock));
            machine.SetBinding(TextBlock.TextProperty, new Binding("DeviceName"));
            machine.SetValue(TextBlock.FontSizeProperty, 10.0);
            FrameworkElementFactory stack = new FrameworkElementFactory(typeof(StackPanel));
            stack.AppendChild(label); stack.AppendChild(machine);
            list.ItemTemplate = new DataTemplate { VisualTree = stack };
            return list;
        }

        internal void Show() { if (disposed) return; window.Show(); window.Activate(); if (peers.Count == 0) Discover(); }
        internal void Discover()
        {
            if (disposed || working) return; working = true; status.Text = "正在扫描…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<LanPeer> found = null; string error = null;
                try { found = DiscoverForTest == null ? LanService.Discover(700) : DiscoverForTest(); }
                catch (Exception failure) { error = "扫描失败，可重试：" + failure.Message; RecordFailure("LAN scan", failure); }
                Post(delegate
                {
                    if (disposed) return;
                    working = false;
                    if (error != null) { status.Text = error; return; }
                    peers = found ?? new List<LanPeer>();
                    RefreshPeerLists();
                    status.Text = peers.Count == 0 ? "未发现用户。请确认对方开启共享并放行 DeskTodo；若开启 TUN 严格路由，请在代理中允许局域网直连。"
                        : "发现 " + peers.Count + " 位用户";
                });
            });
        }
        private void RecordFailure(string operation, Exception error)
        { try { if (Log != null) Log(operation + ": " + error); } catch { } }
        private LanResponse SendSafely(LanPeer peer, LanRequest request)
        {
            try { return (SendForTest == null ? LanService.Send(peer, request) : SendForTest(peer, request)) ?? LanResponse.Fail("对方没有返回有效数据，可重新连接。"); }
            catch (Exception error) { RecordFailure("LAN command", error); return LanResponse.Fail("连接失败，可重试：" + error.Message); }
        }
        private void RefreshPeerLists()
        {
            string selectedId = selectedPeer == null ? null : selectedPeer.InstanceId;
            foreach (LanPeer peer in peers) peer.Alias = preferences.NameFor(peer) == peer.DisplayName ? null : preferences.NameFor(peer);
            refreshingPeerLists = true;
            users.ItemsSource = null; hiddenUsers.ItemsSource = null;
            users.ItemsSource = peers.Where(peer => !preferences.IsHidden(peer.InstanceId)).OrderBy(peer => peer.DisplayTitle).ToList();
            hiddenUsers.ItemsSource = peers.Where(peer => preferences.IsHidden(peer.InstanceId)).OrderBy(peer => peer.DisplayTitle).ToList();
            hiddenToggle.Content = "隐藏的用户（" + hiddenUsers.Items.Count + "）";
            if (selectedId != null) users.SelectedItem = peers.FirstOrDefault(peer => peer.InstanceId == selectedId && !preferences.IsHidden(peer.InstanceId));
            refreshingPeerLists = false;
            SelectPeer(users.SelectedItem as LanPeer);
        }
        private void SelectRightClickedUser(object sender, MouseButtonEventArgs e)
        {
            DependencyObject node = e.OriginalSource as DependencyObject;
            while (node != null && !(node is ListBoxItem)) node = VisualTreeHelper.GetParent(node);
            ListBoxItem item = node as ListBoxItem;
            if (item != null) item.IsSelected = true;
        }
        private void SelectPeer(LanPeer peer)
        {
            if (ReferenceEquals(peer, selectedPeer)) return;
            selectedPeer = peer; revision = -1; snapshot = null; deferredSnapshot = null; CloseEdit(); RenderRows();
            code.Text = peer == null ? "" : preferences.CodeFor(peer.InstanceId);
            remoteTitle.Text = peer == null ? "选择用户后查看待办" : peer.DisplayTitle + " 的待办";
            status.Text = peer == null ? "请选择用户" : String.IsNullOrEmpty(code.Text) ? "请输入该用户设置页显示的管理口令" : "已填入上次验证成功的管理口令";
            if (!testDataMode && peer != null && !String.IsNullOrEmpty(code.Text)) window.Dispatcher.BeginInvoke(new Action(delegate { RefreshRemote(true); }));
        }
        private void UserMenuOpening(object sender, ContextMenuEventArgs e)
        {
            LanPeer peer = users.SelectedItem as LanPeer;
            if (peer == null) { e.Handled = true; return; }
            ContextMenu menu = users.ContextMenu;
            menu.Items.Clear();
            MenuItem rename = new MenuItem { Header = "重命名" };
            rename.Click += delegate { ShowRenameDialog(peer); };
            MenuItem hide = new MenuItem { Header = "不显示该用户" };
            hide.Click += delegate { if (!preferences.HiddenIds.Contains(peer.InstanceId)) preferences.HiddenIds.Add(peer.InstanceId); preferences.Save(); RefreshPeerLists(); };
            MenuItem forget = new MenuItem { Header = "清除已记住的管理口令" };
            forget.Click += delegate { preferences.Codes.Remove(peer.InstanceId); preferences.Save(); if (selectedPeer == peer) code.Clear(); };
            menu.Items.Add(rename); menu.Items.Add(hide); menu.Items.Add(new Separator { Style = window.Resources[typeof(Separator)] as Style }); menu.Items.Add(forget);
            users.ContextMenu = menu;
        }
        private void RestoreUser(LanPeer peer)
        {
            preferences.HiddenIds.Remove(peer.InstanceId); preferences.Save(); RefreshPeerLists();
            users.SelectedItem = peer;
        }
        private void ShowRenameDialog(LanPeer peer) { OpenRename(peer, true); }
        internal Window OpenRename(LanPeer peer, bool modal = false)
        {
            Window dialog = new Window { Title = "重命名局域网用户", Owner = window, Width = 340, Height = 180,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
                ShowInTaskbar = false, FontFamily = window.FontFamily, FontSize = window.FontSize };
            dialog.Resources.MergedDictionaries.Add(window.Resources.MergedDictionaries[0]);
            UiInteraction.DisableHoverHints(dialog);
            Border surface = new Border { Background = card, BorderBrush = line, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14), Padding = new Thickness(20) }; dialog.Content = surface;
            StackPanel body = new StackPanel(); surface.Child = body;
            body.Children.Add(new TextBlock { Text = "为这位用户设置本机显示名称", Foreground = text, FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 12) });
            TextBox name = InputBox(36); name.Text = peer.DisplayTitle; name.MaxLength = 30; body.Children.Add(name);
            StackPanel actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 13, 0, 0) };
            Button cancel = Button("取消", input, text), save = Button("保存", accent, accentText);
            cancel.Click += delegate { dialog.Close(); };
            save.Click += delegate
            {
                string value = (name.Text ?? "").Trim();
                if (value.Length == 0) preferences.Aliases.Remove(peer.InstanceId);
                else preferences.Aliases[peer.InstanceId] = value;
                preferences.Save(); RefreshPeerLists();
                remoteTitle.Text = peer.DisplayTitle + " 的待办"; dialog.Close();
            };
            actions.Children.Add(cancel); actions.Children.Add(save); body.Children.Add(actions);
            dialog.Loaded += delegate { name.Focus(); name.SelectAll(); };
            dialog.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) { dialog.Close(); e.Handled = true; }
                else if (e.Key == Key.Enter) { save.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); e.Handled = true; }
            };
            if (modal) dialog.ShowDialog(); else dialog.Show(); return dialog;
        }

        private void RefreshRemote(bool announce)
        {
            if (working || UiDrag.IsActive || selectedPeer == null || String.IsNullOrWhiteSpace(code.Text)) return;
            working = true; string access = code.Text.Trim(); LanPeer peer = selectedPeer;
            long knownRevision = !announce && snapshot != null ? revision + 1 : 0;
            if (announce) status.Text = "正在连接 " + peer.DisplayTitle + "…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                LanResponse response = SendSafely(peer, new LanRequest { Operation = "snapshot", AccessCode = access,
                    KnownRevisionPlusOne = knownRevision });
                Post(delegate
                {
                    if (disposed) return; working = false;
                    if (selectedPeer != peer) return;
                    if (!response.Success) { status.Text = response.Error; snapshot = null; revision = -1; RenderRows(); return; }
                    if (preferences.CodeFor(peer.InstanceId) != access)
                    { preferences.Codes[peer.InstanceId] = access; try { preferences.Save(); } catch (Exception error) { RecordFailure("LAN preferences", error); } }
                    ApplySnapshot(response.Snapshot);
                    status.Text = "已连接 · 数据自动刷新";
                });
            });
        }
        private void ApplySnapshot(LanSnapshot value)
        {
            if (value == null || value.Revision <= revision) return;
            if (UiDrag.IsActive)
            {
                if (deferredSnapshot == null || value.Revision > deferredSnapshot.Revision) deferredSnapshot = value;
                return;
            }
            snapshot = value; revision = value.Revision;
            string selectedGroupId = (addGroup.SelectedItem as LanGroupInfo) == null ? null : ((LanGroupInfo)addGroup.SelectedItem).Id;
            string editingGroupId = (editGroup.SelectedItem as LanGroupInfo) == null ? null : ((LanGroupInfo)editGroup.SelectedItem).Id;
            List<LanGroupInfo> groups = new List<LanGroupInfo> { new LanGroupInfo { Id = null, Name = "未分组" } };
            groups.AddRange(value.Groups ?? new List<LanGroupInfo>());
            addGroup.ItemsSource = groups; editGroup.ItemsSource = groups;
            addGroup.SelectedItem = groups.FirstOrDefault(group => group.Id == selectedGroupId) ?? groups[0];
            if (editingTask != null) editGroup.SelectedItem = groups.FirstOrDefault(group => group.Id == editingGroupId) ?? groups[0];
            addGroup.Visibility = editGroup.Visibility = value.DisableGroups ? Visibility.Collapsed : Visibility.Visible;
            ((Grid)addGroup.Parent).ColumnDefinitions[1].Width = new GridLength(value.DisableGroups ? 0 : 116);
            RenderRows();
        }
        internal void ApplyDeferredSnapshot()
        {
            if (UiDrag.IsActive || deferredSnapshot == null) return;
            LanSnapshot next = deferredSnapshot; deferredSnapshot = null; ApplySnapshot(next);
        }
        internal void LoadPeersForTest(IEnumerable<LanPeer> found, bool simulateOnly = false)
        { testDataMode = simulateOnly; if (simulateOnly) refreshTimer.Stop(); peers = found.ToList(); RefreshPeerLists(); }
        internal void ApplySnapshotForTest(LanSnapshot value) { ApplySnapshot(value); }
        private void RenderRows()
        {
            pendingRows.Children.Clear(); completedRows.Children.Clear();
            string appearance = snapshot == null ? "" : snapshot.DisableGroups + ":" + String.Join("|", (snapshot.Groups ?? new List<LanGroupInfo>()).Select(g => g.Id + ":" + g.Name + ":" + g.Pinned));
            if (appearance != rowAppearance) { rowCache.Clear(); rowAppearance = appearance; }
            List<LanTaskInfo> pending = snapshot == null ? new List<LanTaskInfo>() : OrderedPending(snapshot).ToList();
            List<LanTaskInfo> done = snapshot == null ? new List<LanTaskInfo>() : snapshot.Tasks.Where(task => task.Completed).OrderByDescending(task => task.CompletedAt).ToList();
            HashSet<string> visibleIds = new HashSet<string>(pending.Concat(completedExpanded ? done : Enumerable.Empty<LanTaskInfo>()).Select(t => t.Id));
            foreach (string key in rowCache.Keys.Where(id => !visibleIds.Contains(id)).ToArray()) rowCache.Remove(key);
            pendingCount.Text = pending.Count + " 件";
            bool grouped = snapshot != null && !snapshot.DisableGroups && snapshot.Groups != null && snapshot.Groups.Count > 0;
            if (grouped)
            {
                ILookup<string, LanTaskInfo> members = pending.ToLookup(t => t.GroupId ?? "");
                foreach (LanGroupInfo group in snapshot.Groups.Where(g => g.Pinned)) AppendRemoteGroup(members[group.Id].ToList(), group.Id, group.Name);
                AppendRemoteGroup(members[""].ToList(), null, "未分组");
                foreach (LanGroupInfo group in snapshot.Groups.Where(g => !g.Pinned)) AppendRemoteGroup(members[group.Id].ToList(), group.Id, group.Name);
            }
            else foreach (LanTaskInfo task in pending) pendingRows.Children.Add(CachedTaskRow(task));
            completedHeader.Text = "已完成（" + done.Count + "）";
            StackPanel completedLabel = new StackPanel { Orientation = Orientation.Horizontal };
            FrameworkElement chevron = UiGlyphs.Chevron(!completedExpanded); chevron.Margin = new Thickness(0, 0, 6, 0);
            chevron.VerticalAlignment = VerticalAlignment.Center; completedLabel.Children.Add(chevron);
            if (completedHeader.Parent is Panel) ((Panel)completedHeader.Parent).Children.Remove(completedHeader);
            completedLabel.Children.Add(completedHeader); completedToggle.Content = completedLabel;
            completedRows.Visibility = completedExpanded ? Visibility.Visible : Visibility.Collapsed;
            if (completedExpanded) foreach (LanTaskInfo task in done) completedRows.Children.Add(CachedTaskRow(task));
        }
        internal static IEnumerable<LanTaskInfo> OrderedPending(LanSnapshot data)
        {
            IEnumerable<LanTaskInfo> pending = data.Tasks.Where(t => !t.Completed);
            if (data.PendingSort == "Manual") return pending.OrderByDescending(t => t.Pinned).ThenBy(t => t.SortOrder).ThenBy(t => t.CreatedAt);
            if (data.PendingSort == "Descending") return pending.OrderByDescending(t => t.Pinned).ThenByDescending(t => t.CreatedAt);
            return pending.OrderByDescending(t => t.Pinned).ThenBy(t => t.CreatedAt);
        }
        private void AppendRemoteGroup(List<LanTaskInfo> members, string id, string name)
        {
            Grid heading = new Grid { Height = 27, Margin = new Thickness(0, pendingRows.Children.Count == 0 ? 0 : 8, 0, 3) };
            heading.ColumnDefinitions.Add(new ColumnDefinition()); heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            heading.Children.Add(new TextBlock { Text = name, Foreground = muted, FontSize = 11, FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            TextBlock count = new TextBlock { Text = members.Count.ToString(), Foreground = muted, FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(count, 1); heading.Children.Add(count);
            UiDrag.GroupBoundary(heading, "DeskTodo.RemoteGroup");
            UiDrag.Target(heading, "DeskTodo.RemoteTask", delegate(object moving, bool after)
            {
                LanTaskInfo source = moving as LanTaskInfo;
                if (source != null) SendRequest(new LanRequest { Operation = "reorder", ItemId = source.Id, GroupId = id,
                    ExpectedRevisionPlusOne = revision + 1 }, null);
            }, null, true, delegate(object moving, bool after) { return RemoteGroupSlot(heading, (LanTaskInfo)moving); });
            pendingRows.Children.Add(heading);
            if (id != null)
            {
                heading.Background = Brushes.Transparent; heading.Cursor = Cursors.SizeAll;
                UiDrag.Source(heading, id, "DeskTodo.RemoteGroup", heading, true);
                UiDrag.Target(heading, "DeskTodo.RemoteGroup", delegate(object moving, bool after)
                { SendRequest(new LanRequest { Operation = "reorder-group", ItemId = moving as string, TargetId = id, After = after, ExpectedRevisionPlusOne = revision + 1 }, null); },
                    delegate(object moving)
                    {
                        LanGroupInfo source = snapshot.Groups.FirstOrDefault(g => g.Id == moving as string), target = snapshot.Groups.FirstOrDefault(g => g.Id == id);
                        return source != null && target != null && source.Pinned == target.Pinned;
                    });
            }
            foreach (LanTaskInfo task in members) pendingRows.Children.Add(CachedTaskRow(task));
        }
        private FrameworkElement CachedTaskRow(LanTaskInfo task)
        {
            string signature = task.Text + "\0" + task.GroupId + ":" + task.Completed + ":" + task.CreatedAt + ":" + task.CompletedAt + ":" + task.Pinned + ":" + task.SortOrder +
                ":" + task.ReminderIntervalMinutes + ":" + task.DailyReminderMinutesPlusOne + ":" + task.ReminderManaged;
            CachedRemoteRow cached;
            if (!rowCache.TryGetValue(task.Id, out cached) || cached.Signature != signature)
            { cached = new CachedRemoteRow { Signature = signature, Row = TaskRow(task) }; rowCache[task.Id] = cached; }
            cached.Row.Tag = task; cached.Row.RenderTransform = Transform.Identity; cached.Row.Opacity = 1;
            // Event closures and drag payloads keep an equivalent immutable snapshot;
            // IDs, not object references, identify an item across network refreshes.
            CheckBox check = ((Grid)cached.Row).Children.OfType<CheckBox>().Single(); check.IsChecked = task.Completed;
            return cached.Row;
        }
        private FrameworkElement TaskRow(LanTaskInfo task)
        {
            Grid row = new Grid { Margin = new Thickness(0, 0, 0, 3), MinHeight = 40, Background = Brushes.Transparent,
                Tag = task, Cursor = Cursors.Arrow };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(27) });
            CheckBox check = new CheckBox { IsChecked = task.Completed, Style = window.Resources["AdminCheck"] as Style,
                Width = 22, Height = 28, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 0, 0),
                FocusVisualStyle = null, ToolTip = task.Completed ? "恢复为未完成" : "完成事项" };
            check.Click += delegate { SendChange("update", task, task.Text, task.GroupId, check.IsChecked == true); };
            row.Children.Add(check);
            TextBlock body = new TextBlock { Text = task.Text, TextWrapping = TextWrapping.Wrap, Foreground = task.Completed ? muted : text,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 5, 6, 5), Cursor = task.Completed ? Cursors.Arrow : Cursors.Hand };
            if (task.Completed) body.TextDecorations = TextDecorations.Strikethrough;
            body.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            { if (!task.Completed && e.ClickCount >= 2) { OpenEdit(task); e.Handled = true; } };
            Grid.SetColumn(body, 1); row.Children.Add(body);
            long ticks = task.Completed ? task.CompletedAt : task.CreatedAt;
            string time = ticks > 0 && ticks <= DateTime.MaxValue.Ticks ? new DateTime(ticks, DateTimeKind.Utc).ToLocalTime().ToString("M/d HH:mm") : "";
            TextBlock clock = new TextBlock { Text = time, Foreground = muted, FontSize = 10, VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 3, 0) };
            Grid.SetColumn(clock, 2); row.Children.Add(clock);
            Button more = Button("⋮", Brushes.Transparent, muted); more.MinWidth = 0; more.Width = 25; more.Height = 29;
            more.Padding = new Thickness(0); more.Margin = new Thickness(0, 2, 0, 0);
            more.Click += delegate { more.ContextMenu.IsOpen = true; };
            more.ContextMenu = TaskMenu(task); row.ContextMenu = more.ContextMenu;
            more.ContextMenu.PlacementTarget = more; more.ContextMenu.Placement = PlacementMode.Left;
            Grid.SetColumn(more, 3); row.Children.Add(more);
            if (!task.Completed)
            {
                UiDrag.Source(row, task, "DeskTodo.RemoteTask");
                UiDrag.Target(row, "DeskTodo.RemoteTask", delegate(object moving, bool after)
                {
                    LanTaskInfo source = moving as LanTaskInfo;
                    if (source == null || source.Id == task.Id) return;
                    SendRequest(new LanRequest { Operation = "reorder", ItemId = source.Id, TargetId = task.Id, After = after, ExpectedRevisionPlusOne = revision + 1 }, null);
                }, delegate(object moving) { LanTaskInfo source = moving as LanTaskInfo; return source != null && source.Id != task.Id && source.Pinned == task.Pinned; });
            }
            return row;
        }
        private UiDrag.Slot RemoteGroupSlot(FrameworkElement heading, LanTaskInfo moving)
        {
            int first = pendingRows.Children.IndexOf(heading) + 1, end = first, last = -1;
            while (end < pendingRows.Children.Count && !UiDrag.IsBoundary(pendingRows.Children[end] as FrameworkElement, "DeskTodo.RemoteGroup")) end++;
            for (int i = first; i < end; i++)
            {
                LanTaskInfo item = ((FrameworkElement)pendingRows.Children[i]).Tag as LanTaskInfo;
                if (item != null && item.Id != moving.Id && item.Pinned == moving.Pinned) last = i;
            }
            return new UiDrag.Slot(pendingRows, last >= 0 ? last + 1 : moving.Pinned ? first : end);
        }
        private ContextMenu TaskMenu(LanTaskInfo task)
        {
            ContextMenu menu = NewMenu();
            if (!task.Completed)
            {
                MenuItem edit = new MenuItem { Header = "编辑事项" }; edit.Click += delegate { OpenEdit(task); }; menu.Items.Add(edit);
                MenuItem move = new MenuItem { Header = "移动到分组" };
                List<LanGroupInfo> groups = new List<LanGroupInfo> { new LanGroupInfo { Id = null, Name = "未分组" } };
                if (snapshot != null && snapshot.Groups != null) groups.AddRange(snapshot.Groups);
                foreach (LanGroupInfo target in groups)
                {
                    LanGroupInfo choice = target;
                    MenuItem destination = new MenuItem { Header = choice.Name };
                    destination.Click += delegate { SendChange("update", task, task.Text, choice.Id, task.Completed); };
                    move.Items.Add(destination);
                }
                if (snapshot == null || !snapshot.DisableGroups) menu.Items.Add(move);
                MenuItem reminder = new MenuItem { Header = task.ReminderIntervalMinutes > 0 || task.DailyReminderMinutesPlusOne > 0 ? "设置提醒（已开启）" : "设置提醒" };
                reminder.Click += delegate { OpenReminder(task); }; menu.Items.Add(reminder);
            }
            MenuItem delete = new MenuItem { Header = "删除（用户端可撤销）" };
            delete.Click += delegate { SendChange("delete", task, null, null, false); };
            menu.Items.Add(delete); return menu;
        }
        private void AddTask()
        {
            string value = (addText.Text ?? "").Trim(); if (value.Length == 0) return;
            LanGroupInfo group = addGroup.SelectedItem as LanGroupInfo;
            SendChange("add", null, value, snapshot != null && snapshot.DisableGroups ? null : group == null ? null : group.Id, false);
        }
        private void OpenEdit(LanTaskInfo task)
        {
            editingTask = task; editingRevision = revision; editText.Text = task.Text; editCompleted.IsChecked = task.Completed;
            List<LanGroupInfo> groups = editGroup.ItemsSource as List<LanGroupInfo>;
            if (groups != null) editGroup.SelectedItem = groups.FirstOrDefault(group => group.Id == task.GroupId) ?? groups[0];
            foreach (FrameworkElement section in browseSections) section.Visibility = Visibility.Hidden;
            editLayer.Visibility = Visibility.Visible; editText.Focus(); editText.SelectAll();
        }
        internal void OpenEditForTest(LanTaskInfo task) { OpenEdit(task); }
        private void CloseEdit()
        {
            editLayer.Visibility = Visibility.Collapsed; editingTask = null;
            foreach (FrameworkElement section in browseSections) section.Visibility = Visibility.Visible;
        }
        private void SaveEdit()
        {
            if (editingTask == null) return;
            string value = (editText.Text ?? "").Trim(); if (value.Length == 0) { editText.Focus(); return; }
            LanGroupInfo group = editGroup.SelectedItem as LanGroupInfo;
            SendChange("update", editingTask, value, snapshot != null && snapshot.DisableGroups ? editingTask.GroupId : group == null ? null : group.Id, editCompleted.IsChecked == true, editingRevision + 1);
        }
        private void SendChange(string operation, LanTaskInfo task, string value, string groupId, bool done, long expectedRevision = 0)
        {
            SendRequest(new LanRequest { Operation = operation, ItemId = task == null ? null : task.Id,
                Text = value, GroupId = groupId, Completed = done, ExpectedRevisionPlusOne = expectedRevision }, delegate
                {
                    if (operation == "add") addText.Clear();
                    if (operation == "update" && editLayer.Visibility == Visibility.Visible && editingTask == task) CloseEdit();
                });
        }
        private void SendRequest(LanRequest request, Action success, Action<string> failure = null)
        {
            if (working) { status.Text = "正在同步，请稍候。"; if (failure != null) failure(status.Text); RenderRows(); return; }
            if (selectedPeer == null || String.IsNullOrWhiteSpace(code.Text))
            { status.Text = "请先选择用户并填写管理口令。"; if (failure != null) failure(status.Text); RenderRows(); return; }
            LanPeer peer = selectedPeer; string access = code.Text.Trim();
            request.AccessCode = access;
            working = true; status.Text = "正在同步…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                LanResponse response = SendSafely(peer, request);
                Post(delegate
                {
                    if (disposed) return; working = false;
                    if (selectedPeer != peer) { if (failure != null) failure("当前用户已切换，请关闭此提醒窗口后重新设置。"); return; }
                    if (!response.Success) { status.Text = response.Error; if (failure != null) failure(response.Error); RenderRows(); return; }
                    ApplySnapshot(response.Snapshot);
                    if (success != null) success();
                    status.Text = "修改已同步到 " + peer.DisplayTitle;
                });
            });
        }
        private void Post(Action action)
        {
            if (window.Dispatcher.HasShutdownStarted || window.Dispatcher.HasShutdownFinished) return;
            try { window.Dispatcher.BeginInvoke(new Action(delegate
            {
                if (disposed) return;
                try { action(); }
                catch (Exception error) { working = false; status.Text = "操作未完成，可重试：" + error.Message; RecordFailure("LAN UI", error); }
            })); }
            catch (InvalidOperationException) { /* The dispatcher can shut down between the check and enqueue. */ }
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true; refreshTimer.Stop();
            if (reminderDialog != null) reminderDialog.Close();
            try { if (window.IsVisible) window.Close(); } catch { }
        }
    }
}
