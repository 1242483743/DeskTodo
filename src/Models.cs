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
        public bool IsCompleted { get { return CompletedAt > 0; } }
    }

    [DataContract]
    public sealed class AppState
    {
        [DataMember] public int Version = 1;
        [DataMember] public List<TodoItem> Items = new List<TodoItem>();
        [DataMember] public double X = -1;
        [DataMember] public double Y = -1;
        [DataMember] public double Width = 360;
        [DataMember] public double Height = 520;
        [DataMember] public bool PositionSet;
        [DataMember] public bool Locked = true;
        [DataMember] public bool DesktopMode = true;
        [DataMember] public bool DarkMode;
        [DataMember] public string Title = "DeskTodo";
        [DataMember] public string ThemeId = "forest";
        [DataMember(EmitDefaultValue = false)] public bool ShowMinimizeButton;
        [DataMember(EmitDefaultValue = false)] public bool ShowCloseButton;
        [DataMember(EmitDefaultValue = false)] public bool HideTray;
        // Inverse flag keeps the feature enabled when an older JSON file lacks it.
        [DataMember(EmitDefaultValue = false)] public bool HideItemTimes;
        [DataMember(EmitDefaultValue = false)] public bool ShowItemDates;
        [DataMember] public string DefaultCorner = "TopRight";
        [DataMember] public int DefaultScalePercent = 100;
        // Legacy v1.8 global rules are retained only for reading old JSON.
        // Scheduling uses TodoItem rules exclusively; do not copy global rules to all items.
        [DataMember] public int ReminderIntervalHours;
        [DataMember] public int DailyReminderMinutesPlusOne;
        [DataMember] public long LastIntervalReminderAt;
        [DataMember] public string LastDailyReminderDate;
        [DataMember] public long ReminderSnoozeUntil;
        public IEnumerable<TodoItem> Pending { get { return Items.Where(t => !t.Deleted && !t.IsCompleted).OrderBy(t => t.CreatedAt); } }
        public IEnumerable<TodoItem> Completed { get { return Items.Where(t => !t.Deleted && t.IsCompleted).OrderByDescending(t => t.CompletedAt); } }
        public TodoItem Add(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return null;
            if (text.Length > 500) throw new ArgumentException("事项最多 500 个字。");
            TodoItem item = new TodoItem { Text = text };
            Items.Add(item);
            return item;
        }
        public void Toggle(TodoItem item) { item.CompletedAt = item.IsCompleted ? 0 : DateTime.UtcNow.Ticks; }
        public void Validate()
        {
            if (Version > 1) throw new InvalidOperationException("数据由较新版本创建，请使用新版软件打开。");
            if (Items == null) Items = new List<TodoItem>();
            if (Items.Any(t => t == null || String.IsNullOrEmpty(t.Id) || String.IsNullOrWhiteSpace(t.Text)))
                throw new InvalidOperationException("数据中的事项无效。");
            if (Items.Select(t => t.Id).Distinct().Count() != Items.Count) throw new InvalidOperationException("事项 ID 重复。");
            foreach (TodoItem item in Items)
            {
                if (item.ReminderIntervalMinutes < 0 || item.ReminderIntervalMinutes > 10080) item.ReminderIntervalMinutes = 0;
                if (item.DailyReminderMinutesPlusOne < 0 || item.DailyReminderMinutesPlusOne > 1440) item.DailyReminderMinutesPlusOne = 0;
                if (item.LastIntervalReminderAt < 0) item.LastIntervalReminderAt = 0;
                if (item.ReminderSnoozeUntil < 0) item.ReminderSnoozeUntil = 0;
            }
            if (double.IsNaN(Width) || double.IsInfinity(Width)) Width = 360;
            if (double.IsNaN(Height) || double.IsInfinity(Height)) Height = 520;
            Width = Math.Max(320, Math.Min(900, Width));
            Height = Math.Max(360, Math.Min(1300, Height));
            if (double.IsNaN(X) || double.IsInfinity(X) || double.IsNaN(Y) || double.IsInfinity(Y)) PositionSet = false;
            ThemeId = ThemeCatalog.Normalize(ThemeId);
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
