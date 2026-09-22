using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Lumen.UI
{
    /// <summary>
    /// 托盘图标：用 GDI+ 画出来（不依赖外部 .ico 资源，避免发布时漏文件）。
    /// </summary>
    public static class TrayIconFactory
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        /// <summary>生成托盘图标（音符，与主图标一致）。</summary>
        public static System.Drawing.Icon Create(int size)
        {
            using (var bitmap = Render(size))
            {
                var handle = bitmap.GetHicon();
                try
                {
                    // 克隆一份托管 Icon，然后立刻释放原生句柄
                    using (var temp = System.Drawing.Icon.FromHandle(handle))
                    {
                        return (System.Drawing.Icon)temp.Clone();
                    }
                }
                finally
                {
                    DestroyIcon(handle);
                }
            }
        }

        /// <summary>生成播放/暂停状态的小圆点叠加图标。</summary>
        public static System.Drawing.Icon CreateWithState(int size, bool playing)
        {
            using (var bitmap = Render(size))
            {
                if (!playing)
                {
                    // 暂停态：右下角加一个浅色方点
                    using (var g = Graphics.FromImage(bitmap))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        float s = size * 0.30f;
                        float x = size - s - size * 0.04f;
                        float y = size - s - size * 0.04f;
                        using (var brush = new SolidBrush(Color.FromArgb(220, 150, 155, 160)))
                        {
                            g.FillEllipse(brush, x, y, s, s);
                        }
                    }
                }

                var handle = bitmap.GetHicon();
                try
                {
                    using (var temp = System.Drawing.Icon.FromHandle(handle))
                    {
                        return (System.Drawing.Icon)temp.Clone();
                    }
                }
                finally
                {
                    DestroyIcon(handle);
                }
            }
        }

        private static Bitmap Render(int size)
        {
            var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.Clear(Color.Transparent);

                float s = size;
                var accent = Color.FromArgb(255, 79, 163, 255);
                var accentLight = Color.FromArgb(255, 126, 190, 255);

                // 深色圆角底（托盘图标小，圆角相应小些）
                using (var path = RoundedRect(new RectangleF(0, 0, s, s), s * 0.22f))
                using (var brush = new SolidBrush(Color.FromArgb(255, 26, 28, 32)))
                {
                    g.FillPath(brush, path);
                }

                float headR = s * 0.125f;
                float cx1 = s * 0.335f;
                float cy1 = s * 0.695f;
                float cx2 = s * 0.635f;
                float cy2 = s * 0.605f;
                float stemW = Math.Max(1f, s * 0.055f);
                float stemTop = s * 0.245f;
                float stem1X = cx1 + headR * 1.12f;
                float stem2X = cx2 + headR * 1.12f;

                using (var b1 = new SolidBrush(accent))
                using (var b2 = new SolidBrush(accentLight))
                {
                    // 符干
                    using (var p = new GraphicsPath())
                    {
                        p.AddRectangle(new RectangleF(stem1X - stemW / 2f, stemTop, stemW, cy1 + headR * 0.2f - stemTop));
                        g.FillPath(b1, p);
                    }
                    using (var p = new GraphicsPath())
                    {
                        p.AddRectangle(new RectangleF(stem2X - stemW / 2f, stemTop, stemW, cy2 + headR * 0.2f - stemTop));
                        g.FillPath(b2, p);
                    }

                    // 横梁
                    using (var beam = new GraphicsPath())
                    {
                        beam.AddPolygon(new[]
                        {
                            new PointF(stem1X - stemW / 2f, stemTop),
                            new PointF(stem2X + stemW / 2f, stemTop),
                            new PointF(stem2X + stemW / 2f, stemTop + s * 0.115f),
                            new PointF(stem1X - stemW / 2f, stemTop + s * 0.155f)
                        });
                        g.FillPath(b2, beam);
                    }

                    // 符头
                    g.TranslateTransform(cx1, cy1);
                    g.RotateTransform(-22);
                    g.FillEllipse(b1, -headR * 1.3f, -headR * 0.95f, headR * 2.6f, headR * 1.9f);
                    g.ResetTransform();

                    g.TranslateTransform(cx2, cy2);
                    g.RotateTransform(-22);
                    g.FillEllipse(b2, -headR * 1.3f, -headR * 0.95f, headR * 2.6f, headR * 1.9f);
                    g.ResetTransform();
                }
            }
            return bitmap;
        }

        private static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = radius * 2f;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
