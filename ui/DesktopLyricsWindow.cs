using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Lumen.Lyrics;
using Lumen.Native;

namespace Lumen.UI
{
    /// <summary>
    /// 桌面歌词。
    /// - 独立置顶歌词条，白字黑描边，无卡拉 OK 逐字变色
    /// - 宽度固定为屏幕可用宽度的 60%，水平居中，不可拖宽
    /// - 字号固定，只跟字号倍数有关，与句子长短无关
    /// - 切句瞬间生效；只有超长句才横向滚动（跑马灯），滚完回到句首
    /// - 没有歌词时就是一条空条，不显示任何占位文字
    /// - 解锁：拖动移动、拖上下边缘改高度、双击锁定；锁定：鼠标完全穿透
    /// </summary>
    public sealed class DesktopLyricsWindow : Window
    {
        private const double WidthRatio = 0.60;
        private const double MinBarHeight = 46;
        private const double MaxBarHeight = 320;
        private const double BaseFontSize = 30;

        /// <summary>默认位置：屏幕上方，距工作区顶部留一点边距。</summary>
        private const double DefaultTopGap = 60;

        private readonly Canvas _canvas;
        private readonly OutlinedTextBlock _text;

        private LyricsDocument _doc;
        private int _currentIndex = -1;
        private bool _locked;
        private double _fontScale = 1.0;

        private readonly DispatcherTimer _ticker;
        private readonly DispatcherTimer _marquee;

        private double _marqueeOffset;
        private double _marqueeTarget;
        private bool _needsMarquee;
        private DateTime _marqueePauseUntil = DateTime.MinValue;

        private double _dragStartY;
        private double _dragStartHeight;
        private bool _resizingHeight;
        private bool _draggingWindow;

        public event Action<double, double, double> PositionChanged;
        public event Action<bool> LockChanged;
        public event Action<double> FontScaleChanged;

        /// <summary>当前播放位置由主窗口推入。</summary>
        public event Action<double> RequestPosition;

        public DesktopLyricsWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            Height = 92;
            FontFamily = new FontFamily("Microsoft YaHei");
            SnapsToDevicePixels = true;
            ShowActivated = false;

            _text = new OutlinedTextBlock
            {
                FontFamily = new FontFamily("Microsoft YaHei"),
                FontSize = BaseFontSize,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                // 白字黑描边（均匀居中描边）
                Outline = Brushes.Black,
                OutlineThickness = 2,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            _canvas = new Canvas { ClipToBounds = true };
            _canvas.Children.Add(_text);

            // 背景框彻底去掉：Canvas 直接作为窗口内容，
            // 桌面上只浮着歌词文字，不再有任何底板/边框。
            Content = _canvas;

            _ticker = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _ticker.Tick += delegate
            {
                var handler = RequestPosition;
                if (handler != null) handler(0);
            };

            _marquee = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _marquee.Tick += OnMarqueeTick;

            MouseLeftButtonDown += OnMouseDown;
            MouseMove += OnMouseMove;
            MouseLeftButtonUp += OnMouseUp;
            MouseRightButtonUp += OnRightClick;

            Loaded += delegate
            {
                ApplyPosition();
                ApplyFontSize();
            };

            SizeChanged += delegate { LayoutText(); };
        }

        // ------------------------------------------------------------------
        // 配置
        // ------------------------------------------------------------------

        public void ApplyConfig(Config config)
        {
            if (config == null) return;

            _fontScale = config.DesktopLyricsFontScale <= 0 ? 1.0 : config.DesktopLyricsFontScale;
            if (config.DesktopLyricsHeight >= MinBarHeight) Height = config.DesktopLyricsHeight;

            ApplyPosition();
            ApplyFontSize();

            if (config.DesktopLyricsLocked != _locked) SetLocked(config.DesktopLyricsLocked);
        }

        private void ApplyPosition()
        {
            // 宽度固定为屏幕可用宽度的 60%，水平居中，不可拖宽
            var area = GetWorkAreaSafe();
            double screenWidth = area.right - area.left;
            double screenHeight = area.bottom - area.top;
            if (screenWidth < 100) screenWidth = 1920;
            if (screenHeight < 100) screenHeight = 1080;

            Width = Math.Max(320, screenWidth * WidthRatio);

            // 默认出现在屏幕上方（工作区顶部往下留一点边距）
            double defaultLeft = area.left + (screenWidth - Width) / 2;
            double defaultTop = area.top + DefaultTopGap;

            Left = double.IsNaN(_desktopLeft) ? defaultLeft : _desktopLeft;
            Top = double.IsNaN(_desktopTop) ? defaultTop : _desktopTop;

            Log.Info(string.Format(
                "桌面歌词: 位置 {0:0},{1:0} 尺寸 {2:0}x{3:0} 工作区 {4},{5}-{6},{7}",
                Left, Top, Width, Height, area.left, area.top, area.right, area.bottom));
        }

