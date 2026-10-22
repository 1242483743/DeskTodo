using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace DesktopTodo
{
    // Optional tools live outside the card controller; the familiar home layout
    // and its drag/drop engine are deliberately kept intact.
    public sealed partial class TodoWindow
    {
        public Action HistoryRequested;
        public Action<string[]> PurgeDeletedRequested;
        private const int CompletedPageSize = 50;
        private int completedVisibleLimit = CompletedPageSize;
        private Button completedMoreButton;
        private Border toastSurface;
        private TextBlock toastText;
        private DispatcherTimer toastTimer;
        private DateTime toastExpiresAt;
        internal string ToastText { get { return toastSurface != null && toastSurface.Visibility == Visibility.Visible ? toastText.Text : ""; } }
        private DispatcherTimer searchTimer;
        private string searchQuery = "";
        private bool completedBeforeSearch;
        private bool searchExpanded;
        private readonly HashSet<string> searchCollapsedGroups = new HashSet<string>();
        private ComboBox timeDisplaySelect;
        private TextBlock timeDisplayLabel;
        private StackPanel lanConnectionDetails;
        private Button recentDeletedButton, historyButton, updateButton;
        private readonly List<Button> advancedButtons = new List<Button>();
        private bool featureReady;
        private bool compactSearchLayout;
        internal bool SearchOpen { get { return Control<FrameworkElement>("SearchBar").Visibility == Visibility.Visible; } }
        private bool CompactSearch
        {
            get { return featureReady && SearchOpen && (Window.ActualHeight > 0 ? Window.ActualHeight : Window.Height) / displayCompensation / contentScale < 450; }
        }

        private void BuildProductivityFeatures()
        {
            completedMoreButton = FeatureButton("加载更多", "Load more", delegate
            {
                ScrollViewer scroll = Control<ScrollViewer>(CompactSearch ? "PendingScroll" : "CompletedScroll");
                double offset = scroll.VerticalOffset;
                completedVisibleLimit += CompletedPageSize; Refresh();
                scroll.ScrollToVerticalOffset(offset);
            });
            Window.RegisterName("CompletedLoadMore", completedMoreButton);
            toastText = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap, MaxHeight = 72, TextTrimming = TextTrimming.CharacterEllipsis };
            toastText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            toastSurface = new Border { Child = toastText, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(18, 0, 18, 44), MaxWidth = 300,
                VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center,
                IsHitTestVisible = false, Visibility = Visibility.Collapsed };
            toastSurface.SetResourceReference(Border.BackgroundProperty, "InputBrush");
            toastSurface.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
            toastSurface.LayoutTransform = new ScaleTransform(contentScale, contentScale);
            toastSurface.Margin = new Thickness(18, 0, 18, 44 * contentScale);
            // The category backdrop is a sibling of CardContent, not its child.
            // Put notifications in that same layer stack so they stay legible.
            Panel.SetZIndex(toastSurface, 30); ((Grid)Control<Border>("Card").Child).Children.Add(toastSurface);
            Window.RegisterName("ToastSurface", toastSurface);
            toastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            toastTimer.Tick += delegate { if (DateTime.UtcNow >= toastExpiresAt) HideToast(); };
            // All page-level actions share the same top baseline and right edge.
            StackPanel actions = Control<StackPanel>("HeaderActions");
            Button menu = Control<Button>("MenuButton");
            actions.Children.Remove(menu); actions.Children.Add(menu);
            foreach (Button action in actions.Children.OfType<Button>()) { action.Height = 34; action.Margin = new Thickness(0); }
            actions.SizeChanged += delegate { SizeHeaderTitle(); };
            Control<FrameworkElement>("DragHeader").SizeChanged += delegate { SizeHeaderTitle(); };
            // Search replaces the add bar, rather than stealing another row from
            // the list on a minimum-size card. Its name stays in the window scope.
            FrameworkElement searchBar = Control<FrameworkElement>("SearchBar");
            ((Panel)VisualTreeHelper.GetParent(searchBar)).Children.Remove(searchBar);
            Grid.SetRow(searchBar, 1); searchBar.Margin = Control<Border>("AddBar").Margin;
            Control<Grid>("MainPanel").Children.Add(searchBar);
            Control<Button>("SearchClose").Content = UiGlyphs.Close();
            Control<TextBlock>("SearchResultLabel").SizeChanged += delegate { SizeCompletedArea(); };
            Control<Button>("SearchButton").Click += delegate { SetSearchOpen(!SearchOpen); };
            Control<Button>("SearchClose").Click += delegate { SetSearchOpen(false); };
            searchTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(120) };
            searchTimer.Tick += delegate { searchTimer.Stop(); ApplySearch(); };
            Control<TextBox>("SearchInput").TextChanged += delegate { searchTimer.Stop(); searchTimer.Start(); };
            Window.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Handled) return;
                if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control &&
                    !EditOpen && !ReminderOpen && !OverlayOpen && !SettingsOpen && !GroupOpen)
                { SetSearchOpen(true); e.Handled = true; }
                else if (e.Key == Key.Escape && SearchOpen && !SettingsOpen && !GroupOpen && !EditOpen && !ReminderOpen && !OverlayOpen)
                { SetSearchOpen(false); e.Handled = true; }
            };
            Window.Closed += delegate { featureWindowClosed = true; searchTimer.Stop(); toastTimer.Stop(); };
            Window.SizeChanged += delegate { if (featureReady && compactSearchLayout != CompactSearch) Refresh(); };

            StackPanel basic = settingsCategoryContents["BasicSectionLabel"];
            StackPanel advanced = CollapseAdvanced(basic, "高级设置", "Advanced options");
            MoveRange(basic, advanced, Control<FrameworkElement>("SecondaryStartupToggle"), Control<FrameworkElement>("SecondaryStartupStatus"));
            MoveRange(basic, advanced, Control<FrameworkElement>("HotkeyLabel"), Control<FrameworkElement>("HotkeyStatus"));
            MoveElement(Control<Button>("ResetButton"), advanced);

            StackPanel appearance = settingsCategoryContents["CustomSectionLabel"];
            StackPanel theme = settingsCategoryContents["ThemeLabel"];
            // Present the most common appearance choices first.
            UIElement[] themeControls = theme.Children.Cast<UIElement>().ToArray(); theme.Children.Clear();
            int insert = 0;
            foreach (UIElement child in themeControls) appearance.Children.Insert(insert++, child);
            Button oldThemeEntry = settingsCategoryButtons["ThemeLabel"];
            ((StackPanel)VisualTreeHelper.GetParent(oldThemeEntry)).Children.Remove(oldThemeEntry);
            Control<CheckBox>("ShowTimesToggle").Visibility = Visibility.Collapsed;
            Control<CheckBox>("ShowDateToggle").Visibility = Visibility.Collapsed;
            timeDisplaySelect = new ComboBox { Style = Window.Resources["ModernComboBox"] as Style, Height = 38,
                Margin = new Thickness(10, 0, 10, 12) };
            Window.RegisterName("TimeDisplaySelect", timeDisplaySelect);
            System.Windows.Automation.AutomationProperties.SetName(timeDisplaySelect, "事项时间显示方式");
            timeDisplayLabel = FeatureLabel("事项时间", "Task timestamps");
            appearance.Children.Insert(insert++, timeDisplayLabel);
            appearance.Children.Insert(insert++, timeDisplaySelect);
            timeDisplaySelect.SelectionChanged += delegate
            {
                if (suppressSettingsEvents || !featureReady) return;
                State.HideItemTimes = timeDisplaySelect.SelectedIndex == 0;
                State.ShowItemDates = timeDisplaySelect.SelectedIndex == 2;
                NotifySettingsChanged(); RefreshTaskTimes(); RefreshSettings();
            };
            StackPanel extraAppearance = CollapseAdvanced(appearance, "高级设置", "Advanced options");
            UIElement[] extras = appearance.Children.Cast<UIElement>().Where(c => c != timeDisplaySelect && c != timeDisplayLabel &&
                !themeControls.Contains(c) && !(c is Button && advancedButtons.Contains((Button)c)) && c != extraAppearance).ToArray();
            foreach (UIElement extra in extras) { appearance.Children.Remove(extra); extraAppearance.Children.Add(extra); }
            // Name edits commit when leaving the field, just like other preferences.
            Control<Button>("TitleSaveButton").Visibility = Visibility.Collapsed;
            Grid.SetColumnSpan(titleSettingInput, 3);
            titleSettingInput.LostKeyboardFocus += delegate
            { if (!String.IsNullOrWhiteSpace(titleSettingInput.Text) && titleSettingInput.Text.Trim() != State.Title) SaveTitleFromSettings(); };
            Window.PreviewMouseDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (titleSettingInput.IsKeyboardFocusWithin && !IsInside(e.OriginalSource as DependencyObject, titleSettingInput) &&
                    !String.IsNullOrWhiteSpace(titleSettingInput.Text) && titleSettingInput.Text.Trim() != State.Title) SaveTitleFromSettings();
            };

            StackPanel lan = settingsCategoryContents["LanSectionLabel"];
            lanConnectionDetails = new StackPanel();
            UIElement first = DirectChild(lan, Control<FrameworkElement>("LanNameLabel"));
            UIElement last = DirectChild(lan, Control<Button>("LanRegenerateButton"));
            int at = lan.Children.IndexOf(first);
            MoveRange(lan, lanConnectionDetails, (FrameworkElement)first, (FrameworkElement)last);
            lan.Children.Insert(at, lanConnectionDetails);

            StackPanel management = settingsCategoryContents["ManagementLabel"];
            recentDeletedButton = FeatureButton("最近删除", "Recently deleted", OpenRecentlyDeleted);
            historyButton = FeatureButton("自动历史备份", "Backup history", delegate { if (HistoryRequested != null) HistoryRequested(); });
            updateButton = FeatureButton("检查更新", "Check for updates", CheckForUpdates);
            management.Children.Insert(0, recentDeletedButton); management.Children.Insert(1, historyButton);
            management.Children.Add(updateButton);
            Window.RegisterName("CheckUpdatesButton", updateButton);
            Button exit = Control<Button>("ExitButton");
            management.Children.Remove(exit);
            foreach (FrameworkElement row in management.Children)
            {
                row.Margin = new Thickness(0, 0, 0, 8);
                Grid pair = row as Grid;
                if (pair != null) foreach (Button child in pair.Children.OfType<Button>()) child.Margin = new Thickness(0);
            }
            exit.Margin = new Thickness(0, 12, 0, 0); management.Children.Add(exit);
            featureReady = true;
            RefreshProductivitySettings();
        }
        private TextBlock FeatureLabel(string zh, string en)
        {
            TextBlock label = new TextBlock { Text = L(zh, en), Tag = new[] { zh, en }, FontSize = 11,
                Margin = new Thickness(10, 12, 10, 8), TextWrapping = TextWrapping.Wrap };
            label.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); return label;
        }
        private void SizeHeaderTitle()
        {
            double width = Control<FrameworkElement>("DragHeader").ActualWidth;
            if (width <= 0) return;
            // Optional window buttons must not overlap the title on narrow cards.
            double available = width - Control<StackPanel>("HeaderActions").ActualWidth - 46;
            Control<FrameworkElement>("HeaderTitleSlot").Width = Math.Max(0, Math.Min(126, available));
        }
        private Button FeatureButton(string zh, string en, Action click)
        {
            Button button = new Button { Content = L(zh, en), Tag = new[] { zh, en },
                Style = Window.Resources["ActionButton"] as Style, Margin = new Thickness(0, 0, 0, 8) };
            button.Click += delegate { click(); }; return button;
        }
        private StackPanel CollapseAdvanced(StackPanel parent, string zh, string en)
        {
            StackPanel content = new StackPanel { Visibility = Visibility.Collapsed };
            Button button = FeatureButton(zh + "  ›", en + "  ›", delegate
            { content.Visibility = content.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; });
            advancedButtons.Add(button); parent.Children.Add(button); parent.Children.Add(content); return content;
        }
        private static UIElement DirectChild(StackPanel parent, FrameworkElement element)
        {
            DependencyObject child = element;
            while (child != null && VisualTreeHelper.GetParent(child) != parent) child = VisualTreeHelper.GetParent(child);
            return child as UIElement;
        }
        private static void MoveRange(StackPanel from, StackPanel to, FrameworkElement first, FrameworkElement last)
        {
            int start = from.Children.IndexOf(DirectChild(from, first));
            int end = from.Children.IndexOf(DirectChild(from, last));
            if (start < 0 || end < start) return;
            UIElement[] move = from.Children.Cast<UIElement>().Skip(start).Take(end - start + 1).ToArray();
            foreach (UIElement child in move) { from.Children.Remove(child); to.Children.Add(child); }
        }
        private static void MoveElement(FrameworkElement element, StackPanel to)
        {
            DependencyObject node = element;
            while (node != null && !(VisualTreeHelper.GetParent(node) is StackPanel)) node = VisualTreeHelper.GetParent(node);
            if (node == null) return;
            StackPanel from = (StackPanel)VisualTreeHelper.GetParent(node);
            from.Children.Remove((UIElement)node); to.Children.Add((UIElement)node);
        }
        private void RefreshProductivitySettings()
        {
            if (!featureReady) return;
            Control<TextBlock>("CustomSectionLabel").Text = L("外观", "Appearance");
            Control<TextBlock>("ManagementLabel").Text = L("数据与管理", "Data and maintenance");
            timeDisplayLabel.Text = L("事项时间", "Task timestamps");
            foreach (Button button in advancedButtons.Concat(new[] { recentDeletedButton, historyButton, updateButton }))
            { string[] texts = button.Tag as string[]; if (texts != null) button.Content = L(texts[0], texts[1]); }
            recentDeletedButton.Content = L("最近删除", "Recently deleted") + "（" + State.RecentlyDeleted.Count() + "）";
            bool previousSuppress = suppressSettingsEvents; suppressSettingsEvents = true;
            try
            {
                string[] labels = IsEnglish ? new[] { "Hidden", "Time only", "Month/day and time" } : new[] { "不显示", "仅时间", "月日和时间" };
                if (timeDisplaySelect.Items.Count == 0 || (string)timeDisplaySelect.Items[0] != labels[0])
                { timeDisplaySelect.Items.Clear(); foreach (string label in labels) timeDisplaySelect.Items.Add(label); }
                timeDisplaySelect.SelectedIndex = State.HideItemTimes ? 0 : State.ShowItemDates ? 2 : 1;
            }
            finally { suppressSettingsEvents = previousSuppress; }
            lanConnectionDetails.Visibility = State.LanSharingEnabled ? Visibility.Visible : Visibility.Collapsed;
            UpdateSettingsCategoryLabels();
        }
        internal void ExpandAdvancedSettings()
        {
            foreach (Button button in advancedButtons)
            {
                StackPanel parent = VisualTreeHelper.GetParent(button) as StackPanel;
                int at = parent.Children.IndexOf(button);
                ((FrameworkElement)parent.Children[at + 1]).Visibility = Visibility.Visible;
            }
        }
        internal void SetSearchOpen(bool open)
        {
            if (open && !SearchOpen)
            {
                completedBeforeSearch = Control<Expander>("CompletedSection").IsExpanded;
                searchExpanded = false; searchCollapsedGroups.Clear();
            }
            Control<FrameworkElement>("SearchBar").Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            Control<FrameworkElement>("AddBar").Visibility = open ? Visibility.Collapsed : Visibility.Visible;
            Control<FrameworkElement>("SearchResultLabel").Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            if (open) { Refresh(); Control<TextBox>("SearchInput").Focus(); Control<TextBox>("SearchInput").SelectAll(); }
            else
            {
                searchTimer.Stop(); Control<TextBox>("SearchInput").Clear(); searchTimer.Stop(); searchQuery = ""; searchCollapsedGroups.Clear();
                Control<Expander>("CompletedSection").IsExpanded = completedBeforeSearch;
                searchExpanded = false; Refresh(); Input.Focus();
            }
            RefreshSearchResults();
        }
        internal void ApplySearch()
        {
            searchTimer.Stop(); string next = Control<TextBox>("SearchInput").Text.Trim();
            if (next != searchQuery) { searchCollapsedGroups.Clear(); completedVisibleLimit = CompletedPageSize; }
            searchQuery = next;
            if (searchQuery.Length > 0 && !searchExpanded)
            { searchExpanded = true; Control<Expander>("CompletedSection").IsExpanded = true; }
            Refresh(); Control<ScrollViewer>("PendingScroll").ScrollToHome();
        }
        private bool MatchesSearch(TodoItem item)
        {
            if (searchQuery.Length == 0) return true;
            TodoGroup group = State.Groups.FirstOrDefault(g => g.Id == item.GroupId);
            return item.Text.IndexOf(searchQuery, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                (group != null && group.Name.IndexOf(searchQuery, StringComparison.CurrentCultureIgnoreCase) >= 0);
        }
        private void RefreshSearchResults()
        {
            if (!featureReady) return;
            int pending = VisiblePending().Count(), done = VisibleCompleted().Count();
            Control<TextBlock>("SearchResultLabel").Text = searchQuery.Length == 0
                ? L("按内容或分组查找全部未完成和已完成事项", "Search all pending and completed tasks by text or group")
                : L("找到 " + pending + " 条未完成、" + done + " 条已完成", "Found " + pending + " pending and " + done + " completed");
            if (searchQuery.Length > 0 && pending == 0)
            {
                Control<TextBlock>("EmptyTitle").Text = CompactSearch ? L("没有匹配的事项", "No matching tasks") : L("没有匹配的未完成事项", "No matching pending tasks");
                Control<TextBlock>("EmptySubtitle").Text = CompactSearch ? L("换个关键词再试试", "Try another keyword") : L("试试其他关键词，或查看下方已完成", "Try another keyword or see completed results below");
            }
        }
        internal void OpenRecentlyDeleted()
        {
            if (Window.FindName("PurgeDeletedButton") != null) Window.UnregisterName("PurgeDeletedButton");
            StackPanel page = new StackPanel();
            page.Children.Add(FeatureLabel("删除的事项保留在本机；恢复后保留原分组、完成状态和时间。", "Deleted tasks stay local. Restore keeps their group and completion time."));
            List<TodoItem> items = State.RecentlyDeleted.ToList();
            if (items.Count == 0) page.Children.Add(FeatureLabel("暂无已删除事项", "No deleted tasks"));
            StackPanel rows = new StackPanel(); page.Children.Add(rows);
            int shown = 0;
            Button more = null;
            Action append = delegate
            {
                foreach (TodoItem item in items.Skip(shown).Take(50))
                {
                    Grid row = new Grid { Margin = new Thickness(4, 4, 4, 8) };
                    row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    TextBlock label = new TextBlock { Text = item.Text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 5, 8, 5) };
                    label.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush"); row.Children.Add(label);
                    Button restore = FeatureButton("恢复", "Restore", delegate
                    {
                        State.Restore(item); ClearUndoHistory(); NotifyChanged(); RefreshSettings(); OpenRecentlyDeleted();
                    });
                    restore.MinWidth = 56; Grid.SetColumn(restore, 1); row.Children.Add(restore); rows.Children.Add(row);
                }
                shown = Math.Min(items.Count, shown + 50);
                if (more != null)
                {
                    more.Visibility = shown < items.Count ? Visibility.Visible : Visibility.Collapsed;
                    more.Content = L("加载更多（剩余 " + (items.Count - shown) + " 条）", "Load more (" + (items.Count - shown) + " remaining)");
                }
            };
            more = FeatureButton("加载更多", "Load more", delegate { append(); }); page.Children.Add(more); append();
            if (items.Count > 0 && PurgeDeletedRequested != null)
            {
                Button clear = FeatureButton("清空最近删除…", "Clear recently deleted…", delegate
                {
                    // Capture only IDs present when confirmation was requested.
                    string[] ids = items.Select(item => item.Id).ToArray();
                    ShowConfirmation(L("清空最近删除", "Clear recently deleted"),
                        L("将从当前清单移除这 " + ids.Length + " 条已删除记录，不影响未完成和已完成事项。操作前会自动保存完整备份；清理后可通过导入该备份恢复。",
                        "Remove these " + ids.Length + " deleted records only. Active tasks are untouched. A full backup is saved first; import that backup to restore them."),
                        L("备份并清空", "Back up and clear"), delegate { PurgeDeletedRequested(ids); });
                });
                clear.Margin = new Thickness(0, 14, 0, 0); Window.RegisterName("PurgeDeletedButton", clear); page.Children.Add(clear);
            }
            ShowFeaturePage(page, L("最近删除", "Recently deleted"));
        }
        private void AppendCompletedPage(List<UIElement> rows, List<TodoItem> items)
        {
            foreach (TodoItem item in items.Take(completedVisibleLimit)) rows.Add(CachedTaskRow(item));
            if (items.Count <= completedVisibleLimit) return;
            int left = items.Count - completedVisibleLimit;
            completedMoreButton.Content = L("加载更多（剩余 " + left + " 条）", "Load more (" + left + " remaining)");
            rows.Add(completedMoreButton);
        }
        internal void ShowToast(string message)
        {
            if (featureWindowClosed) return;
            toastText.Text = message; toastExpiresAt = DateTime.UtcNow.AddSeconds(3);
            toastSurface.BeginAnimation(UIElement.OpacityProperty, null); toastSurface.Opacity = 1;
            toastSurface.Visibility = Visibility.Visible;
            // Tiny notifications need only a fade, not a raster cache or moving
            // text. Keep glyph rendering identical before and after the animation.
            if (SystemParameters.ClientAreaAnimation)
                toastSurface.BeginAnimation(UIElement.OpacityProperty,
                    new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120))
                    { FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop });
            toastTimer.Start();
        }
        private void HideToast() { if (toastSurface != null) toastSurface.Visibility = Visibility.Collapsed; if (toastTimer != null) toastTimer.Stop(); }
        internal void ExpireToastForTest() { toastExpiresAt = DateTime.UtcNow.AddSeconds(-1); HideToast(); }
        private void ShowFeaturePage(StackPanel content, string title)
        {
            OpenSettingsCategory("ManagementLabel");
            settingsCategoryScroll.Content = content; settingsCategoryTitle.Text = title;
        }
        internal void ShowBackupHistory(string[] files, Action<string> restore, string warning)
        {
            StackPanel page = new StackPanel();
            page.Children.Add(FeatureLabel("每日首次保存前保留一份历史，最多保留最近 30 份。恢复前还会备份当前清单。", "A snapshot before the first save each day; keeps 30 snapshots. Restoring also backs up your current list."));
            if (!String.IsNullOrWhiteSpace(warning)) page.Children.Add(FeatureLabel(warning, "The most recent automatic backup failed. Check disk space and permissions."));
            if (files.Length == 0) page.Children.Add(FeatureLabel("暂无历史备份。已有清单在下一次保存时开始留存。", "No snapshots yet. History starts on the next save of an existing list."));
            foreach (string path in files)
            {
                string selected = path;
                page.Children.Add(FeatureButton(Path.GetFileNameWithoutExtension(path).Replace("tasks-", "") + " · 恢复此备份",
                    Path.GetFileNameWithoutExtension(path).Replace("tasks-", "") + " · Restore snapshot", delegate { restore(selected); }));
            }
            ShowFeaturePage(page, L("自动历史备份", "Backup history"));
        }
    }
}
