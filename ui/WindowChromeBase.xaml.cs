using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Runtime.InteropServices;

namespace Lumen.UI
{
    /// <summary>
    /// 无边框窗口基类：自绘标题栏（不会出现两个标题栏），
    /// 靠 WindowChrome 保留系统八向缩放 / 贴边 / 最大化行为。
    /// 子类把标题栏内容赋给 TitleBar，窗口内容赋给 Content。
    /// </summary>
    public partial class WindowChromeBase : Window
    {
        public static readonly DependencyProperty TitleBarHeightProperty =
            DependencyProperty.Register("TitleBarHeight", typeof(double), typeof(WindowChromeBase),
                new PropertyMetadata(44.0));

        public static readonly DependencyProperty TitleBarProperty =
            DependencyProperty.Register("TitleBar", typeof(object), typeof(WindowChromeBase),
                new PropertyMetadata(null));

        public double TitleBarHeight
        {
            get { return (double)GetValue(TitleBarHeightProperty); }
            set { SetValue(TitleBarHeightProperty, value); }
        }

        /// <summary>自绘标题栏内容。</summary>
        public object TitleBar
        {
            get { return GetValue(TitleBarProperty); }
            set { SetValue(TitleBarProperty, value); }
        }

        private readonly ContentPresenter _titleBarArea;
        private readonly ContentPresenter _bodyArea;

        /// <summary>
        /// 窗口主体内容。
        ///
        /// 注意：不能直接把 ContentPresenter 绑到 Window.Content —— 因为
        /// ContentPresenter 本身就在 Window.Content 里面，绑定会自我递归导致
        /// 栈溢出（0xC00000FD）。所以这里用独立的 Body 属性转发。
        /// </summary>
        public static readonly DependencyProperty BodyProperty =
            DependencyProperty.Register("Body", typeof(object), typeof(WindowChromeBase),
                new PropertyMetadata(null));

        public object Body
        {
            get { return GetValue(BodyProperty); }
            set { SetValue(BodyProperty, value); }
        }

        public WindowChromeBase()
        {
            InitializeComponent();

            _titleBarArea = (ContentPresenter)FindName("TitleBarArea");
            _bodyArea = (ContentPresenter)FindName("BodyArea");

            _titleBarArea.SetBinding(ContentPresenter.ContentProperty,
                new Binding("TitleBar") { Source = this, Mode = BindingMode.OneWay });
            _bodyArea.SetBinding(ContentPresenter.ContentProperty,
                new Binding("Body") { Source = this, Mode = BindingMode.OneWay });

            CommandBindings.Add(new CommandBinding(SystemCommands.CloseWindowCommand,
                delegate { Close(); }));
            CommandBindings.Add(new CommandBinding(SystemCommands.MinimizeWindowCommand,
                delegate { WindowState = WindowState.Minimized; }));
            CommandBindings.Add(new CommandBinding(SystemCommands.MaximizeWindowCommand,
                delegate { WindowState = WindowState.Maximized; }));
            CommandBindings.Add(new CommandBinding(SystemCommands.RestoreWindowCommand,
                delegate { WindowState = WindowState.Normal; }));

            StateChanged += delegate { OnWindowStateChanged(); };
        }

        /// <summary>子类可覆写：状态变化（用于切换最大化/还原图标）。</summary>
        protected virtual void OnWindowStateChanged()
        {
        }

        /// <summary>拖动标题栏移动窗口（最大化时还原并跟随鼠标）。</summary>
        protected void DragMoveFromTitleBar(MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ToggleMaximize();
                return;
            }

            if (WindowState == WindowState.Maximized)
            {
                var mouse = e.GetPosition(this);
                double ratio = mouse.X / Math.Max(1, ActualWidth);
                var screenPoint = PointToScreen(mouse);

                WindowState = WindowState.Normal;

                var scale = DpiScale();
                Left = screenPoint.X / scale - ActualWidth * ratio;
                Top = screenPoint.Y / scale - 22;
            }

            try { DragMove(); }
            catch { /* 鼠标已释放 */ }
        }

        public void ToggleMaximize()
        {
            if (ResizeMode == ResizeMode.NoResize || ResizeMode == ResizeMode.CanMinimize) return;
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private double DpiScale()
        {
            var source = PresentationSource.FromVisual(this);
            if (source != null && source.CompositionTarget != null)
                return source.CompositionTarget.TransformToDevice.M11;
            return 1.0;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var helper = new WindowInteropHelper(this);
            if (helper.Handle != IntPtr.Zero) MaximizeFix.Apply(helper.Handle);
        }
    }

    /// <summary>无边框窗口最大化时修正工作区边距（避免盖住任务栏 / 溢出屏幕）。</summary>
    internal static class MaximizeFix
    {
        private const int WM_GETMINMAXINFO = 0x0024;
        private const uint MONITOR_DEFAULTTONEAREST = 2;

        public static void Apply(IntPtr hwnd)
        {
            try
            {
                var source = HwndSource.FromHwnd(hwnd);
                if (source != null) source.AddHook(Hook);
            }
            catch { }
        }

        private static IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != WM_GETMINMAXINFO) return IntPtr.Zero;

            try
            {
                var mmi = (MINMAXINFO)Marshal.PtrToStructure(lParam, typeof(MINMAXINFO));
                var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
                if (monitor != IntPtr.Zero)
                {
                    var info = new MONITORINFO();
                    info.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
                    if (GetMonitorInfo(monitor, ref info))
                    {
                        var work = info.rcWork;
                        var mon = info.rcMonitor;
                        mmi.ptMaxPosition.x = work.left - mon.left;
                        mmi.ptMaxPosition.y = work.top - mon.top;
                        mmi.ptMaxSize.x = work.right - work.left;
                        mmi.ptMaxSize.y = work.bottom - work.top;
                        mmi.ptMaxTrackSize.x = work.right - work.left;
                        mmi.ptMaxTrackSize.y = work.bottom - work.top;
                        Marshal.StructureToPtr(mmi, lParam, true);
                        handled = true;
                    }
                }
            }
            catch { }

            return IntPtr.Zero;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
    }
}
