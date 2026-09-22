using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lumen.Models;

namespace Lumen
{
    /// <summary>
    /// 配置持久化。
    /// 便携式：存在程序旁的 data\library.json，包含分组、播放模式、音量、
    /// 窗口位置、桌面歌词设置、各分组独立进度、打乱后的顺序。
    /// </summary>
    public sealed class Config
    {
        // ---- 播放 ----
        public int VolumePercent = 80;
        public bool Muted;
        public int PlayMode;                       // 0 顺序 / 1 列表循环 / 2 单曲循环

        // ---- 界面 ----
        public bool ShowLyricsPane = true;
        public bool ShowListPane = true;

        public double WindowLeft = double.NaN;
        public double WindowTop = double.NaN;
        public double WindowWidth = 1180;
        public double WindowHeight = 720;
        public bool WindowMaximized;

        // ---- 列表 ----
        public string SortMode = "added";          // added / title / titleDesc / duration / durationDesc / format
        public string CurrentGroupId;

        // ---- 桌面歌词 ----
        public bool DesktopLyricsVisible;
        public bool DesktopLyricsLocked;
        public double DesktopLyricsLeft = double.NaN;
        public double DesktopLyricsTop = double.NaN;
        public double DesktopLyricsHeight = 92;
        public double DesktopLyricsFontScale = 1.0;   // 0.5 ~ 3.0

        // ---- 库 ----
        public List<Group> Groups = new List<Group>();

        /// <summary>首次启动未建分组时自动创建。</summary>
        public Group EnsureGroup()
        {
            if (Groups == null) Groups = new List<Group>();
            if (Groups.Count == 0)
            {
                Groups.Add(new Group { Name = "默认分组" });
            }
            if (string.IsNullOrEmpty(CurrentGroupId) ||
                Groups.Find(g => g.Id == CurrentGroupId) == null)
            {
                CurrentGroupId = Groups[0].Id;
            }
            return CurrentGroup;
        }

        public Group CurrentGroup
        {
            get
            {
                if (Groups == null) return null;
                var g = Groups.Find(x => x.Id == CurrentGroupId);
                return g ?? (Groups.Count > 0 ? Groups[0] : null);
            }
        }

        public Group FindGroup(string id)
        {
            if (Groups == null || string.IsNullOrEmpty(id)) return null;
            return Groups.Find(g => g.Id == id);
        }

        public Group FindGroupByName(string name)
        {
            if (Groups == null || string.IsNullOrEmpty(name)) return null;
            return Groups.Find(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        // ------------------------------------------------------------------
        // 读写
        // ------------------------------------------------------------------

        private static readonly object Gate = new object();

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            IncludeFields = true,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
        };

        public static Config Load()
        {
            lock (Gate)
            {
                try
                {
                    var path = Paths.ConfigFile;
                    if (!File.Exists(path)) return new Config();

                    var text = File.ReadAllText(path, Encoding.UTF8);
                    if (string.IsNullOrWhiteSpace(text)) return new Config();

                    var config = JsonSerializer.Deserialize<Config>(text, JsonOptions);
                    if (config == null) return new Config();

                    if (config.Groups == null) config.Groups = new List<Group>();
                    foreach (var g in config.Groups)
                    {
                        if (g.Tracks == null) g.Tracks = new List<Track>();
                        if (string.IsNullOrEmpty(g.Id)) g.Id = Guid.NewGuid().ToString("N");
                    }
                    config.EnsureGroup();
                    return config;
                }
                catch (Exception ex)
                {
                    Log.Error("读取配置失败，使用默认配置", ex);
                    return new Config();
                }
            }
        }

        /// <summary>保存。写临时文件再替换，避免中途崩溃损坏配置。</summary>
        public bool Save()
        {
            lock (Gate)
            {
                try
                {
                    var path = Paths.ConfigFile;
                    var dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                    var json = JsonSerializer.Serialize(this, JsonOptions);

                    var temp = path + ".tmp";
                    File.WriteAllText(temp, json, new UTF8Encoding(false));

                    if (File.Exists(path)) File.Delete(path);
                    File.Move(temp, path);
                    return true;
                }
                catch (Exception ex)
                {
                    Log.Error("保存配置失败", ex);
                    return false;
                }
            }
        }

        private int _saveQueued;

        /// <summary>
        /// 后台保存：播放进度每几秒就会变，不能每次都同步写盘。
        /// 同一时刻只允许一个保存排队，后到的请求合并进去。
        /// </summary>
        public void SaveAsync()
        {
            if (System.Threading.Interlocked.CompareExchange(ref _saveQueued, 1, 0) != 0) return;

            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                try { Save(); }
                catch (Exception ex) { Log.Warn("后台保存配置失败: " + ex.Message); }
                finally { System.Threading.Interlocked.Exchange(ref _saveQueued, 0); }
            });
        }
    }
}
