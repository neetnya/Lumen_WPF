using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Lumen.Models
{
    /// <summary>
    /// 一首歌。元数据（标题/艺术家/专辑）按 README 决定：只从文件名与目录结构推断，
    /// 不读取内嵌 ID3/FLAC 标签，因此不依赖任何标签库。
    /// </summary>
    public sealed class Track
    {
        public string Path;

        /// <summary>显示用标题（文件名去掉扩展名）。</summary>
        public string Title;

        /// <summary>从「艺术家 - 标题」这类文件名推断；推不出则为空。</summary>
        public string Artist = string.Empty;

        /// <summary>从父目录名推断的专辑/分组名。</summary>
        public string Album = string.Empty;

        public string Format;           // mp3 / flac ...
        public long SizeBytes;

        /// <summary>时长（秒）。0 表示尚未探测（启动后由后台补全）。</summary>
        public double Duration;

        /// <summary>元数据是否已补全（时长是否已探测）。</summary>
        public bool MetadataLoaded;

        /// <summary>最近添加序号（导入顺序），用于「最近添加」排序。</summary>
        public int AddedOrder;

        /// <summary>歌词是否已确认存在（懒探测，-1 未知 / 0 无 / 1 有）。</summary>
        public int HasLyrics = -1;

        public string DurationText
        {
            get
            {
                if (Duration <= 0) return "--:--";
                var t = TimeSpan.FromSeconds(Duration);
                return t.TotalHours >= 1
                    ? string.Format("{0}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds)
                    : string.Format("{0}:{1:00}", t.Minutes, t.Seconds);
            }
        }

        public string FormatText
        {
            get { return string.IsNullOrEmpty(Format) ? "" : Format.ToUpperInvariant(); }
        }

        /// <summary>搜索匹配：标题 / 艺术家 / 专辑（空格分隔多关键词）。</summary>
        public bool Matches(IList<string> keywords)
        {
            if (keywords == null || keywords.Count == 0) return true;
            foreach (var kw in keywords)
            {
                if (!Contains(Title, kw) && !Contains(Artist, kw) && !Contains(Album, kw))
                    return false;
            }
            return true;
        }

        private static bool Contains(string source, string keyword)
        {
            if (string.IsNullOrEmpty(source)) return false;
            return source.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>从文件路径构造 Track，并从文件名推断标题/艺术家。</summary>
        public static Track FromFile(string path, int addedOrder)
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(path) ?? "";
            var ext = (System.IO.Path.GetExtension(path) ?? "").TrimStart('.').ToLowerInvariant();

            var track = new Track
            {
                Path = path,
                Title = name,
                Format = ext,
                AddedOrder = addedOrder
            };

            try
            {
                var dir = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) track.Album = System.IO.Path.GetFileName(dir);
            }
            catch { }

            try
            {
                var info = new FileInfo(path);
                if (info.Exists) track.SizeBytes = info.Length;
            }
            catch { }

            // 「艺术家 - 标题」/「艺术家 – 标题」（含全角破折号）
            var sep = FindArtistSeparator(name);
            if (sep > 0)
            {
                track.Artist = name.Substring(0, sep).Trim();
                var rest = name.Substring(sep + 1).Trim();
                if (rest.Length > 0) track.Title = rest;
            }

            return track;
        }

        private static int FindArtistSeparator(string name)
        {
            // 优先 " - "（带空格），避免把 "01-02" 这类编号误切
            int idx = name.IndexOf(" - ", StringComparison.Ordinal);
            if (idx > 0) return idx + 1;

            char[] dashes = { '–', '—', '−' };   // en/em dash、减号
            for (int i = 0; i < name.Length; i++)
            {
                if (Array.IndexOf(dashes, name[i]) >= 0 && i > 0 && i < name.Length - 1)
                    return i;
            }
            return -1;
        }
    }
}
