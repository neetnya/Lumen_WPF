using System;
using System.Collections.Generic;

namespace Lumen.Lyrics
{
    /// <summary>歌词中的一行：开始时间 + 文本 + 可选结束时间。</summary>
    public sealed class LyricLine
    {
        public double Time;          // 秒
        public double EndTime;       // 秒；未知时为 -1（取下一行时间）
        public string Text;

        public LyricLine(double time, string text)
        {
            Time = time;
            Text = text;
            EndTime = -1;
        }

        public override string ToString()
        {
            return Time.ToString("F2") + " " + Text;
        }
    }

    /// <summary>解析结果：可以是时间轴歌词（同步），也可以是纯文本歌词（不同步）。</summary>
    public sealed class LyricsDocument
    {
        public readonly List<LyricLine> Lines = new List<LyricLine>();

        /// <summary>true = 带时间轴，可同步高亮；false = 纯文本，只滚动。</summary>
        public bool IsSynced;

        public string SourcePath;
        public string FormatName = "无";

        public bool IsEmpty { get { return Lines.Count == 0; } }

        /// <summary>总时长（秒），用于纯文本滚动定位；未知返回 0。</summary>
        public double TotalSeconds
        {
            get
            {
                if (Lines.Count == 0) return 0;
                var last = Lines[Lines.Count - 1];
                return last.EndTime >= 0 ? last.EndTime : last.Time;
            }
        }

        /// <summary>按时间找到当前应高亮的行号；早于第一行返回 -1。</summary>
        public int IndexAt(double seconds)
        {
            if (Lines.Count == 0) return -1;
            if (seconds < Lines[0].Time) return -1;

            int lo = 0, hi = Lines.Count - 1, result = 0;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (Lines[mid].Time <= seconds)
                {
                    result = mid;
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }
            return result;
        }

        /// <summary>补齐 EndTime（用下一行的开始时间）。</summary>
        public void FillEndTimes(double fallbackDuration)
        {
            for (int i = 0; i < Lines.Count; i++)
            {
                if (Lines[i].EndTime >= 0) continue;
                if (i + 1 < Lines.Count) Lines[i].EndTime = Lines[i + 1].Time;
                else Lines[i].EndTime = fallbackDuration > Lines[i].Time ? fallbackDuration : Lines[i].Time + 5.0;
            }
        }
    }
}
