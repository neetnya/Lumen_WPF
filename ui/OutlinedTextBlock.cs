using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Lumen.UI
{
    /// <summary>
    /// 带描边的文本：先用黑色笔触描出字形轮廓，再在其上填充前景色。
    /// 用于桌面歌词「白字黑描边」——描边真正居中、均匀地包裹每个字，
    /// 而不是 DropShadowEffect 那种单一方向的偏移阴影。
    /// </summary>
    public sealed class OutlinedTextBlock : FrameworkElement
    {
        private string _text = string.Empty;
        private FontFamily _fontFamily = new FontFamily("Microsoft YaHei");
        private double _fontSize = 30;
        private FontWeight _fontWeight = FontWeights.Bold;
        private Brush _foreground = Brushes.White;
        private Brush _outline = Brushes.Black;
        private double _outlineThickness = 2;

        public string Text
        {
            get { return _text; }
            set
            {
                if (string.Equals(_text, value, StringComparison.Ordinal)) return;
                _text = value ?? string.Empty;
                InvalidateMeasure();
                InvalidateVisual();
            }
        }

        public FontFamily FontFamily
        {
            get { return _fontFamily; }
            set { _fontFamily = value; InvalidateMeasure(); InvalidateVisual(); }
        }

        public double FontSize
        {
            get { return _fontSize; }
            set { _fontSize = value; InvalidateMeasure(); InvalidateVisual(); }
        }

        public FontWeight FontWeight
        {
            get { return _fontWeight; }
            set { _fontWeight = value; InvalidateMeasure(); InvalidateVisual(); }
        }

        public Brush Foreground
        {
            get { return _foreground; }
            set { _foreground = value; InvalidateVisual(); }
        }

        public Brush Outline
        {
            get { return _outline; }
            set { _outline = value; InvalidateVisual(); }
        }

        public double OutlineThickness
        {
            get { return _outlineThickness; }
            set { _outlineThickness = value; InvalidateVisual(); }
        }

        private Typeface _typeface;

        private Typeface CurrentTypeface
        {
            get
            {
                if (_typeface == null)
                    _typeface = new Typeface(_fontFamily, FontStyles.Normal, _fontWeight, FontStretches.Normal);
                return _typeface;
            }
        }

        private FormattedText BuildFormattedText()
        {
            double ppd = 1.0;
            var source = PresentationSource.FromVisual(this);
            if (source != null && source.CompositionTarget != null)
                ppd = source.CompositionTarget.TransformToDevice.M11;

            return new FormattedText(
                _text,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                CurrentTypeface,
                _fontSize,
                _foreground,
                ppd);
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            var ft = BuildFormattedText();
            return new Size(ft.Width, ft.Height);
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            if (string.IsNullOrEmpty(_text)) return;

            var geometry = BuildFormattedText().BuildGeometry(new Point(0, 0));

            // 先描边（轮廓），再填充（字形），描边会被字形盖住内侧一半，
            // 只剩外侧一半成为均匀的「描边」。
            var pen = new Pen(_outline, _outlineThickness)
            {
                LineJoin = PenLineJoin.Round
            };
            dc.DrawGeometry(null, pen, geometry);
            dc.DrawGeometry(_foreground, null, geometry);
        }
    }
}
