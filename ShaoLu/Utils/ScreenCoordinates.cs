using System.Drawing;

namespace ShaoLu.Utils
{
    /// <summary>
    /// 屏幕相对坐标（横/纵坐标占屏幕宽/高的百分比）与屏幕逻辑像素之间的换算。
    /// 绝对坐标口径与现有实现一致：主屏逻辑像素尺寸（System.Windows.Forms.Screen.PrimaryScreen.Bounds）。
    /// 换算保持 double 原样返回、不做四舍五入，由调用方按既有风格取整。
    /// </summary>
    public static class ScreenCoordinates
    {
        /// <summary>主屏逻辑像素尺寸；获取失败或尺寸非法时返回空矩形（不抛异常）</summary>
        public static Rectangle ScreenBounds
        {
            get
            {
                try
                {
                    var bounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds ?? Rectangle.Empty;
                    return bounds.Width > 0 && bounds.Height > 0 ? bounds : Rectangle.Empty;
                }
                catch
                {
                    return Rectangle.Empty;
                }
            }
        }

        /// <summary>百分比 → 像素（逻辑像素）；屏幕尺寸为 0 或非法时返回 0，不抛异常</summary>
        public static double PercentToPixel(double percent, double screenSize)
        {
            if (!(screenSize > 0)) return 0;
            return percent * screenSize / 100.0;
        }

        /// <summary>像素（逻辑像素）→ 百分比；屏幕尺寸为 0 或非法时返回 0，不抛异常</summary>
        public static double PixelToPercent(double pixel, double screenSize)
        {
            if (!(screenSize > 0)) return 0;
            return pixel * 100.0 / screenSize;
        }

        /// <summary>横坐标：相对模式按百分比 × 主屏宽 ÷ 100 换算，绝对模式原样返回</summary>
        public static double ToPixelX(double value, bool relative)
            => relative ? PercentToPixel(value, ScreenBounds.Width) : value;

        /// <summary>纵坐标：相对模式按百分比 × 主屏高 ÷ 100 换算，绝对模式原样返回</summary>
        public static double ToPixelY(double value, bool relative)
            => relative ? PercentToPixel(value, ScreenBounds.Height) : value;

        /// <summary>宽度：相对模式按百分比 × 主屏宽 ÷ 100 换算，绝对模式原样返回</summary>
        public static double ToPixelWidth(double value, bool relative)
            => relative ? PercentToPixel(value, ScreenBounds.Width) : value;

        /// <summary>高度：相对模式按百分比 × 主屏高 ÷ 100 换算，绝对模式原样返回</summary>
        public static double ToPixelHeight(double value, bool relative)
            => relative ? PercentToPixel(value, ScreenBounds.Height) : value;

        /// <summary>横坐标像素 → 占主屏宽的百分比</summary>
        public static double ToPercentX(double pixel) => PixelToPercent(pixel, ScreenBounds.Width);

        /// <summary>纵坐标像素 → 占主屏高的百分比</summary>
        public static double ToPercentY(double pixel) => PixelToPercent(pixel, ScreenBounds.Height);

        /// <summary>宽度像素 → 占主屏宽的百分比</summary>
        public static double ToPercentWidth(double pixel) => PixelToPercent(pixel, ScreenBounds.Width);

        /// <summary>高度像素 → 占主屏高的百分比</summary>
        public static double ToPercentHeight(double pixel) => PixelToPercent(pixel, ScreenBounds.Height);

        /// <summary>横坐标单位切换（fromRelative/toRelative 相同则原样返回）</summary>
        public static double ConvertX(double value, bool fromRelative, bool toRelative)
            => fromRelative == toRelative ? value : (toRelative ? ToPercentX(value) : ToPixelX(value, true));

        /// <summary>纵坐标单位切换（fromRelative/toRelative 相同则原样返回）</summary>
        public static double ConvertY(double value, bool fromRelative, bool toRelative)
            => fromRelative == toRelative ? value : (toRelative ? ToPercentY(value) : ToPixelY(value, true));

        /// <summary>宽度单位切换（fromRelative/toRelative 相同则原样返回）</summary>
        public static double ConvertWidth(double value, bool fromRelative, bool toRelative)
            => fromRelative == toRelative ? value : (toRelative ? ToPercentWidth(value) : ToPixelWidth(value, true));

        /// <summary>高度单位切换（fromRelative/toRelative 相同则原样返回）</summary>
        public static double ConvertHeight(double value, bool fromRelative, bool toRelative)
            => fromRelative == toRelative ? value : (toRelative ? ToPercentHeight(value) : ToPixelHeight(value, true));
    }
}
