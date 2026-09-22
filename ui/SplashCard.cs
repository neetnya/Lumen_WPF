using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Lumen.UI
{
    /// <summary>
    /// 启动提示卡片（对应 README：「窗口出来之前会先显示一张深色小卡片」）。
    /// 图标 + 「正在读取音乐库…」，随后自动收起。
    /// </summary>
    public sealed class SplashCard : Window
    {
        private readonly TextBlock _status;
        private bool _closed;

        public SplashCard()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            Width = 330;
            Height = 116;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = new FontFamily("Microsoft YaHei UI, Microsoft YaHei, Segoe UI");
            FontSize = 12.5;
            SnapsToDevicePixels = true;

            var frame = new Border
            {
                Background = Theme.Brush("Brush.Raised"),
                BorderBrush = Theme.Brush("Brush.WindowBorder"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 22,
                    ShadowDepth = 3,
                    Opacity = 0.45,
                    Color = Colors.Black
                }
            };

            var grid = new Grid { Margin = new Thickness(20, 0, 20, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var icon = BuildIcon();
            Grid.SetColumn(icon, 0);
            grid.Children.Add(icon);

            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
            texts.Children.Add(new TextBlock
            {
                Text = "Lumen 音乐",
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = Theme.Brush("Brush.Text")
            });
            _status = new TextBlock
            {
                Text = "正在启动…",
                FontSize = 11.5,
                Margin = new Thickness(0, 5, 0, 0),
                Foreground = Theme.Brush("Brush.TextDim")
            };
            texts.Children.Add(_status);
            Grid.SetColumn(texts, 1);
            grid.Children.Add(texts);

            frame.Child = grid;
            Content = frame;

            Opacity = 0;
            Loaded += delegate
            {
                var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160));
                BeginAnimation(OpacityProperty, fade);
            };
        }

        /// <summary>绘制应用图标（与 assets\lumen.ico 同款音符）。</summary>
        private static FrameworkElement BuildIcon()
        {
            var canvas = new Canvas { Width = 40, Height = 40 };

            var accent = new SolidColorBrush(Color.FromRgb(0x4F, 0xA3, 0xFF));
            var accentLight = new SolidColorBrush(Color.FromRgb(0x7E, 0xBE, 0xFF));

            var bg = new System.Windows.Shapes.Rectangle
            {
                Width = 40,
                Height = 40,
                RadiusX = 9,
                RadiusY = 9,
                Fill = new LinearGradientBrush(
                    Color.FromRgb(0x22, 0x26, 0x2D),
                    Color.FromRgb(0x12, 0x14, 0x17),
                    new Point(0, 0), new Point(1, 1))
            };
            canvas.Children.Add(bg);

            // 符干
            var stem1 = new System.Windows.Shapes.Rectangle { Width = 2.4, Height = 17, Fill = accent };
            Canvas.SetLeft(stem1, 15);
            Canvas.SetTop(stem1, 9);
            canvas.Children.Add(stem1);

            var stem2 = new System.Windows.Shapes.Rectangle { Width = 2.4, Height = 17, Fill = accentLight };
            Canvas.SetLeft(stem2, 27);
            Canvas.SetTop(stem2, 9);
            canvas.Children.Add(stem2);

            // 横梁
            var beam = new System.Windows.Shapes.Polygon
            {
                Fill = accentLight,
                Points = new PointCollection
                {
                    new Point(14, 9), new Point(29, 9), new Point(29, 14), new Point(14, 16)
                }
            };
            canvas.Children.Add(beam);

            // 符头
            var head1 = new System.Windows.Shapes.Ellipse { Width = 11, Height = 8, Fill = accent };
            Canvas.SetLeft(head1, 9);
            Canvas.SetTop(head1, 24);
            canvas.Children.Add(head1);

            var head2 = new System.Windows.Shapes.Ellipse { Width = 11, Height = 8, Fill = accentLight };
            Canvas.SetLeft(head2, 21);
            Canvas.SetTop(head2, 21);
            canvas.Children.Add(head2);

            return canvas;
        }

        public void SetStatus(string text)
        {
            if (_closed) return;
            try
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    try { _status.Text = text; } catch { }
                }));
            }
            catch { }
        }

        /// <summary>收起卡片（淡出后关闭）。</summary>
        public void Dismiss()
        {
            if (_closed) return;
            _closed = true;

            try
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    try
                    {
                        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(140));
                        fade.Completed += delegate { try { Close(); } catch { } };
                        BeginAnimation(OpacityProperty, fade);
                    }
                    catch { try { Close(); } catch { } }
                }));
            }
            catch { }
        }
    }
}
