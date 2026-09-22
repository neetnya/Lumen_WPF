using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Lumen
{
    /// <summary>
    /// 系统媒体键。
    /// 直接接管键盘自带的媒体键（播放/暂停、上一首、下一首、停止），
    /// 例如某些键盘上 fn+F7 输出的就是标准 VK_MEDIA_PLAY_PAUSE。
    ///
    /// 媒体键是全局独占资源：被别的播放器占用时注册会失败，
    /// 这里如实把失败原因报给界面（写进托盘提示文字）。
    /// </summary>
    public sealed class MediaKeys : IDisposable
    {
        private const int WM_HOTKEY = 0x0312;

        // 热键 ID
        private const int IdPlayPause = 0x4C01;
        private const int IdNext = 0x4C02;
        private const int IdPrev = 0x4C03;
        private const int IdStop = 0x4C04;

        private const uint VK_MEDIA_NEXT_TRACK = 0xB0;
        private const uint VK_MEDIA_PREV_TRACK = 0xB1;
        private const uint VK_MEDIA_STOP = 0xB2;
        private const uint VK_MEDIA_PLAY_PAUSE = 0xB3;
        private const uint MOD_NOREPEAT = 0x4000;

        private HwndSource _source;
        private IntPtr _handle = IntPtr.Zero;
        private bool _registered;

        /// <summary>是否成功注册（界面据此提示）。</summary>
        public bool IsAvailable { get { return _registered; } }

        /// <summary>失败原因（成功时为空）。</summary>
        public string FailureReason { get; private set; }

        public event Action PlayPausePressed;
        public event Action NextPressed;
        public event Action PrevPressed;
        public event Action StopPressed;

        /// <summary>
        /// 创建隐藏消息窗口并注册媒体键。返回是否成功。
        /// </summary>
        public bool Register()
        {
            try
            {
                // 用一个隐藏的 HwndSource 接收 WM_HOTKEY
                var parameters = new HwndSourceParameters("LumenMediaKeySink")
                {
                    Width = 0,
                    Height = 0,
                    PositionX = -10000,
                    PositionY = -10000,
                    WindowStyle = 0,          // 不可见
                    ExtendedWindowStyle = 0x00000080  // WS_EX_TOOLWINDOW
                };

                _source = new HwndSource(parameters);
                _source.AddHook(WndProc);
                _handle = _source.Handle;

                if (_handle == IntPtr.Zero)
                {
                    FailureReason = "无法创建消息窗口";
                    return false;
                }

                int ok = 0;
                var failed = new System.Collections.Generic.List<string>();

                if (TryRegister(IdPlayPause, VK_MEDIA_PLAY_PAUSE)) ok++;
                else failed.Add("播放/暂停");

                if (TryRegister(IdNext, VK_MEDIA_NEXT_TRACK)) ok++;
                else failed.Add("下一首");

                if (TryRegister(IdPrev, VK_MEDIA_PREV_TRACK)) ok++;
                else failed.Add("上一首");

                if (TryRegister(IdStop, VK_MEDIA_STOP)) ok++;
                else failed.Add("停止");

                if (ok == 0)
                {
                    FailureReason = "已被其他程序占用";
                    _registered = false;
                    Log.Warn("媒体键注册失败：全部被占用（可能被别的播放器占用）");
                    return false;
                }

                _registered = true;
                FailureReason = failed.Count > 0 ? "部分被占用: " + string.Join("/", failed) : null;
                Log.Info("媒体键注册成功 " + ok + "/4" +
                    (failed.Count > 0 ? "，失败: " + string.Join("/", failed) : ""));
                return true;
            }
            catch (Exception ex)
            {
                FailureReason = ex.Message;
                _registered = false;
                Log.Error("媒体键注册异常", ex);
                return false;
            }
        }

        private bool TryRegister(int id, uint vk)
        {
            bool ok = Native.Win32.RegisterHotKey(_handle, id, MOD_NOREPEAT, vk);
            if (!ok)
            {
                int error = Marshal.GetLastWin32Error();
                Log.Warn("媒体键 0x" + vk.ToString("X2") + " 注册失败，错误码 " + error +
                         "（1409 = 已被其他程序注册）");
            }
            return ok;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != WM_HOTKEY) return IntPtr.Zero;

            int id = wParam.ToInt32();
            try
            {
                switch (id)
                {
                    case IdPlayPause: Raise(PlayPausePressed); break;
                    case IdNext: Raise(NextPressed); break;
                    case IdPrev: Raise(PrevPressed); break;
                    case IdStop: Raise(StopPressed); break;
                }
            }
            catch (Exception ex)
            {
                Log.Error("处理媒体键失败", ex);
            }

            handled = true;
            return IntPtr.Zero;
        }

        private static void Raise(Action handler)
        {
            if (handler == null) return;
            var dispatcher = Application.Current != null ? Application.Current.Dispatcher : null;
            if (dispatcher != null && !dispatcher.CheckAccess())
                dispatcher.BeginInvoke(handler);
            else
                handler();
        }

        public void Dispose()
        {
            try
            {
                if (_handle != IntPtr.Zero)
                {
                    Native.Win32.UnregisterHotKey(_handle, IdPlayPause);
                    Native.Win32.UnregisterHotKey(_handle, IdNext);
                    Native.Win32.UnregisterHotKey(_handle, IdPrev);
                    Native.Win32.UnregisterHotKey(_handle, IdStop);
                }
            }
            catch { }

            try
            {
                if (_source != null)
                {
                    _source.RemoveHook(WndProc);
                    _source.Dispose();
                    _source = null;
                }
            }
            catch { }

            _handle = IntPtr.Zero;
            _registered = false;
        }
    }
}
