using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Lumen
{
    /// <summary>崩溃日志：写到数据目录的 lumen.log。</summary>
    public static class Log
    {
        private static readonly object Gate = new object();
        private static string _path;

        public static string Path
        {
            get
            {
                if (_path == null)
                {
                    try { _path = System.IO.Path.Combine(Paths.DataDir, "lumen.log"); }
                    catch { _path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lumen.log"); }
                }
                return _path;
            }
        }

        public static void Info(string message)
        {
            Write("INFO", message);
        }

        public static void Warn(string message)
        {
            Write("WARN", message);
        }

        public static void Error(string message, Exception ex = null)
        {
            var text = ex == null ? message : message + " :: " + ex.GetType().Name + ": " + ex.Message + Environment.NewLine + ex.StackTrace;
            Write("ERROR", text);
        }

        private static void Write(string level, string message)
        {
            try
            {
                lock (Gate)
                {
                    var dir = System.IO.Path.GetDirectoryName(Path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    var line = string.Format("{0:yyyy-MM-dd HH:mm:ss.fff} [{1}] {2}{3}",
                        DateTime.Now, level, message, Environment.NewLine);
                    File.AppendAllText(Path, line, Encoding.UTF8);
                }
            }
            catch { /* 日志失败不能影响播放 */ }
        }
    }
}
