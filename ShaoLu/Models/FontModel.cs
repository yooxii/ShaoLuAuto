using System.Windows;
using System.Windows.Media;

namespace ShaoLu.Models
{
    public class FontModel
    {
        public System.Drawing.Font Font => !string.IsNullOrEmpty(FontFamily) ? new(FontFamily, FontSize, Style, Unit) : null;
        public float FontSize { get; set; }
        public string FontFamily { get; set; }
        public FontWeight FontWeight { get; set; }
        public FontStyle FontStyle { get; set; }
        public System.Drawing.FontStyle Style { get; set; }
        public System.Drawing.GraphicsUnit Unit { get; set; }
        public int FontColor { get; set; }
        public string FontBackgroundColor { get; set; }
        public string FontBorderColor { get; set; }
        public string FontBorderWidth { get; set; }
    }
}
