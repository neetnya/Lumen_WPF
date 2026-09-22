using System;
using System.IO;

namespace Lumen
{
    /// <summary>
    /// 便携式路径：优先程序旁边的 data\，不可写才回退 %APPDATA%\Lumen\，
    /// 环境变量 LUMEN_DATA_DIR 可强制指定。
    /// </summary>
    public static class Paths
    {
        private static string _dataDir;

        public static string ExeDir
        {
            get
            {
                try
                {
                    var path = Environment.ProcessPath;
                    if (!string.IsNullOrEmpty(path)) return Path.GetDirectoryName(path);
                }
                catch { }
                return AppContext.BaseDirectory;
            }
        }

        public static string DataDir
        {
            get
            {
                if (_dataDir != null) return _dataDir;

                var forced = Environment.GetEnvironmentVariable("LUMEN_DATA_DIR");
                if (!string.IsNullOrWhiteSpace(forced))
                {
                    try
                    {
                        Directory.CreateDirectory(forced);
                        _dataDir = forced;
                        return _dataDir;
                    }
                    catch { }
                }

                // 1) 程序旁边（便携）
                var portable = Path.Combine(ExeDir, "data");
                if (TryPrepare(portable))
                {
                    _dataDir = portable;
                    return _dataDir;
                }

                // 2) 源码运行时的仓库目录
                var cwd = Directory.GetCurrentDirectory();
                var local = Path.Combine(cwd, "data");
                if (!string.Equals(cwd, ExeDir, StringComparison.OrdinalIgnoreCase) && TryPrepare(local))
                {
                    _dataDir = local;
                    return _dataDir;
                }

                // 3) 回退 %APPDATA%\Lumen
                var roaming = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lumen");
                TryPrepare(roaming);
                _dataDir = roaming;
                return _dataDir;
            }
        }

        public static string ConfigFile { get { return Path.Combine(DataDir, "library.json"); } }

        public static string CoversDir { get { return Path.Combine(DataDir, "covers"); } }

        public static string SelftestResultFile { get { return Path.Combine(DataDir, "selftest-result.json"); } }

        /// <summary>用于测试：覆盖数据目录。</summary>
        public static void OverrideDataDir(string dir)
        {
            _dataDir = dir;
        }

        private static bool TryPrepare(string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var probe = Path.Combine(dir, ".writable");
                File.WriteAllText(probe, "1");
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
