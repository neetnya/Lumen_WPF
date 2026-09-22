using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Lumen.UI
{
    /// <summary>
    /// 底部播放进度区：
    /// 圆形播放/暂停键、上一首/下一首、播放模式、可点击跳转的进度条、
    /// 音量与静音、专辑封面与曲目信息。
    /// </summary>
    public sealed class BottomBar : Grid
    {
        private readonly Button _btnPlay;
        private readonly Button _btnPrev;
        private readonly Button _btnNext;
        private readonly Button _btnMode;
        private readonly Button _btnMute;
        private readonly ProgressBarEx _progress;
        private readonly SliderEx _volume;
        private readonly TextBlock _title;
        private readonly TextBlock _subtitle;
        private readonly TextBlock _time;
        private readonly TextBlock _volumePercent;
        private readonly Border _cover;

        private bool _isPlaying;
        private int _mode;

        public event Action PlayPauseClicked;
        public event Action PrevClicked;
        public event Action NextClicked;
        public event Action ModeClicked;
        public event Action<double> SeekRequested;
        public event Action<float> VolumeChanged;
        public event Action MuteToggled;

        public BottomBar()
        {
            Height = 84;
            Background = Theme.Brush("Brush.Panel");
            Margin = new Thickness(0);

            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // ---- 进度条（贴在最上面），右侧带时间显示 ----
            var progressRow = new Grid { Height = 14, VerticalAlignment = VerticalAlignment.Top };
            progressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            progressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _progress = new ProgressBarEx { Height = 14, VerticalAlignment = VerticalAlignment.Top };
            _progress.Seek += delegate (double ratio)
            {
                var handler = SeekRequested;
                if (handler != null && _progress.Duration > 0) handler(ratio * _progress.Duration);
            };
            Grid.SetColumn(_progress, 0);
            progressRow.Children.Add(_progress);

            // 时间显示在进度条右侧
            _time = new TextBlock
            {
                Text = "--:-- / --:--",
                FontSize = 11.5,
                Foreground = Theme.Brush("Brush.TextFaint"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 14, 0)
            };
            Grid.SetColumn(_time, 1);
            progressRow.Children.Add(_time);

            Grid.SetRow(progressRow, 0);
            Children.Add(progressRow);

            // ---- 控制行 ----
            // 左右两列等宽（1* / Auto / 1*），这样中间那一列永远在窗口正中，
            // 左边的歌名再长也不会把播放按钮挤偏。
            var row = new Grid { Margin = new Thickness(16, 4, 16, 10) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // 左：封面 + 曲目信息
            // 用 Grid（星号列）而不是 StackPanel：这样文本能拿到确定宽度，
            // TextTrimming 才会真的生效，也就不会把中间按钮挤偏。
            var left = new Grid { VerticalAlignment = VerticalAlignment.Center };
            left.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            left.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _cover = new Border
            {
                Width = 48,
                Height = 48,
                CornerRadius = new CornerRadius(6),
                Background = Theme.Brush("Brush.Raised"),
                Child = Icons.Create(Icons.Music, 20, null, Theme.Brush("Brush.TextFaint"), 1.4)
            };
            Grid.SetColumn(_cover, 0);
            left.Children.Add(_cover);

            var info = new Grid
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 12, 0)
            };
            info.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            info.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _title = new TextBlock
            {
                Text = "未播放",
                FontSize = 13,
                Foreground = Theme.Brush("Brush.Text"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap
            };
            _subtitle = new TextBlock
            {
                Text = "",
                FontSize = 11,
                Foreground = Theme.Brush("Brush.TextFaint"),
                Margin = new Thickness(0, 3, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap
            };
            Grid.SetRow(_title, 0);
            Grid.SetRow(_subtitle, 1);
            info.Children.Add(_title);
            info.Children.Add(_subtitle);

            Grid.SetColumn(info, 1);
            left.Children.Add(info);

            Grid.SetColumn(left, 0);
            row.Children.Add(left);

            // 中：播放控制
            var center = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            _btnMode = GhostButton(Icons.ModeSequential, "播放模式：顺序播放");
            _btnMode.Click += delegate { Raise(ModeClicked); };
            center.Children.Add(_btnMode);

            _btnPrev = GhostButton(Icons.Prev, "上一首");
            _btnPrev.Click += delegate { Raise(PrevClicked); };
            center.Children.Add(_btnPrev);

            _btnPlay = new Button
            {
                Style = (Style)FindResourceSafe("Btn.Round"),
                Content = Icons.Create(Icons.Play, 15, Brushes.White),
                Margin = new Thickness(10, 0, 10, 0)
            };
            _btnPlay.Click += delegate { Raise(PlayPauseClicked); };
            center.Children.Add(_btnPlay);

            _btnNext = GhostButton(Icons.Next, "下一首");
            _btnNext.Click += delegate { Raise(NextClicked); };
            center.Children.Add(_btnNext);

            Grid.SetColumn(center, 1);
            row.Children.Add(center);

            // 右：喇叭 + 音量 + 百分比（整组固定在最右侧）
            var right = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };

            _btnMute = GhostButton(Icons.VolumeHigh, "静音");
            _btnMute.Click += delegate { Raise(MuteToggled); };
            right.Children.Add(_btnMute);

            // 音量滑块紧挨喇叭图标，右侧标注当前百分比
            _volume = new SliderEx { Width = 90, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            _volume.ValueChanged += delegate (float v)
            {
                var handler = VolumeChanged;
                if (handler != null) handler(v);
            };
            right.Children.Add(_volume);

            _volumePercent = new TextBlock
            {
                Text = "80%",
                FontSize = 11,
                Foreground = Theme.Brush("Brush.TextFaint"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                MinWidth = 34
            };
            right.Children.Add(_volumePercent);

            Grid.SetColumn(right, 2);
            row.Children.Add(right);

            Grid.SetRow(row, 1);
            Children.Add(row);
        }

        private object FindResourceSafe(string key)
        {
            try
            {
                var app = Application.Current;
                if (app != null)
                {
                    var found = app.TryFindResource(key);
                    if (found != null) return found;
                }
            }
            catch { }
            return null;
        }

        private Button GhostButton(string icon, string tip)
        {
            var button = new Button
            {
                Style = (Style)FindResourceSafe("Btn.Ghost"),
                Content = Icons.Create(icon, 14, null, Theme.Brush("Brush.TextDim"), 1.5),
                ToolTip = tip
            };
            return button;
        }

        // ------------------------------------------------------------------

        public void SetPlaying(bool playing)
        {
            _isPlaying = playing;
            _btnPlay.Content = Icons.Create(playing ? Icons.Pause : Icons.Play, 15, Brushes.White);
        }

        public void SetMode(int mode)
        {
            _mode = mode;
            string icon;
            string name;

            switch (mode)
            {
                case 1: icon = Icons.ModeRepeatList; name = "列表循环"; break;
                case 2: icon = Icons.ModeRepeatOne; name = "单曲循环"; break;
                default: icon = Icons.ModeSequential; name = "顺序播放"; break;
            }

            _btnMode.Content = Icons.Create(icon, 14, null, Theme.Brush("Brush.TextDim"), 1.4);
            _btnMode.ToolTip = "播放模式：" + name;
        }

        public void SetVolume(float volume, bool muted)
        {
            _volume.SetValueSilent(volume);
            _btnMute.Content = Icons.Create(
                muted ? Icons.VolumeMute : Icons.VolumeHigh, 14,
                null, Theme.Brush(muted ? "Brush.Danger" : "Brush.TextDim"), 1.4);
            _btnMute.ToolTip = muted ? "取消静音" : "静音";
            _volumePercent.Text = Math.Round(volume * 100).ToString("0") + "%";
        }

        public void SetTimeline(double position, double duration, bool hasTrack)
        {
            _progress.SetState(position, duration, hasTrack);
            _time.Text = FormatTime(position) + " / " + FormatTime(duration);
        }

        public void SetTrack(Lumen.Models.Track track)
        {
            if (track == null)
            {
                _title.Text = "未播放";
                _subtitle.Text = "";
                return;
            }

            _title.Text = track.Title;
            _title.ToolTip = track.Title;

            var parts = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrEmpty(track.Artist)) parts.Add(track.Artist);
            if (!string.IsNullOrEmpty(track.Album)) parts.Add(track.Album);
            _subtitle.Text = string.Join(" · ", parts);
            _subtitle.ToolTip = _subtitle.Text;
        }

        private static string FormatTime(double seconds)
        {
            if (seconds <= 0 || double.IsNaN(seconds) || double.IsInfinity(seconds)) return "--:--";
            var t = TimeSpan.FromSeconds(seconds);
            return t.TotalHours >= 1
                ? string.Format("{0}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds)
                : string.Format("{0}:{1:00}", t.Minutes, t.Seconds);
        }

        private static void Raise(Action handler)
        {
            if (handler != null) handler();
        }
    }

    /// <summary>可点击跳转的进度条：细线 + 圆形滑块 + 悬停放大。</summary>
    public sealed class ProgressBarEx : Grid
    {
        private readonly Border _track;
        private readonly Border _fill;
        private readonly Ellipse _knob;
        private double _duration;
        private double _position;
        private bool _dragging;
        private bool _hasTrack;

        public event Action<double> Seek;

        public double Duration { get { return _duration; } }

        public ProgressBarEx()
        {
            Background = Brushes.Transparent;
            Cursor = Cursors.Hand;

            _track = new Border
            {
                Height = 3,
                CornerRadius = new CornerRadius(2),
                Background = Theme.Brush("Brush.Raised"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(16, 0, 16, 0)
            };
            Children.Add(_track);

            _fill = new Border
            {
                Height = 3,
                CornerRadius = new CornerRadius(2),
                Background = Theme.Brush("Brush.Accent"),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(16, 0, 0, 0),
                Width = 0
            };
            Children.Add(_fill);

            _knob = new Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = Theme.Brush("Brush.Accent"),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(11, 0, 0, 0),
                Visibility = Visibility.Collapsed
            };
            Children.Add(_knob);

            SizeChanged += delegate { UpdateVisual(); };
            MouseLeftButtonDown += OnDown;
            MouseMove += OnMove;
            MouseLeftButtonUp += OnUp;
            MouseEnter += delegate { if (_hasTrack) _knob.Visibility = Visibility.Visible; };
            MouseLeave += delegate { if (!_dragging) _knob.Visibility = Visibility.Collapsed; };
        }

        public void SetState(double position, double duration, bool hasTrack)
        {
            _hasTrack = hasTrack;
            _position = position;
            _duration = duration;
            if (!_dragging) UpdateVisual();
        }

        private void UpdateVisual()
        {
            double usable = Math.Max(0, ActualWidth - 32);
            double ratio = _duration > 0 ? Math.Max(0, Math.Min(1, _position / _duration)) : 0;

            _fill.Width = usable * ratio;
            _knob.Margin = new Thickness(16 + usable * ratio - 5, 0, 0, 0);
            _track.Background = _hasTrack ? Theme.Brush("Brush.Raised") : Theme.Brush("Brush.PanelAlt");
        }

        private void OnDown(object sender, MouseButtonEventArgs e)
        {
            if (!_hasTrack || _duration <= 0) return;
            _dragging = true;
            _knob.Visibility = Visibility.Visible;
            CaptureMouse();
            Apply(e.GetPosition(this).X, false);
        }

        private void OnMove(object sender, MouseEventArgs e)
        {
            if (_dragging) Apply(e.GetPosition(this).X, false);
        }

        private void OnUp(object sender, MouseButtonEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false;
            ReleaseMouseCapture();
            Apply(e.GetPosition(this).X, true);
        }

        private void Apply(double x, bool commit)
        {
            double usable = Math.Max(1, ActualWidth - 32);
            double ratio = Math.Max(0, Math.Min(1, (x - 16) / usable));

            _fill.Width = usable * ratio;
            _knob.Margin = new Thickness(16 + usable * ratio - 5, 0, 0, 0);

            if (commit)
            {
                var handler = Seek;
                if (handler != null) handler(ratio);
            }
        }
    }

    /// <summary>音量条：细轨道 + 圆形滑块。</summary>
    public sealed class SliderEx : Grid
    {
        private readonly Border _track;
        private readonly Border _fill;
        private readonly Ellipse _knob;
        private float _value = 0.8f;
        private bool _dragging;
        private bool _silent;

        public event Action<float> ValueChanged;

        public SliderEx()
        {
            Height = 18;
            Background = Brushes.Transparent;
            Cursor = Cursors.Hand;

            _track = new Border
            {
                Height = 3,
                CornerRadius = new CornerRadius(2),
                Background = Theme.Brush("Brush.Raised"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Children.Add(_track);

            _fill = new Border
            {
                Height = 3,
                CornerRadius = new CornerRadius(2),
                Background = Theme.Brush("Brush.TextDim"),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };
            Children.Add(_fill);

            _knob = new Ellipse
            {
                Width = 9,
                Height = 9,
                Fill = Theme.Brush("Brush.Text"),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };
            Children.Add(_knob);

            SizeChanged += delegate { UpdateVisual(); };
            MouseLeftButtonDown += OnDown;
            MouseMove += OnMove;
            MouseLeftButtonUp += OnUp;
        }

        public void SetValueSilent(float value)
        {
            _silent = true;
            _value = Math.Max(0f, Math.Min(1f, value));
            UpdateVisual();
            _silent = false;
        }

        private void UpdateVisual()
        {
            double usable = Math.Max(0, ActualWidth - 10);
            _fill.Width = usable * _value;
            _knob.Margin = new Thickness(usable * _value, 0, 0, 0);
        }

        private void OnDown(object sender, MouseButtonEventArgs e)
        {
            _dragging = true;
            CaptureMouse();
            Apply(e.GetPosition(this).X, false);
        }

        private void OnMove(object sender, MouseEventArgs e)
        {
            if (_dragging) Apply(e.GetPosition(this).X, false);
        }

        private void OnUp(object sender, MouseButtonEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false;
            ReleaseMouseCapture();
            Apply(e.GetPosition(this).X, true);
        }

        private void Apply(double x, bool commit)
        {
            double usable = Math.Max(1, ActualWidth - 10);
            _value = (float)Math.Max(0, Math.Min(1, (x - 5) / usable));
            UpdateVisual();

            if (commit && !_silent)
            {
                var handler = ValueChanged;
                if (handler != null) handler(_value);
            }
        }
    }
}
