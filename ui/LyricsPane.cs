using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Lumen.Lyrics;

namespace Lumen.UI
{
    /// <summary>
    /// 歌词区：
    /// 当前行高亮放大、自动居中滚动、相邻两句微微提亮、点击任意一句跳转播放。
    /// 纯文本歌词也能滚动查看。
    /// </summary>
    public sealed class LyricsPane : Grid
    {
        private readonly ScrollViewer _scroll;
        private readonly StackPanel _stack;
        private readonly TextBlock _empty;
        private readonly List<TextBlock> _lines = new List<TextBlock>();

        private LyricsDocument _doc;
        private int _current = -2;
        private bool _userScrolling;
        private DateTime _lastAutoScroll = DateTime.MinValue;

        /// <summary>点击某句歌词（参数为秒）。</summary>
        public event Action<double> LineClicked;

        public LyricsPane()
        {
            Background = Theme.Brush("Brush.Panel");
            ClipToBounds = true;

            _stack = new StackPanel
            {
                Margin = new Thickness(28, 24, 28, 60),
                VerticalAlignment = VerticalAlignment.Center
            };

            _scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _stack,
                CanContentScroll = false
            };
            _scroll.ScrollChanged += OnScrollChanged;
            _scroll.PreviewMouseWheel += OnMouseWheel;
            Children.Add(_scroll);

            _empty = new TextBlock
            {
                Text = "暂无歌词",
                Foreground = Theme.Brush("Brush.TextFaint"),
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Children.Add(_empty);
        }

        // ------------------------------------------------------------------

        public void SetLyrics(LyricsDocument doc)
        {
            _doc = doc;
            _lines.Clear();
            _stack.Children.Clear();
            _current = -2;

            if (doc == null || doc.IsEmpty)
            {
                _empty.Visibility = Visibility.Visible;
                _scroll.Visibility = Visibility.Collapsed;
                return;
            }

            _empty.Visibility = Visibility.Collapsed;
            _scroll.Visibility = Visibility.Visible;

            for (int i = 0; i < doc.Lines.Count; i++)
            {
                var line = doc.Lines[i];
                var block = new TextBlock
                {
                    Text = line.Text,
                    TextWrapping = TextWrapping.Wrap,
                    // 文字水平居中
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    FontSize = 15,
                    LineHeight = 24,
                    Margin = new Thickness(0, 7, 0, 7),
                    Foreground = Theme.Brush("Brush.TextDim"),
                    Cursor = Cursors.Hand,
                    Tag = i
                };
                block.MouseLeftButtonUp += OnLineClick;
                block.MouseEnter += OnLineEnter;
                block.MouseLeave += OnLineLeave;

                _lines.Add(block);
                _stack.Children.Add(block);
            }

            if (!doc.IsSynced)
            {
                // 纯文本：不做同步，顶到最上面
                _scroll.ScrollToVerticalOffset(0);
            }
        }

        private void OnLineClick(object sender, MouseButtonEventArgs e)
        {
            var block = sender as TextBlock;
            if (block == null || _doc == null || !_doc.IsSynced) return;

            int index = (int)block.Tag;
            if (index < 0 || index >= _doc.Lines.Count) return;

            var handler = LineClicked;
            if (handler != null) handler(_doc.Lines[index].Time);
        }

        private void OnLineEnter(object sender, MouseEventArgs e)
        {
            var block = sender as TextBlock;
            if (block == null) return;
            int index = (int)block.Tag;
            if (index == _current) return;
            block.Foreground = Theme.Brush("Brush.Text");
        }

        private void OnLineLeave(object sender, MouseEventArgs e)
        {
            var block = sender as TextBlock;
            if (block == null) return;
            int index = (int)block.Tag;
            if (index == _current) return;
            block.Foreground = Theme.Brush("Brush.TextDim");
        }

        private void OnMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // 用户手动滚动后，暂停自动居中几秒
            _userScrolling = true;
            _lastAutoScroll = DateTime.UtcNow;
        }

        private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.ExtentHeightChange == 0 && e.VerticalChange != 0)
            {
                _lastAutoScroll = DateTime.UtcNow;
            }
        }

        // ------------------------------------------------------------------

        /// <summary>随播放进度更新高亮行（由主窗口定时调用）。</summary>
        public void UpdatePosition(double seconds, bool isSynced)
        {
            if (_doc == null || _doc.IsEmpty) return;

            if (!_doc.IsSynced)
            {
                // 纯文本：按播放比例缓慢滚动，便于跟读
                return;
            }

            int index = _doc.IndexAt(seconds);
            if (index == _current) return;
            _current = index;

            ApplyHighlight(index);
            if (!_userScrolling || (DateTime.UtcNow - _lastAutoScroll).TotalSeconds > 3)
            {
                _userScrolling = false;
                CenterLine(index);
            }
        }

        private void ApplyHighlight(int index)
        {
            for (int i = 0; i < _lines.Count; i++)
            {
                var block = _lines[i];

                if (i == index)
                {
                    // 当前行高亮放大
                    block.FontSize = 19;
                    block.FontWeight = FontWeights.SemiBold;
                    block.Foreground = Theme.Brush("Brush.Accent");
                }
                else
                {
                    // 其余行统一亮度，不做渐变淡出
                    block.FontSize = 15;
                    block.FontWeight = FontWeights.Normal;
                    block.Foreground = Theme.Brush("Brush.TextDim");
                }

                block.Opacity = 1.0;
            }
        }

        private void CenterLine(int index)
        {
            if (index < 0 || index >= _lines.Count) return;

            var block = _lines[index];
            try
            {
                // 让当前行居中：目标偏移 = 行中心 - 视口一半
                var transform = block.TransformToAncestor(_stack);
                var pos = transform.Transform(new Point(0, 0));
                double lineCenter = pos.Y + block.ActualHeight / 2;
                double target = lineCenter - _scroll.ViewportHeight / 2 + _stack.Margin.Top;

                target = Math.Max(0, Math.Min(target, _scroll.ScrollableHeight));

                var animation = new DoubleAnimation(target, TimeSpan.FromMilliseconds(260))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                _scroll.BeginAnimation(ScrollViewer.VerticalOffsetProperty, null);
                _scroll.ScrollToVerticalOffset(target);
            }
            catch { }
        }

        /// <summary>清空高亮（停止播放时）。</summary>
        public void ClearHighlight()
        {
            _current = -2;
            ApplyHighlight(-1);
        }
    }
}
