using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Lumen.Lyrics
{
    /// <summary>
    /// 歌词解析：LRC / WebVTT / SRT / ASS / SSA / 纯文本。
    /// 扩展名不认识时按内容嗅探；解析结果统一成 LyricsDocument。
    /// </summary>
    public static class LyricsParser
    {
        // [mm:ss.xx] / [mm:ss:xx] / [mm:ss]
        private static readonly Regex LrcTimeTag =
            new Regex(@"\[(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?\]", RegexOptions.Compiled);

        // 增强型 LRC 逐字标签 <mm:ss.xx>
        private static readonly Regex LrcWordTag =
            new Regex(@"<\d{1,3}:\d{1,2}(?:[.:]\d{1,3})?>", RegexOptions.Compiled);

        // [ti:xxx] [ar:xxx] 这类元信息标签
        private static readonly Regex LrcMetaTag =
            new Regex(@"^\[(ti|ar|al|by|offset|re|ve|length)\s*:(.*)\]$",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex VttTag = new Regex(@"<[^>]*>", RegexOptions.Compiled);

        /// <summary>按扩展名解析；扩展名不认识时自动嗅探内容。</summary>
        public static LyricsDocument Parse(string text, string path, double fallbackDuration)
        {
            var doc = ParseCore(text, path, fallbackDuration);
            doc.SourcePath = path;
            return doc;
        }

        private static LyricsDocument ParseCore(string rawText, string path, double fallbackDuration)
        {
            var text = TextEncoding.StripBom(rawText ?? string.Empty);
            if (string.IsNullOrWhiteSpace(text)) return new LyricsDocument { FormatName = "空" };

            var ext = (System.IO.Path.GetExtension(path) ?? string.Empty).ToLowerInvariant();
            LyricsDocument result;

            switch (ext)
            {
                case ".lrc":
                    result = ParseLrc(text, fallbackDuration);
                    // 扩展名是 .lrc 但里面没有时间轴：可能是别的格式被改了名
                    if (result.IsEmpty) result = Sniff(text, fallbackDuration);
                    return result;
                case ".vtt":
                    result = ParseVtt(text, fallbackDuration);
                    if (result.IsEmpty) return Sniff(text, fallbackDuration);
                    return result;
                case ".srt":
                    result = ParseSrt(text, fallbackDuration);
                    if (result.IsEmpty) return Sniff(text, fallbackDuration);
                    return result;
                case ".ass":
                case ".ssa":
                    result = ParseAss(text, fallbackDuration);
                    if (result.IsEmpty) return Sniff(text, fallbackDuration);
                    return result;
                case ".txt":
                    result = ParsePlain(text);
                    // .txt 里可能是带时间轴的歌词，嗅探一下更保险
                    if (!result.IsEmpty)
                    {
                        var sniffed = Sniff(text, fallbackDuration);
                        if (sniffed.IsSynced && !sniffed.IsEmpty) return sniffed;
                    }
                    return result;
            }

            // 按内容嗅探
            return Sniff(text, fallbackDuration);
        }

        /// <summary>内容嗅探。</summary>
        public static LyricsDocument Sniff(string text, double fallbackDuration)
        {
            if (text.IndexOf("WEBVTT", StringComparison.OrdinalIgnoreCase) >= 0) return ParseVtt(text, fallbackDuration);
            if (text.IndexOf("[Script Info]", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("Dialogue:", StringComparison.OrdinalIgnoreCase) >= 0) return ParseAss(text, fallbackDuration);
            if (LrcTimeTag.IsMatch(text)) return ParseLrc(text, fallbackDuration);
            if (Regex.IsMatch(text, @"\d{1,2}:\d{2}:\d{2}[,.]\d{1,3}\s*-->")) return ParseSrt(text, fallbackDuration);
            return ParsePlain(text);
        }

        // ------------------------------------------------------------------
        // LRC
        // ------------------------------------------------------------------

        private static LyricsDocument ParseLrc(string text, double fallbackDuration)
        {
            var doc = new LyricsDocument { IsSynced = true, FormatName = "LRC" };
            double offset = 0;
            var lines = SplitLines(text);

            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;

                var meta = LrcMetaTag.Match(line);
                if (meta.Success)
                {
                    if (meta.Groups[1].Value.Equals("offset", StringComparison.OrdinalIgnoreCase))
                    {
                        double parsed;
                        if (double.TryParse(meta.Groups[2].Value.Trim(), NumberStyles.Float,
                                CultureInfo.InvariantCulture, out parsed))
                        {
                            // offset 单位是毫秒，正数表示歌词提前
                            offset = -parsed / 1000.0;
                        }
                    }
                    continue;
                }

                var matches = LrcTimeTag.Matches(line);
                if (matches.Count == 0) continue;

                // 去掉所有时间标签后剩下的就是歌词文本
                var content = LrcTimeTag.Replace(line, string.Empty);
                // 去掉增强型逐字标签（压平，不做卡拉OK变色）
                content = LrcWordTag.Replace(content, string.Empty).Trim();
                if (content.Length == 0) continue;

                // 一行多时间标签：每个标签都生成一行
                foreach (Match m in matches)
                {
                    double seconds = ToSeconds(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value) + offset;
                    if (seconds < 0) seconds = 0;
                    doc.Lines.Add(new LyricLine(seconds, content));
                }
            }

            doc.Lines.Sort((a, b) => a.Time.CompareTo(b.Time));
            if (doc.Lines.Count == 0) doc.IsSynced = false;
            doc.FillEndTimes(fallbackDuration);
            return doc;
        }

        private static double ToSeconds(string mm, string ss, string frac)
        {
            double minutes = double.Parse(mm, CultureInfo.InvariantCulture);
            double seconds = double.Parse(ss, CultureInfo.InvariantCulture);
            double fraction = 0;
            if (!string.IsNullOrEmpty(frac))
            {
                double value = double.Parse(frac, CultureInfo.InvariantCulture);
                // 1 位 = 十分之一秒，2 位 = 百分之一秒，3 位 = 毫秒
                fraction = value / Math.Pow(10, frac.Length);
            }
            return minutes * 60 + seconds + fraction;
        }

        // ------------------------------------------------------------------
        // WebVTT
        // ------------------------------------------------------------------

        private static LyricsDocument ParseVtt(string text, double fallbackDuration)
        {
            var doc = new LyricsDocument { IsSynced = true, FormatName = "WebVTT" };
            var lines = SplitLines(text);
            var timeLineRegex = new Regex(@"(\d{1,2}:\d{2}:\d{2}[.,]\d{1,3}|\d{1,2}:\d{2}[.,]\d{1,3})\s*-->\s*(\d{1,2}:\d{2}:\d{2}[.,]\d{1,3}|\d{1,2}:\d{2}[.,]\d{1,3})");

            int i = 0;
            while (i < lines.Count)
            {
                var line = lines[i].Trim();

                // NOTE 注释块：跳到空行
                if (line.StartsWith("NOTE", StringComparison.OrdinalIgnoreCase))
                {
                    while (i < lines.Count && lines[i].Trim().Length > 0) i++;
                    continue;
                }

                var m = timeLineRegex.Match(line);
                if (!m.Success) { i++; continue; }

                double start = ParseVttTime(m.Groups[1].Value);
                double end = ParseVttTime(m.Groups[2].Value);

                // 收集 cue 文本直到空行，多行合并
                var parts = new List<string>();
                i++;
                while (i < lines.Count && lines[i].Trim().Length > 0)
                {
                    // cue 的标识行（纯数字或名字）在时间行之前，这里不会遇到
                    parts.Add(CleanVttText(lines[i]));
                    i++;
                }

                // 同一时间重复出现则追加
                var merged = string.Join(" ", parts).Trim();
                if (merged.Length > 0)
                {
                    var existing = doc.Lines.Find(x => Math.Abs(x.Time - start) < 0.001);
                    if (existing != null) existing.Text = (existing.Text + " " + merged).Trim();
                    else doc.Lines.Add(new LyricLine(start, merged) { EndTime = end });
                }
            }

            doc.Lines.Sort((a, b) => a.Time.CompareTo(b.Time));
            if (doc.Lines.Count == 0) doc.IsSynced = false;
            doc.FillEndTimes(fallbackDuration);
            return doc;
        }

        /// <summary>去掉 &lt;v 说话人&gt;、&lt;c.class&gt; 等标签以及 VTT 实体。</summary>
        private static string CleanVttText(string input)
        {
            var s = VttTag.Replace(input, string.Empty);
            s = s.Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">")
                 .Replace("&nbsp;", " ").Replace("&quot;", "\"").Replace("&#39;", "'");
            return s.Trim();
        }

        private static double ParseVttTime(string value)
        {
            value = value.Replace(',', '.');
            var parts = value.Split(':');
            double seconds = 0;
            if (parts.Length == 3)
                seconds = double.Parse(parts[0], CultureInfo.InvariantCulture) * 3600
                        + double.Parse(parts[1], CultureInfo.InvariantCulture) * 60
                        + double.Parse(parts[2], CultureInfo.InvariantCulture);
            else if (parts.Length == 2)
                seconds = double.Parse(parts[0], CultureInfo.InvariantCulture) * 60
                        + double.Parse(parts[1], CultureInfo.InvariantCulture);
            else
                double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds);
            return seconds;
        }

        // ------------------------------------------------------------------
        // SRT
        // ------------------------------------------------------------------

        private static LyricsDocument ParseSrt(string text, double fallbackDuration)
        {
            var doc = new LyricsDocument { IsSynced = true, FormatName = "SRT" };
            var lines = SplitLines(text);
            var arrow = new Regex(@"(\d{1,2}:\d{2}:\d{2}[,.]\d{1,3})\s*-->\s*(\d{1,2}:\d{2}:\d{2}[,.]\d{1,3})");

            int i = 0;
            while (i < lines.Count)
            {
                var m = arrow.Match(lines[i]);
                if (!m.Success) { i++; continue; }

                double start = ParseVttTime(m.Groups[1].Value);
                double end = ParseVttTime(m.Groups[2].Value);

                var parts = new List<string>();
                i++;
                while (i < lines.Count && lines[i].Trim().Length > 0)
                {
                    parts.Add(VttTag.Replace(lines[i], string.Empty).Trim());
                    i++;
                }

                var merged = string.Join(" ", parts).Trim();
                if (merged.Length > 0) doc.Lines.Add(new LyricLine(start, merged) { EndTime = end });
            }

            doc.Lines.Sort((a, b) => a.Time.CompareTo(b.Time));
            if (doc.Lines.Count == 0) doc.IsSynced = false;
            doc.FillEndTimes(fallbackDuration);
            return doc;
        }

        // ------------------------------------------------------------------
        // ASS / SSA
        // ------------------------------------------------------------------

        private static readonly Regex AssOverrideTag = new Regex(@"\{[^}]*\}", RegexOptions.Compiled);

        private static LyricsDocument ParseAss(string text, double fallbackDuration)
        {
            var doc = new LyricsDocument { IsSynced = true, FormatName = "ASS" };
            var lines = SplitLines(text);

            string[] fields = null;   // Format: 行给出的列顺序
            var events = new List<string[]>();

            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.StartsWith("Format:", StringComparison.OrdinalIgnoreCase) && fields == null)
                {
                    fields = SplitAssFields(line.Substring("Format:".Length), -1);
                }
                else if (line.StartsWith("Dialogue:", StringComparison.OrdinalIgnoreCase))
                {
                    // Text 永远在最后一列，所以按列数切分，剩下的逗号全部归 Text
                    int columnCount = fields != null ? fields.Length : 10;
                    events.Add(SplitAssFields(line.Substring("Dialogue:".Length), columnCount));
                }
            }

            // 没有 Format 行时按 ASS 默认列顺序
            if (fields == null)
                fields = new[] { "Layer", "Start", "End", "Style", "Name", "MarginL", "MarginR", "MarginV", "Effect", "Text" };

            int startIdx = IndexOfField(fields, "Start");
            int endIdx = IndexOfField(fields, "End");
            int textIdx = IndexOfField(fields, "Text");
            if (startIdx < 0 || textIdx < 0) { doc.IsSynced = false; return doc; }

            foreach (var ev in events)
            {
                if (ev.Length <= textIdx || ev.Length <= startIdx) continue;

                double start = ParseAssTime(ev[startIdx]);
                double end = endIdx >= 0 && ev.Length > endIdx ? ParseAssTime(ev[endIdx]) : -1;

                // 去掉 {\pos(...)} 之类的样式覆盖；\N 换行转空格
                var content = AssOverrideTag.Replace(ev[textIdx], string.Empty)
                    .Replace("\\N", " ").Replace("\\n", " ").Replace("\\h", " ").Trim();

                if (content.Length > 0) doc.Lines.Add(new LyricLine(start, content) { EndTime = end });
            }

            doc.Lines.Sort((a, b) => a.Time.CompareTo(b.Time));
            if (doc.Lines.Count == 0) doc.IsSynced = false;
            doc.FillEndTimes(fallbackDuration);
            return doc;
        }

        /// <summary>
        /// 按逗号切分 ASS 行。maxFields &gt; 0 时只切出前 maxFields-1 个逗号，
        /// 剩下的全部作为最后一列（Text 列本身可能含逗号，例如 {\pos(190,280)}）。
        /// </summary>
        private static string[] SplitAssFields(string input, int maxFields)
        {
            var parts = new List<string>();
            int start = 0;
            int limit = maxFields > 0 ? maxFields - 1 : int.MaxValue;
            int count = 0;

            for (int i = 0; i < input.Length; i++)
            {
                if (input[i] != ',') continue;
                if (count >= limit) break;
                parts.Add(input.Substring(start, i - start).Trim());
                start = i + 1;
                count++;
            }
            parts.Add(input.Substring(start).Trim());
            return parts.ToArray();
        }

        private static int IndexOfField(string[] fields, string name)
        {
            for (int i = 0; i < fields.Length; i++)
            {
                if (string.Equals(fields[i], name, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return -1;
        }

        /// <summary>ASS 时间：H:MM:SS.cc（百分之一秒）。</summary>
        private static double ParseAssTime(string value)
        {
            value = (value ?? string.Empty).Trim();
            var parts = value.Split(':');
            if (parts.Length == 3)
            {
                double h = double.Parse(parts[0], CultureInfo.InvariantCulture);
                double m = double.Parse(parts[1], CultureInfo.InvariantCulture);
                double s = double.Parse(parts[2], CultureInfo.InvariantCulture);
                return h * 3600 + m * 60 + s;
            }
            double result;
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
            return result;
        }

        // ------------------------------------------------------------------
        // 纯文本
        // ------------------------------------------------------------------

        private static LyricsDocument ParsePlain(string text)
        {
            var doc = new LyricsDocument { IsSynced = false, FormatName = "纯文本" };
            foreach (var raw in SplitLines(text))
            {
                var line = raw.TrimEnd();
                if (line.Trim().Length == 0) continue;
                doc.Lines.Add(new LyricLine(-1, line.Trim()));
            }
            return doc;
        }

        private static List<string> SplitLines(string text)
        {
            var result = new List<string>();
            if (text == null) return result;
            int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\n')
                {
                    int end = i;
                    if (end > start && text[end - 1] == '\r') end--;
                    result.Add(text.Substring(start, end - start));
                    start = i + 1;
                }
                else if (c == '\r')
                {
                    result.Add(text.Substring(start, i - start));
                    if (i + 1 < text.Length && text[i + 1] == '\n') i++;
                    start = i + 1;
                }
            }
            if (start < text.Length) result.Add(text.Substring(start));
            return result;
        }
    }
}
