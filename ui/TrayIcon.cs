using System;
using System.Windows;
using System.Windows.Forms;

namespace Lumen.UI
{
    /// <summary>
    /// 托盘常驻（对应 README 的「托盘」）：
    /// 点关闭最小化到托盘继续播放；托盘右键可显示窗口 / 播放暂停 / 上下首 /
    /// 播放模式 / 显示桌面歌词 / 解锁与锁定桌面歌词 / 退出；双击恢复窗口。
    /// 媒体键的启用状态写在托盘提示文字里（菜单里只保留操作项）。
    /// </summary>
    public sealed class TrayIcon : IDisposable
    {
        private readonly NotifyIcon _icon;
        private readonly ContextMenuStrip _menu;
        private readonly MainWindow _window;

        private readonly ToolStripMenuItem _itemPlayPause;
        private readonly ToolStripMenuItem _itemPrev;
        private readonly ToolStripMenuItem _itemNext;
        private readonly ToolStripMenuItem _itemMode;
        private readonly ToolStripMenuItem _itemDesktopLyrics;
        private readonly ToolStripMenuItem _itemLock;
        private readonly ToolStripMenuItem _itemShow;

        private System.Drawing.Icon _iconNormal;
        private System.Drawing.Icon _iconPaused;

        /// <summary>媒体键状态（写进托盘提示文字）。</summary>
        public bool MediaKeysAvailable { get; set; }
        public string MediaKeysDetail { get; set; }

        public TrayIcon(MainWindow window)
        {
            _window = window;

            _iconNormal = TrayIconFactory.CreateWithState(32, true);
            _iconPaused = TrayIconFactory.CreateWithState(32, false);

            _menu = new ContextMenuStrip
            {
                ShowImageMargin = false,
                Renderer = new DarkMenuRenderer()
            };

            _itemShow = new ToolStripMenuItem("显示窗口");
            _itemShow.Click += delegate { ShowWindow(); };
            _menu.Items.Add(_itemShow);

            _menu.Items.Add(new ToolStripSeparator());

            _itemPlayPause = new ToolStripMenuItem("播放 / 暂停");
            _itemPlayPause.Click += delegate { _window.PlayPauseFromTray(); UpdateState(); };
            _menu.Items.Add(_itemPlayPause);

            _itemPrev = new ToolStripMenuItem("上一首");
            _itemPrev.Click += delegate { _window.PrevFromTray(); UpdateState(); };
            _menu.Items.Add(_itemPrev);

            _itemNext = new ToolStripMenuItem("下一首");
            _itemNext.Click += delegate { _window.NextFromTray(); UpdateState(); };
            _menu.Items.Add(_itemNext);

            _itemMode = new ToolStripMenuItem("播放模式");
            _itemMode.Click += delegate { _window.CycleModeFromTray(); UpdateState(); };
            _menu.Items.Add(_itemMode);

            _menu.Items.Add(new ToolStripSeparator());

            _itemDesktopLyrics = new ToolStripMenuItem("显示桌面歌词");
            _itemDesktopLyrics.Click += delegate
            {
                _window.ToggleDesktopLyrics();
                UpdateState();
            };
            _menu.Items.Add(_itemDesktopLyrics);

            // 桌面歌词锁定后鼠标完全穿透，词条收不到事件，
            // 所以解锁入口必须放在托盘里（README）
            _itemLock = new ToolStripMenuItem("解锁桌面歌词");
            _itemLock.Click += delegate
            {
                _window.EnsureDesktopLyricsForTray();
                var lyrics = _window.DesktopLyrics;
                if (lyrics != null)
                {
                    lyrics.SetLocked(!lyrics.IsLocked);
                    _window.SaveConfigNow();
                }
                UpdateState();
            };
            _menu.Items.Add(_itemLock);

            _menu.Items.Add(new ToolStripSeparator());

            var itemExit = new ToolStripMenuItem("退出");
            itemExit.Click += delegate { _window.ExitApplication(); };
            _menu.Items.Add(itemExit);

            _icon = new NotifyIcon
            {
                Icon = _iconNormal,
                ContextMenuStrip = _menu,
                Visible = true,
                Text = "Lumen 音乐"
            };
            _icon.DoubleClick += delegate { ShowWindow(); };
            _icon.MouseClick += delegate (object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) ShowWindow();
            };

            _menu.Opening += delegate { UpdateState(); };
            UpdateState();
        }

