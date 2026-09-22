using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Lumen.Models;

namespace Lumen.UI
{
    /// <summary>
    /// 列表区。
    ///
    /// 性能要点（修复「导入 600 首卡好几秒」和「切歌卡 1~2 秒」）：
    ///   1. 用 ListBox + VirtualizingStackPanel，只为**可见行**创建可视化元素。
    ///      之前是往 StackPanel 里 add 600 个 Border，一次导入要建几千个控件。
    ///   2. 行模板用数据绑定，滚动时容器复用（Recycling）。
    ///   3. 切歌只改 IsPlaying 标记，不重建列表。
    /// </summary>
    public sealed class TrackListPane : Grid
    {
        private readonly TextBox _search;
        private readonly Button _groupButton;
        private readonly Button _sortButton;
        private readonly Button _moreButton;
        private readonly Button _locateButton;
        private readonly ListBox _list;
        private readonly TextBlock _empty;
        private readonly Border _statusBar;
        private readonly TextBlock _statusText;

        private readonly ObservableCollection<RowItem> _rows = new ObservableCollection<RowItem>();
        private readonly DispatcherTimer _searchDebounce;
        private readonly DispatcherTimer _statusTimer;

        private List<Track> _tracks = new List<Track>();
        private List<Group> _groups = new List<Group>();
        private string _currentGroupId;
        private int _playingIndex = -1;
        private double _numberWidth = 32;

        public event Action<Track> TrackActivated;
        public event Action<Track> PlayRequested;
        public event Action<Track> RemoveRequested;
        public event Action<Track> RevealRequested;
        public event Action<string> SortChanged;
        public event Action ShuffleRequested;
        public event Action ImportFilesRequested;
        public event Action ImportFolderRequested;
        public event Action NewGroupRequested;
        public event Action RenameGroupRequested;
        public event Action DeleteGroupRequested;
        public event Action<string> GroupSelected;
        public event Action<IList<string>> FilesDropped;

