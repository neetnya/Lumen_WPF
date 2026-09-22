using System;
using System.Collections.Generic;
using System.IO;

namespace Lumen.Lyrics
{
    /// <summary>
    /// 查找歌曲同目录下的同名歌词文件。
    /// 支持 LRC / VTT / SRT / ASS / SSA / TXT；扩展名不对时靠内容嗅探。
    /// </summary>
    public static class LyricsFinder
    {
        public static readonly string[] SupportedExtensions =
        {
            ".lrc", ".vtt", ".srt", ".ass", ".ssa", ".txt"
        };

        // 内容嗅探时也允许这些扩展名（有些歌词被存成 .text / .log）
        private static readonly string[] LooseExtensions =
        {
            ".text", ".log", ".sub", ".lyric", ".lyrics", ".lrcx"
        };

        private static readonly Dictionary<string, CachedLyrics> Cache =
            new Dictionary<string, CachedLyrics>(StringComparer.OrdinalIgnoreCase);

        private sealed class CachedLyrics
        {
            public DateTime Stamp;
            public long Size;
            public LyricsDocument Doc;
        }

        /// <summary>清空缓存（重新扫描库时用）。</summary>
        public static void ClearCache()
        {
            lock (Cache) Cache.Clear();
        }

        /// <summary>
        /// 为音频文件查找歌词。找不到返回空文档（IsEmpty == true）。
        /// </summary>
        public static LyricsDocument Find(string audioPath, double fallbackDuration)
        {
            if (string.IsNullOrEmpty(audioPath)) return new LyricsDocument();

            string matched = FindLyricsFile(audioPath);
            if (matched == null) return new LyricsDocument();

            FileInfo info;
            try { info = new FileInfo(matched); }
            catch { return new LyricsDocument(); }

            lock (Cache)
            {
                CachedLyrics entry;
                if (Cache.TryGetValue(matched, out entry) &&
                    entry.Stamp == info.LastWriteTimeUtc && entry.Size == info.Length)
                {
                    return entry.Doc;
                }
            }

            LyricsDocument doc;
            try
            {
                var text = TextEncoding.ReadAllText(matched);
                doc = LyricsParser.Parse(text, matched, fallbackDuration);
            }
            catch (Exception ex)
            {
                Log.Warn("读取歌词失败 " + matched + ": " + ex.Message);
                return new LyricsDocument();
            }

            lock (Cache)
            {
                Cache[matched] = new CachedLyrics
                {
                    Stamp = info.LastWriteTimeUtc,
                    Size = info.Length,
                    Doc = doc
                };
            }
            return doc;
        }

        /// <summary>返回匹配到的歌词文件路径，没有则返回 null。</summary>
        public static string FindLyricsFile(string audioPath)
        {
            string dir;
            string baseName;
            try
            {
                dir = Path.GetDirectoryName(audioPath);
                baseName = Path.GetFileNameWithoutExtension(audioPath);
            }
            catch { return null; }
            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(baseName) || !Directory.Exists(dir)) return null;

            // 1) 同名 + 受支持的扩展名（按优先级）
            foreach (var ext in SupportedExtensions)
            {
                var candidate = Path.Combine(dir, baseName + ext);
                if (File.Exists(candidate)) return candidate;
                // 大小写不敏感的文件系统上，上面已经命中；这里补一个大小写变体
                var variant = FindCaseInsensitive(dir, baseName + ext);
                if (variant != null) return variant;
            }

            // 2) 同名但扩展名可疑：读头部嗅探
            foreach (var candidate in SafeEnumerateFiles(dir, baseName + ".*"))
            {
                var ext = Path.GetExtension(candidate).ToLowerInvariant();
                if (Array.IndexOf(SupportedExtensions, ext) >= 0) continue;
                if (Array.IndexOf(LooseExtensions, ext) < 0) continue;
                if (SniffFile(candidate)) return candidate;
            }

            return null;
        }

        /// <summary>读取文件头判断是不是歌词。</summary>
        private static bool SniffFile(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (info.Length == 0 || info.Length > 4 * 1024 * 1024) return false;

                var bytes = new byte[Math.Min(8192, (int)info.Length)];
                using (var stream = File.OpenRead(path))
                {
                    int read = stream.Read(bytes, 0, bytes.Length);
                    if (read < bytes.Length) Array.Resize(ref bytes, read);
                }

                var text = TextEncoding.Decode(bytes);
                if (string.IsNullOrWhiteSpace(text)) return false;

                // 二进制脏数据（比如误配的同名封面）
                int control = 0;
                foreach (var c in text)
                {
                    if (c < 0x09) control++;
                }
                if (text.Length > 0 && control > text.Length * 0.05) return false;

                var doc = LyricsParser.Sniff(text, 0);
                return !doc.IsEmpty;
            }
            catch
            {
                return false;
            }
        }

        private static IEnumerable<string> SafeEnumerateFiles(string dir, string pattern)
        {
            try { return Directory.EnumerateFiles(dir, pattern); }
            catch { return new string[0]; }
        }

        private static string FindCaseInsensitive(string dir, string fileName)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(dir))
                {
                    if (string.Equals(Path.GetFileName(file), fileName, StringComparison.OrdinalIgnoreCase))
                        return file;
                }
            }
            catch { }
            return null;
        }
    }
}
