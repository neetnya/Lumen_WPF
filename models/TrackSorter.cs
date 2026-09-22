using System;
using System.Collections.Generic;
using Lumen.Models;

namespace Lumen
{
    /// <summary>
    /// 列表排序（对应 README）：
    /// 标题 ↑↓ / 时长 ↑↓ / 最近添加 / 文件格式；
    /// 打乱顺序优先（它是一次性操作且会被记住），切排序或重新导入即恢复有序。
    /// </summary>
    public static class TrackSorter
    {
        public const string Added = "added";
        public const string Title = "title";
        public const string TitleDesc = "titleDesc";
        public const string Duration = "duration";
        public const string DurationDesc = "durationDesc";
        public const string Format = "format";

        public static List<Track> Sort(Group group, string mode)
        {
            var result = new List<Track>();
            if (group == null) return result;

            // 打乱顺序优先（存在配置里，重启后仍是这个顺序）
            if (group.ShuffledOrder != null && group.ShuffledOrder.Count > 0)
            {
                var map = new Dictionary<string, Track>(StringComparer.OrdinalIgnoreCase);
                foreach (var t in group.Tracks) map[t.Path] = t;

                foreach (var path in group.ShuffledOrder)
                {
                    Track t;
                    if (map.TryGetValue(path, out t) && !result.Contains(t)) result.Add(t);
                }
                // 打乱后新导入的曲目接在后面
                foreach (var t in group.Tracks)
                {
                    if (!result.Contains(t)) result.Add(t);
                }
                return result;
            }

            result.AddRange(group.Tracks);

            switch (mode)
            {
                case Title:
                    result.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.CurrentCultureIgnoreCase));
                    break;
                case TitleDesc:
                    result.Sort((a, b) => string.Compare(b.Title, a.Title, StringComparison.CurrentCultureIgnoreCase));
                    break;
                case Duration:
                    result.Sort((a, b) => CompareDuration(a, b, true));
                    break;
                case DurationDesc:
                    result.Sort((a, b) => CompareDuration(a, b, false));
                    break;
                case Format:
                    result.Sort((a, b) =>
                    {
                        int c = string.Compare(a.Format, b.Format, StringComparison.OrdinalIgnoreCase);
                        if (c != 0) return c;
                        return string.Compare(a.Title, b.Title, StringComparison.CurrentCultureIgnoreCase);
                    });
                    break;
                case Added:
                default:
                    result.Sort((a, b) => a.AddedOrder.CompareTo(b.AddedOrder));
                    break;
            }

            return result;
        }

        private static int CompareDuration(Track a, Track b, bool ascending)
        {
            // 未知时长排在最后，不要混在中间
            bool aUnknown = a.Duration <= 0;
            bool bUnknown = b.Duration <= 0;
            if (aUnknown && bUnknown) return a.AddedOrder.CompareTo(b.AddedOrder);
            if (aUnknown) return 1;
            if (bUnknown) return -1;

            int c = a.Duration.CompareTo(b.Duration);
            return ascending ? c : -c;
        }

        public static string DisplayName(string mode)
        {
            switch (mode)
            {
                case Title: return "标题 ↑";
                case TitleDesc: return "标题 ↓";
                case Duration: return "时长 ↑";
                case DurationDesc: return "时长 ↓";
                case Format: return "文件格式";
                case Added:
                default: return "最近添加";
            }
        }

        public static string Next(string mode)
        {
            switch (mode)
            {
                case Added: return Title;
                case Title: return TitleDesc;
                case TitleDesc: return Duration;
                case Duration: return DurationDesc;
                case DurationDesc: return Format;
                case Format: return Added;
                default: return Added;
            }
        }
    }
}
