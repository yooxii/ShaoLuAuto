using ShaoLu.Services.Ocr;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace ShaoLu.Services
{
    /// <summary>
    /// OCR 门面：负责把屏幕区域截成位图、编码成 PNG，再交给 OCR 扩展识别。
    ///
    /// OCR 引擎本身（Tesseract 及其原生库、语言数据）以扩展形式单独发布，
    /// 见 <see cref="OcrExtensionHost"/>；扩展未安装时 <see cref="IsAvailable"/> 为 false，
    /// 调用 <see cref="Recognize"/> / <see cref="RecognizeRegion"/> 只会返回空字符串并记录一次日志，
    /// 不抛异常，界面据此禁用相关功能。
    /// </summary>
    public class OCRService
    {
        private static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        // 缓存 DPI 缩放因子，避免从后台线程访问 WPF 对象
        // 在应用启动时（UI 线程）设置一次，之后仅读取，无竞态风险
        private static double _cachedDpiX = 1.0;
        private static double _cachedDpiY = 1.0;
        private static bool _missingExtensionLogged;

        /// <summary>缓存的 X 方向 DPI 缩放因子</summary>
        public static double CachedDpiX => _cachedDpiX;
        /// <summary>缓存的 Y 方向 DPI 缩放因子</summary>
        public static double CachedDpiY => _cachedDpiY;

        /// <summary>OCR 功能是否可用（OCR 扩展已安装）</summary>
        public static bool IsAvailable => OcrExtensionHost.IsAvailable;

        /// <summary>OCR 不可用时的原因（可用时为空字符串）</summary>
        public static string UnavailableReason => OcrExtensionHost.UnavailableReason;

        /// <summary>OCR 扩展显示名/版本（不可用时为空字符串）</summary>
        public static string EngineDescription => OcrExtensionHost.Description;

        /// <summary>
        /// 在 UI 线程上更新 DPI 缓存（应在主窗口加载后调用）
        /// </summary>
        public static void UpdateDpi()
        {
            try
            {
                var mainWindow = System.Windows.Application.Current?.MainWindow;
                if (mainWindow != null)
                {
                    var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(mainWindow);
                    _cachedDpiX = dpi.DpiScaleX;
                    _cachedDpiY = dpi.DpiScaleY;
                    logger.Info("DPI cache updated: X={0}, Y={1}", _cachedDpiX, _cachedDpiY);
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Failed to update DPI cache, using default 1.0");
            }
        }

        /// <summary>
        /// 对 Bitmap 执行 OCR 识别，返回识别文本；OCR 扩展不可用或识别失败时返回空字符串
        /// </summary>
        /// <param name="bmp">要识别的图像</param>
        public static string Recognize(Bitmap bmp)
        {
            if (bmp == null)
                return string.Empty;

            if (!OcrExtensionHost.IsAvailable)
            {
                LogMissingExtensionOnce();
                return string.Empty;
            }

            try
            {
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    return OcrExtensionHost.RecognizePng(ms.ToArray()) ?? string.Empty;
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "OCR recognition failed");
                return string.Empty;
            }
        }

        /// <summary>
        /// 截取屏幕指定区域并执行 OCR
        /// </summary>
        /// <param name="region">屏幕区域（WPF 逻辑像素坐标）</param>
        /// <returns>识别到的文本；OCR 扩展不可用时返回空字符串</returns>
        public static string RecognizeRegion(System.Windows.Rect region)
        {
            if (region.IsEmpty || region.Width <= 0 || region.Height <= 0)
                return string.Empty;

            if (!OcrExtensionHost.IsAvailable)
            {
                LogMissingExtensionOnce();
                return string.Empty;
            }

            try
            {
                // 使用缓存的 DPI 缩放因子，将逻辑像素转换为物理像素
                double dpiScaleX = _cachedDpiX;
                double dpiScaleY = _cachedDpiY;

                int x = (int)(region.X * dpiScaleX);
                int y = (int)(region.Y * dpiScaleY);
                int width = Math.Max(1, (int)(region.Width * dpiScaleX));
                int height = Math.Max(1, (int)(region.Height * dpiScaleY));

                using (Bitmap bmp = new Bitmap(width, height))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.CopyFromScreen(x, y, 0, 0, new Size(width, height));
                    }
                    return Recognize(bmp);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Screen region OCR failed for region {0}", region);
                return string.Empty;
            }
        }

        /// <summary>释放 OCR 引擎资源</summary>
        public static void Dispose()
        {
            OcrExtensionHost.Shutdown();
        }

        private static void LogMissingExtensionOnce()
        {
            if (_missingExtensionLogged) return;
            _missingExtensionLogged = true;
            logger.Warn("OCR is not available: {0}", OcrExtensionHost.UnavailableReason);
        }
    }
}
