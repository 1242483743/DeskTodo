using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace DesktopTodo
{
    [DataContract]
    public sealed class TodoItem
    {
        [DataMember] public string Id = Guid.NewGuid().ToString("N");
        [DataMember] public string Text = "";
        [DataMember] public long CreatedAt = DateTime.UtcNow.Ticks;
        [DataMember] public long CompletedAt;
        [DataMember] public bool Deleted;
        [DataMember] public int ReminderIntervalMinutes;
        [DataMember] public int DailyReminderMinutesPlusOne;
        [DataMember] public long LastIntervalReminderAt;
        [DataMember] public string LastDailyReminderDate;
        [DataMember] public long ReminderSnoozeUntil;
        [DataMember(EmitDefaultValue = false)] public bool ReminderManaged;
        [DataMember(EmitDefaultValue = false)] public int SortOrder;
        [DataMember(EmitDefaultValue = false)] public bool Pinned;
        [DataMember(EmitDefaultValue = false)] public string GroupId;
        public bool IsCompleted { get { return CompletedAt > 0; } }
    }

    [DataContract]
    public sealed class TodoGroup
    {
        [DataMember] public string Id = Guid.NewGuid().ToString("N");
        [DataMember] public string Name = "";
        [DataMember] public int SortOrder;
        [DataMember(EmitDefaultValue = false)] public bool Pinned;
    }

    [DataContract]
    public sealed class AppState
    {
        [DataMember] public int Version = 4;
        [DataMember] public List<TodoItem> Items = new List<TodoItem>();
        [DataMember(EmitDefaultValue = false)] public List<TodoGroup> Groups = new List<TodoGroup>();
        [DataMember] public double X = -1;
        [DataMember] public double Y = -1;
        [DataMember] public double Width = 360;
        [DataMember] public double Height = 520;
        [DataMember] public bool PositionSet;
        // This machine's selected display and its last card placement are independent
        // of X/Y, which Windows may move onto the primary display when unplugged.
        [DataMember(EmitDefaultValue = false)] public bool StartOnSecondaryOnly;
        [DataMember(EmitDefaultValue = false)] public string SecondaryDeviceName;
        [DataMember(EmitDefaultValue = false)] public bool SecondaryPositionSet;
        [DataMember(EmitDefaultValue = false)] public int SecondaryOffsetX;
        [DataMember(EmitDefaultValue = false)] public int SecondaryOffsetY;
        [DataMember(EmitDefaultValue = false)] public double SecondaryWidth;
        [DataMember(EmitDefaultValue = false)] public double SecondaryHeight;
        [DataMember] public bool Locked = true;
        [DataMember] public bool DesktopMode = true;
        [DataMember] public bool DarkMode;
        [DataMember] public string Title = "DeskTodo";
        [DataMember] public string ThemeId = "forest";
        [DataMember(EmitDefaultValue = false)] public string Language;
        [DataMember(EmitDefaultValue = false)] public string PendingSort = "Ascending";
        [DataMember(EmitDefaultValue = false)] public int WindowOpacityPercent = 100;
        [DataMember(EmitDefaultValue = false)] public bool MiniMode;
        [DataMember(EmitDefaultValue = false)] public bool ShowCompactButton;
        [DataMember(EmitDefaultValue = false)] public bool MiniAlwaysOnTop = true;
        [DataMember(EmitDefaultValue = false)] public int MiniIconOpacityPercent = 92;
        [DataMember(EmitDefaultValue = false)] public double MiniX = Double.NaN;
        [DataMember(EmitDefaultValue = false)] public double MiniY = Double.NaN;
        [DataMember(EmitDefaultValue = false)] public string MiniEdge;
        [DataMember(EmitDefaultValue = false)] public long LastSavedAt;
        [DataMember(EmitDefaultValue = false)] public long DataRevision;
        [DataMember(EmitDefaultValue = false)] public string ActiveGroupId;
        // Keep older data grouped unless the user explicitly turns the feature off.
        [DataMember(EmitDefaultValue = false)] public bool DisableGroups;
        [DataMember(EmitDefaultValue = false)] public List<string> CollapsedGroupIds = new List<string>();
        [DataMember(EmitDefaultValue = false)] public bool LanSharingEnabled;
        [DataMember(EmitDefaultValue = false)] public string LanDisplayName = Environment.UserName;
        [DataMember(EmitDefaultValue = false)] public string LanAccessCode = Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant();
        [DataMember(EmitDefaultValue = false)] public string LanInstanceId = Guid.NewGuid().ToString("N");
        [DataMember(EmitDefaultValue = false)] public bool ShowMinimizeButton;
        [DataMember(EmitDefaultValue = false)] public bool ShowCloseButton;
        [DataMember(EmitDefaultValue = false)] public bool HideTray;
        // Inverse flag keeps the feature enabled when an older JSON file lacks it.
        [DataMember(EmitDefaultValue = false)] public bool HideItemTimes;
        [DataMember(EmitDefaultValue = false)] public bool ShowItemDates;
        [DataMember(EmitDefaultValue = false)] public string ShowHotkey = "Ctrl+Alt+D";
        [DataMember(EmitDefaultValue = false)] public string MiniHotkey = "Ctrl+Alt+M";
        [DataMember] public string DefaultCorner = "TopRight";
        [DataMember] public int DefaultScalePercent = 100;
        // Legacy v1.8 global rules are retained only for reading old JSON.
        // Scheduling uses TodoItem rules exclusively; do not copy global rules to all items.
        [DataMember] public int ReminderIntervalHours;
        [DataMember] public int DailyReminderMinutesPlusOne;
        [DataMember] public long LastIntervalReminderAt;
        [DataMember] public string LastDailyReminderDate;
        [DataMember] public long ReminderSnoozeUntil;
        public IEnumerable<TodoItem> Pending
        {
            get
            {
                IEnumerable<TodoItem> pending = Items.Where(t => !t.Deleted && !t.IsCompleted);
                if (PendingSort == "Manual") return pending.OrderByDescending(t => t.Pinned).ThenBy(t => t.SortOrder).ThenBy(t => t.CreatedAt);
                if (PendingSort == "Descending") return pending.OrderByDescending(t => t.Pinned).ThenByDescending(t => t.CreatedAt);
                return pending.OrderByDescending(t => t.Pinned).ThenBy(t => t.CreatedAt);
            }
        }
        public IEnumerable<TodoItem> Completed { get { return Items.Where(t => !t.Deleted && t.IsCompleted).OrderByDescending(t => t.CompletedAt); } }
        public IEnumerable<TodoGroup> OrderedGroups { get { return Groups.OrderByDescending(g => g.Pinned).ThenBy(g => g.SortOrder); } }
        public TodoItem Add(string text) { return Add(text, null); }
        public TodoItem Add(string text, string groupId)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return null;
            if (text.Length > 500) throw new ArgumentException("事项最多 500 个字。");
            int nextOrder = Items.Where(t => !t.Deleted && !t.IsCompleted).Select(t => t.SortOrder).DefaultIfEmpty(-1).Max() + 1;
            TodoItem item = new TodoItem { Text = text, SortOrder = nextOrder, GroupId = groupId };
            Items.Add(item);
            return item;
        }
        public void Toggle(TodoItem item) { item.CompletedAt = item.IsCompleted ? 0 : DateTime.UtcNow.Ticks; }
        public void Validate()
        {
            if (Version > 4) throw new InvalidOperationException("数据由较新版本创建，请使用新版软件打开。");
            bool upgradingV1 = Version < 2;
            bool upgradingV3 = Version < 3;
            bool upgradingV4 = Version < 4;
            Version = 4;
            if (Items == null) Items = new List<TodoItem>();
            if (Groups == null) Groups = new List<TodoGroup>();
            if (CollapsedGroupIds == null) CollapsedGroupIds = new List<string>();
            if (Items.Any(t => t == null || String.IsNullOrEmpty(t.Id) || String.IsNullOrWhiteSpace(t.Text)))
                throw new InvalidOperationException("数据中的事项无效。");
            if (Items.Select(t => t.Id).Distinct().Count() != Items.Count) throw new InvalidOperationException("事项 ID 重复。");
            if (Groups.Any(g => g == null || String.IsNullOrWhiteSpace(g.Id) || String.IsNullOrWhiteSpace(g.Name)))
                throw new InvalidOperationException("数据中的分组无效。");
            if (Groups.Select(g => g.Id).Distinct().Count() != Groups.Count) throw new InvalidOperationException("分组 ID 重复。");
            HashSet<string> groupIds = new HashSet<string>(Groups.Select(g => g.Id));
            CollapsedGroupIds = CollapsedGroupIds.Where(id => id == "__ungrouped__" || groupIds.Contains(id)).Distinct().ToList();
            int groupOrder = 0;
            foreach (TodoGroup group in Groups.OrderBy(g => g.SortOrder))
            {
                group.Name = group.Name.Trim();
                if (group.Name.Length > 20) group.Name = group.Name.Substring(0, 20);
                group.SortOrder = groupOrder++;
            }
            int repairOrder = 0;
            foreach (TodoItem item in Items.OrderBy(t => t.CreatedAt))
            {
                if (item.ReminderIntervalMinutes < 0 || item.ReminderIntervalMinutes > 10080) item.ReminderIntervalMinutes = 0;
                if (item.DailyReminderMinutesPlusOne < 0 || item.DailyReminderMinutesPlusOne > 1440) item.DailyReminderMinutesPlusOne = 0;
                if (item.LastIntervalReminderAt < 0) item.LastIntervalReminderAt = 0;
                if (item.ReminderSnoozeUntil < 0) item.ReminderSnoozeUntil = 0;
                if (item.SortOrder < 0 || upgradingV1) item.SortOrder = repairOrder;
                if (!String.IsNullOrWhiteSpace(item.GroupId) && !groupIds.Contains(item.GroupId)) item.GroupId = null;
                repairOrder++;
            }
            if (double.IsNaN(Width) || double.IsInfinity(Width)) Width = 360;
            if (double.IsNaN(Height) || double.IsInfinity(Height)) Height = 520;
            Width = Math.Max(320, Math.Min(900, Width));
            Height = Math.Max(360, Math.Min(1300, Height));
            Width = Math.Min(Width, Math.Max(320, Math.Round(Height * 0.8)));
            if (double.IsNaN(X) || double.IsInfinity(X) || double.IsNaN(Y) || double.IsInfinity(Y)) PositionSet = false;
            if (String.IsNullOrWhiteSpace(SecondaryDeviceName)) SecondaryDeviceName = null;
            else SecondaryDeviceName = SecondaryDeviceName.Trim();
            if (double.IsNaN(SecondaryWidth) || double.IsInfinity(SecondaryWidth) || SecondaryWidth < 320 || SecondaryWidth > 900 ||
                double.IsNaN(SecondaryHeight) || double.IsInfinity(SecondaryHeight) || SecondaryHeight < 360 || SecondaryHeight > 1300)
                SecondaryPositionSet = false;
            ThemeId = ThemeCatalog.Normalize(ThemeId);
            if (upgradingV1 && String.IsNullOrWhiteSpace(Language)) Language = "zh-CN";
            if (upgradingV1) MiniAlwaysOnTop = true;
            if (!String.IsNullOrWhiteSpace(Language) && Language != "zh-CN" && Language != "en-US") Language = "zh-CN";
            if (PendingSort != "Ascending" && PendingSort != "Descending" && PendingSort != "Manual") PendingSort = "Ascending";
            if (ShowHotkey == null) ShowHotkey = "Ctrl+Alt+D";
            if (MiniHotkey == null) MiniHotkey = "Ctrl+Alt+M";
            if (WindowOpacityPercent < 30 || WindowOpacityPercent > 100) WindowOpacityPercent = 100;
            if (upgradingV3 && WindowOpacityPercent == 92) WindowOpacityPercent = 100;
            if (MiniIconOpacityPercent < 40 || MiniIconOpacityPercent > 100) MiniIconOpacityPercent = 92;
            if (Double.IsInfinity(MiniX)) MiniX = Double.NaN;
            if (Double.IsInfinity(MiniY)) MiniY = Double.NaN;
            // v1-v3 stored the mini-window coordinates in WPF logical units. v4
            // stores physical desktop pixels so the icon remains recoverable across
            // monitors with different scaling. Discard only this obsolete position.
            if (upgradingV4) { MiniX = Double.NaN; MiniY = Double.NaN; MiniEdge = null; }
            if (MiniEdge != "Left" && MiniEdge != "Right" && MiniEdge != "Top" && MiniEdge != "Bottom") MiniEdge = null;
            if (LastSavedAt < 0 || LastSavedAt > DateTime.MaxValue.Ticks) LastSavedAt = 0;
            if (DataRevision < 0) DataRevision = 0;
            if (ActiveGroupId == "__ungrouped__") { }
            else if (!String.IsNullOrWhiteSpace(ActiveGroupId) && !groupIds.Contains(ActiveGroupId)) ActiveGroupId = null;
            LanDisplayName = String.IsNullOrWhiteSpace(LanDisplayName) ? Environment.UserName : LanDisplayName.Trim();
            if (LanDisplayName.Length > 24) LanDisplayName = LanDisplayName.Substring(0, 24);
            LanAccessCode = String.IsNullOrWhiteSpace(LanAccessCode) ? Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant() : LanAccessCode.Trim().ToUpperInvariant();
            if (LanAccessCode.Length > 16) LanAccessCode = LanAccessCode.Substring(0, 16);
            LanInstanceId = String.IsNullOrWhiteSpace(LanInstanceId) ? Guid.NewGuid().ToString("N") : LanInstanceId.Trim();
            if (DefaultCorner != "TopLeft" && DefaultCorner != "TopRight" && DefaultCorner != "BottomLeft" && DefaultCorner != "BottomRight")
                DefaultCorner = "TopRight";
            if (DefaultScalePercent != 75 && DefaultScalePercent != 100 && DefaultScalePercent != 125 && DefaultScalePercent != 150 &&
                DefaultScalePercent != 175 && DefaultScalePercent != 200 && DefaultScalePercent != 225 && DefaultScalePercent != 250)
                DefaultScalePercent = 100;
            Title = String.IsNullOrWhiteSpace(Title) || Title.Trim() == "待办" ? "DeskTodo" : Title.Trim();
            if (Title.Length > 12) Title = Title.Substring(0, 12);
            if (ReminderIntervalHours < 0 || ReminderIntervalHours > 168) ReminderIntervalHours = 0;
            if (DailyReminderMinutesPlusOne < 0 || DailyReminderMinutesPlusOne > 1440) DailyReminderMinutesPlusOne = 0;
            if (LastIntervalReminderAt < 0) LastIntervalReminderAt = 0;
            if (ReminderSnoozeUntil < 0) ReminderSnoozeUntil = 0;
        }
    }
}
