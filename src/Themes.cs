using System;
using System.Collections.Generic;

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
            new ThemePalette("forest", "森屿", "#3B795C",
                new[] { "#FCFCFA", "#263D35", "#88938D", "#E7EBE5", "#F0F3ED", "#3B795C", "#E9F0E6" },
                new[] { "#222B26", "#E5ECE5", "#A0ADA2", "#3A443C", "#2D3730", "#70AA81", "#3A4A3D" }),
            new ThemePalette("ocean", "海盐", "#3E7188",
                new[] { "#FAFCFD", "#203844", "#7C8F98", "#E2EAEF", "#EDF3F6", "#3E7188", "#E5F0F4" },
                new[] { "#202A30", "#E7EFF2", "#9EADB4", "#35434A", "#29363C", "#70A3B9", "#344750" }),
            new ThemePalette("lavender", "雾紫", "#716387",
                new[] { "#FCFBFD", "#383245", "#8A8294", "#EAE6EE", "#F3F0F6", "#716387", "#EEEAF3" },
                new[] { "#292630", "#EEEAF3", "#AAA2B3", "#45404C", "#35313D", "#9B8AB5", "#423B4B" }),
            new ThemePalette("clay", "陶粉", "#9B6258",
                new[] { "#FDFBF9", "#443330", "#95847F", "#EEE6E1", "#F6F0EC", "#9B6258", "#F3E7E2" },
                new[] { "#302725", "#F2E9E5", "#B7A7A2", "#4D403C", "#3C302D", "#C88376", "#493633" }),
            new ThemePalette("amber", "琥珀", "#98732F",
                new[] { "#FDFCF8", "#403A2D", "#918873", "#EEE9DC", "#F7F3E9", "#98732F", "#F4EDD9" },
                new[] { "#2E2B24", "#F1ECDD", "#ADA58D", "#4A4538", "#39352B", "#C29A4C", "#46402F" }),
            new ThemePalette("sage", "鼠尾草", "#668477",
                new[] { "#FBFCFA", "#2D3D37", "#839189", "#E4EAE6", "#EFF3F0", "#668477", "#E8EFEB" },
                new[] { "#242B28", "#E8EEEA", "#A2AEA7", "#3B4540", "#2F3834", "#88A99B", "#3B4943" }),
            new ThemePalette("indigo", "靛青", "#586C99",
                new[] { "#FBFCFE", "#29354D", "#818BA0", "#E4E8F0", "#EFF2F7", "#586C99", "#E8EDF6" },
                new[] { "#242832", "#E9ECF5", "#A4ABBD", "#3C4250", "#303541", "#8295C0", "#3A4254" }),
            new ThemePalette("teal", "青黛", "#3F7E79",
                new[] { "#FAFCFC", "#243F3D", "#7D9290", "#DFEAE8", "#ECF4F2", "#3F7E79", "#E3F0EE" },
                new[] { "#202C2B", "#E4F0EE", "#9BAFAD", "#344846", "#293A38", "#69A8A1", "#344C49" }),
            new ThemePalette("rose", "暮玫", "#956879",
                new[] { "#FDFBFC", "#45343B", "#95868C", "#EEE5E8", "#F6EFF2", "#956879", "#F2E6EB" },
                new[] { "#30272A", "#F2E9EC", "#B4A5AA", "#4C3D42", "#3B3034", "#BF899D", "#49363D" }),
            new ThemePalette("cocoa", "可可", "#7C6A60",
                new[] { "#FCFBFA", "#403833", "#8E8580", "#EAE5E1", "#F3F0ED", "#7C6A60", "#EEE9E5" },
                new[] { "#2D2927", "#EEEAE7", "#AAA19B", "#48413D", "#38322F", "#A49388", "#453C37" }),
            new ThemePalette("slate", "岩灰", "#667783",
                new[] { "#FBFCFD", "#2E3B43", "#839098", "#E3E8EB", "#EFF2F4", "#667783", "#E8EDF0" },
                new[] { "#242A2E", "#E8EDF0", "#A2ADB3", "#3B4449", "#2F373B", "#899BA6", "#3B484E" }),
            new ThemePalette("sky", "霁蓝", "#4D7FA3",
                new[] { "#FAFCFE", "#263D4E", "#7F919D", "#E0E9EF", "#ECF3F7", "#4D7FA3", "#E4EFF6" },
                new[] { "#202A31", "#E5EEF3", "#9DADB7", "#35444D", "#2A363D", "#72A3C3", "#354A57" })
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
