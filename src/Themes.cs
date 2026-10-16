using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace DesktopTodo
{
    internal sealed class ThemePalette
    {
        internal readonly string Id, Name, Swatch;
        internal readonly string[] Light, Dark;
        internal ThemePalette(string id, string name, string swatch, string[] light, string[] dark)
        { Id = id; Name = name; Swatch = swatch; Light = light; Dark = dark; }
    }

    internal static class ThemeCatalog
    {
        internal static readonly string[] ResourceNames = { "CardBrush", "TextBrush", "MutedBrush", "LineBrush", "InputBrush", "AccentBrush", "HoverBrush" };
        internal static readonly ThemePalette[] All = {
            new ThemePalette("graphite", "石墨", "#292927",
                new[] { "#FAFAF8", "#292927", "#6F6F69", "#E5E5E0", "#FFFFFF", "#292927", "#EFEFED" },
                new[] { "#202020", "#EEEEEC", "#A1A19A", "#393936", "#262625", "#ECECE7", "#30302E" }),
            new ThemePalette("forest", "森屿", "#496456",
                new[] { "#F8FAF7", "#282E29", "#687264", "#E2E7E0", "#FDFEFC", "#496456", "#EBF0E8" },
                new[] { "#202421", "#EBEEE9", "#A1ADA0", "#373E36", "#272D27", "#B6C6BC", "#303830" }),
            new ThemePalette("ocean", "海盐", "#4F6572",
                new[] { "#F7F9FA", "#282D31", "#66727A", "#E1E6E8", "#FDFEFE", "#4F6572", "#EAF0F2" },
                new[] { "#202325", "#ECEEF0", "#A3ACB0", "#373D40", "#272D30", "#BCC8CE", "#30373B" }),
            new ThemePalette("coral", "珊瑚", "#82635D",
                new[] { "#FAF8F6", "#302B29", "#766B66", "#E8E1DD", "#FFFEFD", "#82635D", "#F2EBE7" },
                new[] { "#262221", "#F0ECE9", "#B2A7A1", "#443B38", "#302A28", "#D7BDB5", "#3A312D" }),
            new ThemePalette("sun", "晴黄", "#796B4D",
                new[] { "#FAF9F4", "#302D27", "#716C5F", "#E7E4D9", "#FFFEFB", "#796B4D", "#F1EEE3" },
                new[] { "#25241F", "#F0EDE5", "#B1AC9D", "#413D32", "#2F2C24", "#D4C8AA", "#39352B" }),
            new ThemePalette("mint", "薄荷", "#4E6A63",
                new[] { "#F6FAF8", "#28312E", "#63746C", "#DFE8E3", "#FCFEFD", "#4E6A63", "#E8F1ED" },
                new[] { "#1F2523", "#E9F0EC", "#9FADA6", "#343F39", "#26302B", "#B3CAC2", "#2F3A33" }),
            new ThemePalette("sky", "霁蓝", "#55677D",
                new[] { "#F7F8FB", "#2B2E35", "#646E80", "#E1E4EB", "#FDFEFF", "#55677D", "#EBEEF4" },
                new[] { "#212329", "#ECEEF4", "#A3ABBA", "#393E4A", "#282D36", "#BEC8D9", "#323948" })
        };
        private static readonly Dictionary<string, ThemePalette> byId = Build();
        private static Dictionary<string, ThemePalette> Build()
        {
            Dictionary<string, ThemePalette> result = new Dictionary<string, ThemePalette>(StringComparer.OrdinalIgnoreCase);
            foreach (ThemePalette palette in All) result[palette.Id] = palette;
            return result;
        }
        internal static string Normalize(string id) { return id != null && byId.ContainsKey(id) ? id.ToLowerInvariant() : "forest"; }
        internal static ThemePalette Get(string id) { return byId[Normalize(id)]; }
        private static double Channel(byte value)
        {
            double number = value / 255.0;
            return number <= .04045 ? number / 12.92 : Math.Pow((number + .055) / 1.055, 2.4);
        }
        private static double Luminance(string hex)
        {
            Color colour = (Color)ColorConverter.ConvertFromString(hex);
            return .2126 * Channel(colour.R) + .7152 * Channel(colour.G) + .0722 * Channel(colour.B);
        }
        internal static double Contrast(string first, string second)
        {
            double a = Luminance(first), b = Luminance(second);
            return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
        }
        internal static string AccentText(string id, bool dark)
        {
            ThemePalette palette = ThemeCatalog.Get(id);
            string accent = (dark ? palette.Dark : palette.Light)[5];
            return AccentForeground(accent);
        }
        internal static string AccentForeground(string accent)
        { return Contrast(accent, "#FFFFFF") >= Contrast(accent, "#202020") ? "#FFFFFF" : "#202020"; }
    }

    internal struct ReminderDue
    {
        internal bool Interval, Daily, Snoozed;
        internal bool Any { get { return Interval || Daily || Snoozed; } }
    }

    internal static class ReminderScheduler
    {
        internal static ReminderDue Due(TodoItem item, DateTime localNow, DateTime utcNow)
        {
            ReminderDue due = new ReminderDue();
            if (item == null || item.Deleted || item.IsCompleted) return due;
            if (item.ReminderSnoozeUntil > 0)
            {
                if (utcNow.Ticks >= item.ReminderSnoozeUntil) due.Snoozed = true;
                return due;
            }
            if (item.ReminderIntervalMinutes > 0 && item.LastIntervalReminderAt > 0)
                due.Interval = utcNow.Ticks - item.LastIntervalReminderAt >= TimeSpan.FromMinutes(item.ReminderIntervalMinutes).Ticks;
            if (item.DailyReminderMinutesPlusOne > 0)
            {
                int target = item.DailyReminderMinutesPlusOne - 1;
                due.Daily = localNow.Hour * 60 + localNow.Minute >= target && item.LastDailyReminderDate != localNow.ToString("yyyy-MM-dd");
            }
            return due;
        }
        internal static void MarkShown(TodoItem item, ReminderDue due, DateTime localNow, DateTime utcNow)
        {
            if (due.Interval) item.LastIntervalReminderAt = utcNow.Ticks;
            if (due.Daily) item.LastDailyReminderDate = localNow.ToString("yyyy-MM-dd");
            if (due.Snoozed) item.ReminderSnoozeUntil = 0;
        }
    }
}
