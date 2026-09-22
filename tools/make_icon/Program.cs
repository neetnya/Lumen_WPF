using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

// 用代码生成 assets\lumen.ico
// 用法: dotnet run --project tools\make_icon  （或在 self-test 里调用 Generate）
internal static class MakeIcon
{
    private static void Main(string[] args)
    {
        // 只接受真正的路径参数，忽略 dotnet run 透传的开关（如 --nologo）
        string outPath = null;
        foreach (var a in args)
        {
            if (a.StartsWith("-", StringComparison.Ordinal)) continue;
            outPath = a;
            break;
        }

        if (outPath == null)
        {
            // 默认写到仓库的 assets\lumen.ico（从 bin\Release\net9.0-windows 往上找）
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Lumen.csproj")))
                dir = dir.Parent;
            var root = dir != null ? dir.FullName : Directory.GetCurrentDirectory();
            outPath = Path.Combine(root, "assets", "lumen.ico");
        }

        outPath = Path.GetFullPath(outPath);
        var parent = Path.GetDirectoryName(outPath);
        if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent)) Directory.CreateDirectory(parent);

        Generate(outPath);
        Console.WriteLine("wrote " + outPath + " (" + new FileInfo(outPath).Length + " bytes)");
    }

    /// <summary>画出 Lumen 的应用图标：深色圆角底 + 蓝色音符。</summary>
    public static void Generate(string outPath)
    {
        int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
        var pngs = new byte[sizes.Length][];

        for (int i = 0; i < sizes.Length; i++)
        {
            using (var bmp = Render(sizes[i]))
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                pngs[i] = ms.ToArray();
            }
        }

        // ICO 容器：6 字节头 + 每张图 16 字节目录项 + PNG 数据
        using (var fs = new FileStream(outPath, FileMode.Create, FileAccess.Write))
        using (var bw = new BinaryWriter(fs))
        {
            bw.Write((ushort)0);              // reserved
            bw.Write((ushort)1);              // type = icon
            bw.Write((ushort)sizes.Length);   // count

            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                byte dim = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
                bw.Write(dim);                // width  (0 = 256)
                bw.Write(dim);                // height (0 = 256)
                bw.Write((byte)0);            // palette count
                bw.Write((byte)0);            // reserved
                bw.Write((ushort)1);          // color planes
                bw.Write((ushort)32);         // bits per pixel
                bw.Write(pngs[i].Length);     // data size
                bw.Write(offset);             // data offset
                offset += pngs[i].Length;
            }

            foreach (var png in pngs) bw.Write(png);
        }
    }

    private static Bitmap Render(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.Clear(Color.Transparent);

            float s = size;
            float radius = s * 0.22f;

            // 深色圆角底
            using (var path = RoundedRect(new RectangleF(0, 0, s, s), radius))
            using (var brush = new LinearGradientBrush(
                       new RectangleF(0, 0, s, s),
                       Color.FromArgb(255, 34, 38, 45),
                       Color.FromArgb(255, 18, 20, 23),
                       LinearGradientMode.ForwardDiagonal))
            {
                g.FillPath(brush, path);
            }

            // 蓝色氛围光
            using (var glow = new GraphicsPath())
            {
                glow.AddEllipse(s * 0.05f, s * 0.45f, s * 0.9f, s * 0.7f);
                using (var pgb = new PathGradientBrush(glow))
                {
                    pgb.CenterColor = Color.FromArgb(70, 79, 163, 255);
                    pgb.SurroundColors = new[] { Color.FromArgb(0, 79, 163, 255) };
                    g.FillPath(pgb, glow);
                }
            }

            // 音符：两个符头 + 符干 + 横梁，组成一个连音符
            float headR = s * 0.125f;

            // 左（低）音符
            float cx1 = s * 0.335f;
            float cy1 = s * 0.695f;

            // 右（高）音符
            float cx2 = s * 0.635f;
            float cy2 = s * 0.605f;

            float stemW = Math.Max(1.5f, s * 0.052f);
            float stemTop = s * 0.245f;
            float stem1X = cx1 + headR * 1.12f;
            float stem2X = cx2 + headR * 1.12f;

            using (var accent = new SolidBrush(Color.FromArgb(255, 79, 163, 255)))
            using (var accentLight = new SolidBrush(Color.FromArgb(255, 126, 190, 255)))
            {
                // 符干（先画，让符头盖住底部接缝）
                using (var p = new GraphicsPath())
                {
                    p.AddRectangle(new RectangleF(stem1X - stemW / 2f, stemTop, stemW, cy1 + headR * 0.2f - stemTop));
                    g.FillPath(accent, p);
                }
                using (var p = new GraphicsPath())
                {
                    p.AddRectangle(new RectangleF(stem2X - stemW / 2f, stemTop, stemW, cy2 + headR * 0.2f - stemTop));
                    g.FillPath(accentLight, p);
                }

                // 横梁（连接两根符干顶端）
                using (var beam = new GraphicsPath())
                {
                    beam.AddPolygon(new[]
                    {
                        new PointF(stem1X - stemW / 2f, stemTop),
                        new PointF(stem2X + stemW / 2f, stemTop),
                        new PointF(stem2X + stemW / 2f, stemTop + s * 0.115f),
                        new PointF(stem1X - stemW / 2f, stemTop + s * 0.155f)
                    });
                    g.FillPath(accentLight, beam);
                }

                // 符头（略微倾斜的椭圆）
                g.TranslateTransform(cx1, cy1);
                g.RotateTransform(-22);
                g.FillEllipse(accent, -headR * 1.3f, -headR * 0.95f, headR * 2.6f, headR * 1.9f);
                g.ResetTransform();

                g.TranslateTransform(cx2, cy2);
                g.RotateTransform(-22);
                g.FillEllipse(accentLight, -headR * 1.3f, -headR * 0.95f, headR * 2.6f, headR * 1.9f);
                g.ResetTransform();
            }
        }
        return bmp;
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