        public TrackListPane()
        {
            Background = Theme.Brush("Brush.Window");
            AllowDrop = true;
            Drop += OnDrop;
            DragOver += OnDragOver;

            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // ---- 顶部一行：全部控件 ----
            var top = new Grid { Margin = new Thickness(10, 10, 10, 8) };
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _groupButton = new Button
            {
                Style = (Style)FindResourceSafe("Btn.Group"),
                Height = 28
            };
            _groupButton.Click += delegate { ShowGroupMenu(); };
            Grid.SetColumn(_groupButton, 0);
            top.Children.Add(_groupButton);

            var searchHost = new Grid { Margin = new Thickness(8, 0, 8, 0) };
            _search = new TextBox
            {
                Style = (Style)FindResourceSafe("Box.Search"),
                ToolTip = "搜索标题 / 艺术家 / 专辑（空格分隔多个关键词）"
            };
            searchHost.Children.Add(_search);

            _search.TextChanged += delegate
            {
                _searchDebounce.Stop();
                _searchDebounce.Start();
            };
            Grid.SetColumn(searchHost, 1);
            top.Children.Add(searchHost);

            _moreButton = new Button
            {
                Style = (Style)FindResourceSafe("Btn.Tool"),
                Width = 32,
                Content = Icons.Create(Icons.More, 15, null, Theme.Brush("Brush.Text"), 1.6),
                ToolTip = "更多"
            };
            _moreButton.Click += delegate { ShowMoreMenu(); };
            Grid.SetColumn(_moreButton, 2);
            top.Children.Add(_moreButton);

            _sortButton = new Button
            {
                Style = (Style)FindResourceSafe("Btn.Tool"),
                MinWidth = 30,
                Margin = new Thickness(8, 0, 0, 0),
                Content = Icons.Create(Icons.SortAsc, 14, null, Theme.Brush("Brush.Text"), 1.5),
                ToolTip = "排序"
            };
            _sortButton.Click += delegate { ShowSortMenu(); };
            Grid.SetColumn(_sortButton, 4);
            top.Children.Add(_sortButton);

            _locateButton = new Button
            {
                Style = (Style)FindResourceSafe("Btn.Tool"),
                Width = 32,
                Margin = new Thickness(8, 0, 0, 0),
                Content = Icons.Create(Icons.Locate, 14, null, Theme.Brush("Brush.Text"), 1.5),
                ToolTip = "定位到当前播放"
            };
            _locateButton.Click += delegate { ScrollToPlaying(); };
            Grid.SetColumn(_locateButton, 3);
            top.Children.Add(_locateButton);

            Grid.SetRow(top, 0);
            Children.Add(top);

            // ---- 歌曲列表：虚拟化的 ListBox ----
            _list = new ListBox
            {
                ItemsSource = _rows,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(6, 0, 6, 0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };

            // 附加属性不能在对象初始化器里设，单独设。
            // 这几项是「导入 600 首不卡」的关键：只为可见行创建控件。
            ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
            ScrollViewer.SetVerticalScrollBarVisibility(_list, ScrollBarVisibility.Auto);
            VirtualizingStackPanel.SetIsVirtualizing(_list, true);
            VirtualizingStackPanel.SetVirtualizationMode(_list, VirtualizationMode.Recycling);
            VirtualizingStackPanel.SetScrollUnit(_list, ScrollUnit.Pixel);

            _list.ItemContainerStyle = BuildItemStyle();
            _list.ItemTemplate = BuildRowTemplate();

            // 单击即切歌
            _list.PreviewMouseLeftButtonUp += OnListClick;
            _list.MouseRightButtonUp += OnListRightClick;
            _list.KeyDown += OnListKeyDown;

            Grid.SetRow(_list, 1);
            Children.Add(_list);

            _empty = new TextBlock
            {
                Text = "列表还是空的\n\n把音频文件或文件夹拖进这里，\n或者点右上角 ⋯ 导入",
                Foreground = Theme.Brush("Brush.TextFaint"),
                FontSize = 13,
                TextAlignment = TextAlignment.Center,
                LineHeight = 22,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };
            Grid.SetRow(_empty, 1);
            Children.Add(_empty);

            // ---- 状态提示条 ----
            _statusText = new TextBlock
            {
                Foreground = Theme.Brush("Brush.Accent"),
                FontSize = 11.5,
                Margin = new Thickness(6, 3, 6, 3)
            };
            _statusBar = new Border
            {
                Background = Theme.Brush("Brush.PanelAlt"),
                Child = _statusText,
                Visibility = Visibility.Collapsed,
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(10, 0, 10, 8)
            };
            Grid.SetRow(_statusBar, 2);
            Children.Add(_statusBar);

            _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _searchDebounce.Tick += delegate
            {
                _searchDebounce.Stop();
                ApplyFilter();
            };

            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _statusTimer.Tick += delegate
            {
                _statusTimer.Stop();
                _statusBar.Visibility = Visibility.Collapsed;
            };
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

        // ------------------------------------------------------------------
        // 行数据
        // ------------------------------------------------------------------

        /// <summary>一个可见行。只暴露绑定需要的属性，模板里不做计算。</summary>
        public sealed class RowItem : INotifyPropertyChanged
        {
            public Track Track;
            public int Number;
            public double NumberWidth;

            private bool _isPlaying;

            public bool IsPlaying
            {
                get { return _isPlaying; }
                set
                {
                    if (_isPlaying == value) return;
                    _isPlaying = value;
                    Raise("IsPlaying");
                    Raise("TitleBrush");
                    Raise("NumberBrush");
                    Raise("RowBrush");
                    Raise("IndicatorVisibility");
                }
            }

            public string Title { get { return Track != null ? Track.Title : ""; } }
            public string DurationText { get { return Track != null ? Track.DurationText : "--:--"; } }
            public string FormatText { get { return Track != null ? Track.FormatText : ""; } }
            public string NumberText { get { return Number.ToString(CultureInfo.InvariantCulture); } }
            public string Tooltip { get { return Track != null ? Track.Path : ""; } }

            public Brush TitleBrush
            {
                get { return Theme.Brush(IsPlaying ? "Brush.Accent" : "Brush.Text"); }
            }

            public Brush NumberBrush
            {
                get { return Theme.Brush(IsPlaying ? "Brush.Accent" : "Brush.TextFaint"); }
            }

            public Brush RowBrush
            {
                get { return IsPlaying ? Theme.Brush("Brush.AccentSoft") : Brushes.Transparent; }
            }

            public Visibility IndicatorVisibility
            {
                get { return IsPlaying ? Visibility.Visible : Visibility.Collapsed; }
            }

            public event PropertyChangedEventHandler PropertyChanged;

            /// <summary>元数据补全后刷新显示（时长等）。</summary>
            public void RaiseAll()
            {
                Raise("Title");
                Raise("DurationText");
                Raise("FormatText");
                Raise("Tooltip");
            }

            private void Raise(string name)
            {
                var handler = PropertyChanged;
                if (handler != null) handler(this, new PropertyChangedEventArgs(name));
            }
        }

        private Style BuildItemStyle()
        {
            var style = new Style(typeof(ListBoxItem));

            style.Setters.Add(new Setter(ListBoxItem.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(ListBoxItem.BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(ListBoxItem.PaddingProperty, new Thickness(0)));
            style.Setters.Add(new Setter(ListBoxItem.MarginProperty, new Thickness(0, 1, 0, 1)));
            style.Setters.Add(new Setter(ListBoxItem.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            style.Setters.Add(new Setter(ListBoxItem.FocusableProperty, false));
            style.Setters.Add(new Setter(ListBoxItem.TemplateProperty, BuildItemTemplate()));

            return style;
        }

        private ControlTemplate BuildItemTemplate()
        {
            var template = new ControlTemplate(typeof(ListBoxItem));

            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "bg";
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            border.SetValue(Border.BackgroundProperty,
                new TemplateBindingExtension(ListBoxItem.BackgroundProperty));

            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            border.AppendChild(presenter);
            template.VisualTree = border;

            var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, Theme.Brush("Brush.Hover"), "bg"));
            template.Triggers.Add(hover);

            return template;
        }

        private DataTemplate BuildRowTemplate()
        {
            var template = new DataTemplate(typeof(RowItem));

            var grid = new FrameworkElementFactory(typeof(Grid));
            grid.SetValue(FrameworkElement.HeightProperty, 32.0);

            AddColumn(grid, new GridLength(0, GridUnitType.Auto));
            AddColumn(grid, new GridLength(0, GridUnitType.Auto));
            AddColumn(grid, new GridLength(1, GridUnitType.Star));
            AddColumn(grid, new GridLength(0, GridUnitType.Auto));
            AddColumn(grid, new GridLength(0, GridUnitType.Auto));

            // 序号
            var num = new FrameworkElementFactory(typeof(TextBlock));
            num.SetValue(TextBlock.TextProperty, new Binding("NumberText"));
            num.SetValue(TextBlock.ForegroundProperty, new Binding("NumberBrush"));
            num.SetValue(TextBlock.FontSizeProperty, 11.5);
            num.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
            num.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            num.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 10, 0));
            num.SetValue(FrameworkElement.MinWidthProperty, new Binding("NumberWidth"));
            num.SetValue(Grid.ColumnProperty, 0);
            grid.AppendChild(num);

            // 播放指示
            var indicator = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
            indicator.SetValue(System.Windows.Shapes.Path.DataProperty, Geometry.Parse(Icons.PlaySmall));
            indicator.SetValue(System.Windows.Shapes.Path.FillProperty, Theme.Brush("Brush.Accent"));
            indicator.SetValue(System.Windows.Shapes.Path.StretchProperty, Stretch.Uniform);
            indicator.SetValue(FrameworkElement.WidthProperty, 9.0);
            indicator.SetValue(FrameworkElement.HeightProperty, 9.0);
            indicator.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            indicator.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
            indicator.SetValue(UIElement.VisibilityProperty, new Binding("IndicatorVisibility"));
            indicator.SetValue(Grid.ColumnProperty, 1);
            grid.AppendChild(indicator);

            // 标题
            var title = new FrameworkElementFactory(typeof(TextBlock));
            title.SetValue(TextBlock.TextProperty, new Binding("Title"));
            title.SetValue(TextBlock.ForegroundProperty, new Binding("TitleBrush"));
            title.SetValue(TextBlock.FontSizeProperty, 12.5);
            title.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            title.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            title.SetValue(Grid.ColumnProperty, 2);
            grid.AppendChild(title);

            // 时长
            var duration = new FrameworkElementFactory(typeof(TextBlock));
            duration.SetValue(TextBlock.TextProperty, new Binding("DurationText"));
            duration.SetValue(TextBlock.ForegroundProperty, Theme.Brush("Brush.TextFaint"));
            duration.SetValue(TextBlock.FontSizeProperty, 11.5);
            duration.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            duration.SetValue(FrameworkElement.MarginProperty, new Thickness(10, 0, 0, 0));
            duration.SetValue(FrameworkElement.MinWidthProperty, 46.0);
            duration.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Right);
            duration.SetValue(Grid.ColumnProperty, 3);
            grid.AppendChild(duration);

            // 格式
            var format = new FrameworkElementFactory(typeof(TextBlock));
            format.SetValue(TextBlock.TextProperty, new Binding("FormatText"));
            format.SetValue(TextBlock.ForegroundProperty, Theme.Brush("Brush.TextFaint"));
            format.SetValue(TextBlock.FontSizeProperty, 10.5);
            format.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            format.SetValue(FrameworkElement.MarginProperty, new Thickness(10, 0, 8, 0));
            format.SetValue(FrameworkElement.MinWidthProperty, 30.0);
            format.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Right);
            format.SetValue(Grid.ColumnProperty, 4);
            grid.AppendChild(format);

            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, new Binding("RowBrush"));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));

            var host = new FrameworkElementFactory(typeof(Grid));
            host.AppendChild(border);
            host.AppendChild(grid);
            host.SetValue(FrameworkElement.ToolTipProperty, new Binding("Tooltip"));

            template.VisualTree = host;
            return template;
        }

        private static void AddColumn(FrameworkElementFactory grid, GridLength width)
        {
            var factory = new FrameworkElementFactory(typeof(ColumnDefinition));
            factory.SetValue(ColumnDefinition.WidthProperty, width);
            grid.AppendChild(factory);
        }

        // ------------------------------------------------------------------
        // 数据
        // ------------------------------------------------------------------

        public void SetGroups(List<Group> groups, string currentId)
        {
            _groups = groups ?? new List<Group>();
            _currentGroupId = currentId;
            UpdateGroupButton();
        }

        private void UpdateGroupButton()
        {
            var group = FindCurrentGroup();
            var name = group != null ? group.Name : "分组";
            var count = group != null ? group.Tracks.Count : 0;

            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(new TextBlock
            {
                Text = name,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Theme.Brush("Brush.Text"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 150
            });
            panel.Children.Add(new TextBlock
            {
                Text = count.ToString(CultureInfo.InvariantCulture),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Theme.Brush("Brush.TextFaint"),
                FontSize = 11,
                Margin = new Thickness(8, 0, 0, 0)
            });

            // 箭头和数字之间留出间距
            var chevron = Icons.Create(Icons.ChevronDown, 11, null, Theme.Brush("Brush.TextDim"), 1.5);
            chevron.Margin = new Thickness(10, 0, 0, 0);
            panel.Children.Add(chevron);

            _groupButton.Content = panel;
            _groupButton.ToolTip = name + "（" + count + " 首）\n点击切换分组，右键管理";
        }

        private Group FindCurrentGroup()
        {
            if (_groups == null) return null;
            return _groups.Find(g => g.Id == _currentGroupId) ?? (_groups.Count > 0 ? _groups[0] : null);
        }

        public void SetTracks(List<Track> tracks, string sortMode)
        {
            _tracks = tracks ?? new List<Track>();
            _sortButton.ToolTip = "排序：" + TrackSorter.DisplayName(sortMode);
            ApplyFilter();
        }

        public void SetPlaying(int index)
        {
            _playingIndex = index;
            UpdatePlayingFlags();
        }

        /// <summary>把列表滚动到当前播放的那一行并选中（「定位到当前播放」）。</summary>
        public void ScrollToPlaying()
        {
            if (_playingIndex < 0 || _playingIndex >= _rows.Count) return;

            var row = _rows[_playingIndex];
            _list.SelectedItem = row;
            _list.ScrollIntoView(row);
        }

        /// <summary>只更新播放标记，不重建列表（切歌时保证秒切）。</summary>
        private void UpdatePlayingFlags()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                _rows[i].IsPlaying = (i == _playingIndex);
            }
        }

        public void RefreshRows()
        {
            UpdateGroupButton();
            foreach (var row in _rows) row.RaiseAll();
        }

        public void FlashStatus(string message)
        {
            _statusText.Text = message;
            _statusBar.Visibility = Visibility.Visible;
            _statusTimer.Stop();
            _statusTimer.Start();
        }

        // ------------------------------------------------------------------
        // 过滤
        // ------------------------------------------------------------------

        private void ApplyFilter()
        {
            var keywords = ParseKeywords(_search.Text);

            _rows.Clear();
            int number = 0;

            var pending = new List<RowItem>();
            foreach (var t in _tracks)
            {
                if (!t.Matches(keywords)) continue;
                number++;
                pending.Add(new RowItem { Track = t, Number = number });
            }

            // 序号列宽度按最大编号自适应（序号再大也不会省略号）
            _numberWidth = MeasureNumberColumn(number);
            foreach (var row in pending) row.NumberWidth = _numberWidth;
            foreach (var row in pending) _rows.Add(row);

            UpdateEmptyState(number);
            UpdatePlayingFlags();
        }

        private void UpdateEmptyState(int visibleCount)
        {
            if (visibleCount == 0)
            {
                _empty.Text = _tracks.Count == 0
                    ? "列表还是空的\n\n把音频文件或文件夹拖进这里，\n或者点右上角 ⋯ 导入"
                    : "没有匹配的歌曲\n\n换个关键词试试";
                _empty.Visibility = Visibility.Visible;
                _list.Visibility = Visibility.Collapsed;
            }
            else
            {
                _empty.Visibility = Visibility.Collapsed;
                _list.Visibility = Visibility.Visible;
            }
        }

        private static List<string> ParseKeywords(string text)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return result;

            foreach (var part in text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                result.Add(part.Trim());
            }
            return result;
        }

        private static double MeasureNumberColumn(int count)
        {
            var probe = new FormattedText(
                count.ToString(CultureInfo.InvariantCulture),
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                12,
                Brushes.Black,
                VisualTreeHelper.GetDpi(new DrawingVisual()).PixelsPerDip);

            return Math.Max(26, probe.Width + 6);
        }

        // ------------------------------------------------------------------
        // 交互
        // ------------------------------------------------------------------

        private void OnListClick(object sender, MouseButtonEventArgs e)
        {
            var row = ResolveRow(e.OriginalSource as DependencyObject);
            if (row == null) return;

            // 单击即播放（不再需要双击）
            var handler = TrackActivated;
            if (handler != null) handler(row.Track);
        }

        private void OnListRightClick(object sender, MouseButtonEventArgs e)
        {
            var row = ResolveRow(e.OriginalSource as DependencyObject);
            if (row == null) return;

            e.Handled = true;
            _list.SelectedItem = row;
            ShowTrackMenu(row.Track, _list);
        }

        private RowItem ResolveRow(DependencyObject source)
        {
            while (source != null)
            {
                var item = source as ListBoxItem;
                if (item != null) return item.DataContext as RowItem;

                var presenter = source as ContentPresenter;
                if (presenter != null && presenter.DataContext is RowItem)
                    return presenter.DataContext as RowItem;

                source = VisualTreeHelper.GetParent(source);
            }
            return null;
        }

        private void OnListKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;

            var row = _list.SelectedItem as RowItem;
            if (row == null) return;

            var handler = TrackActivated;
            if (handler != null) handler(row.Track);
        }

        // ------------------------------------------------------------------
        // 菜单
        // ------------------------------------------------------------------

        private void ShowTrackMenu(Track track, FrameworkElement target)
        {
            var menu = new ContextMenu();

            var play = new MenuItem { Header = "播放" };
            play.Click += delegate { Raise(PlayRequested, track); };
            menu.Items.Add(play);

            var remove = new MenuItem { Header = "从列表移除" };
            remove.Click += delegate { Raise(RemoveRequested, track); };
            menu.Items.Add(remove);

            menu.Items.Add(new Separator());

            var reveal = new MenuItem { Header = "在资源管理器中显示" };
            reveal.Click += delegate { Raise(RevealRequested, track); };
            menu.Items.Add(reveal);

            menu.PlacementTarget = target;
            menu.IsOpen = true;
        }

        private void ShowGroupMenu()
        {
            var menu = new ContextMenu();

            foreach (var group in _groups)
            {
                var item = new MenuItem
                {
                    Header = group.Name + "  (" + group.Tracks.Count + ")",
                    IsChecked = group.Id == _currentGroupId
                };
                var id = group.Id;
                item.Click += delegate
                {
                    var handler = GroupSelected;
                    if (handler != null) handler(id);
                };
                menu.Items.Add(item);
            }

            menu.Items.Add(new Separator());

            var newGroup = new MenuItem { Header = "新建分组…" };
            newGroup.Click += delegate { Raise(NewGroupRequested); };
            menu.Items.Add(newGroup);

            var rename = new MenuItem { Header = "重命名分组…" };
            rename.Click += delegate { Raise(RenameGroupRequested); };
            menu.Items.Add(rename);

            var del = new MenuItem { Header = "删除分组…" };
            del.Click += delegate { Raise(DeleteGroupRequested); };
            menu.Items.Add(del);

            menu.PlacementTarget = _groupButton;
            menu.IsOpen = true;
        }

        private void ShowMoreMenu()
        {
            var menu = new ContextMenu();

            var files = new MenuItem { Header = "导入文件…", InputGestureText = "Ctrl+O" };
            files.Click += delegate { Raise(ImportFilesRequested); };
            menu.Items.Add(files);

            var folder = new MenuItem { Header = "导入文件夹…", InputGestureText = "Ctrl+Shift+O" };
            folder.Click += delegate { Raise(ImportFolderRequested); };
            menu.Items.Add(folder);

            menu.Items.Add(new Separator());

            var shuffle = new MenuItem { Header = "打乱顺序" };
            shuffle.Click += delegate { Raise(ShuffleRequested); };
            menu.Items.Add(shuffle);

            menu.Items.Add(new Separator());

            var newGroup = new MenuItem { Header = "新建分组…" };
            newGroup.Click += delegate { Raise(NewGroupRequested); };
            menu.Items.Add(newGroup);

            menu.PlacementTarget = _moreButton;
            menu.IsOpen = true;
        }

        private void ShowSortMenu()
        {
            var menu = new ContextMenu();
            var modes = new[]
            {
                TrackSorter.Added, TrackSorter.Title, TrackSorter.TitleDesc,
                TrackSorter.Duration, TrackSorter.DurationDesc, TrackSorter.Format
            };

            foreach (var mode in modes)
            {
                var item = new MenuItem { Header = TrackSorter.DisplayName(mode) };
                var captured = mode;
                item.Click += delegate
                {
                    var handler = SortChanged;
                    if (handler != null) handler(captured);
                };
                menu.Items.Add(item);
            }

            menu.PlacementTarget = _sortButton;
            menu.IsOpen = true;
        }

        private static void Raise(Action<Track> handler, Track track)
        {
            if (handler != null) handler(track);
        }

        private static void Raise(Action handler)
        {
            if (handler != null) handler();
        }

        // ------------------------------------------------------------------
        // 拖放导入
        // ------------------------------------------------------------------

        private void OnDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

            var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (paths == null || paths.Length == 0) return;

            var handler = FilesDropped;
            if (handler != null) handler(new List<string>(paths));
            e.Handled = true;
        }

        /// <summary>供主窗口读取搜索框（Ctrl+F）。</summary>
        public void FocusSearch()
        {
            _search.Focus();
            _search.SelectAll();
        }

        public void ClearSearch()
        {
            _search.Text = string.Empty;
        }
    }
}
