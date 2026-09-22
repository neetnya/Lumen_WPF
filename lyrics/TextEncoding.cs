using System;
using System.IO;
using System.Text;

namespace Lumen.Lyrics
{
    /// <summary>
    /// 文本编码识别：BOM → UTF-8 严格校验 → GBK 回退。
    /// 对应 README 的「UTF-8 / GBK / UTF-16 自动识别」。
    /// </summary>
    public static class TextEncoding
    {
        private static readonly object ProviderGate = new object();
        private static bool _providerReady;
        private static Encoding _gbk;

        /// <summary>
        /// 注册 CodePagesEncodingProvider（提供 GBK/936 等代码页）。
        ///
        /// 说明：.NET Core 起 GBK 不再内置，必须注册该提供程序。
        /// 程序集 System.Text.Encoding.CodePages.dll 随 .NET 运行时一起分发，
        /// 但默认不会被加载，这里通过反射显式拿到它 —— 这样就不需要额外的
        /// NuGet 包，符合本项目"完全离线还原"的要求。
        /// </summary>
        private static void EnsureProvider()
        {
            if (_providerReady) return;
            lock (ProviderGate)
            {
                if (_providerReady) return;
                _providerReady = true;
                try
                {
                    var type = Type.GetType(
                        "System.Text.CodePagesEncodingProvider, System.Text.Encoding.CodePages",
                        false);
                    if (type == null)
                    {
                        Log.Warn("找不到 CodePagesEncodingProvider，GBK 歌词将无法解码");
                        return;
                    }

                    var property = type.GetProperty("Instance");
                    var instance = property != null ? property.GetValue(null, null) as EncodingProvider : null;
                    if (instance == null)
                    {
                        Log.Warn("CodePagesEncodingProvider.Instance 不可用");
                        return;
                    }

                    Encoding.RegisterProvider(instance);
                    _gbk = Encoding.GetEncoding(936);
                    Log.Info("已注册代码页提供程序，GBK(936) 可用");
                }
                catch (Exception ex)
                {
                    Log.Warn("注册代码页提供程序失败: " + ex.Message);
                }
            }
        }

        /// <summary>GBK(936)；不可用时返回 null。</summary>
        public static Encoding Gbk
        {
            get
            {
                EnsureProvider();
                return _gbk;
            }
        }

        /// <summary>解码 GBK 字节；无法解码时返回 null。</summary>
        public static string DecodeGbk(byte[] bytes)
        {
            var gbk = Gbk;
            if (gbk == null) return null;
            try { return gbk.GetString(bytes); }
            catch { return null; }
        }

        /// <summary>读取文本文件并按内容猜编码。</summary>
        public static string ReadAllText(string path)
        {
            var bytes = File.ReadAllBytes(path);
            return Decode(bytes);
        }

        public static string Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return string.Empty;

            // 1) BOM
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

            // 2) 猜 UTF-16（无 BOM）：大量 0x00 在奇数位
            if (LooksLikeUtf16(bytes))
                return Encoding.Unicode.GetString(bytes);

            // 3) UTF-8 严格校验：能解码就是 UTF-8
            if (IsValidUtf8(bytes))
                return new UTF8Encoding(false).GetString(bytes);

            // 4) 回退 GBK（中文 Windows 上最常见的歌词编码）
            var gbk = DecodeGbk(bytes);
            if (gbk != null) return gbk;

            // 5) 最后兜底：latin-1 保证不丢字节
            return Encoding.Latin1.GetString(bytes);
        }

        /// <summary>严格 UTF-8 校验（拒绝非法续字节 / 超长编码 / 代理区）。</summary>
        public static bool IsValidUtf8(byte[] bytes)
        {
            int i = 0;
            int nonAscii = 0;
            while (i < bytes.Length)
            {
                byte b = bytes[i];
                if (b < 0x80) { i++; continue; }

                int extra;
                if ((b & 0xE0) == 0xC0) extra = 1;
                else if ((b & 0xF0) == 0xE0) extra = 2;
                else if ((b & 0xF8) == 0xF0) extra = 3;
                else return false;

                if (i + extra >= bytes.Length) return false;
                for (int k = 1; k <= extra; k++)
                {
                    if ((bytes[i + k] & 0xC0) != 0x80) return false;
                }
                nonAscii++;
                i += extra + 1;
            }
            // 纯 ASCII 时交给调用方按 GBK 处理也无所谓，这里算合法
            return true;
        }

        private static bool LooksLikeUtf16(byte[] bytes)
        {
            if (bytes.Length < 4 || bytes.Length % 2 != 0) return false;
            int zerosOdd = 0, zerosEven = 0;
            int limit = Math.Min(bytes.Length, 512);
            for (int i = 0; i + 1 < limit; i += 2)
            {
                if (bytes[i] == 0) zerosEven++;
                if (bytes[i + 1] == 0) zerosOdd++;
            }
            int pairs = limit / 2;
            if (pairs == 0) return false;
            return zerosOdd > pairs * 0.6 || zerosEven > pairs * 0.6;
        }

        /// <summary>去掉 UTF-8 BOM 字符。</summary>
        public static string StripBom(string text)
        {
            if (!string.IsNullOrEmpty(text) && text[0] == '\uFEFF') return text.Substring(1);
            return text;
        }
    }
}
