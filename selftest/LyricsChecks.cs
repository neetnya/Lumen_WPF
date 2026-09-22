using System;
using System.Collections.Generic;
using Lumen.Lyrics;

namespace Lumen.SelfTest
{
    /// <summary>歌词格式 / 编码 / 查找的离线校验。</summary>
    internal static class LyricsChecks
    {
        /// <summary>把字符串编码成 GBK 字节（测试用）。</summary>
        private static byte[] GbkTestBytes(string text)
        {
            var enc = TextEncoding.Gbk;
            if (enc == null) return null;   // 代码页不可用，调用方会跳过该项
            return enc.GetBytes(text);
        }

        public static void Run(List<string> results, Action<bool, string> report)
        {            // ---- LRC 标准格式 ----
            var lrc = "[ti:测试]\n[ar:歌手]\n[00:12.50]第一句\n[01:05.25]第二句\n[01:05.25][02:10.00]重复句";
            var doc = LyricsParser.Parse(lrc, "t.lrc", 200);
            report(doc.IsSynced, "LRC 识别为同步歌词");
            report(doc.Lines.Count == 4, "LRC 一行多时间标签展开 (期望 4 行, 实际 " + doc.Lines.Count + ")");
            report(Math.Abs(doc.Lines[0].Time - 12.5) < 0.001, "LRC [mm:ss.xx] 时间解析 = 12.5");
            report(Math.Abs(doc.Lines[1].Time - 65.25) < 0.001, "LRC 第二句时间 = 65.25");
            report(doc.Lines[3].Time > 129.9 && doc.Lines[3].Time < 130.1, "LRC 重复行第二时间 = 130");
            report(doc.Lines[0].Text == "第一句", "LRC 文本提取正确");
            report(Math.Abs(doc.Lines[0].EndTime - 65.25) < 0.001, "LRC EndTime 自动补齐");

            // ---- LRC 增强型逐字标签压平 ----
            var lrc2 = "[00:01.00]<00:01.00>你<00:01.50>好<00:02.00>世界";
            var doc2 = LyricsParser.Parse(lrc2, "t.lrc", 100);
            report(doc2.Lines.Count == 1 && doc2.Lines[0].Text == "你好世界",
                "增强型 <mm:ss.xx> 逐字标签压平 (得到 '" + (doc2.Lines.Count > 0 ? doc2.Lines[0].Text : "") + "')");

            // ---- LRC offset ----
            var lrc3 = "[offset:500]\n[00:10.00]偏移测试";
            var doc3 = LyricsParser.Parse(lrc3, "t.lrc", 100);
            report(doc3.Lines.Count == 1 && Math.Abs(doc3.Lines[0].Time - 9.5) < 0.001,
                "LRC [offset:500] 使歌词提前 0.5s");

            // ---- WebVTT ----
            var vtt = "WEBVTT\n\nNOTE 这是注释\n\n00:00:05.000 --> 00:00:07.500\n<v 歌手>你好</v>\n世界\n\n00:00:08.000 --> 00:00:09.000\n第二句\n";
            var docV = LyricsParser.Parse(vtt, "t.vtt", 100);
            report(docV.IsSynced && docV.Lines.Count == 2, "VTT 解析出 2 条 cue (实际 " + docV.Lines.Count + ")");
            report(docV.Lines.Count > 0 && docV.Lines[0].Text == "你好 世界",
                "VTT <v> 说话人标签去掉 + 多行合并 (得到 '" + (docV.Lines.Count > 0 ? docV.Lines[0].Text : "") + "')");
            report(docV.Lines.Count > 0 && Math.Abs(docV.Lines[0].Time - 5.0) < 0.001, "VTT cue 起始时间 = 5s");
            report(docV.FormatName == "WebVTT", "VTT 格式名正确");

            // ---- SRT ----
            var srt = "1\r\n00:00:03,000 --> 00:00:05,000\r\n第一行\r\n第二行\r\n\r\n2\r\n00:00:06,000 --> 00:00:08,000\r\n下一句\r\n";
            var docS = LyricsParser.Parse(srt, "t.srt", 100);
            report(docS.Lines.Count == 2, "SRT 解析出 2 条 (实际 " + docS.Lines.Count + ")");
            report(docS.Lines.Count > 0 && docS.Lines[0].Text == "第一行 第二行", "SRT 多行合并");
            report(docS.Lines.Count > 0 && Math.Abs(docS.Lines[0].Time - 3.0) < 0.001, "SRT 逗号毫秒时间解析");

            // ---- ASS ----
            var ass = "[Script Info]\nTitle: x\n\n[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n" +
                      "Dialogue: 0,0:00:02.50,0:00:05.00,Default,,0,0,0,,{\\pos(190,280)}带样式的歌词\n" +
                      "Dialogue: 0,0:00:06.00,0:00:08.00,Default,,0,0,0,,第二句\\N换行";
            var docA = LyricsParser.Parse(ass, "t.ass", 100);
            report(docA.Lines.Count == 2, "ASS 解析出 2 条 (实际 " + docA.Lines.Count + ")");
            report(docA.Lines.Count > 0 && docA.Lines[0].Text == "带样式的歌词",
                "ASS {\\pos} 样式覆盖已去掉 (得到 '" + (docA.Lines.Count > 0 ? docA.Lines[0].Text : "") + "')");
            report(docA.Lines.Count > 0 && Math.Abs(docA.Lines[0].Time - 2.5) < 0.001, "ASS H:MM:SS.cc 时间解析");
            report(docA.Lines.Count > 1 && docA.Lines[1].Text == "第二句 换行", "ASS \\N 换行转换");

            // ---- 纯文本 ----
            var plain = "第一行\n\n第二行\n第三行\n";
            var docP = LyricsParser.Parse(plain, "t.txt", 100);
            report(!docP.IsSynced && docP.Lines.Count == 3, "纯文本识别为不同步 (实际 " + docP.Lines.Count + " 行)");
            report(docP.FormatName == "纯文本", "纯文本格式名正确");

            // ---- 内容嗅探：扩展名不对 ----
            var sniffLrc = "[00:01.00]被误命名的歌词";
            report(LyricsParser.Sniff(sniffLrc, 100).IsSynced, "嗅探：.txt 里的 LRC 内容");
            report(LyricsParser.Sniff(vtt, 100).FormatName == "WebVTT", "嗅探：VTT 内容");
            report(LyricsParser.Sniff(ass, 100).FormatName == "ASS", "嗅探：ASS 内容");

            // ---- 二分查找当前行 ----
            var findDoc = LyricsParser.Parse("[00:10.00]A\n[00:20.00]B\n[00:30.00]C", "x.lrc", 60);
            report(findDoc.IndexAt(5) == -1, "IndexAt: 早于首句返回 -1");
            report(findDoc.IndexAt(10) == 0, "IndexAt: 正好等于首句返回 0");
            report(findDoc.IndexAt(25) == 1, "IndexAt: 句中返回前一句");
            report(findDoc.IndexAt(999) == 2, "IndexAt: 超出末句返回最后一句");

            // ---- 编码 ----
            var utf8 = new System.Text.UTF8Encoding(true).GetBytes("[00:01.00]带BOM的UTF8");
            report(TextEncoding.Decode(utf8).StartsWith("[00:01.00]"), "UTF-8 BOM 解码");
            var utf16 = System.Text.Encoding.Unicode.GetBytes("[00:01.00]UTF16");
            report(TextEncoding.Decode(utf16).StartsWith("[00:01.00]"), "UTF-16 LE 解码");

            // GBK：.NET Core 不带 936 代码页，需要注册 CodePagesEncodingProvider
            var gbkBytes = GbkTestBytes("[00:01.00]中文GBK测试");
            if (gbkBytes == null)
            {
                report(false, "GBK 回退解码 (代码页 936 不可用)");
            }
            else
            {
                var gbkDecoded = TextEncoding.Decode(gbkBytes);
                report(gbkDecoded != null && gbkDecoded.Contains("中文GBK测试"),
                    "GBK 回退解码 (得到 '" + gbkDecoded + "')");
                report(!TextEncoding.IsValidUtf8(gbkBytes), "GBK 字节被判定为非 UTF-8");
            }

            // ---- 空 / 边界 ----
            var empty = LyricsParser.Parse("", "e.lrc", 10);
            report(empty.IsEmpty, "空文件产生空文档");
            var whitespace = LyricsParser.Parse("   \r\n\r\n  ", "e.lrc", 10);
            report(whitespace.IsEmpty, "只有空白产生空文档");
            // 非时间轴内容应退化成纯文本歌词（而不是丢失）
            var broken = LyricsParser.Parse("这是一段没有时间轴的歌词\n第二行", "e.lrc", 10);
            report(!broken.IsEmpty && !broken.IsSynced && broken.FormatName == "纯文本",
                "LRC 文件里没有时间轴时退化成纯文本");
            // .lrc 但内容是 VTT → 靠嗅探纠正
            var mislabeled = LyricsParser.Parse(vtt, "wrong.lrc", 100);
            report(mislabeled.FormatName == "WebVTT", "扩展名是 .lrc 但内容是 VTT 时靠嗅探纠正");
        }
    }
}