        /// <summary>取工作区；窗口句柄还没建好时用主显示器的工作区。</summary>
        private Native.Win32.RECT GetWorkAreaSafe()
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            return Native.Win32.GetWorkArea(handle);
        }

        private double _desktopLeft = double.NaN;
        private double _desktopTop = double.NaN;

        public double SavedLeft { get { return Left; } }
        public double SavedTop { get { return Top; } }

        public void SetSavedPosition(double left, double top)
        {
            if (!double.IsNaN(left)) _desktopLeft = left;
            if (!double.IsNaN(top)) _desktopTop = top;
        }

        private void ApplyFontSize()
        {
            // 字号固定：只跟窗口大小 / 字号倍数有关，和句子长短无关
            double screenFactor = Math.Max(0.6, Height / 92.0);
            _text.FontSize = BaseFontSize * _fontScale * screenFactor;
            LayoutText();
        }

        // ------------------------------------------------------------------
        // 歌词
        // ------------------------------------------------------------------

        public void SetLyrics(LyricsDocument doc)
        {
            _doc = doc;
            _currentIndex = -1;
            _marqueeOffset = 0;
            _needsMarquee = false;

            if (doc == null || doc.IsEmpty)
            {
                // 没有歌词时就是一条空条，不显示任何占位文字
                _text.Text = string.Empty;
                LayoutText();
                return;
            }

            UpdatePosition(0);
        }

        /// <summary>更新当前行（切句瞬间生效，不做垂直滚动动画）。</summary>
        public void UpdatePosition(double seconds)
        {
            if (_doc == null || _doc.IsEmpty) return;
            if (!_doc.IsSynced)
            {
                if (_currentIndex != 0)
                {
                    _currentIndex = 0;
                    ShowLine(_doc.Lines[0].Text);
                }
                return;
            }

            int index = _doc.IndexAt(seconds);
            if (index < 0) index = 0;
            if (index == _currentIndex) return;

            _currentIndex = index;
            ShowLine(_doc.Lines[index].Text);
        }

        private void ShowLine(string text)
        {
            _text.Text = text ?? string.Empty;

            // 切句瞬间生效，重置跑马灯
            _marqueeOffset = 0;
            _marqueePauseUntil = DateTime.UtcNow.AddSeconds(1.2);
            LayoutText();
        }

        private void LayoutText()
        {
            double available = Math.Max(0, _canvas.ActualWidth - 24);
            _text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double textWidth = _text.DesiredSize.Width;
            double textHeight = _text.DesiredSize.Height;

            // 只有超长句才横向滚动
            _needsMarquee = textWidth > available && available > 0;
            _marqueeTarget = _needsMarquee ? textWidth - available + 24 : 0;

            if (_needsMarquee)
            {
                if (!_marquee.IsEnabled) _marquee.Start();
                Canvas.SetLeft(_text, 12);
            }
            else
            {
                _marquee.Stop();
                _marqueeOffset = 0;
                // 不超长时水平居中
                Canvas.SetLeft(_text, Math.Max(12, (_canvas.ActualWidth - textWidth) / 2));
            }

            // 垂直居中
            Canvas.SetTop(_text, Math.Max(0, (_canvas.ActualHeight - textHeight) / 2));
        }

        private void OnMarqueeTick(object sender, EventArgs e)
        {
            if (!_needsMarquee) { _marquee.Stop(); return; }
            if (DateTime.UtcNow < _marqueePauseUntil) return;

            _marqueeOffset += 0.9;
            if (_marqueeOffset >= _marqueeTarget)
            {
                // 滚完回到句首
                _marqueeOffset = 0;
                _marqueePauseUntil = DateTime.UtcNow.AddSeconds(1.6);
            }
            Canvas.SetLeft(_text, 12 - _marqueeOffset);
        }

        // ------------------------------------------------------------------
        // 锁定 / 穿透
        // ------------------------------------------------------------------

        public bool IsLocked { get { return _locked; } }

        public void SetLocked(bool locked)
        {
            _locked = locked;
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero)
            {
                Win32.SetClickThrough(handle, locked);
                Win32.SetTopMost(handle);
                Win32.SetToolWindow(handle);
            }

