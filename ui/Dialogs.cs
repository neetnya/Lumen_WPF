using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Lumen.UI
{
    /// <summary>
    /// 自绘的深色对话框，
    /// 替代系统 MessageBox，保持视觉统一。
    /// </summary>
    public static class Dialogs
    {
        public enum Kind { Info, Warning, Error, Question }

        public static void ShowInfo(Window owner, string title, string message)
        {
            Show(owner, title, message, Kind.Info, false);
        }

        public static void ShowError(Window owner, string title, string message)
        {
            Show(owner, title, message, Kind.Error, false);
        }

        public static void ShowWarning(Window owner, string title, string message)
        {
            Show(owner, title, message, Kind.Warning, false);
        }

        /// <summary>确认框，返回是否点了确定。</summary>
        public static bool Confirm(Window owner, string title, string message, string okText = "确定")
        {
            return Show(owner, title, message, Kind.Question, true, okText);
        }

        /// <summary>文本输入框（新建/重命名分组用），取消返回 null。</summary>
        public static string Prompt(Window owner, string title, string message, string initial)
        {
            var window = CreateWindow(owner, title, 380, 210);
            string result = null;

            var root = new StackPanel { Margin = new Thickness(22, 18, 22, 16) };
            root.Children.Add(new TextBlock
            {
                Text = message,
                Foreground = Theme.Brush("Brush.TextDim"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            });

            var box = new TextBox
            {
                Text = initial ?? "",
                Height = 30,
                Padding = new Thickness(8, 0, 8, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = Theme.Brush("Brush.Raised"),
                Foreground = Theme.Brush("Brush.Text"),
                CaretBrush = Theme.Brush("Brush.Text"),
                BorderThickness = new Thickness(0),
                FontSize = 13
            };
            root.Children.Add(box);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 18, 0, 0)
            };
            var cancel = MakeButton("取消", false);
            var ok = MakeButton("确定", true);
            cancel.Click += delegate { window.DialogResult = false; };
            ok.Click += delegate { result = box.Text; window.DialogResult = true; };
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            root.Children.Add(buttons);

            window.Loaded += delegate
            {
                box.Focus();
                box.SelectAll();
                if (Application.Current != null)
                {
                    var ignored = Application.Current.Dispatcher.BeginInvoke(
                        new Action(delegate { box.Focus(); box.SelectAll(); }),
                        System.Windows.Threading.DispatcherPriority.Input);
                }
            };

            WrapContent(window, root);

            bool? dialogResult = window.ShowDialog();
            return dialogResult == true ? result : null;
        }

        private static bool Show(Window owner, string title, string message, Kind kind, bool confirm, string okText = "确定")
        {
            var window = CreateWindow(owner, title, 400, confirm ? 200 : 190);
            bool accepted = false;

            var root = new Grid { Margin = new Thickness(22, 20, 22, 16) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // 标题行：图标 + 标题
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(BuildIcon(kind));
            head.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = Theme.Brush("Brush.Text"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0)
            });
            Grid.SetRow(head, 0);
            root.Children.Add(head);

            var body = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 12, 0, 0),
                MaxHeight = 260
            };
            body.Content = new TextBlock
            {
                Text = message ?? "",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Theme.Brush("Brush.TextDim"),
                LineHeight = 20
            };
            Grid.SetRow(body, 1);
            root.Children.Add(body);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };
            if (confirm)
            {
                var cancel = MakeButton("取消", false);
                cancel.Click += delegate { window.DialogResult = false; };
                buttons.Children.Add(cancel);
            }
            var ok = MakeButton(confirm ? okText : "知道了", true);
            ok.Click += delegate { accepted = true; window.DialogResult = true; };
            buttons.Children.Add(ok);
            Grid.SetRow(buttons, 2);
            root.Children.Add(buttons);

            window.Loaded += delegate { ok.Focus(); };

            WrapContent(window, root);

            window.ShowDialog();
            return confirm ? accepted : true;
        }

        private static FrameworkElement BuildIcon(Kind kind)
        {
            Color color;
            string glyph;

            switch (kind)
            {
                case Kind.Error:
                    color = Color.FromRgb(0xE0, 0x55, 0x5A);
                    glyph = "M9,3 L15,15 H3 Z M9,7 V11 M9,12.6 V13.4";
                    break;
                case Kind.Warning:
                    color = Color.FromRgb(0xE8, 0xA5, 0x3C);
                    glyph = "M9,3 L15,15 H3 Z M9,7 V11 M9,12.6 V13.4";
                    break;
                case Kind.Question:
                    color = Color.FromRgb(0x4F, 0xA3, 0xFF);
                    glyph = "M9,2 A7,7 0 1 0 9.01,2 M7,7 A2,2 0 1 1 9,10 V11 M9,13 V13.6";
                    break;
                default:
                    color = Color.FromRgb(0x4F, 0xA3, 0xFF);
                    glyph = "M9,2 A7,7 0 1 0 9.01,2 M9,8 V13 M9,5 V5.6";
                    break;
            }

            var brush = new SolidColorBrush(color);
            return new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(glyph),
                Stroke = brush,
                StrokeThickness = 1.7,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Width = 18,
                Height = 18,
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private static Button MakeButton(string text, bool primary)
        {
            var button = new Button
            {
                Content = text,
                MinWidth = 76,
                Height = 30,
                Margin = new Thickness(8, 0, 0, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Focusable = true,
                Foreground = primary ? Brushes.White : Theme.Brush("Brush.Text"),
                Background = primary ? Theme.Brush("Brush.Accent") : Theme.Brush("Brush.Raised"),
                BorderThickness = new Thickness(0),
                Template = BuildButtonTemplate(primary)
            };
            return button;
        }

        private static ControlTemplate BuildButtonTemplate(bool primary)
        {
            var template = new ControlTemplate(typeof(Button));

            var border = new FrameworkElementFactory(typeof(Border));
            // 必须给名字：下面的 Trigger 要通过子节点标识符定位它。
            // 用 Setter(dp, value, "") 会抛
            // 「用于 Target 属性的子节点标识符不能是空字符串」。
            border.Name = "bd";
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            border.SetValue(Border.PaddingProperty, new Thickness(14, 0, 14, 0));

            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter);

            template.VisualTree = border;

            var hover = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty,
                primary ? Theme.Brush("Brush.AccentHover") : Theme.Brush("Brush.Hover"), "bd"));
            template.Triggers.Add(hover);

            return template;
        }

        private static Window CreateWindow(Window owner, string title, double width, double height)
        {
            var window = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                Width = width,
                Height = height,
                WindowStartupLocation = owner != null
                    ? WindowStartupLocation.CenterOwner
                    : WindowStartupLocation.CenterScreen,
                Owner = owner,
                Title = title,
                FontFamily = new FontFamily("Microsoft YaHei UI, Microsoft YaHei, Segoe UI"),
                FontSize = 12.5,
                SnapsToDevicePixels = true
            };

            // 用 Content 外套圆角边框；这里先留空，调用方设置 Content 前包一层
            var frame = new Border
            {
                Background = Theme.Brush("Brush.Raised"),
                BorderBrush = Theme.Brush("Brush.WindowBorder"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8)
            };
            window.Tag = frame;

            // 让外部设置 Content 时自动进到 frame 里
            return window;
        }

        /// <summary>
        /// 把内容装进圆角边框。
        ///
        /// 注意：只能在**尚未设置** Window.Content 时调用。
        /// 先 window.Content = root 再 frame.Child = root 会抛异常
        /// （同一个 UIElement 不能有两个父级），导致对话框直接弹不出来 ——
        /// 「导入文件夹没有任何反应」就是这个原因。
        /// </summary>
        private static void WrapContent(Window window, object content)
        {
            var frame = window.Tag as Border;
            if (frame != null)
            {
                frame.Child = content as UIElement;
                window.Content = frame;
            }
            else
            {
                window.Content = content;
            }
        }
    }
}
