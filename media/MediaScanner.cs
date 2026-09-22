using System;
using System.Collections.Generic;
using System.IO;
using Lumen.Models;

namespace Lumen.Media
{
    /// <summary>
    /// 媒体库扫描。
    /// 按 README：打开时只读文件名/大小，标题、时长、歌词由后台分片补上，
    /// 每片最多占用主线程一小段时间，上千首的库也能很快看到窗口。
    /// </summary>
    public static class MediaScanner
    {
        /// <summary>支持的音频扩展名。</summary>
        public static readonly string[] AudioExtensions =
        {
            ".mp3", ".flac", ".wav", ".m4a", ".aac", ".ogg", ".opus",
            ".wma", ".ape", ".aiff", ".aif", ".aifc", ".mp4", ".m4b", ".wv", ".mpc"
        };

        public static bool IsAudioFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            var ext = Path.GetExtension(path);
            if (string.IsNullOrEmpty(ext)) return false;
            return Array.IndexOf(AudioExtensions, ext.ToLowerInvariant()) >= 0;
        }

        /// <summary>
        /// 扫描文件夹（含子目录）里的音频文件。
        /// 只做轻量工作：路径、文件名、大小 —— 不解析音频，保证快。
        /// </summary>
        public static List<Track> ScanFolder(string folder, ref int orderCounter)
        {
            var result = new List<Track>();
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return result;

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories);
            }
            catch (Exception ex)
            {
                Log.Warn("扫描文件夹失败 " + folder + ": " + ex.Message);
                return result;
            }

            var list = new List<string>();
            try
            {
                foreach (var f in files)
                {
                    if (IsAudioFile(f)) list.Add(f);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("枚举文件中断 " + folder + ": " + ex.Message);
            }

            // 稳定排序，让导入顺序可预期
            list.Sort(StringComparer.OrdinalIgnoreCase);

            foreach (var f in list)
            {
                result.Add(Track.FromFile(f, orderCounter++));
            }
            return result;
        }

        /// <summary>把一批文件路径转成 Track（跳过非音频）。</summary>
        public static List<Track> FromFiles(IEnumerable<string> paths, ref int orderCounter)
        {
            var result = new List<Track>();
            if (paths == null) return result;

            foreach (var p in paths)
            {
                if (string.IsNullOrEmpty(p)) continue;

                if (Directory.Exists(p))
                {
                    result.AddRange(ScanFolder(p, ref orderCounter));
                }
                else if (File.Exists(p) && IsAudioFile(p))
                {
                    result.Add(Track.FromFile(p, orderCounter++));
                }
            }
            return result;
        }

        /// <summary>探测单个文件的时长（秒）；失败返回 0。</summary>
        public static double ProbeDuration(string path)
        {
            try
            {
                using (var reader = new NAudio.Wave.AudioFileReader(path))
                {
                    var seconds = reader.TotalTime.TotalSeconds;
                    if (seconds > 0 && !double.IsNaN(seconds) && !double.IsInfinity(seconds)) return seconds;
                }
            }
            catch
            {
                // AudioFileReader 不支持时（如部分 m4a/wma）退回 MediaFoundation
                try
                {
                    using (var reader = new NAudio.Wave.MediaFoundationReader(path))
                    {
                        var seconds = reader.TotalTime.TotalSeconds;
                        if (seconds > 0 && !double.IsNaN(seconds) && !double.IsInfinity(seconds)) return seconds;
                    }
                }
                catch { }
            }
            return 0;
        }
    }
}
