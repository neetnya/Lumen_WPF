using System;
using System.Collections.Generic;
using System.IO;

namespace Lumen
{
    /// <summary>命令行参数（对应 README 的 --selftest / --smoke / 从文件关联打开）。</summary>
    public sealed class CommandLine
    {
        public readonly List<string> Files = new List<string>();
        public double SelfTestSeconds;

        /// <summary>--smoke：正常启动界面，若干秒后自动退出（实机冒烟用）。</summary>
        public double SmokeSeconds;

        public bool ShowHelp;
        public string DataDirOverride;

        public static CommandLine Parse(string[] args)
        {
            var result = new CommandLine();

            for (int i = 0; i < args.Length; i++)
            {
                var a = args[i];
                if (string.IsNullOrWhiteSpace(a)) continue;

                if (a.StartsWith("--", StringComparison.Ordinal))
                {
                    var name = a.Substring(2);
                    string value = null;
                    int eq = name.IndexOf('=');
                    if (eq >= 0)
                    {
                        value = name.Substring(eq + 1);
                        name = name.Substring(0, eq);
                    }

                    switch (name.ToLowerInvariant())
                    {
                        case "selftest":
                            result.SelfTestSeconds = ParseSeconds(value, 8);
                            break;
                        case "smoke":
                            result.SmokeSeconds = ParseSeconds(value, 6);
                            break;
                        case "data-dir":
                            if (value == null && i + 1 < args.Length) value = args[++i];
                            result.DataDirOverride = value;
                            break;
                        case "help":
                        case "h":
                        case "?":
                            result.ShowHelp = true;
                            break;
                    }
                    continue;
                }

                if (a.StartsWith("-", StringComparison.Ordinal) && a.Length > 1) continue;

                // 文件 / 文件夹（可能带引号）
                var path = a.Trim('"');
                if (File.Exists(path) || Directory.Exists(path)) result.Files.Add(path);
            }

            return result;
        }

        private static double ParseSeconds(string value, double fallback)
        {
            double seconds;
            if (!string.IsNullOrEmpty(value) &&
                double.TryParse(value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out seconds) && seconds > 0)
            {
                return seconds;
            }
            return fallback;
        }

        public static void PrintHelp()
        {
            Log.Info("Lumen 音乐 - 本地音乐播放器");
            Log.Info("用法: Lumen.exe [选项] [音频文件或文件夹 ...]");
            Log.Info("  --selftest [秒数]  内置自检，不显示界面（默认 8 秒）");
            Log.Info("  --smoke [秒数]     正常启动界面，若干秒后自动退出（默认 6 秒）");
            Log.Info("  --help             显示本帮助");
        }
    }
}
