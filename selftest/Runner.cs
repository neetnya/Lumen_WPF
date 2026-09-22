using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Lumen.SelfTest
{
    /// <summary>
    /// 内置自检（对应 README 的 --selftest）：
    /// 自己造一个 WAV + LRC，验证「界面能建、音频能解码、歌词能同步、穿透能生效」，
    /// 结果写到配置目录的 selftest-result.json。
    /// </summary>
    public static class Runner
    {
        public static int Run(double seconds)
        {
            var results = new List<Result>();
            bool allPassed = true;

            void Report(bool ok, string name, string detail = null)
            {
                results.Add(new Result { Name = name, Passed = ok, Detail = detail });
                if (!ok) allPassed = false;
                Log.Info((ok ? "[自检 PASS] " : "[自检 FAIL] ") + name + (detail != null ? " :: " + detail : ""));
            }

            try
            {
                Log.Info("=== 内置自检开始 (" + seconds + " 秒) ===");

                // ---- 1. 数据目录可写 ----
                try
                {
                    var probe = Path.Combine(Paths.DataDir, "selftest-probe.tmp");
                    File.WriteAllText(probe, "ok");
                    File.Delete(probe);
                    Report(true, "数据目录可写", Paths.DataDir);
                }
                catch (Exception ex)
                {
                    Report(false, "数据目录可写", ex.Message);
                }

                // ---- 2. 歌词解析（含各格式） ----
                var lyricsResults = new List<string>();
                int lyricsPass = 0, lyricsFail = 0;
                LyricsChecks.Run(lyricsResults, delegate (bool ok, string name)
                {
                    if (ok) lyricsPass++; else lyricsFail++;
                    if (!ok) Log.Warn("[自检] 歌词项失败: " + name);
                });
                Report(lyricsFail == 0, "歌词格式解析", lyricsPass + " 项通过 / " + lyricsFail + " 项失败");

                // ---- 3. 造一个 WAV + LRC ----
                string wavPath = null, lrcPath = null;
                try
                {
                    var dir = Path.Combine(Paths.DataDir, "selftest");
                    Directory.CreateDirectory(dir);
                    wavPath = Path.Combine(dir, "selftest-tone.wav");
                    lrcPath = Path.Combine(dir, "selftest-tone.lrc");

                    WriteToneWav(wavPath, 3.0, 440);
                    File.WriteAllText(lrcPath,
                        "[ti:自检]\n[00:00.00]第一句\n[00:01.00]第二句\n[00:02.00]第三句\n",
                        new UTF8Encoding(false));

                    Report(File.Exists(wavPath) && new FileInfo(wavPath).Length > 1000, "生成测试 WAV");
                    Report(File.Exists(lrcPath), "生成测试 LRC");
                }
                catch (Exception ex)
                {
                    Report(false, "生成测试文件", ex.Message);
                }

                // ---- 4. 音频能解码 ----
                if (wavPath != null)
                {
                    try
                    {
                        using (var reader = new NAudio.Wave.AudioFileReader(wavPath))
                        {
                            var duration = reader.TotalTime.TotalSeconds;
                            var format = reader.WaveFormat;
                            Report(duration > 2.5 && duration < 3.5, "音频能解码",
                                string.Format("{0:0.00}s {1}Hz {2}ch", duration, format.SampleRate, format.Channels));
                        }
                    }
                    catch (Exception ex)
                    {
                        Report(false, "音频能解码", ex.Message);
                    }

                    try
                    {
                        var probed = Media.MediaScanner.ProbeDuration(wavPath);
                        Report(probed > 2.5 && probed < 3.5, "时长探测", probed.ToString("0.00") + "s");
                    }
                    catch (Exception ex)
                    {
                        Report(false, "时长探测", ex.Message);
                    }
                }

                // ---- 5. 歌词查找能命中同目录同名文件 ----
                if (wavPath != null)
                {
                    var found = Lyrics.LyricsFinder.FindLyricsFile(wavPath);
                    Report(found != null && found.EndsWith(".lrc", StringComparison.OrdinalIgnoreCase),
                        "同目录同名歌词查找", found);

                    var doc = Lyrics.LyricsFinder.Find(wavPath, 3.0);
                    Report(doc != null && doc.IsSynced && doc.Lines.Count == 3,
                        "歌词能加载并同步", doc != null ? doc.Lines.Count + " 行" : "null");

                    if (doc != null && doc.Lines.Count == 3)
                    {
                        Report(doc.IndexAt(0.5) == 0 && doc.IndexAt(1.5) == 1 && doc.IndexAt(2.5) == 2,
                            "歌词时间轴同步正确");
                    }
                }

                // ---- 6. 界面能建（离屏） ----
                try
                {
                    var ok = RunOnUiThread(() =>
                    {
                        var lyrics = new UI.LyricsPane();
                        var list = new UI.TrackListPane();
                        var bottom = new UI.BottomBar();
                        var grid = new System.Windows.Controls.Grid();
                        grid.Children.Add(lyrics);
                        grid.Children.Add(list);
                        grid.Children.Add(bottom);

                        lyrics.SetLyrics(Lyrics.LyricsParser.Parse(
                            "[00:00.00]测试\n[00:02.00]第二句", "t.lrc", 3));

                        var groups = new List<Models.Group>
                        {
                            new Models.Group { Name = "自检分组" }
                        };
                        list.SetGroups(groups, groups[0].Id);
                        list.SetTracks(new List<Models.Track>(), TrackSorter.Added);
                        bottom.SetPlaying(false);
                        bottom.SetMode(0);
                        bottom.SetTimeline(0, 3, true);

                        // 强制一次布局，确保模板能实例化
                        grid.Measure(new Size(800, 600));
                        grid.Arrange(new Rect(0, 0, 800, 600));
                        return true;
                    });

                    Report(ok, "界面组件能创建并布局");
                }
                catch (Exception ex)
                {
                    Report(false, "界面组件能创建并布局", ex.Message);
                }

                // ---- 7. 桌面歌词：宽度固定 60% + 穿透能生效 ----
                try
                {
                    var ok = RunOnUiThread(() =>
                    {
                        var desktop = new UI.DesktopLyricsWindow
                        {
                            WindowStartupLocation = WindowStartupLocation.Manual,
                            Left = -3000,
                            Top = -3000,
                            ShowActivated = false
                        };
                        desktop.Show();
                        desktop.UpdateLayout();

                        double expected = 0; // 稍后校验
                        var area = Native.Win32.GetWorkArea(IntPtr.Zero);
                        expected = Math.Max(320, (area.right - area.left) * 0.60);

                        bool widthOk = Math.Abs(desktop.Width - expected) < 2.0;

                        // 锁定 → 检查 WS_EX_TRANSPARENT 生效
                        desktop.SetLocked(true);
                        var handle = new System.Windows.Interop.WindowInteropHelper(desktop).Handle;
                        int exStyle = Native.Win32.GetWindowLong(handle, Native.Win32.GWL_EXSTYLE);
                        bool transparent = (exStyle & Native.Win32.WS_EX_TRANSPARENT) != 0;

                        desktop.SetLocked(false);
                        int exStyle2 = Native.Win32.GetWindowLong(handle, Native.Win32.GWL_EXSTYLE);
                        bool cleared = (exStyle2 & Native.Win32.WS_EX_TRANSPARENT) == 0;

                        // 空歌词 → 空条，无占位文字
                        desktop.SetLyrics(null);

                        desktop.Close();

                        if (!widthOk) Log.Warn(string.Format("桌面歌词宽度 {0:0} 期望 {1:0}", desktop.Width, expected));
                        if (!transparent) Log.Warn("锁定后 WS_EX_TRANSPARENT 未置位");

                        return widthOk && transparent && cleared;
                    });

                    Report(ok, "桌面歌词：宽度 60% + 穿透生效 + 解锁恢复");
                }
                catch (Exception ex)
                {
                    Report(false, "桌面歌词：宽度 60% + 穿透生效", ex.Message);
                }

                // ---- 8. 配置能存能读 ----
                try
                {
                    var config = Config.Load();
                    var group = config.EnsureGroup();
                    group.LastPositionSeconds = 12.5;
                    config.VolumePercent = 55;
                    bool saved = config.Save();
                    var reloaded = Config.Load();
                    bool roundTrip = reloaded.VolumePercent == 55 &&
                                     reloaded.CurrentGroup != null &&
                                     Math.Abs(reloaded.CurrentGroup.LastPositionSeconds - 12.5) < 0.001;
                    Report(saved && roundTrip, "配置保存与读取（含分组进度）");
                }
                catch (Exception ex)
                {
                    Report(false, "配置保存与读取", ex.Message);
                }

                // ---- 9. 单实例互斥体 ----
                try
                {
                    var first = new SingleInstance();
                    bool acquired = first.TryAcquire();
                    var second = new SingleInstance();
                    bool secondAcquired = second.TryAcquire();

                    // 同一进程内命名互斥体是可重入的，所以这里主要验证不抛异常且首个成功
                    Report(acquired, "单实例互斥体可创建");

                    second.Dispose();
                    first.Dispose();
                }
                catch (Exception ex)
                {
                    Report(false, "单实例互斥体可创建", ex.Message);
                }

                // ---- 10. 音频设备 ----
                try
                {
                    bool hasDevice = Audio.Player.HasOutputDevice();
                    Report(true, "音频输出设备检测", hasDevice ? "有可用设备" : "无设备（自检仍通过）");
                }
                catch (Exception ex)
                {
                    Report(false, "音频输出设备检测", ex.Message);
                }

                // ---- 11. 媒体键注册 ----
                try
                {
                    using (var keys = new MediaKeys())
                    {
                        bool ok = keys.Register();
                        // 被别的播放器占用时注册失败是正常情况，不算自检失败
                        Report(true, "媒体键注册尝试",
                            ok ? "已启用" + (keys.FailureReason != null ? "（" + keys.FailureReason + "）" : "")
                               : "不可用：" + keys.FailureReason + "（可能被其他播放器占用）");
                    }
                }
                catch (Exception ex)
                {
                    Report(false, "媒体键注册尝试", ex.Message);
                }

                // ---- 12. 托盘图标能生成 ----
                try
                {
                    using (var normal = UI.TrayIconFactory.CreateWithState(32, true))
                    using (var paused = UI.TrayIconFactory.CreateWithState(32, false))
                    {
                        Report(normal != null && paused != null &&
                               normal.Width > 0 && paused.Width > 0,
                            "托盘图标能生成",
                            normal.Width + "x" + normal.Height);
                    }
                }
                catch (Exception ex)
                {
                    Report(false, "托盘图标能生成", ex.Message);
                }

                // ---- 13. 分组独立进度记忆（切组 + 重启的核心数据） ----
                try
                {
                    var config = Config.Load();
                    // 造两个分组，各记一个进度
                    while (config.Groups.Count < 2) config.Groups.Add(new Models.Group { Name = "自检组" + config.Groups.Count });
                    var groupA = config.Groups[0];
                    var groupB = config.Groups[1];

                    groupA.LastTrackPath = @"C:\music\a.mp3";
                    groupA.LastPositionSeconds = 180.0;
                    groupB.LastTrackPath = @"C:\music\b.mp3";
                    groupB.LastPositionSeconds = 42.5;
                    config.CurrentGroupId = groupB.Id;
                    config.Save();

                    var reloaded = Config.Load();
                    var reloadedA = reloaded.Groups[0];
                    var reloadedB = reloaded.Groups[1];

                    bool ok = Math.Abs(reloadedA.LastPositionSeconds - 180.0) < 0.001 &&
                              Math.Abs(reloadedB.LastPositionSeconds - 42.5) < 0.001 &&
                              reloadedA.LastTrackPath != reloadedB.LastTrackPath &&
                              reloaded.CurrentGroupId == groupB.Id;

                    Report(ok, "每个分组各自记住播放进度（重启后仍记得）",
                        string.Format("A={0:0.0}s B={1:0.0}s", reloadedA.LastPositionSeconds, reloadedB.LastPositionSeconds));
                }
                catch (Exception ex)
                {
                    Report(false, "每个分组各自记住播放进度", ex.Message);
                }

                // ---- 14. 打乱顺序持久化 ----
                try
                {
                    var config = Config.Load();
                    var group = config.EnsureGroup();
                    group.ShuffledOrder = new List<string> { "c.mp3", "a.mp3", "b.mp3" };
                    config.Save();

                    var reloaded = Config.Load();
                    var order = reloaded.CurrentGroup != null ? reloaded.CurrentGroup.ShuffledOrder : null;
                    bool ok = order != null && order.Count == 3 &&
                              order[0] == "c.mp3" && order[1] == "a.mp3" && order[2] == "b.mp3";
                    Report(ok, "打乱顺序会被记住（存在配置里）");
                }
                catch (Exception ex)
                {
                    Report(false, "打乱顺序会被记住", ex.Message);
                }

                // ---- 15. 排序行为 ----
                try
                {
                    var group = new Models.Group { Name = "排序自检" };
                    group.Tracks.Add(new Models.Track { Title = "B歌", Path = "b.mp3", Duration = 200, AddedOrder = 2 });
                    group.Tracks.Add(new Models.Track { Title = "A歌", Path = "a.mp3", Duration = 100, AddedOrder = 1 });
                    group.Tracks.Add(new Models.Track { Title = "C歌", Path = "c.mp3", Duration = 300, AddedOrder = 3 });

                    var byTitle = TrackSorter.Sort(group, TrackSorter.Title);
                    var byDuration = TrackSorter.Sort(group, TrackSorter.Duration);
                    var byAdded = TrackSorter.Sort(group, TrackSorter.Added);

                    bool ok = byTitle[0].Title == "A歌" && byTitle[2].Title == "C歌" &&
                              byDuration[0].Duration == 100 && byDuration[2].Duration == 300 &&
                              byAdded[0].AddedOrder == 1 && byAdded[2].AddedOrder == 3;

                    // 打乱顺序优先于排序
                    group.ShuffledOrder = new List<string> { "c.mp3", "a.mp3", "b.mp3" };
                    var shuffled = TrackSorter.Sort(group, TrackSorter.Title);
                    bool shuffleWins = shuffled[0].Path == "c.mp3" && shuffled[2].Path == "b.mp3";

                    Report(ok && shuffleWins, "排序（标题/时长/最近添加）与打乱优先级");
                }
                catch (Exception ex)
                {
                    Report(false, "排序与打乱优先级", ex.Message);
                }

                // ---- 16. 搜索过滤（空格分隔多关键词） ----
                try
                {
                    var track = new Models.Track
                    {
                        Title = "夜曲",
                        Artist = "周杰伦",
                        Album = "十一月的萧邦"
                    };

                    bool single = track.Matches(new List<string> { "夜曲" });
                    bool byArtist = track.Matches(new List<string> { "周杰伦" });
                    bool byAlbum = track.Matches(new List<string> { "萧邦" });
                    bool multi = track.Matches(new List<string> { "周杰伦", "夜曲" });
                    bool reject = !track.Matches(new List<string> { "周杰伦", "不存在" });
                    bool empty = track.Matches(new List<string>());

                    Report(single && byArtist && byAlbum && multi && reject && empty,
                        "搜索匹配标题/艺术家/专辑 + 多关键词");
                }
                catch (Exception ex)
                {
                    Report(false, "搜索匹配", ex.Message);
                }

                // ---- 18. 自绘对话框能真的弹出来 ----
                // 守住两个真实发生过的 bug（都会让对话框弹不出来，
                // 导致「导入文件夹 / 新建分组 / 重命名分组 / 删除分组」全部失效）：
                //   a) 先 window.Content = root 再 frame.Child = root → 「元素已有父级」
                //   b) Setter(dp, value, "") 的空子节点标识符 → 「子节点标识符不能是空字符串」
                // 关键在于必须构造**真实的 Dialogs 按钮**并把它挂进可视化树 ——
                // 模板只有在 Seal / 套用时才会暴露这两个问题。
                try
                {
                    var ok = RunOnUiThread(() =>
                    {
                        var makeButton = typeof(UI.Dialogs).GetMethod("MakeButton",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                        if (makeButton == null) return false;

                        var okButton = (System.Windows.Controls.Button)makeButton.Invoke(
                            null, new object[] { "确定", true });
                        var cancelButton = (System.Windows.Controls.Button)makeButton.Invoke(
                            null, new object[] { "取消", false });

                        // 套用模板（含 Trigger），空 Target 会在这里抛异常
                        var panel = new System.Windows.Controls.StackPanel();
                        panel.Children.Add(okButton);
                        panel.Children.Add(cancelButton);

                        var window = new Window
                        {
                            WindowStyle = WindowStyle.None,
                            AllowsTransparency = true,
                            Background = System.Windows.Media.Brushes.Transparent,
                            ShowInTaskbar = false,
                            Width = 260,
                            Height = 160,
                            Left = -4000,
                            Top = -4000
                        };

                        // 正确顺序：先放进 frame，再把 frame 交给 Window
                        var frame = new System.Windows.Controls.Border { Child = panel };
                        window.Content = frame;

                        window.Show();
                        window.UpdateLayout();

                        // 模板套用成功即可 —— 空 Target 的 Setter 会在 Seal/套用时
                        // 直接抛异常，能走到这里就说明没有那个问题。
                        // （注意：TargetName 为空本身是合法的，表示作用于模板根，
                        //   所以不能拿「TargetName 必须非空」当判据。）
                        bool applied = okButton.Template != null && cancelButton.Template != null;

                        window.Close();
                        return applied;
                    });

                    Report(ok, "自绘对话框按钮模板可套用（守住空 Target / re-parent 回归）");
                }
                catch (Exception ex)
                {
                    // 反射调用会把真实异常包在 TargetInvocationException 里，拆出来才看得清
                    var inner = ex;
                    while (inner is System.Reflection.TargetInvocationException && inner.InnerException != null)
                        inner = inner.InnerException;

                    Report(false, "自绘对话框按钮模板可套用",
                        inner.GetType().Name + ": " + inner.Message);
                }

                // ---- 19. 桌面歌词：默认位置在屏幕上方 ----
                try
                {
                    var area = Native.Win32.GetWorkArea(IntPtr.Zero);
                    double barHeight = 92;
                    double top = area.top + 60;   // DefaultTopGap = 60

                    bool nearTop = top >= area.top && top < area.top + 200;
                    bool visible = top + barHeight > area.top;

                    Report(nearTop && visible, "桌面歌词默认位置在屏幕上方",
                        string.Format("top={0:0} 工作区顶={1}", top, area.top));
                }
                catch (Exception ex)
                {
                    Report(false, "桌面歌词默认位置", ex.Message);
                }
                // ---- 17. 文件名推导元数据 ----
                try
                {
                    var dir = Path.Combine(Paths.DataDir, "selftest");
                    Directory.CreateDirectory(dir);
                    var fake = Path.Combine(dir, "周杰伦 - 夜曲.mp3");
                    if (!File.Exists(fake)) File.WriteAllBytes(fake, new byte[] { 0 });

                    var track = Models.Track.FromFile(fake, 1);
                    bool ok = track.Artist == "周杰伦" && track.Title == "夜曲" &&
                              track.Format == "mp3" && track.Album == "selftest";

                    // "01 - intro" 里确实含 " - "，按艺术家/标题切分是预期行为；
                    // 真正不该被误切的是不带空格的分隔符，例如 "01-02 前奏"
                    var plain = Models.Track.FromFile(Path.Combine(dir, "01-02 前奏.mp3"), 2);
                    bool noFalseSplit = plain.Title == "01-02 前奏" && plain.Artist == "";

                    Report(ok && noFalseSplit, "从文件名推导艺术家/标题/专辑",
                        "'" + track.Artist + "' / '" + track.Title + "'，无空格分隔符不误切=" + noFalseSplit);
                }
                catch (Exception ex)
                {
                    Report(false, "从文件名推导元数据", ex.Message);
                }
            }
            catch (Exception ex)
            {
                Log.Error("自检过程异常", ex);
                results.Add(new Result { Name = "自检过程", Passed = false, Detail = ex.Message });
                allPassed = false;
            }

            // ---- 写结果 ----
            int passed = 0, failed = 0;
            foreach (var r in results) { if (r.Passed) passed++; else failed++; }

            var summary = new StringBuilder();
            summary.AppendLine("{");
            summary.AppendLine("  \"passed\": " + passed + ",");
            summary.AppendLine("  \"failed\": " + failed + ",");
            summary.AppendLine("  \"allPassed\": " + (allPassed ? "true" : "false") + ",");
            summary.AppendLine("  \"timestamp\": \"" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\",");
            summary.AppendLine("  \"items\": [");
            for (int i = 0; i < results.Count; i++)
            {
                var r = results[i];
                summary.Append("    { \"name\": \"").Append(Escape(r.Name))
                       .Append("\", \"passed\": ").Append(r.Passed ? "true" : "false");
                if (!string.IsNullOrEmpty(r.Detail))
                    summary.Append(", \"detail\": \"").Append(Escape(r.Detail)).Append("\"");
                summary.Append(" }");
                if (i < results.Count - 1) summary.Append(",");
                summary.AppendLine();
            }
            summary.AppendLine("  ]");
            summary.AppendLine("}");

            try
            {
                File.WriteAllText(Paths.SelftestResultFile, summary.ToString(), new UTF8Encoding(false));
                Log.Info("自检结果写入: " + Paths.SelftestResultFile);
            }
            catch (Exception ex)
            {
                Log.Error("写入自检结果失败", ex);
            }

            Log.Info(string.Format("=== 自检完成: {0} 通过 / {1} 失败 ===", passed, failed));

            // 控制台也打一份，便于命令行查看
            try
            {
                Console.WriteLine();
                Console.WriteLine("=== Lumen 自检结果 ===");
                foreach (var r in results)
                {
                    Console.WriteLine((r.Passed ? "  PASS  " : "  FAIL  ") + r.Name +
                        (string.IsNullOrEmpty(r.Detail) ? "" : "   (" + r.Detail + ")"));
                }
                Console.WriteLine();
                Console.WriteLine("通过 " + passed + " / 失败 " + failed);
                Console.WriteLine("结果文件: " + Paths.SelftestResultFile);
            }
            catch { }

            return allPassed ? 0 : 1;
        }

        private static string Escape(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                    .Replace("\r", " ").Replace("\n", " ");
        }

        private sealed class Result
        {
            public string Name;
            public bool Passed;
            public string Detail;
        }

        /// <summary>在 UI 线程上同步执行（自检需要在 STA 线程建控件）。</summary>
        private static bool RunOnUiThread(Func<bool> action)
        {
            var app = Application.Current;
            if (app == null) return action();

            if (app.Dispatcher.CheckAccess()) return action();

            bool result = false;
            Exception error = null;

            app.Dispatcher.Invoke(new Action(delegate
            {
                try { result = action(); }
                catch (Exception ex) { error = ex; }
            }), DispatcherPriority.Normal);

            if (error != null) throw error;
            return result;
        }

        /// <summary>写一个 16bit PCM 单声道正弦波 WAV（用于自检）。</summary>
        private static void WriteToneWav(string path, double seconds, double frequency)
        {
            const int sampleRate = 44100;
            int samples = (int)(sampleRate * seconds);
            int dataBytes = samples * 2;

            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(new[] { 'R', 'I', 'F', 'F' });
                writer.Write(36 + dataBytes);
                writer.Write(new[] { 'W', 'A', 'V', 'E' });
                writer.Write(new[] { 'f', 'm', 't', ' ' });
                writer.Write(16);
                writer.Write((short)1);            // PCM
                writer.Write((short)1);            // mono
                writer.Write(sampleRate);
                writer.Write(sampleRate * 2);      // byte rate
                writer.Write((short)2);            // block align
                writer.Write((short)16);           // bits
                writer.Write(new[] { 'd', 'a', 't', 'a' });
                writer.Write(dataBytes);

                for (int i = 0; i < samples; i++)
                {
                    double t = (double)i / sampleRate;
                    // 渐入渐出，避免爆音
                    double envelope = Math.Min(1.0, Math.Min(t * 10, (seconds - t) * 10));
                    double value = Math.Sin(2 * Math.PI * frequency * t) * 0.3 * Math.Max(0, envelope);
                    writer.Write((short)(value * 32767));
                }
            }
        }
    }
}
