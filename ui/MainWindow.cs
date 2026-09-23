using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Lumen.Audio;
using Lumen.Media;
using Lumen.Models;

namespace Lumen.UI
{
    /// <summary>
    /// 主窗口：自绘标题栏 + 三区布局（左歌词 / 右列表固定 50/50 + 底部播放进度区）。
    /// </summary>
    public partial class MainWindow : WindowChromeBase
    {
        private readonly CommandLine _options;
        private readonly SingleInstance _single;
        private readonly Player _player = new Player();
        private Config _config;
        private MetadataFiller _filler;

        private LyricsPane _lyricsPane;
        private TrackListPane _listPane;
        private BottomBar _bottomBar;
        private Grid _splitBody;
        private DesktopLyricsWindow _desktopLyrics;
        private TrayIcon _tray;
        private MediaKeys _mediaKeys;
        private System.Windows.Threading.DispatcherTimer _playbackTimer;

        private Group _group;
        private List<Track> _view = new List<Track>();
        private int _currentIndex = -1;

        public MainWindow(CommandLine options, SingleInstance single)
        {
            _options = options;
            _single = single;

            Title = "Lumen 音乐";
            Width = 820;
            Height = 720;
            MinWidth = 600;
            MinHeight = 420;
            // 首次打开（没有保存过位置时）出现在屏幕正中央
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            _config = Config.Load();
            _group = _config.EnsureGroup();
        }

        /// <summary>窗口显示后初始化（启动卡片此时可以收起）。</summary>
        public void InitializeAfterShow(SplashCard splash)
        {
            try
            {
                RestoreWindowPlacement();
                TitleBar = BuildTitleBar();
                Body = BuildBody();
                _filler = new MetadataFiller(Dispatcher);
                _filler.BatchCompleted += delegate { RefreshList(false); };
                _filler.AllCompleted += delegate
                {
                    RefreshList(false);
                    if (splash != null) splash.Dismiss();
                };
                HookPlayer();
                HookSingleInstance();
                HookHotkeys();
                HookMediaKeys();
                PreviewKeyDown += OnPreviewKeyDown;
                ApplyGroup(_group, false);
                QueueMetadata();

                // 恢复上次播放位置：每个分组各自记忆（不会自动播放）
                RestoreGroupProgress();

                // 处理命令行带来的文件
                if (_options.Files.Count > 0) OpenExternalFiles(_options.Files);

                UpdateModeButton();
                UpdateVolumeUi();

                // 桌面歌词：位置、字号、行数、锁定状态都会记住
                if (_desktopLyrics == null)
                {
                    _desktopLyrics = new DesktopLyricsWindow();
                    HookDesktopLyrics(_desktopLyrics);
                    _desktopLyrics.SetSavedPosition(
                        _config.DesktopLyricsLeft, _config.DesktopLyricsTop);
                }
                SyncDesktopLyrics();

                // 托盘常驻
                _tray = new TrayIcon(this);
                _tray.MediaKeysAvailable = _mediaKeys != null && _mediaKeys.IsAvailable;
                _tray.MediaKeysDetail = _mediaKeys != null ? _mediaKeys.FailureReason : "未注册";
                _tray.UpdateState();

                StartPlaybackTimer();

                // 没有待补全的元数据时，直接收起启动卡片
                if (_filler.PendingCount == 0 && splash != null) splash.Dismiss();

                Closing += OnClosing;
                Closed += OnClosed;
            }
            catch (Exception ex)
            {
                Log.Error("初始化主窗口失败", ex);
                if (splash != null) splash.Dismiss();
                Dialogs.ShowError(this, "启动失败", ex.Message);
            }
        }

        // ------------------------------------------------------------------
        // 界面搭建
        // ------------------------------------------------------------------

        private FrameworkElement BuildTitleBar()
        {
            var bar = new Grid { Height = TitleBarHeight, Background = Theme.Brush("Brush.Window") };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var title = new TextBlock
            {
                Text = "Lumen 音乐",
                FontSize = 12.5,
                Margin = new Thickness(14, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Theme.Brush("Brush.TextDim")
            };
            Grid.SetColumn(title, 0);
            bar.Children.Add(title);

            // 空白区域可拖动窗口
            var dragArea = new Border { Background = Brushes.Transparent };
            dragArea.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e)
            {
                DragMoveFromTitleBar(e);
            };
            Grid.SetColumn(dragArea, 1);
            bar.Children.Add(dragArea);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 2, 0) };

            var btnLyrics = CaptionButton(Icons.Lyric, "显示/隐藏歌词区");
            btnLyrics.Click += delegate { ToggleLyricsPane(); };
            buttons.Children.Add(btnLyrics);

            var btnMin = CaptionButton(Icons.Minimize, "最小化");
            btnMin.Click += delegate { WindowState = WindowState.Minimized; };
            buttons.Children.Add(btnMin);

            var btnMax = CaptionButton(Icons.Maximize, "最大化");
            btnMax.Click += delegate { ToggleMaximize(); };
            buttons.Children.Add(btnMax);

            var btnClose = CaptionButton(Icons.Close, "关闭（最小化到托盘）");
            btnClose.Style = (Style)FindResource("Btn.CaptionClose");
            btnClose.Click += delegate { Close(); };
            buttons.Children.Add(btnClose);

