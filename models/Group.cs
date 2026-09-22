using System;
using System.Collections.Generic;

namespace Lumen.Models
{
    /// <summary>一个分组（对应 README 的「分组」，播放进度按分组独立记忆）。</summary>
    public sealed class Group
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "默认分组";

        /// <summary>本分组的曲目，顺序即播放顺序。</summary>
        public List<Track> Tracks = new List<Track>();

        /// <summary>是否由「导入文件夹」创建（用于显示与重扫行为）。</summary>
        public bool FromFolder;

        /// <summary>导入时的文件夹路径（FromFolder 时有效）。</summary>
        public string SourceFolder;

        // ---- 每分组独立记忆播放位置 ----
        public string LastTrackPath;
        public double LastPositionSeconds;

        /// <summary>一次性打乱后的顺序（存放曲目路径）；为空表示未打乱。</summary>
        public List<string> ShuffledOrder;
    }
}