            var handler = LockChanged;
            if (handler != null) handler(locked);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero)
            {
                Win32.SetTopMost(handle);
                Win32.SetToolWindow(handle);
                Win32.SetClickThrough(handle, _locked);
            }
            ApplyPosition();
            ApplyFontSize();
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            // 保持置顶
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero && !_locked) Win32.SetTopMost(handle);
        }

        // ------------------------------------------------------------------
        // 鼠标：拖动移动 / 拖上下边缘改高度 / 双击锁定
        // ------------------------------------------------------------------

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_locked) return;

            if (e.ClickCount == 2)
            {
                SetLocked(true);
                e.Handled = true;
                return;
            }

            var pos = e.GetPosition(this);
            // 上下边缘 6px 内视为改高度
            _resizingHeight = pos.Y < 6 || pos.Y > ActualHeight - 6;

            if (_resizingHeight)
            {
                _dragStartY = PointToScreen(pos).Y;
                _dragStartHeight = ActualHeight;
            }
            else
            {
                // 自己跟踪拖动，不用 DragMove()：
                // 词条带 WS_EX_NOACTIVATE，DragMove 依赖窗口激活，会静默失效，
                // 表现就是「拖不动」。
                _draggingWindow = true;
                _dragStartScreen = PointToScreen(pos);
                _dragStartLeft = Left;
                _dragStartTop = Top;
            }

            CaptureMouse();
            e.Handled = true;
        }

        private Point _dragStartScreen;
        private double _dragStartLeft;
        private double _dragStartTop;

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (_locked) return;

            if (_resizingHeight && e.LeftButton == MouseButtonState.Pressed)
            {
                double current = PointToScreen(e.GetPosition(this)).Y;
                double delta = current - _dragStartY;
                double newHeight = Math.Max(MinBarHeight, Math.Min(MaxBarHeight, _dragStartHeight + delta));
                Height = newHeight;
                ApplyFontSize();
                LayoutText();
                return;
            }

            if (_draggingWindow && e.LeftButton == MouseButtonState.Pressed)
            {
                // 手动跟随鼠标移动（换算成 DPI 无关单位）
                // 不做任何位置吸附：拖到哪就是哪
                var now = PointToScreen(e.GetPosition(this));
                double scale = DpiScale();
                Left = _dragStartLeft + (now.X - _dragStartScreen.X) / scale;
                Top = _dragStartTop + (now.Y - _dragStartScreen.Y) / scale;
            }
        }

        private double DpiScale()
        {
            var source = PresentationSource.FromVisual(this);
            if (source != null && source.CompositionTarget != null)
                return source.CompositionTarget.TransformToDevice.M11;
            return 1.0;
        }

        private void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (CaptureMouse())
            {
                ReleaseMouseCapture();
            }
            _resizingHeight = false;
            _draggingWindow = false;

            var handler = PositionChanged;
            if (handler != null) handler(Left, Top, Height);
        }

        private void OnRightClick(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            ShowMenu();
        }

        /// <summary>右键菜单任何时候都能用（会自动激活词条）。</summary>
        public void ShowMenu()
        {
            var menu = new ContextMenu();

            var fontMenu = new MenuItem { Header = "字体大小" };
            fontMenu.Items.Add(FontItem("稍大一点", 1.1));
            fontMenu.Items.Add(FontItem("稍小一点", 1 / 1.1));
            fontMenu.Items.Add(new Separator());
            fontMenu.Items.Add(FontItem("大很多", 1.35));
            fontMenu.Items.Add(FontItem("小很多", 1 / 1.35));
            fontMenu.Items.Add(new Separator());

            var custom = new MenuItem { Header = "输入百分比…" };
            custom.Click += delegate
            {
                var value = Dialogs.Prompt(this, "字体大小", "输入百分比（50 ~ 300）：",
                    Math.Round(_fontScale * 100).ToString("0"));
                if (string.IsNullOrWhiteSpace(value)) return;

                double percent;
                if (!double.TryParse(value.Trim().TrimEnd('%'), out percent)) return;
                percent = Math.Max(50, Math.Min(300, percent));
                SetFontScale(percent / 100.0);
            };
            fontMenu.Items.Add(custom);
            menu.Items.Add(fontMenu);

            menu.Items.Add(new Separator());

            var lockItem = new MenuItem { Header = _locked ? "解锁" : "锁定" };
            lockItem.Click += delegate { SetLocked(!_locked); };
            menu.Items.Add(lockItem);

            menu.Items.Add(new Separator());

            var hide = new MenuItem { Header = "隐藏桌面歌词" };
            hide.Click += delegate { Hide(); };
            menu.Items.Add(hide);

            menu.PlacementTarget = this;
            menu.IsOpen = true;
        }

        private MenuItem FontItem(string header, double factor)
        {
            var item = new MenuItem { Header = header };
            item.Click += delegate { SetFontScale(_fontScale * factor); };
            return item;
        }

        private void SetFontScale(double scale)
        {
            _fontScale = Math.Max(0.5, Math.Min(3.0, scale));
            ApplyFontSize();

            var handler = FontScaleChanged;
            if (handler != null) handler(_fontScale);
        }

        /// <summary>主窗口定时调用：刷新当前句。</summary>
        public void SetTickerEnabled(bool enabled)
        {
            if (enabled) { if (!_ticker.IsEnabled) _ticker.Start(); }
            else _ticker.Stop();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _ticker.Stop();
            _marquee.Stop();
        }
    }
}
