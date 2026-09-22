using System;
using System.Windows;
using System.Windows.Media;

namespace Lumen.UI
{
    /// <summary>
    /// 全部矢量图标。
    /// 用 Path 几何绘制，不依赖字体或 Emoji，任何 DPI 下都清晰。
    /// </summary>
    public static class Icons
    {
        // 播放 / 暂停
        public const string Play = "M4,2 L16,10 L4,18 Z";
        public const string Pause = "M4,3 H8 V17 H4 Z M12,3 H16 V17 H12 Z";
        public const string Stop = "M4,4 H16 V16 H4 Z";

        // 上一首 / 下一首
        public const string Prev = "M5,4 H8 V16 H5 Z M17,4 L7,10 L17,16 Z";
        public const string Next = "M3,4 L13,10 L3,16 Z M16,4 H19 V16 H16 Z";

        // 播放模式
        public const string ModeSequential = "M3,10 H17 M13,6 L17,10 L13,14";
        public const string ModeRepeatList = "M4,7 A6,6 0 0 1 16,7 M16,13 A6,6 0 0 1 4,13 M14,4 L16,7 L13,8 M6,16 L4,13 L7,12";
        public const string ModeRepeatOne = "M4,7 A6,6 0 0 1 16,7 M16,13 A6,6 0 0 1 4,13 M14,4 L16,7 L13,8 M6,16 L4,13 L7,12 M10,8 V12 M9,9 L10,8 L11,9";

        // 音量
        public const string VolumeHigh = "M3,7 H6 L10,4 V16 L6,13 H3 Z M13,7 A4,4 0 0 1 13,13 M15,5 A7,7 0 0 1 15,15";
        public const string VolumeMute = "M3,7 H6 L10,4 V16 L6,13 H3 Z M13,8 L17,12 M17,8 L13,12";

        // 关闭 / 最小化 / 最大化 / 还原
        public const string Close = "M3,3 L13,13 M13,3 L3,13";
        public const string Minimize = "M3,8 H13";
        public const string Maximize = "M3,3 H13 V13 H3 Z";
        public const string Restore = "M5,3 H13 V11 M3,5 H11 V13 H3 Z";

        // 工具栏
        public const string More = "M4,9 A1.4,1.4 0 1 0 4.01,9 M9,9 A1.4,1.4 0 1 0 9.01,9 M14,9 A1.4,1.4 0 1 0 14.01,9";
        public const string Search = "M8.5,3 A5.5,5.5 0 1 0 8.51,3 M12.5,12.5 L16,16";
        public const string ChevronDown = "M4,6 L8.5,10.5 L13,6";
        public const string SortAsc = "M10,15 V4 M5,9 L10,4 L15,9";
        public const string SortDesc = "M10,4 V15 M5,10 L10,15 L15,10";
        public const string Locate = "M8,2 V5 M8,15 V18 M2,8 H5 M15,8 H18 M4.5,4.5 L6.5,6.5 M13.5,13.5 L15.5,15.5 M15.5,4.5 L13.5,6.5 M6.5,13.5 L4.5,15.5 M8,11 A3,3 0 1 0 8.01,11";
        public const string Folder = "M2,5 A1,1 0 0 1 3,4 H7 L9,6 H16 A1,1 0 0 1 17,7 V14 A1,1 0 0 1 16,15 H3 A1,1 0 0 1 2,14 Z";
        public const string Music = "M7,14 A2,2 0 1 0 7.01,14 M9,13.5 V4 L16,6 V11 M14,12.5 A2,2 0 1 0 14.01,12.5";
        public const string Lyric = "M3,4 H17 M3,8 H13 M3,12 H17 M3,16 H10";

        // 播放 / 暂停（小号，用于列表行内）
        public const string PlaySmall = "M5,3 L15,10 L5,17 Z";
        public const string PauseSmall = "M6,4 H9 V16 H6 Z M11,4 H14 V16 H11 Z";

        /// <summary>把路径数据包成一个可任意缩放着色的图标元素。</summary>
        public static System.Windows.Shapes.Path Create(string data, double size, Brush fill, Brush stroke = null,
            double strokeThickness = 1.6)
        {
            var path = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(data),
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform,
                Fill = fill,
                Stroke = stroke,
                StrokeThickness = stroke == null ? 0 : strokeThickness,
                SnapsToDevicePixels = true
            };
            return path;
        }
    }
}
