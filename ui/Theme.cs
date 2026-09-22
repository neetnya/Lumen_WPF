using System;
using System.Windows;
using System.Windows.Media;

namespace Lumen.UI
{
    /// <summary>从 App.xaml 资源字典里取画刷 / 颜色。</summary>
    public static class Theme
    {
        public static Brush Brush(string key)
        {
            var brush = Lookup(key) as Brush;
            if (brush != null) return brush;
            return Brushes.Gray;
        }

        public static Color Color(string key)
        {
            var color = Lookup(key);
            if (color is Color c) return c;
            if (color is SolidColorBrush b) return b.Color;
            return Colors.Gray;
        }

        private static object Lookup(string key)
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
    }
}