        private void ShowWindow()
        {
            try
            {
                SingleInstance.BringToFront(_window);
            }
            catch (Exception ex)
            {
                Log.Warn("显示窗口失败: " + ex.Message);
            }
        }

        /// <summary>同步菜单勾选状态与托盘提示文字。</summary>
        public void UpdateState()
        {
            try
            {
                var config = _window.Configuration;
                var player = _window.AudioPlayer;

                bool playing = player != null && player.IsPlaying;
                _itemPlayPause.Text = playing ? "暂停" : "播放";
                _icon.Icon = playing ? _iconNormal : _iconPaused;

                string modeName = "顺序播放";
                if (player != null)
                {
                    if (player.Mode == Audio.Player.PlayMode.RepeatList) modeName = "列表循环";
                    else if (player.Mode == Audio.Player.PlayMode.RepeatOne) modeName = "单曲循环";
                }
                _itemMode.Text = "播放模式：" + modeName;

                _itemDesktopLyrics.Checked = config.DesktopLyricsVisible;

                var lyrics = _window.DesktopLyrics;
                bool locked = lyrics != null && lyrics.IsLocked;

                _itemLock.Text = locked
                    ? "解锁桌面歌词"
                    : "锁定桌面歌词";
                _itemLock.Checked = locked;
                _itemLock.Enabled = config.DesktopLyricsVisible;

                // 媒体键状态写在托盘提示文字里（菜单里只保留操作项）
                string keys = MediaKeysAvailable
                    ? "媒体键：已启用"
                    : "媒体键：不可用" + (string.IsNullOrEmpty(MediaKeysDetail) ? "" : "（" + MediaKeysDetail + "）");

                string title = "Lumen 音乐";
                if (player != null && player.HasTrack)
                {
                    var name = System.IO.Path.GetFileNameWithoutExtension(player.CurrentPath);
                    title = name + (playing ? " - 播放中" : " - 已暂停");
                }

                // NotifyIcon.Text 上限 63 字符（.NET Framework）/127（新版），保守截断
                var text = title + "\n" + keys;
                if (text.Length > 62) text = text.Substring(0, 62);
                _icon.Text = text;
            }
            catch (Exception ex)
            {
                Log.Warn("刷新托盘状态失败: " + ex.Message);
            }
        }

        public void Dispose()
        {
            try
            {
                _icon.Visible = false;
                _icon.Dispose();
            }
            catch { }

            if (_iconNormal != null) { try { _iconNormal.Dispose(); } catch { } _iconNormal = null; }
            if (_iconPaused != null) { try { _iconPaused.Dispose(); } catch { } _iconPaused = null; }
        }
    }

    /// <summary>深色托盘菜单渲染（与软件整体风格一致）。</summary>
    internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColorTable()) { }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled
                ? System.Drawing.Color.FromArgb(230, 232, 234)
                : System.Drawing.Color.FromArgb(110, 113, 118);
            base.OnRenderItemText(e);
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(38, 40, 44)))
            {
                e.Graphics.FillRectangle(brush, e.AffectedBounds);
            }
        }
    }

    internal sealed class DarkColorTable : ProfessionalColorTable
    {
        public override System.Drawing.Color MenuItemSelected
        {
            get { return System.Drawing.Color.FromArgb(58, 62, 67); }
        }

        public override System.Drawing.Color MenuItemBorder
        {
            get { return System.Drawing.Color.FromArgb(58, 62, 67); }
        }

        public override System.Drawing.Color MenuBorder
        {
            get { return System.Drawing.Color.FromArgb(52, 55, 60); }
        }

        public override System.Drawing.Color ToolStripDropDownBackground
        {
            get { return System.Drawing.Color.FromArgb(38, 40, 44); }
        }

        public override System.Drawing.Color ImageMarginGradientBegin
        {
            get { return System.Drawing.Color.FromArgb(38, 40, 44); }
        }

        public override System.Drawing.Color ImageMarginGradientMiddle
        {
            get { return System.Drawing.Color.FromArgb(38, 40, 44); }
        }

        public override System.Drawing.Color ImageMarginGradientEnd
        {
            get { return System.Drawing.Color.FromArgb(38, 40, 44); }
        }

        public override System.Drawing.Color SeparatorDark
        {
            get { return System.Drawing.Color.FromArgb(52, 55, 60); }
        }

        public override System.Drawing.Color SeparatorLight
        {
            get { return System.Drawing.Color.FromArgb(52, 55, 60); }
        }
    }
}