            Grid.SetColumn(buttons, 2);
            bar.Children.Add(buttons);
            return bar;
        }

        private Button CaptionButton(string icon, string tip)
        {
            var button = new Button
            {
                Style = (Style)FindResource("Btn.Caption"),
                Content = Icons.Create(icon, 12, null, Theme.Brush("Brush.TextDim"), 1.5),
                ToolTip = tip
            };
            return button;
        }

        private FrameworkElement BuildBody()
        {
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });

            // 主体：左歌词 / 右列表，固定 50/50
            _splitBody = new Grid();
            _splitBody.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _splitBody.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _lyricsPane = new LyricsPane();
            _lyricsPane.LineClicked += delegate (double seconds) { SeekTo(seconds); };
            Grid.SetColumn(_lyricsPane, 0);
            _splitBody.Children.Add(_lyricsPane);

            _listPane = new TrackListPane();
            _listPane.TrackActivated += delegate (Track t) { PlayTrack(t); };
            _listPane.PlayRequested += delegate (Track t) { PlayTrack(t); };
            _listPane.RemoveRequested += delegate (Track t) { RemoveTrack(t); };
            _listPane.RevealRequested += delegate (Track t) { RevealInExplorer(t); };
            _listPane.DeleteFileRequested += delegate (Track t) { DeleteTrackFile(t); };
            _listPane.SortChanged += delegate (string mode)
            {
                _config.SortMode = mode;
                // 打乱是一次性行为：一旦用户主动选择排序，就按排序重排，
                // 打乱结果不再强制覆盖排序，仅作为「记忆」保留在配置里。
                if (_group != null) _group.ShuffledOrder = null;
                RebuildView();
                SaveConfig();
            };
            _listPane.ShuffleRequested += delegate { ShuffleGroup(); };
            _listPane.ImportFilesRequested += delegate { ImportFiles(); };
            _listPane.ImportFolderRequested += delegate { ImportFolder(); };
            _listPane.NewGroupRequested += delegate { NewGroup(); };
            _listPane.RenameGroupRequested += delegate { RenameGroup(); };
            _listPane.DeleteGroupRequested += delegate { DeleteGroup(); };
            _listPane.GroupSelected += delegate (string id) { SwitchGroup(id); };
            _listPane.FilesDropped += delegate (IList<string> files) { ImportPaths(files); };
            Grid.SetColumn(_listPane, 1);
            _splitBody.Children.Add(_listPane);

            Grid.SetRow(_splitBody, 0);
            root.Children.Add(_splitBody);

            _bottomBar = new BottomBar();
            _bottomBar.PlayPauseClicked += delegate { TogglePlayPause(); };
            _bottomBar.PrevClicked += delegate { PlayPrevious(); };
            _bottomBar.NextClicked += delegate { PlayNext(true); };
            _bottomBar.ModeClicked += delegate { CycleMode(); };
            _bottomBar.SeekRequested += delegate (double seconds) { SeekTo(seconds); };
            _bottomBar.VolumeChanged += delegate (float v)
            {
                _config.VolumePercent = (int)Math.Round(v * 100);
                _player.Volume = v;
                _config.Muted = false;
                UpdateVolumeUi();
            };
            _bottomBar.VolumeDelta += delegate (float delta) { NudgeVolume(delta); };
            _bottomBar.MuteToggled += delegate
            {
                _config.Muted = !_config.Muted;
                _player.Muted = _config.Muted;
                UpdateVolumeUi();
            };
            Grid.SetRow(_bottomBar, 1);
            root.Children.Add(_bottomBar);

            ApplyPaneVisibility();
            return root;
        }

        // ------------------------------------------------------------------
        // 分组 / 列表
        // ------------------------------------------------------------------

        private void ApplyGroup(Group group, bool restorePosition)
        {
            _group = group ?? _config.EnsureGroup();
            _config.CurrentGroupId = _group.Id;
            _listPane.SetGroups(_config.Groups, _group.Id);
            RebuildView();

            if (restorePosition && !string.IsNullOrEmpty(_group.LastTrackPath))
            {
                _player.Load(_group.LastTrackPath, _group.LastPositionSeconds);
                _currentIndex = IndexOfPath(_group.LastTrackPath);
                _listPane.SetPlaying(_currentIndex);
                LoadLyricsFor(_group.LastTrackPath);
                UpdateBottomBar();
            }
        }

        private void SwitchGroup(string id)
        {
            if (id == null || (_group != null && id == _group.Id)) return;
            SaveGroupProgress();

            var group = _config.FindGroup(id);
            if (group == null) return;

            _config.CurrentGroupId = id;
            ApplyGroup(group, true);
            SaveConfig();
        }

        /// <summary>把分组内容按当前排序/打乱重建为可见列表。</summary>
        private void RebuildView()
        {
            _view = TrackSorter.Sort(_group, _config.SortMode);
            _listPane.SetTracks(_view, _config.SortMode);
            _currentIndex = _currentIndexPath();
            // 重建列表后按新位置刷新播放标记，否则排序/打乱后标记会停在旧 index。
            _listPane.SetPlaying(_currentIndex);
        }

        private int _currentIndexPath()
        {
            var path = _player.CurrentPath;
            if (string.IsNullOrEmpty(path)) return -1;
            return IndexOfPath(path);
        }

        private int IndexOfPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return -1;
            for (int i = 0; i < _view.Count; i++)
            {
                if (string.Equals(_view[i].Path, path, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return -1;
        }

        private void RefreshList(bool rebuildView)
        {
            if (rebuildView) RebuildView();
            _listPane.RefreshRows();
            _listPane.SetPlaying(_currentIndexPath());
        }

        private void RemoveTrack(Track track)
        {
            if (track == null || _group == null) return;
            _group.Tracks.Remove(track);
            if (_group.ShuffledOrder != null) _group.ShuffledOrder.Remove(track.Path);
            if (string.Equals(_player.CurrentPath, track.Path, StringComparison.OrdinalIgnoreCase))
            {
                _player.Stop();
                _currentIndex = -1;
            }
            RebuildView();
            SaveConfig();
        }

        /// <summary>右键菜单「删除此文件」：送进回收站（不弹确认），并从列表移除。</summary>
        private void DeleteTrackFile(Track track)
        {
            if (track == null || string.IsNullOrEmpty(track.Path)) return;
            if (!System.IO.File.Exists(track.Path)) return;

            var name = System.IO.Path.GetFileName(track.Path);

            // 正在播放就先停掉，避免句柄占用导致删除失败
            if (string.Equals(_player.CurrentPath, track.Path, StringComparison.OrdinalIgnoreCase))
            {
                _player.Stop();
                _currentIndex = -1;
            }

            int result = Native.Win32.DeleteToRecycleBin(track.Path);
            if (result != 0)
            {
                Log.Warn("送回收站失败: " + name + " (code " + result + ")");
                Dialogs.ShowError(this, "删除失败", "无法将文件送进回收站：" + name);
                return;
            }

            RemoveTrack(track);
            _listPane.FlashStatus("已送进回收站：" + name);
        }

        private static void RevealInExplorer(Track track)
        {
            if (track == null || string.IsNullOrEmpty(track.Path)) return;
            try
            {
                System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + track.Path + "\"");
            }
            catch (Exception ex)
            {
                Log.Warn("打开资源管理器失败: " + ex.Message);
            }
        }

        /// <summary>「打乱顺序」是一次性操作，结果写进配置，下次打开还是这个顺序。</summary>
        private void ShuffleGroup()
        {
            if (_group == null || _group.Tracks.Count == 0) return;

            var paths = new List<string>();
            foreach (var t in _group.Tracks) paths.Add(t.Path);

            var random = new Random();
            for (int i = paths.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                var tmp = paths[i]; paths[i] = paths[j]; paths[j] = tmp;
            }

            _group.ShuffledOrder = paths;
            RebuildView();
            SaveConfig();
            _listPane.FlashStatus("已打乱顺序（顺序会被记住）");
        }

        // ------------------------------------------------------------------
        // 导入
        // ------------------------------------------------------------------

        private void ImportFiles()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Multiselect = true,
                Filter = "音频文件|*.mp3;*.flac;*.wav;*.m4a;*.aac;*.ogg;*.opus;*.wma;*.ape;*.aiff;*.aif;*.mp4|所有文件|*.*",
                Title = "导入文件"
            };
            if (dialog.ShowDialog(this) == true) ImportPaths(dialog.FileNames);
        }

        private void ImportFolder()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "导入文件夹" };
            if (dialog.ShowDialog(this) != true) return;

            var folder = dialog.FolderName;
            var name = System.IO.Path.GetFileName(folder.TrimEnd('\\', '/'));
            if (string.IsNullOrEmpty(name)) name = folder.TrimEnd('\\', '/');

            // 只要当前有分组，就问「并入还是新建」。
            // （之前用 Tracks.Count > 0 判断，导致在导入出来的空分组里点导入时
            //   根本不出选择框，看起来像「没有任何反应」。）
            bool createNew = true;
            if (_group != null)
            {
                createNew = Dialogs.Confirm(this,
                    "导入文件夹",
                    "把「" + name + "」作为新分组，还是并入当前分组「" + _group.Name + "」？\n\n" +
                    "确定 = 新建分组，取消 = 并入当前分组",
                    "新建分组");
            }

            var order = NextAddedOrder();
            var folderPath = folder;
            var groupName = name;
            var makeNew = createNew;

            // 扫描放后台，避免大文件夹把界面冻住
            _listPane.FlashStatus("正在扫描…");

            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                List<Track> tracks;
                try
                {
                    int counter = order;
                    tracks = MediaScanner.FromFiles(new[] { folderPath }, ref counter);
                }
                catch (Exception ex)
                {
                    Log.Error("扫描文件夹失败", ex);
                    tracks = new List<Track>();
                }

                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (tracks.Count == 0)
                    {
                        Dialogs.ShowInfo(this, "没有找到音频", "这个文件夹里没有找到支持的音频文件。");
                        _listPane.FlashStatus("");
                        return;
                    }

                    if (makeNew)
                    {
                        var group = new Group
                        {
                            Name = string.IsNullOrEmpty(groupName) ? "新分组" : groupName,
                            FromFolder = true,
                            SourceFolder = folderPath,
                            Tracks = tracks
                        };
                        _config.Groups.Add(group);
                        _config.CurrentGroupId = group.Id;
                        ApplyGroup(group, false);
                    }
                    else
                    {
                        AddTracks(tracks);
                    }

                    _filler.Enqueue(tracks);
                    SaveConfig();
                    _listPane.FlashStatus("已导入 " + tracks.Count + " 首");
                }), System.Windows.Threading.DispatcherPriority.Background);
            });
        }

        private void ImportPaths(IList<string> paths)
        {
            if (paths == null || paths.Count == 0) return;

            // 扫描放到后台线程：600 首要走几千次目录枚举 + 文件属性读取，
            // 放在 UI 线程会直接把界面冻住好几秒。
            var snapshot = new List<string>(paths);
            int baseOrder = NextAddedOrder();

            _listPane.FlashStatus("正在扫描…");

            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                List<Track> tracks;
                try
                {
                    int order = baseOrder;
                    tracks = MediaScanner.FromFiles(snapshot, ref order);
                }
                catch (Exception ex)
                {
                    Log.Error("扫描失败", ex);
                    tracks = new List<Track>();
                }

                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (tracks.Count == 0)
                    {
                        Dialogs.ShowInfo(this, "没有找到音频", "拖进来的内容里没有支持的音频文件。");
                        _listPane.FlashStatus("");
                        return;
                    }

                    AddTracks(tracks);
                    _filler.Enqueue(tracks);
                    SaveConfig();
                    _listPane.FlashStatus("已导入 " + tracks.Count + " 首");
                }), System.Windows.Threading.DispatcherPriority.Background);
            });
        }

        private void AddTracks(List<Track> tracks)
        {
            if (_group == null) return;
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in _group.Tracks) existing.Add(t.Path);

            bool added = false;
            foreach (var t in tracks)
            {
                if (existing.Contains(t.Path)) continue;
                _group.Tracks.Add(t);
                existing.Add(t.Path);
                added = true;
            }

            // 重新导入即恢复有序（README）
            _group.ShuffledOrder = null;
            if (added) RebuildView();
        }

        private int NextAddedOrder()
        {
            int max = 0;
            foreach (var g in _config.Groups)
                foreach (var t in g.Tracks)
                    if (t.AddedOrder > max) max = t.AddedOrder;
            return max + 1;
        }

        // ------------------------------------------------------------------
        // 分组管理
        // ------------------------------------------------------------------

        private void NewGroup()
        {
            var name = Dialogs.Prompt(this, "新建分组", "分组名称：", "新分组");
            if (string.IsNullOrWhiteSpace(name)) return;

            var group = new Group { Name = name.Trim() };
            _config.Groups.Add(group);
            _config.CurrentGroupId = group.Id;
            ApplyGroup(group, false);
            SaveConfig();
        }

        private void RenameGroup()
        {
            if (_group == null) return;
            var name = Dialogs.Prompt(this, "重命名分组", "新的分组名称：", _group.Name);
            if (string.IsNullOrWhiteSpace(name)) return;

            _group.Name = name.Trim();
            _listPane.SetGroups(_config.Groups, _group.Id);
            SaveConfig();
        }

        private void DeleteGroup()
        {
            if (_group == null || _config.Groups.Count <= 1)
            {
                Dialogs.ShowInfo(this, "无法删除", "至少要保留一个分组。");
                return;
            }

            if (!Dialogs.Confirm(this, "删除分组",
                    "确定删除分组「" + _group.Name + "」吗？\n（只从列表里移除，不会删除你的音乐文件）",
                    "删除"))
                return;

            var removing = _group;
            _config.Groups.Remove(removing);
            _player.Stop();
            _currentIndex = -1;
            _config.CurrentGroupId = _config.Groups[0].Id;
            ApplyGroup(_config.Groups[0], false);
            SaveConfig();
        }

        // ------------------------------------------------------------------
        // 播放
        // ------------------------------------------------------------------

        private void HookPlayer()
        {
            _player.PlaybackEnded += delegate
            {
                Dispatcher.BeginInvoke(new Action(delegate { OnTrackEnded(); }));
            };
            _player.PlaybackFailed += delegate (string message)
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    _listPane.FlashStatus(message);
                    // 播放失败自动跳到下一首（README）
                    if (_view.Count > 1) PlayNext(false);
                }));
            };
        }

        private void PlayTrack(Track track)
        {
            if (track == null) return;

            var index = _view.IndexOf(track);
            var startAt = 0.0;

            // 同一首再次点击则从头播放
            if (index >= 0 && string.Equals(_player.CurrentPath, track.Path, StringComparison.OrdinalIgnoreCase))
                startAt = 0;


            if (_player.Play(track.Path, startAt))
            {
                _currentIndex = index;
                _lastKnownPosition = startAt;
                _playbackEnded = false;
                _listPane.SetPlaying(index);
                LoadLyricsFor(track.Path);
                UpdateBottomBar();
            }
        }

        private void TogglePlayPause()
        {
            if (!_player.HasTrack)
            {
                // 还没选歌：从当前播放位置或第一首开始
                if (_currentIndex >= 0 && _currentIndex < _view.Count) PlayTrack(_view[_currentIndex]);
                else if (_view.Count > 0) PlayTrack(_view[0]);
                return;
            }

            _player.TogglePlayPause();
            UpdateBottomBar();
        }

        private void PlayNext(bool fromUser)
        {
            if (_view.Count == 0) return;

            if (_player.Mode == Player.PlayMode.RepeatOne && !fromUser)
            {
                PlayTrack(_view[Math.Max(0, _currentIndex)]);
                return;
            }

            int next = _currentIndex + 1;
            if (next >= _view.Count)
            {
                if (_player.Mode == Player.PlayMode.Sequential)
                {
                    _player.Stop();
                    _listPane.SetPlaying(-1);
                    UpdateBottomBar();
                    return;
                }
                next = 0;
            }
            PlayTrack(_view[next]);
        }

        private void PlayPrevious()
        {
            if (_view.Count == 0) return;

            int prev = _currentIndex - 1;
            if (prev < 0) prev = _player.Mode == Player.PlayMode.Sequential ? 0 : _view.Count - 1;
            PlayTrack(_view[prev]);
        }

        private void OnTrackEnded()
        {
            // 当前曲目已放到结尾：进度按 README 归零（下次从头开始）
            _playbackEnded = true;
            SaveGroupProgress();
            _config.Save();

            if (_player.Mode == Player.PlayMode.RepeatOne)
            {
                PlayTrack(_view[Math.Max(0, _currentIndex)]);
                return;
            }
            PlayNext(false);
        }

        private void CycleMode()
        {
            var mode = (int)_player.Mode;
            mode = (mode + 1) % 3;
            _player.Mode = (Player.PlayMode)mode;
            _config.PlayMode = mode;
            UpdateModeButton();
            SaveConfig();
        }

        private void SeekTo(double seconds)
        {
            if (!_player.HasTrack) return;
            _player.Seek(seconds);
            UpdateBottomBar();
        }

        // ------------------------------------------------------------------
        // 歌词
        // ------------------------------------------------------------------

        private void LoadLyricsFor(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                _lyricsPane.SetLyrics(null);
                if (_desktopLyrics != null) _desktopLyrics.SetLyrics(null);
                return;
            }

            var duration = _player.DurationSeconds;

            // 读歌词要走磁盘；缓存未命中时放后台，避免切歌时卡一下。
            // 切歌必须秒响应，所以这里先清空再异步填。
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                Lyrics.LyricsDocument doc = null;
                try
                {
                    doc = Lyrics.LyricsFinder.Find(path, duration);
                }
                catch (Exception ex)
                {
                    Log.Warn("加载歌词失败: " + ex.Message);
                }

                Dispatcher.BeginInvoke(new Action(delegate
                {
                    // 期间又切歌了：丢弃这次结果
                    if (!string.Equals(_player.CurrentPath, path, StringComparison.OrdinalIgnoreCase)) return;

                    _lyricsPane.SetLyrics(doc);
                    if (_desktopLyrics != null) _desktopLyrics.SetLyrics(doc);
                    AutoToggleLyricsPane(doc);
                }), System.Windows.Threading.DispatcherPriority.Background);
            });
        }

        private bool _lyricsPaneUserCollapsed;

        private void AutoToggleLyricsPane(Lyrics.LyricsDocument doc)
        {
            if (_lyricsPaneUserCollapsed) return;

            bool hasLyrics = doc != null && !doc.IsEmpty;
            bool changed = _config.ShowLyricsPane != hasLyrics;
            _config.ShowLyricsPane = hasLyrics;
            if (changed) ApplyPaneVisibility();
        }

        private void ToggleLyricsPane()
        {
            _config.ShowLyricsPane = !_config.ShowLyricsPane;
            _lyricsPaneUserCollapsed = !_config.ShowLyricsPane;
            ApplyPaneVisibility();
            SaveConfig();
        }

        private void ApplyPaneVisibility()
        {
            bool showLyrics = _config.ShowLyricsPane;
            bool showList = _config.ShowListPane;

            // 两侧都关掉时至少保留列表，避免空白窗口
            if (!showLyrics && !showList)
            {
                showList = true;
                _config.ShowListPane = true;
            }

            _lyricsPane.Visibility = showLyrics ? Visibility.Visible : Visibility.Collapsed;
            _listPane.Visibility = showList ? Visibility.Visible : Visibility.Collapsed;

            if (_splitBody != null && _splitBody.ColumnDefinitions.Count == 2)
            {
                // 隐藏一侧后另一侧自动占满（固定 50/50，不可拖动）
                _splitBody.ColumnDefinitions[0].Width = showLyrics
                    ? new GridLength(1, GridUnitType.Star)
                    : new GridLength(0);
                _splitBody.ColumnDefinitions[1].Width = showList
                    ? new GridLength(1, GridUnitType.Star)
                    : new GridLength(0);
            }
        }

        // ------------------------------------------------------------------
        // 桌面歌词
        // ------------------------------------------------------------------

        private void HookDesktopLyrics(DesktopLyricsWindow window)
        {
            window.PositionChanged += delegate (double left, double top, double height)
            {
                _config.DesktopLyricsLeft = left;
                _config.DesktopLyricsTop = top;
                _config.DesktopLyricsHeight = height;
            };
            window.LockChanged += delegate (bool locked)
            {
                _config.DesktopLyricsLocked = locked;
                SaveConfig();
                if (_tray != null) _tray.UpdateState();
            };
            window.FontScaleChanged += delegate (double scale)
            {
                _config.DesktopLyricsFontScale = scale;
                SaveConfig();
            };
        }

        private void SyncDesktopLyrics()
        {
            if (_desktopLyrics == null) return;

            // 位置、字号、行数、锁定状态都会记住
            _desktopLyrics.ApplyConfig(_config);

            if (_config.DesktopLyricsVisible)
            {
                if (!_desktopLyrics.IsVisible) _desktopLyrics.Show();
                _desktopLyrics.SetTickerEnabled(true);

                if (_player.HasTrack)
                {
                    _desktopLyrics.SetLyrics(Lyrics.LyricsFinder.Find(
                        _player.CurrentPath, _player.DurationSeconds));
                }
            }
            else
            {
                _desktopLyrics.Hide();
                _desktopLyrics.SetTickerEnabled(false);
            }

            if (_tray != null) _tray.UpdateState();
        }

        private void EnsureDesktopLyrics()
        {
            if (_desktopLyrics != null) return;
            _desktopLyrics = new DesktopLyricsWindow();
            HookDesktopLyrics(_desktopLyrics);
            _desktopLyrics.SetSavedPosition(_config.DesktopLyricsLeft, _config.DesktopLyricsTop);
        }

        // ------------------------------------------------------------------
        // 窗口与状态
        // ------------------------------------------------------------------

        private void RestoreWindowPlacement()
        {
            if (!double.IsNaN(_config.WindowLeft) && !double.IsNaN(_config.WindowTop))
            {
                // 检查是否还在可见的屏幕范围内
                var left = _config.WindowLeft;
                var top = _config.WindowTop;
                if (left > -32000 && top > -32000 && left < SystemParameters.VirtualScreenWidth &&
                    top < SystemParameters.VirtualScreenHeight)
                {
                    Left = left;
                    Top = top;
                }
            }
            if (_config.WindowWidth > 400) Width = _config.WindowWidth;
            if (_config.WindowHeight > 300) Height = _config.WindowHeight;
            if (_config.WindowMaximized) WindowState = WindowState.Maximized;

            _player.Volume = _config.VolumePercent / 100f;
            _player.Muted = _config.Muted;
            _player.Mode = (Player.PlayMode)Math.Max(0, Math.Min(2, _config.PlayMode));
        }

        private void SaveWindowPlacement()
        {
            _config.WindowMaximized = WindowState == WindowState.Maximized;
            if (WindowState == WindowState.Normal)
            {
                _config.WindowLeft = Left;
                _config.WindowTop = Top;
                _config.WindowWidth = Width;
                _config.WindowHeight = Height;
            }
        }

        /// <summary>
        /// 保存当前分组的播放进度。
        ///
        /// 关键点：一旦播放自然结束，读出来的 position 会回落到 0，
        /// 如果直接保存就会把「刚才听到 4.7 秒」这个信息抹掉。
        /// 所以这里用 _lastKnownPosition 记录播放过程中见过的最后一个有效位置，
        /// 只有在确实播到结尾时才按 README 归零（下次从头开始）。
        /// </summary>
        private void SaveGroupProgress()
        {
            if (_group == null) return;

            var path = _player.CurrentPath;
            if (string.IsNullOrEmpty(path)) return;

            double duration = _player.DurationSeconds;
            bool ended = _playbackEnded || IsAtEnd(_player.PositionSeconds, duration);

            double position;
            if (ended)
            {
                // 上次已经放到结尾的曲子，下次从头开始
                position = 0;
            }
            else
            {
                // 播放中：用实时位置；已停止但未播完：用记住的最后位置
                double live = _player.PositionSeconds;
                position = live > 0.05 ? live : _lastKnownPosition;
            }

            _group.LastTrackPath = path;
            _group.LastPositionSeconds = Math.Max(0, position);
        }

        private double _lastKnownPosition;
        private bool _playbackEnded;

        private static bool IsAtEnd(double position, double duration)
        {
            return duration > 0 && position >= duration - 0.6;
        }

        private void SaveConfig()
        {
            SaveGroupProgress();
            _config.Save();
        }

        private void SaveConfigSoon()
        {
            // 播放进度频繁变化，做一次 1.5 秒的防抖
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1500)
            };
            timer.Tick += delegate
            {
                timer.Stop();
                SaveConfig();
            };
            timer.Start();
        }

        private void UpdateModeButton()
        {
            if (_bottomBar == null) return;
            _bottomBar.SetMode((int)_player.Mode);
        }

        private void UpdateVolumeUi()
        {
            if (_bottomBar == null) return;
            _bottomBar.SetVolume(_config.VolumePercent / 100f, _config.Muted);
        }

        /// <summary>按增量调整音量（鼠标滚轮 / 上下方向键共用）。</summary>
        private void NudgeVolume(float delta)
        {
            int current = _config.VolumePercent;
            int next = Math.Max(0, Math.Min(100, current + (int)Math.Round(delta * 100)));
            if (next == current) return;

            _config.VolumePercent = next;
            _player.Volume = next / 100f;
            _config.Muted = false;
            UpdateVolumeUi();
            SaveConfig();
        }

        private void UpdateBottomBar()
        {
            if (_bottomBar == null) return;

            Track track = (_currentIndex >= 0 && _currentIndex < _view.Count) ? _view[_currentIndex] : null;
            _bottomBar.SetTrack(track);
            _bottomBar.SetPlaying(_player.IsPlaying);
            _bottomBar.SetTimeline(_player.PositionSeconds, _player.DurationSeconds, _player.HasTrack);
        }

        private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // 点关闭 = 最小化到托盘继续播放
            if (!_reallyExit)
            {
                e.Cancel = true;
                Hide();
                SaveConfig();
                return;
            }

            SaveWindowPlacement();
            SaveConfig();
        }

        private void OnClosed(object sender, EventArgs e)
        {
            try { _playbackTimer?.Stop(); } catch { }
            try { _filler?.Cancel(); } catch { }
            try { _mediaKeys?.Dispose(); } catch { }
            try { _player.Dispose(); } catch { }
            try { _desktopLyrics?.Close(); } catch { }
            try { _tray?.Dispose(); } catch { }
            try { _single?.Dispose(); } catch { }
        }

        private bool _reallyExit;

        /// <summary>真正退出（托盘菜单调用）。</summary>
        public void ExitApplication()
        {
            _reallyExit = true;
            try { Close(); } catch { }
            if (Application.Current != null) Application.Current.Shutdown();
        }

        public Config Configuration { get { return _config; } }
        public Player AudioPlayer { get { return _player; } }
        public DesktopLyricsWindow DesktopLyrics { get { return _desktopLyrics; } }
        public void EnsureDesktopLyricsForTray() { EnsureDesktopLyrics(); }
        public void RefreshDesktopLyrics() { SyncDesktopLyrics(); }
        public void ToggleDesktopLyrics()
        {
            _config.DesktopLyricsVisible = !_config.DesktopLyricsVisible;
            SyncDesktopLyrics();
            SaveConfig();
        }
        public void SaveConfigNow() { SaveConfig(); }
        public void PlayPauseFromTray() { TogglePlayPause(); }
        public void NextFromTray() { PlayNext(true); }
        public void PrevFromTray() { PlayPrevious(); }
        public void CycleModeFromTray() { CycleMode(); }
        public void ReloadConfig() { _config.Save(); }

        private void HookSingleInstance()
        {
            if (_single == null) return;
            _single.FilesReceived += delegate (IList<string> files)
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    OpenExternalFiles(files);
                    SingleInstance.BringToFront(this);
                }));
            };
            _single.ActivationRequested += delegate
            {
                Dispatcher.BeginInvoke(new Action(delegate { SingleInstance.BringToFront(this); }));
            };
        }

        // ------------------------------------------------------------------
        // 快捷键（只保留编辑类，播放控制交给媒体键）
        // ------------------------------------------------------------------

        private void HookHotkeys()
        {
            InputBindings.Add(new KeyBinding(
                new RelayCommand(delegate { ImportFiles(); }),
                Key.O, ModifierKeys.Control));

            InputBindings.Add(new KeyBinding(
                new RelayCommand(delegate { ImportFolder(); }),
                Key.O, ModifierKeys.Control | ModifierKeys.Shift));

            InputBindings.Add(new KeyBinding(
                new RelayCommand(delegate { _listPane.FocusSearch(); }),
                Key.F, ModifierKeys.Control));

            InputBindings.Add(new KeyBinding(
                new RelayCommand(delegate { ToggleLyricsPane(); }),
                Key.L, ModifierKeys.Control | ModifierKeys.Alt));

            InputBindings.Add(new KeyBinding(
                new RelayCommand(delegate { ToggleDesktopLyrics(); }),
                Key.D, ModifierKeys.Control | ModifierKeys.Alt));

            InputBindings.Add(new KeyBinding(
                new RelayCommand(delegate
                {
                    _config.Muted = !_config.Muted;
                    _player.Muted = _config.Muted;
                    UpdateVolumeUi();
                    SaveConfig();
                }),
                Key.M, ModifierKeys.Control | ModifierKeys.Alt));

            // Esc 退出最大化
            InputBindings.Add(new KeyBinding(
                new RelayCommand(delegate
                {
                    if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal;
                }),
                Key.Escape, ModifierKeys.None));
        }

        /// <summary>
        /// 全局按键：空格=播放/暂停，←/→=快退/快进，↑/↓=调大/调小音量。
        /// 仅当焦点不在文本输入框（如搜索框）时才生效，避免误触发。
        /// </summary>
        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            // 焦点在文本框 / 密码框等可输入控件里时，方向键与空格属于正常输入/光标操作，交给控件处理。
            var focused = Keyboard.FocusedElement as DependencyObject;
            while (focused != null)
            {
                if (focused is TextBox || focused is PasswordBox) return;
                focused = System.Windows.Media.VisualTreeHelper.GetParent(focused);
            }

            switch (e.Key)
            {
                case Key.Space:
                    TogglePlayPause();
                    e.Handled = true;
                    break;

                case Key.Left:
                    SeekBy(-5);
                    e.Handled = true;
                    break;

                case Key.Right:
                    SeekBy(5);
                    e.Handled = true;
                    break;

                case Key.Up:
                    NudgeVolume(0.05f);
                    e.Handled = true;
                    break;

                case Key.Down:
                    NudgeVolume(-0.05f);
                    e.Handled = true;
                    break;
            }
        }

        /// <summary>按增量调整播放位置（左右方向键）。</summary>
        private void SeekBy(double deltaSeconds)
        {
            if (!_player.HasTrack) return;
            SeekTo(_player.PositionSeconds + deltaSeconds);
        }

        // ------------------------------------------------------------------
        // 系统媒体键
        // ------------------------------------------------------------------

        private void HookMediaKeys()
        {
            _mediaKeys = new MediaKeys();
            _mediaKeys.PlayPausePressed += delegate { TogglePlayPause(); };
            _mediaKeys.NextPressed += delegate { PlayNext(true); };
            _mediaKeys.PrevPressed += delegate { PlayPrevious(); };
            _mediaKeys.StopPressed += delegate
            {
                _player.Stop();
                _listPane.SetPlaying(-1);
                UpdateBottomBar();
            };

            bool ok = _mediaKeys.Register();
            Log.Info(ok
                ? "媒体键已启用" + (_mediaKeys.FailureReason != null ? "（" + _mediaKeys.FailureReason + "）" : "")
                : "媒体键不可用：" + _mediaKeys.FailureReason);
        }

        // ------------------------------------------------------------------
        // 播放进度定时器（驱动歌词高亮 / 桌面歌词 / 进度条）
        // ------------------------------------------------------------------

        private void StartPlaybackTimer()
        {
            _playbackTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(120)
            };
            _playbackTimer.Tick += delegate { TickPlayback(); };
            _playbackTimer.Start();
        }

        private DateTime _lastProgressSave = DateTime.UtcNow;

        private void TickPlayback()
        {
            try
            {
                if (!_player.HasTrack)
                {
                    UpdateBottomBar();
                    return;
                }

                double position = _player.PositionSeconds;
                double duration = _player.DurationSeconds;

                // 记录播放过程中见过的最后一个有效位置：
                // 播放结束后 position 会回落，靠它才能保住进度
                if (_player.IsPlaying && position > 0.05)
                {
                    _lastKnownPosition = position;
                    _playbackEnded = IsAtEnd(position, duration);
                }

                _bottomBar.SetTimeline(position, duration, true);
                _bottomBar.SetPlaying(_player.IsPlaying);
                _lyricsPane.UpdatePosition(position, true);

                if (_desktopLyrics != null && _config.DesktopLyricsVisible)
                    _desktopLyrics.UpdatePosition(position);


                // 分组进度定期落盘（每 5 秒），避免频繁写文件
                if ((DateTime.UtcNow - _lastProgressSave).TotalSeconds >= 5)
                {
                    _lastProgressSave = DateTime.UtcNow;
                    SaveGroupProgress();
                    _config.Save();
                }
            }
            catch (Exception ex)
            {
                Log.Warn("播放进度刷新失败: " + ex.Message);
            }
        }

        // ------------------------------------------------------------------
        // 启动时恢复每个分组各自的播放进度
        // ------------------------------------------------------------------

        private void RestoreGroupProgress()
        {
            if (_group == null || string.IsNullOrEmpty(_group.LastTrackPath)) return;
            if (!System.IO.File.Exists(_group.LastTrackPath))
            {
                _group.LastTrackPath = null;
                _group.LastPositionSeconds = 0;
                return;
            }

            try
            {
                // 恢复时不会自动播放（等用户按播放键）
                if (_player.Load(_group.LastTrackPath, _group.LastPositionSeconds))
                {
                    _currentIndex = IndexOfPath(_group.LastTrackPath);
                    _lastKnownPosition = _group.LastPositionSeconds;
                    _playbackEnded = false;
                    _listPane.SetPlaying(_currentIndex);
                    LoadLyricsFor(_group.LastTrackPath);
                    UpdateBottomBar();
                    _bottomBar.SetPlaying(false);

                    // 打开时定位到当前播放位置（等布局完成后再滚动）
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        _listPane.ScrollToPlaying();
                    }), System.Windows.Threading.DispatcherPriority.Loaded);

                    Log.Info(string.Format("已恢复分组「{0}」的进度: {1} @ {2:0.0}s",
                        _group.Name,
                        System.IO.Path.GetFileName(_group.LastTrackPath),
                        _group.LastPositionSeconds));
                }
            }
            catch (Exception ex)
            {
                Log.Warn("恢复播放进度失败: " + ex.Message);
            }
        }

        /// <summary>
        /// 从文件关联打开：在现有分组里就切过去并播放；
        /// 不在库里就统一收进「打开的文件」分组（README）。
        /// </summary>
        public void OpenExternalFiles(IList<string> files)
        {
            if (files == null || files.Count == 0) return;

            var pending = new List<Track>();

            foreach (var path in files)
            {
                var found = FindTrackInLibrary(path);
                if (found != null)
                {
                    // 切到它所在的分组、选中它
                    var owner = FindGroupOfTrack(found);
                    if (owner != null && owner.Id != _config.CurrentGroupId)
                    {
                        SaveGroupProgress();
                        _config.CurrentGroupId = owner.Id;
                        ApplyGroup(owner, false);
                    }
                    PlayTrack(found);
                    continue;
                }

                var order = NextAddedOrder();
                var scanned = MediaScanner.FromFiles(new[] { path }, ref order);
                pending.AddRange(scanned);
            }

            if (pending.Count == 0) return;

            var group = _config.FindGroupByName("打开的文件") ?? CreateOpenFilesGroup();
            _config.CurrentGroupId = group.Id;
            ApplyGroup(group, false);
            AddTracks(pending);
            _filler.Enqueue(pending);
            if (pending.Count > 0) PlayTrack(_view[0]);
            SaveConfig();
        }

        private Group CreateOpenFilesGroup()
        {
            var existing = _config.FindGroupByName("打开的文件");
            if (existing != null) return existing;

            var group = new Group { Name = "打开的文件" };
            _config.Groups.Add(group);
            return group;
        }

        private Track FindTrackInLibrary(string path)
        {
            foreach (var g in _config.Groups)
            {
                foreach (var t in g.Tracks)
                {
                    if (string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase)) return t;
                }
            }
            return null;
        }

        private Group FindGroupOfTrack(Track track)
        {
            foreach (var g in _config.Groups)
            {
                if (g.Tracks.Contains(track)) return g;
            }
            return null;
        }

        private void QueueMetadata()
        {
            if (_filler == null) return;
            var all = new List<Track>();
            foreach (var g in _config.Groups)
            {
                foreach (var t in g.Tracks)
                {
                    if (!t.MetadataLoaded) all.Add(t);
                }
            }
            if (all.Count > 0) _filler.Enqueue(all);
        }

        /// <summary>供自检使用：统计当前状态。</summary>
        public IDictionary<string, object> DescribeState()
        {
            var state = new Dictionary<string, object>();
            state["groups"] = _config.Groups.Count;
            state["tracks"] = _group != null ? _group.Tracks.Count : 0;
            state["visible"] = _view.Count;
            state["lyricsPane"] = _config.ShowLyricsPane;
            state["listPane"] = _config.ShowListPane;
            state["hasDevice"] = Player.HasOutputDevice();
            state["playing"] = _player.IsPlaying;
            return state;
        }
    }
}
